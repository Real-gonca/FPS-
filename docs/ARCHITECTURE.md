# Arquitetura

## 1. Visão de camadas

```
+---------------------------------------------------------------+
| app/  (WPF, .NET 8)                                           |
|   ProfileStore ── CurveMath ── ConfigBuilder ── DriverClient   |
|   HotkeyManager  RawInputMonitor  ActiveProcessWatcher        |
+-----------------------------+---------------------------------+
                              | DeviceIoControl (METHOD_BUFFERED)
                              v
+---------------------------------------------------------------+
| shared/precisioncurve_ioctl.h   (a única ABI)                 |
|   PC_SET_CONFIG, PC_DRIVER_STATS, PC_VERSION_INFO, flags      |
+-----------------------------+---------------------------------+
                              v
+---------------------------------------------------------------+
| driver/  (KMDF)                                               |
|   Driver.c    filtro + control device + unload                |
|   Config.c    validação, seqlock, IOCTL, stats                |
|   Intercept.c hook de CONNECT + hot path por pacote           |
+-----------------------------+---------------------------------+
                              | MOUSE_INPUT_DATA (in place)
                              v
                         mouclass / win32k
```

Nada no kernel conhece o conceito de "perfil" ou "curva". O kernel conhece **uma tabela de
ganho** e **um punhado de escalas**. Toda a semântica (curva, DPI stage, snap) é compilada
no user mode por `ConfigBuilder`.

## 2. Por que um class filter

Três pontos de interceptação são possíveis:

| Abordagem | Vantagens | Problemas |
|---|---|---|
| HID filter (abaixo de `mouhid`) | vê o relatório cru, inclusive antes do parsing | precisa reimplementar parsing de relatório; muda a pilha USB; reenumeração; mais risco |
| **Class filter (acima de `mouclass`)** ← usado | o driver de classe entrega `MOUSE_INPUT_DATA` já decodificado; delta bruto, sem ballistics | não vê o relatório HID; não muda polling |
| Hook em `win32k` / `SetWindowsHookEx` | — | precisa de patch de kernel ou de um hook de user mode que perde timing; anti-cheat detecta |

O mecanismo do class filter é o documentado pela Microsoft no sample `moufiltr`: o driver de
classe envia `IOCTL_INTERNAL_MOUSE_CONNECT` com um `CONNECT_DATA { ClassDeviceObject,
ClassService }`. O filtro guarda o par original e devolve o *seu* callback no buffer antes de
repassar o IRP. A partir daí o port driver chama o filtro para cada lote de pacotes, em
`DISPATCH_LEVEL`, e o filtro chama o mouclass no final.

```
mouclass            PrecisionCurve              port driver (i8042/HID)
   |  CONNECT {obj, svc}  |                              |
   |--------------------->|                              |
   |                      |  CONNECT {obj, OUR svc}      |
   |                      |----------------------------->|
   |                      |                              |
   |                      |<---- packets (DISPATCH) -----|
   |<---- packets --------|                              |
```

Consequências de projeto:

- o filtro **não** pode falhar em aberto: se ele não conectou, ele repassa e conta.
- ele **precisa** repassar sempre, mesmo quando descarta (senão o cursor trava).
- ele roda em `DISPATCH_LEVEL`, então tudo que ele toca tem de ser non-paged.

## 3. Um pacote, passo a passo

`PcMouseServiceCallback` (driver/Intercept.c):

1. Resolve o contexto do dispositivo (`WdfWdmDeviceGetWdfDeviceHandle` → contexto WDF).
2. Lê `ClassService` / `ClassDeviceObject` **uma vez** para locais. Se algum for `NULL`,
   conta em `PacketsSuppressed` e retorna (sem crash).
3. Se o master switch ou o flag do perfil estão desligados: incrementa contadores e repassa.
4. `PcReadHotConfig`: seqlock, até 3 tentativas, snapshot dos escalares + ponteiro para a LUT.
5. Por pacote:
   - ignora `MOUSE_MOVE_ABSOLUTE` (tablet/touch/RDP),
   - `dt = KeQueryInterruptTime()` − último timestamp, clampado em [20 µs, 10 ms],
   - opcionalmente substitui `dt` pela EMA quando o relatório está >25% fora da média,
   - `len = PcVecLenQ16FromRaw(dx, dy)` (isqrt, sem libm),
   - `speed = len × 10000 / dt` em Q16 (counts/ms),
   - `gain = PcLutSample(...)`, clampado pelos limites da config,
   - `gainX = gain × stageX`, `gainY = gain × stageY` (DPI stage + eixos separados),
   - aplica ganho em 64 bits, clamp do vetor em ±`MaxDeltaPerPacket`,
   - snap opcional para o eixo mais próximo dentro da tolerância,
   - arredonda e guarda o resíduo sub-count por eixo (movimento lento não é truncado),
   - escreve `LastX/LastY` **somente se mudou**.
6. Chama o callback original com o mesmo par (início, fim, consumidos).

Custo medido (harness host, `make bench`): **215–240 ns por pacote**, ~1,9 µs por lote de 8.
A 8 kHz isso é cerca de 0,2% de um núcleo. O valor inclui a camada de shim; num driver real é menor.

## 4. Configuração: dois slots + seqlock

```
Slots[2] { Seq, Flags, Lut[1024], StageScaleX/Y[8], SnapAxis[8], ... }
ActiveSlot: 0 | 1                 (InterlockedExchange)
```

