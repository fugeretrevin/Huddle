# A1 Movement ownership protocol

Status: core + tests written, NOT compiled, NOT run. Adapters below are a plan only; nothing outside the two new files was changed.

## What exists

- `Assets/MuseDesk/Scripts/Spatial/Core/Motion/TransformOwnership.cs` (`MuseDesk.Spatial.Core`, namespace `MuseDesk.Motion`)
  - `TransformOwnership` registry, keyed by an int (use `GetInstanceID()`), `TransformOwnership.Shared` for the app, tests make their own.
  - `OwnershipLease` (value struct: target, owner, priority, version), `MovePriority`, `OwnershipLossReason`.
  - `OwnedMover.Decide / Finish`: pure per-tick helper, no MonoBehaviour.
- `Assets/MuseDesk/Tests/EditMode/Motion/TransformOwnershipTests.cs`: 40 tests, guarded by `MUSEDESK_SPATIAL_TESTS` like `MotionTests`.

## Rules

Priority, lower wins: `UserHeld(1) > Carrier(2) > Navigation(3) > Ambient(4)`.

| Rank | Who |
|---|---|
| UserHeld | a hand holds the transform |
| Carrier | WorkspacePropCarrier during a mode transition, elevator carrying an agent |
| Navigation | AgentMovement walk, HabitatWalker |
| Ambient | RubberBandReturn, RefaceUser, PushableProp relax/settle |

- Strictly higher rank preempts; the old owner's callback runs once with `Preempted`. Equal or lower rank is refused (no stealing between peers; wait for Release/Cancel).
- Same owner re-acquiring at the same rank is idempotent. Changing rank issues a new version (old lease stale, no callback).
- `Release(lease)`: owner finished, no callback. `Cancel(target, reason)`: take it away, callback once. `Recover/RecoverAll`: clear leases whose owner fails `IsOwnerAlive`, no callback (owner is gone).
- Every grant takes a fresh version, so a lease that was released, cancelled, preempted or recovered never validates again. A mover calls `IsOwner` (or `OwnedMover.Decide`) before every write.
- Re-entrancy: new state is installed before the loss callback runs, so callbacks may acquire/cancel any target. Callback exceptions are swallowed and counted (`CallbackErrors`).
- No hidden statics except `Shared`. No per-tick allocation (leases are structs; `RecoverAll` reuses a list; supply the loss handler once, not as a per-tick lambda).

Mover pattern:

```csharp
switch (OwnedMover.Decide(TransformOwnership.Shared, ref _lease, id, myId, MovePriority.Navigation, OnLost))
{
    case WriteDecision.Write:   transform.localPosition = next; break;
    case WriteDecision.Blocked: break;          // a better owner has it
    case WriteDecision.Lost:    OnLostCleanup(); break;
}
// on finish / OnDisable: OwnedMover.Finish(TransformOwnership.Shared, ref _lease);
```

## Root-motion policy

Policy: all agent displacement is script-driven (AgentMovement, HabitatWalker, elevator carrier). Animator root motion is OFF (`applyRootMotion = false`) for every agent and prop in a hierarchy that can move with `WorkspaceRoot`, and no `OnAnimatorMove` may write a transform. Animations may only drive bones below the agent root, never the root.

Verified against the code today:
- `AgentScripts/BuildAgentCharacters.cs:295` sets `anim.applyRootMotion = false` when it builds agent prefabs (true at build time).
- No `OnAnimatorMove` exists in `Assets/MuseDesk/Scripts` (grep).
- No `m_ApplyRootMotion: 1` in any `.unity` or `.prefab` under `Assets` (grep). `MainMRScene.unity` and the Spatial/Factory prefabs show `m_ApplyRootMotion: 0`.

