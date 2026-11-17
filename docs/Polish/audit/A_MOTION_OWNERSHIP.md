# A. Motion and transform ownership audit

Date: 2026-11-15. Branch feature/wall-habitat. Read-only code audit (no Unity run). Scope: `Assets/MuseDesk/Scripts/**`,
`Assets/MuseDesk/Phase2/Runtime/**`. Evidence level for every row: code reading only (no PLAY / WORN claim is made).
Line numbers are from the working tree at audit time and may drift.

Severity: High = visible jump or fight in a normal flow. Med = visible in a less common flow or subtle. Low = cosmetic,
latent, or guarded.

## Summary of the big picture

- The bone layering for characters is already well designed: `BoneRotationLayer` / `BoneScaleLayer`
  (`Scripts/Agents/BonePoseLayer.cs:13,44`) applied in `LateUpdate` with explicit `DefaultExecutionOrder`
  (IdleLife 300, Gaze 310, Tilt 400, ArmNudge 410, Presence 420, Attention 430). These run after the Animator.
  Real fights there are limited to the two cases in section 2.
- Root ownership of the workspace is documented and enforced (`WorkspaceTransitionController.cs:7-15, 97-109`), with two
  small leaks listed below.
- A motion library exists (`Scripts/Spatial/Core/Motion/MuseMotion.cs`, `MuseMotionProfile.cs`, namespace
  `MuseDesk.Motion`) but is used by exactly one call site. Meanwhile at least 10 ad-hoc easing copies exist.
- The biggest visible problems are pops and teleports on the task board (cards teleport between lanes), paper/token/panel
  `SetActive` pops, the Wall habitat appearing instantly, the Wall elevator moving at constant speed, and agent facing
  that never settles exactly.

## 1. Transform writers per object group (Update / LateUpdate / FixedUpdate / coroutines)

### 1a. Agents and mascots (characters and roots)

| Issue | Evidence (file:line) | Affected object | Severity | Suggested fix |
|---|---|---|---|---|
| AgentMovement is the walk writer: position every Update, facing by slerp, snap to home at end | `Agents/AgentMovement.cs:124-170` (pos 137, rot 151/154, end snap 159/164/165) | Agent root (researcher/developer/tester) | Med | Keep as single walk owner; replace end snap with an eased settle (see 3 and 5) |
| RubberBandReturn writes the SAME root localPosition every frame while idle | `Agents/RubberBandReturn.cs:113,121`, yields to walk at 62-69 | Agent root | Med | Order is implicit (both Update, no ExecutionOrder). Add `[DefaultExecutionOrder]` after AgentMovement or move the offset onto a child |
| FaceUserWhileDelivering rotates the root in LateUpdate after arrival | `Agents/FaceUserWhileDelivering.cs:26-47` | Agent root (delivering) | Med | Gated by `AtHome/IsMoving` (29), so no live fight, but exponential slerp (46) never lands exactly; use `Damped.FollowAngle` |
| FaceUser (Workspace) also writes rotation in LateUpdate | `Workspace/FaceUser.cs:19-33` | Any object it is put on (text, panels, characters per comment 6-7) | Low | Verify it is not on agent roots that also have FaceUserWhileDelivering (two writers if both enabled) |
| AgentView writes procedural bob and arm bone rotations in Update | `Agents/AgentView.cs:111-112,119-123,128-129,162-164` | `bob`, `armL`, `armR` | Med | If these point at Animator-driven bones, the Animator overwrites them every frame (Update runs before animation), so the arm Slerp reads a clip pose: wasted or jittery. Confirm on the rigged prefabs that `bob/armL/armR` are unset or on non-animated primitives; otherwise disable AgentView motion when `AgentAnimatorBridge` is active |
| AgentAttention places attention signs (world pos, rot, scale) in LateUpdate | `Agents/AgentAttention.cs:924-936` (rot 929, pos 930, scale 935) | Signs above agents | Low | Instant by design (avoid-lift can step y by a lot at 917); smooth `y` |
| AgentHandoffController parents token to a hand and writes local pose/scale | `Agents/AgentHandoffController.cs:268-275`, dest marker 215-218 | Handoff token, destination marker | Med | See pops in section 6 |
| Wall habitat avatars are driven by HabitatStage each Update, not by the main scripts | `Wall/WallHabitatView.cs:408-414` | Habitat avatars | Low | Single writer, OK. `applyRootMotion=false` at 149 |

### 1b. Carried props, papers, documents, couriers

