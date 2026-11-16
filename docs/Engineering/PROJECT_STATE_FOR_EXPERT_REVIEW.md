# MuseDesk: project state for expert review (2026-11-16, evening)

This brief is for an expert reviewer. It covers:
- what MuseDesk is;
- what is finished and what is in progress;
- what has been proven, and how;
- the honest limits;
- where we most want feedback.

It is strict about evidence. Most recent work is proven in the Unity Editor, by automated tests and against the real
backend. **Only the owner wearing the headset counts as worn proof.**

Competition: Meta VR Start Developer Competition 2026, Productivity track. **Hard deadline 2026-11-18, 12:00 PT**
(about 40 hours away). Building started 2026-11-10.

Technical stack:
- **Device:** Meta Quest 3.
- **Engine:** Unity 6000.6.8f1, URP 17.6, Meta XR SDK 209 (ISDK hands, MRUK room scan), IL2CPP / ARM64 / Vulkan.
- **Backend:** Python FastAPI (REST + WebSocket), with the Meta model API as the live provider.

---

## 1. What MuseDesk is

MuseDesk is a mixed-reality workplace for supervising a small team of AI agents: Researcher, Developer, Tester and a
Manager. The team lives where the user chooses:
- on the real desk, as a miniature office;
- on the floor;
- in a ceiling pod that lowers beside the user;
- in a treehouse habitat on a real wall, with an elevator.

The work loop:
1. The user gives the team an objective.
2. The Manager drafts a plan, or a Researcher brings a decision **paper**.
3. The user reads it and **stamps** it, approve or reject. The mark appears only after the backend confirms.
4. Cards move between agents (on the Wall, the agents ride the elevator to collect them).
5. The user can pull an agent onto the desk, or call it into a full-size **portal**, and ask it questions on the side
   (`/btw`). These questions are read-only.
6. The work ends in a receipt.

Interaction is hands-first. Voice is optional: it uses Wit.ai, needs the network, and a request must start with the
agent's name.

The app has two modes, and the mode is always shown on screen:
- **Offline scripted demo.** This is the judge build: standalone on the Quest, with no PC needed. Everything in it is
  labelled SCRIPTED DEMO.
- **Live.** The backend runs on a PC and the Quest connects to it over USB (`adb reverse`).

Invariants we hold:
- Never present scripted work as live.
- The backend authorises every decision; a local animation never counts as approval.
- Every flow works with hands alone.
- The judge build runs with no PC.

## 2. Evidence levels

- **AUTO** — automated tests:
  - Unity EditMode **1620 / 1620** pass.
  - Backend pytest **331 passed, 1 skipped**. The skip is a symlink test that can't run on this Windows account.
- **PLAY** — Unity Editor Play Mode drivers on the real production scene, with a synthetic room and simulated hands.
  There is no headset or passthrough at this level.
- **LIVE** — scripts that call the same HTTP and WebSocket routes as the Quest client, against a real backend using
  the Meta model:
  - `backend/scripts/live_e2e.py`: **71 / 71**;
  - `backend/scripts/live_journey.py`: **17 / 17**.
- **WORN** — the owner wore that build and reported on the specific interaction. Only the owner can make something WORN.

## 3. Builds and worn rounds

