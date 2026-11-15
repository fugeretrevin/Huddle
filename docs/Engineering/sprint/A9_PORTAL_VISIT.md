# Sprint A9: portal and desk-visit lifecycle (pure core logic)

Status: written without the Unity Editor. NOT compiled, tests NOT run. Flags stay OFF. Namespace `MuseDesk.Spatial.Wall`, assembly `MuseDesk.Spatial.Core`. No existing file was modified (AgentVisitFlow.cs, PortalPlacement.cs are untouched; the new types sit beside them).

## Files
- `Scripts/Spatial/Core/Wall/PortalSpotPlanner.cs`: `PortalBox`, `PortalRoomBounds`, `PortalSpotSettings`, `PortalSpot`, `PortalSpotPlanner`, `PortalSpotTracker`.
- `Scripts/Spatial/Core/Wall/PortalAvatarScale.cs`: `PortalScaleSettings`, `PortalAvatarFit`, `PortalAvatarScale`.
- `Scripts/Spatial/Core/Wall/PresentationLifecycle.cs`: `PresentationLifecycle`, `PresentationIdentityMap`, `PresentationTether`, enums, `PresentationTimings`.
- `Tests/EditMode/Wall/PortalVisitLifecycleTests.cs` (22 tests, `#if MUSEDESK_SPATIAL_TESTS`).
- No .meta files were written; Unity generates them on import (commit them after compiling).

## API
### Spot planning
- The view flattens MRUK to `PortalRoomBounds` (XZ boxes): `floor` rect, `floorY`, `ceilingY` (<= floorY means unknown), `obstacles` (furniture) and `keepOut` (screens, desk front). Bump `revision` when the room changes.
- `PortalSpotPlanner.Plan(user, room, settings)` returns the best side spot or the fallback. Rules: outside the front cone (45 deg) and not behind (135 deg), outside the comfort envelope (`personalRadius` + body), conservative margins (walls 0.20, furniture 0.15, keep-out 0.35, all beyond the body radius), head room under a known ceiling. Candidates: user's right side first, then left, angles 60..115 deg, distances 1.0..1.6 m.
- Fallback: `isFallback = true`, `valid = false`, reason text ready for the UI ("No clear space found..." or "...unchecked" without a floor). Position is user right at `personalRadius + bodyRadius + 0.1`, depends only on the user pose, so it does not jitter.
- `PortalSpotPlanner.Evaluate(...)` checks one XZ point (for "Try another" / drag preview).
- `PortalSpotTracker.Update(now, user, room)`: call every frame, it evaluates at most every 0.75 s, only after the user moved 0.30 m, turned 25 deg, the room revision changed, or the current spot became invalid; a still-valid spot is replaced only by one scoring 0.15 better. `EvaluationCount` counts adoptions (for tests).

### Avatar scale
- `PortalAvatarScale.Compute(nativeHeight, nativeFootOffset, nativeRadius, targetHeight, floorY, ceilingY, settings)` returns uniform `scale` (clamped to range and ceiling), `rootY` (feet exactly on floor), `height`, `bodyRadius`, `clamped`.
- `Step(current, target, dt, settings)` for a calm grow; `RootYFor(scale, footOffset, floorY)` each frame while scaling keeps feet planted.
- `ClearsUserEnvelope(bodyCentre, bodyRadius, user, envelopeRadius)` / `GapToUser`.

### Lifecycle (one instance per kind, share one `PresentationIdentityMap`)
- `new PresentationLifecycle(PresentationKind.DeskVisit | Portal, sharedMap)`.
- `Open(presentationId, logicalAgentId, homeAnchor)` -> `Started | QueuedUntilClosed | AlreadyOpen | RejectedBusy | RejectedInvalid`. While Closing, the request is queued (latest wins) and starts after Closed.
- `MarkReady()` (view finished opening early), `RequestClose()`, `Cancel()` (any point, also withdraws a queued open), `BackendLost()` (immediate cleanup, no animation), `Tick(dt)`.
- Events per started session, exactly once each and in this order at the end: `ReturnHome(presentationId)` then `Closed(PresentationClosed{reason Finished|Cancelled|BackendLost, session})`. `Opened` fires on entering Presenting. `IsHome` is true when Idle.
- Identity: one live presentation per logical agent across visit AND portal when they share the map; the second `Open` for the same agent returns `RejectedBusy`. The class has no backend reference: it cannot start, pause or duplicate work, and it mutates no agent state.
- Tether: `lifecycle.Tether(presentationPoint)` gives `{active, from = HomeAnchor, to, alpha, length}`; the alpha fades in while Opening and out while Closing.

