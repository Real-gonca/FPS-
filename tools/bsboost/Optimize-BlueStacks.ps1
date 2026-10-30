<#
.SYNOPSIS
    Ajusta o BlueStacks 5 e o Windows para deixar o Free Fire mais leve.
    Com backup, dry-run e rollback. Nao instala nada e nao injeta nada.

.DESCRIPTION
    O FPS do Free Fire dentro do BlueStacks e limitado por: GPU, virtualizacao,
    resolucao, nucleos/RAM alocados na instancia e limite de FPS. Nada disso e
    "destravado" por DLL injetada. Este script mexe nas configuracoes reais:

      1. bluestacks.conf  - resolucao, DPI, limite de FPS, nucleos, RAM,
                            otimizacoes de graficos (somente chaves que existem)
      2. Windows          - perfil de GPU de alto desempenho para HD-Player.exe
      3. Plano de energia - alto desempenho (opcional, com rollback)

    Seguro por design: so altera chaves existentes, sempre faz backup do
    arquivo inteiro e grava um inventario das mudancas para rollback exato.

.PARAMETER DryRun
    Mostra o que mudaria e nao escreve nada. Use primeiro.

.PARAMETER Rollback
    Restaura o ultimo backup e desfaz os ajustes de registro/energia.

.PARAMETER Instance
    Nome da instancia do BlueStacks (ex.: Rvc64, Pie64). Padrao: todas.

.EXAMPLE
    .\Optimize-BlueStacks.ps1 -DryRun
    .\Optimize-BlueStacks.ps1
    .\Optimize-BlueStacks.ps1 -Rollback
#>
[CmdletBinding()]
param(
    [switch]$DryRun,
    [switch]$Rollback,
    [string]$Instance,
    [ValidateSet('low','balanced','quality')]
    [string]$Preset = 'balanced',
    [int]$MaxCores = 4,
    [int]$RamMb = 4096,
    [int]$Fps = 60,
    [switch]$SkipPowerPlan,
    [switch]$DisableFullscreenOptimizations
)

$ErrorActionPreference = 'Stop'
$script:Changed = [ordered]@{}
$script:Notes = New-Object System.Collections.Generic.List[string]

$stateDir = Join-Path $env:ProgramData 'BsBoost'
$backupDir = Join-Path $stateDir 'backup'
$stateFile = Join-Path $stateDir 'state.json'

function Write-Step($text) { Write-Host "`n== $text" -ForegroundColor Cyan }
function Write-Ok($text)   { Write-Host "   [ok] $text" -ForegroundColor Green }
function Write-Info($text) { Write-Host "   $text" -ForegroundColor Gray }
function Write-Warn2($text){ Write-Host "   [!] $text" -ForegroundColor Yellow }

if (-not (Test-Path $stateDir)) { New-Item -ItemType Directory -Force -Path $stateDir | Out-Null }
if (-not (Test-Path $backupDir)) { New-Item -ItemType Directory -Force -Path $backupDir | Out-Null }

