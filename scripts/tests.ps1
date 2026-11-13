# Runs EditMode tests for one filter and prints pass/fail counts plus the failing lines.
# Usage: scripts\tests.ps1 FloorAimTests [-Timeout 150]
# The CLI call has its own hard timer (Timeout + 60 s). If it expires, only the CLI client is stopped
# (never the Editor) and "TIMEOUT" is printed, so a stuck run cannot block the caller forever.
param([string]$Filter = "", [int]$Timeout = 150)
$p = Split-Path -Parent $PSScriptRoot
$cliArgs = "command --project-path `"$p`" test run --mode editor --timeout $Timeout"
if ($Filter -ne "") { $cliArgs += " --filter `"$Filter`"" }
$exe = (Get-Command unity).Source
$psi = New-Object System.Diagnostics.ProcessStartInfo
if ($exe -match '\.ps1$') { $psi.FileName = "powershell"; $psi.Arguments = "-NoProfile -ExecutionPolicy Bypass -File `"$exe`" $cliArgs" }
else { $psi.FileName = $exe; $psi.Arguments = $cliArgs }
$psi.RedirectStandardOutput = $true; $psi.RedirectStandardError = $true
$psi.UseShellExecute = $false; $psi.CreateNoWindow = $true
$proc = [System.Diagnostics.Process]::Start($psi)
$outTask = $proc.StandardOutput.ReadToEndAsync(); $errTask = $proc.StandardError.ReadToEndAsync()
$started = Get-Date
if (-not $proc.WaitForExit(($Timeout + 60) * 1000)) {
    try { $proc.Kill() } catch {}
    "== $Filter : TIMEOUT after $([int]((Get-Date) - $started).TotalSeconds) s (CLI client stopped; Editor left running - check 'unity command editor status')"
    exit 2
}
$lines = @(($outTask.Result + "`n" + $errTask.Result) -split "`r?`n" | Where-Object { $_ -ne "" })
$pass = @($lines | Where-Object { $_ -match "\sPassed\s" }).Count
$fail = @($lines | Where-Object { $_ -match "\sFailed\s" })
"== $Filter : passed=$pass failed=$($fail.Count) lines=$($lines.Count) secs=$([int]((Get-Date) - $started).TotalSeconds)"
$fail | Select-Object -First 12 | ForEach-Object { "  " + $_.Substring(0, [Math]::Min(420, $_.Length)) }
if ($pass -eq 0 -and $fail.Count -eq 0) { $lines | Select-Object -First 3 | ForEach-Object { "  " + $_ } }
