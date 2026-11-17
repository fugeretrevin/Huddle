# Final visual refinement

2026-11-13. Branch `release/final-refinement`, commits `a04d5d8`, `d318c0f`. Restore point: git tag `pre-final-refinement` (commit `578292f`) and `Builds/MuseDesk_pre_final_refinement.apk`.

Screenshots: `docs/review_screenshots/final_refinement/rc_01` to `rc_13` (Editor Play Mode, camera at the seated eye point). The grey background is the Scene View with skybox off; in the headset the background is passthrough.

**Evidence level.** Built, installed, and run end to end in Editor Play Mode. Not yet seen on the headset.

## What the review asked about, and what was found

| Reported in the screenshots | Finding | Action |
|---|---|---|
| Faint paper text | Text colour was already solid ink. Cause was Scene View fog plus thin Inter Regular with synthetic bold | Base face is now Inter Medium; bold is the real Inter Bold. Screenshots taken with fog off |
| Stamp covering the paper | Preview artifact: the preview had switched on a child that is off at run time | None needed |
| Large panel half out of view | Real. The artifact viewer was 50 x 60 cm at the user's far right and opened by itself on delivery | Rebuilt, below |
| Details sheet looks detached | Real | Now slides out from under the paper |
| Stretched Approve / Reject keys | Real: a square key scaled 2.1 x 0.8 | Purpose-built wide keys |
| No hover or press colour on keys | Real: the colour component wrote a property the shader ignores | New key state component |

## Decision paper

- **Header:** role colour chip, "From the Researcher" (from the event's agent), "Needs your decision" or "Revised proposal N", and the mode as a chip at the right.
- **Content:** question at 13 mm bold; summary at 9 mm; recommendation on a tinted plate with a green rule (coral when the recommendation is to reject) and its reason beneath, taken from the event; provenance line always visible at 6 mm.
- **Stamp target:** outlined box at the right of the text, 8.0 x 7.8 cm. It tints teal while a held stamp is within 10 cm. Contact prints a pale APPROVE or REJECT; it becomes the dark APPROVED or REJECTED only when the outcome is confirmed, and disappears if the request fails.
- **Stamps** ease back to the rest pad on release.
- **Keys:** Approve and Reject 12.2 x 3.1 cm caps; four chips 5.0 x 2.2 cm. With Voice hidden the three remaining chips re-space evenly.
- **Details:** a second sheet slides 13.8 cm from under the bottom edge in 0.3 s, overlapping by 1 cm, with Why, Risk, If you wait and Options when the event has them.
- **Outcome:** a full-width plate with the result in words after confirmation.

## Receipt, artifact viewer, archive

- **Receipt:** a 14 x 10.5 cm card standing on a small base at the user's front right. Heading (Delivered, Run failed, Stopped), the event's summary, tests and files, mode band. Keys: **View** (only when an artifact was delivered) and **Run again** (demo only).
- **Artifact viewer:** never opens by itself. View opens a 28 x 22 cm paper sheet centred in front of the user, 0.36 m from the eye. Title, mode band, provenance line, body at 7.5 mm, "Page 1 of 3, part 1 of 3, Document 1 of 2", and Previous, Next, Close keys within reach. Long pages are split into parts instead of being cut off. It closes on reset and when a run starts. While open it covers the Developer's face.
- **Archive:** poking the Filed plate raises a 14 x 10 cm paper card from the out-tray with the decision, outcome, result and provenance as separate lines. It closes after 8 s or on the poke after the oldest record. The floating dark readout is gone.

## Task board

- Opens at an inspect pose: 0.78 scale (55 cm wide, was 71 cm), yawed to the eye at the user's front left, every key and pad within 0.55 m. It grows out of the task dock and returns to it.
- Cards have a physical edge plate. States: idle, hover (teal edge), selected (thicker teal, lifted 4 mm, 1.04 scale), pending (amber), refused (coral, 1.2 s).
- Card text in the world: title 8 to 10.7 mm on two lines, state 6 to 7.5 mm, owner on its role colour, mode 5.5 mm.
- Headings show counts ("Backlog 3"); an empty lane's heading dims.
- Bottom rail: New, Pause, Priority at the left; three assignment pads at the right while a card is held. The accepting pad grows and lights teal; a refusing pad lights coral. A released card eases to its slot.
- Dock: "In progress: Developer is on it" names the working agent.

## Design system changes

- **Type:** Inter Medium base, Inter Bold for bold, from the Inter 4.1 release (SIL Open Font License; licence text in `Assets/MuseDesk/Art/Fonts/`).
- **Plates:** 50 plates now have rounded corners (radius 12 percent of the short side, 1.5 to 6 mm). Bands that meet a sheet edge round only their outer corners. 29 thin strips stay square.
- **Keys:** 29 keys. 11 are wide: 9 use Antigravity's Wide and Chip models, the two paper primaries use generated geometry at ratio 3.4. Every key has the state component: hover lightens 12 percent, press darkens 14 percent, release flashes briefly, plus refused and confirmed flashes. Colours are applied through property blocks and removed at rest.
- **Motion:** blends between animation clips have a 0.2 s floor.

## Characters and completion

- Walk animation speed follows actual movement speed (stride length is an estimate: 0.15 m per cycle).
- The Researcher's approval reaction finishes before it walks off; a rejection no longer plays the failure animation.
- Two components wrote the chest bone; now one does.
- Head turn is capped at 60 degrees yaw, 30 pitch.
- **Completion, about 2.5 s, only after delivery is confirmed:** test station shows Passed and the Tester reacts; the other two look over and nod; the factory delivers; the shelf token grows in with one chime; the Tester glances at the user. Motion is reduced in focus mode.
- **Failure, about 1.6 s:** the responsible agent reacts, the others look at it, one low cue.
- Mode plaque shows "Connecting..." only while a connection attempt is actually running.
- Guide: "Your AI team is ready." then "Press Start on the task dock."; hidden while the paper is in the hand.
- Nameplates show short progress text from the event ("5/5 tests passed").

## Not done

- Antigravity's paper folio, receipt ticket, task card, dock plate, nameplate and plaque plates, polished factory and archive tray models are in the project but not yet placed in the scene.
- Office composition (desk spacing, factory alignment) was not changed.
- The receipt cannot be picked up; it is poke-only.
- In the scripted demo the Developer-to-Tester carry is usually skipped, because delivery arrives before it would start.
- The dashed stamp outline; the stamp target has a solid outline.
