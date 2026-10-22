<#
.SINOPSE
    Aplica as otimizações de desempenho no BlueStacks 5 (Free Fire 90 FPS).

.DESCRIÇÃO
    1. Fecha o BlueStacks (senão ele regrava o bluestacks.conf e desfaz tudo).
    2. Faz backup do bluestacks.conf e, opcionalmente, do Pie64.vbox.
    3. Copia o bluestacks.conf otimizado para C:\ProgramData\BlueStacks_nxt.
    4. Opcionalmente:
         -IncluirVBox       copia o Pie64.vbox com VRAM maior
         -ExcluirDefender   adiciona as pastas do BlueStacks na exclusão do Windows Defender

.EXEMPLO
    # Abra o PowerShell como Administrador, vá até a pasta bluestacks\scripts e rode:
    PowerShell -ExecutionPolicy Bypass -File .\Aplicar-Otimizacoes.ps1

.EXEMPLO 2
    PowerShell -ExecutionPolicy Bypass -File .\Aplicar-Otimizacoes.ps1 -IncluirVBox -ExcluirDefender

.OBSERVAÇÃO
    Para reverter tudo: .\Restaurar-Backup.ps1
#>
[CmdletBinding()]
param(
    [string]$BlueStacksDir = "$env:ProgramData\BlueStacks_nxt",
    [string]$PastaOrigem,
    [switch]$IncluirVBox,
    [switch]$ExcluirDefender
)

$ErrorActionPreference = 'Stop'

function Write-Titulo($texto) {
    Write-Host ""
    Write-Host "==============================================================" -ForegroundColor Cyan
    Write-Host " $texto" -ForegroundColor Cyan
    Write-Host "==============================================================" -ForegroundColor Cyan
}

# ---------- 0. Pasta de origem dos arquivos ----------
if (-not $PastaOrigem) {
    if ($PSScriptRoot) {
        $PastaOrigem = Split-Path -Parent $PSScriptRoot
    } else {
        $PastaOrigem = (Get-Location).Path
    }
}

$origemConf = Join-Path $PastaOrigem 'bluestacks.conf'
$origemVbox = Join-Path $PastaOrigem 'Pie64.vbox'
$confDestino = Join-Path $BlueStacksDir 'bluestacks.conf'
$vboxDestino = Join-Path $BlueStacksDir 'Engine\Pie64\Pie64.vbox'

Write-Titulo "OTIMIZACAO BLUESTACKS 5 - FREE FIRE 90 FPS"

if (-not (Test-Path $origemConf)) {
    throw "Arquivo nao encontrado: $origemConf`nColoque este script dentro da pasta 'bluestacks\scripts' do repositorio."
}
if (-not (Test-Path $BlueStacksDir)) {
    throw "Pasta do BlueStacks nao encontrada: $BlueStacksDir"
}
if (-not (Test-Path $confDestino)) {
    throw "bluestacks.conf nao encontrado em: $confDestino"
}

# ---------- 1. Precisa ser Administrador ----------
$admin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
    [Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $admin) {
    throw "Execute este script como ADMINISTRADOR (botao direito no PowerShell > 'Executar como administrador')."
}

# ---------- 2. Fecha o BlueStacks ----------
Write-Host "`n[1/4] Fechando o BlueStacks..." -ForegroundColor Yellow
$nomes = @('HD-Player', 'BlueStacks', 'HD-MultiInstanceManager', 'BlueStacksServices',
           'BstkSVC', 'BstkVMMgr', 'BlueStacksGP', 'HD-Adb', 'BstkAudio', 'BstkCamera')

foreach ($nome in $nomes) {
    Get-Process -Name $nome -ErrorAction SilentlyContinue | ForEach-Object {
        try {
            Write-Host ("  parando {0} (PID {1})" -f $_.ProcessName, $_.Id)
            Stop-Process -Id $_.Id -Force -ErrorAction Stop
        } catch {
            Write-Host ("  nao consegui parar {0}: {1}" -f $_.ProcessName, $_.Exception.Message) -ForegroundColor DarkYellow
        }
    }
}

