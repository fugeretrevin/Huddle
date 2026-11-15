# Start the backend with the Wall Habitat (Phase 3) features on.
#
#   scripts\start_wall_backend.ps1            # Claude if a key is set, otherwise scripted demo
#   scripts\start_wall_backend.ps1 -Demo      # force the scripted demo (no model calls)
#
# The API key is read from the environment only: set ANTHROPIC_API_KEY (or
# MUSEDESK_PROVIDER_API_KEY) in this shell or in backend\.env. It is never written anywhere.
# For a USB-tethered Quest, also run: adb reverse tcp:8765 tcp:8765
param(
    [switch]$Demo,
    [string]$Model = "claude-opus-5-5",
    [int]$Port = 8765
)
$ErrorActionPreference = "Stop"
$backend = Join-Path $PSScriptRoot "..\backend"
$py = Join-Path $backend ".venv\Scripts\python.exe"

$env:MUSEDESK_P3_SIDE_CONVERSATION_ENABLED = "true"
$env:MUSEDESK_P3_MANAGER_ENABLED = "true"
$env:MUSEDESK_P3_PLAN_RUNNER_ENABLED = "true"

$envFile = Join-Path $backend ".env"
$keyInFile = (Test-Path $envFile) -and (Select-String -Path $envFile -Pattern '^\s*(MUSEDESK_PROVIDER_API_KEY|ANTHROPIC_API_KEY)\s*=\s*\S' -Quiet)
$haveKey = $env:ANTHROPIC_API_KEY -or $env:MUSEDESK_PROVIDER_API_KEY -or $keyInFile

if ($Demo -or -not $haveKey) {
    if (-not $Demo) { Write-Host "No API key found: starting the SCRIPTED DEMO (labelled, no model calls)." }
    $env:MUSEDESK_PRODUCER = "demo"
    Remove-Item Env:MUSEDESK_MODEL_PROVIDER -ErrorAction SilentlyContinue
} else {
    & $py -c "import anthropic" 2>$null
    if ($LASTEXITCODE -ne 0) { throw "The anthropic package is missing: cd backend; .venv\Scripts\pip install -e .[model]" }
    Write-Host "Starting with Claude ($Model): /btw answers, Manager plans and plan-card work use the model."
    $env:MUSEDESK_PRODUCER = "live"
    $env:MUSEDESK_MODEL_PROVIDER = "anthropic"
    $env:MUSEDESK_P3_MODEL = $Model
}

Push-Location $backend
# uvicorn logs to stderr; in Windows PowerShell 5.1 that would be fatal under "Stop".
$ErrorActionPreference = "Continue"
try {
    & $py -m uvicorn app.main:app --host 127.0.0.1 --port $Port
} finally {
    Pop-Location
}
