#requires -Version 7.0
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true
$repoPath = Split-Path -Parent $PSScriptRoot
Push-Location $repoPath
try {
    $version = ([xml](Get-Content Directory.Build.props -Raw)).Project.PropertyGroup.Version
    $commit = (git rev-parse HEAD).Trim()
    $dirty = [bool](git status --porcelain --untracked-files=normal)
    $name = "AppSwitcher-$version-win-x64-$($commit.Substring(0, 8))"
    $releasePath = Join-Path $repoPath 'artifacts/releases'
    $packagePath = Join-Path $repoPath "artifacts/release-build/$name"
    if (Test-Path -LiteralPath $packagePath) { throw "Release build already exists: $packagePath. Use a clean checkout or preserve it and choose a new revision." }
    & (Join-Path $PSScriptRoot 'build-browser-package.ps1') -OutputPath $packagePath -RequireFirefox
    & (Join-Path $PSScriptRoot 'check-release-notices.ps1') -PackagePath $packagePath
    Copy-Item -LiteralPath (Join-Path $repoPath 'third-party') -Destination $packagePath -Recurse
    Copy-Item -LiteralPath (Join-Path $repoPath 'README.md') -Destination (Join-Path $packagePath 'README.md')
    Copy-Item -LiteralPath (Join-Path $repoPath 'docs/images') -Destination (Join-Path $packagePath 'docs/images') -Recurse
    Copy-Item -LiteralPath (Join-Path $repoPath 'docs/store') -Destination (Join-Path $packagePath 'docs/store') -Recurse
    Copy-Item -LiteralPath (Join-Path $repoPath 'examples') -Destination (Join-Path $packagePath 'examples') -Recurse
    foreach ($doc in @('PRODUCT.md', 'DESIGN.md')) {
        Copy-Item -LiteralPath (Join-Path $repoPath $doc) -Destination $packagePath
    }
    Get-ChildItem -LiteralPath (Join-Path $repoPath 'docs') -File | Copy-Item -Destination (Join-Path $packagePath 'docs')
    foreach ($doc in @('LICENSE', 'THIRD_PARTY_NOTICES.md', 'CHANGELOG.md', 'SECURITY.md', 'CONTRIBUTING.md', 'MAINTENANCE.md', 'SUPPORT.md', 'CODE_OF_CONDUCT.md', 'ROADMAP.md')) {
        if (Test-Path -LiteralPath (Join-Path $repoPath $doc)) { Copy-Item -LiteralPath (Join-Path $repoPath $doc) -Destination $packagePath }
    }
    $files = @(Get-ChildItem -LiteralPath $packagePath -File -Recurse | Sort-Object FullName | ForEach-Object {
        [ordered]@{ path = [IO.Path]::GetRelativePath($packagePath, $_.FullName).Replace('\', '/'); sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash; bytes = $_.Length }
    })
    [ordered]@{ version = $version; commit = $commit; dirty = $dirty; createdUtc = [DateTime]::UtcNow.ToString('o'); files = $files } |
        ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $packagePath 'release-manifest.json') -Encoding utf8NoBOM
    dotnet run --project AppSwitcher.UiChecks -c Release -- --setup-package-check (Join-Path $packagePath 'app/browser-setup')
    & (Join-Path $PSScriptRoot 'test-installer.ps1') -PackagePath $packagePath
    New-Item -ItemType Directory -Path $releasePath -Force | Out-Null
    $zipPath = Join-Path $releasePath "$name.zip"
    if (Test-Path -LiteralPath $zipPath) { throw "Release archive already exists: $zipPath" }
    Compress-Archive -Path (Join-Path $packagePath '*') -DestinationPath $zipPath
    "$((Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash)  $name.zip" |
        Set-Content -LiteralPath (Join-Path $releasePath "$name.sha256") -Encoding ascii
    & (Join-Path $PSScriptRoot 'build-installer.ps1') -PackagePath $packagePath -OutputPath $releasePath -Name "$name-setup" -Version $version
    $submissionPath = Join-Path $repoPath "artifacts/store-build/$name"
    & (Join-Path $PSScriptRoot 'build-store-submission.ps1') -OutputPath $submissionPath
    $submissionZip = Join-Path $releasePath "$name-store-submission.zip"
    Compress-Archive -Path (Join-Path $submissionPath '*') -DestinationPath $submissionZip
    "$((Get-FileHash -LiteralPath $submissionZip).Hash)  $name-store-submission.zip" |
        Set-Content -LiteralPath (Join-Path $releasePath "$name-store-submission.sha256") -Encoding ascii
    & (Join-Path $PSScriptRoot 'write-release-sbom.ps1') -ReleasePath $releasePath -Version $version -Commit $commit
    Write-Output "Release ready: $zipPath (source dirty: $dirty)"
} finally {
    Pop-Location
}
