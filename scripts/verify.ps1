#Requires -Version 5.1
<#
  Nexus Optimizer — validação de uma mão (Windows).
  Corre: build completo (Release) + testes unitários + resumo.
  Uso:  powershell -ExecutionPolicy Bypass -File scripts\verify.ps1
#>

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

Write-Host "═══════════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host " NEXUS OPTIMIZER — VERIFICAÇÃO" -ForegroundColor Cyan
Write-Host "═══════════════════════════════════════════════════════" -ForegroundColor Cyan

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Host "ERRO: .NET SDK não encontrado. Instale o .NET 8 SDK:" -ForegroundColor Red
    Write-Host "  https://dotnet.microsoft.com/download/dotnet/8.0" -ForegroundColor Yellow
    exit 1
}

Write-Host "`n[1/2] dotnet build (Release, todos os projetos)..." -ForegroundColor White
dotnet build "$root\NexusOptimizer.sln" -c Release --nologo
if ($LASTEXITCODE -ne 0) {
    Write-Host "`nFALHA no build." -ForegroundColor Red
    exit 1
}
Write-Host "Build OK." -ForegroundColor Green

Write-Host "`n[2/2] dotnet test (Nexus.Tests)..." -ForegroundColor White
dotnet test "$root\tests\Nexus.Tests" -c Release --nologo -v minimal
if ($LASTEXITCODE -ne 0) {
    Write-Host "`nFALHA nos testes." -ForegroundColor Red
    exit 1
}

Write-Host "`n═══════════════════════════════════════════════════════" -ForegroundColor Green
Write-Host " TUDO OK — build limpo e testes a passar." -ForegroundColor Green
Write-Host " Correr a app:  dotnet run --project src\Nexus.Presentation" -ForegroundColor Cyan
Write-Host "═══════════════════════════════════════════════════════" -ForegroundColor Green
