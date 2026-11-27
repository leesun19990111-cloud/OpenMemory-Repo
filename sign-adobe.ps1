$ErrorActionPreference='Stop'
$taskRoot=$PSScriptRoot
$signer=Join-Path $taskRoot 'tools\ZXPSignCmd.exe'
$staging=Join-Path ([IO.Path]::GetTempPath()) ('OpenMemory-sign-'+[Guid]::NewGuid().ToString('N'))
$source=Join-Path $staging 'source'
New-Item -ItemType Directory -Path (Join-Path $source 'CSXS') -Force | Out-Null
foreach($file in @('index.html','bridge.js','reader.jsx','CSXS/manifest.xml')){Copy-Item -LiteralPath (Join-Path (Join-Path $taskRoot 'adobe') $file) -Destination (Join-Path $source $file)}
$keyPath=Join-Path $staging 'certificate.p12';$secret=[Guid]::NewGuid().ToString('N')
$zxp=Join-Path $staging 'OpenMemory-Adobe.zxp'
try{
 & $signer -selfSignedCert KR Local OpenMemory OpenMemoryDocuments $secret $keyPath -validityDays 1095
 if($LASTEXITCODE -ne 0){throw 'Certificate generation failed'}
 & $signer -sign $source $zxp $keyPath $secret
 if($LASTEXITCODE -ne 0){throw 'Signing failed'}
 & $signer -verify $zxp
 if($LASTEXITCODE -ne 0){throw 'Adobe signature verification failed'}
 $output=Join-Path $taskRoot 'dist\OpenMemory-v2.3.0'
 $package=Join-Path $output 'AdobeBridge'
 New-Item -ItemType Directory -Path $package -Force | Out-Null
 Copy-Item -LiteralPath $zxp -Destination (Join-Path $output 'OpenMemory-Adobe.zxp') -Force
 Add-Type -AssemblyName System.IO.Compression.FileSystem
 $zip=[IO.Compression.ZipFile]::OpenRead($zxp)
 try{foreach($entry in $zip.Entries){if(!$entry.Name){continue};$dest=[IO.Path]::GetFullPath((Join-Path $package $entry.FullName));if(!$dest.StartsWith($package+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Invalid archive path'};[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($dest))|Out-Null;[IO.Compression.ZipFileExtensions]::ExtractToFile($entry,$dest,$true)}}finally{$zip.Dispose()}
 Write-Output 'Adobe offline signature verified.'
}finally{if(Test-Path -LiteralPath $keyPath){Remove-Item -LiteralPath $keyPath -Force};$secret=$null}