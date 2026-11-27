$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$probe = Join-Path $projectRoot 'test-output\cleanup-capture.xml'
$exePath = Join-Path $projectRoot 'dist\OpenMemory-v2.3.0\OpenMemory.exe'
$worker = Start-Process -FilePath $exePath -ArgumentList ('--collect Excel "' + $probe + '"') -WindowStyle Hidden -PassThru
if (!$worker.WaitForExit(25000)) { $worker.Kill(); throw 'Capture timeout' }
[xml]$capture = Get-Content -LiteralPath $probe -Raw -Encoding UTF8
$prefix = (Join-Path $projectRoot 'test-output\excel-')
foreach ($entry in $capture.Session.Files.Entry) {
 if (!$entry.Path.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) { continue }
 $book = [Runtime.InteropServices.Marshal]::BindToMoniker([string]$entry.Path)
 $app = $book.Application
 $book.Close($false)
 Write-Output ('Closed fixture: ' + $entry.Path)
 if ($app.Workbooks.Count -eq 0) { $app.Quit() }
 [void][Runtime.InteropServices.Marshal]::ReleaseComObject($book)
 [void][Runtime.InteropServices.Marshal]::ReleaseComObject($app)
}
[GC]::Collect()
[GC]::WaitForPendingFinalizers()
