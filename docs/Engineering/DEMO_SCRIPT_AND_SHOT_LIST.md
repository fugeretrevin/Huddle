# Demo script and shot list (offline judge walkthrough)

Date: 2026-11-15. Written from docs and code reads only. Nothing was run on a headset. Rows marked "unverified" are not confirmed in any doc.

Sources: `docs/Engineering/FINAL_DEMO_WALKTHROUGH.md` (2026-11-12 layout, Play Mode only), `docs/Engineering/RC6_DEMO_SHOT_LIST.md`, the scripted agent sequence in `Assets/MuseDesk/Scripts/Networking/DemoAgentGateway.cs:143-288`, and `docs/Polish/NEXT_DEVICE_TEST.md`.

## 0. Ground rules for the judge run

- **Standalone:** no PC, no backend, no Wi-Fi needed. The app starts in the scripted demo. Desk is always available (`MuseDeskReleaseConfig.cs:8`).
- **Labels you will see:** `SCRIPTED DEMO` (offline, template output) or `LIVE` (backend reached). The label appears on the dock, the desk plaque, the paper, the receipt, the agent speech bubble (S9c footer) and the voice HUD. The paper also prints the provenance line, for example "Template recommendation. Not live AI research." (`FINAL_DEMO_WALKTHROUGH.md` step 4).
- **Hands only.** Every P0 step is operable without controllers or voice (`docs/AGENTS.md` invariant 7). Voice is optional and needs internet.
- **Evidence:** the desk steps are the only ones with a worn record (rc6-1, Gate A, WORN, `RC6_HARDWARE_QA.md:28`). Floor and Ceiling were worn on rc6-1 with open issues. Everything marked experimental is PLAY-only or never worn.

## 1. Core walkthrough (Desk, standard flags)

Each step: what the judge does with hands, what they see, and the label. Timing about 90 s for one run.

| # | Judge does | Judge sees | Label | Evidence |
|---|---|---|---|---|
| 1 | Puts the headset on and looks at the desk | Researcher, Developer and Tester at their desks, nameplates reading Idle. A plaque on the desk | Plaque: `SCRIPTED DEMO` (`BuildOfficeV2.cs:20`) | Gate A WORN (rc6-1, A1, A9) |
| 2 | Pokes **Start** on the task dock, front left | Dock: "In progress". Researcher plate: "Reading the brief". Tester runs baseline tests | Dock chip `SCRIPTED DEMO` (`BuildTaskDockV2.cs:447`) | A1-A2 WORN rc6-1 |
| 3 | Optional: opens the board with the board key. Grabs a task card and drops it on an agent | Card glides into the lane (S3S4). Card assigned. The card text is inside the card (`HANDOFF_NEXT_SESSION.md`, text fit) | Card carries the mode label | A6 WORN rc6-1 (card assign); glide PLAY only |
| 4 | Waits about 20 s, then the Researcher walks up holding a paper marked DECISION | Paper with a "!" sign and a walk with a turn before the walk (S3S4). Dock: "Needs your decision" | Dock and paper: `SCRIPTED DEMO` | A2 WORN rc6-1 |
| 5 | Takes the paper by its edge or the TAKE tab | Paper expands (paperUnfold). Question, summary, recommendation, provenance line | Paper: mode band `SCRIPTED DEMO`; provenance "Template recommendation. Not live AI research." | A3 WORN rc6-1 |
| 6 | Optional: pokes **Why** or **Details** | Details sheet slides out under the folio (`detailsSheet` token). Reasoning, risk, impact | Same paper label | PLAY (S9a) |
| 7 | Presses the green **stamp** (or Approve) | Stamp poofs (rc-feedback-1). Paper shows "Sending...", then "Approved". Paper glides to the out-tray. One decision is sent even if pressed twice (S2) | Decision goes to the scripted gateway | A4 WORN rc6-1 (stamp); single-send PLAY only |
| 8 | Watches the handoff | Small token arcs from Researcher to Developer to Tester (S3S4 handoff flight). Chime (the factory/conveyor was removed after rc8 feedback; RemoveFactoryV1) | Token has no label; receipt does | A4 WORN rc6-1 (token and chime); arc timing PLAY only |
| 9 | Watches the Developer write code | Developer bubble: "Scaffolding login UI" then "Implementing OAuth scaffold". Speech bubble footer reads `SCRIPTED DEMO` | Bubble footer `SCRIPTED DEMO` (S9c, `NEXT_DEVICE_TEST.md` step 19) | PLAY only (S9c footer); worn: unverified |
| 10 | Watches the Tester | Tester runs tests: "Baseline tests passed (3/3)" (`DemoAgentGateway.cs:148`) | Bubble `SCRIPTED DEMO` | PLAY |
| 11 | Reads the completion artifact | Summary, test result, files changed. Live: real diff and pytest output (`RC6_HARDWARE_QA.md` D4) | Demo artifacts are summaries prefixed `SCRIPTED DEMO:` (`contracts/phase2/phase2_demo.json`) | AUTO (contract tests); live path unverified on device |
| 12 | Reads the receipt, front right | "Delivered", summary, tests passed, files changed | Receipt label `SCRIPTED DEMO` (or `LIVE`) | PLAY; receipt worn: not in rc6-1 record (unverified) |
| 13 | Optional: pokes **Filed 1** on the out-tray | Archive readout: what was decided and what came of it | Archive label | PLAY only (`UI-05` NOT_TESTED in `QUEST_EVIDENCE_MATRIX.md`) |
| 14 | Presses **Run again** on the receipt | Run restarts | | PLAY |

