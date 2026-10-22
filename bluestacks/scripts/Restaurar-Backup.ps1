<#
.SINOPSE
    Restaura o backup mais recente feito pelo Aplicar-Otimizacoes.ps1.

.EXEMPLO
    PowerShell -ExecutionPolicy Bypass -File .\Restaurar-Backup.ps1

.EXEMPLO 2
    # Escolher um backup específico
    PowerShell -ExecutionPolicy Bypass -File .\Restaurar-Backup.ps1 -Backup "C:\ProgramData\BlueStacks_nxt\_backup_otimizacao_20261022-153000"
#>
[CmdletBinding()]
param(
    [string]$BlueStacksDir = "$env:ProgramData\BlueStacks_nxt",
    [string]$Backup
)

$ErrorActionPreference = 'Stop'

$admin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
    [Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $admin) {
    throw "Execute este script como ADMINISTRADOR."
}

if (-not $Backup) {
    $Backup = Get-ChildItem -Path $BlueStacksDir -Directory -Filter '_backup_otimizacao_*' -ErrorAction SilentlyContinue |
              Sort-Object Name -Descending |
              Select-Object -First 1 -ExpandProperty FullName
}

if (-not $Backup -or -not (Test-Path $Backup)) {
    throw "Nenhum backup encontrado em $BlueStacksDir. Nada a restaurar."
}

Write-Host "Restaurando de: $Backup" -ForegroundColor Cyan

$nomes = @('HD-Player', 'BlueStacks', 'HD-MultiInstanceManager', 'BlueStacksServices',
           'BstkSVC', 'BstkVMMgr', 'BlueStacksGP', 'HD-Adb', 'BstkAudio', 'BstkCamera')
foreach ($nome in $nomes) {
    Get-Process -Name $nome -ErrorAction SilentlyContinue | ForEach-Object {
        try { Stop-Process -Id $_.Id -Force -ErrorAction Stop } catch { }
    }
}
Start-Sleep -Seconds 2

$confBackup = Join-Path $Backup 'bluestacks.conf'
$vboxBackup = Join-Path $Backup 'Pie64.vbox'

if (Test-Path $confBackup) {
    Copy-Item $confBackup (Join-Path $BlueStacksDir 'bluestacks.conf') -Force
    Write-Host "  bluestacks.conf restaurado." -ForegroundColor Green
}

if (Test-Path $vboxBackup) {
    $vboxDestino = Join-Path $BlueStacksDir 'Engine\Pie64\Pie64.vbox'
    if (Test-Path (Split-Path -Parent $vboxDestino)) {
        Copy-Item $vboxBackup $vboxDestino -Force
        Write-Host "  Pie64.vbox restaurado." -ForegroundColor Green
    }
}

Write-Host ""
Write-Host "Pronto. Abra o BlueStacks novamente." -ForegroundColor Cyan
Write-Host "Lembrete: para desfazer o perfil da NVIDIA, abra o NVIDIA Profile Inspector," -ForegroundColor Gray
Write-Host "selecione o perfil 'BlueStacks 5 - Free Fire 90 FPS' e clique em 'Remove profile'." -ForegroundColor Gray
