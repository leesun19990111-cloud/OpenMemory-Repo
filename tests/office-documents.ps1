param([ValidateSet('PowerPoint','Word')][string]$Kind='PowerPoint')
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$exe=Join-Path $root 'dist\OpenMemory-v2.3.0\OpenMemory.exe'
$folder=Join-Path $root ('test-output\'+$Kind+'-'+[Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($folder)|Out-Null
$app=$null;$created=New-Object Collections.ArrayList;$reopened=New-Object Collections.ArrayList
$report=New-Object 'Collections.Generic.List[string]'
$hashes=@{}
function Hash([string]$file){$s=[IO.File]::Open($file,'Open','Read','ReadWrite');$h=[Security.Cryptography.SHA256]::Create();try{return [BitConverter]::ToString($h.ComputeHash($s)).Replace('-','')}finally{$h.Dispose();$s.Dispose()}}
function Capture([string]$name){$output=Join-Path $folder $name;$p=Start-Process -FilePath $exe -ArgumentList ('--collect '+$Kind+' "'+$output+'"') -WindowStyle Hidden -PassThru;if(!$p.WaitForExit(25000) -or $p.ExitCode -ne 0){throw 'Capture failed'};return [xml][IO.File]::ReadAllText($output)}
function CloseFixture($doc){if($Kind -eq 'PowerPoint'){$doc.Close()}else{$doc.Close(0)}}
try{
 $app=New-Object -ComObject ($Kind+'.Application');$app.Visible= if($Kind -eq 'PowerPoint'){-1}else{$true}
 $extension=if($Kind -eq 'PowerPoint'){'.pptx'}else{'.docx'}
 $paths=@((Join-Path $folder ('검증 A'+$extension)),(Join-Path $folder ('검증 B & C'+$extension)))
 foreach($path in $paths){
  if($Kind -eq 'PowerPoint'){$doc=$app.Presentations.Add();[void]$doc.Slides.Add(1,12);$doc.SaveAs($path,24)}
  else{$doc=$app.Documents.Add();$doc.Content.Text='OpenMemory disposable fixture';$doc.SaveAs2($path,16)}
  [void]$created.Add($doc);$hashes[$path]=Hash $path
 }
 $capture=Capture 'capture.xml';$actual=@($capture.Session.Files.Entry|ForEach-Object {$_.Path})
 foreach($path in $paths){if($actual -notcontains $path){throw ('Document not captured: '+$path)}}
 $report.Add('PASS: both open '+$Kind+' documents captured by full path')
 foreach($doc in $created){CloseFixture $doc};$created.Clear()
 [void][Reflection.Assembly]::LoadFrom($exe)
 foreach($path in $paths){$entry=New-Object OpenMemory.Entry;$entry.Path=$path;$entry.App=$Kind;$message=[OpenMemory.Engine]::Restore($entry,[Threading.CancellationToken]::None).GetAwaiter().GetResult();if($message -notmatch '열기 요청'){throw $message}}
 $watch=[Diagnostics.Stopwatch]::StartNew();$found=@()
 while($watch.ElapsedMilliseconds -lt 20000){Start-Sleep -Milliseconds 500;$current=Capture 'restored.xml';$found=@($current.Session.Files.Entry|ForEach-Object {$_.Path});if(@($paths|Where-Object {$found -notcontains $_}).Count -eq 0){break}}
 foreach($path in $paths){if($found -notcontains $path){throw ('Document not reopened: '+$path)}}
 $report.Add('PASS: both closed '+$Kind+' documents reopened through production restore code')
 foreach($path in $paths){if((Hash $path) -ne $hashes[$path]){throw ('Original bytes changed: '+$path)}}
 $report.Add('PASS: SHA-256 unchanged for both '+$Kind+' documents')
 $hashes|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $folder 'sha256.json') -Encoding UTF8
}catch{$report.Add('FAIL: '+$_.Exception.Message);throw}
finally{
 if($app){
  try{$docs=if($Kind -eq 'PowerPoint'){$app.Presentations}else{$app.Documents};for($i=$docs.Count;$i -ge 1;$i--){$doc=$docs.Item($i);if($paths -contains $doc.FullName){CloseFixture $doc}}}catch{}
  [void][Runtime.InteropServices.Marshal]::ReleaseComObject($app)
 }
 [IO.File]::WriteAllLines((Join-Path $folder 'results.txt'),$report)
 Write-Output $folder
 $report
}
