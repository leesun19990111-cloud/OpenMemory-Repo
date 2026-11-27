param([switch]$PackageOnly)
$ErrorActionPreference='Stop'
$taskRoot=$PSScriptRoot
if(!$PackageOnly){
 & (Join-Path $taskRoot 'build.ps1')
 & (Join-Path $taskRoot 'sign-adobe.ps1')
}
$payload=Join-Path $taskRoot 'dist\OpenMemory-v2.3.0'
Copy-Item -LiteralPath (Join-Path $taskRoot '검증결과.md') -Destination $payload -Force
$compiler=Join-Path $taskRoot 'tools\InnoSetup643\ISCC.exe'
if(!(Test-Path -LiteralPath $compiler)){throw 'Inno Setup 6.4.3 compiler is missing.'}
& $compiler /Q (Join-Path $taskRoot 'installer\OpenMemory.iss')
if($LASTEXITCODE -ne 0){throw 'Installer build failed'}
$release=Join-Path $taskRoot 'release'
$setup=Join-Path $release 'OpenMemory-Setup-2.3.0-x64.exe'
$hash=Get-FileHash -LiteralPath $setup -Algorithm SHA256
[IO.File]::WriteAllText((Join-Path $release 'OpenMemory-Setup-2.3.0-x64.sha256'),($hash.Hash+'  '+[IO.Path]::GetFileName($setup)+[Environment]::NewLine),(New-Object Text.UTF8Encoding($false)))
Copy-Item -LiteralPath (Join-Path $taskRoot '사용설명서.txt') -Destination (Join-Path $release 'OpenMemory-2.3.0-사용설명서.txt') -Force
Copy-Item -LiteralPath (Join-Path $taskRoot '검증결과.md') -Destination (Join-Path $release 'OpenMemory-2.3.0-검증결과.md') -Force
Write-Output ('Release: '+$setup)
Write-Output ('SHA256: '+$hash.Hash)
