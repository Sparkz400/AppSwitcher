#requires -Version 7.0
$ErrorActionPreference = 'Stop'
$originalThumbprint = $env:APPSWITCHER_SIGNING_CERTIFICATE_THUMBPRINT
$originalTimestamp = $env:APPSWITCHER_SIGNING_TIMESTAMP_SERVER
$fixture = Join-Path ([IO.Path]::GetTempPath()) ('AppSwitcher-sign-' + [Guid]::NewGuid().ToString('N') + '.txt')
$signTestState = @{Calls=0; Scenario='valid'}
# Local fakes: these tests never read a real private key, sign a binary or contact a timestamp service.
function Get-Item {
    param([string]$LiteralPath)
    if ($LiteralPath -notlike 'Cert:\CurrentUser\My\*') { throw 'Unexpected certificate lookup.' }
    [pscustomobject]@{
        HasPrivateKey = $signTestState.Scenario -ne 'no-key'
        NotAfter = (Get-Date).AddDays($(if ($signTestState.Scenario -eq 'expired') { -1 } else { 30 }))
        NotBefore = (Get-Date).AddDays(-30)
        EnhancedKeyUsageList = @([pscustomobject]@{ObjectId=$(if ($signTestState.Scenario -eq 'wrong-usage') { '1.2.3' } else { '1.3.6.1.5.5.7.3.3' })})
    }
}
function Set-AuthenticodeSignature {
    param($LiteralPath, $Certificate, $HashAlgorithm, $TimestampServer)
    if ($LiteralPath -ne $fixture -or $HashAlgorithm -ne 'SHA256') { throw 'Unexpected signing request.' }
    $signTestState.Calls++
    [pscustomobject]@{ Status=$(if ($signTestState.Scenario -eq 'invalid-signature') { 'NotTrusted' } else { 'Valid' }); TimeStamperCertificate=$(if ($signTestState.Scenario -eq 'no-timestamp') { $null } else { 'synthetic' }) }
}
try {
    'Synthetic fixture' | Set-Content -LiteralPath $fixture
    $env:APPSWITCHER_SIGNING_CERTIFICATE_THUMBPRINT = ''
    & "$PSScriptRoot/sign-release-files.ps1" -Path $fixture
    if ($signTestState.Calls -ne 0) { throw 'Unsigned candidate attempted signing.' }
    $env:APPSWITCHER_SIGNING_CERTIFICATE_THUMBPRINT = 'a' * 40
    $env:APPSWITCHER_SIGNING_TIMESTAMP_SERVER = 'http://timestamp.invalid'
    & "$PSScriptRoot/sign-release-files.ps1" -Path $fixture
    if ($signTestState.Calls -ne 1) { throw 'Configured signing was not attempted.' }
    foreach ($scenario in @('no-key', 'expired', 'wrong-usage', 'invalid-signature', 'no-timestamp')) {
        $signTestState.Scenario = $scenario
        $rejected = $false
        try { & "$PSScriptRoot/sign-release-files.ps1" -Path $fixture } catch { $rejected = $true }
        if (-not $rejected) { throw "Signing gate accepted $($signTestState.Scenario)." }
    }
    Write-Output 'Signing adapter tests passed: unsigned candidate, configured signer and five rejected failures (synthetic certificate/signature).'
} finally {
    $env:APPSWITCHER_SIGNING_CERTIFICATE_THUMBPRINT = $originalThumbprint
    $env:APPSWITCHER_SIGNING_TIMESTAMP_SERVER = $originalTimestamp
    Remove-Item -LiteralPath $fixture -ErrorAction SilentlyContinue
}
