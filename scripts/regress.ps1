# Play Mode regression sweep: each driver in a fresh Play session, then the console's error count.
#   scripts\regress.ps1                       # the standard set
#   scripts\regress.ps1 -Only MeetDrive.Run   # one
param([string[]]$Only = @(), [int]$Settle = 12, [int]$MaxSeconds = 900)
$set = @('MeetDrive.Run', 'AgentSelectGrabDrive.Run', 'UnifiedDockDrive.Run', 'LivingOfficeDrive.RunOfflineStamp', 'LivingOfficeDrive.RunOffline')
if ($Only.Count -gt 0) { $set = $Only }
foreach ($e in $set) {
    $cls, $m = $e.Split('.')
    unity command editor.playmode stop 2>&1 | Out-Null
    Start-Sleep 4
    unity command console clear 2>&1 | Out-Null
    unity command editor.playmode start 2>&1 | Out-Null
    Start-Sleep $Settle
    $o = (unity command script run --file "AgentScripts/$cls.cs" --entry $e --timeout 600 2>&1) -join " "
    $o = $o -replace 'diagnostics:.*$', ''
    # The drivers run asynchronously: poll Status until it reports FINISHED (or time out).
    $deadline = (Get-Date).AddSeconds($MaxSeconds); $st = ""
    while ((Get-Date) -lt $deadline) {
        Start-Sleep 6
        $st = (unity command script run --file "AgentScripts/$cls.cs" --entry "$cls.Status" 2>&1) -join "`n"
        if ($st -match "FINISHED|all steps done|finished with failures") { break }
    }
    $o = (($st -split "`n") | Where-Object { $_ -match "FAIL|PASS|FINISHED|steps|failures|RUNNING|done" } | Select-Object -First 30) -join "`n   "
    $err = (unity command console get --level error 2>&1 | Out-String)
    $n = ([regex]::Matches($err, '"level":"error"')).Count
    $mine = ([regex]::Matches($err, 'MuseDesk|NullReference|Exception')).Count
    "== $e"
    "   " + $o.Substring(0, [Math]::Min(600, $o.Length))
    "   console errors: $n (mentioning MuseDesk/exceptions: $mine)"
}
unity command editor.playmode stop 2>&1 | Out-Null
