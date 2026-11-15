# Living-World Polish: implementation audit (Sprint 0)

Date: 2026-11-15. Branch `feature/living-world-polish`, from `ce19b28` (`feature/wall-habitat`, tag `wall-habitat-2`). Restore tag: `pre-living-world-polish`.

## Baseline

| Item | Value |
|---|---|
| Competition build | `Builds/MuseDesk.apk` = `Builds/MuseDesk_rc6_2.apk`, SHA-256 `173C813F4840DD3A7DCA81430E1914B1A9BEBF0A3B4C3375CF1E7FD53DB9B963` (unchanged by this pass) |
| Scene | `Assets/MuseDesk/Scenes/MainMRScene.unity`, one owner at a time; this pass edits code and docs only unless stated |
| Unity | 6000.6.8f1, Meta XR SDK 209, URP |
| Tests before the pass | Backend 207 pytest; Wall EditMode 98; full EditMode suite run fixture-by-fixture in the previous session |
| Headset baseline video | none available (nobody wearing the Quest); nothing below is worn-verified |
| Art worktrees | `.worktrees/wall-habitat` (art/wall-habitat): Antigravity's Wall modules, Manager FBX, socket registry exist **uncommitted, no prefabs, no manifest entries**: not integrated (another agent's work in progress). `.worktrees/art-polish`, `final-art-qa`, `spatial-art`: earlier merged art branches |

Detailed evidence lives in four audit files (code reading, file:line):

- [A: motion and transform ownership](audit/A_MOTION_OWNERSHIP.md)
- [B: interaction and input ownership](audit/B_INTERACTION_OWNERSHIP.md)
- [C: UI, visual and audio consistency](audit/C_UI_VISUAL_AUDIO.md)
- [D: characters, animation and assets](audit/D_CHARACTERS_AND_ASSETS.md)

## Issue → cause → component → fix → test

Status: **DONE** (code + test evidence named), **IN PROGRESS**, **PLANNED**, **BLOCKED** (needs a person or another team).

### Motion

