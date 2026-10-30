<#
.SYNOPSIS
    Compila bsboost.dll e bsboostctl.exe (x64) sem depender do CMake.

.DESCRIPTION
    Precisa do Visual Studio 2019/2022 com "Desktop development with C++".
    Rode a partir de um "x64 Native Tools Command Prompt for VS" OU deixe o
    script localizar o cl.exe pelo vswhere.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\build.ps1
#>
[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [switch]$UseCMake
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$outDir = Join-Path $root 'bin'
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

function Find-VsWhere {
    $candidates = @(
        "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe",
        "$env:ProgramFiles\Microsoft Visual Studio\Installer\vswhere.exe"
    )
    foreach ($c in $candidates) { if (Test-Path $c) { return $c } }
    return $null
}

function Import-MsvcEnvironment {
    if (Get-Command cl.exe -ErrorAction SilentlyContinue) { return }

    $vswhere = Find-VsWhere
    if (-not $vswhere) {
        throw "cl.exe nao esta no PATH e o vswhere nao foi encontrado. Abra o 'x64 Native Tools Command Prompt for VS' e rode de novo."
    }
    $vsPath = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
    if (-not $vsPath) { throw "Nenhuma instalacao do Visual Studio com C++ x64 foi encontrada." }

    $vcvars = Join-Path $vsPath 'VC\Auxiliary\Build\vcvars64.bat'
    if (-not (Test-Path $vcvars)) { throw "vcvars64.bat nao encontrado em $vcvars" }

    # Importa o ambiente do vcvars para este processo do PowerShell.
    $lines = & cmd.exe /c "`"$vcvars`" >nul 2>&1 && set"
    foreach ($line in $lines) {
        if ($line -match '^([^=]+)=(.*)$') {
            $name = $Matches[1]
            if ($name -match '^(Path|INCLUDE|LIB|LIBPATH|VCINSTALLDIR|VCToolsInstallDir|WindowsSdkDir|UCRTVersion|WindowsSDKVersion)$') {
                [System.Environment]::SetEnvironmentVariable($name, $Matches[2], 'Process')
            }
        }
    }
    if (-not (Get-Command cl.exe -ErrorAction SilentlyContinue)) {
        throw "Falha ao carregar o ambiente do MSVC."
    }
}

Import-MsvcEnvironment
Push-Location $root
try {
    if ($UseCMake -and (Get-Command cmake -ErrorAction SilentlyContinue)) {
        $buildDir = Join-Path $root "build\$Configuration"
        cmake -S $root -B $buildDir -G "Visual Studio 17 2022" -A x64
        if ($LASTEXITCODE -ne 0) { throw "cmake configure falhou" }
        cmake --build $buildDir --config $Configuration
        if ($LASTEXITCODE -ne 0) { throw "cmake build falhou" }
        Get-ChildItem -Path $buildDir -Recurse -Include bsboost.dll, bsboostctl.exe |
            Copy-Item -Destination $outDir -Force
    }
    else {
        $optFlags = if ($Configuration -eq 'Debug') { '/Od', '/Zi' } else { '/O2', '/GL' }
        Write-Host "== bsboost.dll ==" -ForegroundColor Cyan
        & cl.exe /nologo /LD /W4 /utf-8 /D_CRT_SECURE_NO_WARNINGS /DBSBOOST_EXPORTS `
            $optFlags /I. /Fe:"$outDir\bsboost.dll" /Fo:"$outDir\\" `
            bsboost.c /link /IMPLIB:"$outDir\bsboost.lib" advapi32.lib
        if ($LASTEXITCODE -ne 0) { throw "falha ao compilar a DLL" }

        Write-Host "== bsboostctl.exe ==" -ForegroundColor Cyan
        & cl.exe /nologo /W4 /utf-8 /D_CRT_SECURE_NO_WARNINGS $optFlags /I. `
            /Fe:"$outDir\bsboostctl.exe" /Fo:"$outDir\\" `
            host\bsboostctl.c /link "$outDir\bsboost.lib" advapi32.lib
        if ($LASTEXITCODE -ne 0) { throw "falha ao compilar o host" }
    }
}
finally {
    Pop-Location
}

Write-Host ""
Write-Host "Artefatos em $outDir :" -ForegroundColor Green
Get-ChildItem $outDir | Select-Object Name, Length | Format-Table -AutoSize