Violations / gaps:
1. Enforcement is build-time only. No runtime guard: nothing re-asserts `applyRootMotion = false` if an Animator is added or a prefab is rebuilt by another builder (Antigravity BuildAll pipeline). Suggest an EditMode test that loads agent prefabs and asserts false, plus a one-line set in the agent controller `Awake`.
2. Animators outside `BuildAgentCharacters` (props, couriers, pod) were not individually audited for clips with root curves; `applyRootMotion: 0` makes that harmless today.
3. Imported FBX clips with baked root translation are masked only by the flag; a future clip with "Root Transform Position (XZ)" will move the agent if the flag flips.

## Adapter plan (not applied)

All line numbers are in the 9a6fbc6 tree. Each owner needs a stable owner id (its own `GetInstanceID()`), a `static readonly` loss handler or cached delegate, and `IsOwnerAlive = id => ...` wired once (for example via `Resources.InstanceIDToObject(id) is Behaviour b && b.isActiveAndEnabled`, set in one bootstrap).

1. `Scripts/Agents/AgentMovement.cs` (`_moving` at 49, `Update` at 185, `OnDisable` at 179; position writes 216, 231, 266, 281, 286, 302, 324, 328). Target = agent root transform id. On walk start acquire Navigation; in `Update` gate each write block on `Decide == Write`; on arrival and in `OnDisable` call `Finish`. On `Lost`, stop the walk and idle (do not snap). Replaces the `IsMoving` convention.
2. `Scripts/Agents/RubberBandReturn.cs` (`Update` at 63, writes 117, 125). Replace the "yield while `movement.IsMoving`" check at 65 with `Decide(..., Ambient)`; Blocked means skip and zero the offset/contact. Removes the execution-order pin (comment lines 13-14) as a correctness dependency.
3. `Scripts/Workspace/WorkspacePlacement.cs` `RefaceUser` (271, rotation writes 285, 303, 308, 331; start sites 262, 320). Target = `workspaceRoot`. Acquire Ambient at 271; each loop step checks `Decide`; the transition controller acquires Carrier on the same target (see 4), preempting RefaceUser, whose callback `yield break`s via a flag. Replaces the `IsTransitioning` checks at 281, 316, 328 (keep them as belt and braces for one release).
4. `Scripts/Spatial/WorkspaceTransitionController.cs` (`NotifyRootWrite` call at 70) and `Scripts/Spatial/WorkspacePropCarrier.cs` (`_carrying` set at 233 and 335, `LateUpdate` 138). The transition acquires Carrier on `workspaceRoot` and calls `Cancel`-free preemption; the prop carrier holds Carrier leases per carried prop transform (written at 288, 312) and releases at 335. This replaces `NotifyRootWrite` plus `IsTransitioning` as the signal.
5. `Scripts/Workspace/PushableProp.cs` (`Update` 87; the `IsTransitioning` guard 91-101; writes 83-84, 147-152). Target = prop transform. Relax/settle writes require Ambient via `Decide`; a hand grab (ISDK select) acquires UserHeld in the grab event, releases on unselect; the carrier's Carrier lease preempts relax. Delete `_wasTransitioning`: on `Lost`, call `EnterSettling()` when the lease is next re-acquired.
6. Habitat: `Scripts/Spatial/Core/Wall/HabitatWalker.cs` is pure (no transform writes; `Tick` at 185), so the lease belongs to its applier: `HabitatStage.cs` (walker constructed at 103). Acquire Navigation on the agent transform while the walker moves, switch to Carrier while `HabitatElevatorMachine.IsRiding(agent)` (`HabitatMotion.cs:169`), release at `ReportExited`. Elevator `RiderEvicted` (walker line 104 subscription) maps to `Cancel(target, Cancelled)`.

Cross-cutting: when a grab or workspace transition starts it should call `Cancel` on children it wants cleared rather than relying on rank, and scene reload should call `TransformOwnership.Shared.Clear()`.

## Risks

- Not compiled. `readonly struct` needs C# 7.2 (Unity 6 is fine). Test asmdef/define `MUSEDESK_SPATIAL_TESTS` assumed from `MotionTests`.
- `.meta` files not created; Unity will generate them.
- `IsOwnerAlive` wiring is left to the integrator.
