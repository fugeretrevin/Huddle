# MuseDesk: overview for expert review (third pass)

2026-11-12, late. Branch `antigravity/visual-polish`. Competition deadline 2026-11-18, noon Pacific.

This follows your "Complete Spatial UX Redesign" brief. It says what was built from it, what the owner has seen on the headset, what is still unverified, and where we would like direction next.

## 1. What MuseDesk is

A Meta Quest 3 mixed-reality app (Unity 6, Meta Interaction SDK, hand tracking, passthrough). A miniature office sits on the surface in front of a seated user. Three rigged robot agents (Researcher, Developer, Tester) work a software task. When a human decision is needed, the Researcher walks up holding a paper; the user takes it, reads it, and approves or rejects by key or by physical stamp. A FastAPI backend runs the real pipeline in a sandbox ("LIVE"); with no PC the app runs a labelled scripted demo ("SCRIPTED DEMO"). The UI only shows an outcome after the backend or demo gateway confirms it, and never presents scripted or template output as live AI work.

## 2. What was built from your brief

Screenshots: `docs/review_screenshots/ux_redesign/` (`before_01..03`, `after_01..10`).

| Your brief | Built |
|---|---|
| Zone A: the office is the focus | Floating status panels, name tags, mode sign, guide sign and 20 of 24 decor props removed from view. Each desk has an upright nameplate: role, plus current activity in words |
| Zone B: contextual interactions in reach | Front band holds only: stamps (centre), task dock (front left), Focus key (front right), mode plaque. Idle poke keys: about 10 down to 3 |
| Zone C: management collapsed | Task board starts collapsed; the dock's Board key opens it at the user's left |
| Compact task dock | 16 x 9 cm plate: task count, most relevant task, state in words, mode. Start key appears only when a task is ready and nothing is running |
| Contextual card actions | Pause/Resume and Priority appear only for a selected card and only when valid; assignment pads appear only while a card is held; refusals are explained in words |
| Decision paper redesign | Two large keys (Approve, Reject), four small chips (Why, Details, Changes, Voice), separate labelled stamp area, mode band, 13 mm question, provenance line always visible. Details open on a second sheet below so nothing moves |
| Small-paper state | Carried paper shows a DECISION cover, mode band, TAKE tab and under-sheets for thickness |
| Start / Replay / Filed / Tips keys | Start is on the dock. Replay is "Run again" on a completion receipt (Delivered, Run failed, or Stopped after a rejection). Filed is a plate on the out-tray stack. Tips is a one-line guide that shows each step once and leaves |
| Design system | Twelve colour tokens, one type family (Inter) at five sizes with a 5.5 mm floor, text 0.5 mm proud of its surface. `docs/Design/MUSEDESK_DESIGN_SYSTEM.md`, `SPATIAL_INTERACTION_GUIDELINES.md` |
| Microinteractions and audio | Eased show/hide by scale, pulse on state change, eight synthesised audio cues |
| XR visual QA tooling | `AgentScripts/XrVisualQa.cs`: text size, overflow, depth gap to the surface behind, facing, parent-scale hazards, key travel against housing, reach, overlaps, occlusion, card lanes. 13 FAIL before the redesign, 0 now |

Also fixed: hand-dropping a card onto an agent had never sent the assignment (a bug since the first commit).

Backend, contracts, event model, gateways and reducers were not changed.

## 3. Evidence

| Level | State |
|---|---|
| Automated | Compiles; 33 EditMode tests; 108 runtime smoke checks; 0 missing scripts; backend tests pass; XR visual QA 0 FAIL, 54 WARN |
| Editor Play Mode | Whole demo loop driven by calling the methods the keys are wired to: Start, paper offered, approve, receipt, archive, board open and close, Run again, reject. No project errors |
| On the headset | The owner has looked at the redesigned build and said it "is looking really good". The one defect reported was the desk nameplates leaning so they read as tilted down; they are now upright, in a build installed after that report and not yet re-checked |
| Not confirmed on the headset | Item by item, nothing below has been reported on: pressing the stretched Approve and Reject keys; keys that are hidden by scale until shown (Start, Run again, board toolbar, Filed plate); the Details sheet; stamping on the new stamp area; card drop onto an agent or pad; reach of the open board's keys (0.60 to 0.64 m); audio levels |
| Performance | No device numbers exist. Unattended capture is blocked by the headset lock screen. Foveated rendering is deferred |
| Live mode | Live end-to-end against the desktop server last passed (71 checks) before the redesign and was not re-run, since no backend or gateway code changed. Live mode has not been used on the headset with the new UI |

## 4. Known gaps

- Plates are plain rectangles: no rounded corners, and the stamp area has a solid outline instead of a dashed one.
- Bold is synthetic (Inter Regular only).
- Poke keys have no hover or press colour change (the colour component writes a property the key shader ignores).
- Approve and Reject are a square key stretched 2.1 x 0.8, so the side rim is thicker than the top and bottom.
- With the board open, New, Pause and Priority are a stretch from the seat.
- The dock says only "In progress: the agents are on it" during a run; per-agent progress is on the nameplates.
- The plaque's "Connecting..." word is timed, not read from the connection attempt.
- The archive readout still opens as a floating panel near the Tester for 8 s.
- The artifact viewer panel (opens on delivery at the user's right) was not redesigned.
- Characters and workstation models are unchanged; no Blender work was done in this pass.

## 5. Questions for this review

1. With five days left, what is the best order for: device performance measurement, live-mode run on the headset, visual polish (rounded plates, true bold, key hover), and a recorded demo?
2. Is the idle scene now too bare for a judge's first five seconds, or is the restraint right? Four decor props remain.
3. The dock, receipt and plaque are flat dark or paper plates. Should they become small physical objects (a clipboard, a ticket printer, a brass desk sign) to match the office, or stay as quiet UI?
4. The artifact viewer and archive readout are the last floating panels. What should they become?
5. Is the Researcher walking up with a paper enough of a signature moment, or should approval have a stronger payoff (the factory, the agents reacting)?
6. Anything in the paper layout (`after_05`, `after_06`, `after_07`) that still reads as prototype?

## 6. Where things are

- Scene builders, in run order: `AgentScripts/BuildOfficeV2.cs`, `BuildTaskDockV2.cs`, `BuildPaperV2.cs`, `IntegrateV2.cs`, then `SetupInterFont.Apply`.
- Tools: `XrVisualQa.cs`, `PlayDrive.cs` (Play Mode driver), `PreviewV2.cs` (eye-point captures).
- Docs: `docs/Design/UX_REDESIGN_CHANGELOG.md`, `docs/Engineering/FINAL_DEVICE_QA.md` (ordered on-head checklist), `FINAL_PERFORMANCE_RESULTS.md`, `FINAL_DEMO_WALKTHROUGH.md`.
- Rollback: git tag `pre-ux-redesign`, `Builds/MuseDesk_pre_ux_redesign.apk`.