## What the view layer must call
1. Create one `PresentationIdentityMap`, two lifecycles. Tick them from Update.
2. Feed `PortalRoomBounds` from MRUK (flatten, add screen / desk-front boxes to `keepOut`), `PortalSpotTracker.Update` each frame, show the fallback text when `isFallback`.
3. On `Open`, place the presentation at `spot.position/rotation`, set scale with `PortalAvatarScale.Compute`, grow with `Step` and `RootYFor`.
4. On `ReturnHome`: hide the presentation and restore the home avatar (idempotent; always runs exactly once). On `Closed`: free UI.
5. Call `BackendLost()` from the backend disconnect handler and `Cancel()` for pause / recenter / mode change / tracking loss.
6. Draw the tether line from `Tether(...)` (a LineRenderer, no per-frame allocation).
7. Existing `AgentVisitFlow` / `PortalFlow` / `AgentPresentationRegistry` still own the current wiring; migrating them onto `PresentationLifecycle` is an integration decision, not done here.

## Risks / untested
- Everything uncompiled. Check `Vector2.Angle`, `SeatedUser.Right` sign (positive spot angle = user's right, derived by hand).
- Planner geometry assumes an axis-aligned floor rectangle; rotated rooms need the MRUK adapter to supply a conservative AABB.
- Tests were not run; numeric thresholds in `Evaluate_Rejects...` are hand-computed.

## Applied (Wall controller adapter)

Rollback: `WallHabitatController.usePresentationLifecycle` (default true). False restores the previous path: legacy `PortalPlacementEvaluator.Find`, no mirror, no tracker, no fit clamp. With the Wall flags off nothing changes (the flows never activate).

Files: `Scripts/Spatial/Core/Wall/WallPresentationBridge.cs` (pure: `PresentationMirror`, `BackendLossDetector`, `PortalSpotAdapter`), `Scripts/Wall/WallHabitatController.Presentation.cs` (new partial), small hooks in `WallHabitatController.Interaction.cs`, tests `Tests/EditMode/Wall/WallPresentationBridgeTests.cs`.

- One presentation per logical agent: `PresentationMirror` mirrors `AgentVisitFlow` / `PortalFlow` each frame into two `PresentationLifecycle`s sharing one `PresentationIdentityMap`. Closes run before opens, so the visit-to-portal handover of the same agent registers cleanly; a simultaneous second presentation is rejected and counted (`RejectedCount`, `LastRejection`). The legacy flows still own states and animation; the lifecycle is opened Presenting at once.
- Cancel / return home: `CancelPresentations()` (also for drivers) cancels both; the ReturnHome handlers send the flows home (`_visit.Return`, `_portal.Interrupt`) exactly once per started session, Closed(reason) once. A flow that ended on its own closes its lifecycle with reason Finished.
- Backend lost: `BackendLossDetector` sees the live-to-not-live edge of `IWallBackend.IsLive` (offline demo is never a loss). It calls `BackendLost()`; the Closed(BackendLost) handler forces immediate cleanup: hands released, proxy and portal clone destroyed, ring hidden, visit session cleared, flows returned. `NotifyBackendLost()` is public for an explicit disconnect hook. Nothing is sent to the backend.
- Portal spots: `PortalSpotAdapter.Build` flattens `IRoomGeometry` to `PortalRoomBounds` (floor AABB only when the floor fills >= 90% of its box, furniture as conservative AABBs, ceiling at the head). `ListSpots` gives the side-spot cycle ("Other spot"); `PortalSpotTracker` follows the user with hysteresis while the ring is shown, until the user presses "Other spot". Non-rectangular or missing room: the exact legacy polygon evaluator is used instead. No clear large spot: `PortalSpotIsFallback` is true, the offer is the small unchecked visitor and the card says "Not enough space here for a large visitor; a small one can still come."
- Scale / feet: the ceiling fit uses `PortalAvatarScale.Compute` (via `PortalSpotAdapter.FitHeight`) when the candidate is planned and again when the clone spawns; feet stay on the spot's floor Y (clone root at the spot).
- Tether: `VisitTether` and `PortalTether` (`PresentationTether`: from = home avatar, to = projection / portal chest, alpha by phase) are exposed. No line is drawn yet (needs a LineRenderer/material in the view, which another agent owns); the view can draw it from these without allocation.

Not done / risk: keep-out boxes for the Wall screens and desk front are not added (the planner's front cone still applies); the avatar-envelope check (`ClearsUserEnvelope`) is not wired; scene behaviour is unverified in Play Mode.

Verification (no Unity Editor): `MuseDesk.Spatial.Core` sources compiled with Unity's bundled csc against UnityEngine reference DLLs (0 errors); the EditMode tests (new and A9) compiled against them and NUnit (0 errors, NOT run); `Scripts/Wall/*.cs` compiled against the main tree's `Library/ScriptAssemblies` (0 errors).
