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
    [switch]$TextOnly,     # plan cards stay model text (nothing written or run)
    [switch]$NoScreen,     # do not offer this PC's screen to the headset's screen panel
    [string]$Model = "claude-opus-5-5",
    [int]$Port = 8765
)
$ErrorActionPreference = "Stop"
$backend = Join-Path $PSScriptRoot "..\backend"
$py = Join-Path $backend ".venv\Scripts\python.exe"

$env:MUSEDESK_P3_SIDE_CONVERSATION_ENABLED = "true"
$env:MUSEDESK_P3_MANAGER_ENABLED = "true"
$env:MUSEDESK_P3_PLAN_RUNNER_ENABLED = "true"
Remove-Item Env:MUSEDESK_P3_EXECUTE_CODE -ErrorAction SilentlyContinue
# rc20: the headset's Screen panel shows this PC's main screen (read on request, never stored, only to the paired
# headset over USB). -NoScreen switches it off.
if ($NoScreen) {
    Remove-Item Env:MUSEDESK_SCREEN_SHARE -ErrorAction SilentlyContinue
    Write-Host "Screen panel: OFF (-NoScreen)."
} else {
    $env:MUSEDESK_SCREEN_SHARE = "true"
    Write-Host "Screen panel: ON. The headset's Screen key shows this PC's main screen (only to the paired headset; nothing is saved)."
}

$envFile = Join-Path $backend ".env"
function Get-EnvFileValue($name) {
    if (-not (Test-Path $envFile)) { return $null }
    $m = Select-String -Path $envFile -Pattern ("^\s*" + $name + "\s*=\s*(\S+)") | Select-Object -First 1
    if ($m) { return $m.Matches[0].Groups[1].Value.Trim('"', "'") }
    return $null
}
# Provider: shell env wins, then backend\.env. Values are only tested for presence, never printed.
$provider = $env:MUSEDESK_MODEL_PROVIDER
if (-not $provider) { $provider = Get-EnvFileValue "MUSEDESK_MODEL_PROVIDER" }
$isMeta = $provider -and ($provider.ToLower() -in @("meta", "llama", "meta_llama"))
if ($isMeta) {
    $haveKey = $env:MUSEDESK_PROVIDER_API_KEY -or $env:MUSE_LLM_API_KEY -or $env:LLAMA_API_KEY -or
        (Get-EnvFileValue "MUSEDESK_PROVIDER_API_KEY") -or (Get-EnvFileValue "MUSE_LLM_API_KEY")
} else {
    $haveKey = $env:ANTHROPIC_API_KEY -or $env:MUSEDESK_PROVIDER_API_KEY -or
        (Get-EnvFileValue "MUSEDESK_PROVIDER_API_KEY") -or (Get-EnvFileValue "ANTHROPIC_API_KEY")
}

if ($Demo -or -not $haveKey) {
    if (-not $Demo) { Write-Host "No API key found: starting the SCRIPTED DEMO (labelled, no model calls)." }
    $env:MUSEDESK_PRODUCER = "demo"
    Remove-Item Env:MUSEDESK_MODEL_PROVIDER -ErrorAction SilentlyContinue
} elseif ($isMeta) {
    Write-Host "Starting LIVE with the Meta model API: /btw answers, Manager plans and plan-card work use the model."
    $env:MUSEDESK_PRODUCER = "live"
    $env:MUSEDESK_MODEL_PROVIDER = "meta"
    if (-not $TextOnly) {
        Write-Host "Developer/Tester plan cards write REAL code into backend\sandbox_workspaces\plans\<plan> and run REAL pytest (-TextOnly to switch off)."
        $env:MUSEDESK_P3_EXECUTE_CODE = "true"
    }
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

