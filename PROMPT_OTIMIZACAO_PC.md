# Prompt de Otimização de PC → geração de `.bat` / `.ps1`

Documento-spec para gerar (com qualquer IA de código, ou direto com um agente) um **kit de otimização de Windows para jogos e emuladores**, com foco em:

- **ganho real medido** (não placebo),
- **reversão total em um comando**,
- **nada destrutivo** (sem desativar Defender por completo, sem desinstalar apps, sem "debloat" cego).

> **Filosofia do prompt:** o script é conservador por padrão e agressivo só quando você pedir explicitamente. Ele **inventaria → mostra o que vai fazer → faz → verifica → registra → permite reverter**.

---

## Como usar (3 passos)

1. **Copie o Bloco 1** (Prompt principal) e cole em uma IA com capacidade de gerar arquivos longos (Claude, GPT, Gemini, etc.).
2. **Copie também o Anexo A** (base técnica das otimizações) e cole **junto**, no fim do prompt. É ele que impede a IA de inventar tweaks placebo.
   - Saída esperada: `HL-Optimizer.ps1`, `HL-Optimizer.cmd`, `HL-Reverter.cmd`, `HL-Simular.cmd` e `README-HL.md` (implementação de referência já incluída em [HL-Optimizer/](HL-Optimizer/)).
3. Peça os arquivos, salve no PC, **rode primeiro com `-DryRun`**, e leia o log antes de aplicar. Depois rode o prompt de auditoria (Anexo D) para revisar o código gerado.

**Teste em VM ou com Ponto de Restauração.** O script já cria um, mas cinto e suspensório.

---

## Bloco 1 — PROMPT PRINCIPAL (copiar e colar)

````text
# PAPEL

Você é um engenheiro Windows sênior especializado em tuning de latência e estabilidade para jogos e emuladores (Android emulators: BlueStacks 5, LDPlayer 9, MuMu 12, MEmu; console emulators: PCSX2, Dolphin, RPCS3, Ryujinx/Citron/Yuzu forks). Você escreve scripts PowerShell e batch de produção, seguros, idempotentes e auditáveis. Você sabe distinguir ganho real de placebo e prefere dizer "isso não rende nada" a entregar tweak inútil.

# OBJETIVO

Gerar um KIT DE OTIMIZAÇÃO PARA WINDOWS 10/11 (x64) com a marca **HL Optimizer**, composto por 5 arquivos, que o usuário possa rodar com um duplo clique, ver o que será alterado ANTES de alterar, aplicar em perfis progressivos de agressividade, e REVERTER TUDO com um comando.

Foco: FPS médio, 1% low (stutter) e tempo de carregamento em jogos e emuladores. Não é "debloat" de privacidade — é desempenho, com telemetria incluída apenas porque é barata de desligar.

# ENTREGÁVEIS (nesta ordem, arquivos completos, sem reticências, sem "resto do código igual")

1. `HL-Optimizer.ps1` — script principal com **painel de início da marca** (PowerShell 5.1 nativo, sem módulos externos, sem internet). **> 45 funções** (lista obrigatória abaixo).
2. `HL-Optimizer.cmd` — launcher que eleva privilégio (UAC) e abre o painel; só ASCII.
3. `HL-Reverter.cmd` — launcher que chama `HL-Optimizer.ps1 -Revert -NoPanel`.
4. `HL-Simular.cmd` — launcher de `-DryRun` (mostra tudo sem alterar nada).
5. `README-HL.md` — marca, como usar, o que cada perfil faz, como reverter, avisos, troubleshooting.

# MARCA E PAINEL DE INÍCIO (obrigatório)

- Produto: **HL Optimizer**. Marca/proprietária do arquivo: **HL**. Versão `1.0.0`, build `AAAA.MM`, tagline `FPS / Games / Emulators`.
- A marca deve aparecer em **6 lugares**: (1) painel de início, (2) cabeçalho de comentário no topo dos 4 arquivos, (3) título da janela do console (`$Host.UI.RawUI.WindowTitle`), (4) primeira linha do log, (5) descrição do ponto de restauração, (6) README.
- O painel deve ser desenhado por uma função própria (`Show-BrandPanel`) e reimpresso após cada ação que altere o sistema.
- Estrutura obrigatória do painel (largura adaptativa 50–78 colunas):
  1. Borda `╔═╗`, título centralizado `H  L     O  P  T  I  M  I  Z  E  R`, subtítulo com tagline/versão/build.
  2. Linha `Host / Usuario` e linha `OS / Build`.
  3. **8 badges de saúde** com valor + cor semântica: **VT-x/SVM**, **Hyper-V/hypervisor**, **VBS/HVCI**, **Game DVR**, **plano de energia**, **tipo de disco (SSD/HDD)**, **emuladores detectados**, **exclusões no Defender**.
  4. Rodapé: último backup encontrado, id da sessão, indicador de `DryRun`.
- Cores: ciano (estrutura), verde (ok), amarelo (aviso), vermelho (crítico), cinza (neutro). Nunca indicar erro só por texto.
- Fechamento do programa com a assinatura: `HL Optimizer v1.0.0 - encerrado. Bom jogo!`.
- Se o console tiver menos de 50 colunas, degradar para layout simples (sem caixa, uma linha por badge) em vez de quebrar.
- **Encoding:** o `.ps1` deve ser salvo em **UTF-8 com BOM** (senão o PS 5.1 corrompe as bordas e acentos); os `.cmd` devem ser **100% ASCII** com CRLF.

# REQUISITOS NÃO NEGOCIÁVEIS (segurança)

