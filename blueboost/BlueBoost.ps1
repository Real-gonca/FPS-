<#
 ============================================================================
  BlueBoost — Otimizador de PC fraco para BlueStacks 5
 ============================================================================
  Aplicativo em PowerShell + WinForms (zero dependências, roda em qualquer
  Windows 10/11). Faz três coisas:

    1. Configura o BlueStacks 5 (bluestacks.conf) com presets seguros para
       PC fraco: CPU, RAM, resolução, DPI, renderizador, FPS, vsync.
       SEMPRE cria backup do arquivo antes de alterar.

    2. Aplica otimizações leves e reversíveis no Windows: plano de energia,
       Game DVR/Game Bar, efeitos visuais, transparência, limpeza de TEMP.

    3. Tutorial embutido (passo a passo) com tudo que não dá para automatizar:
       ativar VT no BIOS, drivers de vídeo, configurações dentro do BlueStacks.

  Uso:  clique duplo em BlueBoost.bat   (ou)   powershell -File BlueBoost.ps1
 ============================================================================
#>

#requires -version 5.1
param(
    [switch]$NoElevate   # pular tentativa de elevação (algumas ações podem falhar)
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

# ---------------------------------------------------------------------------
# Elevação (necessária para escrever em C:\ProgramData e limpar C:\Windows\Temp)
# ---------------------------------------------------------------------------
function Test-IsAdmin {
    $id = [Security.Principal.WindowsIdentity]::GetCurrent()
    $pr = New-Object Security.Principal.WindowsPrincipal($id)
    return $pr.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

if (-not (Test-IsAdmin) -and -not $NoElevate) {
    try {
        $elevArgs = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$PSCommandPath`"", '-NoElevate')
        Start-Process -FilePath 'powershell.exe' -ArgumentList $elevArgs -Verb RunAs | Out-Null
        exit
    } catch {
        # usuário recusou o UAC — segue sem admin, com aviso na interface
    }
}

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
[System.Windows.Forms.Application]::EnableVisualStyles()

$script:IsAdmin = Test-IsAdmin

# ---------------------------------------------------------------------------
# Informações do computador
# ---------------------------------------------------------------------------
function Get-HostSpecs {
    $cs  = Get-CimInstance Win32_ComputerSystem
    $cpu = Get-CimInstance Win32_Processor | Select-Object -First 1
    $os  = Get-CimInstance Win32_OperatingSystem

    $vt = $null
    try { $vt = [bool]$cpu.VirtualizationFirmwareEnabled } catch { }
    $hyperv = $false
    try { $hyperv = [bool]$cs.HypervisorPresent } catch { }

    [pscustomobject]@{
        RamGB        = [math]::Round($cs.TotalPhysicalMemory / 1GB, 1)
        Cores        = [int]$cpu.NumberOfLogicalProcessors
        CpuName      = ($cpu.Name -replace '\s+', ' ').Trim()
        VtEnabled    = $vt          # $true / $false / $null (não detectável, ex: Hyper-V ativo)
        HypervisorOn = $hyperv
        OsName       = $os.Caption
        FreeRamGB    = [math]::Round($os.FreePhysicalMemory / 1MB, 1)
    }
}

# ---------------------------------------------------------------------------
# BlueStacks 5 — localizar e ler/gravar bluestacks.conf
# ---------------------------------------------------------------------------
function Get-BsConfPath {
    $candidates = @()
    try {
        $reg = Get-ItemProperty -Path 'HKLM:\SOFTWARE\BlueStacks_nxt' -ErrorAction SilentlyContinue
        if ($reg -and $reg.PSObject.Properties['UserDefinedDir'] -and $reg.UserDefinedDir) {
            $candidates += (Join-Path $reg.UserDefinedDir 'bluestacks.conf')
        }
        if ($reg -and $reg.PSObject.Properties['DataDir'] -and $reg.DataDir) {
            $candidates += (Join-Path (Split-Path $reg.DataDir -Parent) 'bluestacks.conf')
        }
    } catch { }
    $candidates += (Join-Path $env:ProgramData 'BlueStacks_nxt\bluestacks.conf')
    $candidates += 'C:\ProgramData\BlueStacks_nxt\bluestacks.conf'

    foreach ($c in ($candidates | Select-Object -Unique)) {
        if ($c -and (Test-Path -LiteralPath $c)) { return (Resolve-Path -LiteralPath $c).Path }
    }
    return $null
}

function Read-BsConf([string]$Path) {
    # devolve lista de linhas cruas; o formato é  chave="valor"
    return [System.Collections.Generic.List[string]](Get-Content -LiteralPath $Path -Encoding UTF8)
}

function Get-BsInstances($Lines) {
    $set = New-Object System.Collections.Generic.SortedSet[string]
    foreach ($l in $Lines) {
        if ($l -match '^bst\.instance\.([^.]+)\.') { [void]$set.Add($Matches[1]) }
    }
    return @($set)
}

function Get-BsValue($Lines, [string]$Key) {
    foreach ($l in $Lines) {
        if ($l -match ('^' + [regex]::Escape($Key) + '="(.*)"\s*$')) { return $Matches[1] }
    }
    return $null
}

function Set-BsValue($Lines, [string]$Key, [string]$Value) {
    # substitui se existir; senão insere em ordem alfabética aproximada
    $newLine = ('{0}="{1}"' -f $Key, $Value)
    for ($i = 0; $i -lt $Lines.Count; $i++) {
        if ($Lines[$i] -match ('^' + [regex]::Escape($Key) + '=')) {
            if ($Lines[$i] -ne $newLine) { $Lines[$i] = $newLine; return 'alterado' }
            return 'igual'
        }
    }
    for ($i = 0; $i -lt $Lines.Count; $i++) {
        if ([string]::CompareOrdinal($Lines[$i], $newLine) -gt 0) {
            $Lines.Insert($i, $newLine); return 'criado'
        }
    }
    $Lines.Add($newLine)
    return 'criado'
}

function Backup-BsConf([string]$Path) {
    $stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
    $bak = "$Path.bak-$stamp"
    Copy-Item -LiteralPath $Path -Destination $bak -Force
    return $bak
}

function Save-BsConf([string]$Path, $Lines) {
    # UTF-8 sem BOM, igual ao original do BlueStacks
    $enc = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllLines($Path, [string[]]$Lines, $enc)
}

function Stop-BlueStacks {
    $names = @('HD-Player', 'HD-Agent', 'BstkSVC', 'BlueStacksHelper', 'BlueStacksServices')
    $killed = @()
    foreach ($n in $names) {
        $procs = Get-Process -Name $n -ErrorAction SilentlyContinue
        foreach ($p in $procs) {
            try { $p.Kill(); $killed += $n } catch { }
        }
    }
    if ($killed.Count -gt 0) { Start-Sleep -Milliseconds 800 }
    return ($killed | Select-Object -Unique)
}

# ---------------------------------------------------------------------------
# Presets para a instância do BlueStacks
#   Os valores são conservadores e só mexem em chaves conhecidas/seguras.
# ---------------------------------------------------------------------------
function Get-BsPreset([string]$Name, $Specs) {
    $cores = [math]::Max(2, [int]$Specs.Cores)

    switch ($Name) {
        'UltraLeve' {
            # PCs com 4 GB ou menos / dual-core — o mínimo que roda bem
            @{
                cpus               = '2'
                ram                = $(if ($Specs.RamGB -le 4) { '1536' } else { '2048' })
                fb_width           = '960'
                fb_height          = '540'
                dpi                = '160'
                graphics_renderer  = 'gl'
                max_fps            = '30'
                enable_high_fps    = '0'
                enable_vsync       = '0'
                show_sidebar       = '0'
                enable_fps_display = '0'
            }
        }
        'Equilibrado' {
            # 6–8 GB de RAM — bom para a maioria dos jogos leves/médios
            @{
                cpus               = [string]([math]::Min(4, [math]::Max(2, [int]($cores / 2))))
                ram                = '2048'
                fb_width           = '1280'
                fb_height          = '720'
                dpi                = '240'
                graphics_renderer  = 'gl'
                max_fps            = '60'
                enable_high_fps    = '0'
                enable_vsync       = '0'
                show_sidebar       = '0'
                enable_fps_display = '0'
            }
        }
        'Desempenho' {
            # 8 GB ou mais — prioriza fluidez
            @{
                cpus               = [string]([math]::Min(4, $cores))
                ram                = '4096'
                fb_width           = '1280'
                fb_height          = '720'
                dpi                = '240'
                graphics_renderer  = 'dx'
                max_fps            = '60'
                enable_high_fps    = '0'
                enable_vsync       = '0'
                show_sidebar       = '0'
                enable_fps_display = '0'
            }
        }
    }
}

function Get-RecommendedPreset($Specs) {
    if ($Specs.RamGB -le 4.5 -or $Specs.Cores -le 2) { return 'UltraLeve' }
    if ($Specs.RamGB -le 8.5) { return 'Equilibrado' }
    return 'Desempenho'
}

# ---------------------------------------------------------------------------
# Otimizações do Windows (todas reversíveis, com log do que foi feito)
# ---------------------------------------------------------------------------
function Set-RegDword([string]$Path, [string]$Name, [int]$Value) {
    if (-not (Test-Path $Path)) { New-Item -Path $Path -Force | Out-Null }
    New-ItemProperty -Path $Path -Name $Name -Value $Value -PropertyType DWord -Force | Out-Null
}

function Invoke-TweakPowerPlan {
    # Alto desempenho (GUID fixo do Windows)
    $old = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        & powercfg /setactive 8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c 2>&1 | Out-Null
        $code = $LASTEXITCODE
    } finally {
        $ErrorActionPreference = $old
    }
    if ($code -ne 0) { throw 'powercfg retornou erro (plano pode não existir nesta edição do Windows).' }
    'Plano de energia definido para Alto Desempenho. (Reverter: Painel de Controle > Opções de Energia > Equilibrado)'
}

function Invoke-TweakGameDvr {
    Set-RegDword 'HKCU:\System\GameConfigStore' 'GameDVR_Enabled' 0
    Set-RegDword 'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\GameDVR' 'AppCaptureEnabled' 0
    Set-RegDword 'HKCU:\SOFTWARE\Microsoft\GameBar' 'UseNexusForGameBarEnabled' 0
    Set-RegDword 'HKCU:\SOFTWARE\Microsoft\GameBar' 'AutoGameModeEnabled' 1
    'Game DVR/captura da Game Bar desativados e Modo de Jogo ativado. (Reverter: Configurações > Jogos)'
}

function Invoke-TweakVisualEffects {
    Set-RegDword 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects' 'VisualFXSetting' 2
    'Efeitos visuais ajustados para "Melhor desempenho". Faça logoff/login para aplicar 100%. (Reverter: sysdm.cpl > Avançado > Desempenho)'
}

function Invoke-TweakTransparency {
    Set-RegDword 'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize' 'EnableTransparency' 0
    'Transparência do Windows desativada. (Reverter: Configurações > Personalização > Cores)'
}

function Invoke-TweakCleanTemp {
    $total = 0L
    $targets = @($env:TEMP)
    if ($script:IsAdmin) { $targets += (Join-Path $env:WINDIR 'Temp') }
    foreach ($t in $targets) {
        if (-not (Test-Path $t)) { continue }
        Get-ChildItem -LiteralPath $t -Force -ErrorAction SilentlyContinue | ForEach-Object {
            try {
                $size = 0L
                if ($_.PSIsContainer) {
                    $size = (Get-ChildItem $_.FullName -Recurse -Force -ErrorAction SilentlyContinue |
                             Measure-Object Length -Sum -ErrorAction SilentlyContinue).Sum
                } else { $size = $_.Length }
                Remove-Item $_.FullName -Recurse -Force -ErrorAction Stop
                if ($size) { $total += $size }
            } catch { } # arquivo em uso — ignora
        }
    }
    ('Limpeza de temporários concluída: ~{0:N0} MB liberados.' -f ($total / 1MB))
}

function Invoke-TweakStartupInfo {
    # Não desativamos nada automaticamente (muito invasivo) — só abrimos o gerenciador.
    Start-Process 'taskmgr.exe' -ArgumentList '/0 /startup' -ErrorAction SilentlyContinue
    'Gerenciador de Tarefas aberto na aba Inicializar — desative manualmente o que não usa.'
}

# ---------------------------------------------------------------------------
# Texto do tutorial embutido
# ---------------------------------------------------------------------------
$TutorialText = @"
================================================================
 GUIA RÁPIDO — BLUESTACKS 5 EM PC FRACO
================================================================

PASSO 1 — ATIVAR A VIRTUALIZAÇÃO (VT) NO BIOS  [o mais importante!]
  A VT pode dobrar o desempenho do BlueStacks. Sem ela, tudo fica lento.
  1. Reinicie o PC e entre no BIOS/UEFI (geralmente DEL, F2, F10 ou ESC
     logo ao ligar — a tela inicial mostra a tecla).
  2. Procure por: "Intel Virtualization Technology", "Intel VT-x",
     "SVM Mode" (AMD) ou apenas "Virtualization".
     Costuma ficar em: Advanced / CPU Configuration / Security.
  3. Mude para ENABLED, salve (F10) e reinicie.
  4. Use o botão "Verificar VT" na aba BlueStacks 5 para confirmar.
  Obs.: se o Hyper-V do Windows estiver ativo, o BlueStacks usa um modo
  próprio mais lento. Em PC fraco, desative: Painel de Controle >
  Programas > Ativar ou desativar recursos do Windows > desmarque
  "Hyper-V" e "Plataforma de Máquina Virtual" > reinicie.

PASSO 2 — ATUALIZAR O DRIVER DE VÍDEO
  Driver antigo = travamentos e FPS baixo.
  - Intel:  intel.com/content/www/us/en/download-center
  - NVIDIA: nvidia.com.br/drivers   - AMD: amd.com/pt/support
  Em notebook, baixe preferencialmente do site do fabricante do notebook.

PASSO 3 — APLICAR O PRESET NA ABA "BLUESTACKS 5"
  1. FECHE o BlueStacks completamente antes de aplicar.
  2. Escolha a instância e o preset recomendado (a ferramenta sugere
     com base na sua RAM e CPU).
  3. Clique em "Aplicar preset" — um backup do bluestacks.conf é criado
     automaticamente ao lado do original (bluestacks.conf.bak-DATA).

  O que o preset muda:
  - CPU/RAM: nunca dê mais da metade da sua RAM ao emulador. Em PC de
    4 GB, 1,5 GB para o BlueStacks é o teto saudável.
  - Resolução 960x540 + DPI 160: a MAIOR fonte de FPS em PC fraco.
    O jogo renderiza 44% menos pixels que em 720p.
  - FPS 30: estável é melhor que 60 engasgado. Suba depois se sobrar folga.
  - Renderizador OpenGL (gl): mais compatível em GPU integrada antiga.
    Se tiver placa dedicada, teste DirectX (dx) no preset Desempenho.
  - Vsync desligado: menos input lag.

PASSO 4 — DENTRO DO BLUESTACKS (não dá para automatizar)
  Configurações (engrenagem) >
  - Desempenho > Modo de desempenho: "Jogos" (ou "Baixa memória" em
    PC de 4 GB).
  - Gráficos > Modo do motor gráfico: "Compatibilidade" se a tela
    piscar ou travar; "Desempenho" caso contrário.
  - Preferências > desmarque "Iniciar BlueStacks ao ligar o PC".
  - No Multi-Instance Manager: use UMA instância só. Cada instância
    extra consome a RAM configurada inteira.
  - Dentro do JOGO: gráficos no mínimo, 30 FPS, desligue sombras.

PASSO 5 — OTIMIZAR O WINDOWS (aba "Windows")
  Marque as opções e clique em Aplicar. Tudo que a ferramenta faz é
  reversível e o log diz como desfazer cada item.
  Dicas extras manuais:
  - Feche navegador/Discord enquanto joga (Chrome come RAM).
  - Antivírus de terceiros: adicione a pasta do BlueStacks às exceções.
  - Se o disco for HD mecânico, considere mover o BlueStacks para SSD
    (Configurações do BlueStacks > Dados do usuário).
  - Windows Update: deixe concluir e reinicie — atualização pendente
    rouba desempenho em segundo plano.

PROBLEMAS COMUNS
  - "Engine won't start": renderizador incompatível. Troque gl <-> dx
    (ou aplique o preset Ultra Leve, que usa gl).
  - Travou depois de mexer: restaure o backup — apague bluestacks.conf
    e renomeie o .bak-DATA mais recente para bluestacks.conf.
  - Tela preta no jogo: dentro do BlueStacks, Gráficos > ASTC ->
    "Decodificação por software".
================================================================
"@

# ---------------------------------------------------------------------------
# Interface gráfica
# ---------------------------------------------------------------------------
$specs = Get-HostSpecs
$confPath = Get-BsConfPath

$form = New-Object System.Windows.Forms.Form
$form.Text = 'BlueBoost — Otimizador para BlueStacks 5 (PC fraco)'
$form.Size = New-Object System.Drawing.Size(780, 640)
$form.MinimumSize = $form.Size
$form.StartPosition = 'CenterScreen'
$form.Font = New-Object System.Drawing.Font('Segoe UI', 9)

# --- painel de informações no topo -----------------------------------------
$lblSpecs = New-Object System.Windows.Forms.Label
$lblSpecs.Dock = 'Top'
$lblSpecs.Height = 58
$lblSpecs.Padding = New-Object System.Windows.Forms.Padding(10, 6, 10, 0)
$vtTxt = if ($specs.VtEnabled -eq $true) { 'ATIVADA' }
         elseif ($specs.VtEnabled -eq $false) { 'DESATIVADA (ative no BIOS! veja o Tutorial)' }
         else { 'não detectável (Hyper-V presente?)' }
$admTxt = if ($script:IsAdmin) { 'administrador' } else { 'SEM admin — algumas ações podem falhar' }
$lblSpecs.Text = ("Seu PC: {0}  |  {1} núcleos lógicos  |  {2} GB RAM ({3} GB livres)`r`n" +
                  "Virtualização (VT): {4}  |  Executando como: {5}") -f
                  $specs.CpuName, $specs.Cores, $specs.RamGB, $specs.FreeRamGB, $vtTxt, $admTxt

# --- log na parte de baixo ---------------------------------------------------
$txtLog = New-Object System.Windows.Forms.TextBox
$txtLog.Multiline = $true
$txtLog.ReadOnly = $true
$txtLog.ScrollBars = 'Vertical'
$txtLog.Dock = 'Bottom'
$txtLog.Height = 140
$txtLog.BackColor = [System.Drawing.Color]::FromArgb(18, 18, 24)
$txtLog.ForeColor = [System.Drawing.Color]::FromArgb(140, 220, 140)
$txtLog.Font = New-Object System.Drawing.Font('Consolas', 9)

function Write-Log([string]$Msg) {
    $txtLog.AppendText(('[{0}] {1}{2}' -f (Get-Date -Format 'HH:mm:ss'), $Msg, [Environment]::NewLine))
}

# --- abas --------------------------------------------------------------------
$tabs = New-Object System.Windows.Forms.TabControl
$tabs.Dock = 'Fill'

$tabBs  = New-Object System.Windows.Forms.TabPage; $tabBs.Text  = ' BlueStacks 5 '
$tabWin = New-Object System.Windows.Forms.TabPage; $tabWin.Text = ' Windows '
$tabTut = New-Object System.Windows.Forms.TabPage; $tabTut.Text = ' Tutorial '
$tabs.TabPages.AddRange(@($tabBs, $tabWin, $tabTut))

# ========================== ABA BLUESTACKS 5 =================================
$y = 14

$lblConf = New-Object System.Windows.Forms.Label
$lblConf.Location = New-Object System.Drawing.Point(14, $y)
$lblConf.Size = New-Object System.Drawing.Size(720, 20)
$lblConf.Text = if ($confPath) { "Arquivo de configuração: $confPath" }
                else { 'BlueStacks 5 NÃO encontrado neste PC (bluestacks.conf ausente). Instale o BlueStacks 5 primeiro.' }
$tabBs.Controls.Add($lblConf)
$y += 30

$lblInst = New-Object System.Windows.Forms.Label
$lblInst.Location = New-Object System.Drawing.Point(14, ($y + 3))
$lblInst.AutoSize = $true
$lblInst.Text = 'Instância:'
$tabBs.Controls.Add($lblInst)

$cmbInst = New-Object System.Windows.Forms.ComboBox
$cmbInst.Location = New-Object System.Drawing.Point(90, $y)
$cmbInst.Size = New-Object System.Drawing.Size(200, 24)
$cmbInst.DropDownStyle = 'DropDownList'
$tabBs.Controls.Add($cmbInst)

$btnVt = New-Object System.Windows.Forms.Button
$btnVt.Location = New-Object System.Drawing.Point(310, ($y - 1))
$btnVt.Size = New-Object System.Drawing.Size(130, 26)
$btnVt.Text = 'Verificar VT'
$tabBs.Controls.Add($btnVt)

$btnReload = New-Object System.Windows.Forms.Button
$btnReload.Location = New-Object System.Drawing.Point(450, ($y - 1))
$btnReload.Size = New-Object System.Drawing.Size(130, 26)
$btnReload.Text = 'Recarregar'
$tabBs.Controls.Add($btnReload)
$y += 40

$grpPreset = New-Object System.Windows.Forms.GroupBox
$grpPreset.Location = New-Object System.Drawing.Point(14, $y)
$grpPreset.Size = New-Object System.Drawing.Size(566, 120)
$grpPreset.Text = 'Preset'
$tabBs.Controls.Add($grpPreset)

$rbUltra = New-Object System.Windows.Forms.RadioButton
$rbUltra.Location = New-Object System.Drawing.Point(14, 24)
$rbUltra.Size = New-Object System.Drawing.Size(540, 26)
$rbUltra.Text = 'Ultra Leve — 2 CPUs, 1,5–2 GB RAM, 960x540, 30 FPS, OpenGL  (PC de 4 GB / dual-core)'

$rbEquil = New-Object System.Windows.Forms.RadioButton
$rbEquil.Location = New-Object System.Drawing.Point(14, 54)
$rbEquil.Size = New-Object System.Drawing.Size(540, 26)
$rbEquil.Text = 'Equilibrado — 2 GB RAM, 1280x720, 60 FPS, OpenGL  (PC de 6–8 GB)'

$rbPerf = New-Object System.Windows.Forms.RadioButton
$rbPerf.Location = New-Object System.Drawing.Point(14, 84)
$rbPerf.Size = New-Object System.Drawing.Size(540, 26)
$rbPerf.Text = 'Desempenho — 4 CPUs, 4 GB RAM, 1280x720, 60 FPS, DirectX  (PC de 8 GB ou mais)'

$grpPreset.Controls.AddRange(@($rbUltra, $rbEquil, $rbPerf))

$recommended = Get-RecommendedPreset $specs
switch ($recommended) {
    'UltraLeve'   { $rbUltra.Checked = $true; $rbUltra.Text += '   <- RECOMENDADO' }
    'Equilibrado' { $rbEquil.Checked = $true; $rbEquil.Text += '   <- RECOMENDADO' }
    'Desempenho'  { $rbPerf.Checked  = $true; $rbPerf.Text  += '   <- RECOMENDADO' }
}
$y += 132

$chkKill = New-Object System.Windows.Forms.CheckBox
$chkKill.Location = New-Object System.Drawing.Point(14, $y)
$chkKill.Size = New-Object System.Drawing.Size(560, 24)
$chkKill.Checked = $true
$chkKill.Text = 'Fechar processos do BlueStacks antes de aplicar (obrigatório para as mudanças valerem)'
$tabBs.Controls.Add($chkKill)
$y += 34

$btnApply = New-Object System.Windows.Forms.Button
$btnApply.Location = New-Object System.Drawing.Point(14, $y)
$btnApply.Size = New-Object System.Drawing.Size(280, 36)
$btnApply.Text = 'Aplicar preset (cria backup automático)'
$btnApply.BackColor = [System.Drawing.Color]::FromArgb(0, 120, 60)
$btnApply.ForeColor = [System.Drawing.Color]::White
$btnApply.FlatStyle = 'Flat'
$tabBs.Controls.Add($btnApply)

$btnShowCur = New-Object System.Windows.Forms.Button
$btnShowCur.Location = New-Object System.Drawing.Point(310, $y)
$btnShowCur.Size = New-Object System.Drawing.Size(270, 36)
$btnShowCur.Text = 'Mostrar configuração atual da instância'
$tabBs.Controls.Add($btnShowCur)

# --- lógica da aba BlueStacks ---
$BsKeys = @('cpus','ram','fb_width','fb_height','dpi','graphics_renderer',
            'max_fps','enable_high_fps','enable_vsync','show_sidebar','enable_fps_display')

function Update-BsInstances {
    $cmbInst.Items.Clear()
    if (-not $script:confPath) { return }
    try {
        $lines = Read-BsConf $script:confPath
        foreach ($i in (Get-BsInstances $lines)) { [void]$cmbInst.Items.Add($i) }
        if ($cmbInst.Items.Count -gt 0) { $cmbInst.SelectedIndex = 0 }
        Write-Log ("Instâncias encontradas: " + ($(if ($cmbInst.Items.Count) { ($cmbInst.Items -join ', ') } else { 'nenhuma' })))
    } catch {
        Write-Log "ERRO ao ler bluestacks.conf: $($_.Exception.Message)"
    }
}

$btnReload.Add_Click({
    $script:confPath = Get-BsConfPath
    $lblConf.Text = if ($script:confPath) { "Arquivo de configuração: $script:confPath" }
                    else { 'BlueStacks 5 NÃO encontrado neste PC (bluestacks.conf ausente).' }
    Update-BsInstances
})

$btnVt.Add_Click({
    $s = Get-HostSpecs
    if ($s.VtEnabled -eq $true) {
        Write-Log 'VT: ATIVADA no firmware. Perfeito!'
    } elseif ($s.VtEnabled -eq $false) {
        Write-Log 'VT: DESATIVADA. Ative no BIOS (veja o passo 1 do Tutorial) — faz MUITA diferença.'
    } else {
        Write-Log 'VT: não detectável diretamente (um hypervisor como Hyper-V está presente).'
    }
    if ($s.HypervisorOn) {
        Write-Log 'Hyper-V/hypervisor presente: em PC fraco, desative-o para o BlueStacks usar VT nativa (Tutorial, passo 1).'
    }
})

$btnShowCur.Add_Click({
    if (-not $script:confPath -or $cmbInst.SelectedItem -eq $null) { Write-Log 'Nenhuma instância selecionada.'; return }
    $lines = Read-BsConf $script:confPath
    $inst = [string]$cmbInst.SelectedItem
    Write-Log "Configuração atual da instância '$inst':"
    foreach ($k in $BsKeys) {
        $v = Get-BsValue $lines "bst.instance.$inst.$k"
        if ($null -ne $v) { Write-Log ("   {0} = {1}" -f $k, $v) }
    }
})

$btnApply.Add_Click({
    if (-not $script:confPath) { Write-Log 'BlueStacks 5 não encontrado — nada para aplicar.'; return }
    if ($cmbInst.SelectedItem -eq $null) { Write-Log 'Selecione uma instância primeiro.'; return }

    $presetName = if ($rbUltra.Checked) { 'UltraLeve' } elseif ($rbEquil.Checked) { 'Equilibrado' } else { 'Desempenho' }
    $inst = [string]$cmbInst.SelectedItem

    $confirm = [System.Windows.Forms.MessageBox]::Show(
        "Aplicar o preset '$presetName' na instância '$inst'?`n`nUm backup do bluestacks.conf será criado antes.",
        'BlueBoost', 'YesNo', 'Question')
    if ($confirm -ne 'Yes') { return }

    try {
        if ($chkKill.Checked) {
            $k = Stop-BlueStacks
            if ($k.Count -gt 0) { Write-Log ("Processos encerrados: " + ($k -join ', ')) }
            else { Write-Log 'Nenhum processo do BlueStacks em execução.' }
        }

        $bak = Backup-BsConf $script:confPath
        Write-Log "Backup criado: $bak"

        $lines = Read-BsConf $script:confPath
        $preset = Get-BsPreset $presetName $specs
        $changed = 0
        foreach ($k in $preset.Keys) {
            $res = Set-BsValue $lines "bst.instance.$inst.$k" $preset[$k]
            if ($res -ne 'igual') { $changed++ }
            Write-Log ("   {0} = {1}  ({2})" -f $k, $preset[$k], $res)
        }
        Save-BsConf $script:confPath $lines
        Write-Log "Preset '$presetName' aplicado ($changed chaves alteradas). Abra o BlueStacks e confira."
        Write-Log 'Dica: dentro do BlueStacks, confira também Modo de desempenho = Jogos (Tutorial, passo 4).'
    } catch {
        Write-Log "ERRO ao aplicar: $($_.Exception.Message)"
        Write-Log 'Se for acesso negado, execute o BlueBoost como administrador.'
    }
})

# ============================= ABA WINDOWS ===================================
$wy = 14
$lblWin = New-Object System.Windows.Forms.Label
$lblWin.Location = New-Object System.Drawing.Point(14, $wy)
$lblWin.Size = New-Object System.Drawing.Size(720, 20)
$lblWin.Text = 'Otimizações leves e reversíveis. Marque o que quiser e clique em Aplicar — o log explica como desfazer cada uma.'
$tabWin.Controls.Add($lblWin)
$wy += 32

$tweaks = @(
    @{ Text = 'Plano de energia: Alto Desempenho (CPU não reduz velocidade em jogo)'; Fn = { Invoke-TweakPowerPlan }   },
    @{ Text = 'Desativar Game DVR / captura da Game Bar + ativar Modo de Jogo';       Fn = { Invoke-TweakGameDvr }     },
    @{ Text = 'Efeitos visuais do Windows em "Melhor desempenho"';                    Fn = { Invoke-TweakVisualEffects } },
    @{ Text = 'Desativar transparência do Windows';                                   Fn = { Invoke-TweakTransparency } },
    @{ Text = 'Limpar arquivos temporários (TEMP do usuário' + $(if ($script:IsAdmin) { ' e do Windows)' } else { ')' }); Fn = { Invoke-TweakCleanTemp } },
    @{ Text = 'Abrir gerenciador de programas de inicialização (para desativar manualmente)'; Fn = { Invoke-TweakStartupInfo } }
)

$chkTweaks = @()
foreach ($t in $tweaks) {
    $c = New-Object System.Windows.Forms.CheckBox
    $c.Location = New-Object System.Drawing.Point(14, $wy)
    $c.Size = New-Object System.Drawing.Size(700, 26)
    $c.Text = $t.Text
    $c.Checked = $true
    $c.Tag = $t.Fn
    $tabWin.Controls.Add($c)
    $chkTweaks += $c
    $wy += 30
}
# o último (inicialização) desmarcado por padrão: abre janela
$chkTweaks[-1].Checked = $false
$wy += 10

$btnWinApply = New-Object System.Windows.Forms.Button
$btnWinApply.Location = New-Object System.Drawing.Point(14, $wy)
$btnWinApply.Size = New-Object System.Drawing.Size(280, 36)
$btnWinApply.Text = 'Aplicar otimizações selecionadas'
$btnWinApply.BackColor = [System.Drawing.Color]::FromArgb(0, 90, 160)
$btnWinApply.ForeColor = [System.Drawing.Color]::White
$btnWinApply.FlatStyle = 'Flat'
$tabWin.Controls.Add($btnWinApply)

$btnWinApply.Add_Click({
    $any = $false
    foreach ($c in $chkTweaks) {
        if (-not $c.Checked) { continue }
        $any = $true
        try {
            $msg = & $c.Tag
            Write-Log "OK: $msg"
        } catch {
            Write-Log "FALHOU ($($c.Text)): $($_.Exception.Message)"
        }
    }
    if (-not $any) { Write-Log 'Nenhuma otimização selecionada.' }
    else { Write-Log 'Pronto. Reinicie o PC para garantir que tudo foi aplicado.' }
})

# ============================ ABA TUTORIAL ===================================
$txtTut = New-Object System.Windows.Forms.TextBox
$txtTut.Multiline = $true
$txtTut.ReadOnly = $true
$txtTut.ScrollBars = 'Vertical'
$txtTut.Dock = 'Fill'
$txtTut.Font = New-Object System.Drawing.Font('Consolas', 9.5)
$txtTut.BackColor = [System.Drawing.Color]::FromArgb(250, 250, 245)
$txtTut.Text = $TutorialText -replace "`n", "`r`n"
$tabTut.Controls.Add($txtTut)

# --- montar e iniciar ---------------------------------------------------------
$form.Controls.Add($tabs)
$form.Controls.Add($txtLog)
$form.Controls.Add($lblSpecs)

$script:confPath = $confPath
Write-Log 'BlueBoost iniciado.'
if (-not $script:IsAdmin) { Write-Log 'Aviso: rodando SEM administrador — gravar o bluestacks.conf pode falhar.' }
if ($confPath) { Update-BsInstances } else { Write-Log 'bluestacks.conf não encontrado — a aba BlueStacks 5 fica limitada.' }
Write-Log ("Preset recomendado para este PC: " + (Get-RecommendedPreset $specs))

[void]$form.ShowDialog()
