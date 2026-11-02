[CmdletBinding()]
param(
    [string]$OutputPath,
    [string]$DistributionFile,
    [string]$FirefoxSignedXpi,
    [switch]$RequireFirefox
)
$ErrorActionPreference = 'Stop'
$repoPath = Split-Path -Parent $PSScriptRoot
if (-not $FirefoxSignedXpi) {
    $firefoxManifest = Get-Content -LiteralPath (Join-Path $repoPath 'browser-extension\manifest.firefox.json') -Raw | ConvertFrom-Json
    $bundledXpi = Join-Path $repoPath "browser-extension\signed\appswitcher-firefox-$($firefoxManifest.version).xpi"
    if (Test-Path -LiteralPath $bundledXpi -PathType Leaf) { $FirefoxSignedXpi = $bundledXpi }
}
if ($RequireFirefox -and -not $FirefoxSignedXpi) { throw 'Firefox offline deployment requires -FirefoxSignedXpi. Sign the extension during release preparation, then bundle that file. Target PCs need no account or internet connection.' }
if ($FirefoxSignedXpi -and -not (Test-Path -LiteralPath $FirefoxSignedXpi -PathType Leaf)) { throw 'The Firefox XPI file was not found.' }
$packagePath = if ($OutputPath) { [System.IO.Path]::GetFullPath($OutputPath) } else { Join-Path $repoPath 'artifacts\browser-package' }
node (Join-Path $PSScriptRoot 'build-browser-extension.mjs') $packagePath
if ($LASTEXITCODE -ne 0) { throw 'Extension packaging failed.' }
if ($FirefoxSignedXpi) {
    # A source edit needs a newly signed version. Never silently bundle an old signed payload.
    $archive = [System.IO.Compression.ZipFile]::OpenRead([System.IO.Path]::GetFullPath($FirefoxSignedXpi))
    try {
        if (-not $archive.GetEntry('META-INF/mozilla.rsa') -and -not $archive.GetEntry('META-INF/cose.sig')) { throw 'Firefox XPI has no signature metadata.' }
        $sourceFiles = @(Get-ChildItem -LiteralPath (Join-Path $packagePath 'firefox') -File)
        $payloadFiles = @($archive.Entries | Where-Object { -not $_.FullName.StartsWith('META-INF/', [StringComparison]::Ordinal) -and -not $_.FullName.EndsWith('/') })
        if ($payloadFiles.Count -ne $sourceFiles.Count) { throw 'Signed Firefox payload differs from the current source. Sign a new extension version.' }
        foreach ($sourceFile in $sourceFiles) {
            $entry = $archive.GetEntry($sourceFile.Name)
            if (-not $entry) { throw "Signed Firefox payload is missing $($sourceFile.Name)." }
            $sourceStream = $sourceFile.OpenRead()
            $signedStream = $entry.Open()
            try {
                $sourceHash = [Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData($sourceStream))
                $signedHash = [Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData($signedStream))
                if ($sourceHash -ne $signedHash) { throw "Signed Firefox payload differs in $($sourceFile.Name). Sign a new extension version." }
            } finally { $sourceStream.Dispose(); $signedStream.Dispose() }
        }
    } finally { $archive.Dispose() }
}
dotnet publish (Join-Path $repoPath 'AppSwitcher.BrowserHost\AppSwitcher.BrowserHost.csproj') -c Release -r win-x64 --self-contained true -p:RestoreLockedMode=true -o (Join-Path $packagePath 'host')
if ($LASTEXITCODE -ne 0) { throw 'Native host publishing failed.' }
dotnet publish (Join-Path $repoPath 'AppSwitcher.csproj') -c Release -r win-x64 --self-contained true -p:RestoreLockedMode=true -o (Join-Path $packagePath 'app')
if ($LASTEXITCODE -ne 0) { throw 'AppSwitcher publishing failed.' }
$setupPath = Join-Path $packagePath 'app\browser-setup'
New-Item -ItemType Directory -Path $setupPath -Force | Out-Null
foreach ($folder in @('host', 'chromium', 'firefox')) {
    $destinationPath = Join-Path $setupPath $folder
    New-Item -ItemType Directory -Path $destinationPath -Force | Out-Null
    Get-ChildItem -LiteralPath (Join-Path $packagePath $folder) -File | Copy-Item -Destination $destinationPath -Force
}
$distributionSource = if ($DistributionFile) { $DistributionFile } else { Join-Path $repoPath 'browser-extension\distribution.json' }
Copy-Item -LiteralPath $distributionSource -Destination (Join-Path $setupPath 'distribution.json') -Force
$distribution = Get-Content -LiteralPath $distributionSource -Raw | ConvertFrom-Json
@{ chrome = $distribution.chrome.extensionId; edge = $distribution.edge.extensionId; firefox = $distribution.firefox.extensionId } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $packagePath 'extension-ids.json') -Encoding utf8NoBOM
if ($FirefoxSignedXpi) { Copy-Item -LiteralPath $FirefoxSignedXpi -Destination (Join-Path $setupPath 'appswitcher-firefox.xpi') -Force }
elseif (Test-Path -LiteralPath (Join-Path $setupPath 'appswitcher-firefox.xpi')) { Remove-Item -LiteralPath (Join-Path $setupPath 'appswitcher-firefox.xpi') }
Copy-Item -LiteralPath (Join-Path $repoPath 'docs\BROWSER_INTEGRATION.md') -Destination (Join-Path $packagePath 'README.md')
New-Item -ItemType Directory -Path (Join-Path $packagePath 'docs') -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $repoPath 'docs\BROWSER_INTEGRATION.md') -Destination (Join-Path $packagePath 'docs\BROWSER_INTEGRATION.md')
Copy-Item -LiteralPath (Join-Path $repoPath 'docs\OPERATIONS.md') -Destination (Join-Path $packagePath 'docs\OPERATIONS.md')
Compress-Archive -Path (Join-Path $packagePath 'firefox\*') -DestinationPath (Join-Path $packagePath 'appswitcher-firefox-unsigned.zip') -Force
Write-Output "Package ready: $packagePath"
if (-not $FirefoxSignedXpi) { Write-Warning 'Firefox installation is unavailable in this package: no signed XPI was supplied. Chrome and Edge can be installed offline using Load unpacked.' }
