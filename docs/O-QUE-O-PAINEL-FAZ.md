# O que o painel faz, valor por valor

Referência técnica do `painel/FPS-Painel.bat`. Tudo aqui é reversível: a
opção **[2]** exporta as chaves antes de qualquer alteração e a **[13]**
reimporta.

---

## Chaves tocadas

### Mouse — `HKCU\Control Panel\Mouse` (tipo `REG_SZ`)

| Valor | Aplicado | Padrão do Windows | Efeito |
|---|---|---|---|
| `MouseSpeed` | `0` | `1` | Desliga "Aprimorar precisão do ponteiro" |
| `MouseThreshold1` | `0` | `6` | idem |
| `MouseThreshold2` | `0` | `10` | idem |
| `MouseSensitivity` | `10` | `10` | Ponto 1:1 — o Windows não escala os counts |
| `MouseHoverTime` | `10` | `400` | Hover mais curto |

`MouseSensitivity=10` já é o padrão do Windows; o painel grava o valor de
propósito para desfazer quem mexeu.

### Teclado — `HKCU\Control Panel\Keyboard` (tipo `REG_SZ`)

| Valor | Aplicado | Padrão | Efeito |
|---|---|---|---|
| `KeyboardDelay` | `0` | `1` | Menor atraso antes de repetir |
| `KeyboardSpeed` | `31` | `31` | Repetição mais rápida |

Isso afeta tecla **segurada** (movimento contínuo, strafe). Não afeta o tempo
do primeiro clique — esse depende de USB, polling rate e do jogo.

### Game DVR / captura

| Chave | Valor | Aplicado |
|---|---|---|
| `HKCU\System\GameConfigStore` | `GameDVR_Enabled` | `0` |
| idem | `GameDVR_FSEBehaviorMode` | `2` |
| idem | `GameDVR_HonorUserFSEBehaviorMode` | `1` |
| idem | `GameDVR_DXGIHonorFSEWindowsCompatible` | `1` |
| idem | `GameDVR_EFSEFeatureFlags` | `0` |
| `HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\GameDVR` | `AppCaptureEnabled` | `0` |
| `HKLM\SOFTWARE\Policies\Microsoft\Windows\GameDVR` | `AllowGameDVR` | `0` |

A gravação em segundo plano do Game Bar custa frames e causa stutter — que é
sentido como "sensi travando".

### Multimídia — `HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile` (`REG_DWORD`)

| Valor | Aplicado | Padrão | Efeito |
|---|---|---|---|
| `NetworkThrottlingIndex` | `4294967295` (`0xffffffff`) | `10` | Remove o limite de tráfego de rede durante áudio multimídia |
| `SystemResponsiveness` | `10` | `20` | Reserva menos CPU para tarefas de fundo |

### Gráficos — `HKLM\SYSTEM\CurrentControlSet\Control\GraphicsDrivers`

| Valor | Aplicado | Efeito |
|---|---|---|
| `HwSchMode` | `2` | Agendamento de GPU por hardware (HAGS) |

**Teste este.** HAGS ajuda em alguns setups e piora em outros. Se o emulador
ficar instável, use a opção **[13]** para voltar.

### Energia

`powercfg -setactive e9a42b02-d5df-448d-aa00-03f14749eb61` (Desempenho Máximo).
Se o esquema não existir, o painel duplica e lê o GUID novo da saída — a
detecção procura o GUID no texto, então funciona em Windows em português ou em
inglês. Fallback: Alto Desempenho `8c5e7fda-...`.

### Opcionais (menu [12])

| Chave | Valor | Efeito |
|---|---|---|
| `HKCU\Software\Microsoft\DirectX\UserGpuPreferences` | `GpuPreference=2;` no caminho do `.exe` | Força a GPU dedicada |
| `HKCU\...\AppCompatFlags\Layers` | `~ DISABLEDXMAXIMIZEDWINDOWEDMODE` | Desliga otimização de tela inteira |
| `HKCU\Control Panel\Desktop` | `LowLevelHooksTimeout` | Tempo de espera de hooks de teclado/mouse |

`LowLevelHooksTimeout` pode quebrar software de macro/RGB. Por isso está no
menu avançado e a opção de remover volta ao padrão do sistema.

---

## Android via ADB

O painel procura o `adb` no PATH e depois nos caminhos conhecidos de
BlueStacks, LDPlayer, MuMu, Nox, MEmu e Gameloop. Se não achar, o menu [7]
opção [2] aceita o caminho manual. Ele tenta conectar nas portas `5555`,
`5556`, `5557`, `5559`, `5561`, `5565`, `5575`, `5585`, `5595`, `5554`, `62001`,
`21503`, `7555`, `16384`, `16416`, `16448`, `16480`.

> BlueStacks e LDPlayer vêm com **ADB desligado de fábrica**. Ligue nas
> configurações do emulador antes de usar o menu [7].

Comandos aplicados:

| Comando | Valor | Observação |
|---|---|---|
| `settings put system pointer_speed` | `-7` a `7` | Velocidade do cursor do mouse no Android |
| `settings put system peak_refresh_rate` | `120` | Só existe em Android 11+ |
| `settings put system min_refresh_rate` | `120` | Só existe em Android 11+ |
| `settings put global window_animation_scale` | `0` | |
| `settings put global transition_animation_scale` | `0` | |
| `settings put global animator_duration_scale` | `0` | |
| `settings put secure show_touches` | `0` | |
| `settings put system accelerometer_rotation` | `0` | |

Cada comando é aplicado individualmente e o painel imprime `[--] nao aceito
neste Android` quando a chave não existe naquela versão. Nenhuma falha
individual interrompe as outras.

---

## O que o painel **não** altera, de propósito

### Fila de entrada (`MouseDataQueueSize` / `KeyboardDataQueueSize`)

Aparece em muito guia como "baixe para 8 ou 16 que reduz input lag".
**Não é verdade.** Esses valores definem o tamanho do buffer de eventos dos
drivers `mouclass` e `kbdclass`, e o padrão do Windows é `100`. Diminuir o
buffer não acelera nada — ele começa a **descartar** eventos, o que dá a
sensação de resposta mais seca e na prática causa perda de clique e queda de
quadros. O painel não toca nesses valores e o menu [12] opção [3] explica isso.

### Sensibilidade dentro do jogo

O valor fica no perfil da conta, dentro do app, no Android do emulador. Não há
caminho seguro sem root para escrevê-lo. O painel calcula o número e mostra na
tela; você digita.

---

## Precisa reiniciar?

Sim. As alterações de mouse e teclado em `HKCU\Control Panel\*` só são relidas
no logon. O painel avisa ao final de cada aplicação. As alterações via ADB
valem na hora.

## Backup e reversão

- **[2]** exporta todas as chaves para `painel/backups/<timestamp>/` e grava o
  `pointer_speed` atual do Android e o plano de energia ativo.
- **[13] opção 1** reimporta o último backup e devolve o `pointer_speed`.
- **[13] opção 3** restaura os padrões do Windows sem depender de backup.