| Issue | Evidence | Affected object | Severity | Suggested fix |
|---|---|---|---|---|
| HeldPaperController writes world pose each LateUpdate to follow the hand socket | `Documents/HeldPaperController.cs:178-196` (195) | Decision paper while in agent hand | Low | Single writer while `!_taken`; OK. Also forces scale each LateUpdate (171) |
| HeldPaperController scale coroutine and LateUpdate scale lock | `Documents/HeldPaperController.cs:260-279` vs `171` | Decision paper | Low | Safe only because `_scaleLocked` is false during `ScaleTo` (159, 275). Fragile: move to one owner |
| DocumentDeliveryController files the paper with SetPositionAndRotation + scale each frame | `Documents/DocumentDeliveryController.cs:139-150` | Decision paper | Low | Hand-off is guarded by `BeginFiling` (`HeldPaperController.cs:156-163`); OK |
| RemoteDocumentDeliveryController drives courier anchor / paper per frame | `Spatial/RemoteDocumentDeliveryController.cs:293-337, 350-364` | Courier anchor, courier visual | Low | Single driver; uses local `Ease` (405) |
| DocumentCourierController writes courier root in Update (dock follow, bezier legs) | `Art/DocumentCourierController.cs:157-234` (dock 152/163, route 185-186, 221-222) | Courier root | Low | Single writer. But two courier systems exist (Art vs Spatial CourierVisual, `Spatial/CourierVisual.cs:116`): confirm only one is active per flow |
| StampTool return home | `Documents/StampTool.cs:219-237` | Stamp prop | Low | OK, see 3 |
| ApprovalStampController moves the stamp prop by coroutine | `Documents/ApprovalStampController.cs:197-228` | Agent stamp prop | Low | OK |
| PushableProp settling / return | `Workspace/PushableProp.cs:82-83, 86-148` | Desk props | Med | See 2 |

### 1c. Task cards and board (Phase2)

| Issue | Evidence | Affected object | Severity | Suggested fix |
|---|---|---|---|---|
| TaskCardView owns its own transform in Update (settle/lift) | `Phase2/Runtime/Board/TaskCardView.cs:270-306` | Task card | Low | Good pattern (MoveTowards time, ease-out cubic at 289), exact finish |
| BoardPresenter.LayoutRegion also writes card pose through PlaceAtHome/ReturnHome | `Board/BoardPresenter.cs:267-297`, `TaskCardView.cs:456-481` | Task cards | High | Two writers (card Update + presenter layout). They are coordinated by `_displaced`, but a card that did NOT leave its slot is snapped (470-471). See section 6 |
| BoardExpander animates the whole board | `Board/BoardExpander.cs:186-216` | Board root | Low | Single writer, exact (MoveTowards time + eased) |
| ScaleReveal animates scale | `Board/ScaleReveal.cs:85-98` | Board panels | Low | Fine |
| TaskCardView edge plate scale is a hard set | `Board/TaskCardView.cs:236-262` (261) | Edge plate | Low | Add a short ease (hover edge grow pops) |

### 1d. Workspace roots (desk / floor / ceiling / wall)

| Issue | Evidence | Affected object | Severity | Suggested fix |
|---|---|---|---|---|
| WorkspaceTransitionController is the only root writer in Floor/Ceiling and during transitions | `Spatial/WorkspaceTransitionController.cs:66-75`, tick 92-95, ownership guard 97-109 | WorkspaceRoot | Low | Good. Guard reverts other writers after 1 frame (warns once) |
| WorkspacePlacement writes the root in Desk mode: instant snaps | `Workspace/WorkspacePlacement.cs:123-124, 256-257, 303, 326` | WorkspaceRoot | Med | First placement and recenter/HMD-mount re-place (169-174) teleport the office with agents in it. Add a short eased re-place for recenter |
| RefaceUser coroutine keeps writing root rotation for 2.5 s with no mode check | `Workspace/WorkspacePlacement.cs:271-285` | WorkspaceRoot | Med | A mode change started inside that window has two writers. Break the loop if `SpatialWorkspaceState.Mode != Desk || IsTransitioning` (the same guard exists at 120/311/323 but not here) |
| WorkspaceTransitionMachine eased move | `Spatial/Core/WorkspaceTransitionMachine.cs:238` | WorkspaceRoot | Low | Own SmoothStep, exact; candidate to move onto `MotionClock` |
| CeilingWorkspacePodController moves pod root via Update (vertical travel) | `Art/CeilingWorkspacePodController.cs:350-370` (365, 369) | Ceiling pod | Low | Own double-SmoothStep, exact |
| ConfigureRoomPoses snaps the pod root when not transitioning | `Art/CeilingWorkspacePodController.cs:186-194` (191) | Ceiling pod | Med | If a room re-scan reconfigures while docked, the pod jumps; ease it |
| CeilingPodController (spatial) writes mount/segments/mast every frame | `Spatial/CeilingPodController.cs:135-171` | Ceiling mount, mast | Low | Derived pose, OK. Two pod controllers exist (Art and Spatial); keep their roles distinct |
| FloorOfficePlatformController, WorkspaceGhostPreview, FloorAimController write poses | `Art/FloorOfficePlatformController.cs:112,118`; `Spatial/WorkspaceGhostPreview.cs:66-67`; `Spatial/FloorAimController.cs:276-277,335,386` | Floor platform, ghost, aim visuals | Low | Preview-driven, instant follow is acceptable |

