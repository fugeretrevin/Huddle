# Quest evidence matrix (polish pass)

Evidence levels: **UNIT** (EditMode / pytest), **SYNTH** (synthetic room fixture), **PLAY** (Editor Play Mode with simulated hands and no HMD), **APK** (built), **LOG** (Quest device log), **WORN** (a person wearing the headset). Never collapse these into one "pass".

Result codes: PASS / FAIL / BLOCKED / NOT_TESTED. A row closes only at the evidence level the matrix requires.

**Nobody has worn the headset during this pass. Every row that needs WORN, LOG or device metrics is NOT_TESTED.**

| ID | Required | Best evidence now | Result at that level | Notes |
|---|---|---|---|---|
| CORE-01 cold start offline | worn + log | APK (rc6-2 untouched; branch APKs built) | NOT_TESTED worn | |
| CORE-02 approval lifecycle | worn | UNIT (backend), code | NOT_TESTED worn | S2 hardens double submit |
| CORE-03 card → task | live e2e + worn | PLAY (live backend, Wall plan cards) | PASS (PLAY) | card drag not exercised |
| CORE-04 provenance | visual + tests | UNIT (backend labels), PLAY | PASS (PLAY) | gaps: agent bubble, receipts (audit C) |
| UI-01 text fidelity | worn | none | NOT_TESTED | |
| UI-02 key feedback | worn | code | NOT_TESTED | Wall key disabled state planned |
| UI-03 card movement | worn ×10 | code (S2 tracking-loss cancel) | NOT_TESTED | |
| UI-04 paper + stamp | worn ×10 | code (S2 single submit) | NOT_TESTED | |
| UI-05 viewer / receipt / archive | worn | none | NOT_TESTED | |
| MOT-01 start/stop/turn | headset video | UNIT (profile, damped turn), Wall | NOT_TESTED video | desk agents planned |
| MOT-02 workstation pose | multi-angle visual | none | NOT_TESTED | |
| MOT-03 gaze / reactions | worn | code (Wall nod on confirmed completion) | NOT_TESTED | |
| MOT-04 handoff | worn + log | PLAY (Wall carries) | PASS (PLAY, Wall) | desk token flight planned |
| WALL-01 detection | worn, 2 layouts | SYNTH, PLAY | PASS (SYNTH/PLAY) | MRUK wall read never run on device |
| WALL-02 invalid placement | synth + worn | SYNTH (30+ tests) | PASS (SYNTH) | |
| WALL-03 multi-story path | overlay + worn | UNIT (every-tick clearance), PLAY | PASS (UNIT/PLAY) | |
| WALL-04 elevator boarding | worn full trip | UNIT, PLAY round trip | PASS (UNIT/PLAY) | |
| WALL-05 elevator failure | auto + play | UNIT (timeouts, interrupt) | PASS (UNIT) | |
| SIDE-01 agent selection | worn | PLAY (simulated rays) | PASS (PLAY) | real hands never used |
| SIDE-02 /btw isolation | integration | UNIT (pytest store byte-compare), PLAY | PASS (UNIT/PLAY) | |
| SIDE-03 visit return | worn + log | PLAY | PASS (PLAY) | |
| PORT-01 portal placement | worn, 2 layouts | SYNTH, PLAY | PASS (SYNTH/PLAY) | |
| PORT-02 scaled avatar | worn video | PLAY | NOT_TESTED video | |
| PORT-03 portal cancel | auto + worn | UNIT, PLAY | PASS (UNIT/PLAY) | |
| MGR-01 plan proposals | backend integration | UNIT (pytest), PLAY live | PASS (UNIT/PLAY) | no model call yet (no key) |
| MGR-02 delegation | live e2e | PLAY live backend (3 cards, runner) | PASS (PLAY) | |
| XR-01 gesture ownership | worn ×10 | code (S2 precedence) | NOT_TESTED | |
| XR-02 tracking loss | worn + logs | code (S2 card cancel), PLAY (visit) | NOT_TESTED worn | |
| XR-03 recenter / room change | worn | UNIT, PLAY | NOT_TESTED worn | |
| XR-04 four-mode regression | play + worn | PLAY (Desk/Floor/Ceiling/Wall enter/exit, previous session) | PASS (PLAY) | |
| PERF-01 FPS per mode | Quest metrics | none (structure merged 49 → 7 renderers, PLAY count) | NOT_TESTED | |
| PERF-02 stress soak | 10–15 min worn | none | NOT_TESTED | |
| PERF-03 low-effect tier | worn | none | NOT_TESTED | |
| RELEASE-01 flags rollback | build + startup | APK (flags-off build 0 errors) | PASS (APK) | startup not run on device |

Updated as the pass proceeds. Final values are in the report in `RELEASE_POLISH_CHANGELOG.md`.
