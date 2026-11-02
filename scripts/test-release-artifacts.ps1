#requires -Version 7.0
$ErrorActionPreference = 'Stop'
$root = Join-Path (Split-Path -Parent $PSScriptRoot) ('artifacts/release-artifact-tests/' + [Guid]::NewGuid().ToString('N'))
$version = '1.2.0-beta.2'
$commit = '0123456789abcdef0123456789abcdef01234567'
$name = "AppSwitcher-$version-win-x64-$($commit.Substring(0, 8))"

function Write-Fixture([string]$Scenario) {
    $path = Join-Path $root $Scenario
    New-Item -ItemType Directory -Path $path | Out-Null
    foreach ($store in @($false, $true)) {
        $manifestName = $store ? 'submission-manifest.json' : 'release-manifest.json'
        $zipName = $store ? "$name-store-submission.zip" : "$name.zip"
        $data = [Text.Encoding]::UTF8.GetBytes('synthetic payload')
        $file = @{path='app/example.txt'; sha256=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($data)); bytes=$data.Length}
        $manifest = $store ? @{desktopSource=$commit; sourceDirty=$false; signed=$false; submitted=$false; files=@($file)} : @{commit=$commit; version=$version; dirty=$false; files=@($file)}
        if (-not $store) {
            switch ($Scenario) {
                'dirty' { $manifest.dirty = $true }
                'missing-dirty' { $manifest.Remove('dirty') }
                'wrong-source' { $manifest.commit = 'f' * 40 }
                'wrong-version' { $manifest.version = '0.0.0-beta.1' }
                'tampered-payload' { $data = [Text.Encoding]::UTF8.GetBytes('changed payload') }
                'wrong-size' { $file.bytes++ }
                'duplicate-manifest' { $manifest.files = @($file, $file) }
            }
        } elseif ($Scenario -eq 'dirty-store') { $manifest.sourceDirty = $true }
        elseif ($Scenario -eq 'submitted-store') { $manifest.submitted = $true }
        $zip = [IO.Compression.ZipFile]::Open((Join-Path $path $zipName), [IO.Compression.ZipArchiveMode]::Create)
        try {
            $entry = $zip.CreateEntry('app/example.txt')
            $stream = $entry.Open()
            try { $stream.Write($data) } finally { $stream.Dispose() }
            if (-not $store -and $Scenario -eq 'extra-entry') { $null = $zip.CreateEntry('extra.txt') }
            if (-not $store -and $Scenario -eq 'unsafe-entry') { $null = $zip.CreateEntry('../escape.txt') }
            if (-not $store -and $Scenario -eq 'duplicate-entry') { $null = $zip.CreateEntry('app/example.txt') }
            if ($store -or $Scenario -ne 'missing-manifest') {
                $writer = [IO.StreamWriter]::new($zip.CreateEntry($manifestName).Open())
                try { $writer.Write(($manifest | ConvertTo-Json -Depth 5)) } finally { $writer.Dispose() }
            }
        } finally { $zip.Dispose() }
    }
    'synthetic installer' | Set-Content (Join-Path $path "$name-setup.exe")
    foreach ($pair in @(@("$name.zip", "$name.sha256"), @("$name-setup.exe", "$name-setup.exe.sha256"), @("$name-store-submission.zip", "$name-store-submission.sha256"))) {
        "$((Get-FileHash (Join-Path $path $pair[0])).Hash)  $($pair[0])" | Set-Content (Join-Path $path $pair[1])
    }
    switch ($Scenario) {
        'bad-checksum' { 'invalid' | Set-Content (Join-Path $path "$name.sha256") }
        'extra-asset' { 'extra' | Set-Content (Join-Path $path 'extra.txt') }
        'missing-asset' { Remove-Item -LiteralPath (Join-Path $path "$name-setup.exe") }
    }
    return $path
}

try {
    $valid = Write-Fixture 'valid'
    & "$PSScriptRoot/check-release-artifacts.ps1" -Path $valid -Version $version -Commit $commit
    $scenarios = @('dirty', 'missing-dirty', 'wrong-source', 'wrong-version', 'tampered-payload', 'wrong-size', 'duplicate-manifest', 'dirty-store', 'submitted-store', 'extra-entry', 'unsafe-entry', 'duplicate-entry', 'missing-manifest', 'bad-checksum', 'extra-asset', 'missing-asset')
    foreach ($scenario in $scenarios) {
        $path = Write-Fixture $scenario
        $rejected = $false
        try { & "$PSScriptRoot/check-release-artifacts.ps1" -Path $path -Version $version -Commit $commit } catch { $rejected = $true }
        if (-not $rejected) { throw "Release gate accepted $scenario." }
    }
    Write-Output "Release artifact gate passed: valid package and $($scenarios.Count) rejected invalid packages."
} finally {
    $allowed = [IO.Path]::GetFullPath((Join-Path (Split-Path -Parent $PSScriptRoot) 'artifacts/release-artifact-tests')) + [IO.Path]::DirectorySeparatorChar
    if (-not [IO.Path]::GetFullPath($root).StartsWith($allowed, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe fixture cleanup path.' }
    if (Test-Path -LiteralPath $root) { Remove-Item -LiteralPath $root -Recurse -Force }
}
