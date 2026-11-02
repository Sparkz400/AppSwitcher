#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ReleasePath,
    [Parameter(Mandatory)][string]$Version,
    [Parameter(Mandatory)][ValidatePattern('^[0-9a-f]{40}$')][string]$Commit
)
$ErrorActionPreference = 'Stop'
$repoPath = Split-Path -Parent $PSScriptRoot
$name = "AppSwitcher-$Version-win-x64-$($Commit.Substring(0, 8))"
$source = "https://github.com/Sparkz400/AppSwitcher/tree/$Commit"
$reviewed = @(Get-Content "$repoPath/third-party/dependencies.json" -Raw | ConvertFrom-Json)
$packages = [Collections.Generic.List[object]]::new()
$relationships = [Collections.Generic.List[object]]::new()
$components = @(
    @{name='AppSwitcher'; version=$Version; id='SPDXRef-AppSwitcher'},
    @{name='AppSwitcher.BrowserHost'; version=$Version; id='SPDXRef-BrowserHost'},
    @{name='AppSwitcher Browser Bridge'; version=(Get-Content "$repoPath/browser-extension/manifest.chromium.json" -Raw | ConvertFrom-Json).version; id='SPDXRef-BrowserBridge'}
)
foreach ($component in $components) {
    $packages.Add(@{SPDXID=$component.id; name=$component.name; versionInfo=$component.version; downloadLocation=$source;
        filesAnalyzed=$false; licenseConcluded='MIT'; licenseDeclared='MIT'; copyrightText='Copyright (c) 2026 Nico Schürmann and AppSwitcher contributors'; sourceInfo="Source commit: $Commit"})
}
foreach ($dependency in $reviewed) {
    $nugetName = $dependency.name -replace '^runtimepack\.', ''
    $id = 'SPDXRef-' + $dependency.name
    $packages.Add(@{SPDXID=$id; name=$dependency.name; versionInfo=$dependency.version; filesAnalyzed=$false;
        downloadLocation="https://www.nuget.org/api/v2/package/$nugetName/$($dependency.version)";
        licenseConcluded=$dependency.license; licenseDeclared=$dependency.license;
        copyrightText=($dependency.copyright ? $dependency.copyright : 'NOASSERTION');
        sourceInfo="$($dependency.source)/tree/$($dependency.commit)";
        externalRefs=@(@{referenceCategory='PACKAGE-MANAGER'; referenceType='purl'; referenceLocator="pkg:nuget/$nugetName@$($dependency.version)"})})
}
# Keep the installer engine inventory tied to the version used by the existing builder.
$compilerScript = Get-Content "$PSScriptRoot/get-inno-setup.ps1" -Raw
$installerVersion = [regex]::Match($compilerScript, 'releases/download/is-(?<tag>\d+_\d+_\d+)/innosetup-(?<version>\d+\.\d+\.\d+)-x64.exe')
if (-not $installerVersion.Success) { throw 'Review the Inno Setup inventory after changing the compiler download format.' }
$packages.Add(@{SPDXID='SPDXRef-InnoSetup'; name='Inno Setup installer engine'; versionInfo=$installerVersion.Groups['version'].Value;
    filesAnalyzed=$false; downloadLocation="https://github.com/jrsoftware/issrc/tree/is-$($installerVersion.Groups['tag'].Value)";
    licenseConcluded='LicenseRef-InnoSetup'; licenseDeclared='LicenseRef-InnoSetup'; copyrightText='NOASSERTION'})
foreach ($asset in @(@{id='SPDXRef-Portable'; file="$name.zip"}, @{id='SPDXRef-Installer'; file="$name-setup.exe"})) {
    $hash = (Get-FileHash -LiteralPath (Join-Path $ReleasePath $asset.file)).Hash.ToLowerInvariant()
    $packages.Add(@{SPDXID=$asset.id; name=$asset.file; versionInfo=$Version; filesAnalyzed=$false;
        downloadLocation="https://github.com/Sparkz400/AppSwitcher/releases/download/v$Version/$($asset.file)";
        licenseConcluded='NOASSERTION'; licenseDeclared='NOASSERTION'; copyrightText='NOASSERTION';
        checksums=@(@{algorithm='SHA256'; checksumValue=$hash})})
    $relationships.Add(@{spdxElementId='SPDXRef-DOCUMENT'; relationshipType='DESCRIBES'; relatedSpdxElement=$asset.id})
    foreach ($component in @($components.id) + @($reviewed | ForEach-Object { 'SPDXRef-' + $_.name })) {
        $relationships.Add(@{spdxElementId=$asset.id; relationshipType='CONTAINS'; relatedSpdxElement=$component})
    }
}
$relationships.Add(@{spdxElementId='SPDXRef-Installer'; relationshipType='CONTAINS'; relatedSpdxElement='SPDXRef-InnoSetup'})
$sbom = [ordered]@{
    spdxVersion='SPDX-2.3'; dataLicense='CC0-1.0'; SPDXID='SPDXRef-DOCUMENT'; name="$name desktop distributions"
    documentNamespace="https://github.com/Sparkz400/AppSwitcher/sbom/$Commit/$([Guid]::NewGuid())"
    creationInfo=@{created=[DateTime]::UtcNow.ToString('yyyy-MM-ddTHH:mm:ssZ'); creators=@('Tool: AppSwitcher release SBOM generator');
        comment='Package-level inventory of the portable desktop distribution and installer. Runtime packs are inventory units; their internal libraries are not individually expanded. File hashes are in release-manifest.json. Excludes build/test tools, Windows, installed browsers and the separately prepared unsigned store-submission bundle.'}
    packages=$packages.ToArray(); relationships=$relationships.ToArray()
    hasExtractedLicensingInfos=@(@{licenseId='LicenseRef-InnoSetup'; name='Inno Setup License';
        extractedText=(Get-Content "$repoPath/third-party/InnoSetup-LICENSE.txt" -Raw);
        seeAlsos=@("https://github.com/jrsoftware/issrc/blob/is-$($installerVersion.Groups['tag'].Value)/license.txt")})
}
$path = Join-Path $ReleasePath "$name.spdx.json"
if (Test-Path -LiteralPath $path) { throw "SBOM already exists: $path" }
$sbom | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $path -Encoding utf8NoBOM
"$((Get-FileHash -LiteralPath $path).Hash)  $name.spdx.json" | Set-Content -LiteralPath "$path.sha256" -Encoding ascii
Write-Output "SPDX inventory written for $($packages.Count) packages: $path"
