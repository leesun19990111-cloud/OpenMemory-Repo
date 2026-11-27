param([string]$BrowserPath = 'C:\Program Files\Google\Chrome\Application\chrome.exe')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$fixtureRoot = Join-Path $projectRoot ('test-output\browser-' + [guid]::NewGuid().ToString('N'))
$profilePath = Join-Path $fixtureRoot 'isolated-profile'
New-Item -ItemType Directory -Path $profilePath -Force | Out-Null
$browserFile = Split-Path $BrowserPath -Leaf
$browserName = [IO.Path]::GetFileNameWithoutExtension($BrowserPath)
$report = New-Object System.Collections.Generic.List[string]
[void][Reflection.Assembly]::LoadFrom((Join-Path $projectRoot 'dist\OpenMemory-v2.3.0\OpenMemory.exe'))
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
function Test-Window {
 return @([OpenMemory.Desktop]::Windows().Files | Where-Object { $_.Kind -eq 'Browser' -and $_.DataDirectory -eq $profilePath })
}
function Test-Processes {
 return @(Get-WmiObject Win32_Process -Filter "Name='$browserFile'" | Where-Object { $_.CommandLine -and $_.CommandLine.Contains($profilePath) })
}
function Test-Tabs {
 $names = New-Object System.Collections.Generic.List[string]
 foreach ($record in Test-Processes) {
  $process = Get-Process -Id $record.ProcessId -ErrorAction SilentlyContinue
  if (!$process -or $process.MainWindowHandle -eq 0) { continue }
  $rootElement = [Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
  $condition = New-Object Windows.Automation.PropertyCondition([Windows.Automation.AutomationElement]::ControlTypeProperty, [Windows.Automation.ControlType]::TabItem)
  $tabs = $rootElement.FindAll([Windows.Automation.TreeScope]::Descendants, $condition)
  foreach ($tab in $tabs) {
   $names.Add($tab.Current.Name)
   # Edge may defer loading a restored background tab until it is selected.
   if ($tab.Current.Name -eq 'beta.html') {
    try { $tab.GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select() } catch {}
   }
  }
 }
 return $names
}
function Close-TestBrowser {
 foreach ($record in Test-Processes) {
  $process = Get-Process -Id $record.ProcessId -ErrorAction SilentlyContinue
  if (!$process) { continue }
  [void]$process.CloseMainWindow()
  if (!$process.WaitForExit(10000)) { $process.Kill(); $process.WaitForExit(3000) | Out-Null }
 }
}
try {
 $alpha = Join-Path $fixtureRoot 'alpha.html'
 $beta = Join-Path $fixtureRoot 'beta.html'
 '<!doctype html><title>OpenMemory Test Alpha</title><p>Disposable browser test A</p>' | Set-Content -LiteralPath $alpha -Encoding UTF8
 '<!doctype html><title>OpenMemory Test Beta</title><p>Disposable browser test B</p>' | Set-Content -LiteralPath $beta -Encoding UTF8
 $args = '--user-data-dir=' + [OpenMemory.Desktop]::Quote($profilePath) + ' --profile-directory=Default --no-first-run --no-default-browser-check --disable-background-mode --disable-background-networking --new-window ' + [OpenMemory.Desktop]::Quote(([uri]$alpha).AbsoluteUri) + ' ' + [OpenMemory.Desktop]::Quote(([uri]$beta).AbsoluteUri)
 $process = [Diagnostics.Process]::Start((New-Object Diagnostics.ProcessStartInfo($browserPath, $args)))
 for ($i=0; $i -lt 30; $i++) { Start-Sleep -Milliseconds 500; if ((Test-Window).Count -gt 0) { break } }
 $entry = @(Test-Window)[0]
 if (!$entry -or $entry.Profile -ne 'Default') { throw 'Browser/profile not captured' }
 $report.Add('PASS: actual ' + $browserName + ' window and isolated profile captured')
 $initialTabs = @(Test-Tabs)
 if (!(($initialTabs -join '|').Contains('OpenMemory Test Alpha')) -or !(($initialTabs -join '|').Contains('OpenMemory Test Beta'))) { throw ('Initial tabs not visible: ' + ($initialTabs -join ', ')) }
 $report.Add('PASS: both local test tabs observed before closing')
 Close-TestBrowser
 if ((Test-Window).Count -ne 0) { throw 'Test browser did not close' }
 $result = [OpenMemory.Engine]::Restore($entry, [Threading.CancellationToken]::None).GetAwaiter().GetResult()
 $report.Add('Restore result: ' + $result)
 if (!$result.Contains('세션 복원 요청')) { throw ('Browser restore failed: ' + $result) }
 $restoredTabs = @()
 for ($i=0; $i -lt 30; $i++) {
  Start-Sleep -Milliseconds 500
  $restoredTabs = @(Test-Tabs)
  if (($restoredTabs -join '|').Contains('OpenMemory Test Alpha') -and ($restoredTabs -join '|').Contains('OpenMemory Test Beta')) { break }
 }
 if (!(($restoredTabs -join '|').Contains('OpenMemory Test Alpha')) -or !(($restoredTabs -join '|').Contains('OpenMemory Test Beta'))) {
  foreach ($window in @([OpenMemory.Desktop]::Windows().Files | Where-Object { $_.Kind -eq 'Browser' })) { $report.Add('Window: ' + $window.Title + ' / profile=' + $window.Profile + ' / data=' + $window.DataDirectory) }
  foreach ($record in Test-Processes) { $proc = Get-Process -Id $record.ProcessId -ErrorAction SilentlyContinue; $report.Add('Process: ' + $record.ProcessId + ' / handle=' + $proc.MainWindowHandle + ' / title=' + $proc.MainWindowTitle) }
  throw ('Restored tabs not found: ' + ($restoredTabs -join ', '))
 }
 $report.Add('PASS: actual ' + $browserName + ' last-session restore reopened both test tabs')
} catch { $report.Add('FAIL: ' + $_.Exception.Message); throw }
finally {
 Close-TestBrowser
 $report | Set-Content -LiteralPath (Join-Path $fixtureRoot 'results.txt') -Encoding UTF8
 $report | Write-Output
 Write-Output ('Artifacts: ' + $fixtureRoot)
}
