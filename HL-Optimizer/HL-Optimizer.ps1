<#
================================================================================
  ██╗  ██╗██╗         ██████╗ ██████╗ ████████╗██╗███╗   ███╗██╗███████╗███████╗██████╗
  ██║  ██║██║        ██╔═══██╗██╔══██╗╚══██╔══╝██║████╗ ████║██║╚══███╔╝██╔════╝██╔══██╗
  ███████║██║        ██║   ██║██████╔╝   ██║   ██║██╔████╔██║██║  ███╔╝ █████╗  ██████╔╝
  ██╔══██║██║        ██║   ██║██╔═══╝    ██║   ██║██║╚██╔╝██║██║ ███╔╝  ██╔══╝  ██╔══██╗
  ██║  ██║███████╗   ╚██████╔╝██║        ██║   ██║██║ ╚═╝ ██║██║███████╗███████╗██║  ██║
  ╚═╝  ╚═╝╚══════╝    ╚═════╝ ╚═╝        ╚═╝   ╚═╝╚═╝     ╚═╝╚═╝╚══════╝╚══════╝╚═╝  ╚═╝
--------------------------------------------------------------------------------
  MARCA / PROPRIETÁRIA DESTE ARQUIVO : HL
  PRODUTO  : HL Optimizer
  ARQUIVO  : HL-Optimizer.ps1
  VERSÃO   : 1.0.0
  MOTOR    : Windows PowerShell 5.1 (nativo, offline, sem módulos externos)
  FOCO     : FPS médio, 1% low (stutter) e tempo de carregamento em jogos e
             emuladores (Android: BlueStacks/LDPlayer/MuMu/MEmu | Console:
             PCSX2/Dolphin/RPCS3/Ryujinx).
--------------------------------------------------------------------------------
  AVISO: este script ALTERA serviços, registro e plano de energia.
         Sempre rode primeiro:  .\HL-Optimizer.ps1 -DryRun
         Leia os logs em .\_logs antes de aplicar.
         Reversão total:  HL-Reverter.cmd   (-Revert)
================================================================================
#>
#Requires -Version 5.1
[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [ValidateSet('Safe', 'Balanced', 'Extreme')]
    [string]$Profile = 'Balanced',

    [switch]$EmulatorFocus,
    [switch]$IncludeExperimental,
    [switch]$Revert,
    [string]$BackupPath,
    [switch]$DryRun,
    [switch]$NonInteractive,
    [switch]$SkipRestorePoint,
    [switch]$NoPanel,
    [string]$LogDir
)

# ==============================================================================
#  0. IDENTIDADE DA MARCA (HL Optimizer)
# ==============================================================================
$script:Brand          = 'HL Optimizer'
$script:BrandShort     = 'HL'
$script:BrandVersion   = '1.0.0'
$script:BrandBuild     = '2026.10'
$script:BrandTag       = 'FPS / Games / Emulators'
$script:BrandOwner     = 'HL'
$script:Root           = Split-Path -Parent $MyInvocation.MyCommand.Path
if (-not $script:Root) { $script:Root = (Get-Location).Path }

if (-not $LogDir) { $LogDir = Join-Path $script:Root '_logs' }
$script:LogDir      = $LogDir
$script:BackupRoot  = Join-Path $script:Root '_HL-Backup'
$script:LogFile     = $null
$script:BackupDir   = $null
$script:Changes     = New-Object System.Collections.ArrayList
$script:DryRun      = [bool]$DryRun
$script:Interactive = -not $NonInteractive
$script:RebootNeeded = $false
$script:Reverting    = $false
$script:SessionId   = Get-Date -Format 'yyyy-MM-dd_HH-mm-ss'
$script:PanelWidth  = 78
$script:EmulatorCache = $null

if ($WhatIfPreference) { $script:DryRun = $true }

# Serviços que NUNCA podem ser tocados (aborta o script se tentarem)
$script:NeverTouch = @(
    'RpcSs', 'RpcEptMapper', 'DcomLaunch', 'PlugPlay', 'Power', 'BrokerInfrastructure',
    'SystemEventsBroker', 'EventLog', 'Winmgmt', 'Themes', 'UxSms', 'Dwm', 'AudioSrv',
    'AudioEndpointBuilder', 'CryptSvc', 'ProfSvc', 'UserManager', 'LanmanWorkstation',
    'Dhcp', 'Dnscache', 'BFE', 'mpssvc', 'gpsvc', 'Schedule', 'SamSs', 'LSM', 'nsi',
    'TrustedInstaller', 'wuauserv', 'NlaSvc', 'netprofm', 'WinDefend', 'DeviceInstall'
)

# Padrões de serviços de fabricante que causam stutter (somente parar/desabilitar)
$script:BloatwarePatterns = @(
    'NahimicService', 'Nahimic', 'SonicStudio', 'SonicRadar', 'ArmouryCrate',
    'LightingService', 'AuraService', 'iCUE', 'CorsairService', 'Razer',
    'LGHUB', 'LogiRegistryService', 'RTCore64', 'MSIAfterburner', 'NZXT',
    'DragonCenter', 'MSI_', 'GFNService', 'AsusUpdateCheck'
)

# Caminhos de emuladores conhecidos
$script:EmulatorCatalog = @(
    @{ Name = 'BlueStacks 5'; Exe = 'HD-Player.exe';  Paths = @("$env:ProgramFiles\BlueStacks_nxt", "$env:ProgramFiles\BlueStacks", "${env:ProgramFiles(x86)}\BlueStacks_nxt") },
    @{ Name = 'LDPlayer 9';   Exe = 'dnplayer.exe';   Paths = @("$env:ProgramFiles\LDPlayer\LDPlayer9", 'C:\LDPlayer\LDPlayer9', 'D:\LDPlayer\LDPlayer9') },
    @{ Name = 'MuMu Player';  Exe = 'MuMuPlayer.exe'; Paths = @("$env:ProgramFiles\Netease\MuMuPlayer-12.0", 'C:\Program Files\Netease\MuMuPlayer-12.0') },
    @{ Name = 'MEmu';         Exe = 'MEmu.exe';       Paths = @("$env:ProgramFiles\Microvirt\MEmu", 'C:\Program Files\Microvirt\MEmu') },
    @{ Name = 'PCSX2';        Exe = 'pcsx2-qt.exe';   Paths = @("$env:ProgramFiles\PCSX2", "$env:LOCALAPPDATA\Programs\PCSX2") },
    @{ Name = 'Dolphin';      Exe = 'Dolphin.exe';    Paths = @("$env:ProgramFiles\Dolphin", "$env:LOCALAPPDATA\Dolphin") },
    @{ Name = 'RPCS3';        Exe = 'rpcs3.exe';      Paths = @("$env:ProgramFiles\RPCS3", 'C:\RPCS3') },
    @{ Name = 'Ryujinx';      Exe = 'Ryujinx.exe';    Paths = @("$env:APPDATA\Ryujinx", "$env:ProgramFiles\Ryujinx") }
)

# ==============================================================================
#  1. UI / LOG / PAINEL
# ==============================================================================
function Get-PanelWidth {
    $w = 78
    try {
        $cw = [Console]::WindowWidth
        if ($cw -gt 20 -and ($cw - 2) -lt $w) { $w = $cw - 2 }
    } catch { }
    if ($w -lt 50) { $w = 50 }
    return $w
}

function Format-Center {
    param([string]$Text, [int]$Width)
    if ($null -eq $Text) { $Text = '' }
    if ($Text.Length -ge $Width) { return $Text.Substring(0, $Width) }
    $left = [int](($Width - $Text.Length) / 2)
    return (' ' * $left) + $Text + (' ' * ($Width - $Text.Length - $left))
}

function Write-Log {
    param(
        [Parameter(Mandatory = $true)][string]$Message,
        [ValidateSet('INFO', 'OK', 'WARN', 'ERRO', 'ACAO', 'VERIF', 'DRYRUN', 'TITULO')]
        [string]$Level = 'INFO'
    )
    $ts = Get-Date -Format 'yyyy-MM-dd HH:mm:ss'
    $colors = @{ INFO = 'Gray'; OK = 'Green'; WARN = 'Yellow'; ERRO = 'Red'; ACAO = 'Cyan'; VERIF = 'DarkGray'; DRYRUN = 'Magenta'; TITULO = 'White' }
    $line = '[' + $ts + '] [' + $Level.PadRight(6) + '] ' + $Message

    if ($script:Interactive -or $Level -in @('WARN', 'ERRO', 'TITULO')) {
        Write-Host ('  ' + $line) -ForegroundColor $colors[$Level]
    }
    if ($script:LogFile) {
        try { Add-Content -LiteralPath $script:LogFile -Value $line -Encoding UTF8 -ErrorAction Stop } catch { }
    }
}

function Write-Line {
    param([string]$Char = '─', [string]$Color = 'DarkCyan')
    $w = Get-PanelWidth
    Write-Host ('  ' + ($Char * ($w - 2))) -ForegroundColor $Color
}

function Write-Section {
    param([string]$Title)
    Write-Host ''
    Write-Host ('  ┌─ ' + $Title.ToUpper() + ' ') -ForegroundColor Cyan
    Write-Line '─'
}

function Write-PanelRow {
    param([string]$Text, [string]$Color = 'Gray')
    $w = Get-PanelWidth
    $inner = $w - 4
    if ($Text.Length -gt $inner) { $Text = $Text.Substring(0, $inner) }
    Write-Host ('║ ' + $Text.PadRight($inner) + ' ║') -ForegroundColor $Color
}

function Format-Badge {
    param([string]$Label, [string]$Value)
    $dots = 26 - $Label.Length
    if ($dots -lt 2) { $dots = 2 }
    return (' ▪ ' + $Label + ' ' + ('.' * $dots) + ' ' + $Value)
}

function Show-Pause {
    if (-not $script:Interactive) { return }
    Write-Host ''
    Write-Host '  Pressione ENTER para voltar ao painel...' -ForegroundColor DarkGray
    $null = Read-Host
}

# ==============================================================================
#  2. PAINEL DE INÍCIO (BRANDING HL OPTIMIZER)
# ==============================================================================
function Show-BrandPanel {
    # atualiza o snapshot para os badges refletirem o estado AGORA
    if (Get-Command Get-SystemSnapshot -ErrorAction SilentlyContinue) { $script:SysInfo = Get-SystemSnapshot }
    $w = Get-PanelWidth
    $inner = $w - 4
    $edge = '═' * ($w - 2)

    Write-Host ''
    Write-Host ('╔' + $edge + '╗') -ForegroundColor DarkCyan
    Write-Host ('║ ' + (Format-Center 'H  L     O  P  T  I  M  I  Z  E  R' $inner) + ' ║') -ForegroundColor Cyan
    Write-Host ('║ ' + (Format-Center ($script:BrandTag + '   |   v' + $script:BrandVersion + ' build ' + $script:BrandBuild) $inner) + ' ║') -ForegroundColor DarkCyan
    Write-Host ('╟' + ('─' * ($w - 2)) + '╢') -ForegroundColor DarkCyan

    $os = '?'; try { $os = 'Windows ' + (Get-CimInstance Win32_OperatingSystem -ErrorAction Stop).Caption.Replace('Microsoft ', '') } catch { }
    Write-PanelRow (' Host: ' + $env:COMPUTERNAME + '  |  User: ' + $env:USERNAME) 'DarkGray'
    Write-PanelRow (' OS  : ' + $os + '  |  Build: ' + [Environment]::OSVersion.Version.Build) 'DarkGray'

    Write-Host ('╟' + ('─' * ($w - 2)) + '╢') -ForegroundColor DarkCyan

    # ---- badges de saúde do sistema ----
    $b = Get-HealthBadges
    foreach ($badge in $b) {
        $stateColors = @{ good = 'Green'; warn = 'Yellow'; bad = 'Red' }
        $color = 'Gray'
        if ($stateColors.ContainsKey([string]$badge.State)) { $color = $stateColors[[string]$badge.State] }
        Write-PanelRow (Format-Badge $badge.Label $badge.Value) $color
    }

    Write-Host ('╟' + ('─' * ($w - 2)) + '╢') -ForegroundColor DarkCyan
    $last = Get-LatestBackup
    $bk = 'nenhum backup ainda'
    if ($last) { $bk = (Split-Path -Leaf $last.FullName) }
    Write-PanelRow (' Backup .......... ' + $bk) 'DarkGray'
    Write-PanelRow (' Sessao .......... ' + $script:SessionId + '   |   DryRun: ' + $(if ($script:DryRun) { 'SIM' } else { 'nao' })) 'DarkGray'
    Write-Host ('╚' + $edge + '╝') -ForegroundColor DarkCyan
}