### 1e. Panels / inspectors / UI that follow the user

| Issue | Evidence | Affected object | Severity | Suggested fix |
|---|---|---|---|---|
| MovablePanel drag + settle (single owner, exact settle) | `Spatial/MovablePanel.cs:235-247, 260-274, 337-338` | Movable panels | Low | Good pattern. `snap` path at 269 is by design |
| PlacementGuide card follows head with exponential smoothing, no end | `Spatial/PlacementGuide.cs:442-471` (449,453,465) | Placement card | Low | Continuous follow; fine |
| SpatialModeStrip hard-follows root or near-user pose every LateUpdate | `Spatial/SpatialModeStrip.cs:201-214` | Mode strip | Med | When `_followRoot` / near-user pose availability flips (204), the strip teleports between two poses. Ease the switch |
| SummonController keys placed with SetPositionAndRotation (re-seat on distance) | `Spatial/SummonController.cs:160-169, 171-183` | Bring/send keys, agent ray targets | Low | Keys must hold still (165); appearance is hidden by ScaleEase (143) |
| PerchController / SpatialMailboxPresenter place stand/mailbox instantly | `Spatial/PerchController.cs:235-240`; `Spatial/SpatialMailboxPresenter.cs:131` | Remote perch, mailbox | Low | Acceptable if only placed while hidden; verify |
| NearUserKit rig follows desk pose per LateUpdate and re-parents items | `Spatial/NearUserKit.cs:84-152` (96, 113-114, 128, 145) | Stamps, desk controls | Med | Detach/Reattach re-parent with identical locals: visually an instant pose (items jump to the rig). OK if the rig is exactly at the root pose; otherwise a hop |
| AttentionBeacon writes cue pose per frame | `Spatial/AttentionBeacon.cs:496,507` | Beacons | Low | Fine |
| Agent speech bubble / status chip use pop ease with MoveTowards | `Agents/AgentSpeechBubble.cs:176`; `AgentStatusChip.cs:107` | Bubbles | Low | Good, exact |

### 1f. Elevator / habitat (Scripts/Wall)

| Issue | Evidence | Affected object | Severity | Suggested fix |
|---|---|---|---|---|
| Elevator cabin moves at constant speed: instant start and stop | `Spatial/Core/Wall/HabitatMotion.cs:297-305` (300) | Cabin Y | High | Use `ProfiledMover.Step` (its doc names elevator cabins, `MuseMotion.cs:152-156`) |
| Elevator doors are linear in time | `HabitatMotion.cs:282` (DoorOpen01 linear), consumed at `Wall/WallHabitatView.cs:398-402` | Doors | Med | Ease with `MuseMotionProfile.Active.elevatorDoor` (already defined, 0.45 s InOutCubic, `MuseMotionProfile.cs:37`) |
| Habitat root placement is an instant SetPositionAndRotation plus SetActive | `Wall/WallHabitatController.cs:205-206, 369-372` | Habitat root | High | Whole habitat appears (and disappears, 192) in one frame. Scale-reveal or fade it |
| Habitat avatars: position/yaw each Update from the walker state machine | `Wall/WallHabitatView.cs:410-411` | Habitat avatars | Low | Single owner; walker uses `Damped.FollowAngle` (`HabitatWalker.cs:181`) |
| Printed card flight has its own SmoothStep | `Wall/WallHabitatView.cs:483-492` | Printed task cards | Low | Exact; fine |
| Visit proxy writes position/rotation/scale each Update | `Wall/WallHabitatController.Interaction.cs:216-262` | Carried agent proxy | Med | See section 3 |
| Portal root placed instantly; avatar scale by SmoothStep | `Wall/WallHabitatController.Interaction.cs:455, 496-504` | Portal | Low | OK; ring pops in at 464 (section 6) |
| Ghost preview placed per frame | `Wall/WallHabitatController.cs:394-408` | Placement ghost | Low | OK |

