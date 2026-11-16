# E2E scenario matrix

2026-11-16. Evidence: **AUTO** (EditMode 1619/1619; backend pytest 331 passed / 1 skipped), **PLAY** (Unity Editor
Play Mode driver, synthetic room, simulated hands), **LIVE** (real backend + Meta model over HTTP/WS:
`backend/scripts/live_e2e.py` 71/71, `backend/scripts/live_journey.py` 17/17), **WORN** (owner on the Quest — only the
owner can grant it). Nothing below is marked passed because code exists.

Status values: PASS / FAIL / BLOCKED / NOT RUN. "Today" = this session; older evidence is named by build.

| # | Scenario | Status | Evidence | User input | Expected | Actual | Backend proof | UI proof | Remaining issue |
|---|---|---|---|---|---|---|---|---|---|
| 1 | Manager objective creation | PASS | LIVE today; PLAY earlier (LivingOfficeDrive rc13) | "Build me a login page with email validation, password requirements, and tests." (journey script; on device: goal key) | plan proposed, nothing created | 201 in 18 s, `LIVE_MODEL`, 8 steps (R/D/T), status `proposed`, no tasks | live_journey B1 | plan paper rows (PLAY) | Free-text objective on device needs voice; hands path offers preset goals |
| 2 | Plan generation (schema-validated) | PASS | LIVE + AUTO | — | validated plan with owners, deps, risks | assumptions + risks + per-step acceptance criteria and danger flags | B1 evidence JSON | — | — |
| 3 | Plan approval (exactly once) | PASS | LIVE today; PLAY today (LivingOfficeDrive RunOfflineStamp 13/13 after the Editor clock stall cleared) | stamp Approve | one approval, cards after confirmation | commit 200, 8 tasks; replay creates nothing; stale revision 409; dangerous step without OK 409 | B4 | rc13 PLAY passed; today's PLAY rerun BLOCKED by an Editor clock stall (unscaledDeltaTime = 0; see gaps doc) | Re-run LivingOfficeDrive RunOfflineStamp after an Editor restart |
| 4 | Plan revision | PASS | LIVE + PLAY rc13 | "add a 12-character password rule" (revise) | new revision, old cannot be approved | rev 1→2, 12-char criterion present, old rev 409 | B5 | revise key → new paper (PLAY) | Revise on device sends the paper's edit, not free text |
| 5 | Task delegation | PASS | PLAY rc13 + LIVE | approve | cards print, owners collect | 8 cards created and run | C3 | LivingOfficeDrive steps 9+ (rc13) | — |
| 6 | Board synchronization | PASS | LIVE + AUTO | — | board = backend states | v2 seq dense, revisions +1 per event, snapshot lists artifacts | live_e2e [card] | — | — |
| 7 | Researcher question | PASS (desk/live) | LIVE (side routes) + AUTO | "Why this approach?" | grounded rationale | covered by side routing tests; desk research is a labelled template | test_p3_side_routing | — | Researcher rationale for the desk task is the template's |
| 8 | Developer question | PASS | LIVE today | "What are you working on right now?" | real task state | named its real task, state completed, the 12-char proposal | D2 | — | — |
| 9 | Tester question | PASS | LIVE today | "Have all the tests passed?" | no fabricated pass | "I don't have information confirming the tests passed… no results claimed" | D3 | — | Manager-plan tests are plans, not runs (by design) |
| 10 | Manager question | PASS (status page) | PLAY rc13 | open Manager → Status | real board summary | status page from the board | — | plan paper status page | Spoken "what's the team's progress" routes to the Manager only with voice |
| 11 | Unsupported question | PASS | LIVE today | "What is the weather tomorrow?" | honest limit | "I do not have that information in the context provided." | D5 | — | — |
| 12 | Read-only /btw | PASS | LIVE + AUTO + PLAY | any question | no mutation | plan/tasks byte-identical before/after | D (before==after) | visit card (PLAY) | — |
| 13 | Proposed task change | PASS (after rc14 fix) | LIVE today + AUTO | "Actually, change the password rule to 12 characters." | proposal, needs confirmation, not applied | first run: answered as status (safe, no proposal) → fixed → `amendment_suggested`, `applied=false` | D6 | — | Confirming an amendment from the visit card is not a UI flow (proposal is shown, change goes through Revise) |
| 14 | Agent distance grab | PASS (PLAY) / NOT WORN | PLAY today (AgentSelectGrabDrive 12/12, PortalFxDrive SelectAim) | Select → point → pinch | target clear, carry | rc14 beam + "Pinch to pick up Developer"; carry; original hidden | — | capture select_ray.png | WORN needed |
| 15 | Agent desk visit | PASS (PLAY) | PLAY today | release over perch | lands, faces user | perched, lively, poke reacts | — | Perch drive | WORN needed |
| 16 | Agent return | PASS (PLAY) | PLAY today | Return / throw back | one leave, one return, no copies | registry 1/1; avatar reappears | — | MeetDrive 8–9 | — |
| 17 | Decision approval (desk) | PASS | LIVE + PLAY + WORN (rc6–rc8 stamp) | stamp Approve | one approval, work resumes | approve applied, replay no-op, conflict 409, real build | live_e2e [approve] | OfferHoldDrive PASS | hold-until-taken not WORN |
| 18 | Decision rejection | PASS | LIVE + PLAY | stamp Reject | nothing built, cancelled branch | "Rejected: no OAuth files written", no artifact | live_e2e [reject] | — | — |
| 19 | Actual task execution | PASS (desk auth task) | LIVE | approve | real files, real run | 2 files written, artifact retrievable, labelled live | live_e2e | receipt (PLAY) | Code is a fixed scaffold, not request-specific (gaps G-2) |
| 20 | Test pass | PASS | LIVE | — | real counts | 6 passed / 0 failed | live_e2e artifact | — | — |
| 21 | Test failure | PASS (AUTO/PLAY) | AUTO + PLAY (RunOfflineWithFailure) | — | failure shown, bounded retry | backend retest loop tests; offline failure branch drive | pytest | LivingOfficeDrive failure variant (rc13) | — |
| 22 | Artifact inspection | PASS (desk) | LIVE + PLAY | Receipt | files + tests visible | artifact 200 with files_changed, counts, diff excerpt | live_e2e | receipt card | Manager-plan "artifacts" are text results |
| 23 | Wall elevator | PASS (AUTO/PLAY) | AUTO 30-min stress (0 teleports) + PLAY; WORN rc11 (walked) | — | no teleports/clipping | — | — | — | rc10+ teleport fix not confirmed WORN |
| 24 | Portal | PASS (PLAY) | PLAY today (MeetDrive 15/15, PortalFxDrive) | Meet → agent → Open here | safe spot, emerge, platform, return | stages + platform + hidden original captured | — | portal_platform_*.png | WORN + device FPS needed |
| 25 | Workspace switching | PASS (PLAY) | PLAY (UnifiedDockDrive 0 failed today) | Spaces dock | session kept | — | — | — | — |
| 26 | Backend disconnect / resync | PASS | LIVE (resume after_seq no duplicates) + PLAY (ResyncCheck rc13) | — | no lost/duplicated state | dense seq, no dupes on resume | live_e2e | LivingOfficeDrive ResyncCheck | Device-level network drop NOT RUN |
| 27 | App restart | NOT RUN on device | AUTO (snapshot projection) | — | state restored from snapshot | — | — | — | Run on Quest |
| 28 | Offline fallback | PASS (PLAY), WORN earlier builds | PLAY + WORN (rc6–rc11 offline judge build) | no PC | full desk demo offline | — | — | — | — |
| 29 | Ceiling hand-over at the shelf | PASS (PLAY) | PLAY today (PodProbe Shelf) | pod lowered with a pending paper | Researcher walks to the front shelf | at (0.088, 0.468), offering | — | ceil_pod2.png | WORN needed |
| 30 | 15-minute idle soak | NOT RUN | — | — | stable | — | — | — | Owner session with quest_perf_watch |
