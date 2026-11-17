# Before a filming take (Quest + PC). Read-only by default.
#   scripts\record_preflight.ps1                # checks: installed build hash, battery, storage, refresh, backend, adb reverse, PC RAM
#   scripts\record_preflight.ps1 -CaptureProps  # ALSO ask the Quest to record 1920x1080 (developer props; reset on reboot; verify a test clip)
#   scripts\record_preflight.ps1 -Restart       # clean take: force-stop + relaunch MuseDesk (resets the scripted demo)
#   scripts\record_preflight.ps1 -Pull          # copy /sdcard/Oculus/VideoShots to Captures\quest\<date> (never deletes on the headset)
param([switch]$CaptureProps, [switch]$Pull, [switch]$Restart, [string]$Expect = "BD0204F4556E6A301379A09F0ED986B9797273A989FC71AA222EEA0A17B602AD")
$adb = "C:\Program Files\Unity\Hub\Editor\6000.6.8f1\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe"
$d = "2G97C5ZHCK037K"
$root = Split-Path -Parent $PSScriptRoot
function A([string]$a) { (& $adb -s $d shell $a 2>$null) -join "`n" }
$ok = $true
function Line($name, $good, $detail) { if (-not $good) { $script:ok = $false }; "{0,-26} {1,-5} {2}" -f $name, $(if ($good) { "OK" } else { "CHECK" }), $detail }

$dev = (& $adb devices 2>$null) -join " "
Line "Quest connected (USB)" ($dev -match "$d\s+device") $d
$p = ((A "pm path com.musedesk.quest") -replace '^package:', '').Trim()
$h = if ($p) { (A "sha256sum $p").Split(' ')[0].ToUpper() } else { "" }
Line "Installed build" ($h -eq $Expect) ($(if ($h) { $h.Substring(0, 8) + "... (expect " + $Expect.Substring(0, 8) + "...)" } else { "MuseDesk not installed" }))
$bat = [regex]::Match((A "dumpsys battery"), 'level: (\d+)').Groups[1].Value
Line "Battery" ([int]$bat -ge 60) "$bat% (film above 60%; plug in between takes)"
$free = [regex]::Match((A "df -h /sdcard | tail -1"), '\s(\d+(\.\d+)?[GM])\s+\d+%').Groups[1].Value
Line "Free storage" ($free -match 'G' -and [double]($free -replace '[GM]', '') -ge 5) "$free free"
$hz = ((A "dumpsys display | grep -o 'fps=[0-9.]*'") -split "`n" | Select-Object -First 1)
Line "Display mode" $true "$hz (the app asks for its own rate; rc15 ran at 72 fps)"
$rev = (& $adb -s $d reverse --list 2>$null) -join " "
$be = try { (Invoke-WebRequest -UseBasicParsing -TimeoutSec 3 http://127.0.0.1:8765/health).StatusCode -eq 200 } catch { $false }
Line "Backend on PC (live takes)" $be $(if ($be) { "127.0.0.1:8765 answers" } else { "off: offline (SCRIPTED DEMO) takes only; scripts\start_wall_backend.ps1 for live" })
Line "adb reverse 8765 (live)" ($rev -match '8765') $(if ($rev -match '8765') { "set" } else { "not set: adb reverse tcp:8765 tcp:8765" })
$os = Get-CimInstance Win32_OperatingSystem; $gb = [math]::Round($os.FreePhysicalMemory / 1MB, 1)
Line "PC free RAM" ($gb -ge 2) "$gb GB (below 2 GB the live backend may be stopped by the system; close Unity Play Mode / browsers)"

if ($CaptureProps) {
    A "setprop debug.oculus.capture.width 1920; setprop debug.oculus.capture.height 1080; setprop debug.oculus.capture.bitrate 20000000; setprop debug.oculus.fullRateCapture 1" | Out-Null
    "capture props set: " + (A "getprop debug.oculus.capture.width") + "x" + (A "getprop debug.oculus.capture.height") + " (reset on reboot; record a 10 s test clip and check it)"
}
if ($Restart) {
    # A clean take: force-stop and relaunch MuseDesk (scripted demo state, agents at home, no visitors, no papers).
    # Nothing is approved or completed for you: every interaction in the take stays yours.
    & $adb -s $d shell am force-stop com.musedesk.quest | Out-Null
    Start-Sleep 2
    & $adb -s $d shell am start -n com.musedesk.quest/com.unity3d.player.UnityPlayerGameActivity | Out-Null
    "MuseDesk restarted for a clean take (put the headset on; the office appears at your desk)"
}
if ($Pull) {
    $dest = Join-Path $root ("Captures\quest\" + (Get-Date -Format yyyyMMdd))
    New-Item -ItemType Directory -Force $dest | Out-Null
    & $adb -s $d pull /sdcard/Oculus/VideoShots $dest
    "pulled to $dest (the headset copies are kept)"
}
if ($ok) { "READY" } else { "CHECK the lines above before filming" }
