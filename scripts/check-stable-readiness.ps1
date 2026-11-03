#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Version,
    [Parameter(Mandatory)][ValidatePattern('^[0-9a-f]{40}$')][string]$Commit,
    [string]$EvidencePath = "$PSScriptRoot/../docs/release-readiness.json"
)
$ErrorActionPreference = 'Stop'
if ($Version -match '^\d+\.\d+\.\d+-(beta|rc)\.\d+$') { return }
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw 'Unsupported release version.' }
$evidence = Get-Content -LiteralPath $EvidencePath -Raw | ConvertFrom-Json
if ($evidence.version -cne $Version -or $evidence.qualifiedCommit -notmatch '^[0-9a-f]{40}$') { throw 'Stable qualification version/commit missing.' }
git merge-base --is-ancestor $evidence.qualifiedCommit $Commit
if ($LASTEXITCODE -ne 0) { throw 'Qualified revision is not an ancestor of the release.' }
$changed = @(git diff --name-only $evidence.qualifiedCommit $Commit -- . ':!docs/**' ':!CHANGELOG.md' ':!Directory.Build.props')
if ($LASTEXITCODE -ne 0 -or $changed.Count) { throw 'Application/build changes since qualification require new acceptance evidence.' }
$qualifiedProps = (git show "$($evidence.qualifiedCommit):Directory.Build.props") -join "`n"
if ($LASTEXITCODE -ne 0) { throw 'Cannot read qualified build properties.' }
$releaseProps = (git show "${Commit}:Directory.Build.props") -join "`n"
if ($LASTEXITCODE -ne 0) { throw 'Cannot read release build properties.' }
if (($qualifiedProps -replace '<Version>[^<]+</Version>', '<Version>RELEASE</Version>') -cne ($releaseProps -replace '<Version>[^<]+</Version>', '<Version>RELEASE</Version>')) { throw 'Build properties changed beyond the release version; repeat qualification.' }
function Assert-EvidenceReport([string]$Report, [string]$Label) {
    if ($Report -notmatch '^docs/[a-zA-Z0-9_./-]+\.md$') { throw "Invalid acceptance report: $Label" }
    $repo = [IO.Path]::GetFullPath("$PSScriptRoot/..")
    $file = [IO.Path]::GetFullPath((Join-Path $repo $Report))
    if (-not $file.StartsWith((Join-Path $repo 'docs') + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or -not (Test-Path -LiteralPath $file -PathType Leaf)) { throw "Missing acceptance report: $Label" }
}
$deferred = @()
foreach ($gate in @('workstation', 'offlineBrowsers')) {
    $record = $evidence.gates.$gate
    if ($record.status -cnotin @('passed', 'deferred') -or [string]::IsNullOrWhiteSpace($record.reviewedBy)) { throw "Stable gate incomplete: $gate" }
    Assert-EvidenceReport $record.evidence $gate
    if ($record.status -ceq 'deferred') {
        if ([string]::IsNullOrWhiteSpace($record.reason)) { throw "Deferred gate lacks a disclosed limitation: $gate" }
        $deferred += $gate
    }
}
if ($deferred.Count) {
    $authorization = $evidence.publicationAuthorization
    if ($authorization.status -cne 'approved-with-limitations' -or $authorization.version -cne $Version -or
        [string]::IsNullOrWhiteSpace($authorization.approvedBy) -or
        $authorization.deploymentAccepted -isnot [bool] -or $authorization.deploymentAccepted -or
        @($authorization.deferredGates).Count -ne $deferred.Count -or
        @(Compare-Object $deferred @($authorization.deferredGates) -CaseSensitive).Count) {
        throw 'Deferred checks require explicit versioned publication authorization with the exact disclosed scope; deployment must remain unaccepted.'
    }
    Assert-EvidenceReport $authorization.evidence 'publication authorization'
    Write-Output "Owner-authorized publication with deferred checks: $($deferred -join ', '). Site deployment is not accepted."
}
Write-Output "Stable release records verified for $Version. Windows publisher signing is optional."
