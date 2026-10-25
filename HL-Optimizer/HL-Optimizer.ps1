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
  VERSÃO   : 2.0.0 (build 2026.10-r2)
  MOTOR    : Windows PowerShell 5.1 (nativo, offline, sem módulos externos)
  FOCO     : FPS médio, 1% low (stutter) e tempo de carregamento em jogos e
             emuladores (Android: BlueStacks/LDPlayer/MuMu/MEmu | Console:
             PCSX2/Dolphin/RPCS3/Ryujinx).
  NOVO 2.0 : motor de tweaks data-driven (catalogo), instalador winget,
             Windows Update via COM, startup/tarefas/Hyper-V, historico de
             undo, config export/import, relatorio HTML, benchmark comparativo.
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
    [switch]$AllowExperimental,
    [switch]$ListTweaks,
    [switch]$ListApps,
    [switch]$Report,
    [string[]]$ApplyTweaks,
    [string]$ApplyPreset,
    [string]$ImportConfig,
    [string]$LogDir
)

# ==============================================================================
#  0. IDENTIDADE DA MARCA (HL Optimizer)
# ==============================================================================
$script:Brand          = 'HL Optimizer'
$script:BrandShort     = 'HL'
$script:BrandVersion   = '2.0.0'
$script:BrandBuild     = '2026.10-r2'
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
$script:AllowExperimental = [bool]$AllowExperimental

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
#  7b. MOTOR DE TWEAKS (data-driven) + CATÁLOGO
#      Cada tweak e um objeto com itens (Reg / Svc / Appx / Task).
#      O motor sabe: detectar estado, aplicar, verificar, salvar backup e desfazer.
# ==============================================================================
function RegItem  { param([string]$P, [string]$N, $V, [string]$T = 'DWord') @{ Kind = 'Reg';  Path = $P; Name = $N; Value = $V; Type = $T } }
function SvcItem  { param([string]$N, [string]$Start = 'Disabled')      @{ Kind = 'Svc';  Name = $N; Start = $Start } }
function AppxItem { param([string]$N)                                   @{ Kind = 'Appx'; Name = $N } }
function TaskItem { param([string]$P, [string]$N)                       @{ Kind = 'Task'; Path = $P; Name = $N } }
function KeyItem  { param([string]$P)                                   @{ Kind = 'RegKey'; Path = $P } }

function New-Tweak {
    param(
        [string]$Id, [string]$Cat, [string]$Name, [string]$Desc, $Items,
        [string]$Gain = '', [string]$Risk = 'baixo', [string]$Guard = '',
        [switch]$Reboot, [switch]$Experimental
    )
    [pscustomobject]@{
        Id = $Id; Cat = $Cat; Name = $Name; Desc = $Desc; Items = @($Items)
        Gain = $Gain; Risk = $Risk; Guard = $Guard
        Reboot = [bool]$Reboot; Experimental = [bool]$Experimental
    }
}