## 2. Duplicate writers, procedural bones and Animator ordering

| Issue | Evidence | Affected object | Severity | Suggested fix |
|---|---|---|---|---|
| Spine/chest are layered by several scripts with separate `BoneRotationLayer` instances: IdleLife (300), Presence (420) both apply to spine and chest | `AgentIdleLife.cs:60,236,249`; `AgentPresence.cs:88,566-567`; presence says it hands off to IdleLife when present (`AgentPresence.cs:20`) | Spine, chest bones | Med | Documented hand-off exists (`IdleLife.Owns/SetBoneOffset`, `AgentIdleLife.cs:74-88`). Risk is only if a clip does not key those bones: a second independent layer then treats the first layer's output as "clean" and can accumulate. Add a test for clips that do not key spine/chest |
| AgentTilt uses `Owns/SetBoneOffset` so one writer per bone | `AgentTilt.cs:61-77` | Chest | Low | Good pattern |
| AgentArmNudge (410) and AgentPresence arm layers (420) can write the same upper arm | `AgentArmNudge.cs:116`; `AgentPresence.cs:676,682` | Upper arm / forearm | Med | Both are `BoneRotationLayer` on arm bones, no `Owns` hand-off like Tilt/IdleLife. If Presence arm gesture and a finger nudge overlap, the second layer's `Same()` check (`BonePoseLayer.cs:23`) will take the first layer's result as "clean" |
| Gaze writes neck/head at 310; Presence sends head gestures through `AgentGaze.SetExpression` | `AgentGaze.cs:480,493,29`; `AgentPresence.cs:22` | Neck, head | Low | One writer per bone. Good. Runs in LateUpdate after the Animator |
| Look-at roll/yaw/pitch smoothing is exponential (`Lerp(_yaw,...,k)`) | `AgentGaze.cs:443-452` | Head gaze | Low | Acceptable for a continuous follow; it should still stop exactly when a gaze target is released (see 3) |
| AgentView arm Slerp (Update) vs the Animator | `AgentView.cs:122-129,162-164` | Arms | Med | See 1a |
| Two AgentAnimatorBridge classes (`MuseDesk.Art` and `MuseDesk.Agents`) | `Art/AgentAnimatorBridge.cs:13`; `Agents/AgentAnimatorBridge.cs:30` | Agent Animators | Low | Mitigated: Art bridge skips MoveSpeed when the Agents bridge is present (`Art/AgentAnimatorBridge.cs:107-109`). Rename one to avoid confusion |
| Walk-bob is written to the agent root Y by AgentMovement while the Animator also bobs | `AgentMovement.cs:136` (`walkBobHeight`) | Agent root | Low | Root bob stacked on the clip's own step; tune or disable root bob for rigged agents |
| RubberBandReturn vs AgentMovement on the same transform | `RubberBandReturn.cs:59-122`; `AgentMovement.cs:159-166` | Agent root | Med | Frame-order dependent re-home (comment at `RubberBandReturn.cs:72-77` acknowledges a one-frame stale home). Pin execution order |
| PushableProp vs WorkspacePropCarrier both reposition props when the office moves | `Workspace/PushableProp.cs:90-95` (EnterSettling snaps home on parent move > 2 cm per frame); `Spatial/WorkspacePropCarrier.cs:276-290, 312-313` | Loose desk props | Med | If the office travels faster than ~1.4 m/s at 72 Hz, PushableProp detects a "jump" each frame and snaps props to home mid-transition while the carrier freezes them. Disable PushableProp in transitions (carrier already toggles `pushable.enabled`, `WorkspacePropCarrier.cs:273`) and check the trigger threshold |
| WorkspacePlacement.RefaceUser vs WorkspaceTransitionController | `WorkspacePlacement.cs:271-285` vs `WorkspaceTransitionController.cs:97-109` | WorkspaceRoot | Med | See 1d |
| NearUserKit re-parents items and WorkspacePropCarrier pins children of the root | `NearUserKit.cs:128,145`; `WorkspacePropCarrier.cs:260-265` | Stamps / props | Low | Carrier handles "not in office" (`e.free`). OK |

## 3. Interpolation by accident (`Lerp(current, target, k*dt)`, `Slerp`, `SmoothDamp`)

`SmoothDamp` is not used anywhere in `Assets/MuseDesk`. No finite motion below uses `MotionClock`.

