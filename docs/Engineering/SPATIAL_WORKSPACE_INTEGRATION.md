# Spatial workspace: reach side integration

2026-11-13. Written by the Spatial-Reach agent. Status: **code written, never compiled, never run, never seen on a headset.** Nothing here has been verified beyond reading source.

The spatial workspace is an optional, flag-gated system in two halves. The placement half (other files in `Assets/MuseDesk/Scripts/Spatial/`) can move the whole office onto the floor beside the user or dock it near the ceiling. This document covers the reach half: telling the user that something needs them when the office is far away, and getting the decision paper into their hands without an agent walking across the real room.

The only contract between the halves is `Assets/MuseDesk/Scripts/Spatial/SpatialWorkspaceState.cs` plus the static events in `SummonRequests.cs`. Neither half references the other's components.

## 1. What is gated, and by what

There is no separate flag on the reach side. Everything keys off `SpatialWorkspaceState`, which only the placement side writes, and whose defaults are Desk mode.

| Thing | Desk mode (`IsRemote` false, `Mode` Desk) | Remote (`IsRemote` true) |
|---|---|---|
| `AttentionController` | Runs. Classifies reducer events. Shows nothing, plays nothing | Same |
| `AttentionBeacon` office cue | Hidden, silent (unless `showInDeskMode` is ticked; default off) | Shown above the concerned station for Blocking, Completed, Error |
| `AttentionBeacon` mirror cue | Hidden always | Shown near the user (`showMirror`, default on) |
| `RemoteDocumentDeliveryController` courier | Never re-points anything, never moves anything | Carries the paper to the user |
| `RemoteDocumentDeliveryController` transition hold | **Active**: acquires a hold while a hand holds the paper | Same |
| `DocumentDeliveryController.fileSeconds` | Untouched | Lengthened to 1.1 s from the moment the paper is taken until it is filed, then restored |
| `SummonController` keys | Hidden by scale; `PressBring` / `PressStatus` return at once | Shown near the user when `Mode != Desk` and not transitioning |
| `SummonController` ray targets | Near-zero scale | Usable (only built if the rig has ray interactors) |
| `PerchController` | `Open` returns at once (unless `allowInDeskMode`; default off) | Opens on request for 10 s |
| `SummonVoiceRouter` | Ignores everything | Routes two phrases, only if voice is enabled, available and (existing policy) live |

The one behaviour that runs in Desk mode is the transition hold. It changes nothing a user can see today: it increments a counter that only the placement side's transition code reads (`WorkspaceTransitionController.cs:75`, `CeilingPodController.cs:75`). It is deliberately mode-independent, because the case it exists for is "the user is holding the paper and a move from Desk to Floor is requested".

If the builder is never run, none of this exists in the scene and the scripts are inert.

## 2. Files

Runtime, all in `Assets/MuseDesk/Scripts/Spatial/`:

| File | What it is |
|---|---|
| `AttentionModel.cs` | `AttentionLevel` enum and the pure classification (no Unity types) |
| `AttentionController.cs` | Feeds the model from `SessionController.Reducer`; exposes level, agent, quiet count, `Changed` |
| `AttentionBeacon.cs` | The office cue and the near-user mirror cue |
| `RemoteDeliveryMachine.cs` | Pure delivery state machine with the clock passed in |
| `RemoteHoldLedger.cs` | Pure "at most one outstanding hold" wrapper |
| `RemoteNearUser.cs` | Near-user point, frame and facing helpers; cached head lookup |
| `RemoteDocumentDeliveryController.cs` | Moves the paper by re-pointing `HeldPaperController.followHand` |
| `CourierVisual.cs` | The tray; shown and hidden by scale |
| `SummonRequests.cs` | Static request events |
| `SummonController.cs` | Near-user keys and optional ray targets |
| `SummonVoicePhrases.cs`, `SummonVoiceRouter.cs` | Phrase matching and the optional voice route |
| `PerchController.cs` | The summary perch |

