#requires -Version 7.0
$ErrorActionPreference = 'Stop'
$root = Join-Path (Split-Path -Parent $PSScriptRoot) ('artifacts/notice-tests/' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path "$root/app", "$root/host" -Force | Out-Null
try {
    $libraries = [ordered]@{}
    foreach ($item in (Get-Content "$PSScriptRoot/../third-party/dependencies.json" -Raw | ConvertFrom-Json)) {
        $libraries["$($item.name)/$($item.version)"] = @{type='package'}
    }
    @{libraries=$libraries} | ConvertTo-Json -Depth 4 | Set-Content "$root/app/AppSwitcher.deps.json"
    @{libraries=@{ 'AppSwitcher.BrowserHost/1.2.0-beta.1'=@{type='project'} }} | ConvertTo-Json -Depth 4 | Set-Content "$root/host/AppSwitcher.BrowserHost.deps.json"
    & "$PSScriptRoot/check-release-notices.ps1" -PackagePath $root
    $libraries['Unreviewed.Dependency/1.0.0'] = @{type='package'}
    @{libraries=$libraries} | ConvertTo-Json -Depth 4 | Set-Content "$root/app/AppSwitcher.deps.json"
    $rejected = $false
    try { & "$PSScriptRoot/check-release-notices.ps1" -PackagePath $root } catch { $rejected = $_.Exception.Message -like 'Unreviewed or ambiguous*' }
    if (-not $rejected) { throw 'An unreviewed dependency was not rejected.' }
    $libraries.Remove('Unreviewed.Dependency/1.0.0')
    $libraries.Remove(@($libraries.Keys)[0])
    @{libraries=$libraries} | ConvertTo-Json -Depth 4 | Set-Content "$root/app/AppSwitcher.deps.json"
    $rejected = $false
    try { & "$PSScriptRoot/check-release-notices.ps1" -PackagePath $root } catch { $rejected = $_.Exception.Message -like 'The notice inventory*' }
    if (-not $rejected) { throw 'A stale dependency inventory was not rejected.' }
    Write-Output 'Release-notice negative checks passed.'
} finally {
    $allowed = [IO.Path]::GetFullPath((Join-Path (Split-Path -Parent $PSScriptRoot) 'artifacts/notice-tests')) + [IO.Path]::DirectorySeparatorChar
    if (-not [IO.Path]::GetFullPath($root).StartsWith($allowed, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe test cleanup path.' }
    Remove-Item -LiteralPath $root -Recurse -Force
}
