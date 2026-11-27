$ErrorActionPreference = 'Stop'
$oldExe = Join-Path $PSScriptRoot 'dist\OpenMemory\OpenMemory.exe'
$newExe = Join-Path $PSScriptRoot 'dist\OpenMemory-v2.3.0\OpenMemory.exe'
[void][Reflection.Assembly]::LoadFrom($newExe)
$blocked = $false
$alreadyRunning = $false
foreach ($process in @(Get-Process OpenMemory -ErrorAction SilentlyContinue)) {
 $path = [OpenMemory.Desktop]::ProcessPath($process.Id)
 if ($path -eq $newExe) { $alreadyRunning = $true; continue }
 if ($path -eq $oldExe) {
  [void]$process.CloseMainWindow()
  if (!$process.WaitForExit(5000)) { Write-Output ('Previous version still busy: ' + $process.Id); $blocked = $true }
  else { Write-Output ('Previous version closed: ' + $process.Id) }
 } else { Write-Output ('Another OpenMemory instance is running: ' + $process.Id); $blocked = $true }
}
if ($blocked) { exit 2 }
if (!$alreadyRunning) {
 $process = Start-Process -FilePath $newExe -ArgumentList '--tray' -WindowStyle Hidden -PassThru
 if ($process.WaitForExit(1500)) { throw ('Tray app exited: ' + $process.ExitCode) }
 Write-Output ('Tray app running: ' + $process.Id)
 Write-Output ('Executable: ' + [OpenMemory.Desktop]::ProcessPath($process.Id))
} else { Write-Output 'New tray version is already running.' }
Write-Output ('Startup enabled: ' + [OpenMemory.Startup]::IsEnabled($newExe))
