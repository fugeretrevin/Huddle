# rc22 owner check (on the headset): your rc21 notes

**Build:** `Builds/MuseDesk_rc22_release.apk`, label `rc22-release`, release-signed (CN=MuseDesk), versionCode 6,
SHA-256 `04A218BC9F36AFC2D1A3F0FCA3F2CCEA5E4DFCC86B7A11DC0C0A53E5AC84A301` (label found in the APK).
Installed on the Quest (2026-11-17, `adb install -r`, settings kept); on-device sha256 matches; versionCode=6. Rollback: rc21 `3D9DA134`.
**Rule:** only your words make anything WORN.

| # | Your note (rc21, live) | What rc22 does | Check |
|---|---|---|---|
| 1 | Manager on the Ceiling and Floor | The Manager key shows on the dock in Floor and Ceiling at rest; "Manager, ..." works there; cards print into the habitat only on the Wall | Floor: press Manager, ask for a plan |
| 2 | Developer must work tasks I add | A card you add and assign to developer/tester/researcher is worked for real (restricted workspace, real pytest) — live check 5/5: "reverse a string" done in 15 s, 8 tests | Add a task, assign Developer |
| 3 | Mic less picky | Forgiving names ("manage her", "develop her", "hey muse"...), filler ignored ("um", "can you", "please"), an unnamed sentence on the mic key goes to the Manager; one log line per sentence | Talk naturally |
| 4 | Board: name and count separate; scroll arrows; remove the LIVE line | Heading = name only; count in its own box ("1–4 of 13"); Up/Down arrows per column; no LIVE line on cards (SCRIPTED DEMO stays offline) | Add many tasks |
| 5 | "Why?" on an acceptance paper should be voiced | The agent says the why-answer | Ask why |
| 6 | Screen panel: stay upright; resize; "Hide screen" | Upright (pitch/roll 0) while moved; corner handle resizes (0.35–1.6 m); the key reads "Hide / screen" | Move and resize |
| 7 | Stamps grabbable while invisible | Hidden stamps have their grab/poke switched off until they poof back | Pinch where a stamp was |
| 8 | Backlog: highest priority first | The runner takes the highest priority ready card first (plan and your cards together) | Two tasks, different priority |
| 9 | Approval broken after unticking | Crossed-out steps: the stamp frame says "Revise first" (coral), the key "Revise now", stamping is refused and the Manager says why | Untick a step, stamp |
| 10 | "Developer, come down here" | A "come ..." request opens the portal by itself when the spot is room-checked; "meet the developer" still previews | Say it |
| 11 | Manager answers questions | A question is answered (spoken + on its paper); only a goal makes a plan | "Manager, what is the developer doing?" |
| 12 | "Can't do that, but here's what I can do" | Not-understood sentences get a spoken, specific fallback with 2–3 things that work right now | Say nonsense to an agent |
| 13 | Remove tasks, clear the board | Remove key on the selected card ("Sure?"), Clear key on the board ("Clear all?") | Remove one, clear |
| 14 | Wall placement less strict | Touching furniture or covering part of a window is allowed (amber, scored lower); 3 cm edge margin | Place near furniture |
| 15 | Remove the Manager floor's right wall | Its end wall is cut out (632 triangles) so you see in from that side | Look from your desk |
| 16 | Select mode off when doing something else | Any other key, grab, paper, Meet or screen turns Select off ("Select off") | Arm Select, press Manager |
| 17 | Close the "Stopped / rejected" panel | The run receipt has a Close key; the Manager's rejected/stamped paper has Close | Reject, close |
| 18 | Grab agents cleaner; drop where placed | The carry holds through short pinch blips (0.2 s release, 1 s lost-hand grace); let go over the desk and the agent stands THERE | Carry and drop beside the pad |
| 19 | Researcher's button clips the shirt | The button sits on the shirt (fixed at import) | Look at the Researcher |

