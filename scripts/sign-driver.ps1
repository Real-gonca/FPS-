<#
.SYNOPSIS
    Signs PrecisionCurve.sys for development (test signing) or for release.

.DESCRIPTION
    Windows 10/11 x64 loads a kernel driver only if its signature chains to a
    certificate the kernel trusts:

      Development   A self-signed certificate added to the local machine's
                    Trusted Root + Trusted Publishers stores, plus
                    `bcdedit /set testsigning on`. The machine then boots with
                    a visible "Test Mode" watermark and Secure Boot must be off.

      Release       An EV code-signing certificate (hardware token / HSM /
                    Azure Trusted Signing) *and*, for broad distribution,
                    Microsoft's attestation or WHQL signature obtained through
                    the Partner Center. Since Windows 10 1607 the kernel will
                    not load a driver that has only a third-party signature --
                    the Microsoft portal countersignature is what makes it
                    loadable on customer machines.

    There is no supported way around this. See docs/BUILD_AND_SIGN.md.

.EXAMPLE
    .\sign-driver.ps1 -TestSign
    .\sign-driver.ps1 -Pfx .\codesign.pfx -Password (Read-Host -AsSecureString) -TimestampUrl http://timestamp.digicert.com
#>

[CmdletBinding(DefaultParameterSetName = 'Test')]
param(
    [Parameter(ParameterSetName = 'Test')]
    [switch]$TestSign,

    [Parameter(ParameterSetName = 'Release', Mandatory = $true)]
    [string]$Pfx,

    [Parameter(ParameterSetName = 'Release')]
    [SecureString]$Password,

    [string]$SysPath = (Join-Path $PSScriptRoot '..\bin\x64\Release\PrecisionCurve\PrecisionCurve.sys'),

    [string]$TimestampUrl = 'http://timestamp.digicert.com',

    [switch]$SkipBcdEdit
)

$ErrorActionPreference = 'Stop'
$CertSubject = 'CN=PrecisionCurve Development'

function Assert-Administrator {
    $principal = New-Object Security.Principal.WindowsPrincipal(
        [Security.Principal.WindowsIdentity]::GetCurrent())
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw 'Run this script from an elevated PowerShell session.'
    }
}

function Find-SignTool {
    $candidates = @(
        (Get-Command signtool.exe -ErrorAction SilentlyContinue | Select-Object -First 1).Source
    ) | Where-Object { $_ }

    if ($candidates.Count -gt 0) { return $candidates[0] }

    # Fall back to the newest Windows Kit on disk.
    $kits = Get-ChildItem 'C:\Program Files (x86)\Windows Kits\10\bin' -Directory -ErrorAction SilentlyContinue |
        Sort-Object Name -Descending

    foreach ($kit in $kits) {
        $candidate = Join-Path $kit.FullName 'x64\signtool.exe'
        if (Test-Path $candidate) { return $candidate }
    }

    throw 'signtool.exe not found. Install the Windows SDK (Windows SDK Signing Tools).'
}

function New-DevCertificate {
    $existing = Get-ChildItem Cert:\LocalMachine\My |
        Where-Object { $_.Subject -eq $CertSubject } |
        Select-Object -First 1

    if ($existing) {
        Write-Host "Reusing development certificate: $($existing.Thumbprint)"
        return $existing
    }

    Write-Host 'Creating a self-signed code-signing certificate...'
    $cert = New-SelfSignedCertificate `
        -Type CodeSigningCert `
        -Subject $CertSubject `
        -CertStoreLocation 'Cert:\LocalMachine\My' `
        -KeyUsage DigitalSignature `
        -KeyLength 3072 `
        -HashAlgorithm SHA256 `
        -NotAfter (Get-Date).AddYears(3)

    # Kernel-mode code signing also needs the EKU 1.3.6.1.5.5.7.3.3 and the
    # "kernel mode" usage; New-SelfSignedCertificate sets the first, and the
    # second is implied by CodeSigningCert with -KeyUsage DigitalSignature for
    # test signing purposes.

    $exported = Join-Path $env:TEMP 'PrecisionCurveDev.cer'
    Export-Certificate -Cert $cert -FilePath $exported | Out-Null

    foreach ($store in 'Root', 'TrustedPublisher') {
        Import-Certificate -FilePath $exported -CertStoreLocation "Cert:\LocalMachine\$store" | Out-Null
        Write-Host "  imported into LocalMachine\$store"
    }

    Remove-Item $exported -Force -ErrorAction SilentlyContinue
    return $cert
}

function Invoke-Sign([string]$SignTool, [string]$Thumbprint, [string]$File) {
    Write-Host "Signing $(Split-Path $File -Leaf)..."
    & $SignTool sign /fd sha256 /sha1 $Thumbprint /tr $TimestampUrl /td sha256 $File
    if ($LASTEXITCODE -ne 0) { throw "signtool failed with exit code $LASTEXITCODE" }

    Write-Host 'Adding a page hash catalogue signature is only needed for WHQL submission; skipping.'
    Get-AuthenticodeSignature -FilePath $File |
        Format-List Status, StatusMessage, SignerCertificate
}

Assert-Administrator

if (-not (Test-Path $SysPath)) {
    throw "Driver binary not found: $SysPath. Build it first."
}

$signTool = Find-SignTool
Write-Host "Using signtool: $signTool"

if ($TestSign -or $PSCmdlet.ParameterSetName -eq 'Test') {
    $cert = New-DevCertificate
    Invoke-Sign $signTool $cert.Thumbprint $SysPath

    if (-not $SkipBcdEdit) {
        Write-Host 'Enabling test signing (requires a reboot)...'
        & bcdedit /set testsigning on | Out-Null
        Write-Warning 'Secure Boot must be OFF for test signing to work. The desktop will show "Test Mode".'
    }

    Write-Host ''
    Write-Host 'Development signature applied. Reboot, then run install-driver.ps1.' -ForegroundColor Green
    Write-Host 'Remember: a test-signed driver only loads on machines with test signing enabled.' -ForegroundColor Yellow
}
else {
    if (-not (Test-Path $Pfx)) { throw "Certificate not found: $Pfx" }

    $thumbprint = $null
    if ($Password) {
        $secure = $Password
    } else {
        $secure = Read-Host -AsSecureString 'Certificate password'
    }

    $plain = [Runtime.InteropServices.Marshal]::PtrToStringAuto(
        [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure))

    try {
        $imported = Import-PfxCertificate -FilePath $Pfx -CertStoreLocation 'Cert:\LocalMachine\My' `
            -Password (ConvertTo-SecureString $plain -AsPlainText -Force)
        $thumbprint = $imported.Thumbprint
    }
    finally {
        if ($plain) { $plain = $null }
    }

    Invoke-Sign $signTool $thumbprint $SysPath

    Write-Host ''
    Write-Host 'Signed with your release certificate.' -ForegroundColor Green
    Write-Host 'Next step for distribution: submit the .sys to the Microsoft Partner Center' -ForegroundColor Yellow
    Write-Host 'Hardware Developer portal for attestation (or WHQL) signing. Without that' -ForegroundColor Yellow
    Write-Host 'countersignature Windows 10/11 will not load the driver on customer machines.' -ForegroundColor Yellow
}
