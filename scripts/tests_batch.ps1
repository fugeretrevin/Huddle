# Batch runner for the Unity EditMode suite: one fixture at a time (same CLI mechanism as scripts\tests.ps1),
# grouped in batches, per-fixture hard timeout, resumable, machine-readable results.
#
# Usage (see docs/Engineering/TESTING.md):
#   scripts\tests_batch.ps1 -List                       # print discovered fixtures + groups, run nothing
#   scripts\tests_batch.ps1 -Group board                # run one group from scripts\test_catalog.json
#   scripts\tests_batch.ps1 -Only MotionTests,PresenceTests
#   scripts\tests_batch.ps1 -From FloorAimTests         # resume the sorted full list from this fixture
#   scripts\tests_batch.ps1 -UpdateCatalog              # regenerate fixture lists in scripts\test_catalog.json
#   scripts\tests_batch.ps1 -BatchSize 8 -FixtureTimeout 120 -MinFreeMemMB 1500
#
# Results: TestResults\editmode_<timestamp>.json (and .txt). Exit code 0 = all passed, 1 = failures/timeouts/errors.
# Requires the `unity` CLI on PATH and an open Editor for -Project (same as tests.ps1). Never starts/stops the Editor.
# Windows PowerShell 5.1 compatible.
param(
    [string]$Group = "",
    [string[]]$Only = @(),
    [string]$From = "",
    [int]$BatchSize = 10,
    [int]$FixtureTimeout = 150,      # passed to `unity ... test run --timeout`; hard kill of the CLI client is this + 60 s
    [int]$PauseBetweenBatches = 5,   # seconds, lets the Editor settle / GC
    [int]$MinFreeMemMB = 0,          # >0: before each fixture wait (up to 5 min) until free RAM >= this, else record status=lowmem and stop
    [scriptblock]$BetweenBatches = $null,  # optional hook run after every batch, e.g. { Write-Host 'restart the Editor now' }
    [switch]$FullNames,              # filter by Namespace.Class instead of the bare class name
    [switch]$StopOnTimeout,
    [switch]$List,
    [switch]$UpdateCatalog,
    [string]$ProjectRoot = ""        # defaults to the parent of scripts\
)
$ErrorActionPreference = "Stop"
if ($ProjectRoot -eq "") { $ProjectRoot = Split-Path -Parent $PSScriptRoot }
$testRoot = Join-Path $ProjectRoot "Assets\MuseDesk\Tests\EditMode"
$catalogPath = Join-Path $PSScriptRoot "test_catalog.json"