# Servicos do BlueStacks (podem reiniciar sozinhos; nao impedem a aplicacao)
foreach ($svc in @('BstkSVC', 'BlueStacksServices', 'BstkVM')) {
    $servico = Get-Service -Name $svc -ErrorAction SilentlyContinue
    if ($servico -and $servico.Status -ne 'Stopped') {
        try {
            Write-Host ("  parando servico {0}" -f $svc)
            Stop-Service -Name $svc -Force -ErrorAction Stop
        } catch {
            Write-Host ("  servico {0} continua ativo: {1}" -f $svc, $_.Exception.Message) -ForegroundColor DarkYellow
        }
    }
}

Start-Sleep -Seconds 2

# So os processos "de tela" realmente regravam o bluestacks.conf ao fechar.
$criticos = @('HD-Player', 'BlueStacks', 'HD-MultiInstanceManager')
$restantes = Get-Process -Name $criticos -ErrorAction SilentlyContinue
if ($restantes) {
    throw "Ainda existe BlueStacks aberto. Feche tudo (inclusive o icone na bandeja) e tente de novo."
}
Write-Host "  OK - o emulador esta fechado." -ForegroundColor Green

# ---------- 3. Backup ----------
Write-Host "`n[2/4] Fazendo backup dos arquivos atuais..." -ForegroundColor Yellow
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$backup = Join-Path $BlueStacksDir "_backup_otimizacao_$stamp"
New-Item -ItemType Directory -Path $backup -Force | Out-Null

Copy-Item $confDestino (Join-Path $backup 'bluestacks.conf') -Force
if (Test-Path $vboxDestino) {
    Copy-Item $vboxDestino (Join-Path $backup 'Pie64.vbox') -Force
}
Write-Host "  Backup salvo em: $backup" -ForegroundColor Green

# ---------- 4. Aplica ----------
Write-Host "`n[3/4] Aplicando as configuracoes..." -ForegroundColor Yellow

Copy-Item $origemConf $confDestino -Force
Write-Host "  bluestacks.conf substituido." -ForegroundColor Green

if ($IncluirVBox) {
    if (Test-Path (Split-Path -Parent $vboxDestino)) {
        Copy-Item $origemVbox $vboxDestino -Force
        Write-Host "  Pie64.vbox substituido (VRAM 128 MB)." -ForegroundColor Green
    } else {
        Write-Host "  AVISO: $vboxDestino nao existe. Pulei o .vbox (rode o BlueStacks uma vez e tente de novo)." -ForegroundColor DarkYellow
    }
} else {
    Write-Host "  Pie64.vbox mantido (use -IncluirVBox se quiser aplicar)." -ForegroundColor DarkGray
}

if ($ExcluirDefender) {
    try {
        Add-MpPreference -ExclusionPath $BlueStacksDir -ErrorAction Stop
        Add-MpPreference -ExclusionPath 'C:\Program Files\BlueStacks_nxt' -ErrorAction Stop
        Write-Host "  Pastas do BlueStacks excluidas da verificacao em tempo real do Defender." -ForegroundColor Green
    } catch {
        Write-Host "  Nao consegui alterar o Defender: $($_.Exception.Message)" -ForegroundColor DarkYellow
    }
}

# ---------- 5. Resumo ----------
Write-Titulo "PRONTO"
Write-Host @"
Aplicado:
  CPU ............... 4 nucleos
  RAM ............... 5120 MB (mantida)
  Resolucao ......... 1280x720 @ 160 dpi (era 320 dpi)
  FPS maximo ........ 90 (High FPS ligado, VSync desligado)
  Sidebar ........... desligada
  Telemetria ........ desligada
  DNS secundario .... 1.0.0.1

Proximo passo (o mais importante): importar o perfil da NVIDIA
  Arquivo: nvidia\BlueStacks-FreeFire-90FPS.nip
  Programa: NVIDIA Profile Inspector

Se algo der errado, reverta com:
  PowerShell -ExecutionPolicy Bypass -File .\Restaurar-Backup.ps1
"@ -ForegroundColor Gray
