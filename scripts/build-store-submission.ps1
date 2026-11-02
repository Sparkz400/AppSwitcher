#requires -Version 7.0
[CmdletBinding()]
param([Parameter(Mandatory)][string]$OutputPath)
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true
$repoPath = Split-Path -Parent $PSScriptRoot
$output = [IO.Path]::GetFullPath($OutputPath)
if (Test-Path -LiteralPath $output) { throw "Submission output already exists: $output" }
New-Item -ItemType Directory -Path $output -Force | Out-Null
$build = Join-Path $output 'source'
node (Join-Path $PSScriptRoot 'build-browser-extension.mjs') $build | Out-Host
foreach ($name in @('Install-BrowserHost.ps1','Uninstall-BrowserHost.ps1','extension-ids.json')) {
    Remove-Item -LiteralPath (Join-Path $build $name)
}
$version = '1.1.1'
Copy-Item -LiteralPath (Join-Path $repoPath 'docs/store/assets') -Destination $output -Recurse
Get-ChildItem -LiteralPath (Join-Path $repoPath 'docs/store') -File | Copy-Item -Destination $output
Copy-Item -LiteralPath (Join-Path $repoPath 'LICENSE') -Destination $output
foreach ($browser in @('chromium', 'firefox')) {
    $directory = Join-Path $build $browser
    $manifestPath = Join-Path $directory 'manifest.json'
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    if ($manifest.version -ne '1.1.0') { throw 'Review and increment the store submission version after an offline extension version change.' }
    $manifest.version = $version
    $icons = @{}
    foreach ($size in @(16,32,48,128)) {
        $file = Join-Path $repoPath "docs/store/assets/icon-$size.png"
        $bytes = [IO.File]::ReadAllBytes($file)
        $width = [Net.IPAddress]::NetworkToHostOrder([BitConverter]::ToInt32($bytes,16))
        $height = [Net.IPAddress]::NetworkToHostOrder([BitConverter]::ToInt32($bytes,20))
        if ([Convert]::ToHexString($bytes[0..7]) -ne '89504E470D0A1A0A' -or $width -ne $size -or $height -ne $size) { throw "Invalid store icon: $file" }
        Copy-Item -LiteralPath $file -Destination $directory
        $icons["$size"] = "icon-$size.png"
    }
    $manifest | Add-Member -NotePropertyName icons -NotePropertyValue $icons
    if ($browser -eq 'chromium') { $manifest.PSObject.Properties.Remove('key') }
    $manifest | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $manifestPath -Encoding utf8NoBOM
    foreach ($file in @('core.js','background.js','identity.html','profile.html','profile.js','pages.css')) {
        if ((Get-FileHash -LiteralPath (Join-Path $directory $file)).Hash -ne (Get-FileHash -LiteralPath (Join-Path $repoPath "browser-extension/$file")).Hash) {
            throw "Store packaging changed executable/source content: $file"
        }
    }
    $targets = $browser -eq 'chromium' ? @('chrome','edge') : @('firefox')
    foreach ($target in $targets) {
        $zip = Join-Path $output "$target-upload-$version.zip"
        Compress-Archive -Path (Join-Path $directory '*') -DestinationPath $zip
        $archive = [IO.Compression.ZipFile]::OpenRead($zip)
        try {
            if (-not $archive.GetEntry('manifest.json') -or $archive.Entries.Count -ne 11) { throw "Unexpected store archive content: $target" }
        } finally { $archive.Dispose() }
    }
}
$files = @(Get-ChildItem -LiteralPath $output -File -Recurse | Sort-Object FullName | ForEach-Object {
    [ordered]@{ path=[IO.Path]::GetRelativePath($output,$_.FullName).Replace('\','/'); sha256=(Get-FileHash -LiteralPath $_.FullName).Hash }
})
[ordered]@{ desktopSource=(git -C $repoPath rev-parse HEAD).Trim(); sourceDirty=[bool](git -C $repoPath status --porcelain --untracked-files=normal); extensionVersion=$version; signed=$false; submitted=$false; files=$files } |
    ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $output 'submission-manifest.json') -Encoding utf8NoBOM
Write-Output "Store preparation verified: $output"