# ---- discovery: every class whose body contains [Test]/[TestCase]/[UnityTest] ------------------------------
function Get-Fixtures {
    $result = @()
    if (-not (Test-Path $testRoot)) { return $result }
    foreach ($f in Get-ChildItem -Path $testRoot -Recurse -Filter *.cs) {
        $text = [System.IO.File]::ReadAllText($f.FullName)
        $ns = ""
        $dormant = ($text -match '(?m)^#if\s+MUSEDESK_TESTS_RUNTIME_REF')
        $nm = [regex]::Match($text, '(?m)^\s*namespace\s+([\w\.]+)')
        if ($nm.Success) { $ns = $nm.Groups[1].Value }
        $classRx = [regex]'(?m)^([ \t]*)(?:\[[^\]]*\][ \t]*)*(?:public\s+|internal\s+)?(?:static\s+|sealed\s+|abstract\s+|partial\s+)*class\s+(\w+)'
        $all = $classRx.Matches($text)
        if ($all.Count -eq 0) { continue }
        $minIndent = ($all | ForEach-Object { $_.Groups[1].Value.Length } | Measure-Object -Minimum).Minimum
        $ms = @($all | Where-Object { $_.Groups[1].Value.Length -eq $minIndent })
        for ($i = 0; $i -lt $ms.Count; $i++) {
            $start = $ms[$i].Index
            $end = $text.Length
            if ($i + 1 -lt $ms.Count) { $end = $ms[$i + 1].Index }
            $body = $text.Substring($start, $end - $start)
            if ($body -match '\[\s*(Test|TestCase|TestCaseSource|UnityTest)\b') {
                $name = $ms[$i].Groups[2].Value
                $full = $name
                if ($ns -ne "") { $full = "$ns.$name" }
                $rel = $f.FullName.Substring($testRoot.Length).TrimStart('\')
                $result += [pscustomobject]@{ name = $name; full = $full; file = $rel.Replace('\', '/'); dormant = $dormant }
            }
        }
    }
    return ($result | Sort-Object name -Unique)
}

# ---- group assignment: regex rules from the catalog, first match wins; fallback "core" ---------------------
function Get-GroupOf($fx, $rules) {
    if ($fx.dormant) { return "dormant" }   # compiled out until MuseDesk.Runtime asmdef exists; see TESTING.md
    foreach ($r in $rules) {
        if ($fx.file -match $r.fileRegex -or $fx.name -match $r.nameRegex) { return $r.group }
    }
    return "core"
}

$fixtures = @(Get-Fixtures)
$rules = @()
$catalog = $null
if (Test-Path $catalogPath) {
    $catalog = Get-Content $catalogPath -Raw | ConvertFrom-Json
    $rules = @($catalog.rules)
}
foreach ($fx in $fixtures) { $fx | Add-Member -NotePropertyName group -NotePropertyValue (Get-GroupOf $fx $rules) -Force }

if ($UpdateCatalog) {
    $groups = [ordered]@{}
    foreach ($g in ($fixtures | Select-Object -ExpandProperty group -Unique | Sort-Object)) {
        $groups[$g] = @($fixtures | Where-Object { $_.group -eq $g } | Select-Object -ExpandProperty name)
    }
    $out = [ordered]@{
        generated = (Get-Date).ToString("s")
        note      = "rules: first match on file path regex OR class name regex wins; unmatched = core. fixtures lists are a generated snapshot; tests_batch.ps1 re-discovers from source at run time."
        rules     = $rules
        fixtures  = $groups
    }
    ($out | ConvertTo-Json -Depth 6) | Set-Content -Path $catalogPath -Encoding UTF8
    "catalog updated: $catalogPath ($($fixtures.Count) fixtures)"
    exit 0
}

if ($List) {
    $fixtures | Sort-Object group, name | ForEach-Object { "{0,-12} {1,-45} {2}" -f $_.group, $_.name, $_.file }
    "total fixtures: $($fixtures.Count)"
    exit 0
}

# ---- selection -------------------------------------------------------------------------------------------
$sel = @($fixtures | Where-Object { $_.group -ne "dormant" } | Sort-Object name)
if ($Group -ne "") {
    $sel = @($fixtures | Sort-Object name)
    $sel = @($sel | Where-Object { $_.group -eq $Group })
    if ($sel.Count -eq 0) { "no fixtures in group '$Group'. Groups: " + (($fixtures | Select-Object -ExpandProperty group -Unique | Sort-Object) -join ", "); exit 1 }
}
if ($Only.Count -gt 0) {
    $want = @($Only | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ -ne "" })
    $sel = @($fixtures | Where-Object { $want -contains $_.name } | Sort-Object name)
    $missing = @($want | Where-Object { $n = $_; -not ($fixtures | Where-Object { $_.name -eq $n }) })
    if ($missing.Count -gt 0) { "warning: not discovered: " + ($missing -join ", ") }
}
if ($From -ne "") {
    $idx = -1
    for ($i = 0; $i -lt $sel.Count; $i++) { if ($sel[$i].name -eq $From) { $idx = $i; break } }
    if ($idx -lt 0) { "-From fixture '$From' not in the selection"; exit 1 }
    $sel = @($sel[$idx..($sel.Count - 1)])
}
if ($sel.Count -eq 0) { "nothing to run"; exit 1 }

# ---- one-fixture runner (same CLI mechanism as scripts\tests.ps1) ----------------------------------------
function Get-FreeMemMB {
    try { return [int]((Get-CimInstance Win32_OperatingSystem).FreePhysicalMemory / 1024) } catch { return 999999 }
}

function Invoke-Fixture([string]$filter, [int]$timeout) {
    $cliArgs = "command --project-path `"$ProjectRoot`" test run --mode editor --timeout $timeout --filter `"$filter`""
    $exe = (Get-Command unity).Source
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    if ($exe -match '\.ps1$') { $psi.FileName = "powershell"; $psi.Arguments = "-NoProfile -ExecutionPolicy Bypass -File `"$exe`" $cliArgs" }
    else { $psi.FileName = $exe; $psi.Arguments = $cliArgs }
    $psi.RedirectStandardOutput = $true; $psi.RedirectStandardError = $true
    $psi.UseShellExecute = $false; $psi.CreateNoWindow = $true
    $started = Get-Date
    $r = [ordered]@{ passed = 0; failed = 0; skipped = 0; seconds = 0; status = "ok"; failures = @(); note = "" }
    try {
        $proc = [System.Diagnostics.Process]::Start($psi)
        $outTask = $proc.StandardOutput.ReadToEndAsync(); $errTask = $proc.StandardError.ReadToEndAsync()
        if (-not $proc.WaitForExit(($timeout + 60) * 1000)) {
            try { $proc.Kill() } catch {}
            $r.status = "timeout"
        } else {
            $lines = @(($outTask.Result + "`n" + $errTask.Result) -split "`r?`n" | Where-Object { $_ -ne "" })
            $r.passed = @($lines | Where-Object { $_ -match "\sPassed\s" }).Count
            $failLines = @($lines | Where-Object { $_ -match "\sFailed\s" })
            $r.failed = $failLines.Count
            $r.skipped = @($lines | Where-Object { $_ -match "\s(Skipped|Ignored|Inconclusive)\s" }).Count
            $r.failures = @($failLines | Select-Object -First 12 | ForEach-Object { $_.Substring(0, [Math]::Min(420, $_.Length)) })
            if ($r.failed -gt 0) { $r.status = "failed" }
            elseif ($r.passed -eq 0 -and $r.skipped -eq 0) {
                $r.status = "error"   # no result lines at all: filter matched nothing, compile error, or CLI/Editor problem
                $r.note = (($lines | Select-Object -First 3) -join " | ")
            }
        }
    } catch { $r.status = "error"; $r.note = $_.Exception.Message }
    $r.seconds = [int]((Get-Date) - $started).TotalSeconds
    return $r
}

# ---- run -------------------------------------------------------------------------------------------------
$resultsDir = Join-Path $ProjectRoot "TestResults"
if (-not (Test-Path $resultsDir)) { New-Item -ItemType Directory -Path $resultsDir | Out-Null }
$stamp = (Get-Date).ToString("yyyyMMdd_HHmmss")
$jsonPath = Join-Path $resultsDir "editmode_$stamp.json"
$results = @()
$runStart = Get-Date
$stop = $false
$n = 0
"== running $($sel.Count) fixture(s) in batches of $BatchSize (timeout $FixtureTimeout s each) -> $jsonPath"

for ($b = 0; $b -lt $sel.Count -and -not $stop; $b += $BatchSize) {
    $batch = @($sel[$b..([Math]::Min($b + $BatchSize, $sel.Count) - 1)])
    "-- batch $([int]($b / $BatchSize) + 1): " + (($batch | ForEach-Object { $_.name }) -join ", ")
    foreach ($fx in $batch) {
        $n++
        if ($MinFreeMemMB -gt 0) {
            $waited = 0
            while ((Get-FreeMemMB) -lt $MinFreeMemMB -and $waited -lt 300) { Start-Sleep -Seconds 10; $waited += 10 }
            if ((Get-FreeMemMB) -lt $MinFreeMemMB) {
                $results += [pscustomobject]@{ fixture = $fx.name; group = $fx.group; passed = 0; failed = 0; skipped = 0; duration = 0; status = "lowmem"; failures = @(); note = "free RAM below $MinFreeMemMB MB; resume with -From $($fx.name)" }
                "!! low memory, stopping. Resume with: scripts\tests_batch.ps1 -From $($fx.name)"
                $stop = $true; break
            }
        }
        $filter = $fx.name
        if ($FullNames) { $filter = $fx.full }
        $r = Invoke-Fixture $filter $FixtureTimeout
        $results += [pscustomobject]@{ fixture = $fx.name; group = $fx.group; passed = $r.passed; failed = $r.failed; skipped = $r.skipped; duration = $r.seconds; status = $r.status; failures = $r.failures; note = $r.note }
        "[{0}/{1}] {2,-9} {3,-45} passed={4} failed={5} skipped={6} {7}s {8}" -f $n, $sel.Count, $r.status.ToUpper(), $fx.name, $r.passed, $r.failed, $r.skipped, $r.seconds, $r.note
        # incremental save so a crash / hang loses nothing
        ($results | ConvertTo-Json -Depth 5) | Set-Content -Path $jsonPath -Encoding UTF8
        if ($r.status -eq "timeout" -and $StopOnTimeout) { "!! timeout, stopping (resume with -From $($fx.name))"; $stop = $true; break }
    }
    if (-not $stop -and ($b + $BatchSize) -lt $sel.Count) {
        if ($BetweenBatches -ne $null) { & $BetweenBatches }
        if ($PauseBetweenBatches -gt 0) { Start-Sleep -Seconds $PauseBetweenBatches }
    }
}

($results | ConvertTo-Json -Depth 5) | Set-Content -Path $jsonPath -Encoding UTF8
$tp = ($results | Measure-Object passed -Sum).Sum; $tf = ($results | Measure-Object failed -Sum).Sum; $ts = ($results | Measure-Object skipped -Sum).Sum
$bad = @($results | Where-Object { $_.status -ne "ok" })
$summary = "SUMMARY: fixtures=$($results.Count)/$($sel.Count) passed=$tp failed=$tf skipped=$ts timeouts=$(@($results | Where-Object { $_.status -eq 'timeout' }).Count) errors=$(@($results | Where-Object { $_.status -eq 'error' }).Count) secs=$([int]((Get-Date) - $runStart).TotalSeconds) results=$jsonPath"
foreach ($x in $bad) { "  not ok: $($x.fixture) [$($x.status)]" }
$summary
$summary | Set-Content -Path ([System.IO.Path]::ChangeExtension($jsonPath, ".txt")) -Encoding UTF8
if ($bad.Count -gt 0 -or $stop) { exit 1 }
exit 0


