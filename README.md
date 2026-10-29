# PrecisionCurve

**Aceleração de mouse em modo kernel, estágios de DPI por software e snap de eixos — para Windows 10/11 x64.**

PrecisionCurve é um *mouse class filter driver* (KMDF) que intercepta pacotes de movimento
brutos antes das pointer ballistics do Windows, mais um aplicativo de bandeja (WPF) que
desenha a curva em tempo real e um serviço opcional que troca de perfil sem a GUI aberta.

```
        +---------------------+        IOCTL (METHOD_BUFFERED)        +----------------------+
        |  PrecisionCurve.exe | <------------------------------------> |  PrecisionCurve.sys  |
        |  WPF + tray + LUT   |                                        |  (KMDF upper filter) |
        +---------------------+                                        +----------+-----------+
                                                                                | MOUSE_INPUT_DATA
                                                                                v
   HID device  ->  mouhid / mouclass (FDO)  ->  [ PrecisionCurve filter ]  ->  win32k
                                                   aplica ganho, DPI stage,
                                                   snap, clamp
```

---

## O que está pronto

| Requisito | Onde está | Estado |
|---|---|---|
| Filter driver KMDF na pilha do mouse | `driver/Driver.c`, `driver/Intercept.c` | implementado |
| Interceptação dos pacotes brutos antes da ballistics | `PcMouseServiceCallback` (hook de `IOCTL_INTERNAL_MOUSE_CONNECT`) | implementado |
| Curvas Linear / Natural / Motivity / Jump / Classic | `shared/precisioncurve_curves.h`, `app/Core/CurveMath.cs` | implementado |
| Tabela de ganho compilada (kernel só interpola inteiros) | `PcBuildLut` → `PcLutSample` | implementado |
| DPI Stages por software (multiplicação de vetor, por eixo) | `PC_SET_CONFIG.StageScaleXQ16/YQ16` | implementado |
| Separação de eixos X/Y | escalas e ganhos independentes por eixo | implementado |
| Snap de ângulo | `PcSnapVectorQ16`, até 8 eixos unitários | implementado |
| Leitura de descriptors / polling rate | `app/Core/RawInputMonitor.cs` (taxa **medida** via Raw Input) | implementado (ver limitações) |
| Alterar polling rate | — | **impossível por software** (ver `docs/REALITY_CHECK.md`) |
| Polling smoother / frame pacing | flag `PC_FLAG_DT_SMOOTHING` no driver | implementado |
| GUI moderna com gráfico em tempo real | `app/MainWindow.xaml`, `app/Controls/CurveGraph.cs` | implementado |
| Sliders Base / Offset / Accel / Cap / MaxSpeed | `MainWindow.xaml`, `MainViewModel` | implementado |
| Perfis automáticos por processo | `ActiveProcessWatcher` + `ProfileStore.MatchProcess` | implementado |
| Bandeja, sem janela na inicialização | `app/App.xaml.cs` | implementado |
| Hotkeys globais | `app/Core/HotkeyManager.cs` (`RegisterHotKey`) | implementado |
| Serviço do Windows | `service/PrecisionCurve.Service` | implementado |
| Assinatura / instalação | `scripts/sign-driver.ps1`, `scripts/install-driver.ps1`, `docs/BUILD_AND_SIGN.md` | implementado |
| CLI de controle | `tools/pcctl` (C++/Win32) | implementado |

Também existem: um cliente C++ independente (`pcctl`), um serviço sem dependências
(`StartServiceCtrlDispatcherW` na mão), e uma suíte de testes que roda **o código real do
driver** em user mode.

---

## Estrutura de pastas

```
PrecisionCurve/
├── driver/                     # o driver (C, WDK/KMDF)
│   ├── PrecisionCurve.h        # contexto, seqlock, protótipos, regras do hot path
│   ├── Driver.c                # DriverEntry, filter device, control device, unload
│   ├── Intercept.c             # CONNECT hook + o caminho quente (por pacote)
│   ├── Config.c                # validação, publicação atômica, IOCTL, stats
│   ├── PrecisionCurve.inf      # INF de upper filter (IDs de hardware a editar)
│   └── PrecisionCurve.vcxproj  # projeto VS/WDK
├── shared/                     # contrato único entre kernel e user mode
│   ├── precisioncurve_ioctl.h  # ABI: structs, IOCTLs, flags, limites
│   ├── precisioncurve_math.h   # fixed point sem libm (usável em DISPATCH_LEVEL)
│   └── precisioncurve_curves.h # fórmulas em double + compilador de LUT (só user mode)
├── app/                        # GUI C# / WPF (.NET 8)
│   ├── Core/                   # ABI, cliente IOCTL, matemática, perfis, hotkeys, raw input
│   ├── Controls/CurveGraph.cs  # plotagem da curva (render próprio)
│   ├── ViewModels/             # estado da UI e comandos
│   ├── Resources/Theme.xaml    # tema escuro
│   └── MainWindow.xaml(.cs)
├── service/PrecisionCurve.Service/  # host de serviço, sem NuGet
├── tools/pcctl/                # cliente C++ de linha de comando
├── tests/                      # testes que rodam sem Windows/WDK
│   ├── test_math.c             # isqrt, LUT, arredondamento, snap
│   ├── parity/                 # vetores gerados em C vs implementação JS
│   ├── host/                   # shim do WDK + simulação do driver + benchmark
│   └── tools/                  # validadores de XAML e de sintaxe C#
├── scripts/                    # build, assinatura, instalação, check-host.sh
└── docs/                       # arquitetura, build/assinatura, realidade do hardware
```

