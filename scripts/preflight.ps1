# Final release preflight: every automated group, one ledger.
#   scripts\preflight.ps1                 # all offline groups (stop the live backend first: the app goes live mid-run otherwise)
#   scripts\preflight.ps1 -Only spatial   # one group (compile, editmode, backend, drives, spatial, portal, amend, desk)
# Writes Logs\preflight\<commit>_<yyyyMMdd_HHmm>.md: group, exact command, build/commit, PASS/FAIL, seconds, console errors, detail.
# Evidence level of every Play group: PLAY (Unity Editor, synthetic room, simulated hands). Never WORN.
param([string[]]$Only = @())
$ErrorActionPreference = "Continue"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root
$commit = (git rev-parse --short HEAD).Trim()
$label = (Select-String -Path Assets\MuseDesk\Resources\ReleaseConfig.asset -Pattern 'buildLabel: (\S+)').Matches[0].Groups[1].Value
$outDir = Join-Path $root "Logs\preflight"; New-Item -ItemType Directory -Force $outDir | Out-Null
$out = Join-Path $outDir ("{0}_{1}.md" -f $commit, (Get-Date -Format yyyyMMdd_HHmm))
$rows = New-Object System.Collections.ArrayList
$script:blocked = $false

function Want($g) { return $Only.Count -eq 0 -or $Only -contains $g }
function U($cls, $entry, $argJson = $null) {
    # Right after Play starts the Pipeline server can be mid domain-reload: retry instead of losing the step.
    for ($i = 0; $i -lt 10; $i++) {
        if ($argJson) { $o = (unity command script run --file "AgentScripts/$cls.cs" --entry "$cls.$entry" --args $argJson 2>&1) -join "`n" }
        else { $o = (unity command script run --file "AgentScripts/$cls.cs" --entry "$cls.$entry" 2>&1) -join "`n" }
        if ($o -notmatch "isn't reachable|No Pipeline instance") { break }
        Start-Sleep 3
    }
    return ($o -replace '(?s)diagnostics:.*$', '').Trim()
}
function Play([int]$settle = 12) {
    unity command editor.playmode stop 2>&1 | Out-Null; Start-Sleep 4
    unity command console clear 2>&1 | Out-Null
    unity command editor.playmode start 2>&1 | Out-Null; Start-Sleep $settle
    if (-not (ClockOk)) { Write-Warning "Editor Play Mode clock is not advancing (known Editor stall): results of this group are BLOCKED, not product failures. Restart the Editor." ; $script:blocked = $true } else { $script:blocked = $false }
}
function Errors() {
    # Real errors only; Meta XR SDK Editor-only messages (no headset over Link) are counted apart (AgentScripts/LogTally.cs).
    $s = U LogTally Errors
    if ($s -match 'errors=(\d+) editorOnly=(\d+)(.*)') { return "$($matches[1]) (+$($matches[2]) editor-only)$($matches[3])" } else { return "?" }
}
function ClockOk() {
    $a = U ClockProbe Run; Start-Sleep 2; $b = U ClockProbe Run
    $ta = [double]([regex]::Match($a, 't=([\d.]+)').Groups[1].Value); $tb = [double]([regex]::Match($b, 't=([\d.]+)').Groups[1].Value)
    return ($tb - $ta) -gt 0.5
}
function Row($group, $cmd, $ok, $secs, $errs, $detail) {
    $d = ($detail -replace '\s+', ' ').Trim(); if ($d.Length -gt 300) { $d = $d.Substring(0, 300) + "…" }
    $res = if ($ok) { "PASS" } elseif ($script:blocked) { "BLOCKED (Editor clock)" } else { "FAIL" }
    [void]$rows.Add([pscustomobject]@{ group = $group; cmd = $cmd; result = $res; secs = [int]$secs; errs = $errs; detail = $d })
    "{0,-34} {1,-4} {2,5}s errs={3}  {4}" -f $group, $res, [int]$secs, $errs, $d.Substring(0, [Math]::Min(140, $d.Length))
}
function Drive($cls, $entry, $max = 900) {
    $t0 = Get-Date; Play
    $null = U $cls $entry
    $deadline = (Get-Date).AddSeconds($max); $st = ""
    do { Start-Sleep 6; $st = U $cls "Status" } while ((Get-Date) -lt $deadline -and $st -notmatch 'FINISHED')
    $ok = ($st -match 'FINISHED') -and ($st -match 'steps failed=0')
    $sum = (($st -split "`n") | Where-Object { $_ -match 'run ended|steps failed|FAIL ' }) -join " | "
    Row "$cls.$entry" "unity command script run --file AgentScripts/$cls.cs --entry $cls.$entry (+ Status poll)" $ok ((Get-Date) - $t0).TotalSeconds (Errors) $sum
}
function WaitFor($cls, $entry, $pattern, $max) {
    $deadline = (Get-Date).AddSeconds($max)
    do { Start-Sleep 3; $r = U $cls $entry } while ((Get-Date) -lt $deadline -and $r -notmatch $pattern)
    return $r
}