Variations (from `FINAL_DEMO_WALKTHROUGH.md`): Reject at step 7 ends with "Stopped. You rejected the proposal." Changes at step 7 brings a revised proposal (up to two revisions). Rejection is worn on rc6-1 (A5 row).

## 2. Spatial workspace (step 15, Floor and Ceiling)

Flags `floorMode` and `ceilingMode` are ON in the committed asset (`ReleaseConfig.asset`). They are PLAY-only on rc6-2. The owner wore rc6-1 and found Gate B and Gate C problems (`RC6_HARDWARE_QA.md:29-33`). Show them only as "validation", not as a shipped feature, unless the owner clears Gates B and C (`FINAL_FEATURE_FLAG_DECISION.md`).

| # | Judge does | Judge sees | Label | Evidence |
|---|---|---|---|---|
| 15a | Presses **Floor** | "Look at the floor" card, gaze dwell 0.6 s, then the preview | `SCRIPTED DEMO` / `LIVE` on the office plaque | Gate B: worn on rc6-1 (B1-B3 "works"); fixes PLAY |
| 15b | Points and drags the footprint, lets go on a ready spot, presses **Place here** | Office moves; outline solid teal then green. Blocked spot: coral/amber with a reason | Mode console | B5-B5c: unverified on rc6-2 |
| 15c | Presses **Ceiling** and confirms a dock spot | Scan grid under the real ceiling, mount appears | Mode console | Gate C: worn on rc6-1 with three open problems |
| 15d | Presses **Desk** to return | Office comes back | | B7 / Desk: worn on rc6-1 (A-series) |

## 3. Experimental demos (flag-gated, not in the core walkthrough)

Show these only in a validation build (`Builds/MuseDesk_polish_validation.apk`). Do not show them in the competition build: all are OFF there. Every one is PLAY-only or AUTO. None has been worn (`WALL_HABITAT_STATUS.md`, "Nothing has been worn").

