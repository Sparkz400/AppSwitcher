#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Path,
    [Parameter(Mandatory)][ValidatePattern('^\d+\.\d+\.\d+-beta\.\d+$')][string]$Version,
    [Parameter(Mandatory)][ValidatePattern('^[0-9a-f]{40}$')][string]$Commit
)
$ErrorActionPreference = 'Stop'
$name = "AppSwitcher-$Version-win-x64-$($Commit.Substring(0, 8))"
$expected = @("$name.zip", "$name.sha256", "$name-setup.exe", "$name-setup.sha256", "$name-store-submission.zip", "$name-store-submission.sha256")
$actual = @(Get-ChildItem -LiteralPath $Path -Force)
if ($actual.Count -ne 6 -or @($actual | Where-Object { $_.PSIsContainer -or $_.Name -cnotin $expected }).Count) {
    throw 'Release must contain exactly the six expected assets.'
}
foreach ($pair in @(@("$name.zip", "$name.sha256"), @("$name-setup.exe", "$name-setup.sha256"), @("$name-store-submission.zip", "$name-store-submission.sha256"))) {
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
Write-Output "Release artifacts verified for $Version at $Commit."