## Automated evidence (PLAY/AUTO, not WORN)
- EditMode 1839/1839. Backend 407 passed, 1 skipped. Live: user task worked for real 5/5.
- Play probes: Select off on Manager press (cause 'Manager'); drop 15 cm left of the pad lands there ("Spot", perch follows);
  Manager in Floor (key shown, paper opens, voice plans); Manager question answered vs goal planned; crossed-out step shows
  ✗ and "Revise now", approve refused and spoken; board "Backlog" + counter "1–4 of 13", Down arrow, 9 scrolled out; screen
  upright after drag, resize to 1.1 m; "Developer, come down here" -> PortalRequest; Manager floor right wall cut (632 tris),
  the view from the seated eye into the Manager floor.
- Play regression, each group alone: Meet (step 10 updated: "Tester, come here" now opens by itself), Select-grab, Unified
  dock, Living Office stamp + failure, Floor/Ceiling, portal, Apply change, desk walk, desk paper: all PASS, 0 real errors.
- The Editor's Play clock stalled twice (known Editor fault; restarting Play fixed it the second time).

## Results
(Your words only.)

## rc23 / rc24 (2026-11-17 night)

- **rc23** `28DCF3ED` (versionCode 7), installed and hash-verified: board arrows at the column foot, small talk, idle arm clearance, one-sided desk monitors, clutter removed.
- **rc24** `6358B73D11B5CB34066F1417B2A8663C0B397ED48CFF93E45F864880809D03A0` (versionCode 8, CN=MuseDesk, label found): rc23 + the Wall habitat's three monitor screens one-sided (318 triangles to a dark back; captured from both sides). **Installed** on the Quest (2026-11-17, APK hash re-checked, device reports versionCode 8, `adb reverse tcp:8765` set, live backend health 200). Not worn yet.
- Backend (no APK needed): change requests such as "can you also let the user delete a task by its number?" now offer Apply change (side_intent verbs; 12 new tests; backend 419 passed). Live: `live_real_work` 13/13 (to-do module + delete change, 30 tests), `live_adhoc_task` 5/5.
- Play regression on rc24 code: all groups PASS, 0 real errors (ledgers `Logs/preflight/ecfd1b0_20261117_21*.md`).

## rc25 (2026-11-17 late): open the agents' app in a window, see it and tap it in the headset

Owner: "add a window for that launcher, with a way to close it ... so we can test 'make an app that does X' and then we can see it in MuseDesk ... don't hard code that".

- **Backend**:
  - `app/phase3/app_launcher.py` + `app_window_host.py` open the newest program the agents built: the session's work, else the newest on this PC.
  - A program with `build_ui(ui)` draws its own window from a small toolkit (no tkinter import in generated code). Otherwise a run panel shows `main(argv)` output.
  - The same restricted-workspace static checks run before opening. Credential-free env. One window at a time.
  - The window closes with its Close app key, Esc, the headset, or the backend stopping.
  - Routes `/v2/sessions/{sid}/apps` (+ `/open`, `/close`, `/tap`, `/type`), on with `MUSEDESK_APP_WINDOWS` (the start script sets it with the screen panel).
  - The screen frame crops to the app window (`X-App: 1`).
  - Developer and planner prompts tell the agents to build apps with `build_ui(ui)`.
  - Tests: `tests/test_app_windows_rc25.py`. The full backend suite passes.
- **Unity**:
  - The Screen panel gets an Open app / Close app key at its lower left, plus a caption.
  - A fingertip pressed onto the picture taps the app (`ScreenTapLogic`, 5 EditMode tests). EditMode: 1883 passed, 0 failed.
- **PLAY** (Editor, live backend):
  - The panel showed the beach-ball window streamed from the PC.
  - A tap on Run reran it.
  - A tap on the window's own Close app closed it; the panel returned to the whole screen and the key to Open app.
  - Note: one edit to `WallHabitatController.App.cs` was made while Play Mode was running. Play was then stopped, the project recompiled, and the checks were redone.
- **rc25** `77B7B50AB6411A6975EFC12A55E44B9C58CDCCA16DD28047508659747ED8D63C` (versionCode 9, CN=MuseDesk). **Installed** on the Quest, `adb reverse` set. Not worn yet.
