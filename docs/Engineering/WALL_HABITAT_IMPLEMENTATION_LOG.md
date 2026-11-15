# Wall Habitat: implementation log

Branch `feature/wall-habitat`. Restore point: tag `pre-wall-habitat` (`e14df8e`).

| Commit | What |
|---|---|
| `b3815ef` | Backend: read-only side questions; Manager plan propose, revise, reject and commit (atomic, idempotent, revision-checked, dangerous items need authorisation); p3 flags default off; 194 pytest |
| `d23286a` | Pure core plus 67 tests: wall placement and persistence, layout and validator, nav, leases, elevator, stage, visit, portal and manager flows, AgentSelect claim |
| `e814a74` | Runtime: controller, view, UI kit, gateway, event bridge, MRUK walls, `WorkspaceMode.Wall`, director host hook, release flags (off) |
| `589faa6` | Scene builder `BuildWallHabitatV1`, Play Mode driver and walkthrough, first PLAY fixes |
| `f5f1217` | Isolation-guard fix, manager placeholder, validation switch, docs |
| `6bb050d` | ReleaseConfig asset gains the six fields (all 0), status, tests, performance and log docs |
| (this) | Claude provider (`AnthropicProvider`), plan runner (backend + offline demo board), static-structure merge, Unity timeouts above backend model deadlines, `scripts/start_wall_backend.ps1`; 207 pytest, 98 Wall EditMode |

## Decisions

1. **Wall is a host of the director, not a fifth core mode.** `SpatialDirectorCore` (Desk/Floor/Ceiling, swept-route safety) is untouched. The director publishes Wall and forwards requests to the host. Other modes leave Wall first.
2. **The desk office is hidden, not moved or disabled.** Only renderers are switched off, so agent scripts, decisions and the remote paper delivery keep running exactly as in Floor mode. Wall publishes `IsRemote = true`.
3. **Habitat characters are visual clones.** Their scripts are stripped before activation, so there is never a second worker. The visit and portal projections are clones too.
4. **Collision truth lives in data, not in art.** The proxy art is drawn from the same boxes the validator proves clear. Art modules must fit those boxes.
5. **Corridor leases plus a travel lock** replace node-by-node reservation. The single-file corridors made face-to-face deadlocks possible. A blocked trip soft-transfers (fade, re-home) and never walks through anything.
6. **Select Agents uses the rc6 pinch arbiter**, not an ISDK distance-grab interactable on the agent. Distance-grabbing would move the canonical rig; the arbiter already gives holding and UI precedence and treats a lost hand as a cancel.
7. **Preset questions and objectives on poke keys** (typing in VR is impractical). `AskText` and `RequestPlan` take free text for a later voice hook.
8. **Offline behaviour is honest.** `/btw` answers come from local task state, labelled `OFFLINE_SNAPSHOT` or `SCRIPTED DEMO`. Offline plans are `SCRIPTED_DEMO`. Offline commits create real demo-board cards through the existing v2 path.

## Problems found and fixed

1. **Elevator lane starvation (AUTO).** A second rider waited behind a 25 s round trip and its act expired. The start deadline is now 60 s.
2. **Face-to-face deadlock risk in single-file corridors (design review).** Replaced by segment leases.
3. **Projection followed the hand during the release debounce (PLAY).** It now holds while the pinch opens.
4. **Projection was invisible (PLAY).** It was cloned from desk agents whose renderers were hidden. It is now cloned from the habitat avatars.
5. **Desk monitors hid the characters (PLAY capture).** They are now low laptops. Railings and doors were recoloured.
6. **The Manager's colour tint rendered translucent (PLAY capture).** Removed; the Manager now has a cap and badge only.
7. **`SpatialIsolationTests` tripped on a comment (AUTO).** The comment was reworded.
8. **Plan cards never started offline (PLAY).** The runner waited for the scripted login pipeline, which sat at its decision. Plan work is now independent of the pipeline (one plan card at a time).
9. **`start_wall_backend.ps1` died on uvicorn's first log line (manual run).** PowerShell 5.1 treats native stderr as fatal under `Stop`; the launch now runs under `Continue`.
