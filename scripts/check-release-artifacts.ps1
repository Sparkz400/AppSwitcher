#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Path,
    [Parameter(Mandatory)][ValidatePattern('^\d+\.\d+\.\d+-beta\.\d+$')][string]$Version,
    [Parameter(Mandatory)][ValidatePattern('^[0-9a-f]{40}$')][string]$Commit
)
$ErrorActionPreference = 'Stop'
$name = "AppSwitcher-$Version-win-x64-$($Commit.Substring(0, 8))"
$expected = @("$name.zip", "$name.sha256", "$name-setup.exe", "$name-setup.exe.sha256", "$name-store-submission.zip", "$name-store-submission.sha256", "$name.spdx.json", "$name.spdx.json.sha256")
$actual = @(Get-ChildItem -LiteralPath $Path -Force)
if ($actual.Count -ne $expected.Count -or @($actual | Where-Object { $_.PSIsContainer -or $_.Name -cnotin $expected }).Count) {
    throw 'Release must contain exactly the eight expected assets.'
}
foreach ($pair in @(@("$name.zip", "$name.sha256"), @("$name-setup.exe", "$name-setup.exe.sha256"), @("$name-store-submission.zip", "$name-store-submission.sha256"), @("$name.spdx.json", "$name.spdx.json.sha256"))) {
    $file = Join-Path $Path $pair[0]
    $checksum = (Get-Content -LiteralPath (Join-Path $Path $pair[1]) -Raw).Trim()
    $hash = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash
    if ((Get-Item -LiteralPath $file).Length -eq 0 -or $checksum -cne "$hash  $($pair[0])") { throw "Release checksum mismatch: $($pair[0])" }
}
foreach ($package in @(@("$name.zip", 'release-manifest.json'), @("$name-store-submission.zip", 'submission-manifest.json'))) {
    $zip = [IO.Compression.ZipFile]::OpenRead((Join-Path $Path $package[0]))
    try {
        $entries = @($zip.Entries | Where-Object { -not $_.FullName.EndsWith('/') })
        $paths = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
        foreach ($entry in $entries) {
            if ($entry.FullName -match '(^/|\\|:|(^|/)\.\.?(/|$))' -or -not $paths.Add($entry.FullName)) { throw 'Unsafe or duplicate archive path.' }
        }
        $manifestEntry = $zip.GetEntry($package[1])
        if (-not $manifestEntry) { throw 'Release manifest missing.' }
        $reader = [IO.StreamReader]::new($manifestEntry.Open())
        try { $manifest = $reader.ReadToEnd() | ConvertFrom-Json } finally { $reader.Dispose() }
        if ($package[1] -eq 'release-manifest.json') {
            if ($manifest.commit -cne $Commit -or $manifest.version -cne $Version -or $manifest.dirty -isnot [bool] -or $manifest.dirty) { throw 'Release source mismatch or dirty build.' }
        } elseif ($manifest.desktopSource -cne $Commit -or $manifest.sourceDirty -isnot [bool] -or $manifest.sourceDirty -or
            $manifest.signed -isnot [bool] -or $manifest.signed -or $manifest.submitted -isnot [bool] -or $manifest.submitted) {
            throw 'Submission source or distribution status mismatch.'
        }
        $recorded = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
        foreach ($file in $manifest.files) {
            if (-not $file.path -or $file.path -eq $package[1] -or -not $recorded.Add($file.path)) { throw 'Duplicate or invalid manifest path.' }
            $entry = $zip.GetEntry($file.path)
            if (-not $entry -or $file.sha256 -notmatch '^[A-Fa-f0-9]{64}$') { throw 'Manifest file or hash missing.' }
            $stream = $entry.Open()
            try { $hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream)) } finally { $stream.Dispose() }
            if ($hash -ne $file.sha256) { throw "Manifest checksum mismatch: $($file.path)" }
            if ($package[1] -eq 'release-manifest.json' -and ($null -eq $file.bytes -or $file.bytes -ne $entry.Length)) { throw 'Manifest size mismatch.' }
        }
        if ($recorded.Count -eq 0 -or $recorded.Count -ne $entries.Count - 1) { throw 'Archive and manifest file sets differ.' }
    } finally { $zip.Dispose() }
}
$schema = Join-Path (Split-Path -Parent $PSScriptRoot) 'artifacts/tools/spdx-2.3.schema.json'
if (-not (Test-Path -LiteralPath $schema)) {
    New-Item -ItemType Directory -Path (Split-Path -Parent $schema) -Force | Out-Null
    Invoke-WebRequest 'https://raw.githubusercontent.com/spdx/spdx-spec/aadf3b0b8dbbabdb4d880b0fc714255fea436ff7/schemas/spdx-schema.json' -OutFile $schema
}
if ((Get-FileHash -LiteralPath $schema).Hash -ne '239208B7AC287B3CF5D9A9AF23F9D69863971102A5E1587A27A398B43490B89B') { throw 'SPDX schema checksum mismatch.' }
$sbomJson = Get-Content -LiteralPath (Join-Path $Path "$name.spdx.json") -Raw
if (-not (Test-Json -Json $sbomJson -SchemaFile $schema)) { throw 'Invalid SPDX document.' }
$sbom = $sbomJson | ConvertFrom-Json
if ($sbom.spdxVersion -ne 'SPDX-2.3' -or $sbom.dataLicense -ne 'CC0-1.0' -or $sbom.documentNamespace -notlike "https://github.com/Sparkz400/AppSwitcher/sbom/$Commit/*") { throw 'SBOM source mismatch.' }
$ids = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$null = $ids.Add('SPDXRef-DOCUMENT')
foreach ($package in $sbom.packages) { if (-not $ids.Add($package.SPDXID)) { throw 'Duplicate SBOM package.' } }
foreach ($relation in $sbom.relationships) {
    if (-not $ids.Contains($relation.spdxElementId) -or -not $ids.Contains($relation.relatedSpdxElement)) { throw 'SBOM relationship points to a missing package.' }
}
$reviewed = @(Get-Content "$PSScriptRoot/../third-party/dependencies.json" -Raw | ConvertFrom-Json)
if (@($sbom.packages).Count -ne $reviewed.Count + 6) { throw 'SBOM package set mismatch.' }
$bridgeVersion = (Get-Content "$PSScriptRoot/../browser-extension/manifest.chromium.json" -Raw | ConvertFrom-Json).version
foreach ($component in @(@{id='SPDXRef-AppSwitcher'; version=$Version}, @{id='SPDXRef-BrowserHost'; version=$Version}, @{id='SPDXRef-BrowserBridge'; version=$bridgeVersion})) {
    $found = @($sbom.packages | Where-Object { $_.SPDXID -ceq $component.id -and $_.versionInfo -ceq $component.version -and $_.sourceInfo -ceq "Source commit: $Commit" -and $_.licenseDeclared -eq 'MIT' })
    if ($found.Count -ne 1) { throw 'SBOM application source or version mismatch.' }
}
$engine = @($sbom.packages | Where-Object SPDXID -CEQ 'SPDXRef-InnoSetup')
$compiler = Get-Content "$PSScriptRoot/get-inno-setup.ps1" -Raw
if ($engine.Count -ne 1 -or $engine[0].versionInfo -notmatch '^\d+\.\d+\.\d+$' -or -not $compiler.Contains("innosetup-$($engine[0].versionInfo)-x64.exe") -or $engine[0].licenseDeclared -cne 'LicenseRef-InnoSetup') { throw 'SBOM installer engine mismatch.' }
$license = @($sbom.hasExtractedLicensingInfos | Where-Object licenseId -CEQ 'LicenseRef-InnoSetup')
if ($license.Count -ne 1 -or $license[0].extractedText -cne (Get-Content "$PSScriptRoot/../third-party/InnoSetup-LICENSE.txt" -Raw)) { throw 'SBOM installer license mismatch.' }
foreach ($dependency in $reviewed) {
    $found = @($sbom.packages | Where-Object { $_.SPDXID -ceq ('SPDXRef-' + $dependency.name) -and $_.name -ceq $dependency.name -and $_.versionInfo -ceq $dependency.version -and $_.licenseDeclared -ceq $dependency.license })
    if ($found.Count -ne 1) { throw "SBOM dependency mismatch: $($dependency.name)" }
}
foreach ($asset in @(@{id='SPDXRef-Portable'; file="$name.zip"}, @{id='SPDXRef-Installer'; file="$name-setup.exe"})) {
    $package = @($sbom.packages | Where-Object { $_.SPDXID -ceq $asset.id -and $_.name -ceq $asset.file -and $_.versionInfo -ceq $Version })
    $hash = (Get-FileHash -LiteralPath (Join-Path $Path $asset.file)).Hash
    if ($package.Count -ne 1 -or @($package[0].checksums).Count -ne 1 -or $package[0].checksums[0].algorithm -ne 'SHA256' -or $package[0].checksums[0].checksumValue -ne $hash) { throw 'SBOM asset checksum mismatch.' }
    $contained = @($sbom.relationships | Where-Object { $_.spdxElementId -ceq $asset.id -and $_.relationshipType -eq 'CONTAINS' } | ForEach-Object relatedSpdxElement)
    $expectedIds = @('SPDXRef-AppSwitcher','SPDXRef-BrowserHost','SPDXRef-BrowserBridge') + @($reviewed | ForEach-Object { 'SPDXRef-' + $_.name })
    if ($asset.id -eq 'SPDXRef-Installer') { $expectedIds += 'SPDXRef-InnoSetup' }
    if ($contained.Count -ne $expectedIds.Count -or (Compare-Object $expectedIds $contained)) { throw 'SBOM component relationships mismatch.' }
}
Write-Output "Release artifacts and SPDX inventory verified for $Version at $Commit."
