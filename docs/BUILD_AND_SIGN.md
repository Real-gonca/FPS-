# Construir, assinar e instalar

## 0. A parte sem graça, dita de frente

Windows 10/11 x64 **não carrega** um driver de kernel assinado apenas por um certificado
comum. Desde o 1607:

1. o binário precisa estar assinado com SHA-256;
2. a assinatura precisa encadear até uma âncora confiável da máquina;
3. **e** para distribuição ao público, o `.sys` precisa do *countersignature* da Microsoft
   (attestation signing ou WHQL, via Partner Center).

Em desenvolvimento existem dois caminhos: **test signing** (grátis, exige desligar Secure
Boot e reiniciar) ou uma **VM com debug** + `bcdedit /debug on`. Não existe atalho
"desativa a checagem só para este driver".

---

## 1. Pré-requisitos

| Componente | Versão | Observação |
|---|---|---|
| Visual Studio 2022 | 17.8+ | workload *Windows Driver Development* |
| Windows SDK | 10.0.22621+ | vem com o workload |
| WDK | 10.0.22621+ | idem |
| .NET SDK | 8.0 | app, serviço e testes |
| CMake | 3.20+ | só para `pcctl` |
| Máquina de teste | Win10 22H2 / Win11 | **VM com snapshot** é o certo |

---

## 2. Build

```powershell
# tudo
.\scripts\build.ps1

# separado
msbuild driver\PrecisionCurve.vcxproj /p:Configuration=Release /p:Platform=x64
dotnet build app\PrecisionCurve.App.csproj -c Release
dotnet build service\PrecisionCurve.Service\PrecisionCurve.Service.csproj -c Release
cmake -S tools\pcctl -B build\pcctl -A x64 && cmake --build build\pcctl --config Release
```

Saídas:
- `bin\x64\Release\PrecisionCurve\PrecisionCurve.sys` (+ `.inf`, `.cat`)
- `app\bin\Release\net8.0-windows\PrecisionCurve.exe`
- `service\PrecisionCurve.Service\bin\Release\net8.0-windows\PrecisionCurve.Service.exe`

Antes de qualquer coisa, rode os testes de host (eles não precisam de Windows):

```bash
make -C tests
```

---

## 3. Assinatura de desenvolvimento (test signing)

```powershell
# 1. assina com um certificado autoassinado e liga o test signing
.\scripts\sign-driver.ps1 -TestSign

# 2. reinicie
Restart-Computer
```

O script:

1. cria `CN=PrecisionCurve Development` em `Cert:\LocalMachine\My`;
2. exporta e importa o `.cer` em `Trusted Root` e `TrustedPublisher`;
3. assina o `.sys` com `signtool sign /fd sha256 /tr <timestamp> /td sha256`;
4. `bcdedit /set testsigning on`.

Depois do reboot, a área de trabalho mostra **Test Mode** no canto. Isso é esperado.

> Secure Boot **deve** estar desligado para o test signing valer. Em VM (Hyper-V/VMware) é
> trivial; em hardware real, desligar Secure Boot tem consequências — use uma máquina de
> teste.

---

## 4. Instalação

### 4.1 Anexar a todos os mouses atuais (recomendado para dev)

```powershell
# PowerShell como Administrador
.\scripts\install-driver.ps1
```

O script:

1. copia o `.sys` para `%SystemRoot%\System32\drivers\`;
2. `sc.exe create PrecisionCurve type= kernel start= demand`;
3. para cada mouse HID presente, adiciona `"PrecisionCurve"` ao valor `UpperFilters`
   (REG_MULTI_SZ) em
   `HKLM\SYSTEM\CurrentControlSet\Enum\<InstanceID>\Device Parameters`.

Reinicie (ou desconecte/reconecte o mouse) para o filtro entrar na pilha.

### 4.2 via INF + pnputil (recomendado para pacote assinado)

Edite `driver\PrecisionCurve.inf` e troque os IDs de exemplo pelos seus
(Device Manager → mouse → Propriedades → Detalhes → *Hardware Ids*), depois:

```powershell
pnputil /add-driver driver\PrecisionCurve.inf /install
```

O INF usa `Include=msmouse.inf` + `Needs=HID_Mouse_Inst.NT`, então o mouse mantém o driver
de função real e o nosso entra como *upper filter*.

### 4.3 Verificar

```powershell
sc.exe query PrecisionCurve        # STATE: RUNNING (após o reboot)
pcctl info                         # versão, ABI, features
pcctl stats                        # contadores
Get-PnpDevice -Class Mouse | Format-List InstanceId, Status
```

No app: o indicador no canto superior direito fica verde com "Connected · v1.0".

---

## 5. Assinatura de produção

1. **Certificado EV code signing** (token/HSM ou Azure Trusted Signing).
2. Assine o `.sys` e o `.cat` com SHA-256 + timestamp:
   ```powershell
   signtool sign /fd sha256 /tr http://timestamp.digicert.com /td sha256 /a PrecisionCurve.sys
   signtool sign /fd sha256 /tr http://timestamp.digicert.com /td sha256 /a PrecisionCurve.cat
   ```
3. **Envie ao Partner Center** (Hardware Developer) para *attestation signing* (mais rápido,
   exige EV + conta verificada) ou WHQL (testes HLK completos).
4. Distribua o pacote assinado pela Microsoft (`pnputil /add-driver ... /install` ou MSI).

Sem o passo 3, o driver não carrega em máquinas de clientes, ponto. O texto de erro típico é
**"Windows cannot verify the digital signature"** ou **Code 52**.

---

## 6. Rodar em VM com debug (o ambiente certo para desenvolver)

```powershell
# host
bcdedit /debug on
bcdedit /dbgsettings net hostip:192.168.1.10 port:50000 key:1.2.3.4

