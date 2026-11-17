# Final polish fix log (rc17 → final release)

Every change after rc17 (`7220f0a`, APK `BD0204F4…`), with its evidence. The earlier fixes F-1…F-18 are in
`END_TO_END_GAPS_AND_FIXES.md`.

**Evidence levels:** AUTO, PLAY (Editor), LIVE (backend + Meta model), WORN (owner). Nothing below is WORN yet.

## Product code (goes into the next APK)

| # | Defect (how found) | Fix | Evidence | Level |
|---|---|---|---|---|
| P-1 | Leaving Wall mode while the Manager's summary receipt was being delivered logged `Cannot set the parent of the GameObject 'ReceiptFlyer' while activating or deactivating the parent GameObject 'WallHabitat'`. The receipt flight was started under a habitat that was being switched off. Found by the new preflight's real-error count. | `WallHabitatController.Manager.cs`: a flight requested while the habitat is inactive is deferred and starts on its next tick; a reset clears it | Preflight "Apply change" group: real console errors 2 → **0**; EditMode 1636/1636 | PLAY + AUTO |
| P-2 | **Release blocker:** rc17 is signed with the Android **debug** key, versionCode 1; a Meta release channel needs release signing | `AgentScripts/BuildApkTo.RunSigned(path, versionCode)` reads `%USERPROFILE%\.musedesk\signing.json`, applies it in memory for the build, and restores ProjectSettings. `scripts/make_release_keystore.ps1` is for the owner to run once. `.gitignore`: `*.keystore`, `*.jks`, `signing.json`. Also: `Run` refuses any existing APK path and protects rc17 | Manifest checked: release build, not debuggable, VR launcher category, Quest supported devices. **Key: owner action** | AUTO |

## Tools and verification (Editor and PC only; never in the APK)

| # | What | Why |
|---|---|---|
| T-1 | `scripts/preflight.ps1` | One command for every automated group (13), with a ledger. Waits out domain reloads; detects the Editor clock stall (BLOCKED, not FAIL); counts real errors apart from Meta SDK Editor-only messages (`AgentScripts/LogTally.cs`); always leaves Play Mode before EditMode (a test run in Play Mode deadlocked the Editor once this session) |
| T-2 | `scripts/record_preflight.ps1` | Before each take: installed build hash, battery, storage, display mode, backend, `adb reverse`, PC RAM. `-Restart` gives a clean take, `-CaptureProps` the 1080p capture props, `-Pull` copies the clips |
| T-3 | `AgentScripts/CineCam.cs` | Capture-only in-engine camera: 7 presets + dolly; renders to the Game view only (not to the headset); turns the desk nameplates to the film camera while on and hands them back on Off |
| T-4 | `backend/scripts/live_real_work.py` R7–R9 + markdown evidence | Brief §9: "what have you changed?", the change request, Apply change (exactly one step, really worked) |
| T-5 | `AgentScripts/ClockProbe.cs`, `FindText.cs`, `InFront.cs`, `PlaqueClear.cs` | Diagnostics used in this pass |

## Checked and deliberately NOT changed

| Finding | Decision |
|---|---|
| The desk mode plaque's word looked hidden behind the stamp domes in an in-engine wide shot | From the true seated eye (WorkspaceRoot (0, 0.30, 0.62)) the word sits above the domes (3–9 mm clear), and the dock's own SCRIPTED DEMO chip is visible. Not a product defect; film desk wides from eye height or above |
| 85,000 Editor warnings `[OVRPlugin] XR_ERROR_ACTIONSET_NOT_ATTACHED` and 7 Meta SDK errors | Editor-only (no headset over Link); absent from the Quest's own log (rc15 session) |
| "Stuck" Ceiling transition in the first preflight | The Editor's Play Mode clock had stalled; with a running clock the transition takes about 6 s (PLAY) |
| Wall Manager close-up showed a lavender card at its face | It is the plan card the Manager carries between rides; the straight-on desk close-up is clean |
| Running model code in WSL 2 / a container | Documented in `FINAL_LIVE_DEMO_EVIDENCE.md` §Security; not done under the deadline (no Docker, 0.5 GB free RAM, needs testing) |

## Regression after these changes

| Suite | Result |
|---|---|
| Preflight, all 13 groups (`Logs/preflight/7220f0a_20261116_2222.md`) | **13/13 PASS**, before P-1 |
| Apply-change group after P-1 | PASS, **0 real errors** |
| EditMode after P-1 | **1636/1636** |
| Backend pytest | **371 passed**, 1 skipped |
| LIVE `live_real_work.py` (extended, 13 checks) | **13/13** (`FINAL_LIVE_DEMO_EVIDENCE.md`) |