function Get-HealthBadges {
    $list = New-Object System.Collections.ArrayList
    $v = $script:SysInfo

    # Virtualização (VT-x / SVM)
    if ($v.VirtualizationFirmware) {
        $null = $list.Add(@{ Label = 'Virtualizacao (VT-x/SVM)'; Value = 'LIGADA'; State = 'good' })
    } else {
        $null = $list.Add(@{ Label = 'Virtualizacao (VT-x/SVM)'; Value = 'DESLIGADA (BIOS!)'; State = 'bad' })
    }

    # Hypervisor / Hyper-V
    if ($v.HypervisorPresent) {
        $null = $list.Add(@{ Label = 'Hyper-V / hypervisor'; Value = 'ATIVO (prejudica emulador)'; State = 'warn' })
    } else {
        $null = $list.Add(@{ Label = 'Hyper-V / hypervisor'; Value = 'DESLIGADO'; State = 'good' })
    }

    # VBS / HVCI
    if ($v.VbsStatus -eq 2) {
        $null = $list.Add(@{ Label = 'VBS / HVCI (Isolamento)'; Value = 'ATIVO (-5..15% CPU)'; State = 'warn' })
    } elseif ($v.VbsStatus -eq 1) {
        $null = $list.Add(@{ Label = 'VBS / HVCI (Isolamento)'; Value = 'habilitado, parado'; State = 'warn' })
    } else {
        $null = $list.Add(@{ Label = 'VBS / HVCI (Isolamento)'; Value = 'DESLIGADO'; State = 'good' })
    }

    # Game DVR
    if ($v.GameDvr) {
        $null = $list.Add(@{ Label = 'Game DVR / Capturas'; Value = 'ATIVO (-5..15%)'; State = 'warn' })
    } else {
        $null = $list.Add(@{ Label = 'Game DVR / Capturas'; Value = 'DESLIGADO'; State = 'good' })
    }

    # Plano de energia
    $null = $list.Add(@{ Label = 'Plano de energia'; Value = $v.PowerName; State = $(if ($v.PowerName -match 'Alto|Ultimate|High|Desempenho') { 'good' } else { 'warn' }) })

    # Disco do sistema
    $null = $list.Add(@{ Label = 'Disco do sistema (C:)'; Value = $v.DiskType; State = $(if ($v.DiskType -eq 'SSD') { 'good' } else { 'warn' }) })

    # Emuladores
    if ($v.Emulators.Count -gt 0) {
        $null = $list.Add(@{ Label = 'Emuladores detectados'; Value = ($v.Emulators -join ', '); State = 'good' })
    } else {
        $null = $list.Add(@{ Label = 'Emuladores detectados'; Value = 'nenhum'; State = 'Gray' })
    }

    # Defender: pasta de emulador excluída?
    if ($v.DefenderExclusions -gt 0) {
        $null = $list.Add(@{ Label = 'Exclusoes no Defender'; Value = ($v.DefenderExclusions.ToString() + ' caminho(s)'); State = 'good' })
    } else {
        $null = $list.Add(@{ Label = 'Exclusoes no Defender'; Value = 'nenhuma'; State = 'warn' })
    }

    return $list
}

# ==============================================================================
#  3. AMBIENTE / PRIVILÉGIOS / BACKUP
# ==============================================================================
function Assert-Admin {
    param([switch]$Silent)
    $id = [Security.Principal.WindowsIdentity]::GetCurrent()
    $pr = New-Object Security.Principal.WindowsPrincipal($id)
    if (-not $pr.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        if (-not $Silent) {
            Write-Host ''
            Write-Host '  [X] HL Optimizer precisa ser executado como ADMINISTRADOR.' -ForegroundColor Red
            Write-Host '      Feche esta janela e use HL-Optimizer.cmd (eleva automaticamente).' -ForegroundColor Yellow
        }
        return $false
    }
    return $true
}

function Initialize-HLEnvironment {
    foreach ($d in @($script:LogDir, $script:BackupRoot)) {
        if (-not (Test-Path -LiteralPath $d)) {
            try { $null = New-Item -ItemType Directory -Path $d -Force -ErrorAction Stop } catch { }
        }
    }
    $suffix = ''
    if ($script:DryRun) { $suffix = '_dryrun' }
    $script:LogFile = Join-Path $script:LogDir ('hl-optimizer_' + $script:SessionId + $suffix + '.log')
    $head = @(
        '==================================================================',
        ' HL OPTIMIZER v' + $script:BrandVersion + '  -  ' + $script:BrandTag,
        ' Proprietario da marca: ' + $script:BrandOwner,
        ' Host: ' + $env:COMPUTERNAME + '   Usuario: ' + $env:USERNAME,
        ' Perfil: ' + $Profile + '   EmulatorFocus: ' + [bool]$EmulatorFocus + '   Experimental: ' + [bool]$IncludeExperimental,
        '=================================================================='
    )
    foreach ($h in $head) { try { Add-Content -LiteralPath $script:LogFile -Value $h -Encoding UTF8 -ErrorAction Stop } catch { $script:LogFile = $null; break } }
    try { $Host.UI.RawUI.WindowTitle = $script:Brand + ' v' + $script:BrandVersion + ' - ' + $script:BrandTag } catch { }
    try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch { }

    $script:SysInfo = Get-SystemSnapshot
}

function Get-SystemSnapshot {
    $info = [ordered]@{
        OS = $env:OS; Machine = $env:COMPUTERNAME; User = $env:USERNAME
        VirtualizationFirmware = $false; HypervisorPresent = $false
        VbsStatus = -1; HvciRunning = $false; GameDvr = $false; Hags = -1
        PowerGuid = ''; PowerName = '?'; DiskType = 'Desconhecido'
        Emulators = @(); DefenderExclusions = 0; RamGb = 0; Cpu = ''
    }
    try { $cs = Get-CimInstance Win32_ComputerSystem -ErrorAction Stop; $info.HypervisorPresent = [bool]$cs.HypervisorPresent; $info.RamGb = [math]::Round($cs.TotalPhysicalMemory / 1GB, 1) } catch { }
    try { $cpu = Get-CimInstance Win32_Processor -ErrorAction Stop | Select-Object -First 1; $info.Cpu = $cpu.Name; $info.VirtualizationFirmware = [bool]$cpu.VirtualizationFirmwareEnabled } catch { }

    $vbs = Get-VbsStatus
    $info.VbsStatus = $vbs.Status; $info.HvciRunning = $vbs.Hvci

    $info.GameDvr = (Get-GameDvrStatus)
    $info.Hags = (Get-HagsStatus)

    $p = Get-ActivePowerScheme
    $info.PowerGuid = $p.Guid; $info.PowerName = $p.Name
    $info.DiskType = (Get-DiskType -DriveLetter 'C')
    $info.Emulators = @(Get-InstalledEmulators | ForEach-Object { $_.Name })
    $info.DefenderExclusions = (Get-DefenderExclusionCount)
    return [pscustomobject]$info
}

function New-BackupSession {
    if ($script:DryRun) { return }
    if ($script:BackupDir) { return }
    $script:BackupDir = Join-Path $script:BackupRoot $script:SessionId
    try { $null = New-Item -ItemType Directory -Path $script:BackupDir -Force -ErrorAction Stop } catch {
        Write-Log ('Nao foi possivel criar pasta de backup: ' + $_.Exception.Message) 'ERRO'
        throw 'BACKUP_FALHOU'
    }
    $meta = [ordered]@{
        brand = $script:Brand; version = $script:BrandVersion; session = $script:SessionId
        createdUtc = (Get-Date).ToUniversalTime().ToString('o'); profile = $Profile
        host = [ordered]@{ name = $env:COMPUTERNAME; os = [Environment]::OSVersion.VersionString; ps = $PSVersionTable.PSVersion.ToString() }
        changes = @(); defenderExclusionsAdded = @(); powerSchemeBefore = ''; hypervisorBefore = ''; rebootRequired = $false
    }
    Save-BackupSession -Meta $meta
    Write-Log ('Backup iniciado em ' + $script:BackupDir) 'INFO'
}

function Get-BackupObject {
    $file = Join-Path $script:BackupDir 'state.json'
    if (-not (Test-Path -LiteralPath $file)) { return $null }
    try { return (Get-Content -LiteralPath $file -Raw -Encoding UTF8 | ConvertFrom-Json) } catch { return $null }
}

function Save-BackupSession {
    param($Meta)
    if ($script:DryRun -or -not $script:BackupDir) { return }
    try {
        if (-not $Meta) { $Meta = Get-BackupObject }
        if (-not $Meta) { return }
        $Meta.changes = @($script:Changes)
        $Meta.rebootRequired = $script:RebootNeeded
        $json = $Meta | ConvertTo-Json -Depth 8
        Set-Content -LiteralPath (Join-Path $script:BackupDir 'state.json') -Value $json -Encoding UTF8
        # CSV legível
        $rows = @()
        foreach ($c in $script:Changes) {
            $rows += [pscustomobject]@{
                id = $c.id; tipo = $c.type; alvo = $c.target
                antes = ($c.before.GetEnumerator() | ForEach-Object { $_.Key + '=' + $_.Value }) -join '; '
                depois = ($c.after.GetEnumerator() | ForEach-Object { $_.Key + '=' + $_.Value }) -join '; '
                ganho = $c.gain; risco = $c.risk
            }
        }
        if ($rows.Count -gt 0) { $rows | Export-Csv -LiteralPath (Join-Path $script:BackupDir 'state.csv') -NoTypeInformation -Encoding UTF8 }
    } catch { Write-Log ('Falha ao salvar backup: ' + $_.Exception.Message) 'ERRO' }
}

