# UX redesign changelog

2026-11-12, commit `0bf7643` and the commit after it, branch `antigravity/visual-polish`.
Rollback: git tag `pre-ux-redesign` (commit `25ac342`) and `Builds/MuseDesk_pre_ux_redesign.apk`.

**Evidence level.** Everything below compiles, was built into an APK and installed. The full loop was exercised in Editor Play Mode by invoking the same methods the keys call. **None of it has been seen or touched on the headset**: the unattended launch is still blocked by the headset lock screen, and stereo depth, hand poke and grab cannot be checked from the Editor. The list of what to look at first is in `docs/Engineering/FINAL_DEVICE_QA.md`.

Screenshots: `docs/review_screenshots/ux_redesign/` (`before_*`, `after_*`).

## What the user sees now

At rest: three agents at their desks, a small mode plaque, two stamps, a compact task dock at front left and one Focus key at front right. Poke keys visible at idle went from 10 to 3 (dock Start, dock Board, Focus). Floating black status panels, floating name tags, the large mode sign, the guide sign and 20 decor props are gone from view.

| Area | Before | After |
|---|---|---|
| Task board | 62 cm board always in view with three permanent keys | Starts collapsed. A 16 x 9 cm **task dock** shows the count, the most relevant task, its state in words and the mode. **Board** opens and closes the full board |
| Starting work | Loose Start key | **Start** on the dock, shown only when a ready task exists and nothing is running. It sends the same assign request as dropping the card on the Researcher |
| Card actions | Permanent Pause and Priority keys | A toolbar that appears when a card is selected and shows only the valid actions; hides after 8 s |
| Assignment pads | Always shown | Shown only while a card is held, with the outcome or refusal in words |
| Cards | Title, id and revision, role, P-number, state, mode | Title, one state line, role word, mode words. Priority only when not Normal, as a word |
| Decision paper | Five equal keys, stamp area between them | Two large keys (Approve green, Reject coral), four small chips (Why, Details, Changes, Voice), a separate labelled stamp area, mode band, 13 mm question |
| Carried paper | Blank sheet | Cover reading DECISION with the mode band, a teal TAKE tab and two under-sheets for thickness |
| Paper states | Keys vanish while sending | Sending shows inert plates and "Sending..."; the outcome is printed on the sheet only after the confirming event |
| Agent status | Black panel floating over each agent | Desk nameplate: role and current activity in words, with the role colour |
| Mode | Large floating sign | Desk plaque: SCRIPTED DEMO or LIVE, plus the connection word |
| Onboarding | Sign plus a Tips key | One line above the plaque, one sentence per step, each shown once, gone after 10 s |
| After a run | Loose Replay key | **Receipt** card: Delivered, Run failed or Stopped (after a rejection), with what the event reported and **Run again** in the demo |
| Archive | Loose Filed key, readout always shown | Poke the "Filed N" plate on the out-tray; the readout shows for 8 s |
| Type | LiberationSans, sizes from 2 mm | Inter everywhere, nothing under 5.5 mm |
| Sound | Partial | Eight synthesised cues: key press, paper taken, attention, approve, reject, changes, delivered, error |

## Bugs found and fixed on the way

- **Dropping a card on an agent never sent the assignment.** `TaskDragController.OnReleased` cleared the drop target before reading it, since the first commit. Fixed; this path has therefore never been tested on the headset.
- After a rejection there was no way to run the demo again once the Replay key was removed. The receipt now appears for a rejected run.
- A voice approval while the paper was still carried, followed by a grab, re-showed the keys on a resolved paper. Fixed in the paper's state handling.
- `scripts/quest_run.ps1` left the headset's proximity override on after a run. It now restores it.

## Kept as it was

Backend, contracts, the event model, both gateways and the reducers are untouched. Every earlier feature is still reachable: stamps, distance grab on paper, cards and stamps, agent touch summary, focus mode, artifact viewer, revise loop, archive.

## Not done

- **On-head validation and performance measurement** (lock screen; see the two Engineering documents).
- **Rounded corners and a dashed stamp outline.** Plates are plain quads; the stamp area has a solid outline.
- **Bold weight** is TextMeshPro's synthetic bold on Inter Regular, not a true bold cut.
- **Key hover tint.** The keys' colour component writes `_Color`, which the URP Simple Lit shader ignores, so keys have no hover or press tint. Not changed because it could not be checked on the headset.
- **Board keys are a stretch.** With the board open, New, Pause and Priority are 0.60 to 0.64 m from the eye.
- **"Connecting..." on the plaque is timed**, not read from the connection attempt, so it can linger a few seconds after a refused connection. The mode words themselves are exact.
- No Blender work was needed, so there is no art handoff.