| Build | Worn | What the owner reported | Answered in |
|---|---|---|---|
| rc6_2 | yes | Desk, Floor and Ceiling basics worked; earlier grab and text-mirroring issues | rc7 |
| rc8 | yes | 10 findings:<br>• doubled Wall agents<br>• Manager paper key labels truncated, no stamp zone<br>• panels too far away at start-up<br>• remove the factory belt<br>• Floor and Ceiling not reachable from the Wall dock<br>• select-agent grab broken<br>• offering agent kept giving and taking back the paper<br>• paper keys should be More and Revise only<br>• wanted hands-free mic | rc9 |
| rc9 | yes | Wall walkers slid instead of walking; an elevator rider teleported home; the portal was unreachable; the mode panel and placement flow had gone | rc10 |
| rc11 | yes | 72 fps on Wall and the small portal. Asks:<br>• Wall placement too strict<br>• wanted the Floor-style beam with reasons<br>• the portal was "just growing"<br>• the visitor should be lively<br>• cards looked poor | rc13 |
| rc13 | yes ("much better") | Six asks:<br>• Ceiling desk rugs intersect<br>• the paper should be handed over at the pod's end dock<br>• portal visitors float, so add a platform<br>• grabbing or meeting an agent makes a second copy<br>• the visitor's head spins and its body rolls<br>• no indicator when selecting agents | **rc14** |
| **rc14** | superseded by rc15 before it was worn (rollback) | — | — |
| **rc15** (current) | **installed, hash-verified (047DD408…BB62B15), not yet worn** | rc14 + honest status wording (see §5) | — |

Device performance, measured in the owner's sessions with the in-app heartbeat and compositor stats:
- **72 fps steady** on Desk idle, board open, task running, paper held, Wall preview and Wall active, with the small
  portal open (rc9 and rc11).
- One-off hitches of 60–150 ms on scene changes, a ~1 s hitch at app start, about 385–398 MB of memory.
- **Not measured yet:**
  - the rc13+ portal art (arch, particles, light column);
  - the rc14 hover platform;
  - a 15-minute soak and thermals.

## 4. Feature inventory

| Area | What it does now | Evidence | Known gaps |
|---|---|---|---|
| Desk office | Three rigged agents using 77 animation clips, with idle life, gaze, poke / wave / duck reactions, status chips and speech bubbles | WORN + PLAY | Reaction tuning is subjective |
| Decision paper | The agent walks up, offers the paper and holds it until taken. Grabbing expands it. More / Revise keys. Physical stamps; the backend confirms before the mark lands. Replays are idempotent; a conflicting second answer gets 409 | WORN (stamp, rc6–rc8) + AUTO + PLAY + LIVE | Hold-until-taken not worn; wrist-snap grab poses only (no finger-wrap) |
| Task dock and board | Start, a board with cards (assign / pause / priority / new), receipt with the artifact, Focus mode, Calm motion | WORN (basic) + AUTO + LIVE | — |
| Spaces dock | One dock for Desk / Floor / Ceiling / Wall, plus context keys and the mic toggle | WORN (rc11) + PLAY | — |
| Floor office | Guide card, scan grid, pointing arc, pinch-drag footprint with colours and reasons; Place moves the office along a clear route | WORN (rc6) + AUTO + PLAY | Desks overhang the deck by 3–6 cm |
| Ceiling pod | Dock spot, pod lowers beside the user, Lower / Raise, pull gesture. **rc14:**<br>• deck rugs no longer overlap (the app separates them at load; the art file is unchanged)<br>• with the pod lowered, the Researcher hands the paper over at the pod's front shelf | WORN (rc6); rc14 PLAY | rc14 not worn |
| Wall habitat | Antigravity's treehouse (Full or Compact) on a scanned wall; one agent per role; routed walkers; elevator with dispatcher, claims and rider priority; Manager on the top floor | AUTO (30-minute seeded stress, 0 teleports) + PLAY; WORN rc11 (placed, walked) | Elevator no-teleport fix not yet confirmed worn |
| Wall placement | Floor-style beam per hand, ring cursor, reason chip; pinch-drag the ghost; release on a refused spot snaps to the nearest valid one; loosened rules | AUTO + PLAY | Not worn on rc13+ |
| Manager plan | Pick a goal. The Manager drafts a plan (live: written by the Meta model and validated by the backend), shown on a paper with steps, owners, dependencies, risks and provenance, and a framed "Stamp here". Revise works; stale revisions are refused. A risky step needs a separate OK. The stamp approves exactly once; cards print; owners ride the elevator; the Manager synthesises a receipt. **rc15:** results read "Written, not run" | PLAY 13/13 + LIVE 17/17 | Live runner is sequential and **text-only** (§6) |
| Select agents and `/btw` | **rc14:** pointing beam, target ring and "Pinch to pick up \<Agent\>". The carried projection dangles, lands on the desk perch and comes alive. **The original is hidden while it visits (no second copy).** Read-only questions are grounded in the task's real events and artifacts; change requests become proposals that are never applied | AUTO + PLAY 12/12 + LIVE | Hand carry not worn on rc10+ |
| Meet and large portal | Meet key or voice opens a preview at a spot checked against the user's keep-out zone (or a small-visitor fallback). The floor portal opens, a light column rises, and the agent steps out. **rc14:** it stands on a hovering platform; the original is hidden. It leaves with a turn and a wave | AUTO (keep-out 24+, entry 19) + PLAY (MeetDrive 15/15) | Not worn as a large visitor; in the owner's room it fell back to the small visitor; device FPS unmeasured |
| Visitor life | Faces the user; head and neck follow the user, a nearby hand or the touching finger; breathes; leans away from a fingertip and springs back; hops when poked; waves back; thinks while a question is pending; gestures while answering. **rc14:**<br>• Animator always updates<br>• a target behind the agent is released, not flipped to<br>• head turn capped at 240°/s<br>• lean capped at 12°<br>• bone guard against accumulated rotation | PLAY (head stays within ~13° of the body even with the Animator off) | The worn head spin was **not reproduced in the Editor**; fixes are defensive and need a worn check |
| Voice | Hands-free mic, muted by default; requests start with a name; "stop" is always heard; mode words; portal requests | AUTO + PLAY | Online only; never in the judge path |
| Backend | Sessions, idempotency, v2 board, Manager plans with validation and retry, plan runner, side questions, sandboxed workers behind the approval gate | AUTO 331 + LIVE | See §6 |

