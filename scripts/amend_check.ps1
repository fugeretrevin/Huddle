# Play Mode check of the visit card's "Apply change" (rc17), after a full Living Office run in the same session.
#   scripts\amend_check.ps1            # offline (backend off): scripted step
#   scripts\amend_check.ps1 -Live      # live backend on: the step is really worked
param([switch]$Live)
function Run($cls, $entry, $argJson = $null) {
    if ($argJson) { $o = (unity command script run --file "AgentScripts/$cls.cs" --entry "$cls.$entry" --args $argJson 2>&1) -join "`n" }
    else { $o = (unity command script run --file "AgentScripts/$cls.cs" --entry "$cls.$entry" 2>&1) -join "`n" }
    return ($o -replace '(?s)diagnostics:.*$', '').Trim()
}
unity command editor.playmode stop 2>&1 | Out-Null; Start-Sleep 4
unity command editor.playmode start 2>&1 | Out-Null; Start-Sleep $(if ($Live) { 30 } else { 12 })
$run = if ($Live) { 'RunLive' } else { 'RunOffline' }
Run LivingOfficeDrive $run
$deadline = (Get-Date).AddSeconds($(if ($Live) { 1500 } else { 600 }))
do { Start-Sleep 8; $st = Run LivingOfficeDrive Status } while ((Get-Date) -lt $deadline -and $st -notmatch 'FINISHED')
($st -split "`n" | Where-Object { $_ -match 'FAIL|run ended|steps failed' }) -join "`n"
"-- perch the Developer"
foreach ($p in 'arm', 'pinch', 'carry', 'drop', 'off') { $null = Run PortalFxDrive Perch "[\`"$p\`"]"; Start-Sleep 2 }
Start-Sleep 3
"-- ask for a change"; Run AmendDrive AskChange
Start-Sleep $(if ($Live) { 25 } else { 4 })
Run AmendDrive State
"-- press Apply change"; Run PortalFxDrive Key '[\"visit:Apply_change\"]'
$deadline = (Get-Date).AddSeconds($(if ($Live) { 900 } else { 240 }))
do { Start-Sleep 10; $s = Run AmendDrive State } while ((Get-Date) -lt $deadline -and $s -notmatch 'change card .*=(completed|failed)')
$s
unity command editor.playmode stop 2>&1 | Out-Null
