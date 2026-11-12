# MuseDesk full system audit

Date: 2026-11-12. Branch `integration/audit-repair`. Unity 6000.6.8f1, Meta XR SDK 209, backend FastAPI (Python, `backend/.venv`).

This is the inventory and findings record. What was changed in response is in `REPAIR_AND_INTEGRATION_LOG.md`; what is still open is in `REMAINING_ISSUES.md`; test evidence is in `END_TO_END_TEST_RESULTS.md`.

## How this audit was done

| Area | Method | Detail record |
|---|---|---|
| Unity runtime code | Every C# file under `Assets/MuseDesk/Scripts` and `Assets/MuseDesk/Phase2/Runtime` read and traced from backend event to visible result | `_audit_unity_runtime.md` (per-finding file and line evidence) |
| Scene | Editor scripts run through the Unity pipeline against the open `MainMRScene`: missing scripts, unassigned references, poke-button wiring and facing, group footprints, captures from the seated eye point | `AgentScripts/SceneHealth.cs`, `SceneProbe.cs`, `ButtonProbe.cs`, `LayoutBounds.cs`, `ViewFrom.cs` |
| Backend | Code review plus running the service and tests; cross-session token probes against every route | Section 4 below |
| Performance | Static read of project settings, URP assets, the scene YAML, FBX and `.meta` files, and per-frame scripts | Section 5 below |

"Confirmed" means the defect follows from the code or was reproduced by running it. "Suspected" means it depends on something only the headset can show.

## 1. Baseline at the start

- Unity compiled with 0 errors. `MainMRScene` is the only scene in the build list.
- Scene: 2,625 GameObjects, **0 missing scripts**, one XR rig, one enabled camera, one audio listener.
- Backend: 93 tests passing.
- Unity NUnit suite: 54 tests passing, but they test *mirrors* of the runtime constants. The suite that exercises the real types (`Phase2RuntimeTests.cs`) is compiled out, because a test assembly cannot reference `Assembly-CSharp`.
- Quest 3 `2G97C5ZHCK037K` connected over USB.
- Restore point: commit `c7b31e8`. Uncommitted work recovered from an orphaned git stash and committed as `1b0d9f0` before anything else was touched.

## 2. Feature inventory

Status at the start of the audit, and after the repair pass. "Code-verified" means compiled plus covered by an automated check; it is **not** a claim about on-head behaviour.

| Feature | At start | After repair | Notes |
|---|---|---|---|
| Live or demo selection and mode label | Functional | Functional | Label comes from the gateway that was actually adopted |
| Offline scripted session | Functional, with a fabricated success on reject | Code-verified | Reject no longer reports completed work |
| Workspace placement | Mis-integrated | Code-verified, needs on-head check | See 3.1 |
| Agent state to animation | Functional | Functional | All nine backend states map; listening/speaking have no visual |
| Agent walk-up delivery | Functional, rotation defect | Code-verified | Walks to a fixed hand-over spot on the user's side; turns back to the desk on return |
| Paper grab, expand, detach | Functional (verified on head 2026-11-11) | Unchanged | |
| Approve / reject by poke | Functional | Functional | |
| Approve / reject by physical stamp | Broken | Code-verified, needs on-head check | Stamps drifted to a stale world position and were 87 cm away |
| Failed-response handling | Missing | Code-verified | Paper restores its buttons with a message; stamps re-arm |
| Researcher to developer handoff | Mis-integrated | Code-verified | Two controllers fought over the researcher |
| Developer to tester handoff | Broken in the demo | Code-verified | Was dropped while the first handoff was still walking |
| Software factory | Mis-integrated | Code-verified | Approval was shown as a test pass |
| Testing station lamps | Partial | Code-verified | Stuck on Testing; state shown by size only |
| Workstation monitors | Partial | Code-verified | Now show the agent's real progress line |
| Completed-project shelf | Partial | Code-verified | Counted rejected runs and baseline tests as finished projects |
| Task board rendering | Functional | Functional | Placeholder preview cards removed |
| Task board live commands | Broken | Code-verified | Live token was never stored, so the board was always the demo board |
| Drag to assign | Partial | Partial | Command path sound; card now shows pending / confirmed / rejected. Still no drop-zone highlight |
| Pause, resume, priority | Unusable (1.8 cm targets on a grabbable card) | Wired, needs on-head check | Desk-side buttons act on the last-grabbed card |
| Create task | Broken in the demo (blank card) | Code-verified | |
| Artifact viewer | Visual only | Partial | Paging fixed; never opens offline because the demo has no artifact fixture |
| Focus mode | Partial | Code-verified | Held-back updates can be reviewed after leaving focus |
| Architecture diagram | Partial | Partial | Builds; label size and selection unresolved |
| Layout persistence | Visual only (never saved) | Code-verified | Saves on pause, per build |
| Reconnect and resume | Partial | Code-verified | Snapshot is projected to the agents and the pending paper; badge shows reconnecting |
| Voice decisions | Unsafe ("don't approve" approved) | Code-verified | Voice build only; needs a Wit.ai token and network |
| Hand push / finger collision | Parked | Parked | `DynamicFingerCollision.GloballyEnabled = false` |

