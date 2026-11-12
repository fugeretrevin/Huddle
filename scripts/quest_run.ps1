param(
    [string]$Label = "run",
    [int]$Seconds = 90,
    [switch]$Install,
    [string]$Apk = "C:\dev\MuseDesk\MuseDesk\Builds\MuseDesk.apk"
)
# Launch MuseDesk on the Quest with the proximity sensor overridden (nobody wearing it), let it run
# for $Seconds, and collect: VrApi performance lines, MuseDesk log lines, exceptions, a screenshot.
# Every adb call is time-boxed so a stuck command cannot hang the run.
$adb = "C:\Program Files\Unity\Hub\Editor\6000.6.8f1\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe"
$d = "2G97C5ZHCK037K"
$outDir = Join-Path (Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)) "Logs\quest"
New-Item -ItemType Directory -Force $outDir | Out-Null

function Adb([string[]]$a, [int]$timeout = 25) {
    $p = New-Object System.Diagnostics.Process
    $p.StartInfo.FileName = $adb
    $p.StartInfo.Arguments = ("-s $d " + ($a -join ' '))
    $p.StartInfo.RedirectStandardOutput = $true
    $p.StartInfo.RedirectStandardError = $true
    $p.StartInfo.UseShellExecute = $false
    $p.StartInfo.CreateNoWindow = $true
    [void]$p.Start()
    $task = $p.StandardOutput.ReadToEndAsync()
    if (-not $p.WaitForExit($timeout * 1000)) { try { $p.Kill() } catch {}; return "(adb timed out: $($a -join ' '))" }
    return $task.Result
}

$state = Adb @('shell', '"dumpsys vrpowermanager | grep -E ^State"')
$fg = Adb @('shell', '"dumpsys activity activities | grep topResumedActivity | head -1"')
"headset: $($state.Trim())  foreground: $(($fg -replace '^.*u0 ','' -replace ' t\d+.*$','').Trim())"

if ($Install) { "install: " + (Adb @('install', '-r', "`"$Apk`"") 240).Trim().Split("`n")[-1] }

[void](Adb @('shell', 'input', 'keyevent', 'KEYCODE_WAKEUP'))
[void](Adb @('shell', 'am', 'broadcast', '-a', 'com.oculus.vrpowermanager.prox_close'))
Start-Sleep 3
[void](Adb @('shell', 'am', 'force-stop', 'com.musedesk.quest'))
[void](Adb @('logcat', '-c'))
[void](Adb @('logcat', '-G', '8M'))
$start = Adb @('shell', 'am', 'start', '-n', 'com.musedesk.quest/com.unity3d.player.UnityPlayerGameActivity') 45
"start: " + ($start.Trim() -replace "`r?`n", ' | ')

Start-Sleep $Seconds

"pid: " + (Adb @('shell', 'pidof', 'com.musedesk.quest')).Trim()
$vr = Adb @('shell', '"logcat -d -s VrApi:I | grep FPS="') 40
$vrFile = Join-Path $outDir "vrapi_$Label.txt"
$vr | Out-File -Encoding utf8 $vrFile
"VrApi lines: " + (($vr -split "`n") | Where-Object { $_ -match 'FPS=' }).Count + "  -> $vrFile"
"MRUK world-lock warnings: " + (Adb @('shell', '"logcat -d -s Unity:V | grep -c EnableWorldLock"')).Trim()
"Unity error-level lines: " + (Adb @('shell', '"logcat -d -s Unity:E | grep -vc ^-"')).Trim()
$log = Adb @('shell', '"logcat -d -v time -s Unity:V | grep -E ''\[MuseDesk|Exception|NullReference|MissingReference|was not found in the|is missing|not found''"') 40
$logFile = Join-Path $outDir "unitylog_$Label.txt"
$log | Out-File -Encoding utf8 $logFile
($log -split "`n") | Where-Object { $_.Trim() } | ForEach-Object { $x = $_ -replace '^\d\d-\d\d (\S+) ./Unity\s*\(\s*\d+\):\s*', '$1 '; if ($x.Length -gt 200) { $x.Substring(0, 200) } else { $x } } | Select-Object -First 60
$mem = Adb @('shell', '"dumpsys meminfo com.musedesk.quest | grep -E ''TOTAL PSS|TOTAL:|Native Heap|Graphics''"')
"memory: " + (($mem -split "`n" | ForEach-Object { $_.Trim() } | Where-Object { $_ }) -join ' || ')
[void](Adb @('shell', 'screencap', '-p', '/sdcard/musedesk_shot.png') 30)
$shot = Join-Path $outDir "device_$Label.png"
[void](Adb @('pull', '/sdcard/musedesk_shot.png', "`"$shot`"") 40)
if (Test-Path $shot) { "screenshot: $shot ($((Get-Item $shot).Length) bytes)" }
