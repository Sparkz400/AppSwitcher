#requires -Version 7.0
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$toolRoot = Join-Path (Split-Path -Parent $PSScriptRoot) 'artifacts/tools/innosetup-7.1.1'
$compilerRoot = Join-Path $toolRoot 'compiler'
$compiler = Join-Path $compilerRoot 'ISCC.exe'
if (Test-Path -LiteralPath $compiler) { return $compiler }
New-Item -ItemType Directory -Path $toolRoot -Force | Out-Null
$download = Join-Path $toolRoot 'innosetup-7.1.1-x64.exe'
if (-not (Test-Path -LiteralPath $download)) {
    Invoke-WebRequest -Uri 'https://github.com/jrsoftware/issrc/releases/download/is-7_1_1/innosetup-7.1.1-x64.exe' -OutFile $download
}
if ((Get-FileHash -LiteralPath $download -Algorithm SHA256).Hash -ne '8A5A6711C3766A6E5FB74F280BA0E3551E719CE94538D901FDBD1F8F9920B3F0') {
    throw 'Inno Setup download checksum does not match the pinned official release.'
}
$signature = Get-AuthenticodeSignature -LiteralPath $download
if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notlike '*Pyrsys B.V.*') {
    throw 'Inno Setup publisher signature could not be verified.'
}
$arguments = '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CURRENTUSER /PORTABLE=1 /NOICONS /DIR="' + $compilerRoot + '" /LOG="' + (Join-Path $toolRoot 'portable-install.log') + '"'
$process = Start-Process -FilePath $download -ArgumentList $arguments -WindowStyle Hidden -PassThru
if (-not $process.WaitForExit(120000)) { $process.Kill($true); throw 'Portable compiler setup timed out.' }
if ($process.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $compiler)) { throw "Portable compiler setup failed: $($process.ExitCode)." }
return $compiler
