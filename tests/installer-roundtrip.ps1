param([switch]$TestBuild)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$folder=Join-Path $root ('test-output\installer-'+[Guid]::NewGuid().ToString('N'))
$target=Join-Path $folder '설치 시험 & 경로'
[IO.Directory]::CreateDirectory($folder)|Out-Null
$setup=Join-Path $root 'release\OpenMemory-Setup-2.3.0-x64.exe'
$run='HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$uninstall='HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{84774D91-0ED8-4D8B-A6E8-7F407C4B2C82}_is1'
if($TestBuild){$setup=Join-Path $root 'test-output\installer-build\OpenMemory-Installer-Test.exe';$uninstall='HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{7DE58F46-61C7-4C63-BF95-33F5191C372D}_is1'}
if(Test-Path -LiteralPath $uninstall){throw 'Existing installed release detected; refusing to replace it during test.'}
$previous=(Get-ItemProperty -LiteralPath $run -Name OpenMemory -ErrorAction SilentlyContinue).OpenMemory
$report=New-Object 'Collections.Generic.List[string]'
$snapshot=@{}
$adobeRoot=Join-Path $env:APPDATA 'Adobe\CEP\extensions\local.openmemory.documents'
$connectionFiles=@('mimetype','index.html','bridge.js','reader.jsx','CSXS\manifest.xml','META-INF\signatures.xml')|ForEach-Object {Join-Path $adobeRoot $_}
$connectionFiles+=@('2021','2023','2024','2026','2027')|ForEach-Object {Join-Path $env:APPDATA ('Autodesk\Revit\Addins\'+$_+'\OpenMemory.addin')}
foreach($file in $connectionFiles){$snapshot[$file]=if([IO.File]::Exists($file)){[IO.File]::ReadAllBytes($file)}else{$null}}
$sessions=@{}
$sessionRoot=Join-Path $env:LOCALAPPDATA 'OpenMemory\sessions'
if(Test-Path -LiteralPath $sessionRoot){foreach($file in Get-ChildItem -LiteralPath $sessionRoot -File | Where-Object {$_.Name -match '^\d{8}-\d{6}-\d{3}-[a-f0-9]{6}\.xml$'}){$sessions[$file.FullName]=(Get-FileHash -LiteralPath $file.FullName).Hash}}
function Run-Installer([string]$Components,[string]$Tasks,[string]$Log){
 $args='/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP- /NOICONS /DIR="'+$target+'" /TYPE=custom /COMPONENTS="'+$Components+'" /TASKS="'+$Tasks+'" /LOG="'+(Join-Path $folder $Log)+'"'
 $p=Start-Process -FilePath $setup -ArgumentList $args -WindowStyle Hidden -PassThru
 if(!$p.WaitForExit(30000) -or $p.ExitCode -ne 0){throw ('Installer failed: '+$p.ExitCode)}
}
try{
 Run-Installer 'core' '' 'install.log'
 $installed=Join-Path $target 'OpenMemory.exe'
 if((Get-FileHash -LiteralPath $installed).Hash -ne (Get-FileHash -LiteralPath (Join-Path $root 'dist\OpenMemory-v2.3.0\OpenMemory.exe')).Hash){throw 'Installed binary differs'}
 $report.Add('PASS: per-user installation into Unicode, spaces and ampersand path')
 if(!(Test-Path -LiteralPath $uninstall)){throw 'Uninstall registration missing'}
 $report.Add('PASS: Windows installed-app registration created')
 $p=Start-Process -FilePath $installed -ArgumentList ('--self-test "'+(Join-Path $folder 'installed-unit')+'"') -WindowStyle Hidden -PassThru
 if(!$p.WaitForExit(30000) -or $p.ExitCode -ne 0){throw 'Installed executable failed self-test'}
 $report.Add('PASS: installed executable passes all unit checks')
 Run-Installer 'core,revit,adobe' 'startup' 'upgrade.log'
 if((Get-ItemPropertyValue -LiteralPath $run -Name OpenMemory) -ne ('"'+$installed+'" --startup')){throw 'Startup registration wrong'}
 $report.Add('PASS: upgrade succeeds and login auto-restore uses installed path')
 foreach($year in @('2021','2023','2024','2026','2027')){$manifest=Join-Path $env:APPDATA ('Autodesk\Revit\Addins\'+$year+'\OpenMemory.addin');[xml]$m=[IO.File]::ReadAllText($manifest);if($m.RevitAddIns.AddIn.Assembly -ne (Join-Path $target ('RevitBridge\'+$year+'\OpenMemory.Revit.dll'))){throw 'Revit manifest path wrong'}}
 $report.Add('PASS: selected Revit connections point to installed DLLs for all five versions')
 foreach($relative in @('mimetype','index.html','bridge.js','reader.jsx','CSXS\manifest.xml','META-INF\signatures.xml')){if((Get-FileHash -LiteralPath (Join-Path $adobeRoot $relative)).Hash -ne (Get-FileHash -LiteralPath (Join-Path $target ('AdobeBridge\'+$relative))).Hash){throw 'Adobe connection payload mismatch'}}
 $report.Add('PASS: selected Adobe connection installs the exact signed payload')
 Run-Installer 'core,revit,adobe' '' 'startup-off.log'
 if((Get-ItemProperty -LiteralPath $run -Name OpenMemory -ErrorAction SilentlyContinue).OpenMemory -eq ('"'+$installed+'" --startup')){throw 'Unchecked startup task remained enabled'}
 $report.Add('PASS: unchecked startup task disables this installation registration')
 Run-Installer 'core,revit,adobe' 'startup' 'startup-on.log'
 $uninstaller=Join-Path $target 'unins000.exe'
 $p=Start-Process -FilePath $uninstaller -ArgumentList ('/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /LOG="'+(Join-Path $folder 'uninstall.log')+'"') -WindowStyle Hidden -PassThru
 if(!$p.WaitForExit(30000) -or $p.ExitCode -ne 0){throw 'Uninstall failed'}
 if((Test-Path -LiteralPath $installed) -or (Test-Path -LiteralPath $uninstall)){throw 'Application or uninstall registration remains'}
 $report.Add('PASS: uninstall removes program files and Windows app registration')
 $after=(Get-ItemProperty -LiteralPath $run -Name OpenMemory -ErrorAction SilentlyContinue).OpenMemory
 if($after -eq ('"'+$installed+'" --startup')){throw 'Startup registration remains'}
 $report.Add('PASS: uninstall removes its own startup registration')
 foreach($file in $sessions.Keys){if(!(Test-Path -LiteralPath $file) -or (Get-FileHash -LiteralPath $file).Hash -ne $sessions[$file]){throw 'Saved session changed'}}
 $report.Add('PASS: existing saved sessions preserved byte-for-byte')
 foreach($year in @('2021','2023','2024','2026','2027')){$file=Join-Path $env:APPDATA ('Autodesk\Revit\Addins\'+$year+'\OpenMemory.addin');if($snapshot[$file] -and ![Linq.Enumerable]::SequenceEqual([byte[]]$snapshot[$file],[IO.File]::ReadAllBytes($file))){throw 'Previous Revit manifest not restored'}}
 $report.Add('PASS: previous Revit connections restored on uninstall')
}catch{$report.Add('FAIL: '+$_.Exception.Message);throw}
finally{
 if($null -ne $previous){Set-ItemProperty -LiteralPath $run -Name OpenMemory -Value $previous}else{Remove-ItemProperty -LiteralPath $run -Name OpenMemory -ErrorAction SilentlyContinue}
 foreach($file in $snapshot.Keys){if($null -ne $snapshot[$file]){[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($file))|Out-Null;[IO.File]::WriteAllBytes($file,$snapshot[$file])}elseif([IO.File]::Exists($file)){Remove-Item -LiteralPath $file}}
 [IO.File]::WriteAllLines((Join-Path $folder 'results.txt'),$report)
 Write-Output $folder
 $report
}