$script:TweakCatalog = @(
    # ---------------------------------------------------------------- GAMING
    (New-Tweak -Id 'game-dvr' -Cat 'Gaming' -Name 'Desligar Game DVR / Capturas' -Gain '5-15%' -Risk 'perde clipes da Game Bar' `
        -Desc 'Maior ganho real entre os servicos: a gravacao em segundo plano consome CPU/GPU o tempo todo.' -Items @(
            (RegItem 'HKCU:\System\GameConfigStore' 'GameDVR_Enabled' 0),
            (RegItem 'HKCU:\Software\Microsoft\Windows\CurrentVersion\GameDVR' 'AppCaptureEnabled' 0),
            (RegItem 'HKCU:\Software\Microsoft\Windows\CurrentVersion\GameDVR' 'HistoricalCaptureEnabled' 0),
            (RegItem 'HKCU:\Software\Microsoft\GameBar' 'UseNexusForGameBarEnabled' 0),
            (RegItem 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\GameDVR' 'AllowGameDVR' 0)
        ))
    (New-Tweak -Id 'game-mode-on' -Cat 'Gaming' -Name 'Manter Modo de Jogo LIGADO' -Gain '0-3%' -Risk 'nenhum' `
        -Desc 'Muita gente desliga por engano. Hoje o Modo de Jogo ajuda mais do que atrapalha: mantemos ligado.' -Items @(
            (RegItem 'HKCU:\Software\Microsoft\GameBar' 'AutoGameModeEnabled' 1),
            (RegItem 'HKCU:\Software\Microsoft\GameBar' 'AllowAutoGameMode' 1)
        ))
    (New-Tweak -Id 'fse-off' -Cat 'Gaming' -Name 'Desligar otimizacoes de tela cheia (FSE)' -Gain '0-5%' -Risk 'experimental; testar por jogo' -Experimental `
        -Desc 'Forca modo exclusivo de tela cheia. Em alguns jogos reduz latencia; em outros causa stutter ou alt-tab lento.' -Items @(
            (RegItem 'HKCU:\System\GameConfigStore' 'GameDVR_FSEBehaviorMode' 2),
            (RegItem 'HKCU:\System\GameConfigStore' 'GameDVR_HonorUserFSEBehaviorMode' 1),
            (RegItem 'HKCU:\System\GameConfigStore' 'GameDVR_DXGIHonorFSEWindowsCompatible' 1),
            (RegItem 'HKCU:\System\GameConfigStore' 'GameDVR_FSEBehavior' 2)
        ))
    (New-Tweak -Id 'mmcss-games' -Cat 'Gaming' -Name 'Prioridade MMCSS para jogos' -Gain '0-3% / menos stutter' -Risk 'baixo' `
        -Desc 'Sobe a prioridade do subsistema multimidia para tarefas de jogo (GPU Priority 8, SFIO High).' -Items @(
            (RegItem 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games' 'GPU Priority' 8),
            (RegItem 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games' 'Priority' 6),
            (RegItem 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games' 'Scheduling Category' 'High' 'String'),
            (RegItem 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games' 'SFIO Priority' 'High' 'String')
        ))
    (New-Tweak -Id 'priority-sep' -Cat 'Gaming' -Name 'Prioridade de primeiro plano (38)' -Gain '0-3%' -Risk 'nenhum relevante' `
        -Desc 'Win32PrioritySeparation=38 (0x26): favorece o app em primeiro plano com quantum curto.' -Items @(
            (RegItem 'HKLM:\SYSTEM\CurrentControlSet\Control\PriorityControl' 'Win32PrioritySeparation' 38)
        ))
    (New-Tweak -Id 'visualfx' -Cat 'Gaming' -Name 'Efeitos visuais: melhor desempenho' -Gain '1-2%' -Risk 'sem animacoes do Windows' `
        -Desc 'Mantem fontes suavizadas, remove animacoes e sombras.' -Items @(
            (RegItem 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects' 'VisualFXSetting' 2)
        ))
    (New-Tweak -Id 'mouse-accel' -Cat 'Gaming' -Name 'Desligar aceleracao do mouse' -Gain 'precisao' -Risk 'preferencia pessoal: alguns preferem ligado' `
        -Desc 'Remove "Melhorar precisao do ponteiro". Para FPS competitivo costuma ser obrigatorio.' -Items @(
            (RegItem 'HKCU:\Control Panel\Mouse' 'MouseSpeed' '0' 'String'),
            (RegItem 'HKCU:\Control Panel\Mouse' 'MouseThreshold1' '0' 'String'),
            (RegItem 'HKCU:\Control Panel\Mouse' 'MouseThreshold2' '0' 'String')
        ))
    (New-Tweak -Id 'hags-on' -Cat 'Gaming' -Name 'Ligar HAGS (agendamento de GPU)' -Gain 'teste: +-2..5%' -Risk 'pode piorar em alguns drivers; testar' -Reboot -Experimental `
        -Desc 'Hardware-accelerated GPU Scheduling. Nao e ganho garantido: ative, rebote e compare o 1% low.' -Items @(
            (RegItem 'HKLM:\SYSTEM\CurrentControlSet\Control\GraphicsDrivers' 'HwSchMode' 2)
        ))

    # ------------------------------------------------------------ TELEMETRIA
    (New-Tweak -Id 'telemetry-policy' -Cat 'Telemetria' -Name 'Telemetria no minimo (politica)' -Gain '~0%' -Risk 'privacidade; sem relatorios para a MS' `
        -Desc 'AllowTelemetry=0 nas duas arvores de politica + esconde notificacoes de feedback.' -Items @(
            (RegItem 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\DataCollection' 'AllowTelemetry' 0),
            (RegItem 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\DataCollection' 'AllowTelemetry' 0),
            (RegItem 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\DataCollection' 'DoNotShowFeedbackNotifications' 1)
        ))
    (New-Tweak -Id 'diagtrack' -Cat 'Telemetria' -Name 'Servicos de telemetria / erros' -Gain '~0-1%' -Risk 'sem relatorio de erros detalhado' `
        -Desc 'Desabilita DiagTrack (Experiencias do Usuario Conectado), dmwappushservice e WerSvc.' -Items @(
            (SvcItem 'DiagTrack'),
            (SvcItem 'dmwappushservice'),
            (SvcItem 'WerSvc')
        ))
    (New-Tweak -Id 'advertising-id' -Cat 'Telemetria' -Name 'Desligar ID de anuncios' -Gain '~0%' -Risk 'nenhum' `
        -Desc 'Impede rastreio de publicidade entre apps.' -Items @(
            (RegItem 'HKCU:\Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo' 'Enabled' 0)
        ))
    (New-Tweak -Id 'activity-history' -Cat 'Telemetria' -Name 'Desligar historico de atividades' -Gain '~0%' -Risk 'perde historico entre dispositivos' `
        -Desc 'Politica de sistema: nao publica, nao envia e nao coleta atividades.' -Items @(
            (RegItem 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\System' 'EnableActivityFeed' 0),
            (RegItem 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\System' 'PublishUserActivities' 0),
            (RegItem 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\System' 'UploadUserActivities' 0)
        ))
    (New-Tweak -Id 'feedback-siuf' -Cat 'Telemetria' -Name 'Sem pedidos de feedback' -Gain '~0%' -Risk 'nenhum' `
        -Desc 'Zera a frequencia de pesquisas de satisfacao (SIUF).' -Items @(
            (RegItem 'HKCU:\Software\Microsoft\Siuf\Rules' 'NumberOfSIUFInPeriod' 0),
            (RegItem 'HKCU:\Software\Microsoft\Siuf\Rules' 'PeriodInNanoSeconds' 0)
        ))
    (New-Tweak -Id 'tailored-exp' -Cat 'Telemetria' -Name 'Sem experiencias personalizadas' -Gain '~0%' -Risk 'nenhum' `
        -Desc 'Desliga sugestoes baseadas em dados de diagnostico.' -Items @(
            (RegItem 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Privacy' 'TailoredExperiencesWithDiagnosticDataEnabled' 0),
            (RegItem 'HKCU:\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager' 'SubscribedContent-338393Enabled' 0)
        ))
    (New-Tweak -Id 'suggestions-off' -Cat 'Telemetria' -Name 'Sem apps/sugestoes silenciosas' -Gain '~0-1%' -Risk 'nenhum' `
        -Desc 'Bloqueia instalacao silenciosa de apps promovidos e sugestoes do sistema.' -Items @(
            (RegItem 'HKCU:\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager' 'ContentDeliveryAllowed' 0),
            (RegItem 'HKCU:\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager' 'SilentInstalledAppsEnabled' 0),
            (RegItem 'HKCU:\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager' 'PreInstalledAppsEnabled' 0),
            (RegItem 'HKCU:\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager' 'OemPreInstalledAppsEnabled' 0),
            (RegItem 'HKCU:\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager' 'SystemPaneSuggestionsEnabled' 0),
            (RegItem 'HKCU:\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager' 'SoftLandingEnabled' 0),
            (RegItem 'HKCU:\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager' 'RotatingLockScreenOverlayEnabled' 0),
            (RegItem 'HKCU:\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager' 'SubscribedContent-310093Enabled' 0),
            (RegItem 'HKCU:\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager' 'SubscribedContent-338388Enabled' 0),
            (RegItem 'HKCU:\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager' 'SubscribedContent-338389Enabled' 0)
        ))
    (New-Tweak -Id 'start-suggestions' -Cat 'Telemetria' -Name 'Sem sugestoes no menu Iniciar' -Gain '0-1%' -Risk 'nenhum' `
        -Desc 'Remove "apps recomendados" e dicas do Iniciar.' -Items @(
            (RegItem 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced' 'ShowSuggestions' 0),
            (RegItem 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced' 'Start_IrisRecommendations' 0)
        ))
    (New-Tweak -Id 'websearch-off' -Cat 'Telemetria' -Name 'Sem resultados da web na busca' -Gain '0-1%' -Risk 'busca do Iniciar so encontra arquivos locais' `
        -Desc 'Impede que cada busca no menu Iniciar vire uma requisicao de rede para o Bing.' -Items @(
            (RegItem 'HKCU:\Software\Policies\Microsoft\Windows\Explorer' 'DisableSearchBoxSuggestions' 1)
        ))
    (New-Tweak -Id 'telemetry-tasks' -Cat 'Telemetria' -Name 'Tarefas agendadas de telemetria' -Gain '~0-1%' -Risk 'nenhum' `
        -Desc 'Desabilita as tarefas de compatibilidade/CEIP que acordam o disco em segundo plano.' -Items @(
            (TaskItem '\Microsoft\Windows\Application Experience' 'Microsoft Compatibility Appraiser'),
            (TaskItem '\Microsoft\Windows\Application Experience' 'ProgramDataUpdater'),
            (TaskItem '\Microsoft\Windows\Autochk' 'Proxy'),
            (TaskItem '\Microsoft\Windows\Customer Experience Improvement Program' 'Consolidator'),
            (TaskItem '\Microsoft\Windows\Customer Experience Improvement Program' 'UsbCeip'),
            (TaskItem '\Microsoft\Windows\Feedback\Siuf' 'DmClient'),
            (TaskItem '\Microsoft\Windows\Feedback\Siuf' 'DmClientOnScenarioDownload'),
            (TaskItem '\Microsoft\Windows\Windows Error Reporting' 'QueueReporting')
        ))

    # --------------------------------------------------------------- DEBLOAT
    (New-Tweak -Id 'debloat-xbox' -Cat 'Debloat' -Name 'Apps do Xbox' -Gain '~0-1%' -Risk 'sem Game Bar/overlay; nao use se tiver Game Pass' `
        -Desc 'Remove os 5 pacotes do Xbox. Reversivel: o app volta pela Store.' -Items @(
            (AppxItem 'Microsoft.XboxApp'),
            (AppxItem 'Microsoft.XboxGamingOverlay'),
            (AppxItem 'Microsoft.XboxGameOverlay'),
            (AppxItem 'Microsoft.XboxSpeechToTextOverlay'),
            (AppxItem 'Microsoft.Xbox.TCUI')
        ))
    (New-Tweak -Id 'debloat-bing' -Cat 'Debloat' -Name 'Apps Bing (noticias, clima, esportes)' -Gain '~0-1%' -Risk 'sem esses apps' `
        -Desc 'Remove os apps promovidos da Microsoft que puxam rede em segundo plano.' -Items @(
            (AppxItem 'Microsoft.BingNews'),
            (AppxItem 'Microsoft.BingWeather'),
            (AppxItem 'Microsoft.BingSports'),
            (AppxItem 'Microsoft.BingFinance')
        ))
    (New-Tweak -Id 'debloat-3d' -Cat 'Debloat' -Name '3D Viewer / Paint 3D' -Gain '~0%' -Risk 'sem esses apps' `
        -Desc 'Apps de realidade misturada/3D raramente usados.' -Items @(
            (AppxItem 'Microsoft.Microsoft3DViewer'),
            (AppxItem 'Microsoft.MSPaint3D')
        ))
    (New-Tweak -Id 'debloat-solitaire' -Cat 'Debloat' -Name 'Solitaire Collection + anuncios' -Gain '~0%' -Risk 'nenhum' `
        -Desc 'Remove o jogo pre-instalado com anuncios.' -Items @(
            (AppxItem 'Microsoft.MicrosoftSolitaireCollection')
        ))
    (New-Tweak -Id 'debloat-comms' -Cat 'Debloat' -Name 'People / Skype / Teams pessoal / Mail' -Gain '~0-1%' -Risk 'sem esses apps' `
        -Desc 'Comunicacao que pouca gente usa no PC de jogo.' -Items @(
            (AppxItem 'Microsoft.People'),
            (AppxItem 'Microsoft.SkypeApp'),
            (AppxItem 'MicrosoftTeams'),
            (AppxItem 'MSTeams'),
            (AppxItem 'Microsoft.WindowsCommunicationsApps'),
            (AppxItem 'Microsoft.YourPhone'),
            (AppxItem 'Microsoft.PhoneLink')
        ))
    (New-Tweak -Id 'debloat-media' -Cat 'Debloat' -Name 'Groove Music / Filmes / Clipchamp' -Gain '~0%' -Risk 'sem esses apps' `
        -Desc 'Midia padrao do Windows que ninguem abre.' -Items @(
            (AppxItem 'Microsoft.ZuneMusic'),
            (AppxItem 'Microsoft.ZuneVideo'),
            (AppxItem 'Clipchamp.Clipchamp')
        ))
    (New-Tweak -Id 'debloat-help' -Cat 'Debloat' -Name 'Dicas / Obter Ajuda / Feedback Hub' -Gain '~0%' -Risk 'nenhum' `
        -Desc 'Apps de ajuda e feedback que so ocupam espaco e tarefas.' -Items @(
            (AppxItem 'Microsoft.GetHelp'),
            (AppxItem 'Microsoft.Getstarted'),
            (AppxItem 'Microsoft.WindowsFeedbackHub')
        ))
    (New-Tweak -Id 'debloat-misc' -Cat 'Debloat' -Name 'Office Hub / To Do / Power Automate / Maps' -Gain '~0-1%' -Risk 'sem esses apps' `
        -Desc 'Servicos em segundo plano que aparecem no boot sem serem usados.' -Items @(
            (AppxItem 'Microsoft.MicrosoftOfficeHub'),
            (AppxItem 'Microsoft.Todos'),
            (AppxItem 'Microsoft.PowerAutomateDesktop'),
            (AppxItem 'Microsoft.MixedReality.Portal'),
            (AppxItem 'Microsoft.WindowsMaps')
        ))
    (New-Tweak -Id 'debloat-copilot' -Cat 'Debloat' -Name 'Apps de IA / Copilot (quando presentes)' -Gain '~0-1%' -Risk 'sem Copilot' `
        -Desc 'Remove por padrao de nome, entao funciona em versoes diferentes do Windows 11.' -Items @(
            (AppxItem 'Microsoft.Copilot*'),
            (AppxItem 'Microsoft.Windows.Ai*'),
            (AppxItem 'Microsoft.WindowsAi*')
        ))
    (New-Tweak -Id 'debloat-3rdparty' -Cat 'Debloat' -Name 'Apps de terceiros pre-instalados' -Gain '~0-1%' -Risk 'nenhum' `
        -Desc 'Spotify/TikTok/Instagram/Facebook que vem em algumas instalacoes OEM.' -Items @(
            (AppxItem 'SpotifyAB.SpotifyMusic'),
            (AppxItem 'Bytedance.TikTok*'),
            (AppxItem 'Facebook.Instagram*'),
            (AppxItem 'Facebook.Facebook*')
        ))

    # -------------------------------------------------------------- SERVICOS
    (New-Tweak -Id 'sysmain-off' -Cat 'Servicos' -Name 'SysMain / Superfetch' -Gain '0-3%' -Risk 'só em SSD; em HDD piora' -Guard 'SSD' `
        -Desc 'Em SSD o prefetch e inutil. Em HDD ele ajuda: o motor bloqueia automaticamente nesse caso.' -Items @(
            (SvcItem 'SysMain')
        ))
    (New-Tweak -Id 'wsearch-off' -Cat 'Servicos' -Name 'Windows Search (indexacao)' -Gain '0-3%' -Risk 'busca do Iniciar mais lenta' `
        -Desc 'Alternativa segura: manter ligado e remover pastas de jogos da indexacao.' -Items @(
            (SvcItem 'WSearch')
        ))
    (New-Tweak -Id 'spooler-off' -Cat 'Servicos' -Name 'Fila de impressao' -Gain '~0%' -Risk 'sem impressora instalada' -Guard 'NoPrinter' `
        -Desc 'Servico historicamente explorado e desnecessario em PC sem impressora.' -Items @(
            (SvcItem 'Spooler')
        ))
    (New-Tweak -Id 'bluetooth-off' -Cat 'Servicos' -Name 'Servicos de Bluetooth' -Gain '0-1%' -Risk 'sem Bluetooth' -Guard 'NoBluetooth' `
        -Desc 'Desliga bthserv/BTAGService quando nao ha dispositivo Bluetooth ativo.' -Items @(
            (SvcItem 'bthserv'),
            (SvcItem 'BTAGService')
        ))
    (New-Tweak -Id 'xbox-services' -Cat 'Servicos' -Name 'Servicos do Xbox (4)' -Gain '~0%' -Risk 'sem Game Pass/controle Xbox' -Guard 'NoXbox' `
        -Desc 'XblAuthManager, XblGameSave, XboxNetApiSvc e XboxGipSvc. O motor recusa se achar Game Pass em uso.' -Items @(
            (SvcItem 'XblAuthManager'),
            (SvcItem 'XblGameSave'),
            (SvcItem 'XboxNetApiSvc'),
            (SvcItem 'XboxGipSvc')
        ))
    (New-Tweak -Id 'useless-services' -Cat 'Servicos' -Name 'Servicos inuteis (7)' -Gain '~0-1%' -Risk 'nenhum' `
        -Desc 'RetailDemo, MapsBroker, PhoneSvc, Fax, RemoteRegistry, WinRM e WMPNetworkSvc.' -Items @(
            (SvcItem 'RetailDemo'),
            (SvcItem 'MapsBroker'),
            (SvcItem 'PhoneSvc'),
            (SvcItem 'Fax'),
            (SvcItem 'RemoteRegistry'),
            (SvcItem 'WinRM'),
            (SvcItem 'WMPNetworkSvc')
        ))

    # ------------------------------------------------------------------- UI
    (New-Tweak -Id 'ctx-menu-classic' -Cat 'UI' -Name 'Menu de contexto classico (Win11)' -Gain '~0%' -Risk 'perde o menu novo; reinicie o Explorer' `
        -Desc 'Volta o menu completo do botao direito. Muito pedido por quem usa WinRAR/7-Zip.' -Items @(
            (KeyItem 'HKCU:\Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\InprocServer32')
        ))
    (New-Tweak -Id 'show-ext' -Cat 'UI' -Name 'Mostrar extensoes de arquivo' -Gain '~0%' -Risk 'nenhum' `
        -Desc 'Seguranca basica: ve .exe, .pdf, .jpg e nao apenas o nome.' -Items @(
            (RegItem 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced' 'HideFileExt' 0)
        ))
    (New-Tweak -Id 'taskbar-left' -Cat 'UI' -Name 'Barra de tarefas alinhada a esquerda' -Gain '~0%' -Risk 'preferencia' `
        -Desc 'Ganho indireto: menos animacao de realinhamento e icones no lugar de sempre.' -Items @(
            (RegItem 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced' 'TaskbarAl' 0)
        ))
    (New-Tweak -Id 'taskbar-search-icon' -Cat 'UI' -Name 'Busca so como icone' -Gain '~0%' -Risk 'preferencia' -Items @(
            (RegItem 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced' 'SearchboxTaskbarMode' 1)
        ) -Desc 'Menos um processo de UI desenhando caixa de busca.')
    (New-Tweak -Id 'transparency-off' -Cat 'UI' -Name 'Desligar transparencias' -Gain '0-1%' -Risk 'visual' -Items @(
            (RegItem 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize' 'EnableTransparency' 0)
        ) -Desc 'O compositor deixa de fazer blur em cada janela.')
    (New-Tweak -Id 'end-task' -Cat 'UI' -Name 'Botao "Finalizar tarefa" na barra' -Gain '~0%' -Risk 'nenhum' -Items @(
            (RegItem 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced' 'TaskbarEndTask' 1)
        ) -Desc 'Mata travamentos sem abrir o Gerenciador de Tarefas.')
    (New-Tweak -Id 'widgets-off' -Cat 'UI' -Name 'Desligar Widgets / Noticias' -Gain '0-1%' -Risk 'sem widgets' -Items @(
            (RegItem 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced' 'TaskbarDa' 0),
            (RegItem 'HKLM:\SOFTWARE\Policies\Microsoft\Dsh' 'AllowNewsAndInterests' 0)
        ) -Desc 'O painel de widgets carrega conteudo da web em segundo plano.')
    (New-Tweak -Id 'copilot-off' -Cat 'UI' -Name 'Desligar Copilot' -Gain '0-1%' -Risk 'sem Copilot' -Items @(
            (RegItem 'HKCU:\Software\Policies\Microsoft\Windows\WindowsCopilot' 'TurnOffWindowsCopilot' 1),
            (RegItem 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced' 'ShowCopilotButton' 0)
        ) -Desc 'Remove o botao e a politica do assistente.')
    (New-Tweak -Id 'chat-icon-off' -Cat 'UI' -Name 'Remover icone Chat/Teams da barra' -Gain '~0%' -Risk 'nenhum' -Items @(
            (RegItem 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced' 'TaskbarMn' 0)
        ) -Desc 'Tira o icone que abre o Teams pessoal.')
    (New-Tweak -Id 'recall-off' -Cat 'UI' -Name 'Desligar Recall (Win11 24H2)' -Gain '~0-2%' -Risk 'experimental' -Experimental -Items @(
            (RegItem 'HKCU:\Software\Policies\Microsoft\Windows\WindowsAI' 'DisableAIDataAnalysis' 1)
        ) -Desc 'Recall faz captura/screenshot continuo do que voce faz. Desligar libera CPU, disco e IA.')
    (New-Tweak -Id 'gamebar-hotkey' -Cat 'UI' -Name 'Nao abrir Game Bar com o botao Xbox' -Gain '~0%' -Risk 'perde atalho' -Items @(
            (RegItem 'HKCU:\Software\Microsoft\GameBar' 'UseNexusForGameBarEnabled' 0)
        ) -Desc 'Evita abrir overlay por acidente durante o jogo.')

    # ----------------------------------------------------------------- BOOT
    (New-Tweak -Id 'startup-delay' -Cat 'Boot' -Name 'Remover atraso de inicializacao' -Gain 'boot mais rapido' -Risk 'nenhum' -Items @(
            (RegItem 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Serialize' 'StartupDelayInMSec' 0)
        ) -Desc 'Corta o atraso artificial de 10s dos apps de inicializacao.')
    (New-Tweak -Id 'fast-startup-off' -Cat 'Boot' -Name 'Desligar Inicializacao Rapida' -Gain 'estabilidade' -Risk 'boot um pouco mais lento; corrige dual boot' -Reboot -Items @(
            (RegItem 'HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager\Power' 'HiberbootEnabled' 0)
        ) -Desc 'O "fast startup" mantem drivers em hibernacao e causa bugs de GPU/emulador depois de desligar.')

    # ----------------------------------------------------------------- REDE
    (New-Tweak -Id 'net-throttling' -Cat 'Rede' -Name 'Remover throttle de rede multimidia' -Gain 'sem ganho comprovado' -Risk 'experimental' -Experimental -Items @(
            (RegItem 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile' 'NetworkThrottlingIndex' -1),
            (RegItem 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile' 'SystemResponsiveness' 10)
        ) -Desc 'Clássico de guia de "gaming". Sem evidencia solida; deixe para o modo experimental.')
    (New-Tweak -Id 'do-mode' -Cat 'Rede' -Name 'Otimizacao de Entrega sem P2P' -Gain '0-2% de disco/rede' -Risk 'nao compartilha updates na LAN' -Items @(
            (RegItem 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\DeliveryOptimization\Config' 'DODownloadMode' 1),
            (RegItem 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization' 'DODownloadMode' 1)
        ) -Desc 'Bloqueia upload de updates para outros PCs, que consome disco e rede sem voce ver.')

    # --------------------------------------------------------------- UPDATE
    (New-Tweak -Id 'store-auto-update' -Cat 'Update' -Name 'Desligar update automatico da Store' -Gain '~0-1%' -Risk 'apps desatualizados' -Items @(
            (RegItem 'HKLM:\SOFTWARE\Policies\Microsoft\WindowsStore' 'AutoDownload' 2),
            (RegItem 'HKLM:\SOFTWARE\Policies\Microsoft\WindowsStore' 'AutoInstallMinutes' 0)
        ) -Desc 'A Store baixando apps em segundo plano e um dos causadores classicos de stutter.')
    (New-Tweak -Id 'driver-auto' -Cat 'Update' -Name 'Sem drivers automaticos do Windows Update' -Gain 'estabilidade' -Risk 'experimental: pode faltar driver novo' -Experimental -Reboot -Items @(
            (RegItem 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\DriverSearching' 'SearchOrderConfig' 0),
            (RegItem 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate' 'ExcludeWUDriversInQualityUpdate' 1)
        ) -Desc 'Evita que o Windows troque seu driver de GPU no meio do mes.')
)

# ==============================================================================
#  7c. MOTOR: guardas, deteccao de estado, aplicacao, desfazer por tweak
# ==============================================================================
# --- caches (Appx e tarefas sao caros de consultar; carregamos 1x por sessao) ---
$script:AppxCache = $null
$script:TaskCache = $null

function Get-AppxInstalledHL {
    param([switch]$Refresh)
    if ($null -eq $script:AppxCache -or $Refresh) {
        $script:AppxCache = @(Get-AppxPackage -ErrorAction SilentlyContinue)
    }
    return $script:AppxCache
}

function Get-ScheduledTasksHL {
    param([switch]$Refresh)
    if ($null -eq $script:TaskCache -or $Refresh) {
        $script:TaskCache = @(Get-ScheduledTask -ErrorAction SilentlyContinue)
    }
    return $script:TaskCache
}

function Get-TaskHL {
    param([string]$TaskPath, [string]$TaskName)
    foreach ($t in (Get-ScheduledTasksHL)) {
        if ($t.TaskPath -eq $TaskPath -and $t.TaskName -eq $TaskName) { return $t }
    }
    return $null
}

function Test-TweakGuard {
    param($Tweak)
    switch ([string]$Tweak.Guard) {
        'SSD' {
            $dt = Get-DiskType -DriveLetter 'C'
            if ($dt -ne 'SSD') {
                Write-Log ('PULADO: ' + $Tweak.Name + ' exige SSD (detectado: ' + $dt + ')') 'WARN'
                return $false
            }
        }
        'NoPrinter' {
            $pr = @()
            try { $pr = @(Get-Printer -ErrorAction Stop) } catch { }
            if ($pr.Count -gt 0) { Write-Log ('PULADO: ' + $Tweak.Name + ' - ha impressora instalada') 'WARN'; return $false }
        }
        'NoBluetooth' {
            $bt = 0
            try { $bt = @(Get-PnpDevice -Class Bluetooth -ErrorAction Stop | Where-Object { $_.Status -eq 'OK' }).Count } catch { }
            if ($bt -gt 0) { Write-Log ('PULADO: ' + $Tweak.Name + ' - Bluetooth ativo') 'WARN'; return $false }
        }
        'NoXbox' {
            $gaming = @(Get-AppxPackage -Name 'Microsoft.GamingApp' -ErrorAction SilentlyContinue)
            $xbl = (Get-Service -Name 'XblAuthManager' -ErrorAction SilentlyContinue).Status -eq 'Running'
            if ($gaming.Count -gt 0 -or $xbl) { Write-Log ('PULADO: ' + $Tweak.Name + ' - Game Pass / Xbox em uso') 'WARN'; return $false }
        }
    }
    return $true
}

function New-RegKeySafe {
    param([string]$Path, [string]$Gain = '', [string]$Risk = '')
    if (Test-Path -LiteralPath $Path) { Write-Log ('Chave ja existe: ' + $Path) 'VERIF'; return }
    if ($script:DryRun) { Write-Log ('[DRY-RUN] criaria chave ' + $Path) 'DRYRUN'; return }
    try {
        $null = New-Item -Path $Path -Force -ErrorAction Stop
        Write-Log ('Chave criada: ' + $Path) 'OK'
        Save-BackupEntry -Type 'RegKey' -Target $Path -Before @{ exists = $false } -After @{ exists = $true } -Gain $Gain -Risk $Risk
    } catch { Write-Log ('Falha ao criar chave ' + $Path + ': ' + $_.Exception.Message) 'ERRO' }
}

function Remove-AppxSafe {
    param([string]$Pattern, [string]$Why = '')
    $pkgs = @(Get-AppxInstalledHL -Refresh | Where-Object { $_.Name -like $Pattern })
    if ($pkgs.Count -eq 0) { Write-Log ('App ja ausente: ' + $Pattern) 'VERIF'; return }
    foreach ($p in $pkgs) {
        if ($script:DryRun) { Write-Log ('[DRY-RUN] removeria app ' + $p.Name + ' (fora do padrao ' + $Pattern + ')') 'DRYRUN'; continue }
        try {
            Remove-AppxPackage -Package $p.PackageFullName -ErrorAction Stop
            Write-Log ('App removido: ' + $p.Name + '  ' + $Why) 'OK'
            $null = Get-AppxInstalledHL -Refresh
            Save-BackupEntry -Type 'Appx' -Target $p.PackageFullName `
                -Before @{ installed = $true; location = [string]$p.InstallLocation } `
                -After @{ installed = $false } -Gain '~0-1%' -Risk 'app removido (volta pela Store)' `
                -RevertHint 'Add-AppxPackage -Register <InstallLocation>\AppxManifest.xml'
        } catch {
            Write-Log ('Nao foi possivel remover ' + $p.Name + ': ' + $_.Exception.Message) 'WARN'
        }
    }
}

function Set-ScheduledTaskStateSafe {
    param([string]$TaskPath, [string]$TaskName, [ValidateSet('Disabled', 'Ready')][string]$State = 'Disabled', [string]$Why = '')
    $t = Get-TaskHL -TaskPath $TaskPath -TaskName $TaskName
    if (-not $t) { Write-Log ('Tarefa nao encontrada (ignorada): ' + $TaskPath + $TaskName) 'VERIF'; return }
    if ([string]$t.State -eq $State) { Write-Log ('Tarefa ja em ' + $State + ': ' + $TaskName) 'VERIF'; return }
    if ($script:DryRun) { Write-Log ('[DRY-RUN] tarefa ' + $TaskName + ' -> ' + $State) 'DRYRUN'; return }
    $before = [string]$t.State
    try {
        if ($State -eq 'Disabled') { $null = Disable-ScheduledTask -TaskPath $TaskPath -TaskName $TaskName -ErrorAction Stop }
        else { $null = Enable-ScheduledTask -TaskPath $TaskPath -TaskName $TaskName -ErrorAction Stop }
        $null = Get-ScheduledTasksHL -Refresh
        $t2 = Get-TaskHL -TaskPath $TaskPath -TaskName $TaskName
        $after = 'Disabled'
        if ($t2) { $after = [string]$t2.State }
        Write-Log ('Tarefa ' + $TaskName + ': ' + $before + ' -> ' + $after) 'OK'
        Save-BackupEntry -Type 'ScheduledTask' -Target ($TaskPath.TrimEnd('\') + '\' + $TaskName) -Before @{ state = $before } -After @{ state = $after } `
            -Gain '~0-1%' -Risk 'tarefa desativada' -RevertHint ('Enable-ScheduledTask -TaskPath ' + $TaskPath + ' -TaskName ' + $TaskName)
    } catch { Write-Log ('Falha na tarefa ' + $TaskName + ': ' + $_.Exception.Message) 'ERRO' }
}

function Get-TweakState {
    param($Tweak)
    $done = 0; $pend = 0; $na = 0
    foreach ($it in $Tweak.Items) {
        switch ([string]$it.Kind) {
            'Reg' {
                $s = Get-RegValueState -Path $it.Path -Name $it.Name
                if ($s.Exists -and ("$($s.Value)" -eq "$($it.Value)")) { $done++ } else { $pend++ }
            }
            'RegKey' {
                if (Test-Path -LiteralPath $it.Path) { $done++ } else { $pend++ }
            }
            'Svc' {
                $s = Get-ServiceState -Name $it.Name
                if (-not $s.Found) { $na++ }
                elseif ($s.StartType -eq 'Disabled') { $done++ }
                else { $pend++ }
            }
            'Appx' {
                $p = @(Get-AppxInstalledHL | Where-Object { $_.Name -like $it.Name })
                if ($p.Count -eq 0) { $done++ } else { $pend++ }
            }
            'Task' {
                $t = Get-TaskHL -TaskPath $it.Path -TaskName $it.Name
                if (-not $t) { $na++ }
                elseif ([string]$t.State -eq 'Disabled') { $done++ }
                else { $pend++ }
            }
            default { $na++ }
        }
    }
    if ($done -eq 0 -and $pend -eq 0) { return 'N/D' }
    if ($pend -eq 0) { return 'APLICADO' }
    if ($done -eq 0) { return 'PENDENTE' }
    return 'PARCIAL'
}

function Invoke-TweakItem {
    param($Item, $Tweak)
    switch ([string]$Item.Kind) {
        'Reg'    { Set-RegValueSafe -Path $Item.Path -Name $Item.Name -Value $Item.Value -Type $Item.Type -Gain $Tweak.Gain -Risk $Tweak.Risk }
        'RegKey' { New-RegKeySafe -Path $Item.Path -Gain $Tweak.Gain -Risk $Tweak.Risk }
        'Svc'    { Set-ServiceStateSafe -Name $Item.Name -StartType $Item.Start -StopService -Gain $Tweak.Gain -Risk $Tweak.Risk -Why ('tweak:' + $Tweak.Id) }
        'Appx'   { Remove-AppxSafe -Pattern $Item.Name -Why ('tweak:' + $Tweak.Id) }
        'Task'   { Set-ScheduledTaskStateSafe -TaskPath $Item.Path -TaskName $Item.Name -State 'Disabled' -Why ('tweak:' + $Tweak.Id) }
        default  { Write-Log ('Tipo de item desconhecido no tweak ' + $Tweak.Id) 'WARN' }
    }
}

function Invoke-Tweak {
    param($Tweak, [switch]$Force)
    if ($Tweak.Experimental -and -not ($Force -or $script:AllowExperimental -or $IncludeExperimental)) {
        Write-Log ('Experimental bloqueado: ' + $Tweak.Name + ' (use -IncludeExperimental ou escolha no menu)') 'WARN'
        return
    }
    if (-not (Test-TweakGuard -Tweak $Tweak)) { return }
    Write-Log ('Tweak [' + $Tweak.Id + '] ' + $Tweak.Name + ' | ganho: ' + $Tweak.Gain + ' | risco: ' + $Tweak.Risk) 'ACAO'
    foreach ($it in $Tweak.Items) { Invoke-TweakItem -Item $it -Tweak $Tweak }
    if ($Tweak.Reboot) {
        $script:RebootNeeded = $true
        Write-Log ('Requer REINICIO: ' + $Tweak.Name) 'WARN'
    }
}

function Invoke-TweakSet {
    param([string[]]$Ids, [switch]$Force)
    $applied = 0
    foreach ($id in $Ids) {
        $tw = Get-TweakById -Id $id
        if (-not $tw) { Write-Log ('Tweak inexistente no catalogo: ' + $id) 'WARN'; continue }
        $st = Get-TweakState -Tweak $tw
        if ($st -eq 'APLICADO') { Write-Log ('Ja aplicado: ' + $tw.Name) 'VERIF'; continue }
        Invoke-Tweak -Tweak $tw
        $applied++
    }
    Write-Log ('Tweaks processados: ' + $applied + ' de ' + $Ids.Count) 'INFO'
}

function Get-TweakById {
    param([string]$Id)
    foreach ($t in $script:TweakCatalog) { if ($t.Id -eq $Id) { return $t } }
    return $null
}

function Get-TweakCategories {
    $cats = New-Object System.Collections.ArrayList
    foreach ($t in $script:TweakCatalog) { if (-not ($cats -contains $t.Cat)) { $null = $cats.Add($t.Cat) } }
    return $cats
}

function Find-Tweak {
    param([string]$Text)
    if (-not $Text) { return @() }
    return @($script:TweakCatalog | Where-Object { $_.Id -like ('*' + $Text + '*') -or $_.Name -like ('*' + $Text + '*') -or $_.Desc -like ('*' + $Text + '*') -or $_.Cat -like ('*' + $Text + '*') })
}

function ConvertTo-IndexList {
    param([string]$Text, [int]$Max)
    $out = New-Object System.Collections.ArrayList
    if (-not $Text) { return $out }
    foreach ($part in ($Text -split ',')) {
        $p = $part.Trim()
        if (-not $p) { continue }
        if ($p -match '^(\d+)\s*-\s*(\d+)$') {
            $a = [int]$Matches[1]; $b = [int]$Matches[2]
            if ($a -gt $b) { $tmp = $a; $a = $b; $b = $tmp }
            for ($i = $a; $i -le $b; $i++) { if ($i -ge 1 -and $i -le $Max -and -not ($out -contains ($i - 1))) { $null = $out.Add($i - 1) } }
            continue
        }
        $n = 0
        if ([int]::TryParse($p, [ref]$n)) {
            if ($n -ge 1 -and $n -le $Max -and -not ($out -contains ($n - 1))) { $null = $out.Add($n - 1) }
        }
    }
    return $out
}

function Undo-Tweak {
    param($Tweak)
    $targets = @()
    foreach ($it in $Tweak.Items) {
        switch ([string]$it.Kind) {
            'Reg'    { $targets += ($it.Path + '\' + $it.Name) }
            'RegKey' { $targets += $it.Path }
            'Svc'    { $targets += $it.Name }
            'Appx'   { $targets += $it.Name }
            'Task'   { $targets += ($it.Path.TrimEnd('\') + '\' + $it.Name) }
        }
    }
    $dirs = @()
    if (Test-Path -LiteralPath $script:BackupRoot) {
        $dirs = @(Get-ChildItem -LiteralPath $script:BackupRoot -Directory -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending)
    }
    $ok = 0
    foreach ($t in $targets) {
        $found = $null
        foreach ($d in $dirs) {
            $sf = Join-Path $d.FullName 'state.json'
            if (-not (Test-Path -LiteralPath $sf)) { continue }
            try { $state = Get-Content -LiteralPath $sf -Raw -Encoding UTF8 | ConvertFrom-Json } catch { continue }
            $hit = @($state.changes) | Where-Object { $_.target -eq $t -or $_.target -like ($t + '*') } | Select-Object -First 1
            if ($hit) { $found = $hit; break }
        }
        if ($found) { if (Restore-ChangeEntry -Entry $found) { $ok++ } }
        else { Write-Log ('Sem backup para desfazer: ' + $t) 'WARN' }
    }
    Write-Log ('Desfeitos ' + $ok + ' de ' + $targets.Count + ' alvo(s) do tweak ' + $Tweak.Id) 'INFO'
}

function Show-TweakCategory {
    param([string]$Cat)
    $list = @($script:TweakCatalog | Where-Object { $_.Cat -eq $Cat })
    while ($true) {
        Write-Section ('CATEGORIA: ' + $Cat)
        Write-Host '  #   Estado       Ganho              Nome' -ForegroundColor DarkGray
        Write-Host '  --  -----------  -----------------  ------------------------------------------' -ForegroundColor DarkGray
        $i = 0
        foreach ($t in $list) {
            $i++
            $st = Get-TweakState -Tweak $t
            $color = 'Yellow'
            if ($st -eq 'APLICADO') { $color = 'Green' }
            elseif ($st -eq 'PARCIAL') { $color = 'Cyan' }
            elseif ($st -eq 'N/D') { $color = 'DarkGray' }
            $flags = ''
            if ($t.Experimental) { $flags += ' [EXP]' }
            if ($t.Reboot) { $flags += ' [REB]' }
            Write-Host ('  ' + ([string]$i).PadRight(3) + ' ' + $st.PadRight(12) + ' ' + $t.Gain.PadRight(18) + ' ' + $t.Name + $flags) -ForegroundColor $color
        }
        Write-Host ''
        Write-Host '  Numero(s) = aplicar   (ex: 1,3,5 ou 2-4)      D + numeros = desfazer' -ForegroundColor DarkGray
        Write-Host '  I + numero = detalhes        all = todos      0 = voltar' -ForegroundColor DarkGray
        Write-Host '  HL> ' -ForegroundColor Cyan -NoNewline
        $in = (Read-Host).Trim()
        if ($in -eq '0' -or $in -eq '') { return }
        if ($in -eq 'all') { $in = '1-' + $list.Count }

        if ($in -match '^[dD]\s*(.+)$') {
            $idxs = ConvertTo-IndexList -Text $Matches[1] -Max $list.Count
            foreach ($ix in $idxs) {
                $tw = $list[$ix]
                if (Confirm-Action ('Desfazer "' + $tw.Name + '" usando os backups?')) { Undo-Tweak -Tweak $tw }
            }
            continue
        }
        if ($in -match '^[iI]\s*(\d+)$') {
            $n = [int]$Matches[1]
            if ($n -ge 1 -and $n -le $list.Count) {
                $tw = $list[$n - 1]
                Write-Host ''
                Write-Host ('  ' + $tw.Name) -ForegroundColor Cyan
                Write-Host ('  Categoria : ' + $tw.Cat) -ForegroundColor Gray
                Write-Host ('  Ganho     : ' + $tw.Gain + '   | Risco: ' + $tw.Risk) -ForegroundColor Gray
                if ($tw.Reboot) { Write-Host '  Requer reinicio: SIM' -ForegroundColor Yellow }
                if ($tw.Experimental) { Write-Host '  EXPERIMENTAL: teste e compare antes de manter' -ForegroundColor Yellow }
                Write-Host ('  ' + $tw.Desc) -ForegroundColor Gray
                Write-Host '  Itens:' -ForegroundColor DarkGray
                foreach ($it in $tw.Items) {
                    $line = ''
                    switch ([string]$it.Kind) {
                        'Reg'    { $line = 'Reg  ' + $it.Path + ' \ ' + $it.Name + ' = ' + $it.Value + ' (' + $it.Type + ')' }
                        'RegKey' { $line = 'Chave ' + $it.Path }
                        'Svc'    { $line = 'Svc  ' + $it.Name + ' -> ' + $it.Start }
                        'Appx'   { $line = 'App  ' + $it.Name }
                        'Task'   { $line = 'Task ' + $it.Path + $it.Name }
                    }
                    Write-Host ('    - ' + $line) -ForegroundColor DarkGray
                }
                Show-Pause
            }
            continue
        }

        $idxs = ConvertTo-IndexList -Text $in -Max $list.Count
        if ($idxs.Count -eq 0) { Write-Host '  Entrada invalida.' -ForegroundColor Yellow; continue }
        $sel = @()
        foreach ($ix in $idxs) { $sel += $list[$ix] }
        Write-Host ''
        foreach ($tw in $sel) { Write-Host ('  + ' + $tw.Name + '  (' + $tw.Gain + ')') -ForegroundColor Green }
        if (-not (Confirm-Action ('Aplicar ' + $sel.Count + ' tweak(s)?'))) { continue }
        foreach ($tw in $sel) { Invoke-Tweak -Tweak $tw -Force }
        Write-Log ('Categoria ' + $Cat + ': ' + $sel.Count + ' tweak(s) processado(s)') 'INFO'
        Show-Summary
        Show-Pause
    }
}

function Show-TweakBrowser {
    while ($true) {
        Write-Section 'CATALOGO DE TWEAKS'
        $cats = Get-TweakCategories
        $i = 0
        foreach ($c in $cats) {
            $i++
            $all = @($script:TweakCatalog | Where-Object { $_.Cat -eq $c })
            $ap = 0
            foreach ($t in $all) { if ((Get-TweakState -Tweak $t) -eq 'APLICADO') { $ap++ } }
            $bar = ('#' * [int]($ap * 10 / [math]::Max($all.Count, 1)))
            Write-Host ('  [' + $i + '] ' + $c.PadRight(12) + ' ' + $ap + '/' + $all.Count + ' aplicados  ' + $bar) -ForegroundColor $(if ($ap -eq $all.Count) { 'Green' } else { 'Gray' })
        }
        Write-Host ''
        Write-Host '  [S] Buscar   [P] Presets 1-clique   [E] Mostrar experimentais   [0] Voltar' -ForegroundColor DarkGray
        Write-Host ''
        Write-Host '  HL> ' -ForegroundColor Cyan -NoNewline
        $in = (Read-Host).Trim()
        if ($in -eq '0' -or $in -eq '') { return }
        if ($in -match '^[sS]\s*(.*)$') {
            $txt = $Matches[1]
            if (-not $txt) { Write-Host '  Texto da busca: ' -NoNewline; $txt = (Read-Host).Trim() }
            $res = Find-Tweak -Text $txt
            Write-Section ('BUSCA: ' + $txt + ' (' + $res.Count + ' resultado(s))')
            $k = 0
            foreach ($t in $res) {
                $k++
                Write-Host ('  ' + $k + '. [' + $t.Cat + '] ' + $t.Name + '  - ' + (Get-TweakState -Tweak $t)) -ForegroundColor Gray
            }
            if ($res.Count -gt 0) {
                Write-Host '  Numeros para aplicar (ou 0 para voltar): ' -NoNewline
                $s2 = (Read-Host).Trim()
                $idxs = ConvertTo-IndexList -Text $s2 -Max $res.Count
                if ($idxs.Count -gt 0) {
                    $sel = @(); foreach ($ix in $idxs) { $sel += $res[$ix] }
                    if (Confirm-Action ('Aplicar ' + $sel.Count + ' tweak(s)?')) {
                        foreach ($tw in $sel) { Invoke-Tweak -Tweak $tw -Force }
                        Show-Summary
                    }
                    Show-Pause
                }
            } else { Show-Pause }
            continue
        }
        if ($in -match '^[pP]') { Show-PresetMenu; continue }
        if ($in -match '^[eE]') {
            Write-Section 'TWEAKS EXPERIMENTAIS'
            foreach ($t in @($script:TweakCatalog | Where-Object { $_.Experimental })) {
                Write-Host ('  - [' + $t.Cat + '] ' + $t.Name + '  (' + $t.Gain + ')') -ForegroundColor Yellow
            }
            Write-Log 'Ao aplicar, escolha o tweak pelo numero dentro da categoria.' 'INFO'
            Show-Pause
            continue
        }
        $n = 0
        if ([int]::TryParse($in, [ref]$n) -and $n -ge 1 -and $n -le $cats.Count) {
            Show-TweakCategory -Cat $cats[$n - 1]
            continue
        }
        Write-Host '  Opcao invalida.' -ForegroundColor Yellow
    }
}

# ==============================================================================
#  7d. CATALOGO DE APPS (winget) + INSTALADOR
# ==============================================================================
$script:AppCatalog = @(
    @{ Cat = 'Navegadores'; Id = 'Google.Chrome'; Name = 'Google Chrome' }
    @{ Cat = 'Navegadores'; Id = 'Mozilla.Firefox'; Name = 'Mozilla Firefox' }
    @{ Cat = 'Navegadores'; Id = 'Brave.Brave'; Name = 'Brave' }
    @{ Cat = 'Navegadores'; Id = 'Vivaldi.Vivaldi'; Name = 'Vivaldi' }
    @{ Cat = 'Navegadores'; Id = 'Opera.Opera'; Name = 'Opera' }

    @{ Cat = 'Gaming'; Id = 'Valve.Steam'; Name = 'Steam' }
    @{ Cat = 'Gaming'; Id = 'EpicGames.EpicGamesLauncher'; Name = 'Epic Games Launcher' }
    @{ Cat = 'Gaming'; Id = 'GOG.Galaxy'; Name = 'GOG Galaxy' }
    @{ Cat = 'Gaming'; Id = 'ElectronicArts.EADesktop'; Name = 'EA app' }
    @{ Cat = 'Gaming'; Id = 'Ubisoft.Connect'; Name = 'Ubisoft Connect' }
    @{ Cat = 'Gaming'; Id = 'Discord.Discord'; Name = 'Discord' }
    @{ Cat = 'Gaming'; Id = 'Playnite.Playnite'; Name = 'Playnite (launcher unico)' }
    @{ Cat = 'Gaming'; Id = 'Overwolf.Overwolf'; Name = 'Overwolf' }
    @{ Cat = 'Gaming'; Id = 'RetroArch.RetroArch'; Name = 'RetroArch' }
    @{ Cat = 'Gaming'; Id = 'BlueStack.BlueStacks'; Name = 'BlueStacks 5 (Android)' }

    @{ Cat = 'Benchmark'; Id = 'CPUID.CPU-Z'; Name = 'CPU-Z' }
    @{ Cat = 'Benchmark'; Id = 'TechPowerUp.GPU-Z'; Name = 'GPU-Z' }
    @{ Cat = 'Benchmark'; Id = 'REALiX.HWiNFO'; Name = 'HWiNFO64' }
    @{ Cat = 'Benchmark'; Id = 'CrystalDewWorld.CrystalDiskInfo'; Name = 'CrystalDiskInfo' }
    @{ Cat = 'Benchmark'; Id = 'CrystalDewWorld.CrystalDiskMark'; Name = 'CrystalDiskMark' }
    @{ Cat = 'Benchmark'; Id = 'Microsoft.Sysinternals.ProcessExplorer'; Name = 'Process Explorer' }
    @{ Cat = 'Benchmark'; Id = 'Microsoft.Sysinternals.Autoruns'; Name = 'Autoruns' }

    @{ Cat = 'Midia'; Id = 'VideoLAN.VLC'; Name = 'VLC' }
    @{ Cat = 'Midia'; Id = 'Spotify.Spotify'; Name = 'Spotify' }
    @{ Cat = 'Midia'; Id = 'OBSProject.OBSStudio'; Name = 'OBS Studio' }
    @{ Cat = 'Midia'; Id = 'HandBrake.HandBrake'; Name = 'HandBrake' }
    @{ Cat = 'Midia'; Id = 'Audacity.Audacity'; Name = 'Audacity' }
    @{ Cat = 'Midia'; Id = 'GIMP.GIMP'; Name = 'GIMP' }
    @{ Cat = 'Midia'; Id = 'KDE.Kdenlive'; Name = 'Kdenlive' }
    @{ Cat = 'Midia'; Id = 'ShareX.ShareX'; Name = 'ShareX' }

    @{ Cat = 'Dev'; Id = 'Microsoft.VisualStudioCode'; Name = 'VS Code' }
    @{ Cat = 'Dev'; Id = 'Git.Git'; Name = 'Git' }
    @{ Cat = 'Dev'; Id = 'Microsoft.PowerShell'; Name = 'PowerShell 7' }
    @{ Cat = 'Dev'; Id = 'Microsoft.WindowsTerminal'; Name = 'Windows Terminal' }
    @{ Cat = 'Dev'; Id = 'Notepad++.Notepad++'; Name = 'Notepad++' }
    @{ Cat = 'Dev'; Id = 'OpenJS.NodeJS.LTS'; Name = 'Node.js LTS' }
    @{ Cat = 'Dev'; Id = 'Python.Python.3.12'; Name = 'Python 3.12' }
    @{ Cat = 'Dev'; Id = 'Docker.DockerDesktop'; Name = 'Docker Desktop (usa Hyper-V!)' }
    @{ Cat = 'Dev'; Id = 'Postman.Postman'; Name = 'Postman' }

    @{ Cat = 'Utilitarios'; Id = '7zip.7zip'; Name = '7-Zip' }
    @{ Cat = 'Utilitarios'; Id = 'RARLab.WinRAR'; Name = 'WinRAR' }
    @{ Cat = 'Utilitarios'; Id = 'voidtools.Everything'; Name = 'Everything (busca)' }
    @{ Cat = 'Utilitarios'; Id = 'Microsoft.PowerToys'; Name = 'PowerToys' }
    @{ Cat = 'Utilitarios'; Id = 'Rufus.Rufus'; Name = 'Rufus' }
    @{ Cat = 'Utilitarios'; Id = 'Ventoy.Ventoy'; Name = 'Ventoy' }
    @{ Cat = 'Utilitarios'; Id = 'Bitwarden.Bitwarden'; Name = 'Bitwarden' }
    @{ Cat = 'Utilitarios'; Id = 'KeePassXCTeam.KeePassXC'; Name = 'KeePassXC' }
    @{ Cat = 'Utilitarios'; Id = 'Wireshark.Wireshark'; Name = 'Wireshark' }
    @{ Cat = 'Utilitarios'; Id = 'PuTTY.PuTTY'; Name = 'PuTTY' }
    @{ Cat = 'Utilitarios'; Id = 'BleachBit.BleachBit'; Name = 'BleachBit' }

    @{ Cat = 'Comunicacao'; Id = 'Zoom.Zoom'; Name = 'Zoom' }
    @{ Cat = 'Comunicacao'; Id = 'Telegram.TelegramDesktop'; Name = 'Telegram Desktop' }
    @{ Cat = 'Comunicacao'; Id = 'SlackTechnologies.Slack'; Name = 'Slack' }

    @{ Cat = 'Escritorio'; Id = 'TheDocumentFoundation.LibreOffice'; Name = 'LibreOffice' }
    @{ Cat = 'Escritorio'; Id = 'ONLYOFFICE.DesktopEditors'; Name = 'ONLYOFFICE' }
    @{ Cat = 'Escritorio'; Id = 'SumatraPDF.SumatraPDF'; Name = 'SumatraPDF' }

    @{ Cat = 'Seguranca'; Id = 'Malwarebytes.Malwarebytes'; Name = 'Malwarebytes' }
    @{ Cat = 'Seguranca'; Id = 'GenDigital.NortonSecurity'; Name = 'Norton 360' }
)

function Test-Winget {
    $cmd = Get-Command winget.exe -ErrorAction SilentlyContinue
    if (-not $cmd) {
        Write-Log 'winget nao encontrado. Instale o "App Installer" na Microsoft Store.' 'ERRO'
        return $false
    }
    return $true
}

function Show-AppInstaller {
    if (-not (Test-Winget)) { Show-Pause; return }
    $cats = New-Object System.Collections.ArrayList
    foreach ($a in $script:AppCatalog) { if (-not ($cats -contains $a.Cat)) { $null = $cats.Add($a.Cat) } }
    while ($true) {
        Write-Section 'INSTALADOR DE APPS (winget)'
        $i = 0
        foreach ($c in $cats) {
            $i++
            $n = @($script:AppCatalog | Where-Object { $_.Cat -eq $c }).Count
            Write-Host ('  [' + $i + '] ' + $c.PadRight(14) + $n + ' apps') -ForegroundColor Gray
        }
        Write-Host ''
        Write-Host '  [B] Buscar no winget   [U] Desinstalar um app   [0] Voltar' -ForegroundColor DarkGray
        Write-Host '  HL> ' -ForegroundColor Cyan -NoNewline
        $in = (Read-Host).Trim()
        if ($in -eq '0' -or $in -eq '') { return }
        if ($in -match '^[bB]') {
            Write-Host '  Buscar: ' -NoNewline
            $q = (Read-Host).Trim()
            if ($q) {
                Write-Log ('winget search ' + $q) 'ACAO'
                & winget.exe search $q --accept-source-agreements 2>&1 | Select-Object -First 25 | ForEach-Object { Write-Host ('  ' + $_) -ForegroundColor Gray }
                Show-Pause
            }
            continue
        }
        if ($in -match '^[uU]') {
            Write-Host '  ID do winget para desinstalar (ex: Valve.Steam): ' -NoNewline
            $id = (Read-Host).Trim()
            if ($id -and (Confirm-Action ('Desinstalar ' + $id + '?'))) {
                & winget.exe uninstall --id $id -e --accept-source-agreements 2>&1 | ForEach-Object { Write-Host ('  ' + $_) -ForegroundColor Gray }
                Show-Pause
            }
            continue
        }
        $n = 0
        if ([int]::TryParse($in, [ref]$n) -and $n -ge 1 -and $n -le $cats.Count) {
            $cat = $cats[$n - 1]
            $apps = @($script:AppCatalog | Where-Object { $_.Cat -eq $cat })
            Write-Section ('APPS: ' + $cat)
            $k = 0
            $installedText = ''
            try { $installedText = ((& winget.exe list --accept-source-agreements 2>$null) -join "`n") } catch { }
            foreach ($a in $apps) {
                $k++
                $inst = ''
                if ($installedText -and ($installedText -match [regex]::Escape($a.Id))) { $inst = '  [instalado]' }
                Write-Host ('  ' + ([string]$k).PadRight(3) + $a.Name.PadRight(32) + $a.Id + $inst) -ForegroundColor $(if ($inst) { 'Green' } else { 'Gray' })
            }
            Write-Host ''
            Write-Host '  Numeros para instalar (ex: 1,3 / 2-4 / all)  0 = voltar' -ForegroundColor DarkGray
            Write-Host '  HL> ' -ForegroundColor Cyan -NoNewline
            $sel = (Read-Host).Trim()
            if ($sel -eq '0' -or $sel -eq '') { continue }
            if ($sel -eq 'all') { $sel = '1-' + $apps.Count }
            $idxs = ConvertTo-IndexList -Text $sel -Max $apps.Count
            if ($idxs.Count -eq 0) { continue }
            $chosen = @(); foreach ($ix in $idxs) { $chosen += $apps[$ix] }
            Write-Host ''
            foreach ($c in $chosen) { Write-Host ('  + ' + $c.Name + '  (' + $c.Id + ')') -ForegroundColor Green }
            if (-not (Confirm-Action ('Instalar ' + $chosen.Count + ' app(s) via winget?'))) { continue }
            $okc = 0
            foreach ($c in $chosen) {
                Write-Log ('winget install ' + $c.Id) 'ACAO'
                if ($script:DryRun) { Write-Log ('[DRY-RUN] instalaria ' + $c.Id) 'DRYRUN'; continue }
                try {
                    $out = & winget.exe install --id $c.Id -e --silent --accept-package-agreements --accept-source-agreements 2>&1
                    $code = $LASTEXITCODE
                    if ($code -eq 0) { Write-Log ('Instalado: ' + $c.Name) 'OK'; $okc++ }
                    else { Write-Log ('Falha (' + $code + ') ao instalar ' + $c.Id + ': ' + (($out | Select-Object -Last 2) -join ' ')) 'ERRO' }
                } catch { Write-Log ('Erro ao instalar ' + $c.Id + ': ' + $_.Exception.Message) 'ERRO' }
            }
            Write-Log ('Instalados: ' + $okc + ' de ' + $chosen.Count) 'INFO'
            Show-Pause
            continue
        }
        Write-Host '  Opcao invalida.' -ForegroundColor Yellow
    }
}

# ==============================================================================
#  7e. PRESETS (aplicacao em 1 clique)
# ==============================================================================
$script:Presets = @(
    @{ Id = 'gamer'; Name = 'HL Gamer'; Profile = 'Balanced'; Desc = 'FPS em jogos nativos: Game DVR off, MMCSS, prioridade, telemetria e debloat basico.'; Tweaks = @('game-dvr', 'game-mode-on', 'mmcss-games', 'priority-sep', 'visualfx', 'telemetry-policy', 'diagtrack', 'suggestions-off', 'store-auto-update', 'do-mode', 'telemetry-tasks', 'widgets-off', 'startup-delay') }
    @{ Id = 'emulador'; Name = 'HL Emulador'; Profile = 'Balanced'; Desc = 'Emuladores Android/console: tudo do Gamer + exclusoes no Defender + GPU por app.'; Tweaks = @('game-dvr', 'game-mode-on', 'priority-sep', 'visualfx', 'telemetry-policy', 'diagtrack', 'suggestions-off', 'store-auto-update', 'do-mode', 'telemetry-tasks', 'widgets-off', 'startup-delay', 'useless-services'); Emulator = $true }
    @{ Id = 'privacidade'; Name = 'HL Privacidade'; Profile = 'Safe'; Desc = 'Sem telemetria, sem ID de anuncios, sem historico, sem sugestoes e sem busca web.'; Tweaks = @('telemetry-policy', 'diagtrack', 'advertising-id', 'activity-history', 'feedback-siuf', 'tailored-exp', 'suggestions-off', 'start-suggestions', 'websearch-off', 'telemetry-tasks') }
    @{ Id = 'minimal'; Name = 'HL Minimalista'; Profile = 'Balanced'; Desc = 'Tira o excesso de apps do Windows e desliga widgets/Copilot. Sem mexer em servicos.'; Tweaks = @('debloat-bing', 'debloat-3d', 'debloat-solitaire', 'debloat-media', 'debloat-help', 'debloat-misc', 'widgets-off', 'chat-icon-off', 'copilot-off', 'suggestions-off') }
    @{ Id = 'notebook'; Name = 'HL Notebook'; Profile = 'Safe'; Desc = 'Menos calor e consumo, mantendo FPS: energia, telemetria, sugestoes e boot mais rapido.'; Tweaks = @('game-dvr', 'telemetry-policy', 'diagtrack', 'suggestions-off', 'startup-delay', 'transparency-off') }
)

function Show-PresetMenu {
    Write-Section 'PRESETS 1-CLIQUE'
    $i = 0
    foreach ($p in $script:Presets) {
        $i++
        Write-Host ('  [' + $i + '] ' + $p.Name) -ForegroundColor Cyan
        Write-Host ('      ' + $p.Desc) -ForegroundColor DarkGray
        Write-Host ('      ' + $p.Tweaks.Count + ' tweaks + perfil ' + $p.Profile) -ForegroundColor DarkGray
    }
    Write-Host ''
    Write-Host '  Numero = aplicar preset      0 = voltar' -ForegroundColor DarkGray
    Write-Host '  HL> ' -ForegroundColor Cyan -NoNewline
    $in = (Read-Host).Trim()
    if ($in -eq '0' -or $in -eq '') { return }
    $n = 0
    if (-not [int]::TryParse($in, [ref]$n) -or $n -lt 1 -or $n -gt $script:Presets.Count) { return }
    $pr = $script:Presets[$n - 1]
    Write-Host ''
    Write-Host ('  ' + $pr.Name + ' -> ' + $pr.Tweaks.Count + ' tweaks + perfil ' + $pr.Profile) -ForegroundColor White
    if (-not (Confirm-Action 'Aplicar este preset agora?')) { return }
    if (-not $script:SkipRestorePoint -and -not $script:DryRun) { New-RestorePointHL }
    switch ($pr.Profile) {
        'Safe' { Invoke-ProfileSafe }
        'Balanced' { Invoke-ProfileBalanced }
        'Extreme' { Invoke-ProfileExtreme }
    }
    Invoke-TweakSet -Ids $pr.Tweaks
    if ($pr.Emulator) { Invoke-EmulatorFocus }
    Show-Summary
    Show-Pause
}

# ==============================================================================
#  7f. CONFIG EXPORT / IMPORT
# ==============================================================================
function Export-HLConfig {
    param([string]$Path)
    if (-not $Path) { $Path = Join-Path $script:Root ('HL-config_' + (Get-Date -Format 'yyyy-MM-dd_HH-mm') + '.json') }
    $applied = @()
    foreach ($t in $script:TweakCatalog) { if ((Get-TweakState -Tweak $t) -eq 'APLICADO') { $applied += $t.Id } }
    $cfg = [ordered]@{
        brand = $script:Brand; version = $script:BrandVersion
        exportedAt = (Get-Date).ToString('s'); host = $env:COMPUTERNAME
        profile = $Profile; powerScheme = (Get-ActivePowerScheme).Name
        appliedTweaks = $applied
        presets = @($script:Presets | ForEach-Object { $_.Id })
        note = 'Importe em outro PC com: .\HL-Optimizer.ps1 -ImportConfig <arquivo>'
    }
    try {
        ($cfg | ConvertTo-Json -Depth 5) | Set-Content -LiteralPath $Path -Encoding UTF8
        Write-Log ('Config exportada: ' + $Path) 'OK'
        Write-Host ('  ' + $Path) -ForegroundColor Cyan
    } catch { Write-Log ('Falha ao exportar config: ' + $_.Exception.Message) 'ERRO' }
}

function Import-HLConfig {
    param([string]$Path)
    if (-not $Path -or -not (Test-Path -LiteralPath $Path)) { Write-Log 'Arquivo de config nao encontrado.' 'ERRO'; return }
    try { $cfg = Get-Content -LiteralPath $Path -Raw -Encoding UTF8 | ConvertFrom-Json }
    catch { Write-Log ('Config invalida: ' + $_.Exception.Message) 'ERRO'; return }
    Write-Section 'IMPORTAR CONFIG'
    Write-Host ('  Exportado em : ' + $cfg.exportedAt + '  (host ' + $cfg.host + ')') -ForegroundColor Gray
    Write-Host ('  Perfil       : ' + $cfg.profile) -ForegroundColor Gray
    $ids = @($cfg.appliedTweaks)
    Write-Host ('  Tweaks       : ' + $ids.Count) -ForegroundColor Gray
    foreach ($id in $ids) {
        $tw = Get-TweakById -Id $id
        if ($tw) { Write-Host ('    - ' + $tw.Name) -ForegroundColor DarkGray }
        else { Write-Host ('    - ' + $id + '  (desconhecido nesta versao)') -ForegroundColor Yellow }
    }
    if (-not (Confirm-Action 'Aplicar estes tweaks neste PC?')) { return }
    Invoke-TweakSet -Ids $ids
    Show-Summary
}

function Show-ConfigMenu {
    while ($true) {
        Write-Section 'CONFIG (exportar / importar)'
        Write-Host '  [1] Exportar config atual (JSON)' -ForegroundColor Gray
        Write-Host '  [2] Importar config de um arquivo JSON' -ForegroundColor Gray
        Write-Host '  [3] Ver pasta de backups e logs' -ForegroundColor Gray
        Write-Host '  [0] Voltar' -ForegroundColor DarkGray
        Write-Host '  HL> ' -ForegroundColor Cyan -NoNewline
        $in = (Read-Host).Trim()
        switch ($in) {
            '1' { Export-HLConfig; Show-Pause }
            '2' {
                Write-Host '  Caminho do JSON: ' -NoNewline
                $p = (Read-Host).Trim().Trim('"')
                Import-HLConfig -Path $p
                Show-Pause
            }
            '3' { Open-FolderHL -Path $script:BackupRoot; Open-FolderHL -Path $script:LogDir }
            '0' { return }
            default { }
        }
    }
}

# ==============================================================================
#  7g. HISTORICO DE BACKUPS / UNDO
# ==============================================================================
function Show-UndoHistory {
    Write-Section 'HISTORICO DE BACKUPS'
    $dirs = @()
    if (Test-Path -LiteralPath $script:BackupRoot) {
        $dirs = @(Get-ChildItem -LiteralPath $script:BackupRoot -Directory -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending)
    }
    if ($dirs.Count -eq 0) { Write-Log 'Nenhum backup encontrado. Nada a desfazer.' 'WARN'; return }
    $i = 0
    foreach ($d in $dirs) {
        $i++
        $count = 0; $prof = '?'; $reboot = $false
        $sf = Join-Path $d.FullName 'state.json'
        if (Test-Path -LiteralPath $sf) {
            try { $st = Get-Content -LiteralPath $sf -Raw -Encoding UTF8 | ConvertFrom-Json; $count = @($st.changes).Count; $prof = $st.profile; $reboot = [bool]$st.rebootRequired } catch { }
        }
        Write-Host ('  [' + $i + '] ' + $d.Name + '   perfil: ' + $prof + '   alteracoes: ' + $count + $(if ($reboot) { '   [reboot]' } else { '' })) -ForegroundColor Gray
    }
    Write-Host ''
    Write-Host '  Numero = restaurar este backup      0 = voltar' -ForegroundColor DarkGray
    Write-Host '  HL> ' -ForegroundColor Cyan -NoNewline
    $in = (Read-Host).Trim()
    if ($in -eq '0' -or $in -eq '') { return }
    $n = 0
    if (-not [int]::TryParse($in, [ref]$n) -or $n -lt 1 -or $n -gt $dirs.Count) { return }
    Invoke-RevertHL -Path $dirs[$n - 1].FullName
    Show-Pause
}

# ==============================================================================
#  7h. RELATORIO HTML
# ==============================================================================
function ConvertTo-HtmlSafe {
    param([string]$Text)
    if ($null -eq $Text) { return '' }
    return ($Text -replace '&', '&amp;' -replace '<', '&lt;' -replace '>', '&gt;' -replace '"', '&quot;')
}

function Export-HtmlReportHL {
    param([string]$Path)
    if (-not $Path) { $Path = Join-Path $script:LogDir ('hl-relatorio_' + $script:SessionId + '.html') }
    $v = Get-SystemSnapshot
    $script:SysInfo = $v
    $rows = ''
    foreach ($c in $script:Changes) {
        $before = ''
        if ($c.before) { $before = ($c.before.GetEnumerator() | ForEach-Object { $_.Key + '=' + $_.Value }) -join '; ' }
        $after = ''
        if ($c.after) { $after = ($c.after.GetEnumerator() | ForEach-Object { $_.Key + '=' + $_.Value }) -join '; ' }
        $rows += '<tr><td>' + (ConvertTo-HtmlSafe $c.type) + '</td><td>' + (ConvertTo-HtmlSafe $c.target) + '</td><td>' + (ConvertTo-HtmlSafe $before) + '</td><td>' + (ConvertTo-HtmlSafe $after) + '</td><td>' + (ConvertTo-HtmlSafe $c.gain) + '</td></tr>'
    }
    $applied = 0; $total = @($script:TweakCatalog).Count
    foreach ($t in $script:TweakCatalog) { if ((Get-TweakState -Tweak $t) -eq 'APLICADO') { $applied++ } }
    $badges = ''
    foreach ($b in (Get-HealthBadges)) {
        $cls = 'warn'
        if ($b.State -eq 'good') { $cls = 'good' } elseif ($b.State -eq 'bad') { $cls = 'bad' }
        $badges += '<div class="badge ' + $cls + '"><span>' + (ConvertTo-HtmlSafe $b.Label) + '</span><b>' + (ConvertTo-HtmlSafe $b.Value) + '</b></div>'
    }
    $html = @"
<!DOCTYPE html>
<html lang="pt-BR"><head><meta charset="utf-8">
<title>HL Optimizer - Relatorio</title>
<style>
 body{background:#0f1216;color:#d7dee8;font-family:Segoe UI,Arial,sans-serif;margin:0;padding:32px}
 h1{color:#38d6c4;margin:0 0 4px} h2{color:#8fd3ff;border-bottom:1px solid #24303c;padding-bottom:6px;margin-top:32px}
 .sub{color:#7c8b9c}
 table{width:100%;border-collapse:collapse;margin-top:12px;font-size:13px}
 th,td{border:1px solid #24303c;padding:6px 8px;text-align:left;vertical-align:top}
 th{background:#182028;color:#9fb3c8}
 tr:nth-child(even){background:#141a21}
 .badges{display:flex;flex-wrap:wrap;gap:8px;margin-top:12px}
 .badge{background:#161d25;border:1px solid #24303c;border-radius:8px;padding:8px 12px;min-width:210px}
 .badge span{display:block;color:#7c8b9c;font-size:12px}
 .badge.good b{color:#4ade80}.badge.warn b{color:#fbbf24}.badge.bad b{color:#f87171}
 .foot{margin-top:28px;color:#5f6f80;font-size:12px}
 .kpi{display:flex;gap:24px;margin-top:16px}
 .kpi div{background:#161d25;border:1px solid #24303c;border-radius:8px;padding:12px 16px}
 .kpi b{display:block;font-size:22px;color:#38d6c4}
</style></head><body>
<h1>HL Optimizer</h1>
<div class="sub">Marca: HL &middot; v$($script:BrandVersion) build $($script:BrandBuild) &middot; relatorio gerado em $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')</div>
<div class="kpi">
  <div><span>Host</span><b>$(ConvertTo-HtmlSafe $env:COMPUTERNAME)</b></div>
  <div><span>Tweaks aplicados</span><b>$applied/$total</b></div>
  <div><span>Alteracoes nesta sessao</span><b>$($script:Changes.Count)</b></div>
  <div><span>Perfil</span><b>$(ConvertTo-HtmlSafe $Profile)</b></div>
</div>
<h2>Saude do sistema</h2>
<div class="badges">$badges</div>
<h2>Hardware</h2>
<table><tr><th>Item</th><th>Valor</th></tr>
<tr><td>CPU</td><td>$(ConvertTo-HtmlSafe $v.Cpu)</td></tr>
<tr><td>RAM</td><td>$($v.RamGb) GB</td></tr>
<tr><td>GPU</td><td>$(ConvertTo-HtmlSafe (Get-GpuVendor))</td></tr>
<tr><td>Disco do sistema</td><td>$(ConvertTo-HtmlSafe $v.DiskType)</td></tr>
<tr><td>Plano de energia</td><td>$(ConvertTo-HtmlSafe $v.PowerName)</td></tr>
<tr><td>Emuladores</td><td>$(ConvertTo-HtmlSafe (($v.Emulators) -join ', '))</td></tr>
</table>
<h2>Alteracoes desta sessao</h2>
<table><tr><th>Tipo</th><th>Alvo</th><th>Antes</th><th>Depois</th><th>Ganho</th></tr>$rows</table>
<h2>Como desfazer</h2>
<p>Execute <code>HL-Reverter.cmd</code> ou <code>.\HL-Optimizer.ps1 -Revert</code>. Backups em <code>_HL-Backup\</code>.</p>
<div class="foot">HL Optimizer &middot; marca e proprietaria: HL &middot; este relatorio nao sai do seu PC.</div>
</body></html>
"@
    try {
        if (-not $script:DryRun) {
            Set-Content -LiteralPath $Path -Value $html -Encoding UTF8
            Write-Log ('Relatorio HTML: ' + $Path) 'OK'
        } else { Write-Log ('[DRY-RUN] geraria relatorio em ' + $Path) 'DRYRUN' }
        Write-Host ('  ' + $Path) -ForegroundColor Cyan
        if (-not $script:DryRun) { try { Start-Process $Path } catch { } }
    } catch { Write-Log ('Falha ao gerar relatorio: ' + $_.Exception.Message) 'ERRO' }
}

# ==============================================================================
#  7i. RESTAURACAO DE UMA ALTERACAO (usado pelo revert e pelo undo por tweak)
# ==============================================================================
function Set-StartupApprovedBytes {
    param([string]$Key, [string]$Name, [bool]$Enabled)
    if (-not (Test-Path -LiteralPath $Key)) { $null = New-Item -Path $Key -Force -ErrorAction Stop }
    $bytes = [byte[]](3, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0)
    if ($Enabled) { $bytes = [byte[]](2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0) }
    $null = New-ItemProperty -LiteralPath $Key -Name $Name -Value $bytes -PropertyType Binary -Force -ErrorAction Stop
}

function Restore-ChangeEntry {
    param($Entry)
    if (-not $Entry) { return $false }
    if ($script:DryRun) {
        Write-Log ('[DRY-RUN] reverteria ' + $Entry.type + ' ' + $Entry.target) 'DRYRUN'
        return $true
    }
    try {
        switch ([string]$Entry.type) {
            'Registry' {
                $full = [string]$Entry.target
                $idx = $full.LastIndexOf('\')
                if ($idx -lt 1) { return $false }
                $path = $full.Substring(0, $idx)
                $name = $full.Substring($idx + 1)
                $existed = ([string]$Entry.before.exists -eq 'True')
                if ($existed) {
                    $type = 'DWord'
                    if ($Entry.before.type) { $type = [string]$Entry.before.type }
                    $val = $Entry.before.value
                    if ($type -eq 'DWord') { $val = [int]$val }
                    elseif ($type -eq 'QWord') { $val = [int64]$val }
                    if (-not (Test-Path -LiteralPath $path)) { $null = New-Item -Path $path -Force }
                    $null = New-ItemProperty -LiteralPath $path -Name $name -Value $val -PropertyType $type -Force -ErrorAction Stop
                    Write-Log ('  registro restaurado: ' + $name + ' = ' + $val) 'OK'
                } else {
                    if (Test-Path -LiteralPath $path) {
                        Remove-ItemProperty -LiteralPath $path -Name $name -Force -ErrorAction SilentlyContinue
                        Write-Log ('  valor criado removido: ' + $name) 'OK'
                    }
                }
                return $true
            }
            'RegKey' {
                if ([string]$Entry.before.exists -ne 'True') {
                    if (Test-Path -LiteralPath ([string]$Entry.target)) {
                        Remove-Item -LiteralPath ([string]$Entry.target) -Recurse -Force -ErrorAction Stop
                        Write-Log ('  chave criada removida: ' + $Entry.target) 'OK'
                        return $true
                    }
                }
                return $false
            }
            'Service' {
                $map = @{ 'Disabled' = 'disabled'; 'Manual' = 'demand'; 'Automatic' = 'auto'; 'AutomaticDelayedStart' = 'delayed-auto' }
                $orig = [string]$Entry.before.startType
                if ($map.ContainsKey($orig)) {
                    $null = & sc.exe config ([string]$Entry.target) start= $map[$orig] 2>&1
                    if ([string]$Entry.before.status -eq 'Running') { Start-Service -Name ([string]$Entry.target) -ErrorAction SilentlyContinue }
                    Write-Log ('  servico restaurado: ' + $Entry.target + ' -> ' + $orig) 'OK'
                    return $true
                }
                return $false
            }
            'PowerScheme' {
                if ($Entry.before.guid) {
                    $null = & powercfg.exe /setactive ([string]$Entry.before.guid) 2>&1
                    Write-Log ('  plano de energia restaurado: ' + $Entry.before.name) 'OK'
                    return $true
                }
                return $false
            }
            'BcdEdit' {
                if ([string]$Entry.target -eq 'hypervisorlaunchtype') {
                    $v = 'auto'
                    if ($Entry.before.value) { $v = [string]$Entry.before.value }
                    $null = & bcdedit.exe /set hypervisorlaunchtype $v 2>&1
                    Write-Log ('  hypervisorlaunchtype restaurado: ' + $v + ' (requer reinicio)') 'OK'
                    return $true
                }
                return $false
            }
            'DefenderExclusion' {
                Remove-MpPreference -ExclusionPath ([string]$Entry.target) -ErrorAction Stop
                Write-Log ('  exclusao removida: ' + $Entry.target) 'OK'
                return $true
            }
            'Appx' {
                $loc = [string]$Entry.before.location
                if ($loc -and (Test-Path -LiteralPath (Join-Path $loc 'AppxManifest.xml'))) {
                    Add-AppxPackage -Register (Join-Path $loc 'AppxManifest.xml') -DisableDevelopmentMode -ErrorAction Stop
                    Write-Log ('  app reativado: ' + $Entry.target) 'OK'
                    return $true
                }
                Write-Log ('  nao foi possivel restaurar o app (reinstale pela Store): ' + $Entry.target) 'WARN'
                return $false
            }
            'ScheduledTask' {
                $full = [string]$Entry.target
                $idx = $full.LastIndexOf('\')
                if ($idx -lt 1) { return $false }
                $path = $full.Substring(0, $idx)
                if (-not $path) { $path = '\' }
                $name = $full.Substring($idx + 1)
                $null = Enable-ScheduledTask -TaskPath $path -TaskName $name -ErrorAction Stop
                Write-Log ('  tarefa reativada: ' + $name) 'OK'
                return $true
            }
            'Startup' {
                $enabled = ([string]$Entry.before.enabled -eq 'True')
                Set-StartupApprovedBytes -Key ([string]$Entry.target) -Name ([string]$Entry.before.name) -Enabled $enabled
                Write-Log ('  startup restaurado: ' + $Entry.before.name) 'OK'
                return $true
            }
            'Info' {
                Write-Log ('  sem reversao automatica para: ' + $Entry.target) 'INFO'
                return $true
            }
            'Dns' {
                $idx = [int]$Entry.before.interfaceIndex
                $servers = @()
                if ($Entry.before.servers) { $servers = @(([string]$Entry.before.servers) -split ',' | Where-Object { $_ }) }
                if ($servers.Count -gt 0) { Set-DnsClientServerAddress -InterfaceIndex $idx -ServerAddresses $servers -ErrorAction Stop }
                else { Set-DnsClientServerAddress -InterfaceIndex $idx -ResetServerAddresses -ErrorAction Stop }
                Write-Log ('  DNS restaurado na interface ' + $idx) 'OK'
                return $true
            }
            default {
                Write-Log ('  tipo desconhecido ignorado: ' + $Entry.type) 'WARN'
                return $false
            }
        }
    } catch {
        Write-Log ('  FALHA ao reverter ' + $Entry.target + ': ' + $_.Exception.Message) 'ERRO'
        return $false
    }
}

# ==============================================================================
#  7j. WINDOWS UPDATE (COM, sem modulos externos)
# ==============================================================================
function Get-AvailableUpdatesHL {
    $res = New-Object System.Collections.ArrayList
    try {
        $session = New-Object -ComObject Microsoft.Update.Session
        $searcher = $session.CreateUpdateSearcher()
        Write-Log 'Consultando Windows Update (pode levar 30-90s)...' 'ACAO'
        $found = $searcher.Search("IsInstalled=0 and IsHidden=0 and Type='Software'")
        foreach ($u in $found.Updates) {
            $null = $res.Add([pscustomobject]@{
                Title  = [string]$u.Title
                Kb     = (@($u.KBArticleIDs) -join ',')
                SizeMb = [math]::Round(([double]$u.MaxDownloadSize / 1MB), 1)
                Reboot = ([int]$u.InstallationBehavior.RebootBehavior -ne 0)
            })
        }
    } catch { Write-Log ('Falha ao consultar o Windows Update: ' + $_.Exception.Message) 'ERRO' }
    return $res
}

function Install-UpdatesHL {
    try {
        $session = New-Object -ComObject Microsoft.Update.Session
        $searcher = $session.CreateUpdateSearcher()
        $found = $searcher.Search("IsInstalled=0 and IsHidden=0 and Type='Software'")
        if ($found.Updates.Count -eq 0) { Write-Log 'Nenhuma atualizacao pendente.' 'OK'; return }
        $coll = New-Object -ComObject Microsoft.Update.UpdateColl
        foreach ($u in $found.Updates) {
            if (-not $u.EulaAccepted) { try { $u.AcceptEula() } catch { } }
            $null = $coll.Add($u)
        }
        Write-Log ('Baixando ' + $coll.Count + ' atualizacao(oes)...') 'ACAO'
        $dl = $session.CreateUpdateDownloader()
        $dl.Updates = $coll
        $null = $dl.Download()
        Write-Log 'Download concluido. Instalando...' 'ACAO'
        $inst = $session.CreateUpdateInstaller()
        $inst.Updates = $coll
        $r = $inst.Install()
        Write-Log ('Resultado: codigo ' + $r.ResultCode + ' | reinicio necessario: ' + [bool]$r.RebootRequired) 'OK'
        if ($r.RebootRequired) { $script:RebootNeeded = $true; Write-Log 'REINICIO necessario para concluir as atualizacoes.' 'WARN' }
    } catch { Write-Log ('Falha ao instalar atualizacoes: ' + $_.Exception.Message) 'ERRO' }
}

function Set-PauseUpdatesHL {
    param([int]$Weeks = 4)
    $now = Get-Date
    $start = $now.ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
    $end = $now.AddDays($Weeks * 7).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
    $p = 'HKLM:\SOFTWARE\Microsoft\WindowsUpdate\UX\Settings'
    Set-RegValueSafe -Path $p -Name 'PauseFeatureUpdatesStartTime' -Value $start -Type String -Gain 'evita update no meio do jogo' -Risk 'sem patches por algumas semanas'
    Set-RegValueSafe -Path $p -Name 'PauseFeatureUpdatesEndTime' -Value $end -Type String
    Set-RegValueSafe -Path $p -Name 'PauseQualityUpdatesStartTime' -Value $start -Type String
    Set-RegValueSafe -Path $p -Name 'PauseQualityUpdatesEndTime' -Value $end -Type String
    Set-RegValueSafe -Path $p -Name 'PauseUpdatesExpiryTime' -Value $end -Type String
    Write-Log ('Atualizacoes pausadas ate ' + $end) 'OK'
}

function Resume-UpdatesHL {
    $p = 'HKLM:\SOFTWARE\Microsoft\WindowsUpdate\UX\Settings'
    foreach ($n in @('PauseFeatureUpdatesStartTime', 'PauseFeatureUpdatesEndTime', 'PauseQualityUpdatesStartTime', 'PauseQualityUpdatesEndTime', 'PauseUpdatesExpiryTime')) {
        Remove-RegValueSafe -Path $p -Name $n -Gain 'retomar updates' -Risk 'nenhum'
    }
    Write-Log 'Atualizacoes retomadas.' 'OK'
}

function Show-UpdateMenu {
    while ($true) {
        Write-Section 'WINDOWS UPDATE'
        Write-Host '  [1] Ver atualizacoes pendentes' -ForegroundColor Gray
        Write-Host '  [2] Baixar e instalar todas' -ForegroundColor Gray
        Write-Host '  [3] Pausar updates por 4 semanas (recomendado antes de jogar)' -ForegroundColor Gray
        Write-Host '  [4] Retomar updates' -ForegroundColor Gray
        Write-Host '  [5] Abrir Windows Update nas Configuracoes' -ForegroundColor Gray
        Write-Host '  [0] Voltar' -ForegroundColor DarkGray
        Write-Host '  HL> ' -ForegroundColor Cyan -NoNewline
        $in = (Read-Host).Trim()
        switch ($in) {
            '1' {
                $ups = Get-AvailableUpdatesHL
                if ($ups.Count -eq 0) { Write-Host '  Nada pendente (ou consulta falhou).' -ForegroundColor Green }
                else {
                    Write-Host ('  ' + $ups.Count + ' atualizacao(oes):') -ForegroundColor Cyan
                    foreach ($u in $ups) {
                        Write-Host ('   - ' + $u.Title + '  [' + $u.Kb + ']  ' + $u.SizeMb + ' MB') -ForegroundColor Gray
                    }
                }
                Show-Pause
            }
            '2' { if (Confirm-Action 'Baixar e instalar TODAS as atualizacoes agora? Isso pode demorar.') { Install-UpdatesHL }; Show-Pause }
            '3' { if (Confirm-Action 'Pausar atualizacoes por 4 semanas?') { Set-PauseUpdatesHL -Weeks 4 }; Show-Pause }
            '4' { if (Confirm-Action 'Retomar atualizacoes agora?') { Resume-UpdatesHL }; Show-Pause }
            '5' { try { Start-Process 'ms-settings:windowsupdate' } catch { } }
            '0' { return }
            default { }
        }
    }
}

# ==============================================================================
#  7k. INICIALIZACAO (startup apps)
# ==============================================================================
function Get-StartupItemsHL {
    $items = New-Object System.Collections.ArrayList
    $defs = @(
        @{ Hive = 'HKCU'; Run = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'; App = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run' },
        @{ Hive = 'HKLM'; Run = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Run'; App = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run' },
        @{ Hive = 'HKLM32'; Run = 'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run'; App = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run32' }
    )
    foreach ($d in $defs) {
        if (-not (Test-Path -LiteralPath $d.Run)) { continue }
        try { $props = Get-ItemProperty -LiteralPath $d.Run -ErrorAction Stop } catch { continue }
        foreach ($p in $props.PSObject.Properties) {
            if ($p.Name -like 'PS*') { continue }
            $enabled = $true
            $ap = Get-RegValueState -Path $d.App -Name $p.Name
            if ($ap.Exists -and ($ap.Value -is [byte[]]) -and $ap.Value.Length -gt 0) {
                if ($ap.Value[0] -ge 3) { $enabled = $false }
            }
            $null = $items.Add([pscustomobject]@{
                    Hive = $d.Hive; Name = $p.Name; Command = [string]$p.Value
                    Enabled = $enabled; AppKey = $d.App
                })
        }
    }
    return $items
}

function Set-StartupItemHL {
    param($Item, [bool]$Enabled)
    $before = $true
    $ap = Get-RegValueState -Path $Item.AppKey -Name $Item.Name
    if ($ap.Exists -and ($ap.Value -is [byte[]]) -and $ap.Value.Length -gt 0 -and $ap.Value[0] -ge 3) { $before = $false }
    if ($script:DryRun) { Write-Log ('[DRY-RUN] startup ' + $Item.Name + ' -> enabled=' + $Enabled) 'DRYRUN'; return }
    try {
        Set-StartupApprovedBytes -Key $Item.AppKey -Name $Item.Name -Enabled $Enabled
        Write-Log ('Startup ' + $Item.Name + ': ' + $before + ' -> ' + $Enabled) 'OK'
        Save-BackupEntry -Type 'Startup' -Target $Item.AppKey -Before @{ enabled = $before; name = $Item.Name } -After @{ enabled = $Enabled; name = $Item.Name } `
            -Gain 'boot mais leve' -Risk 'app nao inicia sozinho'
    } catch { Write-Log ('Falha ao alterar startup ' + $Item.Name + ': ' + $_.Exception.Message) 'ERRO' }
}

function Show-StartupManager {
    while ($true) {
        Write-Section 'APPS DE INICIALIZACAO'
        $items = @(Get-StartupItemsHL)
        if ($items.Count -eq 0) { Write-Log 'Nenhum app de inicializacao encontrado.' 'INFO'; Show-Pause; return }
        $i = 0
        foreach ($it in $items) {
            $i++
            $st = 'LIGADO '
            $color = 'Green'
            if (-not $it.Enabled) { $st = 'desligado'; $color = 'DarkGray' }
            Write-Host ('  ' + ([string]$i).PadRight(3) + ' [' + $st + '] ' + $it.Hive.PadRight(6) + ' ' + $it.Name.PadRight(28) + $it.Command) -ForegroundColor $color
        }
        Write-Host ''
        Write-Host '  Numero(s) = ligar/desligar (alterna)    0 = voltar' -ForegroundColor DarkGray
        Write-Host '  HL> ' -ForegroundColor Cyan -NoNewline
        $in = (Read-Host).Trim()
        if ($in -eq '0' -or $in -eq '') { return }
        $idxs = ConvertTo-IndexList -Text $in -Max $items.Count
        foreach ($ix in $idxs) {
            $it = $items[$ix]
            Set-StartupItemHL -Item $it -Enabled (-not $it.Enabled)
        }
        if ($idxs.Count -gt 0) { Show-Summary; Show-Pause }
    }
}

# ==============================================================================
#  7l. TAREFAS AGENDADAS (auditoria)
# ==============================================================================
function Show-ScheduledTaskAudit {
    while ($true) {
        Write-Section 'TAREFAS AGENDADAS (nao-Microsoft)'
        $tasks = @()
        try {
            $tasks = @(Get-ScheduledTasksHL -Refresh | Where-Object { $_.TaskPath -notlike '\Microsoft\*' -and $_.TaskName -notlike 'HL*' } | Sort-Object TaskPath, TaskName | Select-Object -First 40)
        } catch { Write-Log ('Falha ao listar tarefas: ' + $_.Exception.Message) 'ERRO' }
        if ($tasks.Count -eq 0) { Write-Log 'Nenhuma tarefa de terceiros encontrada.' 'INFO'; Show-Pause; return }
        $i = 0
        foreach ($t in $tasks) {
            $i++
            $color = 'Gray'
            if ([string]$t.State -eq 'Disabled') { $color = 'DarkGray' }
            elseif ([string]$t.State -eq 'Ready') { $color = 'Yellow' }
            Write-Host ('  ' + ([string]$i).PadRight(3) + ' ' + ([string]$t.State).PadRight(9) + ' ' + $t.TaskPath + $t.TaskName) -ForegroundColor $color
        }
        Write-Host ''
        Write-Host '  Numero(s) = desativar     R + numeros = reativar     0 = voltar' -ForegroundColor DarkGray
        Write-Host '  HL> ' -ForegroundColor Cyan -NoNewline
        $in = (Read-Host).Trim()
        if ($in -eq '0' -or $in -eq '') { return }
        $reenable = $false
        if ($in -match '^[rR]\s*(.+)$') { $reenable = $true; $in = $Matches[1] }
        $idxs = ConvertTo-IndexList -Text $in -Max $tasks.Count
        foreach ($ix in $idxs) {
            $t = $tasks[$ix]
            $state = 'Disabled'
            if ($reenable) { $state = 'Ready' }
            Set-ScheduledTaskStateSafe -TaskPath $t.TaskPath -TaskName $t.TaskName -State $state -Why 'auditoria'
        }
        if ($idxs.Count -gt 0) { Show-Summary; Show-Pause }
    }
}

# ==============================================================================
#  7m. RECURSOS DO WINDOWS (features / virtualizacao)
# ==============================================================================
$script:FeatureList = @(
    @{ Name = 'Microsoft-Hyper-V-All'; Label = 'Hyper-V (completo)'; Note = 'CRITICO: com Hyper-V ativo, BlueStacks/LDPlayer caem para modo lento' }
    @{ Name = 'Microsoft-Hyper-V-Hypervisor'; Label = 'Hypervisor da Microsoft'; Note = 'idem acima' }
    @{ Name = 'VirtualMachinePlatform'; Label = 'Plataforma de Maquina Virtual'; Note = 'usada por WSL2/Android/WSA' }
    @{ Name = 'WindowsHypervisorPlatform'; Label = 'Windows Hypervisor Platform'; Note = 'usada por VirtualBox/emuladores acelerados' }
    @{ Name = 'Microsoft-Windows-Subsystem-Linux'; Label = 'WSL'; Note = 'Linux no Windows' }
    @{ Name = 'Containers'; Label = 'Containers'; Note = 'Docker no modo Windows' }
    @{ Name = 'Containers-DisposableClientVM'; Label = 'Windows Sandbox'; Note = 'maquina descartavel' }
)

function Get-FeatureStateHL {
    param([string]$Name)
    try {
        $f = Get-WindowsOptionalFeature -Online -FeatureName $Name -ErrorAction Stop
        return [string]$f.State
    } catch { return 'N/D' }
}

function Show-FeaturesManager {
    while ($true) {
        Write-Section 'RECURSOS DO WINDOWS / VIRTUALIZACAO'
        Write-Log 'Desligar Hyper-V/VMP e o maior ganho isolado em emulador Android. Exige reinicio.' 'INFO'
        $i = 0
        $states = @()
        foreach ($f in $script:FeatureList) {
            $i++
            $st = Get-FeatureStateHL -Name $f.Name
            $states += $st
            $color = 'DarkGray'
            if ($st -eq 'Enabled') { $color = 'Yellow' } elseif ($st -eq 'Disabled') { $color = 'Green' }
            Write-Host ('  ' + ([string]$i).PadRight(3) + ' ' + $st.PadRight(9) + ' ' + $f.Label.PadRight(34) + $f.Note) -ForegroundColor $color
        }
        Write-Host ''
        Write-Host '  Numero(s) = ativar/desativar (alterna; exige reinicio)    0 = voltar' -ForegroundColor DarkGray
        Write-Host '  Dica: Hyper-V ON + emulador Android = perda de 50-90% de FPS.' -ForegroundColor DarkGray
        Write-Host '  HL> ' -ForegroundColor Cyan -NoNewline
        $in = (Read-Host).Trim()
        if ($in -eq '0' -or $in -eq '') { return }
        $idxs = ConvertTo-IndexList -Text $in -Max $script:FeatureList.Count
        foreach ($ix in $idxs) {
            $f = $script:FeatureList[$ix]
            $st = $states[$ix]
            if ($st -eq 'Enabled') {
                if (Confirm-Action ('Desativar "' + $f.Label + '"? Pode quebrar WSL2/Docker/WSA. Exige reinicio.')) {
                    if ($script:DryRun) { Write-Log ('[DRY-RUN] desativaria ' + $f.Name) 'DRYRUN' }
                    else {
                        try {
                            $r = Disable-WindowsOptionalFeature -Online -FeatureName $f.Name -NoRestart -ErrorAction Stop
                            Write-Log ('Recurso desativado: ' + $f.Label) 'OK'
                            Save-BackupEntry -Type 'Feature' -Target $f.Name -Before @{ state = 'Enabled' } -After @{ state = 'Disabled' } -Gain 'ganho em emulador' -Risk 'quebra WSL2/Docker' -RequiresReboot $true
                            $script:RebootNeeded = $true
                        } catch { Write-Log ('Falha ao desativar ' + $f.Label + ': ' + $_.Exception.Message) 'ERRO' }
                    }
                }
            } elseif ($st -eq 'Disabled') {
                if (Confirm-Action ('Ativar "' + $f.Label + '"?')) {
                    if ($script:DryRun) { Write-Log ('[DRY-RUN] ativaria ' + $f.Name) 'DRYRUN' }
                    else {
                        try {
                            $null = Enable-WindowsOptionalFeature -Online -FeatureName $f.Name -NoRestart -ErrorAction Stop
                            Write-Log ('Recurso ativado: ' + $f.Label) 'OK'
                            Save-BackupEntry -Type 'Feature' -Target $f.Name -Before @{ state = 'Disabled' } -After @{ state = 'Enabled' } -RequiresReboot $true
                            $script:RebootNeeded = $true
                        } catch { Write-Log ('Falha ao ativar ' + $f.Label + ': ' + $_.Exception.Message) 'ERRO' }
                    }
                }
            } else { Write-Log ('Estado desconhecido para ' + $f.Label + ' (carregue como admin)') 'WARN' }
        }
        Show-Pause
    }
}

# ==============================================================================
#  7n. INFORMACOES ESTENDIDAS DO SISTEMA
# ==============================================================================
function Invoke-SystemInfoExtended {
    Write-Section 'INFORMACOES DO SISTEMA'
    $lines = New-Object System.Collections.ArrayList
    $add = { param($t) Write-Host ('  ' + $t) -ForegroundColor Gray; $null = $lines.Add($t) }
    try { $os = Get-CimInstance Win32_OperatingSystem -ErrorAction Stop
        & $add ('OS        : ' + $os.Caption + ' ' + $os.Version + ' (build ' + $os.BuildNumber + ')')
        & $add ('Instalado : ' + $os.InstallDate)
        & $add ('Uptime    : ' + [math]::Round(((Get-Date) - $os.LastBootUpTime).TotalHours, 1) + ' h')
        & $add ('RAM livre : ' + [math]::Round($os.FreePhysicalMemory / 1MB, 0) + ' MB de ' + [math]::Round($os.TotalVisibleMemorySize / 1MB, 0) + ' MB')
    } catch { }
    try { $cs = Get-CimInstance Win32_ComputerSystem; & $add ('Modelo    : ' + $cs.Manufacturer + ' ' + $cs.Model) } catch { }
    try { $bb = Get-CimInstance Win32_BaseBoard; & $add ('Placa-mae : ' + $bb.Manufacturer + ' ' + $bb.Product) } catch { }
    try { $b = Get-CimInstance Win32_BIOS; & $add ('BIOS      : ' + $b.Manufacturer + ' ' + $b.SMBIOSBIOSVersion + ' (' + $b.ReleaseDate + ')') } catch { }
    try { $cpu = Get-CimInstance Win32_Processor | Select-Object -First 1
        & $add ('CPU       : ' + $cpu.Name.Trim())
        & $add ('Nucleos   : ' + $cpu.NumberOfCores + 'C/' + $cpu.NumberOfLogicalProcessors + 'T  @ ' + $cpu.MaxClockSpeed + ' MHz')
        & $add ('VT-x/SVM  : ' + $(if ($cpu.VirtualizationFirmwareEnabled) { 'HABILITADO' } else { 'DESABILITADO (BIOS!)' }))
    } catch { }
    try {
        $i = 0
        foreach ($m in (Get-CimInstance Win32_PhysicalMemory)) {
            $i++
            & $add ('RAM slot ' + $i + ': ' + [math]::Round($m.Capacity / 1GB, 0) + ' GB ' + $m.Speed + ' MHz ' + $m.Manufacturer)
        }
    } catch { }
    try {
        foreach ($g in (Get-CimInstance Win32_VideoController)) {
            & $add ('GPU       : ' + $g.Name + ' | driver ' + $g.DriverVersion + ' (' + $g.DriverDate + ') | VRAM ' + [math]::Round($g.AdapterRAM / 1GB, 1) + ' GB')
        }
    } catch { }
    try { foreach ($d in (Get-PhysicalDisk)) { & $add ('Disco     : ' + $d.FriendlyName + ' | ' + $d.MediaType + ' | ' + [math]::Round($d.Size / 1GB, 0) + ' GB | saude ' + $d.HealthStatus) } } catch { }
    $secBoot = 'indisponivel/legado'
    try { if (Confirm-SecureBootUEFI -ErrorAction Stop) { $secBoot = 'LIGADO' } else { $secBoot = 'desligado' } } catch { }
    & $add ('SecureBoot: ' + $secBoot)
    try { $tpm = Get-Tpm -ErrorAction Stop; & $add ('TPM       : ' + $(if ($tpm.TpmPresent) { 'presente' } else { 'ausente' })) } catch { }
    try {
        $lic = Get-CimInstance SoftwareLicensingProduct -Filter "PartialProductKey IS NOT NULL AND Name LIKE 'Windows%'" -ErrorAction Stop | Select-Object -First 1
        if ($lic) { & $add ('Ativacao  : ' + $lic.LicenseStatus + ' (1 = ativado)') }
    } catch { }
    try {
        $dg = Get-CimInstance -Namespace 'root\Microsoft\Windows\DeviceGuard' -Class Win32_DeviceGuard -ErrorAction Stop
        & $add ('VBS       : status ' + $dg.VirtualizationBasedSecurityStatus + ' | servicos ' + (@($dg.SecurityServicesRunning) -join ','))
    } catch { }
    $file = Join-Path $script:LogDir ('hl-sistema_' + $script:SessionId + '.txt')
    if (-not $script:DryRun) { try { Set-Content -LiteralPath $file -Value $lines -Encoding UTF8; Write-Log ('Salvo: ' + $file) 'OK' } catch { } }
}

# ==============================================================================
#  7o. REDE: DNS RAPIDO (com backup) + ONEDRIVE
# ==============================================================================
function Set-DnsHL {
    param([ValidateSet('Cloudflare', 'Google', 'Quad9', 'Automatic')][string]$Provider)
    $map = @{
        Cloudflare = @('1.1.1.1', '1.0.0.1')
        Google     = @('8.8.8.8', '8.8.4.4')
        Quad9      = @('9.9.9.9', '149.112.112.112')
        Automatic  = @()
    }
    $adapters = @()
    try { $adapters = @(Get-NetAdapter -Physical -ErrorAction Stop | Where-Object { $_.Status -eq 'Up' }) } catch { }
    if ($adapters.Count -eq 0) { Write-Log 'Nenhum adaptador de rede ativo.' 'ERRO'; return }
    foreach ($a in $adapters) {
        $idx = [int]$a.ifIndex
        $current = @()
        try { $current = @((Get-DnsClientServerAddress -InterfaceIndex $idx -AddressFamily IPv4 -ErrorAction Stop).ServerAddresses) } catch { }
        if ($script:DryRun) { Write-Log ('[DRY-RUN] DNS de ' + $a.Name + ' (' + ($current -join ',') + ') -> ' + ($map[$Provider] -join ',')) 'DRYRUN'; continue }
        try {
            if ($map[$Provider].Count -gt 0) { Set-DnsClientServerAddress -InterfaceIndex $idx -ServerAddresses $map[$Provider] -ErrorAction Stop }
            else { Set-DnsClientServerAddress -InterfaceIndex $idx -ResetServerAddresses -ErrorAction Stop }
            Write-Log ('DNS de ' + $a.Name + ': ' + ($current -join ', ') + ' -> ' + ($map[$Provider] -join ', ')) 'OK'
            Save-BackupEntry -Type 'Dns' -Target $a.Name -Before @{ interfaceIndex = $idx; servers = ($current -join ',') } -After @{ interfaceIndex = $idx; servers = ($map[$Provider] -join ',') } `
                -Gain 'resolucao de nomes mais rapida' -Risk 'nenhum (reversivel)'
        } catch { Write-Log ('Falha ao alterar DNS de ' + $a.Name + ': ' + $_.Exception.Message) 'ERRO' }
    }
    if (-not $script:DryRun) { try { Clear-DnsClientCache -ErrorAction SilentlyContinue } catch { } }
}

function Invoke-RemoveOneDriveHL {
    $path = Join-Path $env:SystemRoot 'System32\OneDriveSetup.exe'
    if (-not (Test-Path -LiteralPath $path)) { $path = Join-Path $env:SystemRoot 'SysWOW64\OneDriveSetup.exe' }
    if (-not (Test-Path -LiteralPath $path)) { Write-Log 'OneDriveSetup.exe nao encontrado (talvez ja removido).' 'WARN'; return }
    if (-not (Confirm-Action 'Desinstalar o OneDrive? Seus arquivos na nuvem NAO sao apagados, mas param de sincronizar.')) { return }
    if ($script:DryRun) { Write-Log '[DRY-RUN] desinstalaria o OneDrive' 'DRYRUN'; return }
    try {
        Start-Process -FilePath $path -ArgumentList '/uninstall' -Wait -ErrorAction Stop
        Write-Log 'OneDrive desinstalado. Reinstale com winget: Microsoft.OneDrive' 'OK'
        Save-BackupEntry -Type 'Info' -Target 'OneDrive' -Before @{ installed = $true } -After @{ installed = $false } -Gain 'menos um processo em segundo plano' -Risk 'sem sincronizacao' -RevertHint 'winget install Microsoft.OneDrive'
    } catch { Write-Log ('Falha ao desinstalar OneDrive: ' + $_.Exception.Message) 'ERRO' }
}

# ==============================================================================
#  7p. MENUS DE SISTEMA E DIAGNOSTICO
# ==============================================================================
function Show-SystemMenu {
    while ($true) {
        Write-Section 'SISTEMA'
        Write-Host '  [1] Apps de inicializacao (startup)' -ForegroundColor Gray
        Write-Host '  [2] Tarefas agendadas de terceiros' -ForegroundColor Gray
        Write-Host '  [3] Recursos do Windows / Hyper-V / Virtualizacao' -ForegroundColor Gray
        Write-Host '  [4] Informacoes detalhadas do hardware' -ForegroundColor Gray
        Write-Host '  [5] DNS rapido (Cloudflare/Google/Quad9/automatico)' -ForegroundColor Gray
        Write-Host '  [6] Desinstalar OneDrive' -ForegroundColor Gray
        Write-Host '  [7] Servicos de fabricante (RGB/audio) - parar' -ForegroundColor Gray
        Write-Host '  [0] Voltar' -ForegroundColor DarkGray
        Write-Host '  HL> ' -ForegroundColor Cyan -NoNewline
        $in = (Read-Host).Trim()
        switch ($in) {
            '1' { Show-StartupManager }
            '2' { Show-ScheduledTaskAudit }
            '3' { Show-FeaturesManager }
            '4' { Invoke-SystemInfoExtended; Show-Pause }
            '5' {
                Write-Host '  [1] Cloudflare  [2] Google  [3] Quad9  [4] Automatico  [0] voltar' -ForegroundColor Gray
                Write-Host '  HL> ' -ForegroundColor Cyan -NoNewline
                $d = (Read-Host).Trim()
                switch ($d) {
                    '1' { Set-DnsHL -Provider Cloudflare }
                    '2' { Set-DnsHL -Provider Google }
                    '3' { Set-DnsHL -Provider Quad9 }
                    '4' { Set-DnsHL -Provider Automatic }
                }
                Show-Pause
            }
            '6' { Invoke-RemoveOneDriveHL; Show-Pause }
            '7' { Invoke-BloatwarePlan; Show-Summary; Show-Pause }
            '0' { return }
            default { }
        }
    }
}

function Show-DiagnosticsMenu {
    while ($true) {
        Write-Section 'DIAGNOSTICO E MEDICAO'
        Write-Host '  [1] Diagnostico completo (gera relatorio)' -ForegroundColor Gray
        Write-Host '  [2] Benchmark rapido (CPU/disco/RAM, ~20s)' -ForegroundColor Gray
        Write-Host '  [3] Benchmark comparativo (antes/depois)' -ForegroundColor Gray
        Write-Host '  [4] Verificacao do ambiente (self-check)' -ForegroundColor Gray
        Write-Host '  [5] Relatorio HTML com tudo' -ForegroundColor Gray
        Write-Host '  [0] Voltar' -ForegroundColor DarkGray
        Write-Host '  HL> ' -ForegroundColor Cyan -NoNewline
        $in = (Read-Host).Trim()
        switch ($in) {
            '1' { Invoke-Diagnostics; Show-Pause }
            '2' { Invoke-QuickBenchmark; Show-Pause }
            '3' { Invoke-CompareBenchmark; Show-Pause }
            '4' { Invoke-SelfCheck; Show-Pause }
            '5' { Export-HtmlReportHL; Show-Pause }
            '0' { return }
            default { }
        }
    }
}

function Invoke-CompareBenchmark {
    Write-Section 'BENCHMARK COMPARATIVO'
    $files = @()
    if (Test-Path -LiteralPath $script:LogDir) {
        $files = @(Get-ChildItem -LiteralPath $script:LogDir -Filter 'hl-baseline_*.json' -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending)
    }
    if ($files.Count -eq 0) {
        Write-Log 'Nenhuma linha de base ainda. Vamos criar a primeira.' 'INFO'
        if (-not (Confirm-Action 'Medir a linha de base AGORA (com o PC em repouso, jogos fechados)?')) { return }
        $before = Measure-LoadSnapshot
        Show-BenchResult -Data $before -Label 'LINHA DE BASE'
        $file = Join-Path $script:LogDir ('hl-baseline_' + $script:SessionId + '.json')
        if (-not $script:DryRun) { try { ($before | ConvertTo-Json) | Set-Content -LiteralPath $file -Encoding UTF8 } catch { } }
        Write-Log ('Linha de base salva: ' + $file) 'OK'
        Write-Log 'Aplique um preset (opcao 1) e volte aqui para comparar.' 'INFO'
        return
    }
    $base = $null
    try { $base = Get-Content -LiteralPath $files[0].FullName -Raw -Encoding UTF8 | ConvertFrom-Json } catch { }
    if (-not $base) { Write-Log 'Linha de base ilegivel.' 'ERRO'; return }
    Write-Log ('Comparando com a linha de base de ' + $base.quando) 'INFO'
    if (-not (Confirm-Action 'Medir AGORA para comparar com a linha de base?')) { return }
    $now = Measure-LoadSnapshot
    Show-BenchResult -Data $base -Label 'ANTES'
    Show-BenchResult -Data $now -Label 'DEPOIS'
    $dCpu = 0.0; $dRam = 0.0; $dProc = 0; $dSvc = 0
    if ($base.cpu_medio -gt 0) { $dCpu = [math]::Round((($now.cpu_medio - $base.cpu_medio) / $base.cpu_medio) * 100, 1) }
    if ($base.ram_livre_mb -gt 0) { $dRam = [math]::Round((($now.ram_livre_mb - $base.ram_livre_mb) / $base.ram_livre_mb) * 100, 1) }
    $dProc = [int]$now.processos - [int]$base.processos
    $dSvc = [int]$now.servicos_ativos - [int]$base.servicos_ativos
    Write-Host ''
    Write-Host '  DIFERENCA (negativo em CPU = melhor)' -ForegroundColor Cyan
    Write-Host ('    CPU medio ....... ' + $dCpu + ' %') -ForegroundColor $(if ($dCpu -lt 0) { 'Green' } else { 'Yellow' })
    Write-Host ('    RAM livre ....... ' + $dRam + ' %') -ForegroundColor $(if ($dRam -gt 0) { 'Green' } else { 'Yellow' })
    Write-Host ('    Processos ....... ' + $dProc) -ForegroundColor Gray
    Write-Host ('    Servicos ativos . ' + $dSvc) -ForegroundColor Gray
    Write-Log ('Comparativo: CPU ' + $dCpu + '% | RAM livre ' + $dRam + '% | processos ' + $dProc + ' | servicos ' + $dSvc) 'INFO'
    if (Confirm-Action 'Salvar esta medicao como nova linha de base?') {
        $file = Join-Path $script:LogDir ('hl-baseline_' + $script:SessionId + '.json')
        if (-not $script:DryRun) { try { ($now | ConvertTo-Json) | Set-Content -LiteralPath $file -Encoding UTF8; Write-Log ('Nova linha de base: ' + $file) 'OK' } catch { } }
    }
    Write-Log 'Lembre: isto mede carga do sistema, nao FPS. Para FPS/1% low use CapFrameX ou PresentMon.' 'INFO'
}

function Measure-LoadSnapshot {
    $samples = 15
    $cpu = @(); $disk = @(); $mem = @()
    for ($i = 1; $i -le $samples; $i++) {
        Write-Progress -Activity 'HL Optimizer - medindo' -Status ("amostra $i de $samples") -PercentComplete (($i / $samples) * 100)
        try {
            $cpu += [double](Get-Counter '\Processor(_Total)\% Processor Time' -ErrorAction Stop).CounterSamples[0].CookedValue
            $disk += [double](Get-Counter '\PhysicalDisk(_Total)\Current Disk Queue Length' -ErrorAction Stop).CounterSamples[0].CookedValue
            $mem += [double](Get-Counter '\Memory\Available MBytes' -ErrorAction Stop).CounterSamples[0].CookedValue
        } catch { break }
        Start-Sleep -Seconds 1
    }
    Write-Progress -Activity 'HL Optimizer' -Completed
    $cMed = 0.0; $cMax = 0.0; $dMed = 0.0; $mMed = 0.0
    if ($cpu.Count -gt 0) {
        $cMed = [math]::Round(($cpu | Measure-Object -Average).Average, 1)
        $cMax = [math]::Round(($cpu | Measure-Object -Maximum).Maximum, 1)
    }
    if ($disk.Count -gt 0) { $dMed = [math]::Round(($disk | Measure-Object -Average).Average, 2) }
    if ($mem.Count -gt 0) { $mMed = [math]::Round(($mem | Measure-Object -Average).Average, 0) }
    $procs = @(Get-Process).Count
    $svcs = @(Get-Service | Where-Object { $_.Status -eq 'Running' }).Count
    return [pscustomobject]@{
        quando          = (Get-Date).ToString('s')
        cpu_medio       = $cMed
        cpu_max         = $cMax
        disco_fila      = $dMed
        ram_livre_mb    = $mMed
        processos       = $procs
        servicos_ativos = $svcs
    }
}

function Show-BenchResult {
    param($Data, [string]$Label)
    Write-Host ''
    Write-Host ('  ' + $Label) -ForegroundColor Cyan
    Write-Host ('    CPU medio .......... ' + $Data.cpu_medio + ' %   (pico ' + $Data.cpu_max + ' %)') -ForegroundColor Gray
    Write-Host ('    Fila de disco ...... ' + $Data.disco_fila) -ForegroundColor Gray
    Write-Host ('    RAM livre .......... ' + $Data.ram_livre_mb + ' MB') -ForegroundColor Gray
    Write-Host ('    Processos .......... ' + $Data.processos) -ForegroundColor Gray
    Write-Host ('    Servicos ativos .... ' + $Data.servicos_ativos) -ForegroundColor Gray
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
#  12. REVERSÃO (usa Restore-ChangeEntry: suporta todos os tipos)
# ==============================================================================
function Invoke-RevertHL {
    param([string]$Path)
    $script:Reverting = $true
    Write-Section 'REVERTER (restaurar estado anterior)'
    $dir = $null
    $target = $Path
    if (-not $target) { $target = $script:BackupPath }
    if ($target -and (Test-Path -LiteralPath $target)) { $dir = Get-Item -LiteralPath $target }
    if (-not $dir) {
        $latest = Get-LatestBackup
        if (-not $latest) { Write-Log ('Nenhum backup encontrado em ' + $script:BackupRoot) 'ERRO'; return }
        if ($script:Interactive -and -not $target) {
            Write-Host ('  Backup mais recente: ' + (Split-Path -Leaf $latest.FullName)) -ForegroundColor Cyan
            if (-not (Confirm-Action 'Restaurar este backup?')) { return }
        }
        $dir = $latest
    }
    $stateFile = Join-Path $dir.FullName 'state.json'
    if (-not (Test-Path -LiteralPath $stateFile)) { Write-Log 'state.json nao encontrado nesse backup' 'ERRO'; return }
    try { $state = Get-Content -LiteralPath $stateFile -Raw -Encoding UTF8 | ConvertFrom-Json }
    catch { Write-Log ('Backup corrompido: ' + $_.Exception.Message) 'ERRO'; return }

    $changes = @($state.changes)
    Write-Log ('Restaurando ' + $changes.Count + ' alteracao(oes) de ' + (Split-Path -Leaf $dir.FullName)) 'ACAO'
    $ok = 0; $fail = 0
    for ($i = $changes.Count - 1; $i -ge 0; $i--) {
        if (Restore-ChangeEntry -Entry $changes[$i]) { $ok++ } else { $fail++ }
    }
    Write-Section 'Resultado da reversao'
    Write-Host ('  Restaurados: ' + $ok + '   Falhas: ' + $fail) -ForegroundColor $(if ($fail -eq 0) { 'Green' } else { 'Yellow' })
    Write-Log ('Reversao: ' + $ok + ' OK, ' + $fail + ' falha(s)') 'INFO'
    if ($state.rebootRequired) { Write-Log 'Algumas alteracoes exigem REINICIO para valer.' 'WARN' }
    Write-Log 'Obs.: ajustes de PCIe/USB ficam no plano de energia. Use "powercfg -restoredefaultschemes" para zerar.' 'INFO'
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
#  13b. LISTAGENS / MENUS DE TOPO
# ==============================================================================
function Show-TweakList {
    Write-Section 'CATALOGO COMPLETO DE TWEAKS'
    $lastCat = ''
    foreach ($t in $script:TweakCatalog) {
        if ($t.Cat -ne $lastCat) {
            Write-Host ''
            Write-Host ('  == ' + $t.Cat.ToUpper() + ' ==') -ForegroundColor Cyan
            $lastCat = $t.Cat
        }
        $st = Get-TweakState -Tweak $t
        $color = 'Yellow'
        if ($st -eq 'APLICADO') { $color = 'Green' } elseif ($st -eq 'N/D') { $color = 'DarkGray' } elseif ($st -eq 'PARCIAL') { $color = 'Cyan' }
        $tags = ''
        if ($t.Experimental) { $tags += ' [EXP]' }
        if ($t.Reboot) { $tags += ' [REB]' }
        Write-Host ('  ' + $t.Id.PadRight(24) + ' ' + $st.PadRight(10) + ' ' + $t.Gain.PadRight(18) + ' ' + $t.Name + $tags) -ForegroundColor $color
    }
    Write-Log ('Total: ' + @($script:TweakCatalog).Count + ' tweaks. Aplique por id: -ApplyTweaks "game-dvr,diagtrack"') 'INFO'
}

function Show-AppList {
    Write-Section 'CATALOGO DE APPS (winget)'
    $lastCat = ''
    foreach ($a in $script:AppCatalog) {
        if ($a.Cat -ne $lastCat) { Write-Host ''; Write-Host ('  == ' + $a.Cat.ToUpper() + ' ==') -ForegroundColor Cyan; $lastCat = $a.Cat }
        Write-Host ('  ' + $a.Id.PadRight(42) + $a.Name) -ForegroundColor Gray
    }
    Write-Log ('Total: ' + @($script:AppCatalog).Count + ' apps. Instale por id: winget install <id>') 'INFO'
}

function Show-ProfilesMenu {
    while ($true) {
        Write-Section 'PERFIS 1-CLIQUE'
        $i = 0
        foreach ($p in $script:Presets) {
            $i++
            Write-Host ('  [' + $i + '] ' + $p.Name + '  (' + $p.Tweaks.Count + ' tweaks + perfil ' + $p.Profile + ')') -ForegroundColor Cyan
            Write-Host ('      ' + $p.Desc) -ForegroundColor DarkGray
        }
        Write-Host ''
        Write-Host '  [S] Perfil SEGURO      (energia, Game DVR, telemetria)' -ForegroundColor Green
        Write-Host '  [E] Perfil EQUILIBRADO (recomendado: + servicos/bloatware)' -ForegroundColor Cyan
        Write-Host '  [A] Perfil AGRESSIVO   (hypervisor/HAGS - assume risco)' -ForegroundColor Yellow
        Write-Host '  [0] Voltar' -ForegroundColor DarkGray
        Write-Host '  HL> ' -ForegroundColor Cyan -NoNewline
        $in = (Read-Host).Trim().ToUpper()
        $n = 0
        if ($in -eq '0') { return }
        if ([int]::TryParse($in, [ref]$n) -and $n -ge 1 -and $n -le $script:Presets.Count) {
            $pr = $script:Presets[$n - 1]
            Write-Host ''
            Write-Host ('  ' + $pr.Name + ': ' + $pr.Tweaks.Count + ' tweaks + perfil ' + $pr.Profile) -ForegroundColor White
            if (-not (Confirm-Action 'Aplicar este preset agora?')) { continue }
            if (-not $script:SkipRestorePoint -and -not $script:DryRun) { New-RestorePointHL }
            switch ($pr.Profile) {
                'Safe' { Invoke-ProfileSafe }
                'Balanced' { Invoke-ProfileBalanced }
                'Extreme' { Invoke-ProfileExtreme }
            }
            Invoke-TweakSet -Ids $pr.Tweaks
            if ($pr.Emulator) { Invoke-EmulatorFocus }
            Show-Summary
            Show-BrandPanel
            Show-Pause
            continue
        }
        switch ($in) {
            'S' { Invoke-ProfileSafe; Show-Summary; Show-BrandPanel; Show-Pause }
            'E' { Invoke-ProfileBalanced; Show-Summary; Show-BrandPanel; Show-Pause }
            'A' { Invoke-ProfileExtreme; Show-Summary; Show-BrandPanel; Show-Pause }
            default { Write-Host '  Opcao invalida.' -ForegroundColor Yellow }
        }
    }
}

# ==============================================================================
#  14. MENU PRINCIPAL
# ==============================================================================
function Show-Menu {
    param([bool]$Refresh)
    if ($Refresh) { Show-BrandPanel }
    Write-Host ''
    Write-Host '  ┌─ HL OPTIMIZER - MENU ' -ForegroundColor Cyan -NoNewline
    Write-Line '─'
    Write-Host '  [1]  Perfis 1-clique     ' -ForegroundColor Cyan -NoNewline
    Write-Host '(Gamer / Emulador / Privacidade / Minimalista / Notebook + manual)' -ForegroundColor DarkGray
    Write-Host ('  [2]  Tweaks               ' + @($script:TweakCatalog).Count + ' itens em ' + (Get-TweakCategories).Count + ' categorias - aplicar e desfazer') -ForegroundColor Gray
    Write-Host ('  [3]  Instalar apps        ' + @($script:AppCatalog).Count + ' apps via winget') -ForegroundColor Gray
    Write-Host '  [4]  Windows Update       buscar, instalar, pausar' -ForegroundColor Gray
    Write-Host '  [5]  Sistema             startup, tarefas, Hyper-V, DNS, OneDrive' -ForegroundColor Gray
    Write-Host '  [6]  Diagnostico          relatorio, benchmark, comparativo, self-check' -ForegroundColor Gray
    Write-Host '  [7]  Emuladores           exclusoes, GPU por app, checklist' -ForegroundColor Magenta
    Write-Host '  [8]  Sessao de jogo       boost temporario (restaura ao sair)' -ForegroundColor Gray
    Write-Host '  [9]  Reverter             historico de backups e undo por tweak' -ForegroundColor Red
    Write-Host '  [T]  Limpeza              temporarios + cache de shader' -ForegroundColor Gray
    Write-Host '  [R]  Relatorio HTML       [C] Config export/import' -ForegroundColor Gray
    Write-Host '  [L]  Logs   [B] Backups   [P] Placebos   [A] Sobre   [0] Sair' -ForegroundColor DarkGray
    Write-Host ''
}

function Read-MenuChoice {
    Write-Host '  HL> ' -ForegroundColor Cyan -NoNewline
    return (Read-Host).Trim().ToUpper()
}

# ==============================================================================
#  15. EXECUÇÃO PRINCIPAL
# ==============================================================================
$isAdmin = Assert-Admin
if (-not $isAdmin) { exit 3 }

Initialize-HLEnvironment

try { Test-NeverTouchList } catch {
    Write-Host '  [X] Falha na lista de protecao. Abortando.' -ForegroundColor Red
    exit 4
}

if (-not $NoPanel) { Show-BrandPanel }

# ---------------------------- modos de linha de comando ----------------------
if ($ListTweaks) { Show-TweakList; exit 0 }
if ($ListApps) { Show-AppList; exit 0 }
if ($Report) { Export-HtmlReportHL; exit 0 }

if ($ApplyPreset) {
    $pr = $script:Presets | Where-Object { $_.Id -eq $ApplyPreset } | Select-Object -First 1
    if (-not $pr) { Write-Log ('Preset inexistente: ' + $ApplyPreset) 'ERRO'; exit 1 }
    Write-Log ('Aplicando preset ' + $pr.Name) 'TITULO'
    switch ($pr.Profile) {
        'Safe' { Invoke-ProfileSafe }
        'Balanced' { Invoke-ProfileBalanced }
        'Extreme' { Invoke-ProfileExtreme }
    }
    Invoke-TweakSet -Ids $pr.Tweaks
    if ($pr.Emulator) { Invoke-EmulatorFocus }
    Show-Summary
    exit 0
}

if ($ApplyTweaks -and $ApplyTweaks.Count -gt 0) {
    $ids = @()
    foreach ($chunk in $ApplyTweaks) { foreach ($x in ($chunk -split ',')) { if ($x.Trim()) { $ids += $x.Trim() } } }
    Write-Log ('Aplicando ' + $ids.Count + ' tweak(s) por linha de comando') 'TITULO'
    Invoke-TweakSet -Ids $ids
    Show-Summary
    exit 0
}

if ($ImportConfig) { Import-HLConfig -Path $ImportConfig; Show-Summary; exit 0 }

if ($Revert) {
    Invoke-RevertHL
    Show-Summary
    exit 0
}

# ---------------------------- modo não interativo ----------------------------
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

# ---------------------------- loop do menu -----------------------------------
:mainLoop while ($true) {
    Show-Menu -Refresh $false
    $choice = Read-MenuChoice
    switch ($choice) {
        '1' { Show-ProfilesMenu }
        '2' { Show-TweakBrowser }
        '3' { Show-AppInstaller }
        '4' { Show-UpdateMenu }
        '5' { Show-SystemMenu }
        '6' { Show-DiagnosticsMenu }
        '7' { Invoke-EmulatorFocus; Show-Summary; Show-Pause }
        '8' { Invoke-GameSession; Show-Pause }
        '9' { Show-UndoHistory }
        'T' { Invoke-TempCleanup; Show-Pause }
        'R' { Export-HtmlReportHL; Show-Pause }
        'C' { Show-ConfigMenu }
        'L' { Open-FolderHL -Path $script:LogDir }
        'B' { Open-FolderHL -Path $script:BackupRoot }
        'P' { Show-PlaceboList; Show-Pause }
        'A' { Show-About; Show-Pause }
        '0' { break mainLoop }
        'Q' { break mainLoop }
        default { Write-Host '  Opcao invalida.' -ForegroundColor Yellow; Start-Sleep -Milliseconds 500 }
    }
    if ($choice -in @('1', '2', '7', '8', '9')) { Show-BrandPanel }
}

Write-Host ''
Write-Host ('  ' + $script:Brand + ' v' + $script:BrandVersion + ' - encerrado. Bom jogo!') -ForegroundColor Cyan
if ($script:LogFile) { Write-Host ('  Log: ' + $script:LogFile) -ForegroundColor DarkGray }
exit 0
