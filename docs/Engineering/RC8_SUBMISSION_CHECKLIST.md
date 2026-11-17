# Submission checklist (deadline 2026-11-18, 12:00 PT)

Source: `03_QUEST_QA_DEMO_AND_SUBMISSION.md` §F and the official rules
(https://start-developer-competition-26.devpost.com/rules).

**Owner** marks items only the owner can do: accounts, uploads, publishing, the final submit. Claude never submits or
publishes. Statuses are as of 2026-11-16.

## Build

| Item | Who | Status |
|---|---|---|
| Release APK chosen; SHA-256 recorded in `RC8_RELEASE_GATE_MATRIX.md` | Claude | current candidate in the matrix (rc10 line, or the newer build noted there) |
| Rollback APKs kept (rc9, rc8, rc7, rc6_2) | Claude | done |
| Flags final (`ReleaseConfig.asset`); `RC8_FINAL_FLAGS_AND_ROLLBACK.md` matches | Claude | done (all ON incl. largeAgentPortal; owner decision) |
| On-device frame rate at least 60 for every enabled mode (`RC8_PERFORMANCE_RESULTS.md`) | Owner wears, Claude reads the logs | Desk + Wall measured on rc9 (72); rc10 Wall art / portal NOT RUN |
| WORN smoke of the five-minute flow (`03_...` §B) on the exact submitted APK | Owner | NOT RUN on rc10 |
| No secrets in the APK or repo (`wit.config`, Wit token asset, `backend/.env` never copied) | Claude | rule kept; check again before the final build |
| Signed, versioned APK (version code above the last upload) | Claude builds, owner uploads | check the version code at upload |

## Meta / Devpost (owner)

| Item | Status |
|---|---|
| VR Start eligibility and Devpost registration accepted | Owner |
| Track **Productivity**; Division **New Experience** only if first conceived or built after 2026-09-24 | Owner |
| **Competition** release channel in the Meta VR Developer Dashboard; APK uploaded | Owner |
| Judge access / invite link works from a second account or install | Owner |
| APK free and available until winners are announced | Owner |

## Video and page

| Item | Status |
|---|---|
| Public YouTube or Vimeo video **under 3:00** (aim for 2:45-2:55), captions on | NOT STARTED (`RC8_DEMO_CAPTURE_LEDGER.md`) |
| No third-party logos or art, unlicensed music, or identifiable non-entrants | check on the final edit |
| Scripted parts labelled `[SCRIPTED DEMO]`; live-model parts labelled as live | check on the final edit |
| Description: what it does, why, how it is built, what is next, launch timing; honest live/scripted wording (`03_...` §E "Accurate capability language") | draft needed |
| Screenshots, credits (Antigravity art, sound sources), judge test instructions (Desk first; Spaces dock; Wall → Meet) | draft needed |
| Owner reviews and presses Submit; saves the confirmation screenshot and links | Owner |
| Nothing changed after the deadline; start uploading well before the last hour | Owner |

## Judge test instructions (draft)

1. Sit at a desk and open MuseDesk. The office and the dock appear at your desk. Watch the agents and poke one.
2. Press **Start** on the dock. When an agent walks over with a paper, take the paper, read it, and press the stamp to
   approve or reject it.
3. On the **Spaces** dock, try **Floor** or **Ceiling**: look where the guide card says, then press **Place**. Press
   **Desk** to come back.
4. From the desk, press **Wall**, look at a wall, then press **Place**. Press **Manager** to see a plan printed and
   stamped. Press **Meet** to bring an agent to full size beside you.
5. Optional: turn **Mic** on and speak to an agent by name ("Researcher, why?"). This needs a network connection.