| Issue | Evidence | Moves what | Severity | Suggested fix |
|---|---|---|---|---|
| Walk facing slerp never converges, then end snap | `Agents/AgentMovement.cs:151,154` then `164-165` | Agent yaw during/after walk | High | Rotation slerp factor `dt*10`/`dt*6`; end-of-walk snap of `localRotation` is a visible pop if still off. Use `Damped.FollowAngle` and a final ease, or lengthen the last-stretch turn |
| FaceUserWhileDelivering | `Agents/FaceUserWhileDelivering.cs:46` | Agent root yaw when facing user | Med | `Slerp(cur, target, dt*8)`: continuous follow of a moving head is fine, but it also never finishes after the head stops; use `Damped.FollowAngle` |
| FaceUser | `Workspace/FaceUser.cs:32` | Text/panel/character yaw | Low | Same pattern, `turnSpeed` 10 |
| Wall visit proxy "Perched" | `Wall/WallHabitatController.Interaction.cs:242,245` | Proxy position and scale | Med | `Lerp(_proxy.position, PerchWorld, dt*10)` and scale `dt*6`: never lands exactly, can be frame-rate dependent. Use `MotionClock` with `visitLand` token (`MuseMotionProfile.cs:45`, 0.35 s OutCubic) |
| StampTool return when `returnSeconds <= 0` | `Documents/StampTool.cs:225-227` | Stamp prop | Low | `Lerp(current, home, 1-exp(-rate*dt))` fallback only |
| AgentView arm slerps | `Agents/AgentView.cs:128-129,162-164` | Arm rotation | Med | `dt*6`/`dt*8` per frame; frame-rate dependent, see 1a |
| RubberBandReturn offset | `Agents/RubberBandReturn.cs:55,110,112,120` | Agent localPosition offset | Low | Intentional spring; never reaches zero exactly, so `localPosition = _home + _offset` writes forever |
| AgentTilt, AgentArmNudge lean/nudge | `AgentTilt.cs:98,135,142`; `AgentArmNudge.cs:60,98,106` | Torso lean, arm swing | Low | Intentional squishy response; the `Clear()` at `mag<1e-4f` (`AgentTilt.cs:145-149`) ends it |
| AgentGaze weight / yaw / pitch | `AgentGaze.cs:443-452` | Head gaze | Low | Intentional follow |
| AgentIdleLife amount / lean | `AgentIdleLife.cs:191-192` | Idle amplitude | Low | Intentional blend |
| AgentPresence face weight / yaw | `AgentPresence.cs:533,544` | Face-user blend | Low | Mixed: MoveTowards for weight (OK), exponential yaw |
| WorkspacePlacement.RefaceUser | `Workspace/WorkspacePlacement.cs:280-281` | WorkspaceRoot yaw | Med | `Slerp(cur, target, dt*8)` for a bounded 2.5 s window; exits without an exact final value (final yaw is whatever the slerp reached) |
| MovablePanel drag smoothing | `Spatial/MovablePanel.cs:237,243` | Hand follow while dragging | Low | Appropriate (continuous input), settle at 337 is exact |
| PlacementGuide card follow | `Spatial/PlacementGuide.cs:449,453,465` | Placement card | Low | Continuous follow, exponential. Fine |
| GrabReadout fps | `Workspace/GrabReadout.cs:30` | Debug number | Low | Not a motion |

## 4. Existing easing / tween / motion utilities and how widely used

