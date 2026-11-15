# A13 Performance (code-level static pass)

Status: static scan only. Nothing compiled, nothing run, no Quest measurements exist. Do not quote any figure from this sprint as device performance.

## Scope
Scanned `Assets/MuseDesk/Scripts/` and `Assets/MuseDesk/Phase2/Runtime/` for per-frame cost (GetComponent/Find*, Camera.main, LINQ, string building into TMP, `.material`, Resources.Load, reflection, unthrottled physics queries, repeated coroutines). Excluded (owned by other agents this sprint, so NOT scanned for fixes): GrabHoverHighlight, WallUi, WallHabitatController*, AgentAttention, AgentAnimatorBridge, AgentMovement, AgentRegistry, FeedbackController, TaskCardView, TaskDragController, BoardPresenter, WorkstationStatusDisplay, ReleaseConfigApplier, Scripts/Voice, speech bubble/receipt/archive/voice HUD.

## Findings, ranked by likely Quest impact
1. **Draw calls / SRP Batcher breaks (rendering, not scripts)** - highest likely impact on Quest 3, unmeasured. Any `renderer.material` access creates a unique material instance (fine for batching under SRP Batcher if the shader is compatible, but extra memory and GC). Remaining instance-material uses: `CardRoleTint.Apply` (once per agent change, per card) and `TaskCardView` line ~112 `statusMarker.material` (owned elsewhere). Recommend MPB or a small pooled set of shared materials per role colour.
2. **TMP world-space text count and overdraw** - many TMP labels at scale 0.01 on boards/cards/stations. Each text mesh rebuild on `.text` change is costly. Updates found are event-driven (card Bind, DecisionPanel.Bind) rather than per-frame: OK. Recommend: keep label count low, disable `Extra Padding`, `Raycast Target` off on TMP, avoid rich-text `<size>` where not needed (AgentView label).
3. **Per-frame reflection boxing** - `HeldPaperController.Update` called `PropertyInfo.GetValue` every frame (boxed int, ~24 B/frame, plus reflection overhead on IL2CPP). FIXED.
4. **Always-on cosmetic Update loops per agent** - `AgentView.Update` (trig, 2 Slerps for arms, bob writes), `RubberBandReturn.Update` (writes localPosition every frame even at rest), `AgentTilt.LateUpdate`. All allocation-free; cost is small but multiplies by 3 agents and dirties transforms every frame, which hurts when animators are also running. Not changed (behaviour risk); candidate: early-out when at rest and re-home logic proven safe.
5. **Debug/diagnostic scripts left in scene** - `GrabReadout` runs `Debug.Log` plus string interpolation every 0.5 s and `Camera.main`; it is marked DEBUG. `DiagnosticsOverlay` is throttled (0.5 s, StringBuilder) and fine. Recommend removing `GrabReadout` from release scene (builder change, not done here).
6. **One-time heavy calls** - `DynamicFingerCollision.Resolve` uses LINQ + `FindObjectsOfType` once in Start; feature is parked (`GloballyEnabled=false`, LateUpdate returns immediately). Disabling the component in release would remove the (cheap) per-frame call. `Resources.Load` in AppBootstrap/DemoAgentGateway/Phase2DemoGateway are startup only.
7. **Camera.main** - cached internally in Unity 6; per-frame uses in FaceUser, FaceUserWhileDelivering, WorkspacePlacement are acceptable (not cached by us; negligible).
8. **Physics** - `OnTriggerStay` in RubberBandReturn/AgentTilt calls `ClosestPoint` per overlapping collider per physics step only while a hand touches; fine. `Physics.CheckSphere` in finger solver is gated off.

