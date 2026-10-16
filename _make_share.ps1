$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$exe = Join-Path $root "publish\AutoInputTester.exe"
if (-not (Test-Path $exe)) { throw "publish\AutoInputTester.exe fehlt" }

$out = Join-Path $env:USERPROFILE "Desktop\AutoInputTester_FERTIG"
$zip = Join-Path $env:USERPROFILE "Desktop\AutoInputTester_FERTIG.zip"

if (Test-Path $out) { Remove-Item $out -Recurse -Force }
New-Item -ItemType Directory -Path $out | Out-Null

Copy-Item $exe (Join-Path $out "AutoInputTester.exe")
Copy-Item (Join-Path $root "starten-edge-typewriter.bat") $out
Copy-Item (Join-Path $root "starten-chrome-typewriter.bat") $out
Copy-Item (Join-Path $root "SO-STARTEN.txt") $out

if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $out "*") -DestinationPath $zip -Force

Write-Host "OK Ordner: $out"
Write-Host "OK ZIP:    $zip"
Get-ChildItem $out | Format-Table Name, Length -AutoSize
