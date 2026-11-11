# TASK-01 — Unity / Quest 3 XR foundation

**Owner:** XR engineer/AI agent. **Priority:** P0. **Prerequisites:** verified Unity 6 environment, Quest device, MVDH. **Do not edit:** backend models/contracts.

## Goal

Create a clean Unity 6 project that runs on Quest 3, displays passthrough, has working hand tracking and one interaction rig, and provides desk-relative workbench placement via MRUK or manual fallback.

## Implement

1. Create MVDH Unity XR project: OpenXR, Meta XR All-in-One, URP. Record editor/package versions and commit lock files.
2. Configure Android/Meta Quest build profile, Meta XR OpenXR features, Project Setup Tool required items.
3. Add one correct Interaction SDK rig, enable hand selection/grab/poke. Delete duplicate default camera.
4. Add passthrough, test Quest hardware. Never request raw camera permission unless actually needed for raw frames.
5. Add MRUK scene-loading, spatial-data permission, table anchor query; wait for scene-loaded before querying.
6. Implement `WorkspacePlacement` with hand-confirmed placement and manual fallback if permission denied or no table detected.
7. Add empty `WorkspaceRoot`, `DeliveryZone`, `StationAnchor` placeholder transforms, accessible **Move/Reset** control.
8. Add `DiagnosticsOverlay`: hardware, mode, current scene status, endpoint, FPS and last event ID; controllable debug visibility.

## Deliverables

- `quest/` Unity project, `MainMRScene`, `WorkspaceRoot.prefab`, `WorkspacePlacement.cs`, setup notes and build scripts if useful.
- Screenshot/capture from Quest of passthrough and placed workbench.
- One APK build path and exact command/run steps.

## Acceptance

- [ ] `adb devices` sees Quest and Build & Run launches app.
- [ ] Hands can select at least one world-space button with controllers set down.
- [ ] Scene data permitted → desk-relative workbench placement works.
- [ ] Scene data denied/unavailable → manual placement works.
- [ ] Only one XR rig and no fatal Unity/Android logs.
- [ ] Project cleanly reopens on another team member's machine with pinned packages.

## Coordination

Provide interface hooks `WorkspaceRootTransform` and `AgentStationAnchor[role]`. Merge before character agent mounts prefabs; no other worker should edit scene concurrently.
