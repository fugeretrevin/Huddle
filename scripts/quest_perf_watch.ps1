param(
    [string]$Label = "worn",
    [int]$WaitMinutes = 180,     # how long to wait for MuseDesk to be running on the headset
    [int]$CaptureMinutes = 30    # how long to record once it is
)
# Passive performance capture: never launches the app, never touches the proximity sensor or the lock
# screen. It waits until MuseDesk is running (i.e. someone put the headset on and opened it), then
# records the compositor's per-second VrApi lines and memory while they use it, and writes a summary.
$adb = "C:\Program Files\Unity\Hub\Editor\6000.6.8f1\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe"
$d = "2G97C5ZHCK037K"
$outDir = Join-Path (Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)) "Logs\quest"
New-Item -ItemType Directory -Force $outDir | Out-Null

function Adb([string]$a, [int]$timeout = 25) {
    $p = New-Object System.Diagnostics.Process
    $p.StartInfo.FileName = $adb; $p.StartInfo.Arguments = "-s $d $a"
    $p.StartInfo.RedirectStandardOutput = $true; $p.StartInfo.RedirectStandardError = $true
    $p.StartInfo.UseShellExecute = $false; $p.StartInfo.CreateNoWindow = $true
    [void]$p.Start(); $task = $p.StandardOutput.ReadToEndAsync()
    if (-not $p.WaitForExit($timeout * 1000)) { try { $p.Kill() } catch {}; return "" }
    return $task.Result
}

$deadline = (Get-Date).AddMinutes($WaitMinutes); $appPid = ""
while ((Get-Date) -lt $deadline) {
    $appPid = (Adb 'shell pidof com.musedesk.quest').Trim()
    if ($appPid) { break }
    Start-Sleep 20
}
if (-not $appPid) { "MuseDesk was not opened on the headset within $WaitMinutes minutes: nothing captured."; exit 0 }
"MuseDesk running (pid $appPid) at $(Get-Date -Format HH:mm:ss); recording for up to $CaptureMinutes minutes"
[void](Adb 'logcat -G 16M'); [void](Adb 'logcat -c')
$mem = @(); $end = (Get-Date).AddMinutes($CaptureMinutes)
while ((Get-Date) -lt $end) {
    Start-Sleep 30
    if (-not (Adb 'shell pidof com.musedesk.quest').Trim()) { break }
    $m = Adb 'shell "dumpsys meminfo com.musedesk.quest | grep -E ''TOTAL PSS|TOTAL:''"'
    if ($m -match '(\d{5,})') { $mem += [int]$matches[1] }
}
$vr = Adb 'shell "logcat -d -v time -s VrApi:I | grep FPS="' 60
$vrFile = Join-Path $outDir "vrapi_$Label.txt"; $vr | Out-File -Encoding utf8 $vrFile
$log = Adb 'shell "logcat -d -v time -s Unity:V | grep -E ''\[MuseDesk|Exception|NullReference|MissingReference''"' 60
$log | Out-File -Encoding utf8 (Join-Path $outDir "unitylog_$Label.txt")

$fps = @(); $app = @(); $gpuPct = @(); $cpuPct = @(); $stale = 0
foreach ($line in ($vr -split "`n")) {
    if ($line -match 'FPS=(\d+)/(\d+)') { $fps += [int]$matches[1]; $target = [int]$matches[2] }
    if ($line -match 'App=([\d.]+)ms') { $app += [double]$matches[1] }
    if ($line -match 'GPU%=([\d.]+)') { $gpuPct += [double]$matches[1] }
    if ($line -match 'CPU%=([\d.]+)') { $cpuPct += [double]$matches[1] }
    if ($line -match 'Stale=(\d+)') { $stale += [int]$matches[1] }
}
function Stat($a) { if ($a.Count -eq 0) { return "n/a" }; $s = $a | Sort-Object; "avg {0:N1}  min {1:N1}  p5 {2:N1}  p95 {3:N1}  max {4:N1}" -f (($a | Measure-Object -Average).Average), $s[0], $s[[int]([Math]::Floor($s.Count * 0.05))], $s[[int]([Math]::Min($s.Count - 1, [Math]::Floor($s.Count * 0.95)))], $s[-1] }
$sum = @(
    "MuseDesk on-device performance, label '$Label', $(Get-Date -Format 'yyyy-MM-dd HH:mm')",
    "samples (1 per second): $($fps.Count); target refresh: $target Hz",
    "FPS:            $(Stat $fps)",
    "App GPU time ms: $(Stat $app)",
    "GPU utilisation: $(Stat $gpuPct)",
    "CPU utilisation: $(Stat $cpuPct)",
    "stale frames total: $stale",
    "memory PSS kB:  $(Stat $mem)",
    "Unity exception lines: $((($log -split "`n") | Where-Object { $_ -match 'Exception|NullReference|MissingReference' }).Count)",
    "raw: $vrFile"
)
$sumFile = Join-Path $outDir "perf_summary_$Label.txt"; $sum | Out-File -Encoding utf8 $sumFile; $sum

# Per-scenario split (uses the [MuseDesk.Perf] scenario lines the app logs); writes Logs\quest\perf_<Label>.csv
$split = Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) "quest_perf_split.ps1"
if (Test-Path $split) { try { & $split -Label $Label } catch { "perf split failed: $($_.Exception.Message)" } }