| Utility | Where | Usage | Notes |
|---|---|---|---|
| `MuseDesk.Motion.Ease`, `EaseKind`, `MotionToken`, `MotionClock`, `ProfiledMover`, `Damped`, `MotionSettings` (Reduced Motion, MaxStep clamp) | `Scripts/Spatial/Core/Motion/MuseMotion.cs` | Only 1 call site: `Spatial/Core/Wall/HabitatWalker.cs:181` (`Damped.FollowAngle`) | Complete, exact at both ends, handles hitches and Reduced Motion. Best foundation |
| `MuseMotionProfile` ScriptableObject (design tokens: hover, press, cardPickup, cardRelease, panelAppear/Hide, paperUnfold, detailsSheet, elevatorDoor, accel/brake, portal, visitLand, fadeSwap, completion) | `Scripts/Spatial/Core/Motion/MuseMotionProfile.cs`, loaded from `Resources/MuseMotionProfile` else defaults | 0 consumers beyond itself | The values are defined but nothing reads them yet. Notably `walkTurnLimit`, `startSeconds`, `stopSeconds` are Characters tokens not yet used by AgentMovement |
| `ScaleEase` (show/hide by scale, pulse) | `Scripts/Workspace/OfficeUi.cs:45-127` | About 148 hits across 34 files incl. `ScaleReveal`, DecisionArchive, AttentionBeacon, CeilingPodArt, SummonController, VoiceHud (grep `ScaleEase|ScaleReveal|OfficeTokens`) | Scale-only; ease-out cubic; MoveTowards time so it finishes exactly. The de facto show/hide utility. Not a general position tween |
| `OfficeTokens` colour tokens | `Scripts/Workspace/OfficeUi.cs:10-37` | Widely used for colours | Colour only, no motion tokens |
| `ScaleReveal` / `BoardExpander` (Phase2) | `Phase2/Runtime/Board/ScaleReveal.cs`, `BoardExpander.cs` | Board only | Same pattern (MoveTowards time then eased) re-implemented |
| `PanelLayoutMath.Smooth` | `Spatial/Core/PanelLayoutMath.cs` | MovablePanel, PanelResetChip | Exponential blend helper |
| Inline `SmoothStep` / `t*t*(3-2t)` / ease-out cubic copies | AgentMovement.cs:130; DocumentCourierController.cs:182,218; CeilingWorkspacePodController.cs:364; DocumentDeliveryController.cs:143; PushableProp.cs:130; ApprovalStampController.cs:224; RemoteDocumentDeliveryController.cs:405; WorkspaceTransitionMachine.cs:238; WallHabitatView.cs:488; Wall...Interaction.cs:231,254; StampTool.cs:233; HeldPaperController.cs:269 | About 14 copies (51 matches in 30 files for the generic pattern) | Candidates to switch to `Ease.Evaluate(EaseKind...)` and `MotionClock` |
| `AnimationCurve` fields | `MuseMotion.cs:83` (optional curve on `MotionToken`), editor-only particle curve `Editor/ArtPipeline/SpatialWorkspaceArtSetup.cs:1121` | No runtime scripts use authored curves | Fine |
| `StampPoofMachine.Smooth` | `Documents/StampPoofMachine.cs:178-190` | Stamp poof only | Local helper |

Conclusion: reuse `MuseDesk.Motion` (`MotionClock` for fixed-endpoint tweens, `ProfiledMover` for elevators/travel,
`Damped` for yaw following, `MuseMotionProfile` tokens for durations). Keep `ScaleEase` for scale show/hide.

## 5. Animator usage, root motion, play/crossfade, snaps and teleports

Root motion: no runtime script sets `applyRootMotion = true`. The scene and the character prefabs do not serialize
`m_ApplyRootMotion: 1` (grep over `Assets/**/*.prefab,*.unity` returned no matches); several props/factory prefabs and
the scene have it 0. Wall habitat explicitly forces it false (`Wall/WallHabitatView.cs:149`,
`Wall/WallHabitatController.Interaction.cs:492`). Clips are in-place cycles (`Agents/AgentAnimatorBridge.cs:175-179`).
Agent walking is driven by `AgentMovement`, not by the Animator.