## 5. What changed today (rc14 / rc15)

- **The owner's six rc13 asks:** all fixed in rc14 (see §3 and §4). rc14 is installed, and the installed copy's hash
  matches the build.
- **Official Unity tooling audit:**
  - Our `unity` command **is** the official Unity CLI (1.0.0-beta.17), with `com.unity.pipeline` 1.0.0-pre.1 and Unity's
    official agent plugin installed.
  - All our wrappers already run on top of it.
  - Decision: keep the current setup. We will also use its read-only inspection commands
    (gameobject find, component get_fields, animator get_controller, clip get) without writing new code.
  - Nothing was installed. See `UNITY_OFFICIAL_TOOLING_CAPABILITY_AUDIT.md`.
- **End-to-end journeys:**
  - The canonical request ("Build me a login page with email validation, password requirements, and tests.") was run
    live, through plan, revise, approve-once, run and side questions.
  - One real bug was found and fixed: "Actually, change the password rule to 12 characters" was answered as a status
    question. It is still read-only, but it now produces a suggested change that waits for confirmation.
  - Docs: `END_TO_END_USER_JOURNEY_SPEC.md`, `E2E_SCENARIO_MATRIX.md` (30 scenarios), `LIVE_WORKFLOW_VALIDATION.md`,
    `QUEST_USER_ACCEPTANCE_QA.md` (the owner's headset script), `JUDGE_DEMO_VALIDATION.md`,
    `END_TO_END_GAPS_AND_FIXES.md`.
- **rc15:** text-only plan results no longer say "Completed". The status page reads "N of M written up (text only,
  nothing was run)", and each card reads "Written, not run · Proposed change: …". This is a display change only; the
  backend state is unchanged.

## 6. Honest limits we are choosing to ship with

- **The live Manager runner writes text; it executes nothing.** For the login request it produced:
  - a research write-up;
  - three "Proposed change" descriptions (including the revised 12-character rule);
  - four test *plans*.

  No code was written and no tests were run on this path. This is now labelled at the paper footer, the status head
  line and each card.
- **The executed path is the desk auth task.** After approval, the Developer writes real files into a per-session
  sandbox and the Tester runs real pytest (6 passed / 0 failed). The artifact has real counts and a diff. However:
  - the code is a **fixed OAuth scaffold**, not written from the user's request;
  - the research proposal before it is a labelled template.
