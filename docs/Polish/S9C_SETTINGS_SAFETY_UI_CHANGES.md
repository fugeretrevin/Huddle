# S9c: settings, walk safety and UI consistency

Branch `feature/living-world-polish`. **Editor and Play Mode only. Nothing here is verified on a headset.** The competition build `Builds/MuseDesk.apk` was not touched, and the committed `ReleaseConfig.asset` keeps the new flag at 0, so the release layout is unchanged.

## 1. In-app "Calm motion" key (audit U5, UI row 12)

A poke key on the mode console that flips `MotionSettings.ReducedMotion` live and saves the choice for this device (`ReleaseConfigApplier.SetReducedMotionPreference`, PlayerPrefs key `MuseDesk.ReducedMotion`).

- **Where:** a clone of the Focus key, `MotionToggleKey`, in the same parent (`ModeConsole`), directly under the console plate with a 5 mm gap, at 0.62 of the Focus key's scale (cap 30 x 30 mm). Same key stack as every other key (`PokeInteractable`, `InteractableUnityEventWrapper`, `KeyStateVisual`), so hover and press use the standard tint through the property block; the FeedbackController click is registered at start like the other keys.
- **Label:** three lines at 5.5 mm bold Ink: `Calm` / `motion` / `ON` or `OFF`. The state is also the cap: Teal when on (the same material as an active console key), Paper when off.
- **Flag:** `MuseDeskReleaseConfig.reducedMotionToggle` (default and committed value 0). With the flag off the key sits at its hidden scale (0.001 of its size; never `SetActive`, so the poke interactable stays registered) and `Press` ignores anything that reaches it. `ReleaseConfigApplier.Apply` calls `MotionToggleKey.RefreshAll()` after setting `Active`.
- **Validation build:** `AgentScripts/WallValidationConfig.On()` now also sets `reducedMotionToggle = true` (and `Off()` clears it), so the validation APK shows the key and the competition build (made with `Off()`) does not. `ReleaseConfigDrive.Set` accepts `motiontoggle=1`.
- **Honesty:** it only changes how fast and how softly things move. It never touches task state or sends anything.
- **Logic and tests:** `MuseDesk.Motion.ReducedMotionPreference` (pure: `Resolve`, `Save`, `Flip`, `KeyText`, `KeyVisible`, with an `IPrefStore` so tests need no PlayerPrefs) in `Scripts/Spatial/Core/Motion/`. `ReleaseConfigApplier` now uses it for the read and the write, so the saved-beats-build-default rule is the tested one.

