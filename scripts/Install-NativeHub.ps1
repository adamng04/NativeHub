param(
    [string]$PackagePath = (Join-Path $PSScriptRoot 'NativeHub.msix'),
    [string]$CertificatePath = (Join-Path $PSScriptRoot 'NativeHub.cer')
)

$ErrorActionPreference = 'Stop'
$package = [IO.Path]::GetFullPath($PackagePath)
$certificate = [IO.Path]::GetFullPath($CertificatePath)
if (-not (Test-Path -LiteralPath $package) -or -not (Test-Path -LiteralPath $certificate)) {
    throw 'NativeHub.msix or NativeHub.cer was not found beside this script.'
}

Write-Warning 'This trusts a local development certificate for the current Windows user, then installs NativeHub.'
$confirmation = Read-Host 'Type INSTALL to continue'
if ($confirmation -cne 'INSTALL') { Write-Host 'Installation cancelled.'; exit 1 }

Import-Certificate -FilePath $certificate -CertStoreLocation 'Cert:\CurrentUser\TrustedPeople' | Out-Null
Add-AppxPackage -Path $package
Write-Host 'NativeHub installed. Launch it from Start.'
