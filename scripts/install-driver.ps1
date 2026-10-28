<#
.SYNOPSIS
    Installs or removes the PrecisionCurve mouse filter driver.

.DESCRIPTION
    Two install strategies are supported:

      (default)  Attach the filter to the HID mouse devices that are present
                 right now, by adding "PrecisionCurve" to their UpperFilters
                 registry value. No INF needed, works for every mouse you plug
                 in *at the time you run it*.

      -UseInf    Deploy through pnputil with driver\PrecisionCurve.inf, which
                 is the correct way for a signed, redistributable package.
                 You must edit the hardware IDs in the INF first.

    Neither path can bypass Driver Signature Enforcement: on a normal Windows
    10/11 machine the .sys must be signed by a certificate the kernel trusts,
    or test signing must be enabled (see sign-driver.ps1).

.EXAMPLE
    .\install-driver.ps1                       # attach to all current mice
    .\install-driver.ps1 -Uninstall
    .\install-driver.ps1 -UseInf -InfPath .\driver\PrecisionCurve.inf
#>

[CmdletBinding()]
param(
    [switch]$Uninstall,
    [switch]$UseInf,
    [string]$SysPath = (Join-Path $PSScriptRoot '..\bin\x64\Release\PrecisionCurve\PrecisionCurve.sys'),
    [string]$InfPath = (Join-Path $PSScriptRoot '..\driver\PrecisionCurve.inf'),
    [switch]$SkipRebootPrompt
)

$ErrorActionPreference = 'Stop'
$ServiceName = 'PrecisionCurve'
$FilterName  = 'PrecisionCurve'

function Assert-Administrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw 'This script must run from an elevated PowerShell session.'
    }
}

function Get-MouseInstances {
    <# Every present mouse device, as (InstanceId, FriendlyName). #>
    Get-PnpDevice -Class Mouse -PresentOnly -ErrorAction SilentlyContinue |
        Where-Object { $_.InstanceId -like 'HID\*' } |
        Select-Object InstanceId, FriendlyName
}

function Get-UpperFilters([string]$InstanceId) {
    $key = "HKLM:\SYSTEM\CurrentControlSet\Enum\$InstanceId"
    $parameters = Join-Path $key 'Device Parameters'

    if (-not (Test-Path $parameters)) { return @() }

    $value = (Get-ItemProperty -Path $parameters -Name 'UpperFilters' -ErrorAction SilentlyContinue).UpperFilters
    if ($null -eq $value) { return @() }
    return @($value)
}

