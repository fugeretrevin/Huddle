# Runs one Play Mode driver method and prints a trimmed result.
# Usage: scripts\drv.ps1 PlayDrive Start   |   scripts\drv.ps1 AgentInteractionDrive Poke Developer
param([string]$Class, [string]$Method, [string]$Arg = "", [int]$Max = 700)
$p = Split-Path -Parent $PSScriptRoot
if ($Arg -ne "") {
    $json = '[\"' + $Arg + '\"]'
    $o = (unity command --project-path $p script run --file "AgentScripts/$Class.cs" --entry "$Class.$Method" --args $json) -join " "
} else {
    $o = (unity command --project-path $p script run --file "AgentScripts/$Class.cs" --entry "$Class.$Method") -join " "
}
$o = $o -replace 'diagnostics:.*$', ''
"[$Class.$Method $Arg] " + $o.Substring(0, [Math]::Min($Max, $o.Length))
