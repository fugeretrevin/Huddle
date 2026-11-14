# Refresh + recompile in the shared Editor, wait for it, and print compile errors (CS....) from the console.
# Usage: scripts\compile.ps1 [-Timeout 240]
param([int]$Timeout = 240)
$p = Split-Path -Parent $PSScriptRoot
unity command --project-path $p script recompile | Out-Null
$start = Get-Date
$status = ""
while (((Get-Date) - $start).TotalSeconds -lt $Timeout) {
    Start-Sleep -Seconds 4
    $s = unity command --project-path $p script recompile_status --result-only 2>$null | Out-String
    if ($s -match '"status"\s*:\s*"(\w+)"') { $status = $Matches[1] }
    if ($status -eq 'completed' -or $status -eq 'up_to_date' -or $status -eq 'idle') { break }
}
"== recompile status: $status after $([int]((Get-Date) - $start).TotalSeconds) s"
$st = unity command --project-path $p console status --result-only 2>$null | Out-String
"== console status: " + ($st -replace '\s+', ' ').Substring(0, [Math]::Min(400, ($st -replace '\s+', ' ').Length))
$log = unity command --project-path $p console get --level error --tail 60 --result-only 2>$null | Out-String
$errs = ($log -split "`n") | Where-Object { $_ -match 'error CS\d+' } | ForEach-Object { ($_ -replace '^\s*"?(message"\s*:\s*")?', '').Trim() } | Select-Object -Unique
if ($errs.Count -eq 0) { "== no CS errors in the console tail" } else { "== CS errors:"; $errs | Select-Object -First 25 | ForEach-Object { "  " + $_.Substring(0, [Math]::Min(300, $_.Length)) } }
