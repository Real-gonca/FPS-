<#
.SYNOPSIS
    Builds the driver, the tray application and the service.

.DESCRIPTION
    Requires:
      * Visual Studio 2022 with "Desktop development with C++" and the
        "Windows Driver Development" workload (WDK + Windows SDK),
      * .NET 8 SDK for the managed projects.

.EXAMPLE
    .\build.ps1                 # everything, Release x64
    .\build.ps1 -SkipDriver     # only the managed projects
    .\build.ps1 -Configuration Debug
#>

[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [ValidateSet('x64')]
    [string]$Platform = 'x64',

    [switch]$SkipDriver,
    [switch]$SkipManaged
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

function Get-MSBuild {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'

    if (Test-Path $vswhere) {
        $path = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild `
            -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
        if ($path) { return $path }
    }

    $command = Get-Command msbuild.exe -ErrorAction SilentlyContinue
    if ($command) { return $command.Source }

    throw 'MSBuild not found. Install Visual Studio 2022 (see docs/BUILD_AND_SIGN.md).'
}

Push-Location $root
try {
    if (-not $SkipDriver) {
        $msbuild = Get-MSBuild
        Write-Host "== driver ($Configuration|$Platform) ==" -ForegroundColor Cyan

        & $msbuild 'driver\PrecisionCurve.vcxproj' `
            /p:Configuration=$Configuration `
            /p:Platform=$Platform `
            /p:SolutionDir="$root\" `
            /m /v:minimal

        if ($LASTEXITCODE -ne 0) { throw "Driver build failed ($LASTEXITCODE)." }
    }

    if (-not $SkipManaged) {
        Write-Host '== tray application ==' -ForegroundColor Cyan
        & dotnet build 'app\PrecisionCurve.App.csproj' -c $Configuration
        if ($LASTEXITCODE -ne 0) { throw "Application build failed ($LASTEXITCODE)." }

        Write-Host '== service ==' -ForegroundColor Cyan
        & dotnet build 'service\PrecisionCurve.Service\PrecisionCurve.Service.csproj' -c $Configuration
        if ($LASTEXITCODE -ne 0) { throw "Service build failed ($LASTEXITCODE)." }
    }

    Write-Host ''
    Write-Host 'Build complete.' -ForegroundColor Green
    Write-Host "  driver : bin\$Platform\$Configuration\PrecisionCurve\PrecisionCurve.sys" -ForegroundColor DarkGray
    Write-Host "  app    : app\bin\$Configuration\net8.0-windows\PrecisionCurve.exe" -ForegroundColor DarkGray
    Write-Host ''
    Write-Host 'Next: .\scripts\sign-driver.ps1 -TestSign   then   .\scripts\install-driver.ps1' -ForegroundColor DarkGray
}
finally {
    Pop-Location
}