## 3. Unity findings

The ten highest-impact confirmed defects, with evidence, are in `_audit_unity_runtime.md` section 1. Summary:

| # | Sev | Defect |
|---|---|---|
| 1 | P1 | Self-stamps glide to a stale world position |
| 2 | P1 | Live token never stored; the task board never goes live |
| 3 | P2 | Handoff and return-home both drive the researcher |
| 4 | P2 | Second handoff dropped |
| 5 | P2 | Reject shows success in the demo (shelf token, PASSED lamp, chime) |
| 6 | P2 | "+ New task" creates a blank P0 card in the demo |
| 7 | P3 | Agents never restore their station rotation |
| 8 | P2 | A human approval plays the factory's test-success stage |
| 9 | P2 | A failed respond leaves the paper stuck and locks the stamps |
| 10 | P2 | Artifact viewer is 1-based; backend and demo are 0-based |

Further findings found after that document was written:

### 3.1 Facing and layout disagreed (P1, confirmed by measurement)

`WorkspacePlacement` pointed `WorkspaceRoot` +Z at the user, but the scene was laid out for the opposite side. Measured in root-local space with the eye at (0, +0.30, +0.55):

- Stamps, the hand-over spot and the trophy shelf sat behind the agents; the stamps were about 0.87 m from the eye.
- The task board was 0.2 m in front of the face and 0.35 m above eye level.
- The agent delivering a paper walked to a point inside the factory conveyor.

The user confirmed the intended side on 2026-11-12: **agents face the user**. The layout was rearranged to match (repair log, section 4).

### 3.2 Button labels (P2, confirmed by capture)

- `+ New task`, `Prev`, `Next`, `Close` wrapped one letter per line (word wrap on, in a rect narrower than one glyph).
- `＋`, `◀`, `▶` are not in the TMP font and rendered as boxes.
- `Focus` and `Explain` labels were scaled to 0.01 under a 0.05 parent: about 0.3 mm tall, effectively invisible.
- `PrevPoke`'s press visual was offset sideways from its poke surface.

### 3.3 Things that looked like data but were not (P2, confirmed)

- Four `PreviewCard_*` objects and two `DependencyLink_*` objects on the task board were static art. They read as tasks and dependencies but were driven by nothing.
- The artifact panel's Prev / Next / Close buttons were always visible, floating beside an invisible panel.
- A `task.completed` event is also sent for the baseline test run and for a cancelled branch after a rejection. The shelf, completion chime and factory delivery treated every one as a finished project.

### 3.4 Development leftovers in the production scene (P3)

- `[BuildingBlock] Grabbable Item` (orange test cube) placed 0.45 m in front of the user.
- `GrabReadout`: every 0.5 s it searched for the paper by name, read fields by reflection, logged, and overwrote the status text.
- `RuntimeSettings.showDiagnosticsOverlay` on, showing device model, seq and fps to the user.

## 4. Backend findings

Baseline 93 tests. Reviewed: v1 routes (`app/main.py`), v2 routes (`app/phase2/`), WebSocket replay, idempotency, the approval gate, session scoping, the sandbox (`workspace_guard.py`), restart behaviour, and `source_mode` labelling.

Confirmed and fixed (details in the repair log):

