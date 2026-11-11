# Quest 3 + Windows development station: complete setup guide

**Audience:** human developer or build/debug agent on a Windows 10/11 computer. **Outcome:** a Unity mixed-reality APK launches on a Quest 3, accepts tracked hands, talks to a Python server on your PC, and provides repeatable on-device debugging.

## A. What you need

- Meta Quest 3, controllers initially for headset system settings (the delivered app itself must be hands-first).
- Windows development computer: 16 GB RAM minimum for Unity workflow per Meta's quickstart; dedicated GPU recommended, preferably 32 GB RAM if running Unity and multiple coding agents simultaneously.
- Reliable **USB-C data cable** (not charge-only). For performance/fast data transfer, Meta recommends 5 Gbps or better.
- Headset battery/charger, safe desk with enough light for hand tracking, a phone with Meta Horizon app, and a verified Meta developer account.
- For direct PC-connected app mode without USB, PC and Quest on the same trusted local network; do not assume campus, guest or enterprise Wi-Fi allows device-to-device connections.
- Accounts: verified Meta developer account and Meta VR Start membership (competition eligibility), Unity ID, Devpost account, optionally GitHub and LLM provider.

## B. Install Windows tools (in order)

1. Install **Git for Windows**, **Visual Studio Code** (or your editor), and **Python 3.12** (or an approved recent 3.x version compatible with backend dependencies). Confirm in PowerShell: `git --version`, `py -3.12 --version`, `python --version`.
2. Install **Unity Hub**. Through Hub, install **Unity Editor 6000.0.66f2 or a later verified compatible Unity 6 version** (choose one and freeze it for the team). Select modules **Android Build Support**, **OpenJDK**, **Android SDK & NDK Tools**. Unity 6.1+ shows Meta Quest as its own platform in Build Profiles; for older Unity 6 use Android platform.
3. Install **Meta VR Developer Hub (MVDH)**, the tool formerly called MQDH. Sign in with the same developer identity as on Quest. It provides device management, casting, logs, APK deploy, capture, and performance tools.
4. Install **Node.js 18+** (optional but strongly useful for Meta VR CLI with AI agents). Confirm `node --version`, `npm --version`. Node is not required for Unity itself.
5. Optional but recommended: install **Meta XR Simulator** desktop application. Current simulator is standalone, not the old `com.meta.xr.simulator` Unity package. Use it to test interactions without wearing a headset, but **not** as the final release test.
6. Create short clean project path, e.g. `C:\dev\musedesk`; avoid syncing Unity Library/Temp folders through OneDrive.

## C. Enable Quest 3 developer mode

1. Register/verify a Meta developer account at https://developers.meta.com/.
2. Pair your Quest 3 in the **Meta Horizon** phone application.
3. In the mobile app, open the paired headset and switch on **Developer Mode**. The exact navigation label may vary by app update (typically device settings → Developer Mode).
4. Put the headset on; navigate to Developer settings. Enable the requested debugging options. A current Meta guide also directs developers to enable **MTP Notification** in the headset's Developer settings.
5. Connect the Quest to your PC with the USB-C **data** cable.
6. In the headset, accept **Allow USB debugging** and select **Always allow from this computer** for your personal trusted development machine. Do not grant this blindly on shared/public computers.
7. In MVDH → Device Manager, verify the Quest shows **Active**.

### Windows ADB driver and verification

If `adb devices` is empty, Meta's software setup instructs Windows users to install its **Oculus ADB driver** (`oculus-adb-driver-2.0`, `android_winusb.inf`) as described in official Meta docs.

ADB ships with Unity's Android SDK tools (example location):

```powershell
# Replace <UnityVersion> with the exact editor folder installed by Hub.
$platformTools = 'C:\Program Files\Unity\Hub\Editor\<UnityVersion>\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools'
& "$platformTools\adb.exe" devices
```

Optionally add `platform-tools` to PATH, restart terminal, and run:

```powershell
adb version
adb devices
```

Expected: a serial number followed by `device`. If `unauthorized`, put the Quest on and approve the prompt. If empty, check cable, USB port, driver, developer mode, and MVDH.