---

## Construir

### Driver (Windows + WDK)

```powershell
# Visual Studio 2022 com o workload "Windows Driver Development"
msbuild driver\PrecisionCurve.vcxproj /p:Configuration=Release /p:Platform=x64
# ou
.\scripts\build.ps1
```

### Aplicativo e serviço

```powershell
dotnet build app\PrecisionCurve.App.csproj -c Release
dotnet build service\PrecisionCurve.Service\PrecisionCurve.Service.csproj -c Release
```

### CLI (C++)

```powershell
cmake -S tools\pcctl -B build\pcctl -A x64
cmake --build build\pcctl --config Release
```

### Assinar e instalar

```powershell
.\scripts\sign-driver.ps1 -TestSign          # dev: certificado próprio + testsigning
.\scripts\install-driver.ps1                 # copia .sys, cria serviço, anexa UpperFilters
.\scripts\install-driver.ps1 -UseInf -InfPath .\driver\PrecisionCurve.inf   # via pnputil
```

Passo a passo completo, incluindo certificado EV, attestation signing e **como se recuperar
de um filter driver defeituoso**: [`docs/BUILD_AND_SIGN.md`](docs/BUILD_AND_SIGN.md).
Detalhes de projeto (por que class filter, modelo de concorrência, IRQL, ABI, testes):
[`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md).

---

## Testar (não precisa de Windows, WDK nem certificado)

```bash
./scripts/check-host.sh          # tudo: testes + benchmark + validadores
./scripts/check-host.sh --quick  # sem o benchmark
make -C tests                    # só a suíte
make -C tests bench              # só o benchmark
```

O que roda:

| Suíte | O que prova | Resultado atual |
|---|---|---|
| `tests/test_math.c` | `isqrt` vs `sqrt`, propagação de resíduo, LUT monotônica, snap, entradas malformadas | OK |
| `tests/parity` | C ↔ JS bit a bit em 2.506 pontos; tabelas de 64 e 256 knots idênticas | OK (erro máximo 0.00e+0) |
| `tests/host` | **o código real do driver** compilado contra um shim do WDK e executado: 114 checagens | OK |
| `make bench` | custo por pacote | **215–240 ns/pacote** neste host, ~1,9 µs por lote de 8, ou **≈0,2% de um núcleo a 8 kHz** (varia com a máquina) |
| `tests/tools/check_xaml.py` | XAML bem formado, todo `StaticResource` existe, todo `Binding` resolve para uma propriedade real, todo `Commands.*` existe | OK (59 bindings) |
| `tests/tools/check_csharp.py` | sintaxe dos 16 arquivos C# via tree-sitter (o que dá para checar sem `dotnet`) | OK |

Tudo isso está em `./scripts/check-host.sh` (e `--quick` para pular o benchmark).

Os testes de driver cobrem, entre outros: pass-through exato quando desligado, ganho 1,5×
com resíduo sem deriva (200 passos de 1 count = 300), 2× aplicado, packet absoluto ignorado,
clamp por pacote, snap dentro/fora da tolerância, troca de estágio, IOCTLs malformados
recusados sem tocar na config viva, e o caminho "callback nunca conectado" (que não pode
causar NULL deref).

Para provar que os testes têm dentes, o harness foi rodado contra mutações do driver
(remover o clamp, zerar o resíduo, ignorar o flag de enable) — todas foram detectadas.

Os dois scripts acima dependem de Python 3 (e, no caso do C#, de
`pip install tree_sitter tree_sitter_c_sharp`); eles **não** fazem parte de `make test`
porque nem toda máquina de build tem essas ferramentas. São um smoke test, não um
compilador: o type check de verdade continua sendo `dotnet build`.

No Windows, o `CurveMath` se auto-verifica contra o mesmo arquivo de vetores:

```powershell
PrecisionCurve.exe --selftest --quiet   # gera selftest.log, retorna código 0/1
```

---

## Por que não causa BSOD (e onde o risco real está)

O caminho quente do driver foi escrito sob regras explícitas:

1. **Nenhuma alocação** depois de `DriverEntry`. Se a alocação inicial falhar, o driver
   recusa carregar — falha limpa em vez de ponteiro nulo depois.
2. **Nenhum lock bloqueante**: a config é publicada por troca de slot (`InterlockedExchange`)
   e lida por seqlock com **3 tentativas**; se as três falharem, cai para ganho 1,0 (unity)
   em vez de aplicar tabela rasgada.
3. **Nenhum ponteiro vindo de user mode** é dereferenciado: tudo passa por
   `WdfRequestRetrieveInputBuffer` + validação campo a campo, duas vezes (IOCTL e publicação).
4. **Callback nulo**: se o mouclass ainda não conectou (ou está sendo removido), o pacote é
   descartado e contabilizado — nunca chamado.
5. **Sem ponto flutuante no kernel**: o driver só interpola uma tabela inteira. `isqrt` é
   implementado sem libm e sem divisão no laço.
6. **Overflow**: todo produto é feito em 64 bits com saturação explícita; vetores são
   clampados antes do snap (que calcula quadrados).
7. **FPU/IRQL**: nada em código paginável no hot path; `PAGED_CODE()` aparece só onde o KMDF
   garante PASSIVE_LEVEL.

O que **não** é testável fora de uma máquina Windows: IRQL real, concorrência real, PnP e
energia. Para isso: VM + Driver Verifier (`verifier /standard /driver PrecisionCurve.sys`)
e teste de suspend/resume, hot-plug de mouse e desinstalação com o mouse em uso.

---

## Limitações honestas

Resumo (detalhes e o "porquê" em [`docs/REALITY_CHECK.md`](docs/REALITY_CHECK.md)):

- **DPI físico não muda.** O que existe aqui é multiplicação de vetor (DPI efetivo por
  software). O sensor continua contando o que sempre contou.
- **Polling rate não é alterável por software.** O `bInterval` está no endpoint descriptor
  USB, que o driver de classe HID não expõe; mudar exige firmware ou SDK do fabricante.
  O app **mede** a taxa observada e o driver **estabiliza** a estimativa de tempo
  (frame pacing) — não faz buffering de relatórios.
- **Aceleração do Windows**: a interceptação acontece antes das ballistics, mas o Windows
  ainda aplica a transformação dele depois. Para comparar com a curva desenhada, desligue
  "Aperfeiçoar precisão do ponteiro".
- **Anti-cheat**: driver de kernel que altera input é exatamente o tipo de coisa que
  anti-cheats sinalizam. Use por sua conta e risco em jogos competitivos.

---

## Decisões de projeto

- **Filter de classe, não filter de HID.** O hook fica no callback que `mouclass` registra
  no port driver: o delta bruto está disponível, não há envolvimento da pilha USB, e o
  mouse continua funcionando se o filtro falhar ao carregar.
- **A matemática vive no user mode.** O kernel recebe uma tabela pronta de 1024 knots e
  interpola linearmente. Mudar uma curva é recompilar uma tabela, não reescrever código de
  kernel — e o mesmo código roda na GUI, no CLI e no serviço.
- **ABI explícita e validada.** `StructSize` + `AbiVersion` em toda struct, checagem de
  layout no startup da GUI (`Abi.SelfCheck`) e códigos IOCTL conferidos contra `CTL_CODE`.
- **Duas fontes de "ligado"**: o flag do perfil e o master switch (hotkey). O driver exige
  os dois — um perfil marcado como desligado nunca acelera, mesmo com o master ligado.
- **Opt-in absoluto**: o driver carrega desligado e com tabela unity. Nada muda até alguém
  enviar uma configuração.

---

## Roadmap (o que eu faria em seguida)

1. Configuração por dispositivo (hoje a config é global, compartilhada entre todos os mouses).
2. Smoothing real de relatórios no kernel (timer de alta resolução) — hoje só há
   estabilização da estimativa de `dt`, que não adiciona latência.
3. Contador de taxa por dispositivo, lendo `RIDI_DEVICEINFO` por handle.
4. Assinatura via Azure Trusted Signing no CI e instalação por MSI.
5. Testes de concorrência (publicar config enquanto o callback roda) com um modelo de
   *thread* no harness.

---

## Ferramentas extras

- **[`blueboost/`](blueboost/README.md)** — BlueBoost: otimizador independente para PC
  fraco com foco em configurar o BlueStacks 5 (presets no `bluestacks.conf` com backup
  automático, tweaks reversíveis do Windows e tutorial embutido). PowerShell + WinForms,
  zero dependências — basta clicar em `BlueBoost.bat`.

## Aviso

Projeto educacional/de engenharia. Carregar drivers não assinados exige desativar proteções
do Windows; fazer isso em máquina de terceiros é uma péssima ideia. Nenhuma licença foi
definida neste repositório — escolha uma antes de distribuir.