| # | Issue | Cause | Component | Fix | Test / evidence | Status |
|---|---|---|---|---|---|---|
| M1 | No shared timing; ~14 inline SmoothStep/cubic copies; Lerp-by-dt motions never end exactly | No project motion library | All | `MuseDesk.Motion` (Scripts/Spatial/Core/Motion): `Ease`, `MotionToken`, `MotionClock`, `ProfiledMover`, `Damped`, `MotionSettings` (Reduced Motion, max step), `MuseMotionProfile` | `MotionTests` (10, AUTO) | DONE (foundation); adoption ongoing |
| M2 | Wall walkers start at full speed, stop dead, turn while walking | `HabitatWalker.MoveToward` constant speed + linear yaw | Wall | Velocity profile on the same validated polyline, stop before sharp corners, turn in place, damped heading, early lease of next segment | `HabitatMotionTests` 22 incl. every-tick clearance (AUTO) | DONE (Editor-unseen motion quality) |
| M3 | Elevator cabin constant speed; linear doors | `HabitatElevatorMachine.Move` MoveTowards | Wall | `ProfiledMover` accel/cruise/brake, exact landing; eased door drawing | `HabitatMotionTests`, `MotionTests.ProfiledMover_*` | DONE |
| M4 | Walk cycle plays at full rate while accelerating (foot skating) | No stride matching | Wall view | `MatchStride`: Animator speed = ground speed / cruise | code; needs headset video | DONE (unverified visually) |
| M5 | Task card changes lane by teleport; lane neighbours snap | `TaskCardView` glides only when displaced | Board (A #1, #2) | Glide every slot change with `cardRelease` token | see S3S4_DESK_MOTION_CHANGES.md | DONE (code + PLAY smoke) |
| M6 | New card / habitat / wall keys pop in at full size | SetActive / scale 0→1 | Board, Wall (A #3, #5, #9) | `panelAppear` reveal; keys use HiddenFactor | see S3S4_DESK_MOTION_CHANGES.md | DONE (code + PLAY smoke) |
| M7 | Desk agent facing never converges then snaps on arrival; no turn in place | `AgentMovement` Lerp + final snap | Desk agents (A #6) | Damped heading + turn-before-walk + stride match (same rules as Wall) | see S3S4_DESK_MOTION_CHANGES.md | DONE (code + PLAY smoke) |
| M8 | Handoff token pops and jumps hand to hand | `AgentHandoffController` | Desk (A #7) | Timed flight between sockets with pickup/settle tokens | see S3S4_DESK_MOTION_CHANGES.md | DONE (code + PLAY smoke) |
| M9 | Decision paper blinks via SetActive | `DocumentDeliveryController` | Documents (A #8) | Fade/scale with `panelHide`, never while held | see S3S4_DESK_MOTION_CHANGES.md | DONE (code + PLAY smoke) |
| M10 | Two writers on one transform: RefaceUser vs transition; RubberBand vs AgentMovement; PushableProp vs PropCarrier | No ownership rule | Workspace, agents (A #10) | Mode/owner guards; explicit order | see S3S4_DESK_MOTION_CHANGES.md | DONE (code + PLAY smoke) |

### Interaction

| # | Issue | Cause | Fix | Status |
|---|---|---|---|---|
| I1 | Tracking loss during a card drag counts as a drop and can assign | `TaskDragController` treats `!held` as release (B #1) | Cancel on tracking loss | DONE (S2, see S2_INTERACTION_CHANGES.md) |
| I2 | Double poke sends two board commands; late 409 overwrites success | new idempotency key per poke (B #4) | Per-card in-flight lock, stable key per action | DONE (S2, see S2_INTERACTION_CHANGES.md) |
| I3 | Approve/reject can be sent twice by different entry points; cancelled Respond leaves "Saving" | key per call, bypasses (B #5) | One guard for all entry points, stable key, reset on fail | DONE (S2, see S2_INTERACTION_CHANGES.md) |
| I4 | Far-gesture precedence contradicts the spec; same-kind claims from two arbiters both succeed | HandClaim numbers, registry equality (B #2, #3) | Spec precedence, owner-aware registry | DONE (S2, see S2_INTERACTION_CHANGES.md) |
| I5 | Voice gesture can open the mic while holding a paper | no arbiter check (B #7) | Consult holding/claims | DONE (S2, see S2_INTERACTION_CHANGES.md) |
| I6 | Summon ray Bring/Ask has no debounce | (B #8) | Debounce | DONE (S2, see S2_INTERACTION_CHANGES.md) |

### Characters

| # | Issue | Cause | Fix | Status |
|---|---|---|---|---|
| C1 | Wall avatars "work" whenever home, whatever the task | view mapped Home→work clip | `HabitatPresence.FromTaskState` → work / waiting / thinking / worried / idle; Manager from plan flow | DONE (`MotionTests.Presence_MapsOnlyRealStates`) |
| C2 | Wall Manager plays Talking permanently | placeholder mapping | Manager works only while drafting/committing | DONE |
| C3 | No reaction to confirmed completion in Wall | none | one `Nodding` on a reducer `task.completed` | DONE (Editor-unseen) |
| C4 | Turn_Left/Right, Nodding, Listening and ~18 more clips unused | not wired | Wall turn-in-place uses Turn_*; Desk adoption PLANNED | PARTIAL |
| C5 | Desk idle gestures on random timers | `AgentAttention` | Keep (ambient, not task claims); throttle under Focus | PLANNED |
| C6 | Desk agent walks have no clearance check | straight lines | Document the authored waypoints; add bounds assertions in a Play Mode driver | PLANNED |
| C7 | No Manager model in production | Antigravity art not delivered | Placeholder stays labelled; manifest swap ready | BLOCKED (art) |

### UI / visual / audio

| # | Issue | Fix | Status |
|---|---|---|---|
| U1 | State colours drift (green has ≥7 values; amber/coral drift; Tester role = amber) | `OfficeTokens.StateColor` used by board, agent glow, workstation display, Wall | DONE (role colours open) |
| U2 | Wall key disabled state invisible | dimmed label | DONE |
| U3 | Provenance footer on Wall cards is the smallest text | 4.2 mm InkSoft | DONE |
| U4 | No elevator, portal, page, card sounds | `WallAudio` cues; Wall key click (page/card cues on desk still open) | PARTIAL |
| U5 | No global Reduced Motion / quiet setting | ReleaseConfig + PlayerPrefs; Focus mutes Wall cues; no in-app toggle | PARTIAL |

## Not possible in this pass without a person

Worn tests (every row of the QA matrix marked worn), physical hand success rates (9/10), comfort, headset video, device frame timing and soak. These remain `NOT_TESTED` in `QUEST_EVIDENCE_MATRIX.md` until someone wears the headset.