- **Escritor** (fila sequencial de IOCTL, PASSIVE_LEVEL): preenche o slot **inativo**, valida
  e clampa cada campo, calcula `LutScale` (reciprocal da velocidade máxima), incrementa
  `Seq` (ímpar → preenche → par), faz `InterlockedExchange(ActiveSlot)` e
  `InterlockedIncrement(Generation)`.
- **Leitor** (hot path, qualquer IRQL): lê `ActiveSlot`, copia os campos com `Seq` antes e
  depois, e só aceita se `Seq` não mudou e era par.

Por que seqlock se o escritor nunca toca o slot ativo? Porque o slot ativo de hoje é o slot
inativo de amanhã: se um callback guardou um ponteiro para a LUT e o escritor começar a
reescrever aquele slot, o `Seq` ímpar impede que a leitura seja aceita. O custo é três
branches previsíveis; o benefício é nunca aplicar um ganho vindo de uma tabela pela metade.

Trocar de estágio de DPI copia o slot ativo para o inativo e troca — não há caminho que
reescreva o slot ativo.

## 5. IRQL, locks e memória

| Caminho | IRQL | Pode paginar? | Lock |
|---|---|---|---|
| `PcMouseServiceCallback` | DISPATCH | **não** | nenhum (seqlock lock-free) |
| `PcEvtIoInternalDeviceControl` | PASSIVE | sim (`PAGED_CODE`) | nenhum (um escritor) |
| `PcEvtIoDeviceControl` | PASSIVE | sim | nenhum |
| `PcEvtDeviceCleanup` | ≤ DISPATCH | **não** | `InterlockedExchangePointer` |
| `PcEvtDeviceAdd` / `DriverEntry` | PASSIVE | sim | — |

Memória:

- `PC_RUNTIME` (2 slots de ~4,3 KB + contadores) é alocado **uma vez** em `DriverEntry` com
  `ExAllocatePool2(POOL_FLAG_NON_PAGED)` (ou `ExAllocatePoolWithTag` em alvos antigos).
- Falha de alocação → `STATUS_INSUFFICIENT_RESOURCES` → o driver não carrega. Sem estado
  parcial.
- Nenhuma IRP é pendurada: todo IOCTL completa na hora (`WdfRequestCompleteWithInformation`).
- Nenhum buffer de user mode é mapeado ou guardado: `METHOD_BUFFERED` copia para o system
  buffer, que é non-paged e morre com o request.

## 6. Modelo de segurança

- O control device usa SDDL `..._WORLD_RW_RES_R` por padrão: o usuário interativo pode
  configurar seu próprio mouse sem UAC; qualquer outro acesso é negado.
- `PC_RESTRICT_TO_ADMIN` (em `PrecisionCurve.h`) troca para `..._WORLD_R_RES_R`, exigindo
  administrador para escrever a config.
- IOCTLs de escrita pedem `FILE_WRITE_DATA`; leitura pede `FILE_READ_DATA`.
- Toda validação é feita nos dois lados. Exemplos de coisas recusadas com
  `STATUS_INVALID_PARAMETER`: `LutCount` fora de 2..1024, ganho acima de 64×, eixo cujo
  comprimento não é ~1,0, `StructSize` errado, ABI futura, bits de flag desconhecidos,
  lixo depois de `LutCount`.
- O app roda `asInvoker` (manifest). Instalar o driver exige elevação — como deve ser.

## 7. ABI e versionamento

- `PC_ABI_VERSION` é comparado em **toda** struct; versão diferente → `STATUS_REVISION_MISMATCH`.
- `StructSize` é comparado com `sizeof` do kernel: struct maior ou menor é rejeitada antes de
  qualquer leitura.
- O lado C# verifica o layout no startup (`Abi.SelfCheck`): se `Marshal.SizeOf<PC_SET_CONFIG>()`
  não for 4276 bytes, o app se recusa a falar com o driver em vez de enviar lixo.
- `PC_VERSION_INFO.Features` permite que a GUI degrade graciosamente contra um driver antigo.

## 8. Testes

```
tests/
├── test_math.c        unidade da matemática compartilhada (host, -lm)
├── parity/            C ↔ JS, vetores dourados compartilhados com o C#
├── host/include/      shim do WDK (ntddk.h, wdf.h, kbdmou.h, ...)
├── host/shim.c        objetos WDF falsos + relógio controlável + Interlocked
├── host/test_driver.c 114 checagens sobre o driver real compilado com o shim
└── host/bench_driver.c benchmark do hot path
```

O shim existe porque a maior parte dos bugs de driver é **lógica** (validação, estado,
aritmética), não syscalls. Rodar o driver em user mode pega isso em segundos, sem VM. O que
ele não pega — IRQL, concorrência real, PnP — está listado como trabalho de VM.

## 9. Limitações de projeto (conscientes)

1. **Config global**, não por dispositivo. `PC_SET_CONFIG` não carrega `UnitId`/handle.
2. **Sem smoothing de relatórios** de verdade: só estabilização de `dt`. Buffering de
   relatórios adicionaria latência exatamente no que o usuário sente.
3. **Snap limitado a 8 eixos** pelo tamanho da ABI (cobre 2 e 4 linhas).
4. **Tabela de 1024 knots**: interpolação linear; knots demais custaria 4 KB por slot e
   nenhum ganho perceptível.
5. **Sem WHQL**: assinatura de produção depende do Partner Center (ver BUILD_AND_SIGN).
