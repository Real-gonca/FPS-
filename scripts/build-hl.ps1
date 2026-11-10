# Run on a Windows 10/11 development machine with the .NET 8 SDK.
# This does not install or build the legacy PrecisionCurve kernel driver.
[CmdletBinding()]
param([switch]$Publish)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if ($env:OS -ne 'Windows_NT') { throw 'WPF build/publish must be validated on Windows.' }
$root = Split-Path $PSScriptRoot -Parent
Push-Location $root
try {
    dotnet run --project HL.Optimizer.Pro/Tests/HL.Optimizer.Tests.csproj -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Core tests failed.' }
    dotnet build HL.Optimizer.Pro/HL.Optimizer.Pro.csproj -c Release
    if ($LASTEXITCODE -ne 0) { throw 'WPF compilation failed.' }
    if ($Publish) {
        dotnet publish HL.Optimizer.Pro/HL.Optimizer.Pro.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o artifacts/hl-win-x64
        if ($LASTEXITCODE -ne 0) { throw 'Release publication failed.' }
        if (!(Test-Path artifacts/hl-win-x64/HL.Optimizer.Pro.exe)) { throw 'Executable missing.' }
        Write-Host 'Unsigned development Release: artifacts/hl-win-x64/HL.Optimizer.Pro.exe'
        Write-Warning 'Not a production certification. Complete the VM checklist in HL.Optimizer.Pro/README.md before distribution.'
    }
} finally { Pop-Location }
