# Final capture instructions

**Order and content:** `FINAL_VIDEO_SHOT_LEDGER.md`. **Honesty rules:** `DEMO_STORYBOARD.md`.

**Tools on this PC (checked 2026-11-16):** Meta Quest Developer Hub (MQDH) is installed. OBS and scrcpy are not; nothing new
needs installing.

## A. Quest first-person footage (real interactions)

### 1. Capture size (optional, before the session)

`scripts\record_preflight.ps1 -CaptureProps` asks the Quest to record 1920×1080 at a higher bitrate:
`debug.oculus.capture.width/height/bitrate` and `debug.oculus.fullRateCapture`.
- These are developer properties. They reset on reboot.
- Their effect differs between OS versions, so **record a 10-second test clip and check** its size, framerate, and that the
  passthrough room and the app both appear.
- If the clip is wrong, reboot the Quest to undo them and use the default capture.

### 2. Record

- **On the headset (most reliable):** Meta button → Camera → **Record video**; stop the same way.
- **From the PC:** MQDH → your device → **Cast**. The cast window can be screen-recorded (see B.3), but its quality is lower:
  use it only as a fallback.

### 3. Pull the clips to the PC

- `scripts\record_preflight.ps1 -Pull` copies `/sdcard/Oculus/VideoShots` into `Captures\quest\<date>`.
- `Captures/` is outside source control.

### 4. Filming tips

- Seated, with slow head motion (fast turns look bad in first person).
- Hold each readable thing for **2 s**: the paper, the footer, the status rows, the receipt.
- Keep your hands in frame for the paper, the stamp, the beam and the pinch.
- Before a take, run `scripts\record_preflight.ps1`. It checks the build hash, battery, storage, the backend and `adb reverse`.
- **The Quest is 95% full** (25 GB free). That's enough, but delete old clips after pulling.

## B. In-engine views (Editor; the same scene and models as the APK)

These are for close-ups and tracking shots that a seated user can't take. They're allowed when the scene state is real:
- an offline (SCRIPTED DEMO) run; or
- a live run in the Editor, with the backend on and the Editor session live.

### 1. Start a run in Play Mode

- **Offline:** `scripts\drv.ps1 LivingOfficeDrive RunOffline`.
- **Live:** with the backend running, `LivingOfficeDrive RunLive`.
- To place the Wall first: `PortalFxDrive Setup` then `PortalFxDrive Place`.

### 2. Choose a camera

`AgentScripts/CineCam.cs` is a capture-only camera, created at run time in the Editor. It is not in the APK and doesn't touch
the XR rig.
- **Presets:** `unity command script run --file AgentScripts/CineCam.cs --entry CineCam.Shot --args '[\"<preset>\"]'`
  - `desk_wide`, `desk_agents`, `wall_wide`, `wall_manager`, `elevator`, `printer`, `portal_side`.
- **Slow move between two presets:** `CineCam.Dolly` with `'[\"wall_wide\",\"wall_manager\",8]'` (8 s).
- **Back to normal:** `CineCam.Off`.
- The Game view shows the CineCam picture while it is on: set the Game view to **1920×1080 (16:9)**.

### 3. Record the Game view

- **Windows Snipping Tool → Record** (Win+Shift+R): drag a rectangle around the Game view only.
- **Or Xbox Game Bar** (Win+Alt+R): records the whole Unity window; crop it in the edit.
- **Stills:** `unity command capture game_view --camera __CineCam --output-path Captures\stills\<name>.png`.

### 4. Background

The Editor has no passthrough room, so the background is a calm neutral colour.
- Use in-engine shots for close-ups and the miniature "fly-through".
- Use Quest footage for anything that shows the real room.

## C. PC cutaway (shot 8: the real result)

After the live run:
1. Open a terminal with a large font (Ctrl+Plus).
2. `cd backend\sandbox_workspaces\plans` then `dir /od` (the newest folder is the run).
3. `cd <that folder>` → `type login_page.py | more` (or the file named on the receipt).
4. `..\..\..\.venv\Scripts\python.exe -I -B -m pytest -q`. The counts must equal the card's.

Record it with the Snipping Tool (B.3). Caption: "The same folder on the PC: real files, real pytest."

## D. External footage (shots 1 and 9)

- A second person films you at the desk, from the side and over the shoulder, with the wall in view.
- Use natural light and avoid filming the real monitor's content (logos).
- Then film the same moves you made in the Quest takes, so the edit can match the two.

## E. Sound and music

- Original or licensed music only; no commercial tracks without permission.
- **In-app sounds:** the stamp, print tick, elevator ding, portal and receipt are already in the build. The Quest recording
  captures them; keep them low under the music.
- **Voice-over:** keep it minimal (the brief's six lines). Never say the offline demo is live.

## F. Do not

- Show debug overlays or the Unity console.
- Crop away a failed interaction and present the rest as working.
- Reuse an earlier run's numbers in a new run's shot without a caption.