**Scene change (exactly):** `AgentScripts/BuildMotionToggleV1.Apply()` added one object, `MotionToggleKey`, under `Spatial/NearUserRig/ModeConsole` (the Focus key's parent), saved hidden by scale. `git diff` of `MainMRScene.unity`: 1153 inserted lines, 0 deleted, nothing else touched. Idempotent; `Revert()` deactivates it and renames it `MotionToggleKey_old` (`Apply` revives it). The builder also removes any component copied from the Focus key whose type name contains "Focus" (none were found: the clone root carries Transform, BuildingBlock, PokeInteractable, InteractableUnityEventWrapper, KitKeyRecord, KeyStateVisual).

Screenshots (Play Mode, temporary camera in the driver, flag forced on): `docs/review_screenshots/polish/s9c_calm_motion_key_off.png`, `s9c_calm_motion_key_on.png`.

## 2. Desk walk clearance (audit C6)

`AgentScripts/DeskClearanceDrive.cs` (Play Mode, read-only) reads the live scene and asserts every sample of every authored desk walk. Pure geometry is `Scripts/Agents/Core/WalkClearance.cs` (rectangle and triangle obstacles, disc footprint, 1 cm samples plus every polyline vertex, so the S9a corner stops are tested), with `WalkClearanceTests` (12).

**Model.** Frame: `WorkspaceRoot`-local metres. Footprint: the agent root `CapsuleCollider` radius times scale, **0.080 m** (all three agents). Obstacles: the **plan-view triangles of every mesh** under `WorkspaceRoot` (not text, not part of an agent, not hidden by scale) that reach into the agent's body band (feet + 15 mm to 0.25 m, the collider top), the other two agents at their home slot, and the user zone (a 0.30 m square centred on the seated head, root-local (0, 0.62)). An obstacle the agent already touches at an endpoint (its own station at home, the spot it stops at) is exempt for the first and last 10 cm; the report shows both the strict minimum and the unexempted one.

**Routes (7):** R1 Researcher home to the delivery anchor (`exitWaypoints` x2, then `AgentRegistry.deliveryAnchor`); R2 the same back (a rejection with no handoff); R3 and R5 Researcher to the Developer's front and back (the straight handoff); R4 delivery spot to the Developer front (the real approval handoff starts where the Researcher stands); R6 and R7 Developer to the Tester's front and back. Every `DeliverTo`, `DeliverInFrontOfUser` and `ReturnHome` caller is covered; `DeliverInFrontOfUser` is only the fallback with no anchor, so it is not a route here.

**Results (collider radius 0.080 m).** Positive means clear.

| Route | Length | Corners | Min clearance | Worst against | Result |
|---|---:|---:|---:|---|---|
| R1 home to delivery | 0.62 m | 2 | **-0.066 m** at (-0.306, 0.174) | Researcher station mesh, front edge 14 mm from the lane, 63 to 65 mm above the feet | FAIL |
| R2 delivery to home | 0.62 m | 2 | **-0.066 m** | same | FAIL |
| R3 home to Developer | 0.12 m | 0 | -0.010 m | Researcher nameplate rim, 70 mm away, 77 mm up | FAIL |
| R5 Developer to home | 0.12 m | 0 | -0.010 m | same | FAIL |
| R4 delivery to Developer | 0.06 m | 0 | +0.008 m | Inbox tray wall | PASS (tight) |
| R6 Developer to Tester | 0.13 m | 0 | -0.004 m | Developer station brushed-metal piece, 76 mm away | FAIL |
| R7 Tester to Developer | 0.13 m | 0 | -0.004 m | same | FAIL |

How much is the 0.08 collider: with radius 0.06 the minima are -0.047 (R1, R2), -0.018 (R3, R5), -0.004 (R6, R7); with 0.05, -0.037, -0.009, -0.003; with 0.04, -0.027 and **R3, R5, R6, R7 pass** (0.000 to +0.007). **R1 and R2 fail at every radius down to 0.04.** (The rendered bodies are 0.14 to 0.17 m wide, so the true footprint is about 0.05 to 0.08.)

**Update (2026-11-15): the table above is the S9c finding and is history.** Superseded by the V2 layout (`DESK_CHOREOGRAPHY_LAYOUT.md`, V2): all 7 routes pass in Desk, Floor and Ceiling, minimum 0.017 m at r 0.080 (target 0.015). Editor/PLAY evidence only, not worn; outer-desk deck overhang in Floor/Ceiling is still open.

Findings, with numbers. Nothing was moved in S9c.

1. **R1/R2 (structural).** The lane `WalkExit_2 (-0.47, 0.181)` to the anchor `(-0.20, 0.170)` runs between the Researcher desk front (z = 0.160) and the Inbox tray walls (z from 0.202, x -0.359 to -0.281): a lane 42 mm wide for a 160 mm footprint. The agent's body passes through the desk's front lip (63 to 65 mm up) and alongside the tray wall at 21 to 28 mm. Also, the corner stop at `WalkExit_1 (-0.47, 0.00)` is 29 mm from `Phase6/Decor_BookStack/Book_3` (-0.051 at r = 0.08; -0.021 at 0.05).
2. **R3/R5 and R6/R7 (marginal).** The stop in front of the receiver (`stopDistance` 0.18 m) puts the courier within 4 to 10 mm of a station prop or the nameplate rim at 76 to 77 mm up.

**Proposed fixes (not applied; they change on-head-proven choreography, so they are the owner's call).** (a) R1/R2: move the delivery lane in front of the trays: `WalkExit_2` and the anchor to z about 0.375 (clears the tray back wall at 0.298 by 0.077 and keeps 0.095 to the user zone at 0.47; clear at r = 0.05, tight at 0.08) and move `WalkExit_1` west of the book stack, or move the Inbox tray and the book stack out of the lane. A trial of the lane `(-0.30,0) (-0.47,0) (-0.47,0.375) (-0.20,0.375)` still collides with `Book_3` at the corner (-0.021 at r = 0.05), so the book stack must move too. (b) R3 to R7: raise `AgentHandoffController.backOffset` (0.015) so the courier stops about 10 mm farther from the station fronts, or accept the 4 to 10 mm touch for a character whose true footprint is below the collider. `DeskClearanceDrive.Try("r;x,z;x,z;...")` evaluates a proposed lane against the live scene without moving anything.

Run: `scripts\drv.ps1 DeskClearanceDrive Report -Max 6000` (also `ReportAt 0.05`, `Why`, `Obstacles`, `Try`).

## 3. Hover colour (audit row 7)

`GrabHoverHighlight` hover colour is now the **Teal** token ("general interaction, primary affordance, hover" in the design system), keeping the authored alpha. 17 scene instances and 2 prefabs have the old cyan (0.35, 0.85, 0.95) serialized, so a changed default alone would not reach them: `Awake` maps the legacy cyan to Teal (`PaletteHover`). Held colour is unchanged. No scene or prefab edit.

## 4. Provenance (audit row 13)

Three of the four surfaces in the audit already carry the mode in code and needed no change: **receipt** (`CompletionReceipt.modeText`, LIVE or SCRIPTED DEMO from the event's `source_mode`), **archive** (`DecisionArchive.cardMode` and the readout line, from the stored decision's own `source_mode`, plus the research provenance label), **voice HUD** (`VoiceHud.modeText`, every `Show` call in `VoiceCommandCenter` passes `ModeWord()`). The audit's "no provenance label found in code" was wrong for them.

The **agent speech bubble** had none. It now has a small footer line under the text: `SCRIPTED DEMO` or `LIVE`, the same words the status chip uses, from `controller.IsLive`; with no session there is no footer at all (never a guessed "live"). 5.5 mm (85 percent of the 6.5 mm text), InkSoft grey. The bubble text itself is still the caller's own, true to the run state (`AgentStatusSentence`). Pure rules and tests: `Agents/Core/BubbleProvenance.cs`, `BubbleProvenanceTests` (3). While adding it I fixed a latent sizing bug: `AgentSpeechBubble.Layout` read the line count from a label that is still disabled before the first draw (TMP reports one line), which would have cut the footer; the height now comes from the measured text. Play check: `bubble text='Waiting for your decision'`, label holds the footer, 2 lines, plate 30 mm tall.

Not data-backed, so not labelled: the model-written text of an artifact (`ArtifactViewer`) already shows its `provenanceLabel`; nothing else in these four surfaces shows model-written content.

## 5. Font fallback (audit row 8)

`WallUi.MakeText` with a null font no longer falls back silently to TextMeshPro's default (Liberation). It uses the last Inter font it was given, else a loaded `Inter-Medium` asset, and logs **one** warning (`[MuseDesk.Wall] WallUi.MakeText got no font for "..."`). No Resources copy of the font was added (it would duplicate an atlas). The Inter assets are not touched.

## 6. Row 4 (two key visual systems)

**Stays open by decision.** `KeyStateVisual` and `PokeKeyStateVisual` are both on-head-proven; merging risks the submission keys. Not changed.

## Evidence

- EditMode, one fixture at a time: `WalkClearanceTests` 12, `BubbleProvenanceTests` 3, `ReducedMotionPreferenceTests` 7 (new); `MotionTests` 32, `PresenceTests` 8, `DeskPolishLogicTests` 22, `WallOfflineLogicTests` 31, `AgentWalkPathTests` 5, `AgentWalkCornersTests` 6, `AgentStatusSentenceTests` 8, `SmallLogicTests` 8. All passed, 0 failed.
- Play Mode (Editor, no HMD): desk demo start, paper, approve, completed (State 'Completed', receipt 'Delivered', 'SCRIPTED DEMO' throughout), **0 console errors** (4 warnings, all Meta XR "no headset"). Toggle driven with the flag forced on from a runtime copy of the config: hidden (scale 0.00003) with the flag off, shown (0.031) with it on; press: `False -> True`, saved = 1, label `Calm/motion/ON`, cap Teal 2FA39A; press again: `True -> False`, saved = 0, label OFF, cap Paper F7F2E3; flag off again: hidden. The driver removes the saved choice afterwards.
- Not run: the Play toggle with a real finger (no hands), on-head anything, the full EditMode suite.

## Files

New: `Scripts/Workspace/MotionToggleKey.cs`, `Scripts/Spatial/Core/Motion/ReducedMotionPreference.cs`, `Scripts/Agents/Core/WalkClearance.cs`, `Scripts/Agents/Core/BubbleProvenance.cs`, three test files, `AgentScripts/BuildMotionToggleV1.cs`, `MotionToggleDrive.cs`, `DeskClearanceDrive.cs`, `ProvenanceDrive.cs`. Changed: `MuseDeskReleaseConfig.cs` (flag, Summary), `Resources/ReleaseConfig.asset` (one line, `reducedMotionToggle: 0`), `ReleaseConfigApplier.cs`, `AgentAttention.cs`, `AgentSpeechBubble.cs`, `GrabHoverHighlight.cs`, `WallUi.cs`, `WallValidationConfig.cs`, `ReleaseConfigDrive.cs`, `MainMRScene.unity` (the one added key).