function Save-BackupEntry {
    param(
        [string]$Type, [string]$Target, [hashtable]$Before, [hashtable]$After,
        [string]$Gain = '', [string]$Risk = '', [string]$RevertHint = '', [bool]$RequiresReboot = $false
    )
    $entry = [ordered]@{
        id = ($script:Changes.Count + 1); type = $Type; target = $Target
        before = $Before; after = $After; verified = $true
        requiresReboot = $RequiresReboot; gain = $Gain; risk = $Risk
        revertHint = $RevertHint; time = (Get-Date).ToString('s')
    }
    $null = $script:Changes.Add([pscustomobject]$entry)
    if ($RequiresReboot) { $script:RebootNeeded = $true }
    if (-not $script:DryRun) {
        if (-not $script:BackupDir) { New-BackupSession }
        Save-BackupSession
    }
}

function Get-LatestBackup {
    if (-not (Test-Path -LiteralPath $script:BackupRoot)) { return $null }
    return (Get-ChildItem -LiteralPath $script:BackupRoot -Directory -ErrorAction SilentlyContinue |
        Where-Object { Test-Path (Join-Path $_.FullName 'state.json') } |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1)
}

# ==============================================================================
#  4. REGISTRO (leitura segura, distingue AUSENTE de vazio)
# ==============================================================================
function Get-RegValueState {
    param([Parameter(Mandatory = $true)][string]$Path, [Parameter(Mandatory = $true)][string]$Name)
    $res = [pscustomobject]@{ Exists = $false; Value = $null; Type = 'ABSENT' }
    try {
        if (-not (Test-Path -LiteralPath $Path)) { return $res }
        $key = Get-Item -LiteralPath $Path -ErrorAction Stop
        if ($key.GetValueNames() -notcontains $Name) { return $res }
        $res.Exists = $true
        $res.Type = [string]$key.GetValueKind($Name)
        $res.Value = $key.GetValue($Name, $null, [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)
    } catch { }
    return $res
}

function Set-RegValueSafe {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)]$Value,
        [ValidateSet('DWord', 'QWord', 'String', 'ExpandString', 'MultiString')][string]$Type = 'DWord',
        [string]$Gain = '', [string]$Risk = '', [switch]$RequiresReboot
    )
    $before = Get-RegValueState -Path $Path -Name $Name
    if ($before.Exists -and ("$($before.Value)" -eq "$Value") -and ($before.Type -eq $Type)) {
        Write-Log ('Registro ja aplicado: ' + $Name + ' = ' + $Value) 'VERIF'
        return
    }
    if ($script:DryRun) {
        Write-Log ('[DRY-RUN] faria: ' + $Name + ' : ' + $before.Type + '/' + $before.Value + ' -> ' + $Type + '/' + $Value + '  (' + $Path + ')') 'DRYRUN'
        return
    }
    try {
        if (-not (Test-Path -LiteralPath $Path)) { $null = New-Item -Path $Path -Force -ErrorAction Stop }
        $null = New-ItemProperty -LiteralPath $Path -Name $Name -Value $Value -PropertyType $Type -Force -ErrorAction Stop
        $after = Get-RegValueState -Path $Path -Name $Name
        $ok = ($after.Exists -and ("$($after.Value)" -eq "$Value"))
        Write-Log ('Registro ' + $Name + ': ' + $before.Type + '/' + $before.Value + ' -> ' + $after.Type + '/' + $after.Value + $(if ($ok) { '  [OK]' } else { '  [FALHOU]' })) $(if ($ok) { 'OK' } else { 'ERRO' })
        if ($ok) {
            Save-BackupEntry -Type 'Registry' -Target ($Path + '\' + $Name) `
                -Before @{ exists = $before.Exists; value = "$($before.Value)"; type = $before.Type } `
                -After @{ exists = $after.Exists; value = "$($after.Value)"; type = $after.Type } `
                -Gain $Gain -Risk $Risk -RevertHint ('Remove-ItemProperty -LiteralPath "' + $Path + '" -Name "' + $Name + '"') `
                -RequiresReboot ([bool]$RequiresReboot)
        } else { Write-Log ('Registro nao confirmou o novo valor (pode estar protegido): ' + $Path + '\' + $Name) 'WARN' }
    } catch {
        Write-Log ('Falha ao escrever registro ' + $Name + ': ' + $_.Exception.Message) 'ERRO'
    }
}

function Remove-RegValueSafe {
    param([string]$Path, [string]$Name, [string]$Gain = '', [string]$Risk = '')
    $before = Get-RegValueState -Path $Path -Name $Name
    if (-not $before.Exists) { Write-Log ('Registro ja ausente: ' + $Name) 'VERIF'; return }
    if ($script:DryRun) { Write-Log ('[DRY-RUN] removeria valor ' + $Path + '\' + $Name) 'DRYRUN'; return }
    try {
        Remove-ItemProperty -LiteralPath $Path -Name $Name -Force -ErrorAction Stop
        $after = Get-RegValueState -Path $Path -Name $Name
        Write-Log ('Removido valor de registro ' + $Name + ': ' + $before.Value + ' -> ' + $(if ($after.Exists) { 'AINDA EXISTE' } else { 'ausente [OK]' })) 'OK'
        Save-BackupEntry -Type 'Registry' -Target ($Path + '\' + $Name) `
            -Before @{ exists = $before.Exists; value = "$($before.Value)"; type = $before.Type } `
            -After @{ exists = $false; value = ''; type = 'ABSENT' } -Gain $Gain -Risk $Risk
    } catch { Write-Log ('Falha ao remover valor ' + $Name + ': ' + $_.Exception.Message) 'ERRO' }
}

# ==============================================================================
#  5. SERVIÇOS (via sc.exe: mais confiável que Set-Service no PS 5.1)
# ==============================================================================
function Get-ServiceState {
    param([Parameter(Mandatory = $true)][string]$Name)
    $res = [pscustomobject]@{ Name = $Name; Found = $false; StartType = 'ABSENT'; Status = 'ABSENT'; Delayed = $false }
    try {
        $svc = Get-Service -Name $Name -ErrorAction Stop
        $res.Found = $true
        $res.Status = [string]$svc.Status
        $reg = 'HKLM:\SYSTEM\CurrentControlSet\Services\' + $Name
        $start = Get-RegValueState -Path $reg -Name 'Start'
        $delayed = Get-RegValueState -Path $reg -Name 'DelayedAutostart'
        switch ("$($start.Value)") {
            '0' { $res.StartType = 'Boot' }
            '1' { $res.StartType = 'System' }
            '2' { $res.StartType = 'Automatic'; if ("$($delayed.Value)" -eq '1') { $res.StartType = 'AutomaticDelayedStart'; $res.Delayed = $true } }
            '3' { $res.StartType = 'Manual' }
            '4' { $res.StartType = 'Disabled' }
            default { $res.StartType = 'Desconhecido' }
        }
    } catch { }
    return $res
}

function Set-ServiceStateSafe {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [ValidateSet('Disabled', 'Manual', 'Automatic', 'AutomaticDelayedStart')][string]$StartType = 'Disabled',
        [switch]$StopService,
        [string]$Gain = '', [string]$Risk = '', [string]$Why = ''
    )
    if ($script:NeverTouch -contains $Name) {
        Write-Log ('BLOQUEADO pela lista de protecao: ' + $Name) 'ERRO'
        throw 'NEVER_TOUCH_VIOLATION'
    }
    if (-not $Why) { $Why = 'otimizacao' }

    $st = Get-ServiceState -Name $Name
    if (-not $st.Found) { Write-Log ('Servico nao encontrado (ignorado): ' + $Name) 'VERIF'; return }

    $alreadyOff = ($st.StartType -eq 'Disabled') -and ((-not $StopService) -or ($st.Status -eq 'Stopped'))
    if ($alreadyOff) { Write-Log ('Ja aplicado: ' + $Name + ' (' + $st.StartType + '/' + $st.Status + ')') 'VERIF'; return }

    if ($script:DryRun) {
        Write-Log ('[DRY-RUN] faria: servico ' + $Name + ' ' + $st.StartType + '/' + $st.Status + ' -> ' + $StartType + $(if ($StopService) { '/Stopped' } else { '' }) + '  (' + $Why + ')') 'DRYRUN'
        return
    }

    $scMap = @{ Disabled = 'disabled'; Manual = 'demand'; Automatic = 'auto'; AutomaticDelayedStart = 'delayed-auto' }
    $scValue = $scMap[$StartType]
    $beforeHash = @{ startType = $st.StartType; status = $st.Status }
    try {
        $null = & sc.exe config $Name start= $scValue 2>&1
        $newStatus = $st.Status
        if ($StopService -and $st.Status -ne 'Stopped') {
            Stop-Service -Name $Name -Force -ErrorAction SilentlyContinue
            Start-Sleep -Milliseconds 250
            $newStatus = (Get-Service -Name $Name -ErrorAction SilentlyContinue).Status
        }
        $after = Get-ServiceState -Name $Name
        $ok = ($after.StartType -eq $StartType)
        $msg = 'Servico ' + $Name + ': ' + $st.StartType + '/' + $st.Status + ' -> ' + $after.StartType + '/' + $after.Status
        if ($ok) { Write-Log ($msg + '  [OK]  ' + $Why) 'OK' } else { Write-Log ($msg + '  [FALHOU]') 'ERRO' }
        if ($ok) {
            Save-BackupEntry -Type 'Service' -Target $Name -Before $beforeHash `
                -After @{ startType = $after.StartType; status = $after.Status } `
                -Gain $Gain -Risk $Risk -RevertHint ('sc config ' + $Name + ' start= ' + $scValue + ' (reverter: ' + $st.StartType + ')')
        }
    } catch {
        Write-Log ('Falha ao alterar servico ' + $Name + ': ' + $_.Exception.Message) 'ERRO'
    }
}

function Find-ServiceByPattern {
    param([string[]]$Patterns)
    $found = New-Object System.Collections.ArrayList
    $all = Get-Service -ErrorAction SilentlyContinue
    foreach ($svc in $all) {
        if ($script:NeverTouch -contains $svc.Name) { continue }
        foreach ($p in $Patterns) {
            if ($svc.Name -like ('*' + $p + '*') -or $svc.DisplayName -like ('*' + $p + '*')) {
                if (-not ($found | Where-Object { $_.Name -eq $svc.Name })) { $null = $found.Add($svc) }
                break
            }
        }
    }
    return $found
}

# ==============================================================================
#  6. DETECÇÃO (hardware / segurança / emuladores)
# ==============================================================================
function Get-DiskType {
    param([string]$DriveLetter = 'C')
    try {
        $part = Get-Partition -DriveLetter $DriveLetter -ErrorAction Stop
        $disk = Get-Disk -Number $part.DiskNumber -ErrorAction Stop
        if ($disk.MediaType -and $disk.MediaType -ne 'Unspecified') { return ([string]$disk.MediaType).ToUpper() }
    } catch { }
    try {
        $pd = Get-PhysicalDisk -ErrorAction Stop | Select-Object -First 1
        if ($pd.MediaType -and $pd.MediaType -ne 'Unspecified') { return ([string]$pd.MediaType).ToUpper() }
    } catch { }
    try {
        $wmi = Get-WmiObject -Class Win32_DiskDrive -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($wmi -and "$($wmi.MediaType)" -match 'SSD|Solid') { return 'SSD' }
        if ($wmi -and "$($wmi.MediaType)" -match 'Fixed') { return 'HDD' }
    } catch { }
    return 'Desconhecido'
}

function Test-IsSsd {
    return ((Get-DiskType -DriveLetter 'C') -eq 'SSD')
}

function Get-VbsStatus {
    $r = [pscustomobject]@{ Status = 0; Hvci = $false; Raw = '' }
    try {
        $dg = Get-CimInstance -Namespace 'root\Microsoft\Windows\DeviceGuard' -ClassName 'Win32_DeviceGuard' -ErrorAction Stop
        $r.Status = [int]$dg.VirtualizationBasedSecurityStatus
        $svcRunning = @($dg.SecurityServicesRunning)
        $r.Hvci = ($svcRunning -contains 1) -or ($svcRunning -contains 2)
        $r.Raw = ('VBS=' + $r.Status + ' Services=' + ($svcRunning -join ','))
    } catch { }
    return $r
}

function Get-GameDvrStatus {
    $a = Get-RegValueState -Path 'HKCU:\System\GameConfigStore' -Name 'GameDVR_Enabled'
    $b = Get-RegValueState -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\GameDVR' -Name 'AppCaptureEnabled'
    if ("$($a.Value)" -eq '1' -or "$($b.Value)" -eq '1') { return $true }
    return $false
}

function Get-HagsStatus {
    $v = Get-RegValueState -Path 'HKLM:\SYSTEM\CurrentControlSet\Control\GraphicsDrivers' -Name 'HwSchMode'
    if (-not $v.Exists) { return -1 }
    return [int]$v.Value
}

function Get-ActivePowerScheme {
    $r = [pscustomobject]@{ Guid = ''; Name = '?' }
    try {
        $out = (& powercfg.exe /getactivescheme) -join ' '
        $m = [regex]::Match($out, '([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})')
        if ($m.Success) {
            $r.Guid = $m.Groups[1].Value.ToLower()
            $names = @{
                '381b4222-f694-41f0-9685-ff5bb260df2e' = 'Equilibrado'
                '8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c' = 'Alto desempenho'
                'a1841308-3541-4fab-bc81-f71556f20b4a' = 'Economia de energia'
                'e9a42b02-d5df-448d-aa00-03f14749eb61' = 'Ultimate Performance'
            }
            if ($names.ContainsKey($r.Guid)) { $r.Name = $names[$r.Guid] } else { $r.Name = 'Personalizado' }
        }
    } catch { }
    return $r
}

function Get-GpuVendor {
    try {
        $gpus = Get-CimInstance Win32_VideoController -ErrorAction Stop
        foreach ($g in $gpus) {
            if ("$($g.Name)" -match 'NVIDIA') { return 'NVIDIA' }
        }
        foreach ($g in $gpus) { if ("$($g.Name)" -match 'AMD|Radeon') { return 'AMD' } }
        foreach ($g in $gpus) { if ("$($g.Name)" -match 'Intel') { return 'INTEL' } }
    } catch { }
    return 'DESCONHECIDO'
}

function Test-HyperVInUse {
    $reasons = New-Object System.Collections.ArrayList
    try {
        $cs = Get-CimInstance Win32_ComputerSystem -ErrorAction Stop
        if ($cs.HypervisorPresent) { $null = $reasons.Add('hypervisor em execucao (Hyper-V/VBS/WSL2)') }
    } catch { }
    foreach ($s in @('vmms', 'vmcompute', 'HvHost', 'WslService', 'LxssManager', 'vmicheartbeat', 'vmickvpexchange')) {
        $o = Get-Service -Name $s -ErrorAction SilentlyContinue
        if ($o -and $o.Status -eq 'Running') { $null = $reasons.Add(('servico ' + $s + ' em execucao')) }
    }
    foreach ($p in @('vmmem', 'vmmemWSL', 'wsl', 'docker', 'Docker Desktop', 'vmwp', 'VBoxSVC', 'VirtualBoxVM')) {
        if (Get-Process -Name $p -ErrorAction SilentlyContinue) { $null = $reasons.Add(('processo ' + $p + ' ativo')) }
    }
    try {
        if (Get-AppxPackage -Name '*WindowsSubsystemForAndroid*' -ErrorAction SilentlyContinue) { $null = $reasons.Add('WSA instalado') }
    } catch { }
    return $reasons
}

function Get-InstalledEmulators {
    param([switch]$Refresh)
    if ($script:EmulatorCache -and -not $Refresh) { return $script:EmulatorCache }
    $res = New-Object System.Collections.ArrayList
    foreach ($emu in $script:EmulatorCatalog) {
        foreach ($p in $emu.Paths) {
            if ($p -and (Test-Path -LiteralPath $p)) {
                $exe = $null
                try {
                    $cand = Get-ChildItem -LiteralPath $p -Filter $emu.Exe -Recurse -Depth 2 -File -ErrorAction SilentlyContinue | Select-Object -First 1
                    if ($cand) { $exe = $cand.FullName }
                } catch { }
                $null = $res.Add([pscustomobject]@{ Name = $emu.Name; Path = $p; Exe = $exe })
                break
            }
        }
    }
    $script:EmulatorCache = $res
    return $res
}

function Get-DefenderExclusions {
    try { return @((Get-MpPreference -ErrorAction Stop).ExclusionPath) } catch { return @() }
}

function Get-DefenderExclusionCount {
    return (Get-DefenderExclusions).Count
}

# ==============================================================================
#  7. AÇÕES: ENERGIA, GAME DVR, DEFENDER, GPU
# ==============================================================================
function Set-PowerPlanHigh {
    $cur = Get-ActivePowerScheme
    $target = '8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c'   # Alto desempenho
    if ($cur.Guid -eq $target) { Write-Log 'Plano de energia ja esta em Alto desempenho' 'VERIF' }
    else {
        if ($script:DryRun) { Write-Log ('[DRY-RUN] faria: plano de energia ' + $cur.Name + ' -> Alto desempenho') 'DRYRUN' }
        else {
            $null = & powercfg.exe /setactive $target 2>&1
            if ($LASTEXITCODE -ne 0) {
                $dup = (& powercfg.exe -duplicatescheme 'e9a42b02-d5df-448d-aa00-03f14749eb61' 2>&1) -join ' '
                $m = [regex]::Match($dup, '([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})')
                if ($m.Success) { $target = $m.Groups[1].Value; $null = & powercfg.exe /setactive $target 2>&1 }
            }
            $now = Get-ActivePowerScheme
            Write-Log ('Plano de energia: ' + $cur.Name + ' -> ' + $now.Name) $(if ($now.Guid -ne $cur.Guid) { 'OK' } else { 'ERRO' })
            if ($now.Guid -ne $cur.Guid) {
                Save-BackupEntry -Type 'PowerScheme' -Target 'active' -Before @{ guid = $cur.Guid; name = $cur.Name } `
                    -After @{ guid = $now.Guid; name = $now.Name } -Gain '3-10%' -Risk 'maior consumo/calor (notebook)' `
                    -RevertHint ('powercfg /setactive ' + $cur.Guid)
            }
        }
    }
    # PCIe ASPM off + USB selective suspend off no plano ATIVO
    $subPcie = '501a4d13-42af-4429-9fd1-a8218c268e20'
    $setAspm = 'ee12f906-d277-404b-b6da-e5fa1a576df5'
    $subUsb = '2a737441-1930-4402-8d77-b2bebba308a3'
    $setUsb = '48e6b7a6-50f5-4782-a5d4-53bb8f07e226'
    foreach ($t in @(@($subPcie, $setAspm, 'PCIe ASPM'), @($subUsb, $setUsb, 'Suspensao seletiva USB'))) {
        if ($script:DryRun) { Write-Log ('[DRY-RUN] faria: desligar ' + $t[2] + ' (AC)') 'DRYRUN'; continue }
        $null = & powercfg.exe /setacvalueindex 'SCHEME_CURRENT' $t[0] $t[1] 0 2>&1
        Write-Log ('Ajuste de energia: ' + $t[2] + ' = desligado (AC)') 'OK'
    }
    if (-not $script:DryRun) { $null = & powercfg.exe /setactive 'SCHEME_CURRENT' 2>&1 }
}

function Set-GameDvrOff {
    $changes = @(
        @{ P = 'HKCU:\System\GameConfigStore'; N = 'GameDVR_Enabled'; V = 0 },
        @{ P = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\GameDVR'; N = 'AppCaptureEnabled'; V = 0 },
        @{ P = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\GameDVR'; N = 'HistoricalCaptureEnabled'; V = 0 },
        @{ P = 'HKCU:\Software\Microsoft\GameBar'; N = 'UseNexusForGameBarEnabled'; V = 0 },
        @{ P = 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\GameDVR'; N = 'AllowGameDVR'; V = 0 }
    )
    foreach ($c in $changes) {
        Set-RegValueSafe -Path $c.P -Name $c.N -Value $c.V -Type DWord `
            -Gain '5-15% (gravacao em 2o plano)' -Risk 'perde captura de clipes da Game Bar'
    }
}

function Set-DefenderEmulatorExclusions {
    param([array]$Paths)
    $existing = Get-DefenderExclusions
    foreach ($p in $Paths) {
        if (-not $p) { continue }
        if (-not (Test-Path -LiteralPath $p)) { continue }
        $already = $false
        foreach ($e in $existing) {
            if ($e -and ($e.TrimEnd('\') -ieq $p.TrimEnd('\'))) { $already = $true; break }
        }
        if ($already) { Write-Log ('Exclusao ja existente: ' + $p) 'VERIF'; continue }
        if ($script:DryRun) { Write-Log ('[DRY-RUN] adicionaria exclusao no Defender: ' + $p) 'DRYRUN'; continue }
        try {
            Add-MpPreference -ExclusionPath $p -ErrorAction Stop
            Write-Log ('Exclusao adicionada no Defender: ' + $p) 'OK'
            $bk = Get-BackupObject
            if ($bk) {
                $list = @($bk.defenderExclusionsAdded) + $p
                $bk.defenderExclusionsAdded = $list
                Save-BackupSession -Meta $bk
            }
            Save-BackupEntry -Type 'DefenderExclusion' -Target $p -Before @{ exists = $false } -After @{ exists = $true } `
                -Gain '2-10% (menos scan nos arquivos do emulador)' -Risk 'menos protecao nessa pasta' `
                -RevertHint 'Remove-MpPreference -ExclusionPath <path>'
        } catch { Write-Log ('Falha ao adicionar exclusao ' + $p + ': ' + $_.Exception.Message) 'ERRO' }
    }
}

function Set-GpuPreferenceHigh {
    param([string]$ExePath)
    if (-not $ExePath -or -not (Test-Path -LiteralPath $ExePath)) { return }
    $path = 'HKCU:\Software\Microsoft\DirectX\UserGpuPreferences'
    Set-RegValueSafe -Path $path -Name $ExePath -Value 'GpuPreference=2;' -Type String `
        -Gain 'forca GPU dedicada em notebook' -Risk 'nenhum'
}

# ==============================================================================
#  8. PERFIS
# ==============================================================================
function Test-ProfileIncludes {
    param([string]$ItemProfile, [string]$Target)
    $order = @{ 'Safe' = 1; 'Balanced' = 2; 'Extreme' = 3 }
    return ($order[$ItemProfile] -le $order[$Target])
}

function Test-NeverTouchList {
    # salvaguarda: garante que a lista de protecao esta integra antes de qualquer escrita
    $must = @('RpcSs', 'DcomLaunch', 'PlugPlay', 'Power', 'AudioSrv', 'AudioEndpointBuilder', 'Dwm', 'EventLog', 'Winmgmt')
    foreach ($m in $must) {
        if ($script:NeverTouch -notcontains $m) {
            Write-Log ('Lista de protecao incompleta (falta ' + $m + '). Abortando.') 'ERRO'
            throw 'NEVER_TOUCH_INCOMPLETE'
        }
    }
    return $true
}

function Invoke-ServicePlan {
    $plan = @(
        @{ Name = 'DiagTrack';              Prof = 'Safe';     Gain = '~0% (privacidade)'; Risk = 'perde telemetria' },
        @{ Name = 'dmwappushservice';       Prof = 'Safe';     Gain = '~0%'; Risk = 'baixo' },
        @{ Name = 'WerSvc';                 Prof = 'Safe';     Gain = '~0%'; Risk = 'sem relatorio de erros' },
        @{ Name = 'SysMain';                Prof = 'Balanced'; Gain = '0-3%'; Risk = 'so SSD' },
        @{ Name = 'WSearch';                Prof = 'Balanced'; Gain = '0-3%'; Risk = 'busca do menu mais lenta' },
        @{ Name = 'Spooler';                Prof = 'Balanced'; Gain = '~0%'; Risk = 'so sem impressora' },
        @{ Name = 'bthserv';                Prof = 'Balanced'; Gain = '0-1%'; Risk = 'so sem Bluetooth' },
        @{ Name = 'BTAGService';            Prof = 'Balanced'; Gain = '0-1%'; Risk = 'so sem Bluetooth' },
        @{ Name = 'XblAuthManager';         Prof = 'Balanced'; Gain = '~0%'; Risk = 'so sem Game Pass' },
        @{ Name = 'XblGameSave';            Prof = 'Balanced'; Gain = '~0%'; Risk = 'so sem Game Pass' },
        @{ Name = 'XboxNetApiSvc';          Prof = 'Balanced'; Gain = '~0%'; Risk = 'so sem Game Pass' },
        @{ Name = 'XboxGipSvc';             Prof = 'Balanced'; Gain = '~0%'; Risk = 'so sem controle Xbox' },
        @{ Name = 'RetailDemo';             Prof = 'Balanced'; Gain = '~0%'; Risk = 'nenhum' },
        @{ Name = 'MapsBroker';             Prof = 'Balanced'; Gain = '~0%'; Risk = 'nenhum' },
        @{ Name = 'PhoneSvc';               Prof = 'Balanced'; Gain = '~0%'; Risk = 'nenhum' },
        @{ Name = 'Fax';                    Prof = 'Balanced'; Gain = '~0%'; Risk = 'nenhum' },
        @{ Name = 'RemoteRegistry';         Prof = 'Balanced'; Gain = '~0% (seguranca)'; Risk = 'nenhum' },
        @{ Name = 'WinRM';                  Prof = 'Balanced'; Gain = '~0% (seguranca)'; Risk = 'nenhum' }
    )

    foreach ($item in $plan) {
        if (-not (Test-ProfileIncludes -ItemProfile $item.Prof -Target $Profile)) { continue }
        $n = $item.Name

        # --- guardas de contexto ---
        if ($n -eq 'SysMain') {
            $dt = Get-DiskType -DriveLetter 'C'
            if ($dt -ne 'SSD') {
                Write-Log ('SysMain preservado: disco do sistema = ' + $dt + ' (em HDD o Superfetch ajuda)') 'WARN'
                continue
            }
        }
        if ($n -eq 'Spooler') {
            $pr = @()
            try { $pr = @(Get-Printer -ErrorAction Stop) } catch { }
            if ($pr.Count -gt 0) { Write-Log ('Spooler preservado: ' + $pr.Count + ' impressora(s) instalada(s)') 'WARN'; continue }
            if ($pr.Count -eq 0 -and -not (Get-Command Get-Printer -ErrorAction SilentlyContinue)) {
                Write-Log 'Spooler preservado: nao foi possivel verificar impressoras neste sistema' 'WARN'; continue
            }
        }
        if ($n -in @('bthserv', 'BTAGService')) {
            $btCount = 0
            try { $btCount = @(Get-PnpDevice -Class Bluetooth -ErrorAction Stop | Where-Object { $_.Status -eq 'OK' }).Count } catch { }
            if ($btCount -gt 0) { Write-Log ('Bluetooth preservado: ' + $btCount + ' dispositivo(s) ativo(s)') 'WARN'; continue }
        }
        if ($n -like 'Xbl*' -or $n -like 'Xbox*') {
            $xboxRunning = (Get-Service -Name 'XblAuthManager' -ErrorAction SilentlyContinue).Status -eq 'Running'
            if ($xboxRunning) { Write-Log ('Servicos Xbox preservados: XblAuthManager em execucao') 'WARN'; continue }
        }
        if ($n -eq 'WSearch' -and -not $script:Interactive) {
            Write-Log 'WSearch pulado em modo nao interativo (decisao manual recomendada)' 'WARN'
            continue
        }
        if ($n -eq 'WSearch' -and $script:Interactive) {
            if (-not (Confirm-Action 'Desativar Windows Search (WSearch)? A busca do menu Iniciar fica mais lenta.')) { continue }
        }

        Set-ServiceStateSafe -Name $n -StartType Disabled -StopService -Gain $item.Gain -Risk $item.Risk -Why 'perfil ' + $Profile
    }
}

function Invoke-BloatwarePlan {
    Write-Section 'Servicos de fabricante (stutter)'
    $found = Find-ServiceByPattern -Patterns $script:BloatwarePatterns
    if ($found.Count -eq 0) { Write-Log 'Nenhum servico de fabricante problematico encontrado' 'INFO'; return }
    foreach ($svc in $found) {
        if ($script:NeverTouch -contains $svc.Name) { continue }
        Write-Log ('Encontrado: ' + $svc.Name + ' (' + $svc.DisplayName + ')') 'INFO'
        Set-ServiceStateSafe -Name $svc.Name -StartType Disabled -StopService `
            -Gain '2-6% / reduz stutter' -Risk 'afeta RGB ou audio virtual do fabricante' -Why 'bloatware'
    }
    Write-Log 'Nada foi desinstalado. Reative em services.msc se precisar do RGB/audio.' 'WARN'
}

function Invoke-RegistryPlan {
    Write-Section 'Ajustes de registro'
    # Aparência (mantém fontes suavizadas)
    Set-RegValueSafe -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects' -Name 'VisualFXSetting' -Value 2 -Type DWord -Gain '1-2%' -Risk 'sem animacoes'
    # Prioridade de primeiro plano
    Set-RegValueSafe -Path 'HKLM:\SYSTEM\CurrentControlSet\Control\PriorityControl' -Name 'Win32PrioritySeparation' -Value 38 -Type DWord -Gain '0-3%' -Risk 'nenhum relevante'
}

function Invoke-ProfileSafe {
    Write-Section 'PERFIL SEGURO (ganho garantido, risco ~zero)'
    if (-not $script:SkipRestorePoint -and -not $script:DryRun) { New-RestorePointHL }
    Set-PowerPlanHigh
    Set-GameDvrOff
    Invoke-ServicePlan
    Invoke-RegistryPlan
    $paths = @()
    foreach ($e in Get-InstalledEmulators) { $paths += $e.Path }
    if ($EmulatorFocus -and $paths.Count -gt 0) { Set-DefenderEmulatorExclusions -Paths $paths }
    Write-Log 'Perfil Seguro concluido' 'OK'
}

function Invoke-ProfileBalanced {
    Invoke-ProfileSafe
    Write-Section 'PERFIL EQUILIBRADO'
    Invoke-ServicePlan
    Invoke-BloatwarePlan
    # Limite de upload da Otimizacao de Entrega (nao desabilita o servico)
    $doPath = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\DeliveryOptimization\Config'
    Set-RegValueSafe -Path $doPath -Name 'DODownloadMode' -Value 1 -Type DWord -Gain '0-2% de disco/rede' -Risk 'nao compartilha update com a LAN'
    Write-Log 'Perfil Equilibrado concluido' 'OK'
}

function Invoke-ProfileExtreme {
    Invoke-ProfileBalanced
    Write-Section 'PERFIL AGRESSIVO / EXPERIMENTAL'
    Write-Host '  [!] Este perfil ASSUME RISCO. Leia cada aviso.' -ForegroundColor Yellow

    # Hyper-V / hypervisor
    $reasons = Test-HyperVInUse
    if ($reasons.Count -gt 0) {
        Write-Log ('Hyper-V/hypervisor EM USO: ' + ($reasons -join ' | ')) 'WARN'
        Write-Log 'HL Optimizer NAO vai desativar o hypervisor (quebraria WSL2/Docker/WSA).' 'WARN'
        Write-Log 'Se quiser mesmo: bcdedit /set hypervisorlaunchtype off  (e reverte com auto)' 'WARN'
    } else {
        if (Confirm-Action 'Desativar o hypervisor (bcdedit hypervisorlaunchtype off)? Quebra WSL2/Docker/WSA. Exige reinicio.') {
            if ($script:DryRun) { Write-Log '[DRY-RUN] faria: bcdedit /set hypervisorlaunchtype off' 'DRYRUN' }
            else {
                $cur = ''
                try { $cur = ((& bcdedit.exe /enum '{current}' 2>&1) -join ' ') } catch { }
                $before = 'auto'
                if ($cur -match 'hypervisorlaunchtype\s+(\w+)') { $before = $Matches[1] }
                $null = & bcdedit.exe /set hypervisorlaunchtype off 2>&1
                Write-Log ('hypervisorlaunchtype: ' + $before + ' -> off (requer reinicio)') 'OK'
                Save-BackupEntry -Type 'BcdEdit' -Target 'hypervisorlaunchtype' -Before @{ value = $before } -After @{ value = 'off' } `
                    -Gain '10-100%+ em emulador com VT-x' -Risk 'quebra WSL2/Docker/WSA ate reverter' `
                    -RevertHint ('bcdedit /set hypervisorlaunchtype ' + $before) -RequiresReboot $true
            }
        }
    }

    # VBS / HVCI: apenas detectar e instruir
    $vbs = Get-VbsStatus
    if ($vbs.Status -ne 0) {
        Write-Log ('VBS ativo (' + $vbs.Raw + ') - HL NAO altera via registro (chaves protegidas).') 'WARN'
        Write-Log 'Passo manual: Seguranca do Windows > Seguranca do dispositivo > Isolamento do nucleo > Integridade de memoria = DESLIGADO + reiniciar' 'WARN'
    } else { Write-Log 'VBS/HVCI ja desligado' 'VERIF' }

    # HAGS
    if (Confirm-Action 'Alternar HAGS (Agendamento de GPU acelerado por hardware)? E teste-e-compare, nao ganho garantido.') {
        $hags = Get-HagsStatus
        $new = 2
        if ($hags -eq 2) { $new = 1 }
        Set-RegValueSafe -Path 'HKLM:\SYSTEM\CurrentControlSet\Control\GraphicsDrivers' -Name 'HwSchMode' -Value $new -Type DWord `
            -Gain 'teste: +-2..5% (depende do driver)' -Risk 'pode piorar em alguns jogos' -RequiresReboot
    }

    if ($IncludeExperimental) {
        Write-Section 'EXPERIMENTAL (sem ganho comprovado)'
        if (Confirm-Action 'Aplicar NetworkThrottlingIndex=0xFFFFFFFF e SystemResponsiveness=10? (experimental)') {
            $mm = 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile'
            # 0xFFFFFFFF e lido de volta como -1 (Int32); usamos -1 para a verificacao bater
            Set-RegValueSafe -Path $mm -Name 'NetworkThrottlingIndex' -Value -1 -Type DWord -Gain 'sem ganho comprovado' -Risk 'experimental'
            Set-RegValueSafe -Path $mm -Name 'SystemResponsiveness' -Value 10 -Type DWord -Gain 'sem ganho comprovado' -Risk 'experimental'
        }
    }
    Write-Log 'Perfil Agressivo concluido' 'OK'
}

# ==============================================================================
#  9. FOCO EMULADORES
# ==============================================================================
function Invoke-EmulatorFocus {
    Write-Section 'FOCO EMULADORES'
    $emus = Get-InstalledEmulators
    if ($emus.Count -eq 0) {
        Write-Log 'Nenhum emulador conhecido encontrado. O checklist geral sera gerado mesmo assim.' 'WARN'
    } else {
        foreach ($e in $emus) { Write-Log ('Detectado: ' + $e.Name + ' em ' + $e.Path) 'INFO' }
    }

    $paths = @()
    foreach ($e in $emus) { $paths += $e.Path }
    $paths += @(
        (Join-Path $env:USERPROFILE 'AppData\Local\Android'),
        (Join-Path $env:LOCALAPPDATA 'BlueStacks_nxt')
    )
    Set-DefenderEmulatorExclusions -Paths $paths

    foreach ($e in $emus) {
        if ($e.Exe) { Set-GpuPreferenceHigh -ExePath $e.Exe }
    }

    # Diagnóstico específico de emulador
    $v = $script:SysInfo
    Write-Section 'Diagnostico para emulador'
    $msg = @()
    if (-not $v.VirtualizationFirmware) { $msg += '[X] VT-x/SVM DESLIGADO na BIOS -> maior gargalo possivel' }
    else { $msg += '[OK] VT-x/SVM ligado na BIOS' }
    if ($v.HypervisorPresent) { $msg += '[!] Hypervisor ativo -> BlueStacks/LDPlayer podem cair para modo lento' }
    if ($v.VbsStatus -ne 0) { $msg += '[!] VBS/HVCI ativo -> custo de 5-15% de CPU' }
    if ($v.DiskType -ne 'SSD') { $msg += '[!] Sistema em ' + $v.DiskType + ' -> carregamento lento' }
    if ($v.RamGb -lt 8) { $msg += '[!] RAM total ' + $v.RamGb + ' GB -> use 2 nucleos e 2 GB no emulador' }
    foreach ($m in $msg) { Write-Host ('  ' + $m) -ForegroundColor $(if ($m -like '[X]*') { 'Red' } elseif ($m -like '[!]*') { 'Yellow' } else { 'Green' }) }

    $checklist = @()
    $checklist += 'HL OPTIMIZER - CHECKLIST DE EMULADOR (' + (Get-Date -Format 'yyyy-MM-dd HH:mm') + ')'
    $checklist += '------------------------------------------------------------------'
    $checklist += 'Emuladores detectados: ' + $(if ($emus.Count -gt 0) { ($emus | ForEach-Object { $_.Name }) -join ', ' } else { 'nenhum' })
    $checklist += ''
    $checklist += '[BIOS/WINDOWS]'
    $checklist += ' - VT-x (Intel) ou AMD-V/SVM: ENABLED'
    $checklist += ' - Hyper-V, Plataforma de Hipervisor, VMS, WSL2, Docker, WSA: desinstalados/desligados'
    $checklist += ' - Isolamento de nucleo (VBS/HVCI): DESLIGADO + reiniciar'
    $checklist += ' - Plano de energia: Alto desempenho (na tomada, em notebook)'
    $checklist += ''
    $checklist += '[DENTRO DO EMULADOR - nao e possivel via script]'
    $checklist += ' - Resolucao: 1280x720 (ou 960x540 em PC fraco), DPI 240'
    $checklist += ' - CPU: 4 nucleos | RAM: 4 GB (8 GB se tiver 16 GB)'
    $checklist += ' - Renderer: Vulkan -> OpenGL -> DirectX (nessa ordem)'
    $checklist += ' - FPS travado igual ao monitor (60/90/120), nunca ilimitado'
    $checklist += ' - Desligar: gravacao de tela, economia de bateria, sync vertical extra'
    $checklist += ' - Ligar: modo alto desempenho / high FPS'
    $checklist += ''
    $checklist += '[CONSOLES - PCSX2/Dolphin/RPCS3/Ryujinx]'
    $checklist += ' - Backend Vulkan (ou D3D12 no RPCS3), resolucao interna 1x primeiro'
    $checklist += ' - Async shaders / cache de shaders ligados (mata stutter de compilacao)'
    $checklist += ' - PCSX2: MTVU ligado | Dolphin: Dual Core ligado'
    $file = Join-Path $script:LogDir ('hl-checklist-emulador_' + $script:SessionId + '.txt')
    if (-not $script:DryRun) {
        try { Set-Content -LiteralPath $file -Value $checklist -Encoding UTF8; Write-Log ('Checklist salvo: ' + $file) 'OK' } catch { }
    }
    foreach ($l in $checklist) { Write-Host ('  ' + $l) -ForegroundColor DarkGray }
}

# ==============================================================================
# 10. SESSÃO DE JOGO (boost temporário, reverte ao sair)
# ==============================================================================
function Invoke-GameSession {
    Write-Section 'SESSAO DE JOGO (boost temporario)'
    $exe = $null
    if ($script:Interactive) {
        Write-Host '  Caminho do .exe do jogo/emulador (ENTER usa o 1o emulador detectado):' -ForegroundColor Gray
        $in = Read-Host '  > '
        if ($in) { $exe = $in.Trim('"') }
    }
    if (-not $exe) {
        $e = Get-InstalledEmulators | Select-Object -First 1
        if ($e) { $exe = $e.Exe }
    }

    $stopped = New-Object System.Collections.ArrayList
    $found = Find-ServiceByPattern -Patterns $script:BloatwarePatterns
    Write-Log ('Parando ' + $found.Count + ' servico(s) de fabricante durante a sessao') 'ACAO'
    foreach ($svc in $found) {
        if ($script:NeverTouch -contains $svc.Name) { continue }
        $st = Get-ServiceState -Name $svc.Name
        if ($st.Status -eq 'Running') {
            if (-not $script:DryRun) { Stop-Service -Name $svc.Name -Force -ErrorAction SilentlyContinue }
            $null = $stopped.Add($svc.Name)
            Write-Log ('  pausado: ' + $svc.Name) 'OK'
        }
    }
    $prevPower = Get-ActivePowerScheme
    if (-not $script:DryRun) { $null = & powercfg.exe /setactive '8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c' 2>&1 }

    $proc = $null
    if ($exe -and (Test-Path -LiteralPath $exe)) {
        if (-not $script:DryRun) {
            try {
                $proc = Start-Process -FilePath $exe -PassThru
                Start-Sleep -Seconds 3
                try { $proc.PriorityClass = 'High' ; Write-Log 'Prioridade do processo = High' 'OK' } catch { }
                Write-Log ('Jogo iniciado (PID ' + $proc.Id + '). Feche o jogo para restaurar tudo.') 'ACAO'
                $null = $proc.WaitForExit()
            } catch { Write-Log ('Nao foi possivel iniciar ' + $exe + ': ' + $_.Exception.Message) 'ERRO' }
        }
    } else {
        Write-Log 'Nenhum executavel informado. Aplicando boost e aguardando ENTER para restaurar.' 'WARN'
        if ($script:Interactive) { $null = Read-Host }
    }

    foreach ($s in $stopped) {
        if (-not $script:DryRun) { Start-Service -Name $s -ErrorAction SilentlyContinue }
        Write-Log ('  restaurado: ' + $s) 'OK'
    }
    if ($prevPower.Guid) { if (-not $script:DryRun) { $null = & powercfg.exe /setactive $prevPower.Guid 2>&1 } }
    Write-Log ('Sessao encerrada. Plano de energia restaurado para ' + $prevPower.Name) 'OK'
}

# ==============================================================================
# 10b. LIMPEZA SEGURA (temporarios + cache de shader)
# ==============================================================================
function Invoke-TempCleanup {
    Write-Section 'LIMPEZA (temporarios e cache de shader)'
    $targets = @(
        @{ Path = $env:TEMP;                                       Label = 'Temp do usuario' },
        @{ Path = (Join-Path $env:SystemRoot 'Temp');              Label = 'Temp do Windows' },
        @{ Path = (Join-Path $env:LOCALAPPDATA 'NVIDIA\DXCache');  Label = 'Cache shader NVIDIA DX' },
        @{ Path = (Join-Path $env:LOCALAPPDATA 'NVIDIA\GLCache');  Label = 'Cache shader NVIDIA GL' },
        @{ Path = (Join-Path $env:LOCALAPPDATA 'D3DSCache');       Label = 'Cache shader DirectX' },
        @{ Path = (Join-Path $env:LOCALAPPDATA 'AMD\DxCache');     Label = 'Cache shader AMD' }
    )
    $cutoff = (Get-Date).AddDays(-1)
    $totalMb = 0.0
    foreach ($t in $targets) {
        if (-not $t.Path -or -not (Test-Path -LiteralPath $t.Path)) { continue }
        $files = @()
        try {
            $files = @(Get-ChildItem -LiteralPath $t.Path -Recurse -File -Force -ErrorAction SilentlyContinue |
                Where-Object { $_.LastWriteTime -lt $cutoff })
        } catch { }
        if ($files.Count -eq 0) { Write-Log ('Nada para limpar: ' + $t.Label) 'VERIF'; continue }
        $sum = ($files | Measure-Object -Property Length -Sum).Sum
        $mb = [math]::Round(($sum / 1MB), 1)
        if (-not (Confirm-Action ('Apagar ' + $files.Count + ' arquivo(s) (' + $mb + ' MB) de ' + $t.Label + '?'))) { continue }
        if ($script:DryRun) { Write-Log ('[DRY-RUN] apagaria ' + $mb + ' MB em ' + $t.Path) 'DRYRUN'; continue }
        $freed = 0
        foreach ($f in $files) {
            try { Remove-Item -LiteralPath $f.FullName -Force -ErrorAction Stop; $freed++ } catch { }
        }
        Write-Log ('Limpeza ' + $t.Label + ': ' + $freed + '/' + $files.Count + ' arquivo(s), ' + $mb + ' MB') 'OK'
        $totalMb += $mb
    }
    Write-Log ('Total liberado: ' + [math]::Round($totalMb, 1) + ' MB') 'OK'
    Write-Log 'Nada em _HL-Backup, _logs ou pastas de jogos foi tocado.' 'INFO'
}

# ==============================================================================
# 11. DIAGNÓSTICO / BENCHMARK / RELATÓRIO
# ==============================================================================
function Invoke-Diagnostics {
    Write-Section 'DIAGNOSTICO DO SISTEMA'
    $v = $script:SysInfo
    $lines = New-Object System.Collections.ArrayList
    $add = { param($t) Write-Host ('  ' + $t) -ForegroundColor Gray; $null = $lines.Add($t) }

    & $add ('HL OPTIMIZER v' + $script:BrandVersion + ' - Diagnostico ' + (Get-Date -Format 'yyyy-MM-dd HH:mm'))
    & $add ('Host: ' + $env:COMPUTERNAME + ' | Usuario: ' + $env:USERNAME)
    & $add ('CPU: ' + $v.Cpu)
    & $add ('RAM: ' + $v.RamGb + ' GB')
    & $add ('GPU vendor: ' + (Get-GpuVendor))
    & $add ('Virtualizacao na BIOS (VT-x/SVM): ' + $(if ($v.VirtualizationFirmware) { 'LIGADA' } else { 'DESLIGADA' }))
    & $add ('Hypervisor presente: ' + $(if ($v.HypervisorPresent) { 'SIM' } else { 'NAO' }))
    $hv = Test-HyperVInUse
    & $add ('Hyper-V/WSL/Docker em uso: ' + $(if ($hv.Count -gt 0) { ($hv -join ' | ') } else { 'nao detectado' }))
    & $add ('VBS/HVCI: ' + (Get-VbsStatus).Raw)
    & $add ('Game DVR: ' + $(if ($v.GameDvr) { 'ATIVO' } else { 'desligado' }))
    & $add ('HAGS (HwSchMode): ' + $v.Hags)
    & $add ('Plano de energia: ' + $v.PowerName + ' (' + $v.PowerGuid + ')')
    & $add ('Disco do sistema: ' + $v.DiskType)
    & $add ('Emuladores: ' + $(if ($v.Emulators.Count -gt 0) { ($v.Emulators -join ', ') } else { 'nenhum' }))
    & $add ('Exclusoes no Defender: ' + $v.DefenderExclusions)

    Write-Section 'Carga atual (10s)'
    try {
        $cpuL = (Get-Counter '\Processor(_Total)\% Processor Time' -SampleInterval 1 -MaxSamples 3 -ErrorAction Stop).CounterSamples |
            Measure-Object CookedValue -Average
        $diskQ = (Get-Counter '\PhysicalDisk(_Total)\Current Disk Queue Length' -SampleInterval 1 -MaxSamples 3 -ErrorAction Stop).CounterSamples |
            Measure-Object CookedValue -Average
        $memA = (Get-Counter '\Memory\Available MBytes' -SampleInterval 1 -MaxSamples 3 -ErrorAction Stop).CounterSamples |
            Measure-Object CookedValue -Average
        & $add ('CPU media: ' + [math]::Round($cpuL.Average, 1) + ' %')
        & $add ('Fila de disco media: ' + [math]::Round($diskQ.Average, 2))
        & $add ('RAM livre: ' + [math]::Round($memA.Average, 0) + ' MB')
    } catch { & $add 'Contadores de desempenho indisponiveis' }

    Write-Section 'Top 8 processos por CPU'
    Get-Process -ErrorAction SilentlyContinue | Sort-Object CPU -Descending | Select-Object -First 8 |
        ForEach-Object {
            $cpuSec = 0; if ($null -ne $_.CPU) { $cpuSec = [math]::Round([double]$_.CPU, 1) }
            $ramMb = 0; if ($_.WorkingSet64) { $ramMb = [math]::Round($_.WorkingSet64 / 1MB, 0) }
            & $add ('  ' + $_.ProcessName.PadRight(28) + $cpuSec + ' s  RAM ' + $ramMb + ' MB')
        }

    $file = Join-Path $script:LogDir ('hl-diagnostico_' + $script:SessionId + '.txt')
    if (-not $script:DryRun) {
        try { Set-Content -LiteralPath $file -Value $lines -Encoding UTF8; Write-Log ('Relatorio salvo: ' + $file) 'OK' } catch { }
    }
}

function Invoke-QuickBenchmark {
    Write-Section 'BENCHMARK RAPIDO (nao e FPS - mede carga do sistema)'
    Write-Log 'Para FPS/1% low use CapFrameX ou PresentMon. Aqui medimos CPU, disco e RAM.' 'INFO'
    $samples = 20
    $cpu = @(); $disk = @(); $mem = @()
    for ($i = 1; $i -le $samples; $i++) {
        Write-Progress -Activity 'HL Optimizer - coletando' -Status ("amostra $i de $samples") -PercentComplete (($i / $samples) * 100)
        try {
            $c = Get-Counter '\Processor(_Total)\% Processor Time' -ErrorAction Stop
            $d = Get-Counter '\PhysicalDisk(_Total)\Current Disk Queue Length' -ErrorAction Stop
            $m = Get-Counter '\Memory\Available MBytes' -ErrorAction Stop
            $cpu += [double]$c.CounterSamples[0].CookedValue
            $disk += [double]$d.CounterSamples[0].CookedValue
            $mem += [double]$m.CounterSamples[0].CookedValue
        } catch { break }
        Start-Sleep -Seconds 1
    }
    Write-Progress -Activity 'HL Optimizer' -Completed
    if ($cpu.Count -eq 0) { Write-Log 'Nao foi possivel coletar contadores' 'ERRO'; return }
    $r = [pscustomobject]@{
        CPU_medio = [math]::Round(($cpu | Measure-Object -Average).Average, 1)
        CPU_max = [math]::Round(($cpu | Measure-Object -Maximum).Maximum, 1)
        Disco_fila_media = [math]::Round(($disk | Measure-Object -Average).Average, 2)
        RAM_livre_MB = [math]::Round(($mem | Measure-Object -Average).Average, 0)
        Amostras = $cpu.Count
    }
    $r | Format-List | Out-String | Write-Host
    $file = Join-Path $script:LogDir ('hl-benchmark_' + $script:SessionId + '.csv')
    if (-not $script:DryRun) {
        $r | Export-Csv -LiteralPath $file -NoTypeInformation -Encoding UTF8
        Write-Log ('Benchmark salvo: ' + $file) 'OK'
    }
}

function Invoke-SelfCheck {
    Write-Section 'VERIFICACAO (self-check)'
    $results = @()
    $results += [pscustomobject]@{ Item = 'PowerShell 5.1+'; Ok = ($PSVersionTable.PSVersion.Major -ge 5) }
    $results += [pscustomobject]@{ Item = 'Executando como admin'; Ok = (Assert-Admin -Silent) }
    $results += [pscustomobject]@{ Item = 'Pasta de logs gravavel'; Ok = (Test-Path $script:LogDir) }
    $results += [pscustomobject]@{ Item = 'Cmdlets Defender'; Ok = [bool](Get-Command Get-MpPreference -ErrorAction SilentlyContinue) }
    $results += [pscustomobject]@{ Item = 'Cmdlets Storage (Get-Disk)'; Ok = [bool](Get-Command Get-Disk -ErrorAction SilentlyContinue) }
    $results += [pscustomobject]@{ Item = 'powercfg disponivel'; Ok = [bool](Get-Command powercfg.exe -ErrorAction SilentlyContinue) }
    $results += [pscustomobject]@{ Item = 'bcdedit disponivel'; Ok = [bool](Get-Command bcdedit.exe -ErrorAction SilentlyContinue) }
    $results += [pscustomobject]@{ Item = 'Protecao do Sistema (restauracao)'; Ok = $true }
    foreach ($r in $results) {
        Write-Host ('  ' + $(if ($r.Ok) { '[PASS]' } else { '[FAIL]' }) + ' ' + $r.Item) -ForegroundColor $(if ($r.Ok) { 'Green' } else { 'Red' })
    }
    Write-Log ('Self-check: ' + (@($results | Where-Object { $_.Ok }).Count) + '/' + $results.Count + ' OK') 'INFO'
}

function New-RestorePointHL {
    if ($script:DryRun) { return }
    try {
        Enable-ComputerRestore -Drive ($env:SystemDrive + '\') -ErrorAction SilentlyContinue
        Checkpoint-Computer -Description ($script:Brand + ' ' + $script:BrandVersion) -RestorePointType 'MODIFY_SETTINGS' -ErrorAction Stop
        Write-Log 'Ponto de restauracao criado' 'OK'
    } catch {
        Write-Log ('Nao foi possivel criar ponto de restauracao (' + $_.Exception.Message + '). Continue por sua conta.') 'WARN'
    }
}

# ==============================================================================
# 12. REVERSÃO
# ==============================================================================
function Invoke-RevertHL {
    $script:Reverting = $true
    Write-Section 'REVERTER (restaurar estado anterior)'
    $dir = $null
    if ($BackupPath) {
        if (Test-Path -LiteralPath $BackupPath) { $dir = Get-Item -LiteralPath $BackupPath } 
    }
    if (-not $dir) {
        $latest = Get-LatestBackup
        if (-not $latest) { Write-Log 'Nenhum backup encontrado em ' + $script:BackupRoot 'ERRO'; return }
        if ($script:Interactive) {
            Write-Host ('  Backup mais recente: ' + (Split-Path -Leaf $latest.FullName)) -ForegroundColor Cyan
            if (-not (Confirm-Action 'Restaurar este backup?')) { return }
        }
        $dir = $latest
    }
    $stateFile = Join-Path $dir.FullName 'state.json'
    if (-not (Test-Path -LiteralPath $stateFile)) { Write-Log 'state.json nao encontrado' 'ERRO'; return }
    try { $state = Get-Content -LiteralPath $stateFile -Raw -Encoding UTF8 | ConvertFrom-Json }
    catch { Write-Log ('Backup corrompido: ' + $_.Exception.Message) 'ERRO'; return }

    $changes = @($state.changes)
    Write-Log ('Restaurando ' + $changes.Count + ' alteracao(oes) do backup ' + (Split-Path -Leaf $dir.FullName)) 'ACAO'
    $ok = 0; $fail = 0

    for ($i = $changes.Count - 1; $i -ge 0; $i--) {
        $c = $changes[$i]
        if ($script:DryRun) { Write-Log ('[DRY-RUN] reverteria ' + $c.type + ' ' + $c.target) 'DRYRUN'; continue }
        try {
            switch ($c.type) {
                'Registry' {
                    $full = [string]$c.target
                    $idx = $full.LastIndexOf('\')
                    if ($idx -gt 0) {
                        $path = $full.Substring(0, $idx)
                        $name = $full.Substring($idx + 1)
                        if ($c.before.exists -eq $true) {
                            $type = 'DWord'
                            if ($c.before.type) { $type = [string]$c.before.type }
                            $val = $c.before.value
                            if ($type -eq 'DWord') { $val = [int]$val }
                            if ($type -eq 'QWord') { $val = [int64]$val }
                            if (-not (Test-Path -LiteralPath $path)) { $null = New-Item -Path $path -Force }
                            $null = New-ItemProperty -LiteralPath $path -Name $name -Value $val -PropertyType $type -Force -ErrorAction Stop
                            Write-Log ('  registro restaurado: ' + $name + ' = ' + $val) 'OK'
                        } else {
                            if (Test-Path -LiteralPath $path) {
                                Remove-ItemProperty -LiteralPath $path -Name $name -Force -ErrorAction SilentlyContinue
                                Write-Log ('  valor criado removido: ' + $name) 'OK'
                            }
                        }
                        $ok++
                    }
                }
                'Service' {
                    $map = @{ 'Disabled' = 'disabled'; 'Manual' = 'demand'; 'Automatic' = 'auto'; 'AutomaticDelayedStart' = 'delayed-auto' }
                    $orig = [string]$c.before.startType
                    if ($map.ContainsKey($orig)) {
                        $null = & sc.exe config ([string]$c.target) start= $map[$orig] 2>&1
                        if ([string]$c.before.status -eq 'Running') { Start-Service -Name ([string]$c.target) -ErrorAction SilentlyContinue }
                        Write-Log ('  servico restaurado: ' + $c.target + ' -> ' + $orig) 'OK'
                        $ok++
                    }
                }
                'PowerScheme' {
                    if ($c.before.guid) { $null = & powercfg.exe /setactive ([string]$c.before.guid) 2>&1; Write-Log ('  plano de energia restaurado: ' + $c.before.name) 'OK'; $ok++ }
                }
                'BcdEdit' {
                    if ($c.target -eq 'hypervisorlaunchtype') {
                        $v = 'auto'; if ($c.before.value) { $v = [string]$c.before.value }
                        $null = & bcdedit.exe /set hypervisorlaunchtype $v 2>&1
                        Write-Log ('  hypervisorlaunchtype restaurado: ' + $v + ' (requer reinicio)') 'OK'
                        $ok++
                    }
                }
                'DefenderExclusion' {
                    try { Remove-MpPreference -ExclusionPath ([string]$c.target) -ErrorAction Stop; Write-Log ('  exclusao removida: ' + $c.target) 'OK'; $ok++ }
                    catch { Write-Log ('  falha ao remover exclusao: ' + $c.target) 'WARN'; $fail++ }
                }
                default { Write-Log ('  tipo desconhecido ignorado: ' + $c.type) 'WARN' }
            }
        } catch { $fail++; Write-Log ('  FALHA ao reverter ' + $c.target + ': ' + $_.Exception.Message) 'ERRO' }
    }

    Write-Section 'Resultado da reversao'
    Write-Host ('  Restaurados: ' + $ok + '   Falhas: ' + $fail) -ForegroundColor $(if ($fail -eq 0) { 'Green' } else { 'Yellow' })
    Write-Log ('Reversao: ' + $ok + ' OK, ' + $fail + ' falha(s)') 'INFO'
    if ($state.rebootRequired) { Write-Log 'Algumas alteracoes exigem REINICIO para valer.' 'WARN' }
    Write-Log 'Obs.: ajustes de PCIe/USB ficam no plano de energia atual. Use "powercfg -restoredefaultschemes" se quiser zerar.' 'INFO'
}

# ==============================================================================
# 13. UTILITÁRIOS DE UI / CONFIRMAÇÃO / SOBRE
# ==============================================================================
function Confirm-Action {
    param([string]$Message)
    if ($script:DryRun) { Write-Log ('[DRY-RUN] (simulando confirmacao) ' + $Message) 'DRYRUN'; return $true }
    if (-not $script:Interactive) { return $false }
    Write-Host ''
    Write-Host ('  ? ' + $Message) -ForegroundColor Yellow
    $a = Read-Host '  Continuar? [s/N]'
    return ($a -match '^[sSyY]')
}

function Show-PlaceboList {
    Write-Section 'O que o HL Optimizer NAO faz (placebo)'
    $list = @(
        'Desativar 100+ servicos de lista de YouTube -> ganho ~0% e risco alto',
        'Desativar Windows Defender inteiro -> use exclusoes (mesmo ganho, menos risco)',
        'Desligar paginacao / "limpar RAM" -> piora 1% low',
        'HPET / useplatformclock / MSI mode / prioridade de IRQ -> sem evidencia',
        'Desinstalar apps do fabricante -> so paramos/desabilitamos (reversivel)',
        'Prometer "+200% FPS" -> todo ganho e uma faixa, depende do hardware',
        'Desativar o Modo de Jogo do Windows -> na pratica ele ajuda'
    )
    foreach ($l in $list) { Write-Host ('  x ' + $l) -ForegroundColor DarkGray }
}

function Show-About {
    Write-Section 'Sobre'
    Write-Host '  HL OPTIMIZER' -ForegroundColor Cyan
    Write-Host ('  Versao ' + $script:BrandVersion + '  (build ' + $script:BrandBuild + ')') -ForegroundColor Gray
    Write-Host ('  Marca/proprietaria deste arquivo: ' + $script:BrandOwner) -ForegroundColor Gray
    Write-Host '  Motor: Windows PowerShell 5.1 - offline, sem downloads.' -ForegroundColor Gray
    Write-Host '  Perfis: Safe -> Balanced -> Extreme.  Reversao total: -Revert' -ForegroundColor Gray
    Write-Host ''
    Write-Host '  Uso:' -ForegroundColor White
    Write-Host '    HL-Optimizer.cmd                       (menu)' -ForegroundColor DarkGray
    Write-Host '    .\HL-Optimizer.ps1 -DryRun             (simula, nao altera)' -ForegroundColor DarkGray
    Write-Host '    .\HL-Optimizer.ps1 -Profile Balanced -NonInteractive' -ForegroundColor DarkGray
    Write-Host '    .\HL-Optimizer.ps1 -EmulatorFocus      (foco emuladores)' -ForegroundColor DarkGray
    Write-Host '    .\HL-Optimizer.ps1 -Revert             (desfaz tudo)' -ForegroundColor DarkGray
}

function Open-FolderHL {
    param([string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) { $null = New-Item -ItemType Directory -Path $Path -Force -ErrorAction SilentlyContinue }
    try { Start-Process -FilePath 'explorer.exe' -ArgumentList ('"' + $Path + '"') } catch { }
}

function Show-Summary {
    Write-Section 'Resumo da sessao'
    if ($script:Reverting) {
        Write-Host '  Reversao concluida. Reinicie se o aviso acima pedir.' -ForegroundColor Green
        if ($script:LogFile) { Write-Host ('  Log: ' + $script:LogFile) -ForegroundColor Cyan }
        return
    }
    if ($script:Changes.Count -eq 0) {
        Write-Host '  Nenhuma alteracao aplicada (ou tudo ja estava otimo).' -ForegroundColor Yellow
    } else {
        $script:Changes |
            Select-Object @{ n = 'Item'; e = { $_.target } }, @{ n = 'Tipo'; e = { $_.type } }, @{ n = 'Ganho'; e = { $_.gain } }, @{ n = 'Risco'; e = { $_.risk } } |
            Format-Table -AutoSize | Out-String -Width 200 | Write-Host
    }
    Write-Host ('  Alteracoes: ' + $script:Changes.Count) -ForegroundColor Cyan
    if ($script:BackupDir) { Write-Host ('  Backup    : ' + $script:BackupDir) -ForegroundColor Cyan }
    if ($script:LogFile) { Write-Host ('  Log       : ' + $script:LogFile) -ForegroundColor Cyan }
    Write-Host ('  Reverter  : HL-Reverter.cmd') -ForegroundColor Yellow
    if ($script:RebootNeeded) { Write-Host '  REINICIO NECESSARIO para parte das mudancas.' -ForegroundColor Yellow }
}

# ==============================================================================
# 14. MENU
# ==============================================================================
function Show-Menu {
    param([bool]$Refresh)
    if ($Refresh) { Show-BrandPanel }
    Write-Host ''
    Write-Host '  ┌─ ACOES ' -ForegroundColor Cyan -NoNewline
    Write-Line '─'
    Write-Host '  [1]  Diagnostico completo + relatorio' -ForegroundColor Gray
    Write-Host '  [2]  Perfil SEGURO      ' -ForegroundColor Green -NoNewline
    Write-Host '  (risco ~zero: energia, Game DVR, telemetria)' -ForegroundColor DarkGray
    Write-Host '  [3]  Perfil EQUILIBRADO ' -ForegroundColor Cyan -NoNewline
    Write-Host '  (recomendado: + SysMain/Search/bloatware)' -ForegroundColor DarkGray
    Write-Host '  [4]  Perfil AGRESSIVO   ' -ForegroundColor Yellow -NoNewline
    Write-Host '  (hypervisor/HAGS - assume risco)' -ForegroundColor DarkGray
    Write-Host '  [5]  FOCO EMULADORES    ' -ForegroundColor Magenta -NoNewline
    Write-Host '  (exclusoes, GPU por app, checklist)' -ForegroundColor DarkGray
    Write-Host '  [6]  Sessao de jogo     (boost temporario, reverte ao sair)' -ForegroundColor Gray
    Write-Host '  [7]  Benchmark rapido   (CPU/disco/RAM, ~20s)' -ForegroundColor Gray
    Write-Host '  [8]  Verificacao do sistema (self-check)' -ForegroundColor Gray
    Write-Host '  [9]  REVERTER backup' -ForegroundColor Red
    Write-Host '  [T]  Limpeza de temporarios / cache de shader' -ForegroundColor Gray
    Write-Host '  [L]  Logs    [B] Backups    [P] Placebos    [A] Sobre    [0] Sair' -ForegroundColor DarkGray
    Write-Host ''
}

function Read-MenuChoice {
    Write-Host '  HL> ' -ForegroundColor Cyan -NoNewline
    return (Read-Host).Trim().ToUpper()
}

# ==============================================================================
# 15. EXECUÇÃO PRINCIPAL
# ==============================================================================
$isAdmin = Assert-Admin
if (-not $isAdmin) { exit 3 }

Initialize-HLEnvironment

try { Test-NeverTouchList } catch {
    Write-Host '  [X] Falha na lista de protecao. Abortando.' -ForegroundColor Red
    exit 4
}

if (-not $NoPanel) { Show-BrandPanel }

if ($Revert) {
    Invoke-RevertHL
    Show-Summary
    exit 0
}

if (-not $script:Interactive) {
    Write-Log ('Modo nao interativo: aplicando perfil ' + $Profile) 'TITULO'
    switch ($Profile) {
        'Safe' { Invoke-ProfileSafe }
        'Balanced' { Invoke-ProfileBalanced }
        'Extreme' { Invoke-ProfileExtreme }
    }
    if ($EmulatorFocus) { Invoke-EmulatorFocus }
    Show-Summary
    exit 0
}

:mainLoop while ($true) {
    Show-Menu -Refresh $false
    $choice = Read-MenuChoice
    switch ($choice) {
        '1' { Invoke-Diagnostics; Show-Pause }
        '2' { Invoke-ProfileSafe; Show-Summary; Show-Pause }
        '3' { Invoke-ProfileBalanced; Show-Summary; Show-Pause }
        '4' { Invoke-ProfileExtreme; Show-Summary; Show-Pause }
        '5' { Invoke-EmulatorFocus; Show-Summary; Show-Pause }
        '6' { Invoke-GameSession; Show-Pause }
        '7' { Invoke-QuickBenchmark; Show-Pause }
        '8' { Invoke-SelfCheck; Show-Pause }
        '9' { Invoke-RevertHL; Show-Pause }
        'T' { Invoke-TempCleanup; Show-Pause }
        'L' { Open-FolderHL -Path $script:LogDir }
        'B' { Open-FolderHL -Path $script:BackupRoot }
        'P' { Show-PlaceboList; Show-Pause }
        'A' { Show-About; Show-Pause }
        '0' { break mainLoop }
        'Q' { break mainLoop }
        default { Write-Host '  Opcao invalida.' -ForegroundColor Yellow; Start-Sleep -Milliseconds 600 }
    }
    if ($choice -in @('2', '3', '4', '5', '6', '9')) { Show-BrandPanel }
}

Write-Host ''
Write-Host ('  ' + $script:Brand + ' v' + $script:BrandVersion + ' - encerrado. Bom jogo!') -ForegroundColor Cyan
if ($script:LogFile) { Write-Host ('  Log: ' + $script:LogFile) -ForegroundColor DarkGray }
exit 0
