# MuseDesk: status for expert review (fifth pass)

2026-11-13, about 13:45. Branch `release/final-refinement`, build `rc-feedback-2` (commit `3aa7fc2`), installed on the Quest 3. Deadline 2026-11-18, noon Pacific: about four and a half days.

We would like your feedback on the direction, the risks, and what to cut or finish in the time left. Questions are at the end.

## What MuseDesk is (one paragraph)

A Quest 3 mixed-reality app for supervising AI agents. Three small rigged characters (Researcher, Developer, Tester) work in a miniature office on the user's real desk. When a decision is needed, an agent walks up and hands the user a paper; the user stamps Approve or Reject, and the backend (FastAPI, authoritative) records it. A labelled scripted demo mode works offline for judges. The office can also move to the floor beside the user, or into a pod that docks on the real ceiling and lowers on request. Hands-first; voice is optional and needs internet.

## Evidence levels

AUTO = automated tests; PLAY = Unity Editor Play Mode with driver scripts (synthetic room, simulated hands, no passthrough); APK = built and installed; WORN = the owner tried it on the headset.

| Area | Level |
|---|---|
| Desk decision loop (paper, stamp, receipt, board) | WORN (approve with the stamp works) |
| Ceiling pod, basic | WORN on `rc-spatial-1` ("looked nice") |
| Headset feedback round 1 (stamp poof, floor pointing, console, movable panels, voice centre, character reactions) | WORN on `rc-feedback-1`: produced the five items in round 2 |
| Ceiling ideas round and feedback round 2 (below) | PLAY + AUTO + APK. **Not worn.** |
| Device performance (frame rate, fill rate) | **No numbers yet** |
| Live backend end to end | 71/71 server-side e2e; on-device live run not repeated since round 1 |

## What changed since review 4

1. **`rc-spatial-1`**: Floor and Ceiling modes with art, swept-volume movement safety (every move is planned and swept before anything moves; routes that can't be shown clear are refused; Stop halts and reverses), fail-closed recenter.
2. **Headset feedback 1 (`rc-feedback-1`)**: stamps poof away after a decision; point-and-drag floor placement; ceiling props carried with the pod; upright "needs you" beacon; movable panels with grab bars; one mode console (Desk/Floor/Ceiling plus context keys); voice command centre (local intent parser, undo window); characters react to poke, gaze and wave.
3. **Ceiling ideas round** (from the art team's pitch):
   - integrated their ceiling effects (iris mount, portal cone, halo, rings, particles), driven by our pod state machine; particles can be switched off separately if fill rate suffers;
   - three-stage pod sound (unlock click, travel hum, lock chime), generated in code, spatialised;
   - "reach up and pull it down": aim or look at the docked pod, pinch, pull down 12 cm to lower it, push up to raise it; goes through the same swept-route request as the console key;
   - a scan grid on the real ceiling while choosing the dock spot. Light bouncing onto real furniture was skipped.
4. **Headset feedback 2 (`rc-feedback-2`, now installed)**:
   - **Floor placement refused reasonable spots, and "ready" + release did nothing.** Root cause: the only route shape to the floor turned while overhead, swinging a corner within 6–8 cm of the head axis, so the sweep correctly refused most spots beside or behind the shoulder; and on release, a route failure silently snapped the footprint back. Fix: a route planner that also tries side-step shapes (all through the unchanged sweep); "ready" now includes the route check; letting go on a ready spot places it (needs a 0.25 s pinch and a 5 cm drag); refusals stay on screen with the reason. Chosen-spot margins loosened slightly (user 0.6 → 0.45 m to the sides and behind, furniture 0.15 → 0.11 m), both still above the sweep's own margins. Testing found and fixed a safety bug: losing hand tracking mid-drag counted as letting go and placed the office.
   - **Guided placement:** pressing Floor or Ceiling (or saying it) shows a card, "Look at the floor" / "Look up at the ceiling"; after a 0.6 s gaze hold the preview opens and the scan grid starts from where the user looked. "Start here" appears after 8 s; Cancel restores everything.
   - **Text fit:** task-card labels kept 2.3 mm or more inside the card; agent nameplates centred on one line; gaze status chips kept inside their plate.
   - **Panels move up and down** within a desk-relative band; height is kept.

Automated: every touched fixture passes when run one at a time (spatial, ceiling, floor, panels, guide, audio, VFX map, pull gesture; well over 300 tests in those fixtures). The full EditMode suite in one run hung the Editor once; cause not found.

## Known risks and loose ends

- **Nothing from the last two rounds has been worn.** The floor side-step routes are new office movement paths that nobody has watched on the headset.
- **Performance unknown.** Additive ceiling effects and two scan grids over passthrough; no frame-rate capture yet (a recorder script exists).
- Release-to-place means an accidental grab plus a drag can now move the office (still swept, stoppable, blocked while a paper is held). A pinch meant for a paper can still arm the floor drag or the ceiling pull before the Interaction SDK registers the hover.
- Feel values are untested on the head: guide card pose, gaze angles and dwell, 12 cm pull threshold, sound volumes, chip font size (now 4–4.7 mm).
- Voice needs internet (Wit.ai); no text-to-speech. A voice "Not sent" wording patch was compiled but not re-run.
- Docs lag the code in places (`ROOM_PLACEMENT_FINDINGS.md`, `FINAL_SPATIAL_RELEASE_AUDIT.md` still describes `rc-spatial-1`).
- No demo video or judge script for the current build yet.

## Where we think to go from here (please challenge)

Proposed order for the remaining days:

1. **On-head pass of `rc-feedback-2`** (owner), then one fix round. Highest value: it validates two rounds of unworn work.
2. **Device performance capture** in Desk, Floor and Ceiling (worst case: pod descending with effects). Turn off particles or a scan grid if under target.
3. **Feature freeze** after that round: no new features, only fixes from wear testing.
4. **Judge path hardening:** the offline scripted demo from a cold start, onboarding text, fallbacks when room data is missing (Desk mode only), and a crash-free 15-minute soak.
5. **Submission material:** demo video (Desk loop, then Floor, then Ceiling), short judge guide, honest "what is live vs scripted" note, updated release audit.
6. **Flags decision:** whether Floor and Ceiling ship on by default, or stay opt-in behind the mode console with Desk as the default (rule in `docs/Engineering/FINAL_FEATURE_FLAG_DECISION.md`).

## Questions for you

1. With four and a half days left, is freezing features after the next wear test the right call, or is there one more feature worth the risk?
2. Floor and Ceiling: ship on by default for judges, or Desk-first with the spatial modes as an opt-in "wow" step? Which do you think demos better in a 3-minute video?
3. Release-to-place on the floor: acceptable risk with the sweep and Stop behind it, or should placing need an explicit confirm again?
4. The ceiling effects and scan grids: which, if any, would you cut first if the frame rate is tight?
5. Anything in the judging criteria (standalone use, honesty about AI, hands-first) you think is still under-served?

Start-of-session detail for engineers: `docs/Engineering/HANDOFF_NEXT_SESSION.md`.
