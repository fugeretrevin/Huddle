# Final submission checklist (deadline 2026-11-18, 12:00 PT)

This supersedes `RC8_SUBMISSION_CHECKLIST.md`, which is kept for history.
- **Rules:** https://start-developer-competition-26.devpost.com/rules.
- **Owner only:** accounts, uploads, publishing, the final Submit, and the signing key. Claude never submits, publishes or creates the key.

## Build

| Item | Who | Status (2026-11-16 night) |
|---|---|---|
| Candidate: rc17 `BD0204F4…` (commit `7220f0a`), installed and hash-verified | Claude | DONE |
| Rollbacks kept (rc13–rc16 APKs + hashes) | Claude | DONE |
| Flags final (`ReleaseConfig.asset`: all ON; reduced motion OFF with its toggle) | Claude | DONE |
| **Release signing.** rc17 is signed with the Android DEBUG key (`CN=Android Debug`), versionCode 1. A Meta release channel needs a release-signed APK. | **Owner** creates the key once: `powershell -ExecutionPolicy Bypass -File scripts\make_release_keystore.ps1` (own window; back up the key). Claude then builds the FINAL APK with `BuildApkTo.RunSigned("Builds/MuseDesk_rc18_release.apk", <versionCode ≥ 2>)` | **OPEN: P0 for upload** |
| versionCode higher than any earlier upload (dashboard shows it) | Owner checks, Claude builds | OPEN |
| Release build, not debuggable; VR launcher category; Quest supported-devices tags | Claude | DONE (checked on the rc17 APK) |
| No secrets in the APK or repo (Wit token files, `backend/.env`, keystores ignored; history scanned before the push) | Claude | DONE |
| Worn acceptance of the EXACT submitted APK (`RC17_FINAL_HARDWARE_QA.md`) | Owner | PENDING |
| On-device frame rate for every mode + 15-min soak + restart | Owner wears; Claude reads `Logs\quest\perf_*` | rc15 PASS (72.2 fps); rc17 PENDING |

**Note on the release build:** it is the same code as rc17. Only the signing and versionCode change. Re-check the frame rate
and the B1/B2 smoke on it before uploading.

## Meta / Devpost (owner)

| Item | Status |
|---|---|
| VR Start eligibility and Devpost registration accepted | Owner |
| Track **Productivity**; Division **New Experience** only if first built after 2026-09-24 | Owner |
| **Competition** release channel in the Meta Developer Dashboard; release-signed APK uploaded | Owner |
| Judge access / invite link tested from a second account | Owner |
| APK free and available until winners are announced | Owner |

## Video and page

| Item | Status |
|---|---|
| Video under 3:00, captions, public on YouTube or Vimeo | **CUT DONE 2026-11-18:** v4 is 1:52.3, 1080p30 H.264, captions burned in, QA passed. It lives outside the repo in `C:\dev\MuseDesk\MuseDesk_Demo_Film` (`Renders/MuseDesk_Competition_Cut_v4.mp4`; start from `START_HERE.md` there). The footage is not committed (6 GB, private room). **Upload to YouTube/Vimeo: Owner.** |
| Music original or licensed; no third-party logos; no identifiable non-entrants | DONE: the score and SFX are synthesised in the film pipeline (`make_audio.py`) |
| LIVE vs SCRIPTED provenance | **Owner decision 2026-11-18: no on-screen LIVE/SCRIPTED chips.** Provenance goes in the Devpost description (paragraph in `MuseDesk_Demo_Film/START_HERE.md` §5.4). Two things keep it clear on screen: the scripted decision example plays after the live run is finished, and the template paper's own footer ("Template recommendation. Not live AI research.") stays visible. "4 minutes later" is honest (approve 4:32 → done 8:45). |
| Description (below) reviewed by the owner | DRAFT |
| Screenshots: desk, plan paper with the LIVE footer, Wall habitat, portal visitor, receipt with real counts | TO CAPTURE |
| Credits: Antigravity art, sound sources, Meta Model API, Wit.ai | DRAFT |
| Owner presses Submit; saves the confirmation and links | Owner |

## Capability wording (use exactly; it is true today)

> When connected to its local backend, MuseDesk's AI agents plan with the Meta model, write real code and run real tests in a
> restricted workspace on the developer's PC. You approve every plan and decision with a physical stamp. The standalone
> Quest build includes a clearly labelled scripted demonstration of the same spatial interactions, so it runs without a PC.

**Do not say:**
- "fully sandboxed" or "isolated" (the code runner is a static filter plus isolated Python, not an OS sandbox);
- "researches the web" (research is model reasoning only);
- "autonomous";
- that the judge build is live.

## Judge test instructions (standalone APK)

1. Sit at a desk and open MuseDesk. The office and its dock appear at your desk. Look at the three agents and poke one.
2. Press **Start**. When the Researcher brings a paper, take it, read it (**More** for details), and press the **Approve** or
   **Reject** stamp. Watch the Developer and Tester take over; open the receipt.
3. **Spaces → Wall:** look at a wall, adjust, **Place**. Press **Manager**, pick the goal, and stamp the plan. Watch the cards
   print and the agents ride the elevator.
4. **Select agents:** point at an agent until the beam turns teal, pinch, and carry it to the perch beside you. Ask
   **Status?**, then press **Return**.
5. **Meet:** pick an agent and a spot, **Open here**, then lean toward the visitor and poke it gently. **Send back**.
6. **Spaces → Floor or Ceiling:** look where the card says, **Place**. **Desk** brings the office back.
7. Optional: turn the mic on and start with a name: "Researcher, why?". This needs Wi-Fi.

Everything in the standalone build is labelled **SCRIPTED DEMO**.