"MuseDesk preflight  commit $commit  build label $label  -> $out"

unity command editor.playmode stop 2>&1 | Out-Null; Start-Sleep 3     # EditMode tests and compiles need Play Mode off
if (Want "compile") {
    $t0 = Get-Date; $o = (& .\scripts\compile.ps1 2>&1) -join " "
    $ok = $o -match 'recompile status: (completed|up_to_date)' -and ((unity command console status 2>&1) -join " ") -match '"compilationFailed":\s*false'
    Row "compile" "scripts\compile.ps1" $ok ((Get-Date) - $t0).TotalSeconds 0 (($o -split '==')[1])
}
if (Want "editmode") {
    $t0 = Get-Date; $o = (& .\scripts\tests.ps1 -Timeout 900 2>&1) -join " "
    $ok = $o -match 'passed=(\d+) failed=0' -and [int]$matches[1] -gt 1000
    Row "EditMode (all)" "scripts\tests.ps1 -Timeout 900" $ok ((Get-Date) - $t0).TotalSeconds 0 ([regex]::Match($o, 'passed=\d+ failed=\d+').Value)
}
if (Want "backend") {
    $t0 = Get-Date; Push-Location backend
    $o = (.venv\Scripts\python.exe -m pytest -q -p no:cacheprovider -W ignore 2>&1) | Out-String; Pop-Location
    $dots = ([regex]::Matches((($o -split "`n") | Where-Object { $_ -match '^[.sFE]+\s' }) -join "", '\.')).Count
    $fails = ([regex]::Matches((($o -split "`n") | Where-Object { $_ -match '^[.sFE]+\s' }) -join "", '[FE]')).Count
    Row "backend pytest (all)" "cd backend; .venv\Scripts\python.exe -m pytest -q" ($fails -eq 0 -and $dots -gt 300) ((Get-Date) - $t0).TotalSeconds 0 "$dots passed, $fails failed/errored"
}
if (Want "drives") {
    Drive "MeetDrive" "Run" 300
    Drive "AgentSelectGrabDrive" "Run" 300
    Drive "UnifiedDockDrive" "Run" 300
    Drive "LivingOfficeDrive" "RunOfflineStamp" 900
    Drive "LivingOfficeDrive" "RunOfflineWithFailure" 900
}
if (Want "spatial") {
    $t0 = Get-Date; Play; $log = @()
    $log += U SpatialDrive Enable
    $log += U SpatialDrive Floor; Start-Sleep 2; $log += U SpatialDrive Confirm
    $f = WaitFor SpatialDrive Report 'mode=Floor .*mover=Idle' 60; $log += $f
    $log += U SpatialDrive Desk; $d1 = WaitFor SpatialDrive Report 'mode=Desk .*mover=Idle' 60; $log += $d1
    $log += U SpatialDrive Ceiling; Start-Sleep 2; $log += U SpatialDrive Confirm
    $c = WaitFor SpatialDrive Report 'mode=Ceiling .*mover=Idle' 60; $log += $c
    $log += U SpatialDrive PodDown; $down = WaitFor SpatialDrive Report 'pod=(Engaged|Deployed)' 60; $log += $down
    $log += U SpatialDrive PodUp; $up = WaitFor SpatialDrive Report 'pod=(Docked|Idle)' 60; $log += $up
    $log += U SpatialDrive Desk; $d2 = WaitFor SpatialDrive Report 'mode=Desk .*mover=Idle' 90; $log += $d2
    $ok = ($f -match 'mode=Floor') -and ($c -match 'mode=Ceiling') -and ($down -match 'pod=(Engaged|Deployed)') -and ($up -match 'pod=(Docked|Idle)') -and ($d2 -match 'mode=Desk')
    $log | Out-File -Encoding utf8 (Join-Path $outDir "spatial_$commit.txt")
    Row "Floor -> Desk -> Ceiling (pod down/up) -> Desk" "SpatialDrive Enable/Floor/Confirm/Desk/Ceiling/Confirm/PodDown/PodUp/Desk" $ok ((Get-Date) - $t0).TotalSeconds (Errors) ("floor: " + ([regex]::Match($f, 'mode=\S+').Value) + " | ceiling: " + ([regex]::Match($c, 'mode=\S+').Value) + " " + ([regex]::Match($down, 'pod=\S+').Value) + " -> " + ([regex]::Match($up, 'pod=\S+').Value) + " | end: " + ([regex]::Match($d2, 'mode=\S+').Value))
}
if (Want "portal") {
    $t0 = Get-Date; Play; $log = @()
    $log += U PortalFxDrive Setup; Start-Sleep 2; $log += U PortalFxDrive Place; Start-Sleep 4
    $log += U PortalFxDrive Meet '[\"Developer\"]'; Start-Sleep 3
    $log += U PortalFxDrive Open; Start-Sleep 10
    $open = U PortalFxDrive State; $log += $open
    $log += U PortalFxDrive Touch; Start-Sleep 2; $log += U PortalFxDrive Ask; Start-Sleep 4
    $log += U PortalFxDrive Back; Start-Sleep 10
    $end = U PortalFxDrive State; $log += $end
    $ok = ($open -match 'avatar=True') -and ($open -match 'platform=True') -and ($open -match 'hidden: Developer') -and ($end -match 'portal=Hidden') -and ($end -notmatch 'hidden: \w')
    $log | Out-File -Encoding utf8 (Join-Path $outDir "portal_$commit.txt")
    Row "Meet -> Open here -> touch/ask -> Send back" "PortalFxDrive Setup/Place/Meet Developer/Open/Touch/Ask/Back" $ok ((Get-Date) - $t0).TotalSeconds (Errors) ("open: " + ([regex]::Match($open, 'portal=\S+.*?avatar=\S+').Value) + " " + ([regex]::Match($open, 'platform=\S+ lift=\S+').Value) + " | end: " + ([regex]::Match($end, 'portal=\S+').Value) + " " + ([regex]::Match($end, 'hidden:[^|]*').Value))
}
if (Want "amend") {
    $t0 = Get-Date; $o = (& .\scripts\amend_check.ps1 2>&1) -join "`n"
    $ok = $o -match 'change card \S+ developer=completed' -and $o -match 'applied=1'
    Row "Apply change (offline, after a full plan)" "scripts\amend_check.ps1" $ok ((Get-Date) - $t0).TotalSeconds (Errors) (([regex]::Matches($o, 'committed=\d+ printed=\d+ applied=\d+') | Select-Object -Last 1).Value + " " + [regex]::Match($o, 'change card [^|]*$').Value)
}
if (Want "desk") {
    $t0 = Get-Date; Play
    $o = U DeskClearanceDrive Report
    $ok = $o -match 'below 0\.015 m: 0'
    Row "Desk walk clearance (7 routes)" "DeskClearanceDrive.Report" $ok ((Get-Date) - $t0).TotalSeconds (Errors) ([regex]::Match($o, 'routes=7 below 0\.015 m: \d+').Value)
    $t0 = Get-Date; Play
    $null = U OfferHoldDrive Start; Start-Sleep 60; $o = U OfferHoldDrive Report
    Row "Desk paper offer holds (no take-back)" "OfferHoldDrive.Start, Report after 60 s" ($o -match 'PASS: yes') ((Get-Date) - $t0).TotalSeconds (Errors) ([regex]::Match($o, 'RETRACTS: \d+').Value + " " + [regex]::Match($o, 'offer seen: \S+ at t\+[\d.]+ s').Value)
}
unity command editor.playmode stop 2>&1 | Out-Null

$md = @("# Preflight $commit ($label), $(Get-Date -Format 'yyyy-MM-dd HH:mm')", "", "Evidence: AUTO (compile/EditMode/pytest) and PLAY (Unity Editor drives, synthetic room, simulated hands). Not WORN.", "",
        "| Group | Result | s | Console errors (real, + Editor-only SDK) | Command | Detail |", "|---|---|---|---|---|---|")
foreach ($r in $rows) { $md += "| $($r.group) | **$($r.result)** | $($r.secs) | $($r.errs) | ``$($r.cmd)`` | $($r.detail -replace '\|', '/') |" }
$pass = @($rows | Where-Object { $_.result -eq 'PASS' }).Count
$md += ""; $md += "$pass of $($rows.Count) groups passed."
[IO.File]::WriteAllLines($out, $md, (New-Object System.Text.UTF8Encoding($false)))
"$pass of $($rows.Count) groups passed. Ledger: $out"
