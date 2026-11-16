# MuseDesk: project state for expert review (2026-11-16)

This brief is for an expert reviewer. It says what MuseDesk is, what is finished, what is in progress, what has been
proven and how, and where we most want feedback. It is written to be honest about evidence: most of the recent work is
proven in the Unity Editor and by automated tests, and **only some has been worn on the headset**.

Competition: Meta VR Start Developer Competition 2026, Productivity track. **Hard deadline 2026-11-18, 12:00 PT.**
Building started 2026-11-10. Device: Meta Quest 3. Engine: Unity 6000.6.8f1, URP 17.6, Meta XR SDK 209 (ISDK hands,
MRUK room scan), IL2CPP/ARM64/Vulkan. Backend: Python FastAPI (REST + WebSocket), optional live model provider (Meta).

---

## 1. What MuseDesk is

A mixed-reality workplace for supervising AI agents. A miniature team (Researcher, Developer, Tester, and a Manager)
lives on the user's real desk, or on the floor, a ceiling pod, or a treehouse "habitat" on a real wall. The agents do
work; when a step needs a human decision, an agent walks up holding a **decision paper**. The user takes the paper,
reads it, and **stamps** it (approve / reject). Work then visibly moves between agents and ends in a receipt.
Hands-first throughout; voice is optional.

Two modes, always labelled on screen:
- **Offline scripted demo** (the judge build runs standalone on Quest; every scripted artifact says SCRIPTED DEMO).
- **Live** (a PC backend; the Manager drafts real plans with a Meta model, the backend validates them, a runner works
  the cards; sandboxed tool execution exists behind the approval gate). Never shown as live when it is scripted.

Invariants we hold: no fake "live" claims; the backend authorises decisions (a local animation is not approval); hands
work without controllers or voice; the judge build works with no PC.

## 2. Evidence levels used below

- **AUTO**: EditMode unit tests (pure logic, synthetic rooms). Currently **1617 / 1617 pass**. Backend pytest 327 pass, 1 skip.
- **PLAY**: Unity Editor Play Mode drivers (scripted steps, synthetic room, simulated hands; no headset, no passthrough).
- **WORN**: the owner wore that build on the Quest and reported on the specific interaction. Only the owner can make
  something WORN.

## 3. Build history and owner worn rounds

| Build | Date | What the owner reported when worn (summary) | Answered in |
|---|---|---|---|
| rc6_2 | 11-13 | Desk/Floor/Ceiling basics worked; earlier grab and text-mirroring issues fixed in rc-feedback rounds | rc7 |
| rc8 | 11-15 night | 10 findings W1-W10: Wall agents doubled; Manager paper key labels truncated and no stamp zone; panels far at start; remove factory belt; Floor/Ceiling unreachable from the Wall dock; select-agent grab broken; offering agent looped give/take-back; paper keys should be More + Revise only; hands-free mic wanted | rc9 |
| rc9 | 11-16 | Wall legs walk then slide; an agent rode the elevator and teleported home; portal never used / unreachable; the mode panel and placement flow were gone | rc10 |
| rc10 / rc11 | 11-16 | rc11 worn: Wall placement too strict (only far spots, missed good ones); wanted the Floor-style beam and reasons; portal "just an agent growing from nothing"; the portal agent should be lively (look at you and your hand, react, be talkable); buttons/cards after opening the Manager paper or Meet look poor | **rc12 (this build)** |

Device performance (in-app heartbeat + compositor, owner sessions): **72 fps steady** on Desk idle, board open, task
running, paper held, Wall preview, Wall active and with the (small) portal open (rc9 and rc11 sessions). One-off frame
hitches of 60-150 ms on scenario changes (board open, paper grab) and a ~1 s load hitch at app start. Memory ~385-398 MB.
Not yet measured: rc12's new portal art and column, Floor/Ceiling on rc10+, a 15-minute soak, thermals.
Details: `docs/Engineering/RC8_PERFORMANCE_RESULTS.md`.

Current candidate: **`Builds/MuseDesk_rc12_portal_polish.apk`** (label `rc12-portal-polish`, branch
`feature/rc8-feedback-1`, commit `61f0b8d`; SHA-256 in `RC8_RELEASE_GATE_MATRIX.md`). Rollbacks kept: rc11, rc10, rc9,
rc8, rc7, rc6_2.

## 4. Feature inventory