function Set-UpperFilters([string]$InstanceId, [string[]]$Filters) {
    $key = "HKLM:\SYSTEM\CurrentControlSet\Enum\$InstanceId\Device Parameters"
    if (-not (Test-Path $key)) { return }

    if ($Filters.Count -eq 0) {
        Remove-ItemProperty -Path $key -Name 'UpperFilters' -ErrorAction SilentlyContinue
    } else {
        New-ItemProperty -Path $key -Name 'UpperFilters' -PropertyType MultiString `
            -Value $Filters -Force | Out-Null
    }
}

function Test-SignaturePolicy {
    $boot = bcdedit /enum '{current}' 2>$null | Out-String
    if ($boot -match 'testsigning\s+Yes') {
        Write-Host '  test signing     : ENABLED (development mode)' -ForegroundColor Yellow
    } else {
        Write-Host '  test signing     : disabled' -ForegroundColor DarkGray
    }

    $secureBoot = $false
    try { $secureBoot = Confirm-SecureBootUEFI } catch { }
    Write-Host ("  secure boot      : {0}" -f ($(if ($secureBoot) { 'on' } else { 'off/unknown') })) `
        -ForegroundColor DarkGray

    $signature = Get-AuthenticodeSignature -FilePath $SysPath -ErrorAction SilentlyContinue
    if ($signature) {
        Write-Host ("  binary signature : {0} ({1})" -f $signature.Status, $signature.SignerCertificate.Subject) `
            -ForegroundColor DarkGray
        if ($signature.Status -ne 'Valid') {
            Write-Warning 'The driver binary is not validly signed. Windows will refuse to load it unless test signing is on.'
        }
    }
}

# ---------------------------------------------------------------------------
# Uninstall
# ---------------------------------------------------------------------------
function Uninstall-Driver {
    Write-Host 'Removing PrecisionCurve upper filter from mouse devices...' -ForegroundColor Cyan

    foreach ($mouse in Get-MouseInstances) {
        $filters = Get-UpperFilters $mouse.InstanceId
        if ($filters -contains $FilterName) {
            $remaining = @($filters | Where-Object { $_ -ne $FilterName })
            Set-UpperFilters $mouse.InstanceId $remaining
            Write-Host ("  {0}: filter removed" -f $mouse.InstanceId)
        }
    }

    $service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
    if ($service) {
        Write-Host 'Stopping and deleting the service...' -ForegroundColor Cyan
        & sc.exe stop $ServiceName | Out-Null
        Start-Sleep -Seconds 1
        & sc.exe delete $ServiceName | Out-Null
    }

    $target = Join-Path $env:SystemRoot "System32\drivers\$ServiceName.sys"
    if (Test-Path $target) {
        Remove-Item $target -Force
        Write-Host "  deleted $target"
    }

    Write-Host ''
    Write-Host 'Uninstalled. Reboot before believing it: a filter driver stays loaded while any mouse is using it.' `
        -ForegroundColor Yellow
}

# ---------------------------------------------------------------------------
# Install
# ---------------------------------------------------------------------------
function Install-Driver {
    Write-Host 'PrecisionCurve installer' -ForegroundColor Cyan

    if ($UseInf) {
        if (-not (Test-Path $InfPath)) { throw "INF not found: $InfPath" }

        Write-Host 'Deploying package with pnputil...' -ForegroundColor Cyan
        & pnputil.exe /add-driver $InfPath /install
        if ($LASTEXITCODE -ne 0) {
            throw "pnputil failed with exit code $LASTEXITCODE (check the hardware IDs in the INF)."
        }
        Write-Host 'Package deployed.' -ForegroundColor Green
    }
    else {
        if (-not (Test-Path $SysPath)) {
            throw "Driver binary not found: $SysPath`nBuild it first (msbuild driver\PrecisionCurve.vcxproj) or pass -SysPath."
        }

        Test-SignaturePolicy

        $target = Join-Path $env:SystemRoot "System32\drivers\$ServiceName.sys"
        Copy-Item $SysPath $target -Force
        Write-Host "  copied to $target"

        $existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
        if (-not $existing) {
            & sc.exe create $ServiceName type= kernel start= demand error= normal `
                binPath= $target DisplayName= 'PrecisionCurve mouse input filter' | Out-Null
            if ($LASTEXITCODE -ne 0) { throw 'sc.exe create failed.' }
            Write-Host '  service created (demand start)'
        } else {
            Write-Host '  service already exists, reusing it'
        }

        foreach ($mouse in Get-MouseInstances) {
            $filters = Get-UpperFilters $mouse.InstanceId
            if ($filters -notcontains $FilterName) {
                $updated = @($filters + $FilterName | Where-Object { $_ })
                Set-UpperFilters $mouse.InstanceId $updated
                Write-Host ("  {0}: filter attached{1}" -f $mouse.InstanceId,
                    $(if ($mouse.FriendlyName) { " ($($mouse.FriendlyName))" } else { '' }))
            } else {
                Write-Host ("  {0}: already attached" -f $mouse.InstanceId)
            }
        }
    }

    Write-Host ''
    Write-Host 'Done. Reboot (or unplug/replug the mouse) so the filter loads.' -ForegroundColor Green
    Write-Host 'Verify with:  pcctl info   or   Get-Service PrecisionCurve' -ForegroundColor DarkGray

    if (-not $SkipRebootPrompt) {
        $answer = Read-Host 'Reboot now? [y/N]'
        if ($answer -eq 'y') { Restart-Computer }
    }
}

# ---------------------------------------------------------------------------
try {
    Assert-Administrator

    if (-not (Test-Path $SysPath)) { $SysPath = $SysPath }  # keep param visible in errors

    if ($Uninstall) { Uninstall-Driver } else { Install-Driver }
}
catch {
    Write-Error $_
    exit 1
}
