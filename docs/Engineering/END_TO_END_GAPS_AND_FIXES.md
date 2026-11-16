# End-to-end gaps and fixes

2026-11-16. Fixed items are committed and tested; open items are ranked by submission risk (P0 core → P3 delight).

## Fixed today

| Id | Gap | Fix | Evidence |
|---|---|---|---|
| F-1 | "Actually, change the password rule to 12 characters." was answered as a status question (still read-only, but no proposal for the user to confirm) | `backend/app/phase3/side_intent.py`: lead-ins may carry a comma; softeners and agent names added ("Developer, change …"); negation unchanged | pytest 331/1 skip (+8 cases); LIVE journey D6 → `amendment_suggested`, `applied: false` |
| F-2 | Visiting agents existed twice (projection beside you + the original still working on the wall) | habitat avatar not drawn while its projection is out (`WallHabitatView.SetHidden`, `TickAwayHiding`) | PLAY: hidden during carry/perch/portal, back after return; MeetDrive 15/15, AgentSelectGrabDrive 12/12 |
| F-3 | Portal visitor head spun / body rolled (owner, worn rc13) | Animator always animates; target behind lets go instead of flipping; head turn ≤ 240°/s; lean ≤ 12°; bone guard against accumulation | PLAY: head within ~13° of the body even with the Animator off. Root cause on device **not reproduced in the Editor** (the Animator rewrites the bones there): needs WORN |
| F-4 | Portal visitor looked like it floated | hovering platform (disc, teal rim, floor glow) inside the pad | PLAY captures |
| F-5 | No indicator in Select Agents | per-hand beam, ring and "Pinch to pick up <Agent>" | PLAY SelectAim |
| F-6 | Ceiling deck rugs overlapped (coplanar, ~7 mm) | runtime mesh copy slides the side rug clear (asset untouched) | PLAY capture |
| F-8 | (G-1, presentation part) Manager-plan cards read "Completed" for text-only results | Wall status page: "N of M written up (text only, nothing was run)"; each such card "Written, not run · Proposed change: …" (`PlanBoard.IsTextOnlyResult/ProgressWords`, keyed on the runner's fixed titles; the backend state is unchanged) | EditMode `TextOnlyResults_SayWrittenNotRun`; PLAY: offline run's status page rows read back |
| F-9 | (G-3) Manager stamp journey re-run was blocked by an Editor clock stall | cleared on its own; re-run | PLAY LivingOfficeDrive RunOfflineStamp 13/13 and RunOffline 13/13 |
| F-7 | Ceiling paper was handed over mid-deck | with the pod lowered the Researcher walks to the front shelf (`CeilingShelfHandOver`, `AgentRegistry.Redeliver`) | PLAY: at the shelf, offering; OfferHoldDrive PASS (desk unchanged) |

## Open (ranked)

| Id | Pri | Gap | Why it matters | Proposed handling before 11-18 |
|---|---|---|---|---|
| G-1 | P0 (display fixed: F-8) | Manager-plan cards end in state `completed` although their result is a text proposal / test plan (no code, no test run). The paper footer says "LIVE model · text only, no code tools", but "completed" alone could read as finished work | brief: never show work as complete when only text was produced | Keep the footer; in the video narrate it as planning. A state/label change (e.g. "proposed") touches the contract (`contracts/`, backend, Unity fixtures): post-competition, or a coordinated change if the owner wants it now |
| G-2 | P0 | The executed path (desk auth task) writes a **fixed** OAuth scaffold regardless of the request; labelled `sandbox_execution` (true: real files and tests) but the code is not model-written | honesty of "LIVE" | Disclose in the demo text: "real files and real tests in a sandbox; the code is a fixed scaffold". Do not build an unrestricted code executor under deadline (brief §8) |
| G-3 | resolved (F-9) | Today's Play Mode re-run of the Manager stamp journey (LivingOfficeDrive) BLOCKED: after ~21:30 the Editor's Play Mode clock stalled (`Time.unscaledDeltaTime = 0` while frames advanced; auto-tick on/off made no difference). The paper logic itself works (a direct refresh shows 17 rows + APPROVED after the commit) | PLAY evidence for the plan stamp is from rc13 only | Restart the Editor and re-run `scripts\drv.ps1 LivingOfficeDrive RunOfflineStamp` |
| G-4 | P1 | Confirming a /btw amendment is not a UI flow: the proposal is returned, the change still goes through Revise on the plan | brief D6 step 5–6 | Acceptable (nothing changes silently); add a "Use this as a revision" key post-competition |
| G-5 | P1 | Free-text objectives need voice (Wit.ai, online); the hands path offers preset goals | offline judges can't type | By design for the judge build |
| G-6 | P1 | rc14 changes are PLAY only; portal + platform FPS unmeasured on device | release risk | Owner wears rc14 with `quest_perf_watch.ps1 -Label rc14_worn` (`QUEST_USER_ACCEPTANCE_QA.md`) |
| G-7 | P2 | App restart on the Quest, device network drop, 15-minute soak: not run | brief §12 | Owner session |
| G-8 | P2 | Researcher's plan write-up said "8–128 characters" while the revised developer steps say 12 (the revision only edited developer steps) | consistency of model text | Revise flow could apply criteria plan-wide; post-competition |

No new systems were added for the brief; nothing that worked was removed.
