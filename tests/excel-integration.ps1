$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$exePath = Join-Path $projectRoot 'dist\OpenMemory-v2.3.0\OpenMemory.exe'
$fixtureRoot = Join-Path $projectRoot ('test-output\excel-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixtureRoot -Force | Out-Null
$excel = $null
$books = New-Object System.Collections.ArrayList
$restoredBooks = New-Object System.Collections.ArrayList
$restoredApps = New-Object System.Collections.ArrayList
$report = New-Object System.Collections.Generic.List[string]
$beforeHashes = @{}
function Get-FixtureHash([string]$Path) {
 $stream = [IO.File]::Open($Path, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::ReadWrite)
 $sha = [Security.Cryptography.SHA256]::Create()
 try { return ([BitConverter]::ToString($sha.ComputeHash($stream))).Replace('-','') } finally { $sha.Dispose(); $stream.Dispose() }
}
try {
 $excel = New-Object -ComObject Excel.Application
 $excel.Visible = $true
 $excel.DisplayAlerts = $false
 $suffix = [guid]::NewGuid().ToString('N').Substring(0,8)
 $expectedPaths = @((Join-Path $fixtureRoot ('설계 일정-' + $suffix + '.xlsx')), (Join-Path $fixtureRoot ('면적 & 수량-' + $suffix + '.xlsx')))
 foreach ($filePath in $expectedPaths) {
  $book = $excel.Workbooks.Add()
  [void]$books.Add($book)
  $book.Worksheets.Item(1).Cells.Item(1,1) = 'OpenMemory disposable integration fixture'
  $book.SaveAs($filePath, 51)
  $beforeHashes[$filePath] = Get-FixtureHash $filePath
 }
 $unsaved = $excel.Workbooks.Add()
 [void]$books.Add($unsaved)
 $outputPath = Join-Path $fixtureRoot 'capture.xml'
 $worker = Start-Process -FilePath $exePath -ArgumentList ('--collect Excel "' + $outputPath + '"') -WindowStyle Hidden -PassThru
 if (!$worker.WaitForExit(25000)) { $worker.Kill(); throw 'Collector timeout' }
 if ($worker.ExitCode -ne 0) { throw 'Collector failed' }
 [xml]$capture = Get-Content -LiteralPath $outputPath -Raw -Encoding UTF8
 $actualPaths = @($capture.Session.Files.Entry | ForEach-Object { $_.Path })
 foreach ($filePath in $expectedPaths) {
  if ($actualPaths -notcontains $filePath) { throw "Saved workbook missing: $filePath" }
 }
 $report.Add('PASS: two saved workbooks captured through real Excel windows')
 if (@($actualPaths | Where-Object { $_ -like '*통합 문서*' -or $_ -like '*Book*' }).Count -gt 0) { throw 'Unsaved workbook included as a file' }
 if (@($capture.Session.Warnings.string).Count -eq 0) { throw 'Unsaved workbook warning missing' }
 $report.Add('PASS: unsaved workbook excluded with a warning')
 $excel.Workbooks.Item(1).Worksheets.Item(1).Cells.Item(2,1) = 'unsaved edit'
 $output2 = Join-Path $fixtureRoot 'modified.xml'
 $worker = Start-Process -FilePath $exePath -ArgumentList ('--collect Excel "' + $output2 + '"') -WindowStyle Hidden -PassThru
 if (!$worker.WaitForExit(25000)) { $worker.Kill(); throw 'Second collector timeout' }
 [xml]$modified = Get-Content -LiteralPath $output2 -Raw -Encoding UTF8
 $entry = @($modified.Session.Files.Entry | Where-Object { $_.Path -eq $expectedPaths[0] })[0]
 if (!$entry.Note) { throw 'Modified workbook note missing' }
 if ($excel.Workbooks.Item(1).Saved) { throw 'Collector unexpectedly saved the workbook' }
 $report.Add('PASS: unsaved edits flagged without saving or modifying the document')
 [void][Reflection.Assembly]::LoadFrom($exePath)
 $readSession = [OpenMemory.Data].GetMethod('Read').MakeGenericMethod([OpenMemory.Session])
 $writeSession = [OpenMemory.Data].GetMethod('Write').MakeGenericMethod([OpenMemory.Session])
 $session = $readSession.Invoke($null, [object[]]@([string]$outputPath))
 $onlyFixtures = New-Object 'System.Collections.Generic.List[OpenMemory.Entry]'
 foreach ($entry in $session.Files) { if ($expectedPaths -contains $entry.Path) { $onlyFixtures.Add($entry) } }
 $session.Files = $onlyFixtures
 $savedPath = Join-Path $fixtureRoot 'saved-session.xml'
 [void]$writeSession.Invoke($null, [object[]]@([string]$savedPath, $session.PSObject.BaseObject))
 $reloaded = $readSession.Invoke($null, [object[]]@([string]$savedPath))
 $books[0].Close($false)
 $books[1].Close($false)
 foreach ($entry in $reloaded.Files) {
  $result = [OpenMemory.Engine]::Restore($entry, [Threading.CancellationToken]::None).GetAwaiter().GetResult()
  if ($result -notlike '*열기 요청 전달*') { throw ('Restore request failed: ' + $result) }
 }
 $restoredPaths = @()
 for ($attempt = 0; $attempt -lt 5; $attempt++) {
  Start-Sleep -Milliseconds 500
  $restoredOutput = Join-Path $fixtureRoot 'restored.xml'
  $worker = Start-Process -FilePath $exePath -ArgumentList ('--collect Excel "' + $restoredOutput + '"') -WindowStyle Hidden -PassThru
  if (!$worker.WaitForExit(25000)) { $worker.Kill(); throw 'Restore verification timeout' }
  [xml]$restored = Get-Content -LiteralPath $restoredOutput -Raw -Encoding UTF8
  $restoredPaths = @($restored.Session.Files.Entry | ForEach-Object { $_.Path })
  if (@($expectedPaths | Where-Object { $restoredPaths -notcontains $_ }).Count -eq 0) { break }
 }
 if (@($expectedPaths | Where-Object { $restoredPaths -notcontains $_ }).Count -ne 0) { throw 'Reopened test documents not found in Excel' }
 foreach ($filePath in $expectedPaths) {
  $restoredBook = [Runtime.InteropServices.Marshal]::BindToMoniker($filePath)
  [void]$restoredBooks.Add($restoredBook)
  [void]$restoredApps.Add($restoredBook.Application)
  if ($restoredBook.Worksheets.Item(1).Cells.Item(1,1).Value2 -ne 'OpenMemory disposable integration fixture') { throw 'Saved content did not survive reopening' }
  if ($restoredBook.Worksheets.Item(1).Cells.Item(2,1).Value2) { throw 'Unsaved edits were incorrectly persisted' }
 }
 $report.Add('PASS: saved session reloaded, both closed files reopened, saved content verified')
 $hashRows = @()
 foreach ($filePath in $expectedPaths) {
  $afterHash = Get-FixtureHash $filePath
  if ($afterHash -ne $beforeHashes[$filePath]) { throw ('File bytes changed after restore: ' + $filePath) }
  $hashRows += [pscustomobject]@{Path=$filePath;BeforeSHA256=$beforeHashes[$filePath];AfterSHA256=$afterHash;Equal=$true}
 }
 $hashRows | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $fixtureRoot 'integrity.json') -Encoding UTF8
 $report.Add('PASS: both Excel files have identical SHA-256 before capture and after actual restore')
} catch {
 $report.Add('FAIL: ' + $_.Exception.Message)
 throw
} finally {
 foreach ($book in $restoredBooks) { try { $book.Close($false) } catch {}; [void][Runtime.InteropServices.Marshal]::ReleaseComObject($book) }
 foreach ($app in $restoredApps) { try { if ($app.Workbooks.Count -eq 0) { $app.Quit() } } catch {}; try { [void][Runtime.InteropServices.Marshal]::ReleaseComObject($app) } catch {} }
 foreach ($book in $books) { try { $book.Close($false) } catch {}; [void][Runtime.InteropServices.Marshal]::ReleaseComObject($book) }
 if ($null -ne $excel) { $excel.Quit(); [void][Runtime.InteropServices.Marshal]::ReleaseComObject($excel) }
 [GC]::Collect()
 [GC]::WaitForPendingFinalizers()
 $report | Set-Content -LiteralPath (Join-Path $fixtureRoot 'results.txt') -Encoding UTF8
 $report | Write-Output
 Write-Output ('Artifacts: ' + $fixtureRoot)
}
