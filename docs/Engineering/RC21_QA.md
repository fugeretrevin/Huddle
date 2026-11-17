# rc21 owner check (on the headset): your rc20 notes

**Build:** `Builds/MuseDesk_rc21_release.apk`, label `rc21-release`, release-signed, versionCode 5,
SHA-256 `3D9DA134B732DC2F627B6C36930A620A6E93394124B5FB36E8797E66C5D7635D`. Installed with `adb install -r` (settings kept);
on-device sha256 matches; versionCode=5. Rollback: rc20 `AB11FE6C`. Your rc20 words are in `RC20_QA.md`.
**Rule:** only your words make anything WORN.

| # | Your note | What rc21 does | Check | Result |
|---|---|---|---|---|
| 1 | Remove the Focus key; put Screen there | Focus is hidden; the Screen key sits in its exact slot on the dock | Dock: no Focus, Screen where it was | |
| 2 | The speech panel still clips | Cause: the readout sat flat in the dock's own plane, so it cut into the dock from any angle (the rc20 fix only worked on the Wall). Now it opens 10 cm above the mic key, 8 cm toward you, turned to face you; its keys come with it and never move under a finger | Talk at the desk and on the Wall | |
| 3 | It does not wait for my full sentence | Two causes: our own 6 s cap (now 32 s, a backstop) and Wit's server closing the request at a pause. A pause no longer ends the sentence: the mic reopens and the pieces are joined ("Manager, build a program" + "that prints stars" = one sentence). Short commands ("stop", "approve") still act at once | Say a long goal with a pause | |
| 4 | Paper not attached to the hands | It was pinned to the body, never to a hand. Now it rides the right hand (position and rotation), a little blended with the chest so it does not flail | Watch a question walk | |
| 5 | Agents walk to the lift, stand, turn round | That was a same-floor hand-off: the Tester's door is next to the lift, so the giver stood facing the doors and went home. Every hand-off now goes by tube; only a question for you walks; a waiting agent turns to whoever the trip is for, with a small label ("Developer: a question for you", "Waiting for the lift") | Run a plan | |
| 6 | Little popups at the pipes | A small dark label rides with each sheet: "Researcher → Manager: <the card's title>" (teal = result, coral = problem), then shrinks away; at most 3 | Watch the tubes | |
| 7 | TTS cuter, a bit robotic, unique, not annoying | Each agent's voice is pitched up a little and timed differently, with a light robot colour (small-speaker low cut, a very short metallic echo, a light chorus) and a quiet two-note chirp before a line (a different note per agent) | Ask agents questions | |
| 8 | Screen panel feels slow | The PC keeps capturing while the panel is open (stops 2 s after), skips unchanged frames, and the headset asks for up to 20 a second with no per-frame garbage. On this PC the backend delivered about 21 fps at 1280 wide | Open Screen, scroll a page | |
| 9 | Speech bubble when agents talk + head bob | A bubble with a tail above the talking agent (on the desk and life-size in the portal), the spoken line in it, and a small nod while it talks | Meet an agent, ask | |
| 10 | Antigravity's textures? | Kept our rc20 matte look (your choice). Their files are backed up in `Logs/antigravity_soft_backup/` | | |

## Automated evidence (PLAY/AUTO, not WORN)

- EditMode 1660/1660 (+19: sentence stitching, the walker facing who the trip is for). Backend 382 passed, 1 skipped.
- Play regression (backend off), each group on its own: Meet, Select-grab, Unified dock, Living Office stamp (86 s) and
  failure-path journeys, Floor -> Desk -> Ceiling, portal Meet -> Open -> ask -> Send back (lift 0.073), Apply change, desk
  walk 7/7, desk paper offer: all PASS, 0 real console errors. Ledgers `Logs/preflight/4784871_20261117_17*.md`.
- Play probes: the Screen key's slot equals the old Focus key's and the Focus key is hidden; "Manager, build a program" +
  "that prints stars" delivered as one sentence (2 pieces) to the Manager, "stop" delivered at once; the readout opens in its
  own spot (0.34 m from the eye, 15 cm below it, facing it, intersecting nothing); hand-off and problem report went by tube
  with labels ("Developer → Tester: Hand-off", "Tester → Manager: Problem report"); only the question walked; the paper is in
  the hand while walking (calibrated grip); a tube label captured on screen; the bubble above the perched Developer (12 cm
  above the head bone, all text visible after two fixes: the plate was too short and the head covered it) and above the
  life-size portal visitor (16 mm text at 1.4 m); head bob running while speaking.
- The Unity Editor had to be restarted once (its Play clock stalled at frame 1; the owner restarted it).

## Results

(Your words only.)
