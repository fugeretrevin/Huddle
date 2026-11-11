# Run from repository root after TASK-04 backend is implemented.
$ErrorActionPreference = 'Stop'
$repo = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$backend = Join-Path $repo 'backend'
if (!(Test-Path $backend)) { throw 'backend/ not yet implemented. Run TASK-04 first.' }
$python = Join-Path $backend '.venv\Scripts\python.exe'
if (!(Test-Path $python)) { throw 'Run python venv setup in docs/QUEST3_AND_WINDOWS_SETUP.md first.' }
Push-Location $backend
try { & $python -m uvicorn app.main:app --host 127.0.0.1 --port 8765 --reload }
finally { Pop-Location }
