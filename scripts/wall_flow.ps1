# Wall Habitat Play Mode walkthrough (Editor, synthetic room, simulated hands). Evidence level: PLAY.
# Assumes Play Mode is running (scripts\drv.ps1 needs it). Prints one line per step.
param([string]$Part = "all")
$d = Join-Path $PSScriptRoot 'drv.ps1'
function S([string]$m, [string]$a = "", [int]$max = 900) { & $d WallDrive $m $a -Max $max }
function W([double]$s) { Start-Sleep -Milliseconds ([int]($s * 1000)) }

if ($Part -eq "all" -or $Part -eq "place") {
    S Enable; W 2
    S Enter; W 1
    S Place; W 2
}
if ($Part -eq "all" -or $Part -eq "visit") {
    S Tasks "" 600
    S Select; S Hand developer 200; W 0.8
    S Pinch developer 200; W 1.2
    S ToPerch "" 200; W 1.2
    S Open "" 200; W 1.0
    S Report "" 1600
    S Ask 0 300; W 2.5
    S Report "" 1600
    S AskChange "" 300; W 2.5
    S Report "" 1600
    S Tasks "" 600
}
if ($Part -eq "all" -or $Part -eq "portal") {
    S MeetLarger "" 1400; W 1
    S PortalOpen "" 1400; W 3
    S Report "" 1600
    S Ask 1 300; W 2.5
    S SendBack "" 600; W 2.5
    S Report "" 1200
    S Tasks "" 600
}
if ($Part -eq "all" -or $Part -eq "manager") {
    S Plan 0 1400; W 2
    S PlanReport "" 1400
    S Approve "" 1400; W 4
    S PlanReport "" 1400
}
if ($Part -eq "all" -or $Part -eq "elevator") {
    S Event "Handoff:researcher:manager" 900; W 12
    S Walkers "" 900; W 15
    S Walkers "" 900
}
