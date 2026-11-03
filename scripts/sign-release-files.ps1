#requires -Version 7.0
[CmdletBinding()]
param([Parameter(Mandatory)][string[]]$Path)
$ErrorActionPreference = 'Stop'
$thumbprint = $env:APPSWITCHER_SIGNING_CERTIFICATE_THUMBPRINT
if ([string]::IsNullOrWhiteSpace($thumbprint)) {
    Write-Output 'Publisher signing not configured; candidate files remain unsigned.'
    return
}
if ($thumbprint -notmatch '^[A-Fa-f0-9]{40}$') { throw 'Invalid signing certificate selector.' }
$certificate = Get-Item -LiteralPath "Cert:\CurrentUser\My\$thumbprint"
if (-not $certificate.HasPrivateKey -or $certificate.NotAfter -lt (Get-Date) -or $certificate.NotBefore -gt (Get-Date) -or '1.3.6.1.5.5.7.3.3' -notin $certificate.EnhancedKeyUsageList.ObjectId) {
    throw 'A current code-signing certificate with a private key is required.'
}
$timestamp = $env:APPSWITCHER_SIGNING_TIMESTAMP_SERVER
if ($timestamp -notmatch '^https?://[^\s]+$') { throw 'Configure a publisher-approved timestamp server.' }
foreach ($file in $Path) {
    $resolved = (Resolve-Path -LiteralPath $file).Path
    $signature = Set-AuthenticodeSignature -LiteralPath $resolved -Certificate $certificate -HashAlgorithm SHA256 -TimestampServer $timestamp
    if ($signature.Status -ne 'Valid' -or $null -eq $signature.TimeStamperCertificate) { throw "Publisher signing/timestamp verification failed: $file" }
}