| Area | What it does now | State | Evidence | Known gaps / risk |
|---|---|---|---|---|
| Desk office | Three cute rigged agents (77 Antigravity clips) at workstations on the real desk; idle life, gaze, poke/wave/duck reactions, squishy push, status chips, speech bubbles | Done | WORN (earlier rounds) + PLAY | Reaction tuning is subjective |
| Decision paper | Agent walks up, offers the paper and holds until taken (no take-back loop); grab expands it; More / Revise keys; physical stamps approve/reject; backend confirms before the stamp mark lands | Done | AUTO + PLAY; WORN for grab/stamp in rc6-rc8; hold-until-taken fix NOT WORN | Finger-wrap grab poses not authored (wrist snap only) |
| Task dock + board | Start, board with cards (assign/pause/priority/new), receipt with artifact, Focus mode, Calm motion | Done | WORN (basic) + AUTO | Board card poke/grab coexistence only lightly worn |
| Spaces dock | One dock: Desk / Floor / Ceiling / Wall + context keys (Place, Next spot, Cancel, Stop, Lower/Raise, Bring, Status, Move, Select, Manager, Meet), mic toggle, Calm motion | Done | PLAY 17/17; WORN in rc11 (used) | Wall only from Desk (fail-closed); status says why |
| Floor office | Guided look card, scan grid, pointing arc to the floor, pinch-drag footprint with colours and reasons, Place moves the office along a swept clear route; Stop reverses | Done | WORN (rc6) + AUTO + PLAY | Desks overhang the floor deck by 3-6 cm |
| Ceiling pod | Ceiling dock spot, pod descends beside you, Lower/Raise, pull gesture | Done | WORN (rc6) + PLAY | - |
| Wall habitat | Antigravity treehouse (Full 3-tier / Compact 2-tier) on a scanned wall; one agent per role; walkers with routed corridors; elevator with dispatcher, claims, rider priority, hold-in-cabin; Manager on top floor | Done | AUTO (30-min seeded stress, 0 teleports) + PLAY; WORN rc11 (placed, agents walked) | Elevator "no teleport" fix not yet confirmed worn |
| Wall placement | **rc12**: Floor-style beam per hand, ring cursor, chip "Pinch to move it here" / "Can't: covers a door"; pinch-drag the ghost; letting go on a refused spot snaps to the nearest spot that fits; rules loosened (closer, higher above the desk/screen, finer search) | New in rc12 | AUTO (5 new tests) + PLAY | **NOT WORN**; rules still conservative by design (doors, windows, furniture, edge-on view) |
| Manager plan paper | Pick a goal, the Manager drafts a plan (live model or scripted), paper with rows + framed "Stamp here" zone, stamp approves, cards print, owners ride the elevator, work, report back; receipt | Done | PLAY (13 steps, stamp variant); LIVE-backend plan PASS (Meta model, 13 s); WORN partially (rc8 paper seen) | Live Manager runner is sequential and text-only (honestly labelled) |
| Select agents + /btw | Point at a habitat agent and pinch to carry it to a desk perch; ask preset questions (read-only side conversation); put back | Done | AUTO 19 + PLAY 12/12 | **Hand carry is the real test: not yet worn on rc10+** |
| Meet + large portal | **rc12**: Meet key / voice ("Tester, come here") opens a portal preview with keep-out-checked spot; Antigravity's floor portal opens into an arch, a light column rises, the agent pops in and steps out (Portal_Emerge_Step), leaves with a turn-and-wave into the column; small-visitor fallback when no large spot; wider spot search | New in rc12 | AUTO (keep-out 24+, portal entry 19) + PLAY (stages captured) | **NOT WORN**; rc11 worn session got only the small-visitor fallback (no large spot in that room) |
| Portal visitor life | **rc12**: faces you; head/neck look at you, a nearby hand, or the finger that touched it; breathing; leans and slides away from a fingertip (kinematic, springs back); startled hop on a jab; waves back; thinks while a question is out; gestures while answering | New in rc12 | PLAY (simulated touch: poke reaction, look-at-finger, push) | **NOT WORN**; wave detection assumes the ISDK fingertip order |
| Wall cards | **rc12**: visit / Meet / receipt cards in the task dock's dark unlit style with rim and teal accent; Manager plan as cream paper; keys measured from the real key and laid out evenly (they used to overlap into one bar) | New in rc12 | PLAY (captures) | NOT WORN; key caps are the console's grey keys |
| Voice | Hands-free mic (muted by default, dock toggle); wake word: sentences must start with an agent name or "MuseDesk"; "stop" always heard; mode words; portal requests; decisions still need the paper opened + an undo window | Done | AUTO (wake word 21, voice 39+) + PLAY | Needs Wit.ai + network: never in the offline judge path |
| Onboarding hints | One hint at a time, event-driven (first paper, stamp, receipt, Spaces, Wall, Manager, Meet, Reset layout, hands-lost) | Done | AUTO + PLAY | Some recovery hints deliberately unwired (copy did not fit) |
| Panels | Movable panels (pinch the bar), reset layout, panels re-seat near the user at start-up in any mode; office never moves on its own | Done | AUTO + PLAY; WORN rc11 (dock in reach) | - |
| Backend | REST + WS, sessions, idempotency, v2 board, Manager plans with validation/retry, plan runner, side questions, sandboxed workers behind the approval gate | Done | AUTO 327; LIVE-backend runs | Symlink traversal test skipped on this Windows account |

