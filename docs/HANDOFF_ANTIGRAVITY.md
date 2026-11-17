# Handoff to Antigravity

Written 2026-11-12, about 17:10. Branch `integration/audit-repair`. Last clean commit before this handoff: `95e75d0` (compiles, 140 backend tests, 103 Unity runtime checks, APK built and installed on the Quest).

Read these first, in this order (ten minutes):

1. `docs/Engineering/FINAL_COMPETITION_READINESS.md` (state, what works, what is unverified)
2. `docs/Engineering/FINAL_XR_VALIDATION.md` section 4 (the on-head checklist nobody has run yet)
3. `docs/Engineering/FINAL_POLISH_CHANGELOG.md` (what the last pass changed)
4. `docs/AGENTS.md` (binding rules: never show scripted work as live; the backend decides; no unsafe execution)

## The single most important thing

**The current build has never been run on the headset.** The headset was locked, which blocks app launch. Before adding anything, get a person to unlock it and run the checklist. Every serious interaction bug on this project was found on head, not in the Editor.

## Work in flight when this was written (may be partial: check `git status` first)

Three pieces were started and are NOT finished, NOT compiled together, and NOT committed as working:

| Piece | Files | State |
|---|---|---|
| Revise-and-retest loop (backend) | `backend/app/live_orchestrator.py`, `orchestrator.py`, `workers.py`, `phase2/board_bridge.py`, `run_recovery.py`, new `backend/tests/test_revise_loop.py`, `backend/scripts/live_e2e.py` | A background agent was mid-way. Run `backend\.venv\Scripts\python.exe -m pytest -p no:cacheprovider` from `backend`. If it is not green, either finish it or `git checkout -- backend` to return to the 140-test state |
| Rendered fingers stop at grabbable surfaces | `Assets/MuseDesk/Scripts/Workspace/DynamicFingerCollision.cs`, possibly new `HandSurface*.cs` / `FingerSurface*.cs`, `AgentScripts/BuildHandSurface.cs` | A background agent was mid-way. If the project does not compile because of these, `git checkout -- Assets/MuseDesk/Scripts/Workspace/DynamicFingerCollision.cs` and delete the new files. The component was parked (`GloballyEnabled = false`) before; that is the safe state |
| Touch an agent to see its task | `Assets/MuseDesk/Scripts/Agents/AgentTouchSummary.cs` | Written, never compiled, not in the scene. Needs: compile, then a builder that adds it to the three agents with a small card (plate + TMP text above the head, hidden by default) and `touchPoint` = the `Head` bone |

Intended design of the revise loop, so it can be finished or redone:

- **Request changes** no longer ends the run. The Researcher revises (still a template, still labelled `research_provenance: "template"`), then raises a new decision with a new id, `revision: 1` and `revises_decision_id`. At most 2 revisions; a third request cancels the run honestly.
- **Failing tests** get one developer retry and a real re-run. The artifact reports the final attempt and carries `attempts` and `first_attempt`.
- On the board the card goes running, awaiting approval, running (`task.resumed`, `reason: "changes_requested"`), awaiting approval again.
- The Unity side then needs: the offline demo to mirror it (`Phase2DemoGateway`, `DemoAgentGateway`, `DemoBoardRunBridge`), and a fifth key "Changes" on the decision paper wired to `DocumentDeliveryController.OnRequestChanges` (the method exists; clone `Btn_Details`, five keys at x = -0.124, -0.062, 0, 0.062, 0.124, y = -0.142).

## Not started

1. **Mesh merging.** Your `Assets/MuseDesk/Art/Models/Optimized/*` models cut the three workstations and the Kanban frame from 206 renderers to 68 with the same material count. They are not in the scene. To swap: instantiate under the same parent with the same local pose, re-add `WorkstationVisualController` and call `ResolveAnchorsAndParts()`, move the `FurnitureCollider` child across, re-point any scene reference into the old subtree (for example `TestingStationController.testingStation`), map materials by name (the scene uses per-renderer material overrides on the current instances), deactivate the old one. Check that these child names survive in the optimized models: `Light_Idle`, `Light_Testing`, `Light_Pass`, `Light_Fail`, `Light_Blocked`, `Testing_ScannerRing`, `Testing_ChamberDoor`, `Printer_OutputBlueprint`, `MonitorScreen_Left`, `MonitorScreen_Right`, and the anchor empties. Also: each 3D key housing is five cubes (20 keys); merge into one mesh, or use your `UI_PokeKey_Housing.fbx`.
2. **Distance grab.** The rig already has `DistanceHandGrabInteractor` on both hands. Add `DistanceHandGrabInteractable` to `TaskBoard/CardTemplate` and `DecisionDocument` (its `Reset()` copies settings from the sibling `HandGrabInteractable`; a movement provider is added automatically at start), and add it to each object's `ReRegisterInteractable.interactables` list. Needs an on-head check that it does not fight the near grab.
3. **Performance measurement.** None exists. `scripts\quest_run.ps1 -Label demo -Seconds 120` collects it once the headset is unlocked. See `docs/Engineering/FINAL_PERFORMANCE_REPORT.md`. Foveated rendering is deliberately deferred.

