$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$outputRoot = Join-Path $projectRoot 'dist\OpenMemory-v2.3.0'
New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null
$frameworkCompiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$sourceRoot = Join-Path $projectRoot 'src'
$desktopSources = @('AssemblyInfo.cs','Shared.cs','Desktop.cs','Layout.cs','LayoutTests.cs','Startup.cs','Collector.cs','Adobe.cs','Engine.cs','Program.cs','Tests.cs','IntegrationTests.cs') | ForEach-Object { Join-Path $sourceRoot $_ }
$desktopArgs = @('/nologo','/target:winexe','/platform:anycpu','/optimize+','/utf8output',('/out:' + (Join-Path $outputRoot 'OpenMemory.exe')),('/win32manifest:' + (Join-Path $sourceRoot 'app.manifest')),'/r:System.dll','/r:System.Core.dll','/r:System.Xml.dll','/r:System.Xml.Linq.dll','/r:System.Drawing.dll','/r:System.Windows.Forms.dll','/r:System.Management.dll') + $desktopSources
& $frameworkCompiler @desktopArgs
if ($LASTEXITCODE -ne 0) { throw 'Desktop build failed' }
$bridgeSources = @((Join-Path $sourceRoot 'Shared.cs'),(Join-Path $sourceRoot 'RevitBridge.cs'))
$versions = Get-ChildItem -LiteralPath 'C:\Program Files\Autodesk' -Directory -Filter 'Revit 20*' | Where-Object { Test-Path -LiteralPath (Join-Path $_.FullName 'RevitAPI.dll') }
$dotnetRoot = Join-Path $env:ProgramFiles 'dotnet'
$sdk = Get-ChildItem -LiteralPath (Join-Path $dotnetRoot 'sdk') -Directory | Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
foreach ($revit in $versions) {
 $year = [int]($revit.Name -replace 'Revit ','')
 $bridgeOut = Join-Path $outputRoot "RevitBridge\$year"
 New-Item -ItemType Directory -Path $bridgeOut -Force | Out-Null
 $argsForBridge = @('/nologo','/target:library','/optimize+','/utf8output',('/out:' + (Join-Path $bridgeOut 'OpenMemory.Revit.dll')),('/r:' + (Join-Path $revit.FullName 'RevitAPI.dll')),('/r:' + (Join-Path $revit.FullName 'RevitAPIUI.dll')))
 if ($year -lt 2025) {
  & $frameworkCompiler @argsForBridge /r:System.dll /r:System.Core.dll /r:System.Xml.dll @bridgeSources
 } else {
  $runtimeConfig = Get-Content -LiteralPath (Join-Path $revit.FullName 'RevitAPI.runtimeconfig.json') -Raw | ConvertFrom-Json
  $tfmMajor = [int]([regex]::Match($runtimeConfig.runtimeOptions.tfm,'\d+').Value)
  $runtime = Get-ChildItem -LiteralPath (Join-Path $dotnetRoot 'shared\Microsoft.NETCore.App') -Directory | Where-Object { ([version]$_.Name).Major -eq $tfmMajor } | Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
  if (!$runtime) { throw "Missing .NET runtime for Revit $year" }
  $refs = Get-ChildItem -LiteralPath $runtime.FullName -Filter '*.dll' | Where-Object { ($_.Name -like 'System.*' -and $_.Name -notlike '*.Native.dll') -or $_.Name -in @('mscorlib.dll','netstandard.dll','Microsoft.CSharp.dll') } | ForEach-Object { '/r:' + $_.FullName }
  & (Join-Path $dotnetRoot 'dotnet.exe') (Join-Path $sdk.FullName 'Roslyn\bincore\csc.dll') /nostdlib+ @argsForBridge @refs @bridgeSources
 }
 if ($LASTEXITCODE -ne 0) { throw "Revit $year build failed" }
 Write-Output "Built Revit $year bridge"
}
Copy-Item -LiteralPath (Join-Path $projectRoot '사용설명서.txt') -Destination $outputRoot -Force
Write-Output "Built: $outputRoot\OpenMemory.exe"