# ------------------------------------------------------------------ #
# Rollback                                                            #
# ------------------------------------------------------------------ #
if ($Rollback) {
    Write-Step 'Rollback'

    if (-not (Test-Path $stateFile)) {
        Write-Warn2 "Nenhum estado em $stateFile. Nada para desfazer."
        exit 0
    }
    $state = Get-Content $stateFile -Raw | ConvertFrom-Json

    if ($state.confBackup -and (Test-Path $state.confBackup)) {
        if ($DryRun) {
            Write-Info "[dry-run] restauraria $($state.confPath) de $($state.confBackup)"
        } else {
            Copy-Item $state.confBackup $state.confPath -Force
            Write-Ok "bluestacks.conf restaurado de $($state.confBackup)"
        }
    }

    foreach ($entry in $state.gpuPrefsAdded) {
        if ($DryRun) { Write-Info "[dry-run] removeria preferencia de GPU de $entry"; continue }
        Remove-ItemProperty -Path 'HKCU:\Software\Microsoft\DirectX\UserGpuPreferences' `
                            -Name $entry -ErrorAction SilentlyContinue
        Write-Ok "preferencia de GPU removida: $entry"
    }

    foreach ($entry in $state.appCompatAdded) {
        if ($DryRun) { continue }
        Remove-ItemProperty -Path $entry.key -Name $entry.name -ErrorAction SilentlyContinue
        Write-Ok "AppCompatFlags removido: $($entry.name)"
    }

    if ($state.previousPowerPlan) {
        if ($DryRun) {
            Write-Info "[dry-run] voltaria o plano de energia para $($state.previousPowerPlan)"
        } else {
            powercfg /setactive $state.previousPowerPlan | Out-Null
            Write-Ok "plano de energia restaurado: $($state.previousPowerPlan)"
        }
    }

    if (-not $DryRun) {
        $archived = "$stateFile.undone-$(Get-Date -Format 'yyyyMMdd-HHmmss')"
        Move-Item $stateFile $archived -Force
        Write-Ok "estado arquivado em $archived"
    }
    exit 0
}

# ------------------------------------------------------------------ #
# 1. Diagnostico (somente leitura)                                     #
# ------------------------------------------------------------------ #
Write-Step 'Diagnostico'

$cpu = Get-CimInstance Win32_Processor | Select-Object -First 1
$physicalCores = [int]($cpu.NumberOfCores)
$logical = [int]($cpu.NumberOfLogicalProcessors)
Write-Info "CPU: $($cpu.Name.Trim()) - $physicalCores nucleos fisicos / $logical logicos"

if ($cpu.VirtualizationFirmwareEnabled -eq $false) {
    Write-Warn2 'VT-x/AMD-V desabilitado na BIOS. Sem isso o BlueStacks fica MUITO mais lento que o normal.'
    $script:Notes.Add('ativar VT-x/AMD-V na BIOS')
}
$hyperv = (Get-CimInstance Win32_ComputerSystem).HypervisorPresent
Write-Info "Hipervisor do Windows presente: $hyperv (BlueStacks 5 usa WHPX quando true)"

$gpus = Get-CimInstance Win32_VideoController | Select-Object Name, AdapterRAM
foreach ($g in $gpus) {
    $vram = if ($g.AdapterRAM -and $g.AdapterRAM -gt 0) { "{0:N0} MB" -f ($g.AdapterRAM / 1MB) } else { 'n/d' }
    Write-Info "GPU: $($g.Name) (VRAM reportada: $vram)"
}

$ramGb = [math]::Round((Get-CimInstance Win32_ComputerSystem).TotalPhysicalMemory / 1GB, 1)
Write-Info "RAM total: $ramGb GB"

# ------------------------------------------------------------------ #
# 2. bluestacks.conf                                                   #
# ------------------------------------------------------------------ #
Write-Step 'BlueStacks (bluestacks.conf)'

$confCandidates = @(
    'C:\ProgramData\BlueStacks_nxt\bluestacks.conf',
    'C:\ProgramData\BlueStacks_bgp64\bluestacks.conf',
    'C:\ProgramData\BlueStacks\bluestacks.conf'
)
$confPath = $confCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1

if (-not $confPath) {
    Write-Warn2 'bluestacks.conf nao encontrado. BlueStacks 5 instalado para todos os usuarios?'
}
else {
    Write-Info "arquivo: $confPath"
    $content = Get-Content -Path $confPath -Raw -Encoding UTF8

    # Preset -> valores desejados (nomes de chave sao sufixos)
    switch ($Preset) {
        'low'      { $res = @(960, 540);  $dpi = 200; $fpsTarget = [Math]::Min($Fps, 30); $hd = 0 }
        'balanced' { $res = @(1280, 720); $dpi = 240; $fpsTarget = [Math]::Min($Fps, 60); $hd = 0 }
        'quality'  { $res = @(1600, 900); $dpi = 240; $fpsTarget = [Math]::Min($Fps, 60); $hd = 1 }
    }

    $wanted = [ordered]@{
        'display_width'                  = $res[0]
        'display_height'                 = $res[1]
        'dpi'                            = $dpi
        'fps'                            = $fpsTarget
        'enable_high_definition_graphics'= $hd
        'enable_gl_optimizations'        = 1
        'enable_vsync'                   = 0
        'cpus'                           = [Math]::Min($MaxCores, [Math]::Max(2, $physicalCores - 2))
        'ram'                            = [Math]::Min($RamMb, [int]($ramGb * 1024 * 0.5))
        'enable_root_access'             = 0   # root ligado custa pouco, mas nao ajuda em nada
    }

    # Descobre instancias presentes no arquivo
    $instances = [regex]::Matches($content, 'bst\.instance\.([A-Za-z0-9_]+)\.') |
        ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique

    if (-not $instances) {
        Write-Warn2 'nenhuma instancia encontrada no bluestacks.conf (arquivo vazio ou formato novo).'
    }
    else {
        if ($Instance) {
            if ($instances -notcontains $Instance) {
                Write-Warn2 "instancia '$Instance' nao existe. Disponiveis: $($instances -join ', ')"
                exit 2
            }
            $instances = @($Instance)
        }
        Write-Info "instancias: $($instances -join ', ')"

        foreach ($inst in $instances) {
            foreach ($suffix in $wanted.Keys) {
                $key = "bst.instance.$inst.$suffix"
                $pattern = '(?m)^' + [regex]::Escape($key) + '="?([^"\r\n]*)"?\s*$'
                $m = [regex]::Match($content, $pattern)
                if (-not $m.Success) {
                    continue   # chave nao existe nesta versao: nao inventamos nenhuma
                }
                $old = $m.Groups[1].Value
                $new = [string]$wanted[$suffix]
                if ($old -eq $new) { continue }

                Write-Info ("{0}: {1} -> {2}" -f $key, $old, $new)
                $script:Changed[$key] = @{ old = $old; new = $new }

                if (-not $DryRun) {
                    $replacement = "$key=`"$new`""
                    $content = $content.Remove($m.Index, $m.Length).Insert($m.Index, $replacement)
                }
            }
        }

        if (-not $DryRun -and $script:Changed.Count -gt 0) {
            $stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
            $backup = Join-Path $backupDir "bluestacks.conf.$stamp.bak"
            Copy-Item $confPath $backup -Force
            Write-Ok "backup: $backup"

            $bsRunning = Get-Process -Name 'HD-Player', 'BlueStacksApp', 'BlueStacks' -ErrorAction SilentlyContinue
            if ($bsRunning) {
                Write-Warn2 'BlueStacks esta aberto. Feche-o completamente; as mudancas valem no proximo start.'
            }
            # UTF8 SEM BOM: o Qt do BlueStacks nao gosta de BOM no inicio do conf.
            $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
            [System.IO.File]::WriteAllText($confPath, $content, $utf8NoBom)
            Write-Ok "escrito: $($script:Changed.Count) chave(s) alterada(s)"
            $script:ConfBackup = $backup
        }
    }
}

