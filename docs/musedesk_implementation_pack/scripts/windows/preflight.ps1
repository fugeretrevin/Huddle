# MuseDesk development station preflight. Read-only except display output.
$ErrorActionPreference = 'Continue'
Write-Host '== MuseDesk Windows / Quest 3 preflight ==' -ForegroundColor Cyan
foreach ($command in @('git', 'py', 'node', 'npm', 'adb', 'metavr')) {
    $cmd = Get-Command $command -ErrorAction SilentlyContinue
    if ($cmd) { Write-Host "[OK] $command = $($cmd.Source)" -ForegroundColor Green }
    else { Write-Host "[MISSING or not in PATH] $command" -ForegroundColor Yellow }
}
Write-Host "`n== Unity installations ==" -ForegroundColor Cyan
$unityDir = 'C:\Program Files\Unity\Hub\Editor'
if (Test-Path $unityDir) { Get-ChildItem $unityDir -Directory | Select-Object -ExpandProperty Name }
else { Write-Host "No Unity Hub editor folder found at $unityDir (could be installed elsewhere)" }
Write-Host "`n== ADB / Quest ==" -ForegroundColor Cyan
if (Get-Command adb -ErrorAction SilentlyContinue) { adb devices }
else { Write-Host 'ADB not found: install Unity Android SDK/NDK and add platform-tools to PATH.' }
Write-Host "`n== Suggestions ==" -ForegroundColor Cyan
Write-Host 'Verify developer mode and USB debugging on headset, then MVDH Active status.'
Write-Host 'Run `metavr doctor` if the Meta VR CLI is installed.'
Write-Host 'Before giving coding agents device access, verify they are working in this project repository.'
