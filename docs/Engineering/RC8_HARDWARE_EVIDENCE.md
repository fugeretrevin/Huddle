# rc8 hardware evidence

Build worn: `rc8-living-office`, `Builds/MuseDesk.apk`. The file on disk matches the recorded rc8 hash `788D4A0401EB274EC39751E539C778F3CCC01D1AB9AC1638A03AB15A4C7E1428` (checked 2026-11-15 evening). Nobody confirmed the hash of the package installed on the headset.
Commit: `competition-rc8` (`911465e`). Tester: the owner. Method: an informal worn session, reported verbally to engineering. No video, logcat or frame-rate capture was taken.

Evidence levels: AUTO, PLAY, APK, WORN, NOT RUN. These are defined in the release QA plan, `03_QUEST_QA_DEMO_AND_SUBMISSION.md` §A. Every row below is **WORN**: observed by the owner wearing the Quest 3.

## Round 1 findings (owner, 2026-11-15)

| # | Area | What the owner saw | Severity | Fix owner / branch |
|---|---|---|---|---|
| W1 | Wall mode | When Wall mode opens, each agent appears twice, stacked on itself at two different scales | P1 (hero shot) | Wall doubling + Manager paper workstream |
| W2 | Manager plan paper and buttons | Paper and keys look unpolished. Key labels are too long and truncate with "…" | P1 (unreadable primary text) | Wall doubling + Manager paper workstream |
| W3 | Startup reach | At launch the near-user panels, including the Start/board key dock, are several steps away | P1 (off-screen panel, first five minutes) | Unified dock workstream |
| W4 | Software factory | The factory model and belt animation look wrong (stray pieces, unclear meaning). **Owner decision: remove it** | P1 (decision) | Factory removal workstream |
| W5 | Spaces | Floor and Ceiling modes could not be reached. Owner requires Wall, Floor and Ceiling all available, with the earlier dock and placement flow working for all three | P1 (owner priority) | Unified dock workstream |
| W6 | Select agents | After pressing "Select agents", force grab does not work: agents cannot be grabbed, interacted with or put back | P1 (featured flow broken) | Select-agents grab workstream |
| W7 | Manager stamp | The Manager plan paper has no stamp area, yet the stamps appeared | P1 | Wall doubling + Manager paper workstream |
| W8 | Desk proposal offer | The offering agent repeatedly brings the paper out and takes it back. It should hold the paper up until it is grabbed | P1 (repeated, hero mechanic) | Desk proposal paper workstream |
| W9 | Proposal paper keys | Too many keys. **Owner decision:** remove Approve and Reject (the stamps do that job) and the Voice key; keep only More and Revise | P1 (decision) | Desk proposal paper + Manager paper workstreams |
| W10 | Voice | **Owner decision:** no voice key on the paper. Put a mic mute/unmute toggle on a settings panel so the user can just talk (hands-free) | Change request | Hands-free voice workstream |

## Not observed or not reported in round 1

The owner did not report on the following:

- the desk stamp approve loop;
- the board drag;
- the elevator;
- the portal;
- frame rate.

All of these stay **NOT RUN (WORN)** for rc8 until a worn pass reports them by test number. Use the template in `03_QUEST_QA_DEMO_AND_SUBMISSION.md` §B.

## Next worn pass

The next pass runs on the build that integrates the round-1 fixes (the hash is recorded when it is built). It should:

- re-check each of W1–W10 with the same action;
- run the 20-step smoke in §B of the QA plan;
- capture device frame rate with `scripts\quest_perf_watch.ps1`.