| Issue | Evidence | Affected object | Severity | Suggested fix |
|---|---|---|---|---|
| Agent Animator uses CrossFade with MinBlend 0.2 s and sync of playback speed to ground speed | `Agents/AgentAnimatorBridge.cs:58,180-192,306,314` | Agent clips | Low | Good. Speed ramp `MoveTowards(_rate, want, dt*4)` ok |
| Hard `Play` on resolve: first clip starts with no blend at a random phase | `Agents/AgentAnimatorBridge.cs:220` | Agent initial pose | Low | Acceptable at start, but a re-enable (`OnEnable` resets, 132-147) pops to the base pose after a workspace re-activation. Consider CrossFade from the current pose |
| `AgentConversationPerchController.perchAnimator.Play("Perch_Listening_Idle", 0, 0f)` | `Art/AgentConversationPerchController.cs:158` | Perch prop | Med | Hard start (no fade) of a visible prop clip |
| Wall habitat avatars crossfade 0.2 s; portal avatar 0.3 s | `Wall/WallHabitatView.cs:464`; `Wall/WallHabitatController.Interaction.cs:492` | Habitat avatars | Low | Good |
| Pod, mount, factory, beacon, courier Animators use `CrossFadeInFixedTime` | `Art/CeilingWorkspacePodController.cs:440,448`; `Art/SoftwareFactoryVisualController.cs:74`; `Art/SpatialBeaconVisualController.cs:135,145`; `Art/DocumentCourierController.cs:267`; `Spatial/CourierVisual.cs:161`; `Spatial/CeilingPodArt.cs:161` | Props | Low | Blends present (0.10-0.18 s). `SpatialBeaconVisualController.cs:135` allows `immediate` with 0 blend |
| Agent end-of-walk teleports: position and rotation set exactly | `Agents/AgentMovement.cs:159,164,165` | Agent root | Med | Position snap is at the SmoothStep end (small), rotation snap can be large. Add a settle |
| Agent walk start has no turn-in-place: walks and turns at the same time | `Agents/AgentMovement.cs:153-155` | Agent root | Med | Profile defines `walkTurnLimit` (50 deg) and `startSeconds` but nothing uses them |
| Workspace root teleports on first placement and on recenter / HMD mount | `Workspace/WorkspacePlacement.cs:123-124,169-174,256-257,303,326` | WorkspaceRoot + children (agents inside) | Med | Ease re-place; skip while agents walk (transition controller does this, `WorkspaceTransitionController.cs:79-90`, placement does not) |
| Pod root snap | `Art/CeilingWorkspacePodController.cs:191,255` | Pod | Low | 255 is a test/restore path |
| Courier `SnapDockedImmediate` / docked-idle follow | `Art/DocumentCourierController.cs:149-155,161-164` | Courier root | Low | By design |
| Proxy returns/lerps instead of teleporting | `Wall/WallHabitatController.Interaction.cs:248-257` | Visit proxy | Low | Eased, fine |
| Habitat walker re-home sets Position directly | `Spatial/Core/Wall/HabitatWalker.cs:161-164,224,252-253` | Habitat avatar | Low | Done at visibility 0 (fade), 245-255; OK |

## 6. Instant visibility pops (SetActive / scale jumps on visible objects)