1. `#Requires -Version 5.1` e checagem de administrador com mensagem clara + reexecução elevada quando possível.
2. **Toda** alteração passa por uma função que: (a) lê o valor atual, (b) salva em backup, (c) aplica, (d) relê e confirma o novo valor, (e) loga `antes → depois → verificado`.
3. Backup em `.\_backup\AAAA-MM-DD_HH-mm-ss\state.json` (+ `state.csv` legível) com: nome do serviço, StartType original, Status original, chave de registro, nome do valor, tipo, dados originais. Se a chave/valor não existir, gravar `"__ABSENT__"` e na reversão REMOVER o valor criado.
4. `-Revert` restaura 100% do backup (serviços, registro, plano de energia) e nunca "chuta" valores padrão.
5. Ponto de restauração antes de aplicar (se a Proteção do Sistema estiver ativa); se estiver desativada, avisar e pedir confirmação em vez de falhar.
6. Nenhum download, nenhuma chamada de rede, nenhum executável de terceiros, nada de "debloat tool" externa.
7. Idempotente: rodar duas vezes não gera erro nem altera o backup original de forma destrutiva.
8. Todo comando destrutivo exige confirmação explícita (default = NÃO).
9. Nunca reiniciar/desligar o PC automaticamente: no máximo avisar "requer reinício" e registrar no log.
10. Lista de bloqueio hardcoded (`Assert-NeverTouchList`) que aborta o script se algum item proibido aparecer na lista de mudanças — ver Anexo A, seção "NUNCA desativar".
11. Compatível com caminhos com acento/ espaço e com OneDrive; não usar `%TEMP%` do usuário para logs do sistema.
12. Encoding: `.ps1` em UTF-8 **com BOM**; `.cmd` em ASCII puro com CRLF (sem acentos, sem blocos `if` complexos).
13. Toda mensagem ao usuário em **português do Brasil**, sem jargão não explicado; erros sempre com "o que fazer agora".

# ARQUITETURA OBRIGATÓRIA do Optimize.ps1

## Parametrização
```powershell
[CmdletBinding(SupportsShouldProcess=$true)]
param(
  [ValidateSet('Safe','Balanced','Extreme')] [string]$Profile = 'Balanced',
  [switch]$EmulatorFocus,
  [switch]$IncludeExperimental,
  [switch]$Revert,
  [string]$BackupPath,
  [switch]$DryRun,
  [switch]$NonInteractive,
  [switch]$SkipRestorePoint,
  [string]$LogDir = "$PSScriptRoot\_logs"
)
```

## Funções obrigatórias — mínimo 45, agrupadas por responsabilidade

### Núcleo, UI e marca
- `Initialize-HLEnvironment` — valida admin, cria `_logs` e `_HL-Backup`, define título da janela, id da sessão e snapshot do sistema.
- `Show-BrandPanel` — **painel de início da marca HL Optimizer** (seção Marca acima). Reimpresso após ações.
- `Get-PanelWidth`, `Format-Center`, `Write-PanelRow`, `Write-Line`, `Write-Section`, `Format-Badge` — layout do painel, com degradação para console estreito.
- `Write-Log -Message -Level` — níveis `INFO|OK|WARN|ERRO|ACAO|VERIF|DRYRUN|TITULO`; console colorido **e** arquivo.
- `Show-Menu`, `Read-MenuChoice`, `Show-Pause`, `Show-About`, `Show-PlaceboList`, `Show-Summary`.
- `Get-HealthBadges` — retorna lista de `{ Label, Value, State = good|warn|bad }` usada no painel.
- `Open-FolderHL -Path` — abre `_logs`/`_HL-Backup` no Explorer.

### Segurança e backup
- `Assert-Admin [-Silent]`, `Test-NeverTouchList` (aborta com código 4 se a lista de proteção estiver incompleta).
- `Confirm-Action -Message` (em `-DryRun` simula "sim" para gerar preview completo; em `-NonInteractive` nunca pergunta).
- `New-BackupSession`, `Save-BackupSession -Meta`, `Get-BackupObject`, `Get-LatestBackup`.
- `Save-BackupEntry -Type -Target -Before -After -Gain -Risk -RevertHint -RequiresReboot`.
- `New-RestorePointHL` (Checkpoint-Computer; avisa se a Proteção do Sistema estiver off).

### Serviços
- `Get-ServiceState -Name` — StartType lido do registro (`Boot|System|Automatic|AutomaticDelayedStart|Manual|Disabled`) **+** Status.
- `Set-ServiceStateSafe -Name -StartType -StopService -Gain -Risk -Why` — usa `sc.exe config` (mais confiável que `Set-Service` no 5.1), relê e confirma.
- `Find-ServiceByPattern -Patterns` — descoberta de bloatware por padrão de nome/DisplayName.

### Registro
- `Get-RegValueState -Path -Name` — `Exists/Value/Type`, distinguindo **AUSENTE** de vazio (via `GetValueKind`).
- `Set-RegValueSafe -Path -Name -Value -Type [-RequiresReboot] -Gain -Risk` — cria a chave se faltar.
- `Remove-RegValueSafe -Path -Name` — usado para desfazer valores criados do zero.

### Detecção
- `Get-SystemSnapshot` (CPU, RAM, GPU, OS/build, VBS, Game DVR, HAGS, energia, disco, emuladores, exclusões).
- `Get-DiskType -DriveLetter`, `Test-IsSsd`.
- `Get-VbsStatus` (namespace `root\Microsoft\Windows\DeviceGuard` → `Win32_DeviceGuard`), `Get-GameDvrStatus`, `Get-HagsStatus`.
- `Get-ActivePowerScheme` (`powercfg /getactivescheme` + mapa de GUIDs), `Get-GpuVendor`.
- `Test-HyperVInUse` — retorna **array de motivos** (serviços `vmms/vmcompute/WslService/LxssManager`, processos `vmmem/wsl/docker/VBoxSVC`, WSA instalado).
- `Get-InstalledEmulators` (catálogo BlueStacks/LDPlayer/MuMu/MEmu/PCSX2/Dolphin/RPCS3/Ryujinx), `Get-DefenderExclusions`, `Get-DefenderExclusionCount`.