Tests: `Assets/MuseDesk/Tests/EditMode/SpatialReachTests.cs`. Builder: `AgentScripts/BuildSpatialReachV1.cs`.

## 3. Attention

### Levels

`AttentionLevel`, in priority order (highest wins): `None`, `Passive`, `Suggestion`, `Completed`, `Error`, `Blocking`.

| Level | Cause (reducer event) | Cleared by |
|---|---|---|
| Passive | `AgentChanged` | new run, acknowledge, reset |
| **Suggestion** | **Nothing. Unreachable.** | - |
| Completed | `WorkDelivered` | `TaskFailed`, new run, acknowledge, reset |
| Error | `TaskFailed` | `WorkDelivered`, new run, acknowledge, reset |
| Blocking | `DecisionRequested`, or a pending decision in a loaded snapshot | `DecisionResolved` for that id, or reset. Nothing else |

`Suggestion` is in the enum because the brief asked for the level to exist. The backend has no event for an optional agent suggestion (`Models/Enums.cs:7-21` lists every event type), and inventing one would break the honesty rule, so no code path produces it. A test asserts that after every model input.

Blocking outranks Error because it is the one the user can act on. When a decision resolves and a failure arrived meanwhile, the level falls to Error.

"New run" is `SessionController.RunStateChanged` with `RunInProgress` going from false to true.

### Focus mode

`AttentionController.Update` copies `FocusModeController.Active` (`Workspace/FocusModeController.cs:45`) into the model each frame; the model ignores an unchanged value.

- While focused, `AgentChanged` and `WorkDelivered` do not raise the level. They increment `QuietCount`.
- Blocking and Error are raised as normal and are not counted as quiet.
- Leaving focus keeps the count and surfaces a completion that was held back, so "Done" can appear late but never for work that was not delivered.
- Entering focus again starts a fresh count, mirroring `FocusModeController.SetFocus` (`FocusModeController.cs:147`).
- Nothing is ever approved, rejected or dismissed for the user. `Acknowledge()` cannot clear Blocking.

### The beacon and the existing "!"

The existing marker is `AgentView.alert` (`Agents/AgentView.cs:23-25`), switched by `AgentRegistry` on `DecisionRequested` / `DecisionResolved` (`Agents/AgentRegistry.cs:63`, `:79`). It is untouched and still appears above the Researcher in every mode. The beacon does not duplicate it at the desk (hidden there by default); in remote modes it is the version that can be read from across the room. Both are visible at the far office at once, which a person in the headset should judge.

Shapes, so state is never colour alone:

| Level | Plate | Words | Extra shape |
|---|---|---|---|
| Blocking | amber, 16 x 5 cm | Needs you | diamond with "!" above the plate |
| Completed | green, 10 x 5 cm | Done | none |
| Error | coral, 14 x 5 cm | Problem | bar across the top |

Sizes are at sizer scale 1. At run time the sizer is scaled so the plate height subtends `targetDegrees` (default 2, range 1.5 to 2.5) from the head: scale = 2 d tan(1 degree) / 0.05, clamped to 0.5 to 5. At 1.5 m that is a 5.2 cm plate with 2.3 cm words.

Motion: eased pop in, one 8 percent pulse when the level changes, then still. "Done" leaves by itself after 20 s; "Needs you" and "Problem" stay.

Sound: `FeedbackController.PlayCue(Attention)` on a change to Blocking, `PlayCue(Error)` on a change to Error, only when the cue is being shown. `FeedbackController` already plays those two cues itself from the same reducer events (`Feedback/FeedbackController.cs:175`, `:196`) and refuses to repeat a cue within `minRepeatSeconds` (`:55`, `:209`), and both handlers run inside the same `Reducer.Apply` call, so the result is one sound. That depends on both running in the same frame; it holds today.