## D. Create and configure Unity project (recommended official path)

1. Open **MVDH → XR Projects → Create Project**.
2. Choose a name such as `MuseDeskQuest`; editor: your pinned Unity 6 version; XR Plugin: **OpenXR**; SDKs: **All**; confirm URP/Universal 3D setup. Create project under `C:\dev\musedesk\quest`.
3. In Unity remove the ordinary default Main Camera if an Interaction SDK rig will supply the camera. **Do not create two camera rigs.**
4. Choose **File → Build Profiles**, ensure **Meta Quest** (or Android if applicable) platform is installed and active.
5. Open **Edit → Project Settings → XR Plug-in Management → OpenXR**. Enable **Meta XR** feature group for Android and Standalone as documented by the current SDK; in `Window → Meta → Tools → Project Setup Tool` fix all required issues for both targets.
6. If using the manual path rather than MVDH template, install the Meta All-in-One SDK through Package Manager → Add Package by Name → `com.meta.xr.sdk.all`; accept Meta XR feature installation. Confirm dependencies and package lock after opening project.
7. Follow official **Interaction SDK Getting Started**; add **one** `OVRComprehensiveInteractionRig` (or the current recommended equivalent from the installed sample), not a second ordinary camera. Enable hands and controllers for dev flexibility but ensure hands alone work.
8. Add the **Passthrough** building block or configure the current `OVRPassthroughLayer` according to Meta's passthrough sample. The MR scene must use real passthrough, not a black VR background.
9. Add **MRUK** scene manager and configure spatial data permission at runtime. In the headset, complete **Settings → Environment Setup → Space Setup** before relying on desk scene queries. Test a simple colored cube at a detected table coordinate. Implement **manual tray placement** for denied/missing scene data.
10. Save `Assets/Scenes/MainMRScene.unity`, add to Build Profiles scene list, and commit `Packages/manifest.json`, `Packages/packages-lock.json`, `ProjectSettings/`, `Assets/**/*.meta`, your scene, and prefabs. Do not commit `Library/`, `Temp/`, or build artifacts by default.

### Hands-first first-run smoke test

- Quest passthrough shows your actual room.
- Hand models or hand interaction pointers are visible with controllers set aside.
- A cube can be grabbed by pinch and released.
- A world-space UI button can be selected by hand.
- The workspace remains in place when head moves.

For grab prototype, Interaction SDK offers `Right-click object → Interaction SDK → Add Grab Interaction` and a wizard to add the colliders/rigidbody/interactors needed. Test before coding the custom document system.

## E. Build and deploy APK

1. Plug in headset; MVDH shows active.
2. In Unity Build Profiles select Meta Quest/Android. Use Project Setup Tool to resolve required issues. Configure package identifier e.g. `com.yourteam.musedesk` (replace `yourteam` with your own namespace), IL2CPP/ARM64 and Android options per SDK validation recommendations.
3. Click **Build and Run**; output debug APK under `Builds/Quest/` outside `Assets/`.
4. Alternatively build to APK, then from terminal:

```powershell
adb install -r .\Builds\Quest\MuseDeskQuest.apk
adb logcat -s Unity AndroidRuntime
```

`Build and Run` is preferred for earliest tests. A debug APK installed by ADB is not automatically the signed and submitted Competition release. For final publication, follow Meta Developer Dashboard signing/upload guidance.

### Useful daily development loop

1. Start `backend` using setup below.
2. Run Unity Editor with Meta XR Simulator for fast layout iteration when suitable.
3. Build and Run to Quest for actual hand tracking, passthrough, and document feel.
4. Open MVDH casting + logs; capture screenshot and FPS metrics.
5. Save evidence and exact reproduction steps in your ticket/PR.

## F. Set up the Windows companion/backend

Make one isolated folder `C:\dev\musedesk\backend` after backend scaffold is committed, keep repository samples in `C:\dev\musedesk\samples` or a temp workspace. Typical PowerShell bootstrap:

```powershell
cd C:\dev\musedesk\backend
py -3.12 -m venv .venv
.\.venv\Scripts\python.exe -m pip install --upgrade pip
# Once TASK-04 creates pyproject.toml:
.\.venv\Scripts\python.exe -m pip install -e ".[dev]"
Copy-Item ..\.env.example .\.env -ErrorAction SilentlyContinue
# Once TASK-04 creates app.main:
.\.venv\Scripts\python.exe -m uvicorn app.main:app --host 127.0.0.1 --port 8765 --reload
```

If PowerShell prevents `Activate.ps1`, the explicit `.venv\Scripts\python.exe` commands above work without changing execution policy.

Verify from PC:

```powershell
curl.exe http://127.0.0.1:8765/health
```

Expected response after TASK-04 implementation: JSON health and API version. The implementation pack contains **specifications, not a completed FastAPI application**; the command starts working after TASK-04 is implemented.

### Option 1: Quest connected by USB (best initial development mode)

ADB can reverse a device localhost port into the Windows service on a cable-connected Quest:

```powershell
adb reverse tcp:8765 tcp:8765
adb reverse --list
```

Set Unity development endpoint to `http://127.0.0.1:8765` and WebSocket endpoint to `ws://127.0.0.1:8765` while tethered. This works only while the ADB transport is connected; verify on the device. If the Android app blocks cleartext HTTP/WS, configure a **development-only Android network security exception**; do not carry it into public production builds.

### Option 2: Quest over a trusted local network

1. Connect Windows PC and Quest to same non-isolated Wi-Fi LAN.
2. Find PC IPv4 address via `ipconfig` (e.g. `192.168.x.y`).
3. Start the API with `--host 0.0.0.0` (bound to all PC interfaces) **only for trusted testing**.
4. Use a Windows Defender inbound rule allowing TCP port 8765 on **Private network only**, ideally scoped to the Quest's specific IP or local subnet.
5. Configure Quest app endpoint `http://PC_LAN_IP:8765` with matching `ws://` URL.
6. Include short-lived per-session pairing tokens in app requests; never use unauthenticated workstation endpoints on shared/campus network. Disconnect VPN or address AP client isolation if you cannot connect.

**Do not** expose 8765 to the public internet or let an anonymous Quest client run unrestricted shell commands on your PC.

### Option 3: hosted demonstration (stretch)

Cloud `https://...` + `wss://...` endpoint and isolated per-session sample repositories. This is much more work and requires quota, secrets, cleanups, budget guardrails and end-to-end deployment testing. Prefer robust offline judged demo plus live local demonstration for the deadline.

## G. Meta tooling for AI agents (highly recommended)

Meta's late-2026 **Meta VR CLI** connects AI coding agents to docs, builds, connected Quest devices and diagnostics via MCP. This is directly useful because your coding agents can inspect Unity/Quest problems instead of guessing.

**Windows official native installer (review remote scripts before executing):**

```powershell
iwr -useb https://developers.meta.com/horizon/install-cli/windows/ | iex
metavr --version
metavr doctor
metavr init
```

**Alternative with Node.js 18+:**

```powershell
npx metavr@latest init
npx metavr@latest device list
```

Use `metavr init --codex` or `metavr init --cursor` to configure a specific available coding agent; CLI also supports Claude Code, Gemini and others. You can explicitly register MCP with `metavr mcp install codex` or `metavr mcp install cursor`. Follow official docs for current flags; after `metavr init`, do not duplicate the same MCP server by installing the plugin again without checking.

**Practical development task to give a coding agent:** “Using Meta VR CLI and the current Unity project, verify Meta XR/OpenXR requirements, deploy the debug APK to my connected Quest 3, retrieve errors and FPS information, and propose the smallest fix. Do not alter scene files without approval.”

For optional Unity automation, current Meta docs refer to Unity Pipeline CLI and Meta XR Operator, but these are not essential for MVP. If using experimental Meta XR Operator, pin version and treat its APIs as subject to change.

## H. Build the *miniature developer station* inside Quest

The **developer station in the virtual scene** is different from the real Windows development computer. Implement it as a prefab:

