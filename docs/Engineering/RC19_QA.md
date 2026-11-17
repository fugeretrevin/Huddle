# rc19 owner check (on the headset): your six rc18 notes

**Build:** `Builds/MuseDesk_rc19_release.apk`, label `rc19-release`, RELEASE-signed (your key), versionCode 3.
Hash and install: see the bottom of this file. Rollback: rc18 `FEEFEFC1` (same key, but a lower versionCode: going back may need an uninstall, which resets the saved
panel layout).
**Rule:** only your words make anything WORN. Mark each line PASS / FAIL / NOTE.

**Automated evidence (PLAY/AUTO, not WORN):** compile; EditMode 1640/1640; Living Office offline journey 13/13 in 86 s
(was 124 s), with 0 elevator riding frames; failure-path journey 16/16 (0 rides, 2 sheets by tube); Meet drive 15/15;
plan paper ticks driven and captured (tick, cross, struck-through title); portal measured against a 1.2 m visitor.

| # | Your note | What rc19 does | Check | Result |
|---|---|---|---|---|
| 1 | "slightly less reflections, it looks a little too toy-like" | Every glossy art material lost 18% of its smoothness (agent visors 0.80 -> 0.66, the cream body ceramic 0.68 -> 0.56, brass 0.76 -> 0.62; 27 materials, list in `Logs/rc19_gloss_applied.txt`); sky reflections 1 -> 0.55; the warm key light 0.95 -> 0.85 | Agents read softer, less plastic; still warm and 3D | |
| 2 | Paper tubes distracting; "go over the back wall, not in front" | The tubes now leave the printer, go back to the back wall, run down it and along it under the ceiling, and only come forward under the ceiling to above each desk. Nothing crosses in front of the floors. The glass is fainter (alpha 0.32 -> 0.22) | Approve a plan: tubes sit against the back wall | |
| 3 | Meet: platform too high, head hits the top of the portal | The platform hovers about 6 cm under a 1.2 m visitor (was about 12); the light column is sized from the visitor (head + 8 cm, wider than the headphones) | Meet -> Open here: the head is well under the arch | |
| 4 | Headphones too wide; "make the portal a bit bigger" | The portal (arch, pad, platform) is 1.2x. The arch's posts now clear the headphones. Still inside the clearance the placement checks | Same visit: nothing passes through the posts | |
| 5 | Manager paper: too many buttons; "a check box or an x next to each work item" | Each work item has a small tick key beside its title: a green check keeps it, a red cross skips it (its title is struck through). The Keep 1 / Keep 2 / Skip 3 keys are gone: the paper's keys are More, Revise, Close (and Confirm risk only when a risky step is ticked). Approve / Reject are still the stamps | Manager -> plan: poke a tick, it flips to a cross | |
| 6 | "When the agents are bringing up their papers they should use the tubes too; elevators only for a question" | A finished result (Researcher), a problem report (any agent, red sheet) and a hand-off to an agent on another floor now ride the tubes: the sheet lifts off the desk, rolls up, goes up the tube to the Manager's tray (and on down the other agent's tube for a hand-off). The Manager's plan summary flies straight from the printer to you (no ride down, about 20 s saved). The elevator is left for a question to you (approval). A hand-off on the same floor is still a short walk | Run a plan: nobody rides the elevator; the summary arrives within seconds of the last card | |

## "Build something real" (your note 7)

The live check uses the real Meta model and the real restricted workspace on the PC (Developer writes files, Tester runs
pytest). See the results section below (filled in from the run, never from a guess).

## Results

- **Build:** `Builds/MuseDesk_rc19_release.apk`, SHA-256 `F197F662B6FA02339014D8AC75EA2BB1BF6323F0953D1A1B88BC2C895F5EFD57`,
  signer `CN=MuseDesk, O=MuseDesk`, label `rc19-release` found in the APK, versionCode 3.
- **Installed on the Quest (2026-11-17) with `adb install -r`** (no uninstall, settings kept); the on-device sha256 matches;
  `dumpsys` shows versionCode=3. rc19 is now a protected path in `BuildApkTo.cs` (the next build is rc20, versionCode 4).
- **Live real build:** (below, from the run)