### Ações
- `Set-PowerPlanHigh` (plano + PCIe ASPM off + USB selective suspend off no AC).
- `Set-GameDvrOff`, `Set-DefenderEmulatorExclusions -Paths`, `Set-GpuPreferenceHigh -ExePath`.
- `Invoke-ServicePlan`, `Invoke-BloatwarePlan`, `Invoke-RegistryPlan`.
- `Test-ProfileIncludes -ItemProfile -Target` (composição de camadas sem duplicar código).
- `Invoke-ProfileSafe`, `Invoke-ProfileBalanced`, `Invoke-ProfileExtreme`.

### Módulos do menu
- `Invoke-Diagnostics` — relatório completo em `_logs\hl-diagnostico_*.txt` + top 8 processos.
- `Invoke-EmulatorFocus` — exclusões, GPU por app, diagnóstico de emulador e checklist em `_logs\hl-checklist-emulador_*.txt`.
- `Invoke-GameSession` — boost **temporário**: pausa serviços de fabricante, plano Alto desempenho, `PriorityClass = High` no jogo; **restaura tudo ao fechar** (inclusive se o jogo travar: usar try/finally).
- `Invoke-QuickBenchmark` — 20 amostras de CPU, fila de disco e RAM livre → `_logs\hl-benchmark_*.csv` (deixar explícito que **não é FPS**).
- `Invoke-SelfCheck` — 8 checagens PASS/FAIL (admin, cmdlets Defender/Storage, powercfg, bcdedit, pastas).
- `Invoke-TempCleanup` — limpeza de temporários (usuário/Windows) e cache de shader (NVIDIA/AMD/DirectX), arquivos com mais de 1 dia, **confirmação por pasta**, relata MB liberados; nunca toca em `_HL-Backup`, `_logs` ou pastas de jogos.
- `Invoke-RevertHL` — restauração em ordem inversa, por tipo.

## Comportamento
- Sem parâmetro → **painel da marca** + menu interativo:
  `1` Diagnóstico · `2` Perfil Seguro · `3` Perfil Equilibrado · `4` Perfil Agressivo · `5` Foco Emuladores · `6` Sessão de jogo · `7` Benchmark rápido · `8` Self-check · `9` Reverter · `L` logs · `B` backups · `P` placebos · `A` sobre · `0` sair.
- O painel é reimpresso depois das opções 2–6 e 9 (o usuário vê o estado mudando em tempo real).
- Sempre mostrar um **preview** ("Vou fazer X, Y, Z — 14 alterações. Continuar? [s/N]") antes de aplicar, exceto com `-NonInteractive -Force`.
- `-DryRun`: executa toda a lógica, faz o backup, e loga `[DRY-RUN] faria: ...` sem alterar nada. Nenhuma escrita em serviço/registro/energia.
- Ao final: resumo em tabela (`Item | Antes | Depois | Status`), caminho do log, caminho do backup, e "para reverter: Reverter.cmd" + se precisa reiniciar.
- Códigos de saída: `0` sucesso, `1` erro genérico, `2` abortado pelo usuário, `3` sem admin, `4` lista de bloqueio violada, `5` backup falhou (aborta antes de aplicar).

# O QUE O SCRIPT DEVE FAZER — em camadas

## Camada 0 — Diagnóstico (somente leitura, sempre disponível)
Relatar e salvar em `_logs\diagnostico_*.txt`:
- CPU, núcleos/fios, RAM total e livre, GPU(s) e driver, versão do Windows/build.
- **Virtualização habilitada na BIOS?** (Win32_Processor.VirtualizationFirmwareEnabled + Win32_ComputerSystem.HypervisorPresent) e se o hypervisor está ativo.
- **VBS/HVCI** estado (deve reportar como "custo de virtualização ativo").
- **Hyper-V/WSL2/Docker/WSA** instalados/em execução.
- **Game DVR / Game Bar** estado (registro).
- Plano de energia ativo, presença de bateria, se está na tomada.
- Tipo de disco (SSD/HDD) por volume.
- Uso de CPU/disco em 10 s (`Get-Counter` de `\Processor(_Total)\% Processor Time`, `\PhysicalDisk(_Total)\Current Disk Queue Length`, `\Memory\Available MBytes`) como linha de base.
- Top 10 serviços de terceiros em execução (fabricante/RGB/áudio) — só listar.
- Se a pasta do emulador está nas exclusões do Defender (via `Get-MpPreference`).
- Veredito curto: "Gargalo provável: X" com base nas regras do Anexo A.