| Issue | Evidence | Affected object | Severity | Suggested fix |
|---|---|---|---|---|
| Task card changes lane: reparent then place at once, no ease | `Phase2/Runtime/Board/BoardPresenter.cs:181-186` -> `TaskCardView.cs:485-494` (`keepWorld` only if displaced/held) -> `TaskCardView.cs:456-472` (glide only if `_displaced`) | Task card (backend state change: queued to running to completed) | High | Backend-driven state changes teleport cards across lanes. Always `keepWorld` when a card is visible and ease to the new slot (it already has `BeginSettle` machinery) |
| Remaining cards snap to new slots when a card leaves a lane | `BoardPresenter.cs:267-296`, `TaskCardView.cs:468-471` | Task cards in the same lane | High | `PlaceAtHome` snaps non-displaced cards; always glide when the slot changes |
| New card appears at full size instantly | `BoardPresenter.cs:189-193` (`Rent`, `SetActive(true)`) | Task card | High | Reveal with `ScaleEase`/`ScaleReveal` (card `localScale` is already driven in `TaskCardView.cs:305`) |
| Card disappears instantly on recycle | `BoardPresenter.cs:196-205` (203) | Task card | Med | Ease out before `SetActive(false)` (FIXED S9a) |
| Card details sheet toggles with SetActive | `TaskCardView.cs:436-440` (439) | Card details | Med | Use scale reveal (ISDK rule noted in `ScaleEase` doc) (FIXED S9a) |
| Station drop highlight SetActive | `Board/StationDropZone.cs:64,109` | Drop halo | Low | Fade |
| Decision paper appears by SetActive off/on (OnEnable re-places it) | `Documents/DocumentDeliveryController.cs:75,108-109` | Decision paper | High | The paper starts collapsed in the agent hand (`HeldPaperController.cs:66-103`), but its pose is set by `OnEnable` (80-88) then corrected by `LateUpdate` (195) so the first visible frame can be off the hand. Scale-reveal and place before showing |
| Paper disappears with SetActive after filing, or immediately when not taken | `DocumentDeliveryController.cs:153,162` | Decision paper | Med | The filing glide only runs if `held.IsTaken` and `fileTarget` set (129); otherwise it blinks out. Also session reset (162) |
| Paper content switches between cover and sheet with SetActive | `Documents/HeldPaperController.cs:105-126` (114,121) | Paper cover/sheet | Med | Unfold cross-fade on grab; currently swapped at the start of `ExpandAndEnableActions` |
| Handoff token appears/disappears instantly and is re-parented hand to hand | `Agents/AgentHandoffController.cs:242,267-268,236` | Handoff token | High | Token pops in (267), jumps from source hand to target hand (236), pops out (242). Animate the transfer in world space |
| Agent touch summary card | `Agents/AgentTouchSummary.cs:97,114` | Touch card | Med | Instant show/hide on poke; scale-reveal |
| Decision archive filed sheet | `Documents/DecisionArchive.cs:261` | Filed sheet | Low | Pops when the filing glide ends; the card itself uses ScaleEase (149) |
| Approval stamp and mark | `Documents/ApprovalStampController.cs:139,150,194,207` | Stamp prop, mark text | Low | Stamp prop appears in place (207) and the mark pops at contact (194). Mark pop is intentional at impact; stamp prop appears instantly |
| Project shelf awards | `Workspace/ProjectShelfController.cs:76,82` | Award tokens | Low | Instant |
| Wall keys scaled to zero with no ease | `Wall/WallUi.cs:22-29` (26: `root.localScale = on ? shownScale : Vector3.zero`) | Wall poke keys | Med | Pops, also scale `Vector3.zero` where the project elsewhere uses `HiddenFactor` (0.001) because zero scale breaks poke maths (`OfficeUi.cs:47`). Use ScaleEase |
| Wall panel/dock shown with SetActive | `Wall/WallUi.cs:179`; `Wall/WallHabitatController.cs:507,514` | Wall panels, dock | Med | Instant |
| Habitat root shown and hidden with SetActive | `Wall/WallHabitatController.cs:371` | Whole habitat | High | See 1f |
| Perch pad, portal ring, visit marker, carry prop, select ring SetActive | `Wall/WallHabitatController.Interaction.cs:219,276,464,472`; `Wall/WallHabitatView.cs:419,431,439` | Wall interaction visuals | Med | Instant; ring and marker could pop in/out when a hand hovers |
| Printed card created at full size at the printer | `Wall/WallHabitatView.cs:469-481` | Wall task card | Low | Pops in then flies. Add a short scale-in |
| Ceiling pod visuals SetActive | `Spatial/CeilingPodController.cs:106,121`; `Spatial/CeilingPodArt.cs:89,145,174` | Pod art | Med | Hide/show pop; the same files already use ScaleEase on props (`CeilingPodArt.cs:122-124`) |
| Floor platform SetActive | `Spatial/FloorPlatformPresenter.cs:46,85,94` | Floor platform | Med | Instant (file has ScaleEase usage, verify scope) |
| Mailbox model | `Spatial/SpatialMailboxPresenter.cs:52,78,93` | Mailbox | Low | Guarded by `_ease` |
| Workspace root first show | `Workspace/WorkspacePlacement.cs:125,258,304` | Whole office | Med | `SetActive(true)` at the placed pose in one frame |
| Courier model | `Spatial/CourierVisual.cs:69-103` | Courier | Low | Show/Hide with SetActive; has `ScaleEase` references |
| NearUserKit rig active toggle | `Spatial/NearUserKit.cs:92,101,116` | Desk controls | Low | Follows root active state |
| Beacon art SetActive | `Spatial/AttentionBeacon.cs:380,225,190` | Beacon | Low | Animator restarts in default state (380 comment) |
| Edge plate scale grow | `Phase2/Runtime/Board/TaskCardView.cs:261` | Card edge plate | Low | Instant size change on hover |

## Recommended order of fixes (for the polish pass)

1. Task board: card lane change and neighbour reflow glide, new card reveal (BoardPresenter + TaskCardView, uses existing
   `BeginSettle` and `ScaleEase`).
2. Decision paper and handoff token: reveal in place, world-space token transfer.
3. Wall habitat: reveal/hide the whole habitat, ease keys (`WallKey.Show`), `ProfiledMover` for the elevator cabin and
   `elevatorDoor` token for the doors.
4. Agent walk: turn limit and settle at arrival (`AgentMovement`), replace slerp-per-frame facing with `Damped`.
5. Replace inline SmoothStep copies with `MotionClock` + `MuseMotionProfile` tokens opportunistically.
6. Pin execution order or ownership for RubberBandReturn/AgentMovement, PushableProp/WorkspacePropCarrier,
   RefaceUser/WorkspaceTransitionController.

## Not verified

- Which components are actually enabled on the shipped agent prefabs and scene (FaceUser vs FaceUserWhileDelivering,
  AgentView arm bones, both pod controllers, both courier systems). Needs a prefab/scene read.
- Frame rates and speeds for the PushableProp 2 cm-per-frame trigger.
- Whether the visible-pop rows are masked by art-controller animations on the headset (no WORN evidence is claimed).