## 5. In progress / not verified

- **Everything new in rc12 is unworn:** Wall beam and snap, loosened rules, the portal sequence and visitor life, and the
  new card style.
- **Device FPS for the rc12 portal art** (arch, particles, transparent column) and a 15-minute soak with thermals.
- **The large portal in a real seated room:** the planner needs a clear spot 50-120 degrees to the side, 1.0-1.85 m away,
  clear of screens, the desk front, furniture and walls. The owner's room produced only the small visitor. We widened
  the search but kept the safety margins.
- **Select-agent hand carry, Wall elevator trips, paper hold-until-taken:** fixes are PLAY/AUTO only.
- **Demo video:** not shot (`docs/Engineering/RC8_DEMO_CAPTURE_LEDGER.md` has the shot list).
- **Submission** (Meta dashboard Competition channel, Devpost page, video upload): owner tasks, not started.
  Checklist: `docs/Engineering/RC8_SUBMISSION_CHECKLIST.md`.

## 6. Known limitations we are choosing to ship with

- Voice is online-only (Wit.ai); the hands path covers everything.
- The live Manager runner is sequential and text-only; labelled as such.
- Grab poses are wrist-snap (no finger-wrap poses authored).
- The portal visitor is a visual projection of the agent; its replies come from the same read-only side conversation
  (scripted offline, model-backed live), labelled.
- Wall placement refuses doors, windows, furniture, steep view angles and spots that would hide the screen. Without a
  room scan it offers an amber, unchecked spot and says so.

## 7. Where we want expert feedback

1. **Scope for the last ~40 hours:** freeze now and spend the time on worn verification, the video and submission, or
   push one more interaction pass? Which one feature, if it misbehaves when worn, should be switched off rather than fixed?
2. **Portal:** is pop-in inside a light column plus a step-out clip convincing enough in passthrough, or should the
   agent grow from the miniature (desk-size) into full size so it reads as the same character?
3. **Visitor liveliness:** which reactions matter most for a judge in a 3-minute demo (gaze-follow of the hand, poke
   reaction, wave-back, talking gestures)? Any comfort concerns with a 1.2 m agent beside a seated user?
4. **Wall placement rules:** we allow it above the monitor when the bottom edge clears eye level. Is that acceptable
   ergonomically, or should straight-ahead stay forbidden?
5. **UI consistency:** Wall cards now use the dark task-dock style and the plan uses cream paper. Should the visit card
   also be paper (a "note" from the agent) for consistency with the desk decision paper?
6. **Demo narrative** (2:50): Desk delegation → paper → stamp → handoff → receipt → Wall reveal with the Manager plan →
   Meet portal. Too much? What would you cut?
7. **Honesty labelling:** is "SCRIPTED DEMO" plus "a projection; the real agent keeps working" clear enough on the
   portal card, or is it noise?

## 8. Pointers

- Release gate matrix (per build, evidence, hashes): `docs/Engineering/RC8_RELEASE_GATE_MATRIX.md`
- Flags and rollback: `docs/Engineering/RC8_FINAL_FLAGS_AND_ROLLBACK.md`
- Owner worn findings: `docs/Engineering/RC8_HARDWARE_EVIDENCE.md`
- Live backend evidence: `docs/Engineering/RC8_LIVE_AGENT_EVIDENCE.md`
- Performance: `docs/Engineering/RC8_PERFORMANCE_RESULTS.md`
- Code: `Assets/MuseDesk/Scripts/` (Wall: `Wall/` runtime, `Spatial/Core/Wall/` pure logic; portal: `Wall/WallHabitatController.PortalFx.cs`,
  `Wall/PortalVisitorLife.cs`; Wall aim: `Wall/WallHabitatController.Aim.cs`; cards: `Wall/WallUi.cs`); scene builders and
  Play Mode drivers in `AgentScripts/`; backend in `backend/`.