## Camada 1 — Perfil `Safe` (ganho garantido, risco ~zero)
- Plano de energia: ativar `Alto desempenho` (se não existir, `Ultimate Performance` via `powercfg -duplicatescheme`), salvar GUID antigo no backup.
- No plano ativo (AC): PCIe Link State Power Management = OFF; suspensão seletiva USB = desativado em AC.
- Game DVR/Capturas: desligar `GameDVR_Enabled`, `AppCaptureEnabled`, `HistoricalCaptureEnabled`, política `AllowGameDVR=0`, e "abrir Game Bar com o botão" (`UseNexusForGameBarEnabled=0`).
- **Modo de Jogo do Windows: MANTER ligado** (não mexer — é o consenso atual).
- Exclusões no Defender (se `-EmulatorFocus` ou se detectar pastas de emulador): `Add-MpPreference -ExclusionPath` para `$env:USERPROFILE\AppData\Local\Android\*`, `Program Files\BlueStacks_nxt`, `LDPlayer*`, `MuMu*`, `MEmu*`, pastas de ROM/jogos e as **pastas de VDI/img** do emulador. Registrar cada exclusão adicionada no backup para remoção na reversão.
- Deduplicação de telemetria leve: `DiagTrack`, `dmwappushservice`, `WerSvc` (parar + desabilitar), apenas com log e backup.
- Ajustes de aparência de desempenho: `VisualFXSetting=2` (melhor desempenho), mantendo fontes suavizadas (não zerar tudo — legibilidade).
- `Win32PrioritySeparation = 38 (0x26)`.

## Camada 2 — Perfil `Balanced` (padrão; inclui Camada 1)
- `SysMain` desabilitar **somente se o disco do SO for SSD**; se HDD, apenas avisar e pular.
- `WSearch` desabilitar OU (preferível) manter e remover da indexação as pastas de jogos/emuladores.
- `DoSvc` — não desabilitar às cegas: limitar upload (`DODownloadMode`/limite) e/ou desabilitar só com confirmação, explicando o impacto em Store/Update.
- `Spooler` desabilitar **apenas se** não houver impressora instalada (`Get-Printer` vazio) — senão pular.
- `bthserv`/`BTAGService`: desabilitar se nenhum dispositivo Bluetooth emparelhado/ativo.
- Serviços Xbox (`XblAuthManager`, `XblGameSave`, `XboxNetApiSvc`, `XboxGipSvc`): desabilitar **somente se** não houver Game Pass/app Xbox/controle Xbox detectado; senão apenas avisar.
- Bloatware de fabricante conhecido por causar stutter (somente parar/desabilitar, **nunca desinstalar**): `NahimicService`, `SonicStudio*`, `ArmouryCrateService`, `LightingService`, `iCUE*`, `Razer*`, `G HUB`/`LGHUBUpdaterService`, `MSIAfterburner` OSD. Deixar claro no log: "afeta RGB/áudio virtual; reative se usar".
- `RemoteRegistry`, `Fax`, `PhoneSvc`, `MapsBroker`, `RetailDemo`, `WinRM` (se não usado) → desabilitar (limpeza, ganho ~0, mas seguro).
- Otimização de Entrega: limite de banda de upload em % (ex.: 5%) e "não usar de outros PCs" (modo 0/1) — reversível.
- Limpar arquivos temporários do sistema e cache de shaders de GPU **com confirmação**, com tamanho recuperado no log (nunca apagar `_backup`).

## Camada 3 — Perfil `Extreme` + `-IncludeExperimental` (assume risco)
Tudo isso deve ser **opt-in explícito**, com aviso em vermelho no console e registro de AVISO no backup:
- **Hyper-V / Plataforma de Hipervisor / VMS Platform / WSL / WSA / Sandbox**: se `Test-HyperVInUse` detectar uso, **NÃO desabilitar** — apenas explicar que Hyper-V ativo degrada BlueStacks/LDPlayer e sugerir desinstalar/desabilitar manualmente. Se não estiver em uso, oferecer `bcdedit /set hypervisorlaunchtype off` **com confirmação dupla**, avisando que exige reinício e que quebra WSL2/Docker/WSA até reverter (`auto`). Registrar comando de reversão no log e no README.
- **VBS/HVCI (Integridade de memória)**: não é confiável desligar via script (chaves protegidas: `HKLM\SYSTEM\CurrentControlSet\Control\DeviceGuard\*`, `HVCI`, e a UI de Segurança do Windows reverte). Portanto: o script deve **detectar e instruir** com passo a passo (`Segurança do Windows → Isolamento do núcleo → Integridade de memória: Desligado + reiniciar`), e verificar depois se ficou off via `Win32_DeviceGuard`. Não forçar via registro.
- HAGS (`HKLM\SYSTEM\CurrentControlSet\Control\GraphicsDrivers\HwSchMode`): oferecer alternar (2 = ligado / 1 = desligado) **com aviso de que é "teste e compare"** — não é ganho garantido.
- `NetworkThrottlingIndex = 0xFFFFFFFF` e `SystemResponsiveness = 10` em `Multimedia\SystemProfile`: marcar como **experimental/placebo em muitos casos**; desligado por padrão.
- Desativar `Superfetch` em HDD já está coberto (pular), desativar paginação **NUNCA**.
- Nenhum "timer resolution", "HPET", "MSI mode", "prioridade de IRQ", "registro mágico" — proibido neste kit, mesmo em Extreme (justificar no README como "não incluído por falta de evidência").

## Camada 4 — Boost temporário (`Invoke-GameSession`, opção 6 do menu)
- Pede o caminho do `.exe` (ENTER usa o 1º emulador detectado).
- Pausa serviços de fabricante (RGB/monitoramento) **só durante a sessão**, em try/finally.
- Muda para plano Alto desempenho e define `PriorityClass = High` no processo do jogo.
- Ao fechar o jogo — ou se o script for interrompido — **restaura** serviços e plano de energia. Nunca deixa o PC no estado de boost.