The mirror cue is the same three faces at 0.4 size (2 cm plate, 8.8 mm words) at the near-user point plus (0.17 right, 0.13 up, 0.03 away). It is placed when it appears and moves only if the near-user point moves more than 15 cm.

## 4. Remote delivery of the decision paper

### How the paper is positioned today

Read from `Documents/HeldPaperController.cs`:

- `OnEnable` (`:66-103`): with `agentHoldPoint` set, the paper is parented to it and its local scale is set to `_fullScale * heldScale` divided by the hold point's `lossyScale` (`:80-88`). That division is the 100x rig bone compensation.
- `LateUpdate` (`:168-197`): while `!_taken && !_filing && agentHoldPoint != null && followHand != null` (`:178`), the paper's world position is `followHand.position + up * (half * 0.85)` (`:180-181`) and its rotation is yawed to `Camera.main` with a 12 degree tilt (`:188-195`). Only the position comes from `followHand`.
- `OnGrabbed` (`:239-258`): sets `_taken`, detaches with `SetParent(null, true)` (`:247`), then scales to `_fullScale`.
- `ReadyForDecision` (`:147`) depends on `IsTaken`, `_scaleLocked` and the read timer. Not on `followHand`.

### What the reach side does

`RemoteDocumentDeliveryController` re-points the public `followHand` field (`:200`) at `CourierAnchor`, a transform it owns under `Spatial/SpatialReach`, and animates that transform. It never re-parents the paper, never calls `SetActive`, never touches the decision id.

Phases (`RemoteDeliveryMachine`):

1. **AtOffice.** Nothing is changed. The Researcher walks to `DeliveryZone` and offers the paper exactly as at the desk (`AgentRegistry.cs:51-75`). The machine waits for `AgentMovement.IsMoving` to go false, then 0.7 s for the offer to read. The whole wait is capped at 3 s.
2. **ToPlatform** (0.45 s). `followHand` is swapped. The anchor starts at `paper.position - lift`, which is where the hand was, so there is no jump. It eases to the platform: office root-local (-0.20, 0.06, 0.33), computed with `WorkspaceRoot.TransformPoint`, so it follows the office's position, rotation and scale.
3. **Flying** (0.9 to 1.4 s by distance: 0.55 + 0.4 x metres, clamped). Smoothstep ease in and out, 12 cm rise. `AgentAnimatorBridge.EndOffer()` is called once so the agent lowers its empty hand (`Agents/AgentAnimatorBridge.cs:415`, documented there as safe at any time).
4. **Hovering.** The sheet's centre is at the near-user point. It shows the DECISION cover and TAKE tab as at the desk, upright, turned to the head by `HeldPaperController` itself. The hover point is latched on arrival and only re-latches if the near-user point moves more than 25 cm, so the paper does not chase the head.
5. **Taken.** `HeldPaperController` detaches and unfolds it. `followHand` is restored. The courier flies back to the platform in 0.9 s and hides.

Ceiling mode uses the same path: the platform is wherever the office root is, so the courier brings the paper down without the pod moving.

### Scale

The paper stays a child of `agentHoldPoint` for the whole flight, so its world size is whatever `OnEnable` computed. The placement side scales `WorkspaceRoot` uniformly (`WorkspaceTransitionController.cs:70`), and a rescale after the paper was shown would change the carried size, so `NormalizeHeldScale()` re-applies the `OnEnable` formula from public fields (`FullScale`, `heldScale`, `agentHoldPoint.lossyScale`) when the carry starts and when it arrives. On take, `_fullScale` was captured in `Awake` while the paper was a scene root, so the unfolded size does not depend on the office at all. I could not find a path that leaves the paper at the wrong scale, but the 100x compensation divides by a lossy scale and this has not been run.

### Restore points

`followHand` is put back when: the paper is taken; the paper is hidden; the office stops being remote; `SessionReset`; `OnDisable`. It is only put back if it still equals `CourierAnchor`.

