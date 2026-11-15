# Runs the backend pytest suite with junit XML output into TestResults\.
# Usage: scripts\backend_tests.ps1 [-Timeout 600] [-K expr] [-Marker expr] [-PerTestTimeout 60] [-Extra "-x"]
# Uses backend\.venv (falls back to the main checkout's venv when run from a git worktree).
# Per-test timeout is applied only if pytest-timeout is installed (otherwise noted and skipped).
# Whole-run hard timeout: pytest process is killed after -Timeout seconds. Exit code mirrors pytest (2 = killed).
# Windows PowerShell 5.1 compatible.
param([int]$Timeout = 600, [string]$K = "", [string]$Marker = "", [int]$PerTestTimeout = 60, [string]$Extra = "", [string]$Python = "")
$root = Split-Path -Parent $PSScriptRoot
$backend = Join-Path $root "backend"
if ($Python -eq "") {
    $cands = @((Join-Path $backend ".venv\Scripts\python.exe"))
    try {
        $common = (& git -C $root rev-parse --git-common-dir 2>$null)
        if ($common) { $cands += (Join-Path (Split-Path -Parent ([System.IO.Path]::GetFullPath([System.IO.Path]::Combine($root, "$common")))) "backend\.venv\Scripts\python.exe") }
    } catch {}
    foreach ($c in $cands) { if (Test-Path $c) { $Python = $c; break } }
}
if ($Python -eq "" -or -not (Test-Path $Python)) { "no backend venv found (create backend\.venv or pass -Python)"; exit 1 }
$resultsDir = Join-Path $root "TestResults"
if (-not (Test-Path $resultsDir)) { New-Item -ItemType Directory -Path $resultsDir | Out-Null }
$stamp = (Get-Date).ToString("yyyyMMdd_HHmmss")
$xml = Join-Path $resultsDir "backend_$stamp.xml"
$a = "-m pytest tests --junitxml=`"$xml`" -p no:cacheprovider"
$hasTimeout = $false
& $Python -c "import pytest_timeout" 2>$null
if ($LASTEXITCODE -eq 0) { $hasTimeout = $true; $a += " --timeout=$PerTestTimeout" } else { "note: pytest-timeout not installed; per-test timeout disabled (whole-run timeout $Timeout s still applies)" }
if ($K -ne "") { $a += " -k `"$K`"" }
if ($Marker -ne "") { $a += " -m `"$Marker`"" }
if ($Extra -ne "") { $a += " $Extra" }
$env:PYTHONPATH = $backend
$psi = New-Object System.Diagnostics.ProcessStartInfo
$psi.FileName = $Python; $psi.Arguments = $a; $psi.WorkingDirectory = $backend
$psi.RedirectStandardOutput = $true; $psi.RedirectStandardError = $true; $psi.UseShellExecute = $false; $psi.CreateNoWindow = $true
$started = Get-Date
$proc = [System.Diagnostics.Process]::Start($psi)
$o = $proc.StandardOutput.ReadToEndAsync(); $e = $proc.StandardError.ReadToEndAsync()
if (-not $proc.WaitForExit($Timeout * 1000)) {
    try { $proc.Kill() } catch {}
    "BACKEND TESTS: TIMEOUT after $Timeout s (killed). junit: $xml (may be missing)"; exit 2
}
$proc.WaitForExit()
$lines = @(($o.Result + "`n" + $e.Result) -split "`r?`n" | Where-Object { $_ -ne "" })
$lines | Select-Object -Last 25
$tot = 0; $fail = 0; $err = 0; $skip = 0
if (Test-Path $xml) {
    [xml]$x = Get-Content $xml
    $s = $x.testsuites.testsuite
    if ($s -eq $null) { $s = $x.testsuite }
    foreach ($ts in @($s)) { $tot += [int]$ts.tests; $fail += [int]$ts.failures; $err += [int]$ts.errors; $skip += [int]$ts.skipped }
}
"BACKEND SUMMARY: tests=$tot failed=$fail errors=$err skipped=$skip secs=$([int]((Get-Date) - $started).TotalSeconds) exit=$($proc.ExitCode) junit=$xml"
exit $proc.ExitCode