## Camada 5 — Medição e verificação (não altera nada)
- `Invoke-Diagnostics` → relatório com VT-x, Hyper-V, VBS, Game DVR, HAGS, energia, disco, emuladores, exclusões, contadores de 10 s e top 8 processos.
- `Invoke-QuickBenchmark` → 20 amostras (CPU/fila de disco/RAM livre) em CSV, com o aviso explícito "isto não é FPS; use CapFrameX/PresentMon".
- `Invoke-SelfCheck` → 8 checagens PASS/FAIL de ambiente antes de aplicar.
- `Show-PlaceboList` → o que o kit recusa fazer e por quê.

## Módulo `-EmulatorFocus` (transversal)
- Detecta emuladores instalados (caminhos/pastas) e **apenas orienta** o que fazer DENTRO do emulador (não é possível via script): resolução 1280x720/960x540, 4 núcleos, 4 GB RAM, renderer Vulkan → OpenGL → DirectX, FPS travado igual ao monitor, desligar gravação de tela, alto desempenho.
- Checa e reporta: VT-x/SVM na BIOS (via CPU/`VirtualizationFirmwareEnabled`), Hyper-V ativo, VBS ativo, HAGS, % de disco ocupado, RAM disponível (alerta se < 2 GB livre).
- Aplica exclusões no Defender para a pasta do emulador e para a pasta de imagens (`.vdi`, `.img`, `.qcow2`, vmdk).
- Aplica "Alto desempenho" de GPU por app no Windows (`HKCU\Software\Microsoft\DirectX\UserGpuPreferences` com `GpuPreference=2`) para o `.exe` do emulador — **um por um, com o caminho real detectado**, e backup para reverter.
- Gera no fim um checklist imprimível `_logs\checklist_emulador.txt`.

# REGRAS DE CONTEÚDO (anti-placebo, obrigatório)

- Cada ação precisa ter no código um comentário de uma linha justificando o ganho esperado e o risco: `# +5-15% quando ativo (gravação em 2º plano) | risco: perde gravação de clipes`.
- Se uma ação não tem ganho mensurável, ela **não entra** no perfil padrão. Pode entrar em `-IncludeExperimental` com o rótulo `# sem ganho comprovado`.
- Não usar nomes/descobertas inventadas: só o que está no Anexo A + APIs reais do Windows. Se não tiver certeza do nome de um serviço/chave, o script deve **detectar por padrão de nome** e logar "não encontrado — ignorado", em vez de falhar.

# QUALIDADE DE CÓDIGO

- `Set-StrictMode -Version Latest` e `$ErrorActionPreference = 'Stop'` com try/catch por ação (uma ação falhar não aborta o resto; é logada como `FALHOU`).
- Sem aliases (`?`, `%`, `ls`), sem `Write-Host` sem cor semântica: usar `Write-Log` (verde=ok, amarelo=aviso, vermelho=erro, ciano=ação).
- Arquivo salvo em **UTF-8 com BOM** (para acentos no PS 5.1). Dentro do `.cmd`, **somente ASCII** (sem acentos) e `chcp` não é necessário; nada de blocos `if` complexos no .cmd — só elevação + chamada.
- Sem `Invoke-Expression`, sem `iex`, sem download, sem `curl`/`iwr`.
- Deve passar em `Invoke-ScriptAnalyzer -Severity Warning,Error` (ou explicar as exceções).
- Tamanho de cada arquivo justificado; evitar duplicação usando tabelas de dados (`$script:ServicePlan`, `$script:RegPlan`) em vez de 50 blocos `if`.

# CRITÉRIOS DE ACEITE (o script só está pronto se todos passarem)

1. `.\Optimize.ps1 -DryRun -NonInteractive` roda em uma VM limpa Win11 sem erros e sem alterar nada (comparar snapshot de `Get-Service` + `reg export` antes/depois).
2. `.\Optimize.ps1 -Profile Safe -NonInteractive` aplica, verifica cada item, e `-Revert` volta exatamente ao estado inicial (diff vazio no snapshot, exceto timestamps).
3. Rodar `-Profile Balanced` duas vezes seguidas: sem erro, sem duplicar backup, log mostra "já aplicado".
4. `Reverter.cmd` com duplo clique funciona e mostra resumo do que foi restaurado.
5. `Assert-NeverTouchList` aborta com código 4 se alguém adicionar `RpcSs`, `DcomLaunch`, `PlugPlay`, `Power`, `AudioSrv`, `AudioEndpointBuilder`, `CryptSvc`, `EventLog`, `Winmgmt`, `Themes`, `UxSms`, `Dwm`, `LanmanWorkstation`, `Dhcp`, `Dnscache` à lista.
6. Em uma máquina com Hyper-V/WSL2 ativo, o perfil Extreme **recusa** desativar e explica o motivo.
7. Em uma máquina só com HDD, `SysMain` é preservado e o log explica.
8. Nenhum reboot automático; aviso de "requer reinício" aparece no resumo quando houver mudança que exija.
9. **Painel:** abre em < 1 s, mostra os **8 badges** corretos em uma VM limpa (VT-x desligado, VBS on, Game DVR on, energia Equilibrado) e não quebra com janela de 50 colunas.
10. **Marca:** `README-HL.md`, nome do produto, tagline, versão e assinatura final aparecem nos 6 lugares exigidos; nenhum arquivo ficou com nome `Optimize.ps1`.
11. **Encoding:** o `.ps1` está em UTF-8 com BOM (bordas do painel legíveis no PS 5.1) e os `.cmd` são ASCII puro.
12. **Sessão de jogo:** fechar o jogo (ou matar o script) devolve os serviços pausados e o plano de energia original.

# FORMATO DA SUA RESPOSTA

