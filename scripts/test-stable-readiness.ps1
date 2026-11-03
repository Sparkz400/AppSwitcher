#requires -Version 7.0
$ErrorActionPreference = 'Stop'
$commit = (git rev-parse HEAD).Trim()
& "$PSScriptRoot/check-stable-readiness.ps1" -Version '1.2.0-rc.1' -Commit $commit
& "$PSScriptRoot/check-stable-readiness.ps1" -Version '1.2.0-beta.3' -Commit $commit
$fixture = Join-Path ([IO.Path]::GetTempPath()) ('AppSwitcher-readiness-' + [Guid]::NewGuid().ToString('N') + '.json')
try {
    foreach ($scenario in @('valid-silent-unsigned', 'pending', 'pending-browsers', 'missing-gate', 'wrong-version', 'missing-reviewer', 'missing-report', 'traversal', 'unknown-commit')) {
        $record = @{ version = '1.2.0'; qualifiedCommit = $commit; gates = @{} }
        foreach ($gate in @('workstation', 'offlineBrowsers')) {
            $record.gates[$gate] = @{status='passed'; reviewedBy='Synthetic test'; evidence='docs/WORKSTATION_ACCEPTANCE.md'}
        }
        switch ($scenario) {
            'pending' { $record.gates.workstation.status = 'pending' }
            'pending-browsers' { $record.gates.offlineBrowsers.status = 'pending' }
            'missing-gate' { $record.gates.Remove('offlineBrowsers') }
            'wrong-version' { $record.version = '0.0.0' }
            'missing-reviewer' { $record.gates.workstation.reviewedBy = '' }
            'missing-report' { $record.gates.offlineBrowsers.evidence = 'docs/does-not-exist.md' }
            'traversal' { $record.gates.offlineBrowsers.evidence = 'docs/../README.md' }
            'unknown-commit' { $record.qualifiedCommit = $null }
        }
        $record | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $fixture
        $rejected = $false
        try { & "$PSScriptRoot/check-stable-readiness.ps1" -Version '1.2.0' -Commit $commit -EvidencePath $fixture } catch { $rejected = $true }
        if ($scenario -eq 'valid-silent-unsigned') {
            if ($rejected) { throw 'Stable gate rejected qualified silent-workstation evidence without Narrator or publisher signing.' }
        } elseif (-not $rejected) { throw "Stable gate accepted $scenario." }
    }
    Write-Output 'Stable readiness gate passed: beta/RC and qualified unsigned stable for silent workstations allowed; eight invalid stable qualifications rejected.'
} finally { Remove-Item -LiteralPath $fixture -ErrorAction SilentlyContinue }
