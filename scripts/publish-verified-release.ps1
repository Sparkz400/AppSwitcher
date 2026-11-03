#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidatePattern('^v\d+\.\d+\.\d+(-(beta|rc)\.\d+)?$')][string]$Tag,
    [Parameter(Mandatory)][string]$Repository,
    [Parameter(Mandatory)][string]$Path,
    [Parameter(Mandatory)][string]$NotesPath,
    [switch]$Resume,
    [ValidateRange(1, 24)][int]$VerificationAttempts = 24,
    [ValidateRange(0, 5)][int]$RetrySeconds = 5
)
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true
$assets = @(Get-ChildItem -LiteralPath $Path -File | ForEach-Object FullName)
if ($assets.Count -ne 8) { throw 'Expected the eight previously verified release assets.' }
$prerelease = $Tag -match '-(beta|rc)\.'
$preFlag = $prerelease ? '--prerelease' : '--prerelease=false'
$latestFlag = $prerelease ? '--latest=false' : '--latest'
if (-not $Resume) {
    gh release create $Tag @assets --repo $Repository --draft --verify-tag $preFlag --latest=false --title "AppSwitcher $Tag" --notes-file $NotesPath
}
# CLI lookup also finds drafts. Recovery never uploads or replaces an asset.
$releaseId = gh release view $Tag --repo $Repository --json databaseId --jq '.databaseId'
$release = gh api "repos/$Repository/releases/$releaseId" | ConvertFrom-Json
if ($release.prerelease -ne $prerelease -or $release.tag_name -cne $Tag -or $release.assets.Count -ne $assets.Count) { throw 'Release asset inventory mismatch.' }
foreach ($file in $assets) {
    $asset = @($release.assets | Where-Object name -CEQ (Split-Path $file -Leaf))
    $digest = 'sha256:' + (Get-FileHash -LiteralPath $file).Hash.ToLowerInvariant()
    if ($asset.Count -ne 1 -or $asset[0].digest -cne $digest) { throw "Uploaded asset mismatch: $file" }
}
if ($release.draft) {
    gh release edit $Tag --repo $Repository --draft=false $latestFlag --notes-file $NotesPath
    $release = gh api "repos/$Repository/releases/$releaseId" | ConvertFrom-Json
} elseif (-not $Resume) {
    throw 'An existing publication requires verification-only recovery.'
}
if ($release.draft -or -not $release.immutable) { throw 'Published release is not immutable.' }

# GitHub generates the release attestation asynchronously after publication.
# Retry only absence; signature/integrity failures still stop immediately.
for ($attempt = 1; $attempt -le $VerificationAttempts; $attempt++) {
    $PSNativeCommandUseErrorActionPreference = $false
    try {
        $verification = gh release verify $Tag --repo $Repository 2>&1
        $verificationExitCode = $LASTEXITCODE
    } finally { $PSNativeCommandUseErrorActionPreference = $true }
    $message = ($verification | Out-String).Trim()
    if ($verificationExitCode -eq 0) { Write-Output $message; return }
    if ($message -notmatch 'no attestations (for tag|found for release)' -or $attempt -eq $VerificationAttempts) {
        throw "Release attestation verification failed: $message"
    }
    Write-Output "Release attestation not available yet; retry $attempt/$VerificationAttempts in $RetrySeconds seconds."
    Start-Sleep -Seconds $RetrySeconds
}
