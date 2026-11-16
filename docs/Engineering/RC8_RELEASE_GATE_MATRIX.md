# RC8 → RC9 release gate matrix

Candidate: `Builds/MuseDesk_rc9_feedback1.apk` (build label `rc9-feedback-1`), SHA-256 `__APK_SHA__`, from
`feature/rc8-feedback-1` at `__COMMIT__`. Rollbacks: `MuseDesk_rc8.apk` (788D4A04...E1428), then `MuseDesk_rc7.apk`, then `MuseDesk_rc6_2.apk`.

Evidence levels: **AUTO** (EditMode tests), **PLAY** (Editor Play Mode driver, synthetic room, simulated hands, no
headset), **WORN** (the owner wearing the Quest and reporting the specific interaction). Nothing below is WORN yet:
the only WORN evidence is the owner's round-1 report on rc8 (`RC8_HARDWARE_EVIDENCE.md`, W1-W10), which this build answers.

Tester for every AUTO/PLAY row: Claude (coordinator), 2026-11-16, shared Unity Editor 6000.6.8f1.
EditMode suite: **1500 / 1500 passed** (full run after the last change).

| Gate | Owner finding | Method | Level | Result | Evidence | Next action |
|---|---|---|---|---|---|---|
| Wall: one agent per role, one scale | W1 doubled agents | `WallSingleAgentDrive Setup/Place/Count` | PLAY | PASS | 4 roles x 1 root, all 0.170 m, 4 baked characters hidden, 0 desk roots visible | WORN: look at the wall for doubles |
| Manager paper: keys fit, stamp zone | W2, W7 | `PlanPaperGeometryTests`, `LivingOfficeDrive RunOfflineStamp` | AUTO + PLAY | PASS | smallest text 4.4 mm (floor 4.3 mm at 0.45 m); REJECTED / APPROVED stamps land only after confirmation | WORN: read the paper, stamp it |
| Dedicated Manager model | owner decision | `WallManagerModelV1`, Avatar probe, `LivingOfficeDrive` | PLAY | PASS | `Agent_Manager_Body` on `MuseDesk_WallAgent`, no cap, 0 scripts/colliders; full plan loop 13/13, rides 8/8, 0 console errors | WORN: Manager looks right walking and in the lift |
| Panels near at start-up (office stays) | W3 | `UnifiedDockDrive` reach steps, `SpacesDockTests` | AUTO + PLAY | PASS | every dock key 0.42-0.55 m from the seated eye; re-seat moves panels only | WORN: first launch, is the dock in reach? |
| Factory belt removed | W4 | `RemoveFactoryV1`, `NoFactoryDrive Check`, `FactoryRemovedTests` | AUTO + PLAY | PASS | 0 visible factory/belt renderers; test evidence still reaches the receipt | WORN: none needed beyond a look |
| Desk / Floor / Ceiling / Wall from one dock | W5 | `UnifiedDockDrive Run` | PLAY | PASS 17/17 | Floor place, Ceiling place/lower/raise, Wall place with Move/Select/Manager, back to Desk each time | WORN: switch through all four |
| Select-agents grab, put back | W6 | `AgentSelectGrabDrive Run`, 19 tests | AUTO + PLAY | PASS 12/12 | grab with a 6 deg dip keeps the target, perch, /btw, regrab, put back by stretching to the wall, Return key, refusal while holding a paper; never >1 away | **WORN is the real test** (hands): grab, carry, put back |
| Offer: hold until taken | W8 | `OfferHoldDrive` (probe), `OfferHoldPolicyTests` | AUTO + PLAY | PASS | 0 retracts over 20 s, frozen pose 4537 frames, paper steady (x drift 2 cm) | WORN: wait 20 s without taking the paper |
| Paper keys: More + Revise only | W9 | `PaperKeysTrimV1` | PLAY | PASS | Approve/Reject/Ask why/Voice hidden by scale; More/Revise 1.5x, labels fit | WORN: press More and Revise |
| Hands-free mic + wake word | W10, owner decision | `MicToggleDrive`, `WakeWordTests` (21), `HandsFreeMicTests` (14) | AUTO + PLAY | PASS | key flips and saves; "approve" / "I would approve that" ignored (Unaddressed); "Researcher, approve" acts; "stop" always heard | WORN: mic on, talk normally, then "Researcher, why?" (needs Wit.ai / network) |
| Large portal keep-out (flag ON) | handoff P0 | `PortalKeepOutDrive` + `WallDrive`, 24 tests | AUTO + PLAY | PASS | portal opened at a clear spot 1.34 m to the side, 44 valid checks; box on the spot -> closed "Furniture is in the way"; 1.8 m room -> no large spot (small visitor) | WORN: open the portal in the real room; FPS during the portal |
| Paper hold point centred | art handoff P0 | `PaperHoldCenterV1` | PLAY | PASS | x 0.055 -> 0, y/z kept; offer re-validated after | WORN: paper centred between the hands |
| Grounded feet, loop flags | art handoff | Antigravity `2d5be2f` (merged) | AUTO (art) | PASS | min foot Y >= -2 mm on walk/turn/carry clips; 34 Manager loops serialised | WORN: no sinking feet |
| Frame rate >= 60 fps | competition rule | `scripts\quest_perf_watch.ps1` | WORN | **NOT RUN** | - | owner wears the build with the script running (desk, wall, portal) |
| Live model agents | handoff | backend live run | AUTO (backend) | PASS | see `RC8_LIVE_AGENT_EVIDENCE.md` | WORN with the backend live |
