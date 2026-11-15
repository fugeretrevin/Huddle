# Agent animation and navigation spec

How MuseDesk characters move and what they show. It applies to the Wall Habitat (`Scripts/Spatial/Core/Wall`, `Scripts/Wall`) now. The same rules are planned for the desk-mode agents (`Scripts/Agents`, `AgentMovement`, `AgentHandoffController`).

## 1. Ownership (one writer each)

| Channel | Owner | Never written by |
|---|---|---|
| Habitat avatar root (local position, yaw, visibility) | `HabitatWalker` (pure), applied by `WallHabitatView.Apply` | Animator (root motion off), stage, controller |
| Skeleton | Animator (`MuseDesk_Agent.controller`, 39 states, state name = clip name) | scripts (Wall has no bone writers) |
| Desk agents: neck and head gaze | `AgentGaze` (LateUpdate, order 310, through `BonePoseLayer`) | other layers |
| Spine and chest | `AgentIdleLife` (300) and `AgentPresence` (420), layered | — |
| Elevator cabin height | `HabitatElevatorMachine` (pure) | the walker writes only the rider's y while riding, copied from `CabinY` |
| Visit projection and portal avatar | Wall controller (Interaction partial), one per agent globally | the original agent is never moved |

Root motion is off everywhere (no runtime script enables it; Wall forces `applyRootMotion = false`). Walk clips never carry world traversal.

## 2. Habitat navigation

- **Graph:** `HabitatLayout.nav` (Dijkstra). Nodes are Work, Aisle, Entry, Corridor and Cabin, plus the manager and delivery stops. Every edge is a straight segment that `HabitatLayoutValidator` proves clear for a capsule of the agent's radius and height, plus 5 mm clearance, against every desk, partition, railing, shaft wall and back wall.
- **Reservation:** a trip is cut into segments from one stop to the next. A segment is leased whole (every node) before the first step. While walking, the next segment is leased early if it is entirely free, so trips flow through doorways. This never extends into the cabin and never waits while holding extra. Nodes are released when left. The travel lock means one elevator trip in flight at a time.
- **Timing along a segment (the path itself never changes):**
  1. Turn toward the next point, critically damped with `turnSmoothTime` 0.16 s. While the heading error is above `walkTurnLimit` (50°) the character turns in place (`Turn_Left`/`Turn_Right`) and does not translate.
  2. Accelerate to `walkSpeed` (0.14 m/s at habitat scale) over `startSeconds` 0.30 s.
  3. Brake so the character is stopped exactly at the next stop: the segment end or any corner sharper than 50°. The allowed speed is `min(walkSpeed × alignment, sqrt(2 × decel × distanceToStop))`.
  4. Arrive: speed is 0 and the state is Dwelling (`Offer_Document`) or Home (desk clip).
- **Stride:** walk clip playback = ground speed ÷ walkSpeed, clamped to [0.35, 1.15].
- **Elevator:** request → wait off-corridor (`Idle_Breathing`) → doors open at the rider's floor (eased, 0.45 s) → walk in → doors close only after `ReportBoarded` → profiled travel (accelerate over 0.6 s, brake over 0.7 s, exact landing) → short settle (`arriveDwell` 0.15 s) → doors open → walk out → `ReportExited` → doors close. A rider that never boards or never leaves is timed out (15 s / 8 s) and soft-transferred home. `Interrupt` (recenter, pause, mode change) brakes to the nearest landing and opens the doors, so nobody is ever left between floors.
- **No route or blocked too long:** soft transfer (fade out 0.3 s, re-home, fade in 0.3 s). Nobody walks through anything. This is visual only; task state is never touched.

## 3. Presentation states (derived from authoritative state only)

| Source | Desk activity | Clip (Researcher / Developer / Tester / Manager) |
|---|---|---|
| task `running` | Working | `Reading` / `Typing` / `Inspecting` / `Writing` |
| `awaiting_approval`, input, `paused` | Waiting | `Waiting_For_Input` |
| `blocked` | Blocked | `Thinking` |
| `failed`, error | Problem | `Worried` |
| no task, `queued`, `completed`, `cancelled` | Idle | `Idle_Breathing` |
| Manager plan proposing, committing or delegating | Working | `Writing` |
| Manager plan awaiting approval | Waiting | `Waiting_For_Input` |
| reducer `task.completed` event | one-shot | `Nodding` (1.2 s) |
| walking / turning in place / waiting for the lift / riding / presenting | — | `Walk_Loop` / `Turn_*` / `Idle_Breathing` / `Idle_Breathing` / `Offer_Document` |

A `queued` task never shows working. The habitat never celebrates before a confirmed completion event.

## 4. Planned (not done)

- Desk-mode agents: the same damped heading, turn-in-place, profiled speed and stride matching in `AgentMovement`, and a timed hand-to-hand flight for the handoff token (`AgentHandoffController`). See `IMPLEMENTATION_AUDIT.md` M7 and M8.
- Gaze for habitat avatars: they are tiny at wall distance, so it is only worth it during a desk visit or portal. The visit projection and portal clone could use `Listening` while the user asks and `Talking` while an answer is shown.
- Antigravity transitions (start-walk, stop-settle, elevator step in/out, landing). They are not delivered, and clips will be wired by name when they exist.

## 5. Evidence

AUTO: `HabitatMotionTests` (22, with clearance asserted on every tick for every walker, elevator round trips, deadlock and timeout cases) and `MotionTests` (10). Editor Play Mode: the Wall walkthrough (`scripts/wall_flow.ps1`). Headset: **none**. Foot skating, turn feel and the elevator ride must be judged from worn video.