## Your own uncommitted files

Left exactly as you wrote them, not committed by me: everything under `ArtSource/`, `Assets/MuseDesk/Art/Models/` (new folders and the five modified FBX files: Kanban frame, task card, approval stamp, two paper models), `Assets/MuseDesk/Art/Prefabs/VisualPolish/`, `Assets/MuseDesk/Scenes/VisualPolishPreviewScene.unity`, and `Assets/MuseDesk/Art/Scripts/VisualPolishAffordanceController.cs`.

One change I made to your code: `VisualPolishAffordanceController.cs` did not compile and blocked the whole project. I corrected two things: `OnAssignResolved` now has the real signature `(TaskCardView, Phase2CommandResult<TaskCardV2>)`, and the badge group subscribes to `SessionReducer.AgentChanged(AgentView)` (there is no `AgentStatusChanged` event).

The five modified FBX files are used by `MainMRScene` and are in the installed build. If their size or pivot changed, task-card text and the board layout may no longer line up; check this first if the board looks wrong.

## How to work in this project

- One open Unity Editor, driven from the command line: `unity command script recompile`, `unity command script recompile_status`, `unity command script run --file AgentScripts/<Builder>.cs --entry <Class>.<Method>`. Scene changes are made by idempotent builder scripts in `AgentScripts/`, not by hand. The Editor does not answer while a build is running.
- Checks to run before every commit:
  - `unity command script recompile_status` (must say `failed: false`)
  - `unity command script run --file AgentScripts/RuntimeSmokeTests.cs --entry RuntimeSmokeTests.Run` (103 checks)
  - `backend\.venv\Scripts\python.exe -m pytest -p no:cacheprovider` in `backend` (140)
  - `unity command script run --file AgentScripts/SceneHealth.cs --entry SceneHealth.Run` (0 missing scripts)
- Build and install: `unity command script run --file AgentScripts/BuildApk.cs --entry BuildApk.Run --timeout 900`, then `adb install -r Builds\MuseDesk.apk`. Details in `docs/Engineering/RUN_AND_DEPLOY.md`.
- Layout convention: `WorkspaceRoot` +Z points at the seated user; the eye is about root-local (0, 0.30, 0.62); the user's right is -X. Text and poke keys face the user with their local -Z. Capture what the user sees with `AgentScripts/ViewFrom.cs`.
- Scene builders that define the current scene, safe to re-run: `ArrangeForUserSide.cs`, `PolishPass2.cs`, `FinalPass.cs` (`Paper`, `Affordances`, `Guide`, `Archive`, `DemoKeys`), `BuildAgentLiveliness.cs`, `QuestRenderSetup.cs`. Note `PolishPass2.Buttons` rebuilds the key housings, so re-run `FinalPass.DemoKeys` after it.
- Files are mixed LF/CRLF. Shell text replacement silently misses; edit with a real editor and re-read.
- TextMeshPro auto-sizing breaks at font sizes below 1: put the label at scale 0.01 with 100x the font size.
- Do not claim anything works on the headset unless a person saw it.

## Suggested order

1. On-head checklist on the installed build; fix what it finds.
2. Decide the fate of the in-flight pieces (finish or revert) so the tree compiles and tests are green; commit.
3. Performance run, then the optimized-model swap as a measured before and after.
4. Revise loop on the Unity side and the "Changes" key.
5. Distance grab, then the agent touch summary.
6. Submission steps in `FINAL_COMPETITION_READINESS.md` section 6. Deadline: 2026-11-18, noon Pacific.
