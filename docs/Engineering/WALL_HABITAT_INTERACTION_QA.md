# Wall Habitat: interaction QA

Evidence levels: **AUTO** (EditMode tests) · **PLAY** (Editor Play Mode with `AgentScripts/WallDrive.cs`, synthetic 4 x 4 m room, simulated hands, no headset) · **APK** (built) · **LOG** (device log) · **WORN** (a person in the headset). Nothing in this file has been worn yet.

## Play Mode walkthrough (2026-11-14, `scripts/wall_flow.ps1`)

| Step | Result | Level |
|---|---|---|
| Enable flags (driver only), synthetic room, Wall key | Preview: valid Full spot on a side wall (`wall-W`); the dock shows Place here / Other spot / Compact / Cancel | PLAY |
| Place here | Mode Wall published, `IsRemote=true`; habitat on the wall with +Z into the room; 464 desk-office renderers hidden; stamps and panels stay with the user (NearUserKit); dock shows Move / Desk / Select agents / Manager | PLAY |
| Scripted demo running in the background | A real `decision.requested` from the demo drove a Researcher dispatch walk (`completed=1`) | PLAY |
| Select agents, pinch the Developer from about 2 m | Projection carried; home avatar stays at its desk with the On-visit marker | PLAY |
| Release away from the desk pad (driver moved the hand while opening) | Invalid drop: the projection flew home, nothing changed. Fixed afterwards so the projection holds still while the pinch opens (real fingers drift) | PLAY |
| Carry to the pad, open the pinch | Perched exactly on the pad (d = 0.000 m); visit card shows Status? / Blockers? / Next? / Meet larger / Return | PLAY |
| Status? | Answered, labelled `SCRIPTED DEMO`, "not an AI answer" | PLAY |
| "Stop that and switch to OAuth instead" | `amendment_suggested`; the card says the change was NOT applied | PLAY |
| Task state before and after the visit | Identical (`researcher=demo-build-login/awaiting_approval/r3`) | PLAY |
| Meet larger, then Open here | Ring at a clear floor spot beside the user (about 90° to the side, 1.3 m away); 1.20 m projection; Conversing; question answered; Send back then Hidden | PLAY |
| Manager, Login flow | Plan card, `SCRIPTED_DEMO` plan a, b, c (Researcher, Developer, Tester); nothing created yet | PLAY |
| Approve | 3 cards created through the existing v2 board path (demo gateway, canonical ids); printer delivered 1 card to each cubicle only after `task.created`; Developer/Tester cards `blocked` behind Research (dependency gating) | PLAY |
| Test-injected handoff Researcher to Manager | Walked to the queue, rode up to y 0.150 (the manager landing), exited, handed off, rode down, walked home; 0 soft transfers | PLAY (event injected by the driver; the real event path is covered by the bridge) |
| Console | No errors from project code (only Editor toolbar layout noise) | PLAY |

Found and fixed during PLAY:
1. The projection followed the hand during the release debounce. It now holds while the pinch opens.
2. A projection cloned from a desk agent was invisible, because the desk renderers are hidden in Wall mode. Projections are now cloned from the habitat avatar.
3. Desk monitors hid the characters from the user's side. They are now low laptops.

## On-head checklist (UNVERIFIED: to run on a worn Quest 3 with a validation APK)

Use `AgentScripts/WallValidationConfig.On()` to build. Keep `Off()` for submission.

| # | Check | Pass when |
|---|---|---|
| W1 | Mode console, then the Wall dock **Wall** key | Ghost appears on a real side wall, not over the monitor; its words say what was checked |
| W2 | Pinch on a wall and drag; Other spot; Compact | Ghost follows the wall; red with a reason over a door, window or furniture |
| W3 | Place here | Habitat sits flat on the wall, no gap or penetration; the desk office disappears; stamps and board stay |
| W4 | Look around and move your head | Habitat stays put (anchoring); no jitter |
| W5 | Recenter, then relaunch the app | Habitat comes back on the same wall, or the dock says why it is paused |
| W6 | Desk key | Desk office returns exactly as before; Floor and Ceiling still work |
| W7 | Run the demo (Start) | Characters walk and ride only along decks; elevator doors open before boarding; nobody walks through desks or walls; papers come to you as in Floor mode |
| W8 | Select agents, point at an agent, pinch | Ring highlights; a small projection comes to your hand; taking a paper or stamp with that hand never starts this |
| W9 | Drop on the teal pad on the desk | Snaps on; card readable; Return sends it home |
| W10 | Status?, Next? (backend live with P3 flags on) | The answer matches the real task, labelled; the task keeps running on the board |
| W11 | Meet larger, then Open here | Ring is in clear floor space beside you; the 1 m figure is comfortable; Send back closes it |
| W12 | Manager, Login flow, Approve | Plan readable; real cards appear on the board; printer cards land in the right cubicles |
| W13 | Hand lost mid-carry; pause (headset off) mid-visit | Agent goes home, nothing committed |
| W14 | Performance watcher (see PERFORMANCE) during W7 + W11 | Stable 72 Hz |