```text
DevStation (WorkspaceRoot local position, small scale)
├── DeskMesh
├── MiniLaptop
│   ├── ScreenCanvas (state text / progress)
│   └── LEDStatus
├── TinyKeyboard (cosmetic)
├── DevAgentSpawn
├── DevAgentAnimRoot
├── HandoffAnchor
└── InteractionTarget (optional inspect agent status)
```

Minimum features: status text `Coding / Waiting for approval / Testing / Completed`, animated typing only while corresponding code worker executes, receipt of researcher handoff after an actual resolved decision, safe low-poly meshes and no desktop streaming. The same pattern applies to `ResearchStation` and `QAStation`.

## I. Common failure and fixes

| Symptom | First checks |
|---|---|
| Quest not visible in MVDH/ADB | Data cable, headset developer mode, USB permission prompt, Oculus Windows ADB driver, restart ADB |
| `unauthorized` in `adb devices` | Put headset on, allow USB debugging from trusted machine |
| Unity cannot build Android | Verify installed Android SDK/NDK/JDK via Unity Hub; active Quest/Android build profile |
| Blank XR scene or two viewpoints | Remove duplicate Main Camera/rig, check OpenXR Meta features and project validation |
| Passthrough opaque/black | Check current Meta passthrough building block/layer settings and standalone Quest build |
| Hands not appearing | Set Quest hand tracking ON, enable hands+controllers in rig, put controllers down, validate Interaction SDK components |
| No table anchors | Complete Space Setup; request spatial data permission; verify MRUK scene loaded; fall back to manual tray |
| Works in Editor but not Quest | Android permissions, ARM64/player settings, APK logs, API endpoint (PC `localhost` is not device localhost without `adb reverse`) |
| WebSocket cannot reach PC | `adb reverse`, wrong IP, Windows firewall, guest Wi-Fi isolation/VPN, cleartext Android policy |
| VR performance weak | Reduce shadows, geometry, overdraw, canvas rebuilds, dynamic lights, update frequency; profile on Quest |
| Git merges scramble scene | Assign one scene owner; share prefabs; never hand-edit `.unity` YAML without necessity |
| Judge APK cannot connect | Switch to labeled standalone offline demo or properly hosted backend; never depend on your PC |

## J. External documentation (current as of Nov 10, 2026)

- Unity software prerequisites: https://developers.meta.com/vr/documentation/unity/unity-software-setup/
- MVDH project setup: https://developers.meta.com/vr/documentation/unity/unity-project-setup/
- Hands setup: https://developers.meta.com/vr/documentation/unity/unity-handtracking-hands-setup/
- Passthrough: https://developers.meta.com/vr/documentation/unity/unity-passthrough-gs/
- MRUK scene: https://developers.meta.com/vr/documentation/unity/unity-mr-utility-kit-manage-scene-data/
- Spatial permission: https://developers.meta.com/vr/documentation/unity/unity-spatial-data-perm/
- Grab quick actions: https://developers.meta.com/vr/documentation/unity/unity-isdk-create-grabbable-object/
- Meta XR Simulator: https://developers.meta.com/vr/documentation/unity/unity-simulate-xrsim/
- Meta VR CLI install: https://developers.meta.com/vr/essentials/metavr-install/

## K. Optional Quest Link for faster Editor iteration

Meta also supports using Quest hand tracking while developing in the Unity Editor on Windows through **Meta Horizon Link**, depending on the supported OpenXR/Meta XR configuration. This can reduce APK build cycles for some interaction tests. Treat it as an optional acceleration path, not a requirement, and still verify passthrough, hand tracking, desk placement and frame rate in an actual Quest APK. Official reference: https://developers.meta.com/vr/documentation/unity/unity-handtracking-overview/

## L. Practical layout of your real development station

On your Windows desk, arrange **Unity Editor** (XR scene/game view), **code editor** (backend, scripts, agent prompts), **MVDH** (Quest logs, casting, profiling), and **PowerShell** (backend server + `adb`) so you can debug across boundaries. Use a powered headset/USB-C cable for long sessions, a safe unobstructed desk area for hand interactions, and regular short on-device checks instead of assuming PC simulator parity. Track one reproducible known-good APK/build number daily.