| Sev | Defect |
|---|---|
| P1 | After a backend restart with a decision pending, the user could still respond and was told it applied, but nothing ran and nothing failed |
| P2 | A second task could be started while a run was in progress; in live mode both runs share one workspace directory |
| P2 | Sandboxed `pytest` inherited the full host environment, including `MUSEDESK_PROVIDER_API_KEY` |
| P3 | A cancelled branch was announced as a bare `task.completed` |
| P3 | Task create echoed another route's idempotency record as a "task" |
| P3 | The scripted producer swallowed exceptions |
| P3 | The event WebSocket handler never noticed a client disconnect |
| P3 | The test-run timeout path could raise instead of reporting a timeout |
| P3 | A handoff accepted another session's artifact id (internal only) |
| P4 | A drive-qualified path raised a raw `ValueError` instead of a workspace error |

Verified sound by running it:

- A token for one session is rejected (401, WebSocket close 4401) on every v1 and v2 route of another session.
- The v1 and v2 event streams use separate tables and counters. The v2 stream is dense from 1, so the Unity v2 reducer sees no gaps from v1 activity.
- `implement_oauth` is reachable only after the decision is atomically resolved as approved.

Found and not fixed: see `REMAINING_ISSUES.md` section 2.

## 5. Performance and comfort (static only)

Nothing in this section is an on-device measurement.

Already right: Vulkan, multiview, IL2CPP ARM64, linear colour, MSAA 4x, HDR off, no depth or opaque texture, no post-processing, **no lights and therefore no shadow pass**, one rendering camera, no UGUI canvases, no particle systems, roughly 50 to 75 thousand triangles in total.

| Sev | Finding | Evidence |
|---|---|---|
| P1 | About 520 separate renderers with no batching help. Each workstation FBX is 53 to 60 meshes; the Kanban frame is 36. Dynamic batching and the GPU Resident Drawer are off, and the root moves at runtime so static batching does not apply | Scene YAML counts, FBX contents, `m_GPUResidentDrawerMode: 0` |
| P2 | No foveated rendering on Android, while dynamic resolution may render up to 1.6x | `Assets/XR/Settings/OpenXR Package Settings.asset`; OVRManager in the scene |
| P2 | The Quest quality level points at a URP asset inside `Library/PackageCache`, which cannot be tuned or versioned | `QualitySettings.asset`, build profile |
| P2 | All art materials are URP/Lit with specular and reflections, for a scene with no lights | 33 `.mat` files, 18 embedded materials |
| P2 | Hand physics capsules (about 40 kinematic bodies) on the Default layer with a fully enabled collision matrix; physics at 50 Hz; the two pushable props have no interpolation | `DynamicsManager.asset`, `TimeManager.asset`, scene |
| P3 | Per-frame boxing from reflection reads of `SelectingPointsCount` in `StampTool`, `GrabbableRigidbody`, `HeldPaperController` | Source |
| P3 | `renderer.material` instancing in `TaskCardView`, `CardRoleTint`, `StationDropZone` | Source |
| P3 | All 61 FBX: Read/Write on, no mesh compression, Generic rig on static props (an Animator with no controller on each) | `.meta` files |
| P3 | Camera near clip 0.1 m clips desk-scale miniatures when leaning in | `CenterEyeAnchor` |
| P3 | MRUK loads the room and asks for scene permission at startup, but off-desk placement never uses the table | Scene |

Reach, estimated from transforms with the eye at root-local (0, 0.30, 0.62) after the rearrangement: stamps 0.38 m, Explain 0.45 m, task board centre 0.52 m, Focus 0.55 m, hand-over spot 0.58 m, artifact panel 0.60 m. Still beyond about 0.6 m: the two pushable props (0.75 m) and the agents themselves (0.64 to 0.75 m).

## 6. What only the headset can settle

1. That the workspace now appears in front of the user with the agents facing them, and that the board, stamps and buttons are comfortable to reach.
2. Stamp press feel and the 6 cm contact radius.
3. The desk-side Pause and Priority buttons acting on the last-grabbed card.
4. Legibility of the 2 cm button labels and the card text at the board's new distance.
5. Frame rate, draw calls and thermals.
