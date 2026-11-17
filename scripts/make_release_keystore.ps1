# OWNER ONLY. Creates MuseDesk's RELEASE signing key (needed for a Meta release channel: a debug-signed APK is refused).
# Run it yourself in your own PowerShell window (it asks for passwords; nothing is echoed or logged):
#   powershell -ExecutionPolicy Bypass -File scripts\make_release_keystore.ps1
# Writes, OUTSIDE the repo:
#   %USERPROFILE%\.musedesk\musedesk-release.keystore   the key: BACK IT UP. Every future update must be signed with it.
#   %USERPROFILE%\.musedesk\signing.json                 path + alias + passwords, read only by AgentScripts/BuildApkTo.RunSigned
# Never commit either file. Never paste the passwords into a chat or a log.
$ErrorActionPreference = "Stop"
$dir = Join-Path $env:USERPROFILE ".musedesk"
New-Item -ItemType Directory -Force $dir | Out-Null
$ks = Join-Path $dir "musedesk-release.keystore"
if (Test-Path $ks) { throw "$ks already exists. Keep using it (a new key would be a different app identity)." }
$keytool = "C:\Program Files\Unity\Hub\Editor\6000.6.8f1\Editor\Data\PlaybackEngines\AndroidPlayer\OpenJDK\bin\keytool.exe"
if (-not (Test-Path $keytool)) { throw "keytool not found at $keytool" }

function Plain($secure) { [Runtime.InteropServices.Marshal]::PtrToStringAuto([Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)) }
$p1 = Plain (Read-Host "Keystore password (6+ characters)" -AsSecureString)
$p2 = Plain (Read-Host "Repeat it" -AsSecureString)
if ($p1 -ne $p2 -or $p1.Length -lt 6) { throw "The passwords differ or are shorter than 6 characters." }
$name = Read-Host "Name or organisation for the certificate (e.g. MuseDesk)"
if (-not $name) { $name = "MuseDesk" }

& $keytool -genkeypair -v -keystore $ks -alias musedesk -keyalg RSA -keysize 2048 -validity 10000 `
    -storepass $p1 -keypass $p1 -dname ("CN=" + $name + ", O=" + $name)
if ($LASTEXITCODE -ne 0) { throw "keytool failed" }

$json = @{ keystorePath = $ks; keystorePass = $p1; keyAlias = "musedesk"; keyPass = $p1 } | ConvertTo-Json
[IO.File]::WriteAllText((Join-Path $dir "signing.json"), $json, (New-Object System.Text.UTF8Encoding($false)))
# Only this Windows user may read the folder.
icacls $dir /inheritance:r /grant:r "$($env:USERNAME):(OI)(CI)F" | Out-Null
"Created $ks and signing.json in $dir (readable only by $env:USERNAME). Back up the .keystore and the password."