If the office comes within reach while the paper is hovering untaken, the paper snaps back to the Researcher's hand on the next frame. That is abrupt. It is correct (the paper is reachable again) but should be seen.

### Fallback

- The courier path (ToPlatform plus Flying) may take at most 3 s. Past that the machine goes to Hovering with `SnappedByTimeout` set and the controller puts the anchor at the hover point with no glide.
- A non-finite position, or any exception while computing the path, does the same.
- If there is no published near-user pose, the target is 0.45 m in front of and 0.22 m below the head. If there is no camera either, nothing is delivered and the paper stays with the agent as at the desk.
- The backend is never waited on. This component only reads the paper's public state.

### Transition hold

`RemoteHoldLedger` owns at most one hold. Each frame: `Set(paperActive && paper.IsHeld)`. So it acquires on grab and releases on release, on resolve (the paper's `_filing` makes `IsHeld` false, or the paper is hidden), on reset (the paper is hidden), and explicitly in `OnSessionReset` and `OnDisable`. Extra releases are no-ops.

### Filing after the decision

`DocumentDeliveryController.DismissAfter` (`Documents/DocumentDeliveryController.cs:122-156`) waits up to 4 s for the hand to let go, then glides the paper to `fileTarget` (the out-tray, at the office) over `fileSeconds`, shrinking it to 12 percent with a fixed 5 cm arc (`:139-148`). With the office 1.5 m away the default 0.55 s is about 2.7 m/s.

From outside, the reach side sets the public `fileSeconds` to 1.1 s while the paper is taken and the office is remote, and restores it when the paper is hidden. The value is read when the glide starts (`:139`), so setting it at take time is early enough. The `[Range(0.2, 1.5)]` attribute is inspector-only.

What that does not fix: the arc stays 5 cm, which over 1.5 m is nearly a straight line, and the courier tray does not carry the paper back. See edit 2 below.

### Stamps

The stamps rest at the office and are not brought to the user. In remote modes the paper can be decided with its own Approve and Reject keys, which travel with it. Stamping needs the office summoned. This is a real gap in the remote flow, not an oversight in the code.

## 5. Summoning

`SummonRequests` (static): `BringOfficeHere`, `SendOfficeBack`, `ShowLatestUpdate`, `AskAgent(string agentId)`, each with a `Raise...` method.

**The placement director must subscribe to `BringOfficeHere` and `SendOfficeBack`. At the time of writing no file outside the reach side references `SummonRequests`, so the Bring key raises an event nobody hears.** `ShowLatestUpdate` and `AskAgent` are handled by `PerchController`.

They are requests. The key's words ("Bring it here" / "Send it back") follow `SpatialWorkspaceState.IsRemote`, so they change only after the placement side publishes the new state.

### Hand ray

I could not determine from files whether the rig has `RayInteractor` components: the rig is a Building Block prefab instance and its components are not in the scene YAML. `docs/HANDOFF_ANTIGRAVITY.md:36` records `DistanceHandGrabInteractor` on both hands and says nothing about rays.

So the builder checks at build time. If it finds any `Oculus.Interaction.RayInteractor`, it adds `RayInteractable` + `ColliderSurface` + trigger `BoxCollider` to the beacon's office cue (select asks for the office) and to three world-space stand-ins that follow the stations (select asks for that agent's perch). If it finds none, it adds nothing and says so in its report; the near-user keys are then the only summon path. No second interaction framework is added either way.

### Voice

The voice layer can be listened to without editing it, but only just. `IVoiceCommandSource` has no transcript event; `WitVoiceSource` reports what it heard through `StatusChanged` as `Heard: ...` (`Voice/WitVoiceSource.cs:158`, `:180`). `SummonVoiceRouter` subscribes to that and matches whole phrases.

Limits, all real:

- It applies the existing policy: `VoiceDecisionController.voiceEnabled` (default false, `Voice/VoiceDecisionController.cs:23`) and `requireLiveMode` (default true, `:21`).
- It never starts the microphone. Listening starts today only from the paper's Voice chip, so a phrase is heard only while a paper is open.
- It depends on the wording of a status string.

So voice summoning is wired but not practically usable. Edit 4 below would make it real.

## 6. The perch

A 13 x 7.5 cm stand at the near-user point plus (0.21 right, 0, 0.03 away). It prints the agent's name, LIVE or SCRIPTED DEMO, its activity in the nameplate's words (`Art/WorkstationStatusDisplay.cs:95`), and its own progress line, with a role-colour strip. Data is `Reducer.Snapshot.Agents` and `Reducer.FirstPendingDecision()`. It does not open for an agent the reducer has not reported. It closes after 10 s, on reset, on a snapshot load, and when the office stops being remote.

**Moving the actual character is deferred.** Only one visual of an agent may exist, and bringing the real one to the user would fight `AgentMovement` (which lerps from a world-space start point and owns the transform while moving), the Researcher's delivery walk in `AgentRegistry`, and the hand-off queue in `AgentHandoffController`, all of which assume the agent is at the office. Doing it properly means a movement owner that those three defer to. That is a change to files being edited by others, five days before the deadline, for an optional mode.

## 7. Assumptions about files the reach side does not own

Re-checked against the working tree at the end of the work; all held.

| Assumption | Where |
|---|---|
| `followHand`, `agentHoldPoint`, `heldScale`, `sheetHeight` are public fields | `HeldPaperController.cs:200`, `:30`, `:32`, `:202` |
| The follow uses only `followHand.position`, with lift `_fullScale.y * heldScale * 0.5 * sheetHeight * 0.85` | `HeldPaperController.cs:178-181` |
| The paper's `LateUpdate` runs after this component's `Update` (Unity order) | - |
| `IsTaken`, `IsHeld`, `FullScale`, `ReadyForDecision`, `TakeNow()` are public | `HeldPaperController.cs:137`, `:140`, `:166`, `:147`, `:232` |
| Nothing else reads or writes `followHand` at run time | grep: only `HeldPaperController` |
| A decision toggles `paperRoot` inactive then active, re-running `OnEnable` | `DocumentDeliveryController.cs:75`, `:108-109` |
| `fileSeconds` is public and read when the glide starts | `DocumentDeliveryController.cs:26`, `:139` |
| `AgentMovement.IsMoving` is true from the frame `DeliverTo` is called | `AgentMovement.cs:32`, `:45-50` |
| `EndOffer()` is safe to call at any time and twice | `AgentAnimatorBridge.cs:414-420` |
| `PlayCue` de-duplicates within 0.12 s | `FeedbackController.cs:61`, `:209` |
| `FocusModeController.Active` is a public static bool | `FocusModeController.cs:45` |
| `ActivityWords(string, bool)` is public static | `WorkstationStatusDisplay.cs:95` |
| Reducer events and `FirstPendingDecision()` | `SessionReducer.cs:22-38`, `:53` |
| `RunStateChanged`, `RunInProgress`, `IsLive` | `SessionController.cs:60`, `:52`, `:34` |
| `ScaleEase`, `OfficeTokens.Tint` | `Workspace/OfficeUi.cs:45`, `:29` |
| `Interactable.WhenStateChanged` is a public event of `Action<InteractableStateChangeArgs>` | SDK `Interaction/Core/Interactable.cs:144` |
| `RayInteractable.InjectSurface`, `ColliderSurface.InjectCollider` write serialized fields | SDK `Models/Ray/RayInteractable.cs:175`, `Surfaces/ColliderSurface.cs:118` |

## 8. Edits wanted in files the reach side does not own

None is required for what is built. Each would remove a workaround.

