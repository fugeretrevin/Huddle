# USB-connected Quest -> Windows localhost API; use only for trusted development.
$ErrorActionPreference = 'Stop'
if (!(Get-Command adb -ErrorAction SilentlyContinue)) { throw 'adb not found in PATH' }
adb devices
adb reverse tcp:8765 tcp:8765
adb reverse --list
Write-Host 'Quest Unity dev API URL: http://127.0.0.1:8765' -ForegroundColor Green
Write-Host 'Quest Unity dev WS URL: ws://127.0.0.1:8765/v1/sessions/<id>/events' -ForegroundColor Green
