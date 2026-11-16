# Unity official tooling — capability audit (MuseDesk)

Date: 2026-11-16. Branch `feature/rc8-feedback-1`, HEAD `1299893` (rc14 owner fixes on top of the rc13 candidate `42efeff`).
Scope: read-only discovery plus small inspection tests against the running Editor. **Nothing was installed or upgraded.**
No package, build setting, scene or credential was changed by this audit. The rc13 APK is untouched.

**Decision: keep the existing automation; it already *is* the official CLI + Pipeline. Adopt five more read-only official commands now (section G). No installation is needed or proposed.**

---

## A. What we have (verified)

| Item | Finding | How verified |
|---|---|---|
| `unity` executable | **Official Unity CLI**, `C:\Users\stoof\AppData\Local\Unity\bin\unity.exe`, version `1.0.0-beta.17` | `Get-Command unity -All`, `where.exe unity`, `unity --version`, `unity --help` (Unity's own command tree: editors, projects, pipeline, command, recompile, status, build, test, mcp…) |
| Pipeline package | **Installed**: `com.unity.pipeline` `1.0.0-pre.1` (direct dependency in `Packages/manifest.json`, registry source) | manifest + packages-lock |
| Editor connection | **Connected**: `unity status` → port 7800, state ready, project `C:\dev\MuseDesk\MuseDesk`, Unity 6000.6.8f1, PID 30296. `unity pipeline editors` → hasPipelinePackage true, serverReachable true, safeMode empty, **updateAvailable true (deliberately not taken)** | commands run |
| Command surface | 41 command groups through `unity command` (gameobject, component, scene, prefab, animation.animator, animation.clip, diagnostics/profiler, script, console, capture, test, build, asset, settings.*, …) | `unity command`, `unity command <group> --json` |
| Official Claude Code plugin | **Installed, user scope**: `unity@unity-agent-plugin` `0.1.9-beta` (Unity Technologies), enabled in `~/.claude/settings.json`; skills appear in the session as `unity:unity-pipeline`, `unity:unity-cli`, `unity:ui`, etc. Antigravity and Codex manifests ship in the same marketplace checkout. | `~/.claude/plugins/...`, settings.json |
| MCP | No project `.mcp.json`; `unity mcp` exists in the CLI (server/client configuration) but is **not configured** and not needed (the CLI already reaches the Editor) | `Test-Path .mcp.json`, `unity mcp --help` |
| Unity AI in-editor Assistant | **Not installed** (no `com.unity.ai.assistant`/Muse package; only `com.unity.ai.navigation`, which is NavMesh) | manifest grep |
| Custom automation | Thin wrappers **on top of the official CLI**: `scripts/drv.ps1` = `unity command script run --file AgentScripts/<Class>.cs --entry <Class>.<Method>`; `compile.ps1` = `script recompile` + `recompile_status` + `console status/get`; `tests.ps1` = `unity command test run --mode editor`; `run_guarded.ps1` = `script run` of `AgentScripts/_GuardedRun.cs` (MainMRScene-atomic). Plus ~100 `AgentScripts/` builders/drivers, `backend_tests.ps1`, `quest_perf_watch.ps1` (adb logcat), `quest_run.ps1`. | read the scripts |

**Classification: F — multiple overlapping systems, all official underneath**: C (official CLI with the Pipeline package) + E (official agent plugin skills) + our project wrappers built on C. There is no third-party Unity MCP and no custom Editor command server.

The premise "we might be using a custom CLI" is false: every Play Mode driver, build, compile and test result in this project's evidence docs already went through the official Pipeline server.

## B. Official capabilities confirmed (this session, against the live Editor, MainMRScene, Play Mode with a portal visitor out)

| Command | Read-only | Time | Result | Limits |
|---|---|---|---|---|
| `unity status` / `unity pipeline editors` | yes | <1 s | instance table (port, PID, version, reachability, update flag) | `unity pipeline list` (documented by the plugin skill) is **unknown** in CLI beta.17; `pipeline editors` is the working equivalent |
| `gameobject find --name PortalProjection_developer` | yes | 0.65 s | structured identity: globalId, instanceId, `hierarchyPath=/Spatial/WallHabitat/LargeAgentPortal/PortalProjection_developer` | exact-name match only; no subtree filter |
| `scene get_hierarchy` | yes | 0.72 s | the whole active scene as JSON: every node's name, activeSelf, **component type list**, children | 1.5 MB for MainMRScene: must be filtered locally (we did, in PowerShell); **no transforms** in it |
| `component get_fields --target <path> --component Animator --detail value` | yes | 0.84 s | Avatar = `Agent_Developer.fbx`, Controller = `MuseDesk_Agent.controller`, **CullingMode = Always Animate** (confirms today's fix on the live clone), root motion off | serialized fields only |
| `animation.animator get_controller --controller Assets/MuseDesk/Art/Controllers/MuseDesk_Agent.controller` | yes | 0.65 s | parameters (MoveSpeed, IsCarryingPaper); layers Base (77 states, default Idle_Breathing), UpperBody (16), HeadGaze (7); transitions with conditions | state motions are given by name; clip asset needs its GUID from the controller |
| `animation.clip get --clip GlobalObjectId_V1-1-<guid>-<fileId>-0` | yes | ~0.7 s | `Listening`: 1.96 s, 410 curve bindings over 41 bones, incl. Spine/Chest/Neck/Head | a clip inside an FBX must be addressed by GlobalObjectId (a path resolves to the model and fails, exit 2) |
| `component get_fields` on a TextMeshPro | yes | 0.57 s | text, fontSize, autoSize, wrapping, overflow mode, font asset path | **no laid-out bounds / overflow result** (runtime-computed, not serialized) |
| `diagnostics get_performance` | yes | 0.63 s | draw calls, batches, SetPass, tris, memory, CPU/GPU frame time | **Editor numbers, not Quest** |
| `diagnostics profiler_targets` | yes | <1 s | only `Editor` is connectable | Quest needs a **Development** build with profiler autoconnect; our release APKs are not |
| `script eval --file AgentScripts/BoneWriteProbe.cs` | nudges 4 bones once, restores | ~0.5 s | proved the visitor Animator **does** rewrite Head/Neck/Chest/Spine every frame in the Editor | arbitrary-code capability; same gate as `script run` |
| `capture game_view --camera __ShotCam` | yes | ~1 s | PNG of a chosen camera (used for every capture in this project) | we already used it |

Evidence label for everything above: **Pipeline connected + Editor object inspected (PLAY, Editor)**. None of it is headset proof.

## C. MuseDesk-specific findings

- **Wall + elevator (Test A).** Filtering `get_hierarchy` for `/Spatial/WallHabitat/*` found 128 matching nodes in one call: `WallHabitat_Full/ElevatorCabin` (+ CabinFloor/Roof/Sides), `Door_Lower/Mid/Manager_L/R`, the four `Avatar_*` with Animators, and the Compact helper copy (`WallArtHelper/WallHabitat_Compact`, placeholder avatars, as designed). Positions/anchors are **not** in that output: one `component get_fields` per transform (~0.7 s each). `AgentScripts/WallArtFitCheck.cs` (one `script run`) remains the right tool for geometry/socket fit; the official calls are good for "does it exist / what is on it".
- **Portal + visitor (Test B).** One find + the hierarchy filter showed the full stage: `LargeAgentPortal` → 12 legacy ring segments + `RingSmooth`, `PortalPad` (Animator, `WallHabitatLargePortalVisualController`, `WallHabitatSocketRegistry`; `WallHabitat_LargeAgentPortal_Mesh` skinned; `VFX_Portal_Large_Full` ParticleSystem), `PortalColumn` (inactive while conversing, correct), the new `VisitorPlatform` Body/Rim/FloorGlow, and `PortalProjection_developer` (Animator + `PortalVisitorLife`, **no colliders**: correct, clones are stripped and touch is fingertip-sensed). Real room clearance, finger touch, comfort and frame rate still need the headset.
- **Animation (Test C).** Controller structure and clip bindings are readable without custom code. The visitor uses `MuseDesk_Agent.controller` (shared with the desk agents), Avatar from the role's FBX. This inspection is what corrected our own diagnosis of the owner's "head spinning" report (see `rc14` notes): in the Editor the Animator does write the head bones, so the rc14 changes are defensive for the device (culling, behind-flip, turn-rate cap, lean cap, bone guard) and the spin is **unverified until worn**.
- **Manager/UI (Test D).** TMP fields (text, font, size, wrap, overflow) are readable; computed layout (rendered bounds, truncation, key overlap) is not. Our `PortalFxDrive.MeasureKeys` (renderer bounds in card space) is what caught the overlapping keys in rc12; keep it.
- **Profiler (Test E).** The official profiler commands (record, summary, frame breakdown, markers, memory snapshot) are the most valuable new capability we are *not* using yet, but they need a **Development** Quest build connected to the Profiler. No Quest capture exists; none was fabricated. Owner path today: `scripts\quest_perf_watch.ps1 -Label rc14_worn` (logcat FPS, already proven on rc9/rc11). Post-submission: build a Development APK, `diagnostics profiler_connect` to it, `profiler_record`, then `profiler_summary --sort gpu` and `profiler_markers` for the portal scenario.

## D. Existing vs official workflow (same task)

Task: "List the portal visitor hierarchy, identify its Animator Controller, report colliders and attachment transforms, flag missing references."

| | Existing (custom C# driver via `script run`) | Official commands only |
|---|---|---|
| Commands | 1 (`drv.ps1 PortalFxDrive Renderers/Where/State`) once the driver exists | 4–5 (`gameobject find`, `scene get_hierarchy` + local filter, `component get_fields` ×n, `animator get_controller`) |
| New C# | yes, the first time (we have ~100 such drivers already) | none |
| Recompile / domain reload | no (in-memory compile) | no |
| Correct project | `--project-path .` | same |
| Structured output | our own text | JSON with re-usable handles |
| Transforms / world positions | yes, any frame | one call per object |
| Runtime state (visit state, life clip, lean, hidden flags) | yes (we read our own properties) | **no** (only serialized fields) |
| Editor disruption | none | none |
| Repeatable | yes | yes |

Verdict: for *existence, components, serialized references, animator and clip structure* the official commands are faster to reach for (no code). For *runtime behaviour and geometry* our `script run` drivers stay better. They are the same transport; use both.

## E. Installation risk

None proposed. Nothing to install: CLI, Pipeline and plugin are already present. Explicitly **not** done: `unity pipeline install`/update (an update is available: it would change `Packages/` and recompile — defer to after 2026-11-18), MCP configuration, Unity AI Assistant (credit-based, not needed: we want Editor access, which we already have).

## F. Recommendation

**Keep the existing automation until after submission — it already is the official CLI + Pipeline — and use the read-only official commands in section G for inspection from now on.** No isolated pilot is needed because nothing new has to be installed. Re-evaluate the Pipeline update and Development-build profiling after 2026-11-18.

## G. Verified commands for Claude and Antigravity (one Editor driver at a time; read-only)

```powershell
unity status                                   # which Editor, port, state
unity pipeline editors                         # (NOT 'unity pipeline list' in CLI 1.0.0-beta.17)
unity command --project-path . editor status   # also reports blocked_by_dialog
unity command --project-path . gameobject find --name <ExactName> --json
unity command --project-path . scene get_hierarchy --json        # large: save to a file and filter locally
unity command --project-path . component get_fields --target "<hierarchyPath>" --component <Type> --detail value --json
unity command --project-path . animation.animator get_controller --controller Assets/MuseDesk/Art/Controllers/MuseDesk_Agent.controller --json
unity command --project-path . animation.clip get --clip "GlobalObjectId_V1-1-<guid>-<fileId>-0" --json   # clips inside FBX
unity command --project-path . diagnostics get_performance --result-only                                   # Editor only
unity command --project-path . console status --result-only
```

Gotcha seen in this session: Play Mode drivers time out in wall-clock seconds; if the Editor's player loop stalls
(`Time.unscaledDeltaTime` 0 while `Time.frameCount` climbs), every timed step fails. Check with
`script eval --file` returning `Time.unscaledTime`/`frameCount` twice a few seconds apart before trusting a driver
failure. `editor set_auto_tick --enabled true|false` (persisted) did not change it here; an Editor restart is next.

Existing project entry points stay the default for anything stateful: `scripts\compile.ps1`, `scripts\tests.ps1`, `scripts\drv.ps1 <Class> <Method> [arg]`, `scripts\run_guarded.ps1`. Rules unchanged: never edit `Assets/` scripts in Play Mode or during an APK build; one agent drives the shared Editor at a time; never print `ProjectSettings/wit.config`, the Wit asset or `backend/.env`.