1. Uma frase de resumo do desenho (perfis, nº de ações por camada).
2. Os 4 arquivos, completos, cada um em seu próprio bloco de código, com o nome do arquivo como título.
3. Uma tabela final: `Ação | Camada/Perfil | Ganho esperado | Risco | Como reverter`.
4. Uma seção curta "O que NÃO foi incluído e por quê" (placebos rejeitados).
5. Perguntas em aberto (máx. 3) somente se forem bloqueantes.

Não peça confirmação antes de gerar os arquivos. Gere tudo agora, assumindo Windows 10/11 x64 em português.
````

---

## Bloco 2 — Anexo A: base técnica (colar junto com o Bloco 1)

````text
# ANEXO A — BASE TÉCNICA (fonte de verdade; não invente além disto)

## A.1 Ordem real de impacto
| # | Item | Ganho típico | Observação |
|---|------|--------------|------------|
| 1 | VT-x/SVM na BIOS + Hyper-V desligado | 10–100%+ em emulador | Sem VT-x o emulador roda em modo lento/traduzido |
| 2 | Isolamento de núcleo (VBS + HVCI) desligado | 5–15% CPU | Custo de virtualização em cada acesso à memória |
| 3 | Xbox Game Bar / Game DVR (gravação em 2º plano) | 5–15% | O "serviço" com maior ganho real |
| 4 | Config interna do emulador (resolução, núcleos, renderer) | 20–200% | Peso maior que qualquer tweak de Windows |
| 5 | Plano de energia + GPU em desempenho máximo | 3–10% | Crítico em notebook |
| 6 | Overlays (Discord, GeForce/NVIDIA, Steam, RTSS) | 3–10% | Mais se com Instant Replay/gravação |
| 7 | Antivírus: exclusões em vez de desativar | 2–10% | Escaneamento em tempo real nos arquivos do emulador |
| 8 | Serviços de fabricante (Nahimic, Armoury, iCUE) | 2–6% | Causam stutter, não só FPS médio |
| 9 | SysMain / WSearch / Delivery Optimization | 0–3% | Só perceptível em HDD ou disco cheio |
| 10 | Telemetria, Spooler, Xbox services, Bluetooth | ~0% | Privacidade/segurança, não FPS |

## A.2 Registro / políticas
- Game DVR:
  - `HKCU\System\GameConfigStore` → `GameDVR_Enabled` (DWORD 0/1)
  - `HKCU\Software\Microsoft\Windows\CurrentVersion\GameDVR` → `AppCaptureEnabled` (DWORD)
  - `HKLM\SOFTWARE\Policies\Microsoft\Windows\GameDVR` → `AllowGameDVR` (DWORD)
- Game Bar: `HKCU\Software\Microsoft\GameBar` → `UseNexusForGameBarEnabled`, `AutoGameModeEnabled` (manter 1), `ShowStartupPanel`
- Aparência: `HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects` → `VisualFXSetting` (2 = melhor desempenho)
- Prioridade: `HKLM\SYSTEM\CurrentControlSet\Control\PriorityControl` → `Win32PrioritySeparation` (38 / 0x26)
- GPU por app: `HKCU\Software\Microsoft\DirectX\UserGpuPreferences` → `<caminho.exe>` = `GpuPreference=2;`
- HAGS: `HKLM\SYSTEM\CurrentControlSet\Control\GraphicsDrivers` → `HwSchMode` (1 off / 2 on) [experimental]
- Multimídia (experimental): `HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile` → `NetworkThrottlingIndex` (0xFFFFFFFF), `SystemResponsiveness` (10)
- Delivery Optimization: `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\DeliveryOptimization\Config` (DODownloadMode) e limite de upload

## A.3 Serviços — referência de padrão (o script deve CONFERIR o valor real, não confiar nesta tabela)
| Serviço interno | Padrão usual | Ação sugerida | Ganho |
|---|---|---|---|
| `SysMain` | Automatic | desabilitar só em SSD | 0–3% |
| `WSearch` | Automatic (Delayed) | desabilitar ou remover pastas da indexação | 0–3% |
| `DiagTrack` | Automatic | desabilitar | ~0% (privacidade) |
| `dmwappushservice` | Manual (trigger) | desabilitar | ~0% |
| `WerSvc` | Manual (trigger) | desabilitar | ~0% |
| `DoSvc` | Automatic (Delayed) | limitar/desabilitar com confirmação | 0–2% |
| `Spooler` | Automatic | desabilitar só sem impressora | ~0% |
| `bthserv`, `BTAGService` | Manual | desabilitar só sem Bluetooth | 0–1% |
| `XblAuthManager`, `XblGameSave`, `XboxNetApiSvc`, `XboxGipSvc` | Manual | desabilitar só sem ecossistema Xbox | ~0% |
| `RetailDemo`, `MapsBroker`, `PhoneSvc`, `Fax`, `RemoteRegistry`, `WinRM` | Manual/Disabled | desabilitar | ~0% |
| `NahimicService`, `SonicStudio*` | Automatic | desabilitar (áudio virtual de fabricante) | 2–6% |
| RGB/monitoramento (`ArmouryCrateService`, `LightingService`, `iCUE*`, `Razer*`, `LGHUBUpdaterService`) | Automatic | parar/desabilitar durante o jogo | 2–6% |
| `NVIDIA Display Container LS`, `AMD External Events Utility` | Automatic | **NÃO MEXER** | — |

## A.4 NUNCA desativar (lista de bloqueio)
`RpcSs`, `RpcEptMapper`, `DcomLaunch`, `PlugPlay`, `Power`, `BrokerInfrastructure`, `SystemEventsBroker`, `EventLog`, `Winmgmt`, `Themes`, `UxSms`, `Dwm`, `AudioSrv`, `AudioEndpointBuilder`, `CryptSvc`, `ProfSvc`, `UserManager`, `LanmanWorkstation`, `Dhcp`, `Dnscache`, `BFE`, `mpssvc`, `gpsvc`, `Schedule`, `SamSs`, `LSM`, `nsi`, `TrustedInstaller` (durante execução), `wuauserv` (em vez de desativar, pausar atualizações).

