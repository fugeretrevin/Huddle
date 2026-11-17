# Wall, elevator and portal QA

Scope: the Wall Habitat after the polish pass (`feature/living-world-polish`). All Wall flags are OFF in ReleaseConfig. Test it with `Builds/MuseDesk_wall_validation.apk` (the polish build is listed in `RELEASE_POLISH_CHANGELOG.md`).

## Automated (UNIT)

| Fixture | What it proves |
|---|---|
| `HabitatMotionTests` (22) | Every walker on every tick stays clear of every box (capsule vs AABB with 5 mm clearance), including the new velocity profile. Also covered: elevator round trips, deadlock-free corridor leases, timeouts that soft-transfer, an interrupt that lands at a floor, and no overlap between agents |
| `MotionTests` (10) | Eases are exact at their ends; the clock completes once, can be cancelled and is safe after a hitch; `ProfiledMover` lands exactly without overshooting its speed or acceleration limits; damped turns settle exactly and take the short way; presence maps only real states |
| `WallPlacementTests` (26) | Placement scoring, persistence and restore, room-change suspend |
| `AgentVisitPortalManagerTests` (19) | Visit and portal flows, one presentation at a time, cancellation and loss handling, manager print gating |
| backend `test_p3_*` (62 incl. runner) | `/btw` is read-only (store byte-compare), plan validation and atomic commit, the plan runner works only committed cards and labels its output |

## Editor Play Mode (PLAY, simulated hands, no HMD)

Run with `scripts\wall_flow.ps1` after `SceneGuard.Ensure` and `editor.playmode start`.

| Step | Expected | 2026-11-15 result |
|---|---|---|
| Place on the synthetic wall | Habitat grows out of the wall; desk office hidden; remote delivery on | PASS (capture `review_screenshots/wall/polish_habitat_close.png`) |
| Select, pinch, carry, release over the pad | Projection flies to the hand, follows with damped lag, lands exactly on the pad (d = 0.000) | PASS |
| `/btw` question | Answer labelled with provenance; task state unchanged | PASS (offline and live backend) |
| Manager plan, approve | Three cards printed only after the board confirms them; runner works them in order; habitat carries play | PASS (offline and live backend) |
| Elevator round trip | Doors open before boarding; cabin accelerates, brakes and lands exactly; doors open; rider exits | PASS (UNIT + PLAY earlier) |

## Worn checklist (NOT RUN: needs a person)

1. Wall preview on a real MRUK wall reads well. Place it, walk away, come back: it stays flush.
2. Watch a worker walk from its desk to the elevator. Check for no foot skating, a turn in place at the corner, and a smooth stop at the doors.
3. Ride: the doors open fully before the worker steps in and close behind it. The cabin starts and stops gently and lines up with the floor before the doors open. You hear a small ding and a soft door slide (quiet, not annoying).
4. Manager plan: cards print one by one with a tick. No card appears before the board shows it.
5. Pinch a worker from about 1.5 m, carry it and let go over the pad. The landing should be smooth with no jitter while carrying. Ask one question, then press Return.
6. Meet larger: the preview avoids your monitor and your back. Confirm. The emergence is gentle (a whoosh, no flash) and the avatar stands on the floor, listening, not talking. Send it back.
7. Recenter during a ride and during a portal: nothing is left between floors or floating.
8. Focus mode on: the habitat goes quiet (no ding, no tick).
9. Record frame timing with `scripts\quest_perf_watch.ps1 -Label wall -CaptureMinutes 4` during steps 2–6.

Record each step PASS or FAIL with a note in `QUEST_EVIDENCE_MATRIX.md`.