## Fixes made (behaviour preserving)
| File | Before | After |
|---|---|---|
| `Assets/MuseDesk/Scripts/Documents/HeldPaperController.cs` Update (~line 104-118) | `(int)_selCount.GetValue(grabbable)` every frame: reflection call plus boxing allocation | Cached closed `Func<int>` delegate created once from the property getter (`Delegate.CreateDelegate`); falls back to the original reflection path if creation fails. Same value, same try/catch |
| `Assets/MuseDesk/Phase2/Runtime/Board/StationDropZone.cs` `SetHighlight` (~line 79-97) | `_haloRenderer.material.color = ...` creates/uses a per-renderer material instance on each highlight | `MaterialPropertyBlock` cached on the zone, sets `_BaseColor` and `_Color`; no material instance. Same colours |

Risks: both unverified by compile. `StationDropZone`: if the halo shader reads colour from a property other than `_BaseColor`/`_Color`, tint would not change (the old `.color` maps to the shader's `[MainColor]`; URP Lit/Unlit use `_BaseColor`). `HeldPaperController`: the delegate requires `SelectingPointsCount` to be an `int` property (it is, in Interaction SDK `Grabbable`); IL2CPP stripping is the same exposure as the existing reflection.

## Not fixed because owned elsewhere (noted for those owners)
- `TaskCardView` uses `statusMarker.material.color` (instance material) and sets six TMP `.text` fields per Bind: add change-checks if Bind runs on every board refresh.
- `AgentView.SetState` writes `label.text` with string interpolation per state call; only called on state change from `AgentRegistry` (line ~75). If AgentRegistry calls it per frame, change-check it. Not verified.

## Recommendations, not applied (settings / rendering)
- URP asset: SRP Batcher on; MSAA 4x max on Quest (2x if fill-bound); disable depth texture and opaque texture unless a shader needs them; shadow distance and cascades minimum (no real-time lights per project rules); HDR off; render scale 1.0 default, consider dynamic/fixed foveation (Meta XR FFR level medium/high) on.
- Camera: no extra cameras; disable post-processing on the XR camera.
- Textures: ASTC, mipmaps on, aniso low; Inter TMP atlas static, single font asset, avoid dynamic atlas growth.
- Transparent unlit planes (halos, cards, glow): keep few, avoid large screen-covering overdraw.
- Scripting: IL2CPP, ARM64, incremental GC on, Vulkan with multithreaded rendering; Application.targetFrameRate not set above 72 unless intended (Quest 3 90 Hz capable).
- Physics: fixed timestep 0.0111 (90 Hz) or leave 0.02; keep solver iterations low; limit the number of trigger colliders on hands.
- Animator: set culling mode to Cull Update Transforms for off-view agents; avoid Animators on static props.

## Measuring (Editor only, relative)
`AgentScripts/PerfProfileDrive.cs`: `Begin("desk-idle", 5)`, wait, repeat for `desk-task`, `board-open`, `wall` (use `SetActiveByName("<wall root name>", true)` or force the wall flag by whatever route the Wall driver uses), then `Table()`. Reports avg/p95/max frame ms, GC bytes per frame, frames with allocation, batches, draw calls, SetPass, triangles. Counters via `ProfilerRecorder`; GC and render counters may read n/a depending on the Editor. Editor numbers include editor overhead and are not Quest numbers.

## Performance regression checklist (run on device before each build)
- [ ] OVR Metrics / `adb shell dumpsys` frame time stays within 90 Hz or 72 Hz budget in: desk idle, task active, board open, Wall habitat, ceiling pod, floor office.
- [ ] Profiler (development build, deep profile off) shows GC alloc per frame near 0 in idle and during hand interaction.
- [ ] No new `Update` that calls GetComponent/Find/LINQ/string formatting without a change check.
- [ ] No `renderer.material` in runtime code outside one-time setup (grep `\.material\b`).
- [ ] New TMP text: change-checked, Raycast Target off, no Extra Padding.
- [ ] Batches/draw calls recorded for each scenario with PerfProfileDrive before and after the change (Editor relative) and on device with RenderDoc or Meta tools.
- [ ] No debug scripts (GrabReadout, DiagnosticsOverlay visible) in release build.
- [ ] No extra cameras, real-time lights, or additional full-screen transparents.
- [ ] Thermal check: 15 min session, no sustained frame drops.