## A.5 Placebo / proibido neste kit
- Desativar 80–150 serviços com lista de YouTube/debloat tool genérica.
- "Limpar RAM", "booster de registro", desativar paginação.
- `bcdedit` de timers/HPET/"useplatformclock", MSI mode, prioridade de IRQ.
- Desativar Windows Defender por completo (usar exclusões).
- Desativar o "Modo de Jogo" do Windows.
- Prometer números fixos de FPS: todo ganho é uma faixa e depende do hardware.

## A.6 Pré-requisitos de emulador (o script detecta e orienta; não altera)
- VT-x/AMD-V + IOMMU na BIOS.
- Hyper-V/WSL2/Docker/WSA desinstalados ou desabilitados quando o emulador exigir VT-x cru.
- VBS/HVCI desligado.
- Resolução interna baixa (1280x720 ou 960x540), DPI 240, 4 núcleos, 4 GB RAM.
- Renderer: Vulkan → OpenGL → DirectX (ordem de preferência em Android-emu); consoles: Vulkan, resolução 1x, async shader/cache.
- FPS limitado à taxa do monitor.
````

---

## Bloco 3 — Anexo B: contrato de dados (opcional, aumente a precisão)

````text
# ANEXO B — CONTRATO DE DADOS

## state.json (schema)
{
  "session": "2026-10-25_14-32-07",
  "host": { "name": "", "os": "", "build": "", "psVersion": "" },
  "profile": "Balanced",
  "createdUtc": "2026-10-25T14:32:07Z",
  "changes": [
    { "id": 1, "type": "Service", "target": "SysMain",
      "before": { "startType": "Automatic", "status": "Running" },
      "after":  { "startType": "Disabled",  "status": "Stopped" },
      "verified": true, "requiresReboot": false,
      "gain": "0-3%", "risk": "baixo", "revert": "Set-ServiceStateSafe -Name SysMain -StartupType Automatic" },
    { "id": 2, "type": "Registry", "target": "HKCU\\System\\GameConfigStore\\GameDVR_Enabled",
      "before": { "exists": true, "value": 1, "type": "DWORD" },
      "after":  { "exists": true, "value": 0, "type": "DWORD" },
      "verified": true, "requiresReboot": false }
  ],
  "defenderExclusionsAdded": ["C:\\Program Files\\BlueStacks_nxt"],
  "powerSchemeBefore": "{381b4222-f694-41f0-9685-ff5bb260df2e}",
  "rebootRequired": true
}

## Log (linha)
[2026-10-25 14:32:10] [CHANGE] Service SysMain: Automatic(Running) -> Disabled(Stopped)
[2026-10-25 14:32:10] [VERIFY] Service SysMain: agora Disabled(Stopped) = OK
[2026-10-25 14:32:11] [WARN]  HDD detectado em C: — SysMain preservado

## Saída final (console)
Tabela: Item | Antes | Depois | OK
Rodapé: log, backup, "reinício necessário: sim", "reverter: Reverter.cmd"
````

---

## Bloco 4 — Variante curta (para contexto pequeno ou chat rápido)

````text
Gere um kit Windows 10/11 para FPS em jogos/emuladores com a marca **HL Optimizer** (proprietária: HL, v1.0.0, tagline "FPS / Games / Emulators"): `HL-Optimizer.ps1`, `HL-Optimizer.cmd`, `HL-Reverter.cmd`, `HL-Simular.cmd`, `README-HL.md`.

O .ps1 abre com um PAINEL DE INÍCIO da marca: caixa `╔═╗` com título centralizado "H L O P T I M I Z E R", subtítulo de versão, host/usuario, OS/build e 8 badges coloridos (VT-x/SVM, Hyper-V, VBS/HVCI, Game DVR, plano de energia, SSD/HDD, emuladores, exclusoes Defender) + rodapé com ultimo backup e sessao. Reimprime o painel após cada ação. Marca também no cabeçalho dos arquivos, título da janela, log, nome do ponto de restauração e README. .ps1 em UTF-8 com BOM; .cmd só ASCII. Menu: 1 Diagnostico, 2 Seguro, 3 Equilibrado, 4 Agressivo, 5 Foco Emuladores, 6 Sessao de jogo (boost temporario que restaura ao sair), 7 Benchmark rapido, 8 Self-check, 9 Reverter, L/B/P/A, 0 sair. Minimo de 45 funcoes nomeadas (UI/painel, backup, servico, registro, deteccao, acoes, modulos, reversao).

Optimize.ps1: PS 5.1, admin, `param([ValidateSet('Safe','Balanced','Extreme')]$Profile='Balanced',[switch]$EmulatorFocus,[switch]$IncludeExperimental,[switch]$Revert,[switch]$DryRun,[switch]$NonInteractive)`. Menu interativo + modo não interativo.

