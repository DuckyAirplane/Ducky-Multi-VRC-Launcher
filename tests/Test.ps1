$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$app = Join-Path $root 'Ducky Multi VRC Launcher.exe'
if (-not (Test-Path -LiteralPath $app)) { throw 'Run Build.ps1 first.' }
$result = Join-Path $root 'self-test-results.txt'
if (Test-Path -LiteralPath $result) { Remove-Item -LiteralPath $result }
$process = Start-Process -FilePath $app -ArgumentList '--self-test' -WindowStyle Hidden -Wait -PassThru
if ($process.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $result)) { throw 'Launcher regression tests failed.' }
Get-Content -LiteralPath $result
Remove-Item -LiteralPath $result
