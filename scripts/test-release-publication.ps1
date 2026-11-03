#requires -Version 7.0
$ErrorActionPreference = 'Stop'
$fixture = Join-Path ([IO.Path]::GetTempPath()) ('AppSwitcher-publish-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixture | Out-Null
$publicationState = @{}
# Simulate GitHub responses; these tests never create or edit a remote release.
function gh {
    $global:LASTEXITCODE = 0
    $operation = ($args | Select-Object -First 2) -join ' '
    switch ($operation) {
        'release create' { $publicationState.Creates++; return }
        'release view' { return '123' }
        'release edit' { $publicationState.Edits++; $publicationState.Release.draft = $false; return }
        'release verify' {
            $publicationState.Verifies++
            if ($publicationState.Scenario -eq 'invalid-signature') {
                $global:LASTEXITCODE = 1; return 'failed to verify attestations: invalid signature'
            }
            if ($publicationState.Scenario -eq 'never-ready' -or ($publicationState.Scenario -eq 'delayed' -and $publicationState.Verifies -lt 3)) {
                $global:LASTEXITCODE = 1; return 'no attestations for tag v1.2.0-rc.1 (sha1:synthetic)'
            }
            return 'Release verified.'
        }
        default {
            if ($args[0] -eq 'api') { return $publicationState.Release | ConvertTo-Json -Depth 5 }
            throw "Unexpected GitHub operation: $operation"
        }
    }
}
try {
    1..8 | ForEach-Object { "fixture $_" | Set-Content -LiteralPath (Join-Path $fixture "asset$_.txt") }
    $inventory = @(Get-ChildItem -LiteralPath $fixture -File | ForEach-Object { @{name=$_.Name; digest='sha256:' + (Get-FileHash -LiteralPath $_.FullName).Hash.ToLowerInvariant()} })
    foreach ($scenario in @('fresh', 'delayed', 'published', 'draft-resume', 'never-ready', 'invalid-signature', 'wrong-hash', 'missing-asset', 'duplicate-asset', 'wrong-tag', 'wrong-kind', 'mutable')) {
        $publicationState.Clear()
        $publicationState.Scenario = $scenario
        $publicationState.Creates = 0; $publicationState.Edits = 0; $publicationState.Verifies = 0
        $publicationState.Release = @{draft=($scenario -in @('fresh', 'delayed', 'draft-resume')); immutable=$true; prerelease=$true; tag_name='v1.2.0-rc.1'; assets=($inventory | ConvertTo-Json | ConvertFrom-Json)}
        switch ($scenario) {
            'wrong-hash' { $publicationState.Release.assets[0].digest = 'sha256:wrong' }
            'missing-asset' { $publicationState.Release.assets = @($publicationState.Release.assets | Select-Object -Skip 1) }
            'duplicate-asset' { $publicationState.Release.assets[0] = $publicationState.Release.assets[1] }
            'wrong-tag' { $publicationState.Release.tag_name = 'v0.0.0' }
            'wrong-kind' { $publicationState.Release.prerelease = $false }
            'mutable' { $publicationState.Release.immutable = $false }
        }
        $resume = $scenario -notin @('fresh', 'delayed')
        $failed = $false
        try {
            & "$PSScriptRoot/publish-verified-release.ps1" -Tag v1.2.0-rc.1 -Repository synthetic/repository -Path $fixture -NotesPath synthetic.md -Resume:$resume -VerificationAttempts 3 -RetrySeconds 0 | Out-Null
        } catch { $failed = $true }
        $expectedFailure = $scenario -notin @('fresh', 'delayed', 'published', 'draft-resume')
        if ($failed -ne $expectedFailure) { throw "Incorrect publication outcome: $scenario" }
        if ($resume -and $publicationState.Creates -ne 0) { throw "Recovery uploaded assets: $scenario" }
        if ($resume -and $scenario -ne 'draft-resume' -and $publicationState.Edits -ne 0) { throw "Recovery edited a published release: $scenario" }
        $expectedVerifies = switch ($scenario) { 'never-ready' { 3 } 'delayed' { 3 } { $_ -in @('fresh', 'published', 'draft-resume', 'invalid-signature') } { 1 } default { 0 } }
        if ($publicationState.Verifies -ne $expectedVerifies) { throw "Incorrect verification retries: $scenario" }
    }
    Write-Output 'Release publication tests passed: 12 delayed-attestation, recovery and integrity scenarios.'
} finally {
    # Only the explicitly created fixture files are removed; no recursive cleanup.
    Get-ChildItem -LiteralPath $fixture -File | Remove-Item -Force
    Remove-Item -LiteralPath $fixture
}