Regras: backup JSON de TODA mudança (serviço → StartType/Status; registro → path/nome/tipo/valor) em `_backup\<timestamp>\`; `-Revert` restaura exatamente; verificar cada mudança relendo o valor e logar `antes → depois → OK`; idempotente; ponto de restauração; log em `_logs\`; nada de download/reboot automático; lista de bloqueio que aborta o script se tocar em RpcSs, DcomLaunch, PlugPlay, Power, AudioSrv, AudioEndpointBuilder, Dwm, EventLog, Winmgmt, Themes, LanmanWorkstation, Dhcp, Dnscache, BFE, mpssvc, gpsvc.

Perfil Safe: plano Alto desempenho + PCIe ASPM off + USB selective suspend off (AC); Game DVR/capturas off (HKCU System\GameConfigStore\GameDVR_Enabled, HKCU ...\GameDVR\AppCaptureEnabled, HKLM Policies\...\GameDVR\AllowGameDVR=0); manter Modo de Jogo ligado; exclusões Defender para pastas de emulador; DiagTrack/dmwappushservice/WerSvc off; VisualFXSetting=2; Win32PrioritySeparation=38.
Perfil Balanced: SysMain off só se SSD; WSearch off ou fora da indexação; Spooler off só sem impressora; Bluetooth off só sem dispositivo; serviços Xbox off só sem Game Pass/controle; Nahimic/Armoury/iCUE/Razer/LGHUB parar (nunca desinstalar); DoSvc só com confirmação; Fax/PhoneSvc/MapsBroker/RemoteRegistry/WinRM off.
Perfil Extreme (+IncludeExperimental): hypervisorlaunchtype off SOMENTE se não houver Hyper-V/WSL2/Docker/WSA em uso (senão recusar e explicar); VBS/HVCI apenas DETECTAR e instruir (nunca forçar no registro); HAGS alternável com aviso; NetworkThrottlingIndex/SystemResponsiveness como experimental. Proibido: HPET/timers/MSI mode/desativar paginação/desativar Defender inteiro.

EmulatorFocus: detectar BlueStacks/LDPlayer/MuMu/MEmu, exclusões Defender, GpuPreference=2 no .exe do emulador, relatório de VT-x/Hyper-V/VBS/SSD/RAM livre e checklist com resolução/renderer/núcleos.

Cada ação precisa de comentário com ganho esperado e risco. Entregue os 4 arquivos completos + tabela Ação|Perfil|Ganho|Risco|Reverter.
````

---

## Bloco 5 — Anexo D: prompt de auditoria (rode depois, com o código gerado)

````text
Você é um revisor de segurança e qualidade de scripts PowerShell. Audite o script abaixo.

Verifique, item por item, com veredito PASS/FAIL e linha do código:
1. Toda escrita em serviço/registro/energia é precedida de leitura + backup persistido?
2. `-Revert` reconstrói o estado original, inclusive REMOVENDO valores que não existiam antes?
3. Existe algum caminho onde um serviço da lista proibida pode ser parado (RpcSs, DcomLaunch, PlugPlay, Power, AudioSrv, AudioEndpointBuilder, Dwm, EventLog, Winmgmt, Themes, LanmanWorkstation, Dhcp, Dnscache, BFE, mpssvc, gpsvc)?
4. Há `Invoke-Expression`, download, execução de binário externo, ou `Remove-Item -Recurse` sem confirmação?
5. O script é idempotente? E se rodado duas vezes, sobrescreve o backup original (perdendo o estado pré-otimização)?
6. `-DryRun` realmente não escreve nada? (procure Set-Service, Set-ItemProperty, powercfg sem guarda)
7. Hyper-V/WSL2/Docker/WSA são detectados ANTES de desativar o hypervisor? Existe recusa?
8. Erros de uma ação interrompem as demais? O exit code reflete o resultado?
9. Compatibilidade PS 5.1: usa sintaxe 7+ (`??`, `-not` ternário, `ForEach-Object -Parallel`)?
10. Caminhos com espaço/acento e execução fora de `C:\` funcionam?
11. O .cmd tem apenas ASCII e eleva corretamente? Não quebra se o PS estiver bloqueado por política?
12. Nomes de serviços/chaves existem de fato no Windows 10/11? Aponte qualquer nome inventado.

Depois da tabela, liste as correções prioritárias (P0 quebra/perda de dados, P1 comportamento errado, P2 cosmético) e reescreva apenas as funções afetadas.
````

---

## Checklist humano antes de rodar em máquina real

- [ ] Li o `-DryRun` e nenhuma ação toca em serviço essencial.
- [ ] Criei Ponto de Restauração (ou o script criou e eu vi a mensagem).
- [ ] Sei que **Hyper-V off quebra WSL2/Docker/WSA** até reverter.
- [ ] Sei que **VBS/HVCI off** reduz proteção contra exploits de kernel — decisão consciente.
- [ ] Backupei o `_backup\` em pendrive/nuvem (ele vive na mesma pasta do script).
- [ ] Testei `Reverter.cmd` **antes** de precisar dele.
- [ ] Vou medir com **frame time / 1% low** (CapFrameX, PresentMon), não só FPS médio, 3 rodadas de 60 s na mesma cena.
- [ ] Notebook: testando na tomada e no perfil de desempenho do fabricante.

---

## O que este kit deliberadamente não faz

| Não incluído | Motivo |
|---|---|
| Desativar 100+ serviços / debloat genérico | Ganho ~0%, risco alto de quebrar Store/Update/áudio |
| Desativar Windows Defender inteiro | Exclusões dão o mesmo ganho com risco menor |
| Desligar paginação / "limpar RAM" | Piora 1% low e estabilidade |
| HPET / `useplatformclock` / MSI mode / IRQ priority | Sem evidência sólida; efeitos colaterais imprevisíveis |
| Desinstalar apps do fabricante | Só parar/desabilitar é reversível; desinstalar não |
| Prometer "+200% FPS" | Ganho depende de hardware, GPU, emulador e versão do driver |

---

*Arquivo gerado para o repositório `FPS-`. Uso por sua conta e risco — sempre com backup e teste em VM quando possível.*