# guest (WinDbg)
windbg -k net:port=50000,key=1.2.3.4
```

Símbolos: configure `_NT_SYMBOL_PATH=srv*C:\symbols*https://msdl.microsoft.com/download/symbols`
e adicione `bin\x64\Release` ao caminho de símbolos para ver seu próprio código.

Comandos úteis no hot path:

```
!irql                     # confirma DISPATCH
!stacks 2 PrecisionCurve
ba w4 &g_PcRuntime->ActiveSlot     # breakpoint de hardware na publicação
!poolfind PrecisionCurve
```

Driver Verifier (obrigatório antes de dizer "pronto"):

```powershell
verifier /standard /driver PrecisionCurve.sys
# depois: suspend/resume, hot-plug do mouse, desinstalar com o mouse em uso, 8h de uso
verifier /query
```

---

## 7. Desinstalar

```powershell
.\scripts\install-driver.ps1 -Uninstall
```

O script remove o `UpperFilters` de cada mouse, para e apaga o serviço e apaga o `.sys`.
**Reinicie**: um filter driver continua carregado enquanto qualquer mouse estiver usando a
pilha.

---

## 8. Quando dá errado (e vai dar)

| Sintoma | Causa provável | O que fazer |
|---|---|---|
| Device Manager: **Code 52** | assinatura não confiável | test signing off, ou certificado fora dos stores |
| **Code 39 / 31** | driver não carregou | `%SystemRoot%\inf\setupapi.dev.log`; `sc.exe start PrecisionCurve` |
| **Code 10** | falha em `DriverEntry` | WinDbg; normalmente é alocação ou `WdfDriverCreate` |
| `pcctl info` → "device not found" | filtro não anexado | confira `UpperFilters`, reinicie |
| **Mouse parado depois do reboot** | filtro com bug | ver recuperação abaixo |
| Tela azul | hot path | analise o dump com `!analyze -v`; o bugcheck dirá o offset |

### Recuperação de um filter driver defeituoso

Se o mouse não responde:

1. **Teclado**: `Win + Ctrl + Shift + B` reinicia o driver de vídeo (não resolve isto).
2. Use o teclado para abrir PowerShell como admin:
   `Win`, digite `powershell`, `Ctrl+Shift+Enter`.
3. Remova o filtro:
   ```powershell
   sc.exe stop PrecisionCurve
   sc.exe delete PrecisionCurve
   # e remova "PrecisionCurve" de UpperFilters dos mouses, ou rode -Uninstall
   ```
4. Sem mouse nenhum: **Modo de Segurança** (na tela de boot: `Shift` + Reiniciar →
   Solução de problemas → Opções avançadas → Configurações de inicialização).
   O filtro não carrega em safe mode.
5. Se o Windows não bootar: desabilite o driver pelo prompt de recuperação:
   ```
   dism /image:C:\ /disable-driver /driver:PrecisionCurve.sys
   ```

Guarde sempre um snapshot da VM antes de mexer na pilha de input.

---

## 9. Checklist antes de chamar de "pronto"

- [ ] `make -C tests` verde (matemática, paridade, simulação do driver)
- [ ] `PrecisionCurve.exe --selftest --quiet` retorna 0
- [ ] Driver Verifier ligado, 8h de uso normal, sem bugcheck
- [ ] Suspend/resume com o mouse conectado
- [ ] Hot-plug: plugar/desplugar um segundo mouse
- [ ] Desinstalar com o mouse em uso → reboot → mouse normal
- [ ] `verifier /query` sem violações
- [ ] App: iniciar sem janela, bandeja funcionando, hotkeys, auto-switch
