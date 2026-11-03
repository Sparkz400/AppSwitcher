#requires -Version 7.0
$ErrorActionPreference = 'Stop'
$commit = (git rev-parse HEAD).Trim()
& "$PSScriptRoot/check-stable-readiness.ps1" -Version '1.2.0-rc.1' -Commit $commit
& "$PSScriptRoot/check-stable-readiness.ps1" -Version '1.2.0-beta.3' -Commit $commit
$fixture = Join-Path ([IO.Path]::GetTempPath()) ('AppSwitcher-readiness-' + [Guid]::NewGuid().ToString('N') + '.json')
try {
    $scenarios = @('valid-silent-unsigned', 'valid-authorized-deferral', 'pending', 'pending-browsers', 'missing-gate', 'wrong-version', 'missing-reviewer', 'missing-report', 'traversal', 'unknown-commit', 'deferred-without-authorization', 'deferred-without-reason', 'authorization-wrong-version', 'authorization-missing-owner', 'authorization-missing-report', 'authorization-traversal', 'authorization-wrong-scope', 'authorization-duplicate-scope', 'authorization-claims-deployment', 'authorization-missing-deployment-status', 'unknown-status')
    foreach ($scenario in $scenarios) {
        $record = @{ version = '1.2.0'; qualifiedCommit = $commit; gates = @{} }
        foreach ($gate in @('workstation', 'offlineBrowsers')) {
            $record.gates[$gate] = @{status='passed'; reviewedBy='Synthetic test'; evidence='docs/WORKSTATION_ACCEPTANCE.md'}
        }
        if ($scenario -eq 'valid-authorized-deferral' -or $scenario -like 'deferred-*' -or $scenario -like 'authorization-*') {
            $record.gates.workstation.status = 'deferred'
            $record.gates.workstation.reason = 'Synthetic publication decision; hardware acceptance remains unperformed.'
            $record.publicationAuthorization = @{status='approved-with-limitations'; version='1.2.0'; approvedBy='Synthetic owner'; deploymentAccepted=$false; deferredGates=@('workstation'); evidence='docs/WORKSTATION_ACCEPTANCE.md'}
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
            'deferred-without-authorization' { $record.Remove('publicationAuthorization') }
            'deferred-without-reason' { $record.gates.workstation.Remove('reason') }
            'authorization-wrong-version' { $record.publicationAuthorization.version = '0.0.0' }
            'authorization-missing-owner' { $record.publicationAuthorization.approvedBy = ' ' }
            'authorization-missing-report' { $record.publicationAuthorization.evidence = 'docs/missing.md' }
            'authorization-traversal' { $record.publicationAuthorization.evidence = 'docs/../README.md' }
            'authorization-wrong-scope' { $record.publicationAuthorization.deferredGates = @('offlineBrowsers') }
            'authorization-duplicate-scope' { $record.publicationAuthorization.deferredGates = @('workstation', 'workstation') }
            'authorization-claims-deployment' { $record.publicationAuthorization.deploymentAccepted = $true }
            'authorization-missing-deployment-status' { $record.publicationAuthorization.Remove('deploymentAccepted') }
            'unknown-status' { $record.gates.workstation.status = 'ignored' }
        }
        $record | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $fixture
        $rejected = $false
        try { & "$PSScriptRoot/check-stable-readiness.ps1" -Version '1.2.0' -Commit $commit -EvidencePath $fixture } catch { $rejected = $true }
        if ($scenario -like 'valid-*') {
            if ($rejected) { throw "Stable gate rejected $scenario." }
        } elseif (-not $rejected) { throw "Stable gate accepted $scenario." }
    }
    Write-Output 'Stable readiness gate passed: passed checks and explicit owner-authorized deferral accepted; 19 invalid qualifications rejected.'
} finally { Remove-Item -LiteralPath $fixture -ErrorAction SilentlyContinue }
