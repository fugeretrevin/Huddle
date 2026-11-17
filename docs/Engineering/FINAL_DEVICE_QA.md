# Final device QA

2026-11-12. Build: `Builds/MuseDesk.apk` (86 MB), installed on the Quest 3 at 20:40.

## Status: not validated on the headset

The redesigned build has not been seen or used on the device. An unattended launch was tried once (`scripts/quest_run.ps1`, 60 s): the app process never started and the screenshot is black, because the headset lock screen (`com.oculus.os.vrlockscreen`) holds the display. The lock was not bypassed. The on-head fixes from the previous round (key depth, paper in the hand, card spacing, board labels) are also still unconfirmed.

## What was verified without the headset

| Check | Result |
|---|---|
| Unity compile | Clean |
| EditMode tests | 33 passed, 0 failed |
| `RuntimeSmokeTests` | 108 checks, 0 failed |
| `SceneHealth` | 0 missing scripts |
| Backend pytest | Passes (exit 0); backend unchanged |
| APK build | Succeeded, 0 errors |
| `XrVisualQa` | 0 FAIL, 53 WARN (was 13 FAIL before the redesign). Reports in `docs/Engineering/qa_runs/` |
| Play Mode loop, demo mode | Start, paper offered, approve, delivery receipt, archive poke, board open and close, Run again, reject, Stopped receipt, Run again. No errors from project code |

The Play Mode loop pressed things by calling the methods the keys are wired to. It does not prove that a finger can press them. Live e2e against the desktop server (71 checks) was not re-run, since no backend or gateway code changed.

## What `XrVisualQa` still warns about

- Board keys 0.60 to 0.64 m from the eye when the board is open (New, Pause, Priority).
- Side nameplate activity text is 7.7 mm at 0.64 m, slightly under the angular-size target.
- Keys and props that are deactivated are listed as "would not register if re-shown". They are never shown.
- Decor is beyond reach and has no distance grab; nothing in the flow needs it.

## On-head checklist, in order

Run `scripts/quest_run.ps1 -Label check` after unlocking, or just put the headset on.

1. **Idle view.** Three agents, plaque reading SCRIPTED DEMO, dock at front left, stamps, Focus key at front right. Nothing floating over the agents. The guide line appears once and goes away.
2. **Nameplates.** On the desk fronts, readable from the seat, not covering the agents or monitors.
3. **Dock.** Text sits on the plate, not in front of it. Start and Board press with visible travel and the cap stays in front of its housing.
4. **Start.** Dock reads "In progress"; Start disappears; nameplates change.
5. **Carried paper.** In the Researcher's hand, reading DECISION, with the TAKE tab not hidden by the head or arm.
6. **Take the paper.** It unfolds; cover goes; question, summary, provenance line, two large keys, four chips, stamp area on the right.
7. **Keys on the paper.** The two wide keys are stretched 2.1 x 0.8: check press travel, that the cap returns, and that the thicker side rim looks acceptable. Check the chips are large enough to hit.
8. **Details and Why.** A second sheet opens below; nothing else moves; it does not sink into the desk when the paper is held low.
9. **Approve by key, then on another run by stamp.** "Sending...", then the outcome on the sheet, then the paper glides to the out-tray.
10. **Receipt.** Appears at front right with the delivered summary. Run again presses and starts a new run.
11. **Reject.** Receipt reads Stopped; Run again works.
12. **Archive.** "Filed N" plate on the out-tray is reachable; poking it shows the readout for 8 s.
13. **Board.** Board key opens it at the left; cards do not overlap; text is on the cards. Grab a card: the pads appear; **drop it on a pad or an agent and confirm the assignment is sent** (first real test of the fixed drop). Select a card: the toolbar appears with only valid actions.
14. **Board closed.** Nothing can be poked or grabbed where the board was.
15. **Sound.** Cues are audible but quiet, and distinct for approve and reject.
16. **Earlier fixes.** Pressed key faces stay visible; board column labels are centred and flush.

Things only the headset can show: text depth against its surface, whether the hidden-by-scale keys rest at the right depth once shown, reach, glare and legibility of 5.5 mm captions, and hand occlusion.