# ------------------------------------------------------------------ #
# 3. Preferencia de GPU de alto desempenho                             #
# ------------------------------------------------------------------ #
Write-Step 'GPU de alto desempenho para HD-Player.exe'

$gpuKey = 'HKCU:\Software\Microsoft\DirectX\UserGpuPreferences'
if (-not (Test-Path $gpuKey)) { New-Item -Path $gpuKey -Force | Out-Null }

$playerPaths = @()
foreach ($base in @('C:\Program Files\BlueStacks_nxt', 'C:\Program Files\BlueStacks_bgp64',
                    'C:\Program Files (x86)\BlueStacks_nxt', 'C:\Program Files (x86)\BlueStacks')) {
    if (Test-Path $base) {
        $playerPaths += (Get-ChildItem -Path $base -Filter 'HD-Player.exe' -Recurse -ErrorAction SilentlyContinue |
                         Select-Object -ExpandProperty FullName)
    }
}

$gpuPrefsAdded = @()
if (-not $playerPaths) {
    Write-Warn2 'HD-Player.exe nao encontrado nos caminhos padrao.'
} else {
    foreach ($p in $playerPaths) {
        $existing = (Get-ItemProperty -Path $gpuKey -Name $p -ErrorAction SilentlyContinue).$p
        $value = 'GpuPreference=2;'   # 2 = alto desempenho
        if ($existing -eq $value) { Write-Info "ja configurado: $p"; continue }

        if ($DryRun) {
            Write-Info "[dry-run] definiria GpuPreference=2 para $p"
        } else {
            New-ItemProperty -Path $gpuKey -Name $p -Value $value -PropertyType String -Force | Out-Null
            if (-not $existing) { $gpuPrefsAdded += $p }
            Write-Ok "GpuPreference=2: $p"
        }
    }
}