| Demo | Flag(s) | What the judge does | Label rule | Evidence |
|---|---|---|---|---|
| **Wall Habitat** (treehouse on a real wall) | `wallHabitat` (needs nothing else) | Enter Wall, preview on the wall, **Place here**; the habitat grows out of the wall | Wall cards carry `LIVE` / `SCRIPTED DEMO` | SYNTH, PLAY. MRUK wall read never run on device |
| **Elevator** (worker rides between floors) | `wallHabitat` | Watch a worker ride; doors open, cabin lines up. Soft door and ding | Cue only | PLAY round trip, AUTO timing. `wallElevatorVfx` has no effect (section 4 of `FEATURE_FLAG_MATRIX.md`) |
| **Manager delegation** (plan, approve, cards print) | `wallManager` (+ backend `MUSEDESK_P3_MANAGER_ENABLED`) | Ask for a plan, approve it, cards print with a tick | Plan text: `SCRIPTED DEMO: template plan ... No model was used.` (`backend/app/phase3/planner.py:14`) or `Model-written, no tools: nothing was run or applied.` (`backend/app/phase3/runner.py:256`) | PLAY live 3-card run; pytest. No model call has been made (no key) |
| **Desk visit** (Select Agents, pinch and carry, /btw) | `agentSelect`, `agentBtw` | Far-pinch an agent, carry it to the pad, ask one /btw question, return it | `[SCRIPTED DEMO] ` prefix on the /btw reply (`backend/app/phase3/side.py:206`) | PLAY with simulated rays only. Real hands never used |
| **Portal** ("Meet Larger") | `largeAgentPortal` (needs `agentSelect`) | Confirm a floor spot; a 1 m avatar grows in with a soft whoosh and stands listening | Avatar label on the portal panel | SYNTH, PLAY. Video NOT_TESTED |
| **Calm motion key** (not a demo; a comfort control) | `reducedMotionToggle` | Pokes the key under the mode console; transitions get shorter | Key label `OFF`/`ON` | PLAY. NOT worn |

## 4. Shot list (framing notes)

Framing references: the scene facing convention (settled 2026-11-12) is that root +Z points at the user, the eye is at root-local (0, 0.30, 0.62), and user-facing things sit at +Z within 0.6 m. Frame shots from that eye point unless marked "third-person".

| Shot | What it shows | Framing | Notes |
|---|---|---|---|
| S01 | Cold start, desk with three agents, plaque reads `SCRIPTED DEMO` | Wide, eye height, desk centred, plaque legible | Shows that no PC is needed |
| S02 | Judge's hand on the Start key on the dock | Medium close, hand and key in frame | Dock label legible |
| S03 | Board open, card dragged toward an agent | Over-shoulder from the eye point | Shows the glide |
| S04 | Researcher walking up with the DECISION paper | Third-person side view, desk and agent in frame | Shows turn-then-walk |
| S05 | Paper unfolding in the judge's hand | Close, paper filling the frame | Provenance line readable |
| S06 | Stamp poof on Approve | Macro on the stamp | One stamp, one result |
| S07 | Token arc from Researcher to Developer to Tester | Wide third-person, all three agents in frame | Show hands-off |
| S08 | Speech bubble with `SCRIPTED DEMO` footer | Medium, bubble centred | Footer legible at seated distance |
| S09 | Receipt "Delivered" front right | Medium-close, receipt and label | Confirms the label |
| S10 | Archive readout after **Filed 1** | Medium | |
| S11 | Floor preview with the footprint | Wide, floor and office in frame | Mark "validation" in the caption |
| S12 | Ceiling pod descending (validation) | Wide, pod and desk in frame | Only if Gate C is cleared |
| S13 | Wall habitat growing from the wall (validation) | Wide, wall and habitat | Validation build only |
| S14 | Calm motion key label flip | Close on the key | Validation build only |

Captures of the live run must come from the owner. No capture in this repository is a worn capture.

## 5. Not verified

- The current committed scene has not been walked through in one run on a headset (rc6-2 and later).
- Receipt and archive worn status: unverified.
- Timing of the handoff token and the bubble footer on device: unverified.
- Live-mode artifacts (real diff and pytest output) on device: unverified.
