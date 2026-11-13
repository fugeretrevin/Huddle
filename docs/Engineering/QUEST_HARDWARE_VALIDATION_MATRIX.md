# Quest hardware validation matrix

2026-11-13. One row per thing that must be true on the headset. Nothing in the spatial modes or the final refinement pass has been worn yet, so almost every row ends in "Not tested". This file is the place to record results; fill in the last two columns as you go.

## Evidence levels used in every table

| Code | Meaning |
|---|---|
| **AUTO** | An automated check passes (EditMode tests, smoke checks, XR visual QA script) |
| **PLAY** | Ran in Unity Editor Play Mode, driven by calling the methods the keys and grabs are wired to; no hands, no passthrough, synthetic room |
| **APK** | Built into an APK that installed on the Quest 3 |
| **LOG** | Seen in device logs from a real run |
| **WORN** | Physically verified by a person wearing the headset |
| **NT** | Not tested |
| **FAIL** | Known failure |

A row is only as strong as its highest code. **PLAY is not evidence that a hand can do it.**

## How to run a gate

1. On the PC: `powershell -ExecutionPolicy Bypass -File scripts\quest_perf_watch.ps1 -Label gateA` (it waits for the app, then records performance for 8 minutes; it never launches the app or touches the lock screen).
2. Put the headset on, open MuseDesk, do the steps of one gate.
3. Write the result in the table: `WORN ok`, or `FAIL` with what you saw. For a failure also note the mode and the step, so the same physical test can be repeated after the fix.

Record with each session: build commit (see `FINAL_SPATIAL_RELEASE_AUDIT.md`), APK file, date.

## Gate A: Desk release baseline

| # | Requirement | Evidence now | On-head result | Notes |
|---|---|---|---|---|
| A1 | App launches | APK (an earlier build was launched and worn; this build not) | | |
| A2 | Passthrough works | WORN on an earlier build | | Unchanged code path |
| A3 | Hands track | WORN on an earlier build | | |
| A4 | Office visible in front, agents upright, nameplates readable | PLAY | | Nameplates now have polished plates |
| A5 | Start on the dock starts the run | PLAY | | Key hover/press colours are new |
| A6 | Researcher brings the DECISION cover | PLAY | | |
| A7 | Grab the paper; it unfolds | PLAY (via `TakeNow`) | | New layout; real grab untested |
| A8 | Approve by key | PLAY | | Wide keys: press both ends |
| A9 | Approve by stamp | NT | | New target, provisional mark, stamp return |
| A10 | Reject by key | PLAY | | No failure animation expected |
| A11 | Details slides out and back | PLAY | | |
| A12 | Completion sequence, one chime | PLAY | | |
| A13 | Receipt appears; View opens the viewer above the office | PLAY | | Viewer keys within 0.52 m |
| A14 | Viewer Next / Close | PLAY | | Keys appear by scale |
| A15 | Filed plate raises the archive card | PLAY | | |
| A16 | Board opens in reach; select a card; toolbar | PLAY | | |
| A17 | Hand-drop a card on an agent or pad sends the assignment | NT | | Never tested by hand in any build |
| A18 | Run again restarts | PLAY | | |
| A19 | Characters: no foot slide, no head over-rotation, end on their spots | NT | | Code reviewed only |
| A20 | Mode keys (Floor, Ceiling) sit beside Focus without crowding | PLAY (capture) | | New in this build |

## Gate B: Floor mode

| # | Requirement | Evidence now | On-head result | Notes |
|---|---|---|---|---|
| B1 | Real floor detected from the room scan | NT | | Only synthetic rooms so far |
| B2 | Without a room scan: words say so and offer hand placement | AUTO | | |
| B3 | Ghost preview appears on a sensible spot to the side | PLAY (synthetic) | | Real furniture untested |
| B4 | Next cycles candidates; Confirm places | PLAY | | |
| B5 | Office appears where the ghost was, on its platform | PLAY | | Platform base sits 3 cm below the floor plane |
| B6 | Agents visibly working from the seat | NT | | Text is not meant to be readable at 1 to 2 m |
| B7 | Desk is clear while the office is away (dock, plaque, Focus, mode key, summon keys) | PLAY | | Stamps hidden until needed |
| B8 | Beacon at the office shows "Needs you" for a decision | PLAY | | |
| B9 | Courier carries the paper from the Researcher to in front of you | PLAY | | Path and grab untested by hand |
| B10 | Paper can be grabbed from the hover point; unfolds | PLAY (via `TakeNow`) | | |
| B11 | Stamps appear at the desk when the paper arrives | PLAY | | |
| B12 | Approve / reject works as at the desk | PLAY | | |
| B13 | Receipt at the desk; View works | PLAY | | |
| B14 | Back to Desk restores the office exactly; run state intact | PLAY | | |
| B15 | Relaunch restores the saved floor spot if still valid | PLAY (once) | | |
| B16 | No candidate available: words, and Desk still works | AUTO | | |
| B17 | Stop during a move reverses it to where it started | AUTO + PLAY | | The Cancel key reads Stop while the office moves |

## Gate C: Ceiling mode

| # | Requirement | Evidence now | On-head result | Notes |
|---|---|---|---|---|
| C1 | Ceiling detected from the room scan | NT | | |
| C2 | No ceiling in the scan: words, mode refused | AUTO | | |
| C3 | Preview shows where it will come down | PLAY (synthetic) | | |
| C4 | Dock is to the side, never overhead | AUTO + PLAY | | |
| C5 | Descent path clear of detected furniture, you and walls | AUTO (swept test, 19 tests) | | Scan may miss lamps, monitors, fans: watch the first descent |
| C6 | Smooth descent, mast extends, mount stays on the ceiling | PLAY | | |
| C7 | Stops beside you (0.85 m to the side), not in front of your face | PLAY | | Is it reachable and comfortable? |
| C8 | Shelf opens when lowered | PLAY | | |
| C9 | Interact with the lowered office | NT | | |
| C10 | Send up while holding the paper: it waits | PLAY | | |
| C11 | Retracts and docks | PLAY | | |
| C12 | Paper delivered by courier while docked | PLAY | | |
| C13 | Recenter: office stays put or asks for confirmation, never jumps | AUTO | | Not run in Play Mode: needs real tracking |
| C14 | Stop during descent returns it to the dock | AUTO + PLAY | | |

## Gate D: Live backend

| # | Requirement | Evidence now | On-head result | Notes |
|---|---|---|---|---|
| D1 | Plaque reads LIVE when the desktop server is reachable | AUTO (code) | | "Connecting..." only during a real attempt |
| D2 | Start launches the real pipeline from the board card | LOG (live e2e, 71 checks, before the redesign) | | Not re-run since; no backend or gateway code changed |
| D3 | Real decision paper; approve resumes real work | LOG (as above) | | |
| D4 | Real tests run in the sandbox; artifact delivered | LOG (as above) | | |
| D5 | Paper states "Template recommendation. Not live AI research." when it is a template | AUTO | | |
| D6 | Disconnect and reconnect mid-run | NT | | |
| D7 | Live mode with the office on the floor | NT | | |

## Gate E: Performance

No numbers exist. See `FINAL_SPATIAL_PERFORMANCE.md` for the capture plan and the table to fill in.

## Defect log

For each headset defect: build commit, APK, mode, step number, what happened, evidence (log, screenshot, your words), fix commit, and the result of repeating the same step.

| Date | Build | Mode / step | What happened | Evidence | Fix | Re-test |
|---|---|---|---|---|---|---|
| | | | | | | |