# ------------------------------------------------------------------ #
# 4. Otimizacoes de tela cheia (opcional)                              #
# ------------------------------------------------------------------ #
$appCompatAdded = @()
if ($DisableFullscreenOptimizations -and $playerPaths) {
    Write-Step 'Fullscreen optimizations'
    $layersKey = 'HKCU:\Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers'
    if (-not (Test-Path $layersKey)) { New-Item -Path $layersKey -Force | Out-Null }
    foreach ($p in $playerPaths) {
        if ($DryRun) { Write-Info "[dry-run] desativaria otimizacoes de tela cheia para $p"; continue }
        $old = (Get-ItemProperty -Path $layersKey -Name $p -ErrorAction SilentlyContinue).$p
        New-ItemProperty -Path $layersKey -Name $p -Value '~ DISABLEDXMAXIMIZEDWINDOWEDMODE' `
                         -PropertyType String -Force | Out-Null
        if (-not $old) { $appCompatAdded += @{ key = $layersKey; name = $p } }
        Write-Ok "fullscreen optimizations desativadas: $p"
    }
}

# ------------------------------------------------------------------ #
# 5. Plano de energia                                                  #
# ------------------------------------------------------------------ #
$previousPlan = $null
if (-not $SkipPowerPlan) {
    Write-Step 'Plano de energia'
    $current = (powercfg /getactivescheme) -join ''
    if ($current -match '([0-9a-fA-F-]{36})') { $previousPlan = $Matches[1] }

    $highPerf = '8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c'
    if (Get-Command powercfg.exe -ErrorAction SilentlyContinue) {
        if ($DryRun) {
            Write-Info "[dry-run] ativaria o plano de alto desempenho ($highPerf)"
        } else {
            powercfg /setactive $highPerf 2>$null
            if ($LASTEXITCODE -eq 0) { Write-Ok 'plano de alto desempenho ativado' }
            else { Write-Warn2 'plano de alto desempenho indisponivel neste Windows (notebook com bateria costuma ocultar). Plano atual mantido.' }
        }
    }
}

# ------------------------------------------------------------------ #
# 6. Estado para rollback                                              #
# ------------------------------------------------------------------ #
if (-not $DryRun) {
    $state = [ordered]@{
        timestamp        = (Get-Date).ToString('o')
        confPath         = $confPath
        confBackup       = $script:ConfBackup
        changes          = $script:Changed
        gpuPrefsAdded    = $gpuPrefsAdded
        appCompatAdded   = $appCompatAdded
        previousPowerPlan= $previousPlan
        preset           = $Preset
    }
    $state | ConvertTo-Json -Depth 6 | Set-Content -Path $stateFile -Encoding UTF8
    Write-Ok "estado gravado em $stateFile"
}

# ------------------------------------------------------------------ #
# Resumo                                                              #
# ------------------------------------------------------------------ #
Write-Step 'Resumo'
if ($DryRun) {
    Write-Host '   DRY-RUN: nada foi alterado. Rode sem -DryRun para aplicar.' -ForegroundColor Yellow
} elseif ($script:Changed.Count -eq 0 -and $gpuPrefsAdded.Count -eq 0) {
    Write-Info 'nada a mudar: a configuracao ja estava no alvo.'
}
if ($script:Notes.Count -gt 0) {
    Write-Host "   Pendencias: $($script:Notes -join '; ')" -ForegroundColor Yellow
}
Write-Host ''
Write-Host '   Ajustes manuais que dao mais ganho que qualquer script:' -ForegroundColor Cyan
Write-Host '   - Settings > Graphics do BlueStacks: DirectX ou Vulkan (teste os dois no seu driver)'
Write-Host '   - No Free Fire: graficos no minimo/medio, sombras OFF, anti-aliasing OFF, limite de FPS = refresh do monitor'
Write-Host '   - Feche navegador/overlays (Discord, OBS) enquanto joga; 8 GB de RAM + Discord + BlueStacks = swap'
Write-Host '   - Se o PC tem GPU dedicada: painel NVIDIA/AMD > o BlueStacks deve usar a GPU dedicada (confira em Settings > Graphics)'
Write-Host ''
Write-Host '   Desfazer tudo: .\Optimize-BlueStacks.ps1 -Rollback' -ForegroundColor Gray
