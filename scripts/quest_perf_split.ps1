param(
    [Parameter(Mandatory)][string]$Label,
    [string]$Dir = ""          # default: <repo>\Logs\quest
)
# Splits a worn-session capture by scenario. Reads vrapi_<Label>.txt (logcat -v time VrApi lines) and
# unitylog_<Label>.txt (logcat -v time Unity lines, incl. "[MuseDesk.Perf] scenario=NAME" and heartbeat
# lines), gives each VrApi second the scenario active at its timestamp, writes perf_<Label>.csv.
$ErrorActionPreference = 'Stop'
$inv = [System.Globalization.CultureInfo]::InvariantCulture
if (-not $Dir) { $Dir = Join-Path (Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)) "Logs\quest" }
$vrFile = Join-Path $Dir "vrapi_$Label.txt"
$ulFile = Join-Path $Dir "unitylog_$Label.txt"
$csv    = Join-Path $Dir "perf_$Label.csv"
if (-not (Test-Path $vrFile)) { Write-Output "quest_perf_split: missing $vrFile"; exit 1 }

# "MM-DD HH:MM:SS.mmm" at the start of a logcat -v time line -> DateTime (year 2000, so Feb 29 parses)
function Get-LineTime([string]$line) {
    if ($line -match '^\s*(\d\d)-(\d\d)\s+(\d\d):(\d\d):(\d\d)\.(\d{3})') {
        try { return [datetime]::new(2000, [int]$matches[1], [int]$matches[2], [int]$matches[3], [int]$matches[4], [int]$matches[5], [int]$matches[6]) } catch { return $null }
    }
    return $null
}
function Num([string]$s) { $o = 0.0; if ([double]::TryParse($s, [System.Globalization.NumberStyles]::Float, $inv, [ref]$o)) { return $o }; return $null }
function Pct($a, [double]$p) {
    if ($a.Count -eq 0) { return $null }
    $s = @($a | Sort-Object)
    return $s[[int][Math]::Min($s.Count - 1, [Math]::Floor($s.Count * $p))]
}
function Avg($a) { if ($a.Count -eq 0) { return $null }; return ($a | Measure-Object -Average).Average }
function Fmt($v, [string]$f = 'F2') { if ($null -eq $v) { return '' }; return ([double]$v).ToString($f, $inv) }

# --- scenario timeline + heartbeats from the Unity log ---
$marks = New-Object System.Collections.Generic.List[object]    # @{t; name}
$hbs = New-Object System.Collections.Generic.List[object]
if (Test-Path $ulFile) {
    foreach ($line in (Get-Content $ulFile)) {
        if ($line -notmatch '\[MuseDesk\.Perf\]') { continue }
        $t = Get-LineTime $line
        if ($null -eq $t) { continue }
        if ($line -match '\[MuseDesk\.Perf\]\s+hb\s+scenario=(\w+)') {
            $hb = @{ t = $t; name = $matches[1]; cpu = $null; gpu = $null; total = $null }
            if ($line -match 'cpuMain=([\d.]+)') { $hb.cpu = Num $matches[1] }
            if ($line -match 'gpu=([\d.]+)')     { $hb.gpu = Num $matches[1] }
            if ($line -match 'totalMB=([\d.]+)') { $hb.total = Num $matches[1] }
            $hbs.Add($hb)
        } elseif ($line -match '\[MuseDesk\.Perf\]\s+scenario=(\w+)') {
            $marks.Add(@{ t = $t; name = $matches[1] })
        }
    }
}
$marks = @($marks | Sort-Object { $_.t })

# --- VrApi seconds ---
$rows = New-Object System.Collections.Generic.List[object]
foreach ($line in (Get-Content $vrFile)) {
    if ($line -notmatch 'FPS=(\d+)/(\d+)') { continue }
    $r = @{ fps = [double]$matches[1]; app = $null; stale = 0; temp = $null; t = (Get-LineTime $line) }
    if ($line -match '\bApp=([\d.]+)ms')  { $r.app = Num $matches[1] }
    if ($line -match 'Stale=(\d+)')       { $r.stale = [int]$matches[1] }
    if ($line -match 'Temp=(-?[\d.]+)C')  { $r.temp = Num $matches[1] }
    $rows.Add($r)
}
if ($rows.Count -eq 0) { Write-Output "quest_perf_split: no VrApi FPS= lines in $vrFile"; exit 1 }

# assign scenarios; lines with no timestamp inherit the previous second's scenario
$mi = -1; $cur = 'unknown'; $order = New-Object System.Collections.Generic.List[string]
foreach ($r in $rows) {
    if ($null -ne $r.t) {
        while (($mi + 1) -lt $marks.Count -and $marks[$mi + 1].t -le $r.t) { $mi++; $cur = $marks[$mi].name }
    }
    $r.scenario = $cur
    if (-not $order.Contains($cur)) { $order.Add($cur) }
}

function Summarise([string]$name, $rs, $hs) {
    $fps = @($rs | ForEach-Object { $_.fps })
    $app = @($rs | Where-Object { $null -ne $_.app } | ForEach-Object { $_.app })
    $tmp = @($rs | Where-Object { $null -ne $_.temp } | ForEach-Object { $_.temp })
    $cpu = @($hs | Where-Object { $null -ne $_.cpu -and $_.cpu -gt 0 } | ForEach-Object { $_.cpu })
    $gpu = @($hs | Where-Object { $null -ne $_.gpu -and $_.gpu -gt 0 } | ForEach-Object { $_.gpu })
    $tot = @($hs | Where-Object { $null -ne $_.total } | ForEach-Object { $_.total })
    $stale = 0; foreach ($x in $rs) { $stale += $x.stale }
    $note = ''
    if ($tmp.Count -gt 0) {
        $note = "temp {0}-{1}C" -f (Fmt ($tmp | Measure-Object -Minimum).Minimum 'F1'), (Fmt ($tmp | Measure-Object -Maximum).Maximum 'F1')
        if (($tmp[-1] - $tmp[0]) -ge 2) { $note += ' rising' }
    }
    [pscustomobject][ordered]@{
        scenario = $name
        seconds = $rs.Count
        fps_avg = Fmt (Avg $fps) 'F1'
        fps_min = Fmt (($fps | Measure-Object -Minimum).Minimum) 'F0'
        fps_p5 = Fmt (Pct $fps 0.05) 'F0'
        app_gpu_ms_avg = Fmt (Avg $app)
        app_gpu_ms_p95 = Fmt (Pct $app 0.95)
        cpu_main_ms_avg = Fmt (Avg $cpu)
        cpu_main_ms_p95 = Fmt (Pct $cpu 0.95)
        gpu_ms_unity_avg = Fmt (Avg $gpu)
        mem_total_mb_max = Fmt (($tot | Measure-Object -Maximum).Maximum) 'F0'
        stale_frames = $stale
        thermal_note = $note
    }
}
$out = New-Object System.Collections.Generic.List[object]
foreach ($n in $order) {
    $rs = @($rows | Where-Object { $_.scenario -eq $n })
    $hs = @($hbs | Where-Object { $_.name -eq $n })
    $out.Add((Summarise $n $rs $hs))
}
$out.Add((Summarise 'all' $rows.ToArray() $hbs.ToArray()))
$out | Export-Csv -Path $csv -NoTypeInformation -Encoding UTF8
$out | Format-Table -AutoSize | Out-String -Width 250
Write-Output "csv: $csv"