- Voice is online-only (Wit.ai); the hands path covers everything.
- Grab poses are wrist-snap only.
- Portal and desk visitors are visual projections of the agent; its task is unchanged. Their replies come from the same
  read-only side conversation (scripted offline, model-backed live, labelled).
- Wall placement refuses doors, windows, furniture, steep view angles and spots that would hide the screen. Without a
  room scan it offers an amber, unchecked spot.

## 7. Not verified yet (the risk list)

1. **Everything new in rc13–rc15 is unworn:**
   - Wall beam and snap;
   - portal sequence, platform and visitor life (especially the head-spin fix);
   - Select ray and the hidden original;
   - Ceiling rug and shelf hand-over;
   - card style.
2. Device FPS for the portal art, column and platform; a 15-minute soak; thermals.
3. The large portal in a real seated room. The owner's room produced only the small visitor.
4. The full live chain driven *from the headset* (Level 5); so far it has only been proven over the API.
5. App restart and network drop on the device.
6. Demo video (not shot) and submission (dashboard, Devpost): owner tasks.

## 8. Where we want expert feedback

1. **The last ~40 hours:** freeze after the rc14/rc15 worn check and spend the rest on video and submission, or one more
   interaction pass? Which feature should be switched off, rather than fixed, if it misbehaves when worn? Candidates:
   large portal, Select carry, Ceiling.
2. **Honesty framing:** the strongest *executed* proof is the desk auth task (real files and tests, fixed scaffold). The
   strongest *intelligent* proof is the live Manager plan (a real model plan, revise and approve, text-only results).
   How should the video present these two without over-claiming? Is "Written, not run" clear?
3. **Should the Manager runner execute anything before the deadline?** For example, re-use the sandboxed Developer and
   Tester workers for one bounded "validation module + tests" template keyed on the objective. Or does that add risk
   for little judge value?
4. **Portal:** is a pop-in inside a light column, then stepping off a hovering platform, convincing in passthrough? Or
   should the agent grow from its miniature so it reads as the same character?
5. **Visitor liveliness:** which reactions matter most in a 3-minute demo? Any comfort concerns about a 1.2 m agent
   beside a seated user?
6. **Demo narrative (2:50):** Desk delegation → paper → stamp → handoff → receipt → Wall reveal with the Manager plan →
   Meet portal. What would you cut?
7. **Is the scenario matrix the right acceptance bar**, or is something judges will try missing from it?

## 9. Pointers

- Journeys and evidence: the six E2E docs listed in §5; owner headset script `QUEST_USER_ACCEPTANCE_QA.md`.
- Tooling: `UNITY_OFFICIAL_TOOLING_CAPABILITY_AUDIT.md`.
- Release gates, flags, rollback: `RC8_RELEASE_GATE_MATRIX.md`, `RC8_FINAL_FLAGS_AND_ROLLBACK.md`.
- Worn findings: `RC8_HARDWARE_EVIDENCE.md`.
- Live backend evidence: `RC8_LIVE_AGENT_EVIDENCE.md`, `LIVE_WORKFLOW_VALIDATION.md`.
- Performance: `RC8_PERFORMANCE_RESULTS.md`.
- Code:
  - Wall runtime: `Assets/MuseDesk/Scripts/Wall/`;
  - pure logic: `Assets/MuseDesk/Scripts/Spatial/Core/`;
  - portal: `Wall/WallHabitatController.PortalFx.cs`, `Wall/PortalVisitorLife.cs`;
  - aim and Select ray: `Wall/WallHabitatController.Aim.cs`;
  - Ceiling: `Spatial/CeilingPodArt.cs`, `Spatial/CeilingShelfHandOver.cs`;
  - backend: `backend/app/` (`phase3/` is the Manager, runner and `/btw`).
- Builders and Play Mode drivers: `AgentScripts/`.
