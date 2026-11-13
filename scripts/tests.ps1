# Runs EditMode tests for one filter and prints pass/fail counts plus the failing lines.
# Usage: scripts\tests.ps1 FloorAimTests [-Timeout 150]
param([string]$Filter = "", [int]$Timeout = 150)
$p = Split-Path -Parent $PSScriptRoot
if ($Filter -ne "") { $o = unity command --project-path $p test run --mode editor --filter $Filter --timeout $Timeout }
else { $o = unity command --project-path $p test run --mode editor --timeout $Timeout }
$lines = @($o)
$pass = @($lines | Where-Object { $_ -match "\sPassed\s" }).Count
$fail = @($lines | Where-Object { $_ -match "\sFailed\s" })
"== $Filter : passed=$pass failed=$($fail.Count) lines=$($lines.Count)"
$fail | Select-Object -First 12 | ForEach-Object { "  " + $_.Substring(0, [Math]::Min(420, $_.Length)) }
if ($pass -eq 0 -and $fail.Count -eq 0) { $lines | Select-Object -First 3 | ForEach-Object { "  " + $_ } }