1. **`HeldPaperController`**: a public `Transform carryOverride`. In `LateUpdate` (`:178-181`) use `carryOverride` when set, else `followHand`. Removes the field swap and the restore logic.
2. **`DocumentDeliveryController.DismissAfter`** (`:139-148`): make the arc height a public field (`fileArcHeight`, default 0.05) and raise a public `event Action<Transform> FilingStarted` just before the loop. The reach side would then set a taller arc when remote and fly the tray alongside the paper.
3. **`HeldPaperController`**: public `event Action Taken` raised in `OnGrabbed` (`:246`). Replaces per-frame polling here and in `FeedbackController.cs:139-144`.
4. **`IVoiceCommandSource`**: `event Action<string> Transcript`, raised by `WitVoiceSource.OnResponse` (`:167`) with the raw text before classification. Plus a way to start listening that is not on the paper. Replaces the status-string parsing.
5. **Placement director**: subscribe to `SummonRequests.BringOfficeHere` and `SendOfficeBack`.
6. **Stamps**: either a second courier trip or rest pads that follow the near-user point in remote modes. Not designed.

## 9. Tests

`SpatialReachTests.cs`, EditMode, no scene. 35 tests in four fixtures: attention classification (15), hold balance (5), delivery machine (12), voice phrases (3). None has been run.

The test assembly cannot reference `Assembly-CSharp` (`Phase2RuntimeTests.cs:1-26` explains why the existing real-type suite is fenced off). Rather than mirror the logic or fence these tests off too, they reach the real types by reflection. The cores were written as plain classes with primitive-typed members for that reason. The cost: a rename shows up as a failing test, not a compile error.

Not covered by tests: anything with a `Transform`. The `MonoBehaviour`s are thin, but the field swap, the scale normalisation and the placement maths are untested.

## 10. Play Mode driver

Parameterless public methods on `RemoteDocumentDeliveryController` (on `Spatial/SpatialReach`), callable the way `PlayDrive.Call` calls things:

| Method | Does |
|---|---|
| `DriverForceRemote()` | Publishes Floor / remote with the near-user pose 0.45 m in front of the camera |
| `DriverForceRemoteFar()` | Same, with the near-user pose 1.9 m from the office (full-length flight; off camera) |
| `DriverForceDesk()` | Publishes Desk |
| `DriverTake()` | `HeldPaperController.TakeNow()` |
| `DriverSnap()` | Forces the fallback |
| `DriverReport()` | Returns one line: state, progress, hold counts, paper position and lossy scale, taken, ready |

Also `DriverForceRemoteAt(Vector3)`. These write `SpatialWorkspaceState` as the placement director would; if the director is running it will overwrite them.

Sequence: `DriverForceRemote`, start the demo, wait for the paper, poll `DriverReport` (expect AtOffice, ToPlatform, Flying, Hovering inside about 4.5 s, lossy scale unchanged throughout), `DriverTake`, approve, expect the hold counts equal and state Idle after filing.

Others: `SummonController.PressBring()`, `PressStatus()`; `SummonRequests.RaiseAskAgent("developer")`; `PerchController.IsOpen`; `AttentionBeacon.OfficeCueShown`, `ShownLevel`; `SummonRequests.LastRaised`.

## 11. What only a headset can verify

- Whether the hover point is comfortable to reach and the small cover (about 7 cm tall) is easy to grab there.
- Grabbing the hovering paper: the hand grab against a paper that is being re-positioned every `LateUpdate`, and whether the distance grab on `DecisionDocument` fights it.
- Whether the arc reads as a delivery or as a paper teleporting with a tray under it.
- Beacon legibility at 1.5 to 3 m, and from below in Ceiling mode.
- Whether the beacon and the small "!" together are too much.
- The filing glide from the user to a far out-tray.
- Ray summon, entirely: whether the rig has ray interactors, whether a trigger collider is hit by `ColliderSurface.Raycast`, whether ray targets disturb near interactions.
- The keys cloned from `FocusToggle` at a world-space position that is set at run time (press travel, `PokeVisualRestGuard`).
- Audio: one cue or two.
