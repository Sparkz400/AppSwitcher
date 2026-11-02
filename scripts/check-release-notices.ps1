#requires -Version 7.0
[CmdletBinding()]
param([Parameter(Mandatory)][string]$PackagePath)
$ErrorActionPreference = 'Stop'
$repoPath = Split-Path -Parent $PSScriptRoot
$reviewed = @(Get-Content -LiteralPath (Join-Path $repoPath 'third-party/dependencies.json') -Raw | ConvertFrom-Json)
$seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($manifest in @('app/AppSwitcher.deps.json', 'host/AppSwitcher.BrowserHost.deps.json')) {
    $deps = Get-Content -LiteralPath (Join-Path $PackagePath $manifest) -Raw | ConvertFrom-Json
    foreach ($entry in $deps.libraries.PSObject.Properties) {
        if ($entry.Value.type -eq 'project') { continue }
        $matches = @($reviewed | Where-Object { "$($_.name)/$($_.version)" -eq $entry.Name })
        if ($matches.Count -ne 1) { throw "Unreviewed or ambiguous release dependency: $($entry.Name). Update the inventory and retained notices after review." }
        $seen.Add($entry.Name) | Out-Null
    }
}
if ($seen.Count -ne $reviewed.Count) { throw 'The notice inventory does not exactly match the release dependency set.' }
foreach ($name in @('CommunityToolkit-MIT.txt', 'CommunityToolkit-NOTICES.txt', 'dotnet-MIT.txt', 'Microsoft.Extensions-NOTICES.txt',
    'System.Management-NOTICES.txt', 'serilog-LICENSE.txt', 'serilog-extensions-logging-LICENSE.txt', 'serilog-sinks-file-LICENSE.txt',
    'dotnet-runtime-MIT.txt', 'dotnet-runtime-NOTICES.txt', 'wpf-NOTICES.txt', 'winforms-NOTICES.txt', 'InnoSetup-LICENSE.txt')) {
    $file = Join-Path $repoPath "third-party/$name"
    if (-not (Test-Path -LiteralPath $file) -or (Get-Item -LiteralPath $file).Length -eq 0) { throw "Required license/notice missing: $name" }
}
Write-Output "Release notices verified for $($seen.Count) exact dependencies."
