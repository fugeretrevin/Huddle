# Final video shot ledger (2:45–2:50)

The film's order and wording. `DEMO_STORYBOARD.md` keeps the honesty rules and the recording setup. Where the two differ, the
shot order and timings here win.

**Sources:**
- **Q:** real Quest first-person capture of the running APK.
- **E:** in-engine Editor view of the same build's scene (`AgentScripts/CineCam.cs`; never in the APK).
- **P:** PC screen capture.
- **X:** external camera.

**Evidence of each feature today:**
- **WORN:** the owner wore it on a named build.
- **LIVE:** real backend and model.
- **PLAY:** Editor only.

| # | Time | Shot | Source | Must show (real) | Label rule | Feature evidence | Status | Fallback |
|---|---|---|---|---|---|---|---|---|
| 1 | 0:00–0:12 | **The problem**: many AI chat/terminal/task windows; the camera pulls back to the real desk | X + P (dramatized) | Generic windows, no product logos | Dramatization: it is not MuseDesk UI | n/a | TO FILM | Static desk + windows |
| 2 | 0:12–0:25 | **Meet MuseDesk**: passthrough desk; the Researcher looks up and waves; the user waves back; reveal the other two | Q (+ E close-up) | A real wave / poke reaction; nameplates turning as the head moves (rc16) | Plaque reads SCRIPTED DEMO or LIVE: keep it legible | Desk life WORN rc6–rc15; nameplates PLAY (rc16) | NEEDS rc17 WORN (B1) | rc15 desk footage (then no nameplate turn) |
| 3 | 0:25–0:45 | **Delegate**: Wall → Manager → the login goal; the plan paper fills (steps, owners, risks) | Q | Paper footer "LIVE model · code + tests really run in a sandbox" | LIVE footer readable ≥ 1 s | Plan LIVE (journey 17/17), app PLAY (RunLive 13/13) | NEEDS E1 live session | Offline plan (SCRIPTED DEMO footer) |
| 4 | 0:45–1:02 | **Approve and organize**: stamp lands; APPROVED after confirmation; cards print; owners ride the elevator | Q + E (printer close-up) | The stamp press by a real hand; cards only after the commit | Same paper | Plan stamp PLAY 13/13; desk stamp WORN | NEEDS B3/E | Offline plan stamp |
| 5 | 1:02–1:21 | **The living office**: printer → elevator ride → doors → agent collects card → Developer works → Tester receives | E (tracking shots), real run state | Only states the run really reached (rides, carries) | Caption "LIVE run" or "SCRIPTED DEMO" | Rides PLAY (0 teleports, 30-min stress); WORN rc11 | READY for E capture | Any live or offline run |
| 6 | 1:21–1:40 | **Speak to a coworker**: Select → beam → pinch the Developer → perch → "What are you working on?" → a grounded reply | Q | A real pinch; the answer names its real card | Reply label (LIVE model / scripted) | Beam WORN rc15; carry/perch PLAY; reply LIVE | NEEDS B4 (+ E for live reply) | Preset key "Status?" instead of voice |
| 7 | 1:40–1:56 | **Human decision**: the Researcher brings the desk paper; expand; More; stamp Approve | Q (macro on the stamp) | Paper held until taken; one stamp | "Model-written recommendation …" when live; SCRIPTED DEMO offline | WORN rc6–rc8 (desk loop); model paper LIVE | NEEDS B2 / E6 | Offline desk paper |
| 8 | 1:56–2:19 | **Real result**: "Minutes later" → status rows with real pytest counts → receipt → PC cutaway: sandbox folder, code file, `pytest` "N passed" | Q + P | Real files and counts from THIS run (`FINAL_LIVE_DEMO_EVIDENCE.md`) | "Minutes later" card; "real files, real pytest, local sandbox" caption | LIVE 9/9 + independent re-run | NEEDS E live session | Recorded evidence run from `Logs/live_real_work_evidence.json` (captioned as a recorded live run) |
| 9 | 2:19–2:39 | **Spatial reveal**: wide Wall Habitat on the real wall; Manager upstairs; one elevator moment; then the portal visitor (or the Ceiling pod) | Q + E | The real wall placement; the portal emergence onto the disc | n/a | Wall WORN rc11–rc15; portal WORN rc15; Ceiling WORN rc15 | NEEDS rc17 recheck (B3/B5/B6) | Ceiling shelf hand-over |
| 10 | 2:39–2:50 | **Close**: the visitor returns home; quiet desk; title "MuseDesk / Your AI team, brought to life." | Q + title | The real Send back / Return | n/a | Portal return WORN rc15 | NEEDS B5 | Desk wide |

## Capture order (fastest first; one headset session)

1. **Offline session** (backend off): shots 2, 5 (E), 6 (preset question), 7, 9, 10. All of these sit inside RC17_FINAL_HARDWARE_QA B1–B6, so film while testing.
2. **Live session** (backend on): shots 3, 4, 6 (live reply), 8. Record the whole run without stopping. The edit compresses it with "Minutes later".
3. **PC cutaway** (shot 8): after the live run, film the plan folder and pytest. Commands are in `FINAL_CAPTURE_INSTRUCTIONS.md`.
4. **External** (shots 1 and 9): someone else films you wearing the Quest at the desk and at the wall.

## Rules

- Never cut a broken interaction so that it looks finished. Retake it.
- Never use footage from an earlier run in a new shot without captioning it as a recorded run.
- Never show seven minutes of work as instant.
- No third-party logos or unlicensed music.
