# Art branch merge plan: `art/living-world-polish` (642ace5)

Date: 2026-11-15. Status: read-only analysis, no merge performed. Evidence below is static (Grep/Glob/line counts/YAML reading) unless marked otherwise. Nothing here is on-headset (WORN) or Editor-verified.

Sources: art worktree `C:\dev\MuseDesk\MuseDesk\.worktrees\living-world-art` (branch `art/living-world-polish`, base `feature/living-world-polish` 7f87991 + `art/wall-habitat` 0b8f842). Production: main tree `C:\dev\MuseDesk\MuseDesk` (`feature/living-world-polish`, which has moved past 7f87991).

## 0. Method limits (read first)

- This analysis had no git or shell access. "Changed file" was inferred by comparing the two trees (presence, line counts, GUIDs, grep of distinctive strings). It is not a diff. Byte sizes were not measurable.
- Before any merge, run the authoritative listing: `git diff --numstat <merge-base> art/living-world-polish` and `git diff --name-status`. Use the merge-base of prod and art, not 7f87991, because prod has moved.
- Line counts are a fingerprint only. Equal counts do not prove equal content.

## 1. Risk table

| # | Level | Area | Finding (evidence) | Required action before merge |
|---|---|---|---|---|
| R1 | High | `Assets/MuseDesk/Scenes/MainMRScene.unity` | Art has 150,494 lines vs prod 150,484 (+10). Object count (4,744 `--- !u!` headers) and `m_Name` count (2,334) are identical, so it is a property-level change. The handoff says MainMRScene is untouched. This is unexplained. Could also be prod-side drift. | `git diff --numstat` on this file. If non-zero, review the hunk. Do not merge the scene blindly. |
| R2 | High | Desk agents: `Assets/MuseDesk/Art/Models/Characters/Agent_{Developer,Researcher,Tester}.fbx` (+.meta) and `Assets/MuseDesk/Art/Controllers/MuseDesk_Agent.controller` | Same GUIDs. MainMRScene desk agents are variants in `Assets/MuseDesk/Prefabs/Agents/Agent_*.prefab` (source FBX GUIDs 0469746c…, 2548f5ff…, 9a68e311…; controller GUID 59a092e6…). They pick up the art FBX and controller automatically. FBX meta clips: 39 (prod) to 77 (art). Controller: 39 AnimatorState objects (prod) to 100 (art: 77 Base + 16 UpperBody + 7 HeadGaze). Adds `IsCarryingPaper` bool and two layers at weight 0. All 39 prod state names still exist. Import settings identical (globalScale 1, useFileUnits 1, animationType 2 Generic). | Gate on the state table in section 2 and the Play checks in section 4. |
| R3 | High | Live Art scripts changed, used by MainMRScene: `WorkstationVisualController.cs` (+20 lines), `WorkstationStatusDisplay.cs` (+20), `FactoryEventController.cs` (+17), `TestingStationController.cs` (+17), `SoftwareFactoryVisualController.cs` (+9) | GUID grep: MainMRScene references all five (WorkstationStatusDisplay x3, WorkstationVisualController x3, FactoryEventController x1, SoftwareFactoryVisualController x1, TestingStationController x1). The changes are unknown without a diff. `AgentAnimatorBridge.cs` in `Scripts/Art` (MuseDesk.Art namespace, +18) is not in MainMRScene. | Diff each. Confirm desk workstation, factory and testing visuals are unchanged in Play. |
| R4 | High | Approval/feedback semantics (AGENTS invariant 2) | Art handoff section 2 suggests forwarding stamp/approve/reject callbacks into `MusePhysicalCardFeedback.PlayApprovalConfirmed/PlayRejectionConfirmed` and `MuseVfxPoolPlayer.PlayEffect` on "stamp" events. The method names say "confirmed". A local stamp press must not trigger them before the backend ack. | Do not wire any of these in this merge. Any future wiring must be keyed to acknowledged backend state. |
| R5 | Med | `Assets/MuseDesk/Scripts/Wall/**` and `Assets/MuseDesk/Scripts/Spatial/Core/Wall/**` | The art tree is an older snapshot. Prod-only files: `Scripts/Wall/WallPlanPaper.cs`, `Scripts/Spatial/Core/Wall/HabitatLift.cs`, `Scripts/Spatial/Core/Wall/LivingOfficeFlow.cs`, and related tests. Line counts differ on about 15 shared files (e.g. `WallHabitatView.cs` 561 art vs 702 prod; `HabitatStage.cs` 240 vs 341; `HabitatLayout.cs` 613 vs 561). Art code does not reference any new Art class from these folders. The handoff says Spatial is untouched. | Run numstat against the merge-base. Expected result: zero changes under these two paths. If non-zero, stop and resolve manually. Otherwise take prod's version. |
| R6 | Med | Editor pipeline: `Assets/MuseDesk/Editor/ArtPipeline/*` | New in art: `LivingWorldVfxAndGallerySetup.cs`, `WallHabitatArtSetup.cs`, `CharacterMaskAndSocketSetup.cs`, `ExperiencePolishCapture.cs`. Probably modified: `MuseDeskArtPipeline.cs`, `SpatialWorkspaceArtSetup.cs` (exist in both trees; not diffed). These write prefabs under `Art/Prefabs/{Characters,Spatial,WallHabitat,VFX,Documents}` and scenes under `Scenes/ArtGalleries`. | Compile only. Never run BuildAll/`LivingWorldVfxAndGallerySetup` against the production branch. |
| R7 | Med | Audio ownership | Art adds 12 `.wav` cues in `Assets/MuseDesk/Art/Audio/` (no prod folder). Prod already plays procedural audio from `FeedbackController`, `WallAudio`, `StampTool`, `CeilingPodAudio`. Art components call `AudioSource.PlayOneShot` directly with no Focus-mode mute or rate limit. See section 5. | Choose one owner per cue. Do not attach art audio components to prod prefabs in this merge. |
| R8 | Med | Animation lies (AGENTS invariant 2) | `WallHabitatElevatorVisualController.Update()` (line 314) fires `PlayElevatorArrival` (chime, particles, audio) when cab travel completes, so any caller of `MoveCabToStop` gets an arrival. `WallHabitatManagerDelegationVisualController` has public Play* methods with no caller and auto-returns to idle. Handoff step 3 says to call `PlayDelegationComplete()` during "delegation beats" without requiring a backend event. | Keep unwired. Future wiring must follow a real backend task-assigned/complete event. |
| R9 | Med | Documentation drift vs repo | Handoff says "65 preserved + 12 new" clips. Prod desk controller has 39 states and art has all 39 plus 38 new (not 12). `ULTIMATE_ANIMATION_MANIFEST.md` says HeadGaze has 9 states plus empty. The file has 7 HeadGaze states including `HeadGaze_Empty`. `WALL_HABITAT_ASSET_MANIFEST.md` names `ManagerLookoutSocket`, `TestingCubicleSocket`, `PlanningNookSocket`, `ElevatorBoardPose_*`, `CabAgentStandPose`. These do not match the code or the prefabs (which use `ManagerOfficeSocket`, `TesterCubicleSocket`). The handoff says `AgentScripts/WallArtFitCheck` gates manifest entries. That tool does not exist (only a comment in `HabitatArtManifest.cs` line 12). | Treat code and prefab YAML as truth. Do not trust these doc counts for acceptance. |
| R10 | Med | Wall art prefab set | Art adds `Assets/MuseDesk/Art/Prefabs/WallHabitat/` (25 prefabs incl. `Agent_Manager.prefab`), `Assets/MuseDesk/Art/Controllers/WallHabitat/` (`MuseDesk_WallAgent.controller` and 6 others). None exist in prod. Prod's `HabitatArtManifest.asset` (GUID feabf63b…, wired in MainMRScene `WallHabitatController.artManifest`) has zero entries and `artVersion: proxy-0`. | Keep the manifest empty. Keep the art Wall folders out of the competition build. |
| R11 | Med | Scale and root | Desk prefab variants have root scale 0.03 (prod and art). Art `Agent_Manager.prefab` has a child at scale 0.01 (line 125). Art `Characters/Agent_*.prefab` have no `m_LocalScale` match in grep, so the scale path is unclear. Wall clones are rescaled by height in `WallHabitatView.CloneVisual` (line 333–357), so clones are safe. Standalone art modules are not. | Measure agent height in Play for desk and wall. |
| R12 | Low | New runtime scripts (`Scripts/Art/`) | 10 new files: `MuseVfxPoolPlayer`, `MusePhysicalCardFeedback`, `SpatialQualityPresetAdapter`, `WallHabitatElevatorVisualController`, `WallHabitatManagerDelegationVisualController`, `WallHabitatAmbientLifeVisualController`, `WallHabitatLargePortalVisualController`, `WallHabitatSocketRegistry`, `WallHabitatAgentSelectionVisual`, `WallHabitatPlacementGhostController`. No prod code calls them. GUID grep: only the 13 Wall prefabs and `Agent_Manager.prefab` attach them. Prod's `WallHabitatView` builds its own proxies and does not use prefab sockets. | Low, if not attached. Compile only. |
| R13 | Low | Binary and size | Art has about 137 FBX (prod 115), about 137+ `.blend` under `ArtSource/Blender`, 12 wav (4–20 KB each, per art perf report), 4 gallery scenes (about 14k lines), about 43 PNG screenshots. Sizes not measured. Line-level bulk is generated Unity YAML and FBX metas (`Agent_Developer.fbx.meta` alone is 2,349 lines). No `.gitattributes` file exists at the main repo root, so FBX/blend/wav are likely stored as plain git blobs (no LFS). | Measure with `git ls-tree -r -l` on the art branch. Keep `.blend` and gallery scenes out of the competition branch. |
| R14 | Low | Packages and ProjectSettings | `Packages/manifest.json` 54 lines in both. All 25 `ProjectSettings/*.asset` have identical line counts. | Confirm with numstat (expected empty). |
| R15 | Low | Build settings | `EditorBuildSettings.asset` lists only `MainMRScene.unity` in both trees. Gallery scenes are not in the build. | None. Keep it that way. |

## 2. Desk-agent Animator state check (`Scripts/Agents/AgentAnimatorBridge.cs`, production)

The bridge (namespace `MuseDesk.Agents`, 499 lines in both trees, content not diffed) uses Mode.Direct (CrossFadeInFixedTime by name, SetFloat MoveSpeed) because the controller has `Idle_Breathing` and `Walk_Loop`. Mode.Params (StateId int, IsMoving bool, Tr* triggers) is not used. Neither prod nor art controller has those parameters. Missing states fall back to Idle through `HasState` (lines 231, 336, 348, 471, 487), so a missing name fails silently rather than throwing.

Art file checked: `Assets/MuseDesk/Art/Controllers/MuseDesk_Agent.controller`. "Base Layer" state objects are at lines 563–793 (77 children). Each name was matched to its object header.

| Bridge constant (line) | State name | Art Base Layer object (fileID) | Status |
|---|---|---|---|
| S_Idle (75), default (line 804) | `Idle_Breathing` | -3780433417157173518 | Present |
| S_Walk (76) | `Walk_Loop` | -2092220098466958375 | Present |
| S_Reading (77) | `Reading` | -427365254819247343 | Present |
| S_Typing (78) | `Typing` | -5598353552855464194 | Present |
| S_Inspecting (79) | `Inspecting` | 8885030839381286508 | Present |
| S_Talking (80) | `Talking` | 6749274687868114456 | Present |
| S_Listening (81) | `Listening` | -4091193496569842653 | Present |
| S_Thinking (82) | `Thinking` | -4910270633414687708 | Present |
| S_Waiting (83) | `Waiting_For_Input` | -6090844772480366940 | Present |
| S_Blocked (84) | `Blocked` | 7366210781848304331 | Present |
| S_Worried (85) | `Worried` | 6280543861612147228 | Present |
| S_Offer (86) | `Offer_Document` | 7980405082922740886 | Present |
| S_TaskCompleted (87) | `Task_Completed` | 6880129710037221483 | Present |
| S_Success (88) | `Success` | 7117098129034984029 | Present |
| S_Failure (89) | `Failure` | -8325736748719155690 | Present |
| S_Error (90) | `Error` | -8282178559483714384 | Present |
| S_Give (91) | `Exchange_Package` | 867154331010030020 | Present |
| S_Receive (92) | `Receive_Document` | 4314099101026997471 | Present |
| S_TurnL (93) | `Turn_Left` | -7091337471781076352 | Present |
| S_TurnR (94) | `Turn_Right` | 2575704210220932275 | Present |
| S_Nodding (95) | `Nodding` | -8753233487398250164 | Present |
| Params-mode: `StateId`, `IsMoving`, `TrOffer`, `TrSuccess`, `TrFailure`, `TrError`, `TrHandoffGive`, `TrHandoffReceive` (64–71) | parameters | none in art or prod | Not used on desk (Direct mode). Would break if Params mode were ever selected. |

Other checks:
- Duplicates across layers (for example `Hold_Document`, `Listening`, `Shaking_Head`, `Carry_Package`) exist by design: Base plus UpperBody or HeadGaze. No duplicate was found inside Base for the bridge names.
- Transitions and MoveSpeed thresholds in the Base Layer were not diffed. Verify in Play.

**Wall: `MuseDesk_WallAgent.controller` (art only, no prod copy).** `Scripts/Wall/WallHabitatView.cs` uses these clip names: `Idle_Breathing`, `Walk_Loop`, `Typing`, `Reading`, `Inspecting`, `Waiting_For_Input`, `Offer_Document`, `Talking` (lines 32–34); `Thinking`, `Worried`, `Writing`, `Nodding`, `Turn_Left`, `Turn_Right`, `Listening` (lines 498–500). All 15 names exist in the art WallAgent controller by `m_Name`. Base Layer membership was not checked per name.

**Important correction:** Prod's wall agents are CloneVisual copies of the desk agents (`WallHabitatController.agentSources` in MainMRScene are scene objects 913268390, 1515650341, 1905904050). `StripToVisual` removes MonoBehaviours but keeps the Animator and its controller. So prod's wall visuals run on the desk `MuseDesk_Agent.controller`, not on `MuseDesk_WallAgent.controller`. Every wall clip name above also exists in the desk Base Layer. The WallAgent controller is only used by art Wall prefabs (`Agent_Manager.prefab` etc.).

## 3. Character FBX and rig

- Bone list: `ArtSource/Scripts/generate_characters.py` in prod and art has identical `make_bone(...)` calls (lines 158–185). 26 bones in both. Root, Hips, Spine, Chest, Neck, Head, Eye_L/R, Brow_L/R, Accessory_Root/Tip, Shoulder/UpperArm/Forearm/Hand x2, Thigh/Shin/Foot x2. Bone names are unchanged.
- Importer settings in `.fbx.meta` are identical: globalScale 1, useFileUnits 1, animationType 2 (Generic), hasExtraRoot 0, importAnimation 1.
- Clips: 39 (prod) to 77 (art). Only clip entries changed.
- Desk prefab variant root scale 0.03, same in both trees.
- Desk prefab overrides reference FBX object fileIDs (e.g. -8679921383154817045). Those fileIDs are stable as long as bone names are unchanged. Verify in Editor that no override shows as broken.
- Not verified: the bone fileIDs in the regenerated FBX (binary). Confirm in Play that the `Hand_R` attachment still resolves.

## 4. New runtime scripts: call sites and side effects

| Script | Who calls it | Runs on its own? | Notes |
|---|---|---|---|
| `MuseVfxPoolPlayer` | Only `SpatialQualityPresetAdapter` and Wall prefabs | Awake calls EnsureInitialized (allocates pool only). Prefab with no pool array creates ParticleSystem and AudioSource clones. | Low risk while unattached. `PlayEffect` plays audio on every call. |
| `MusePhysicalCardFeedback` | None in prod. Art Wall prefabs only. | Awake/Update only do MPB work. Update exits early at rest. | Approval/rejection methods are a semantic risk (R4). |
| `WallHabitatElevatorVisualController` | Wall prefabs (`WallHabitat_ElevatorCar`, spines) | Yes: Update fires arrival effects when cab travel ends. Awake highlights an indicator (MPB only). | Gate arrival on real elevator events (R8). |
| `WallHabitatManagerDelegationVisualController` | `WallHabitat_TaskPrinter` | Update auto-returns to idle. No self-play. | Gate on real delegation events (R8). |
| `WallHabitatAmbientLifeVisualController` | Wall prefabs | Update exits unless `workActive`. | Low risk. |
| `WallHabitatLargePortalVisualController` | Wall portal prefabs | Awake only. Public methods play `Portal_Open_Expand` / `Portal_Close_Collapse` and motes. | Gate on real portal events. |
| `SpatialQualityPresetAdapter` | Wall prefabs | Awake and Start. Applies a quality tier to referenced controllers. | Low, if only attached to Wall prefabs. |
| `WallHabitatSocketRegistry` | Wall prefabs | Awake calls RebuildLookup. | Low. |
| `WallHabitatAgentSelectionVisual`, `WallHabitatPlacementGhostController` | Wall prefabs | Awake only. | Low. |

Prod production code that instantiates desk or ceiling/floor prefabs with Wall flags off does not reference any new Art script (GUID grep). Its prefabs do not reference them either. The only new-script attachments are in `Art/Prefabs/WallHabitat/`. Wall flags off therefore means these scripts are not instantiated.

Changed existing Art scripts (see R3) are live in MainMRScene and are the real risk.

## 5. Audio: art `.wav` cues vs procedural prod audio

Prod procedural sources (all synthesized at runtime with `AudioClip.Create`):
- `FeedbackController` (`Scripts/Feedback/FeedbackController.cs`): KeyPress, PaperTaken, Attention, Approve, Reject, Changes, Delivered, Error, PaperArrived, CardLanded (enum lines 14–33).
- `WallAudio` (`Scripts/Wall/WallAudio.cs`): DoorSlide, Ding, PrintTick, PortalOpen, PortalClose, Land (lines 30–35). Focus-mode mute and rate limit.
- `StampTool` (`Scripts/Documents/StampTool.cs`): stamp_poof, stamp_thunk, stamp_nope.
- `CeilingPodAudio` (`Scripts/Spatial/CeilingPodAudio.cs`): cue set not read in full.

Mapping:

| Art cue | Closest prod cue | Recommendation |
|---|---|---|
| `SFX_ButtonPress` | `FeedbackController.KeyPress` | Duplicate. Keep procedural. |
| `SFX_PaperPickup` | `FeedbackController.PaperTaken` | Duplicate. Keep procedural. |
| `SFX_StampPress` | `StampTool` thunk and poof | Duplicate. Keep `StampTool` (it is tied to the real stamp state). |
| `SFX_TaskAssign` | none exact (`Delivered` is partial) | New. Use only after a real assign event. |
| `SFX_ElevatorArrival` | `WallAudio` Land/Ding | Overlap. Choose one owner. |
| `SFX_ManagerPrinter` | `WallAudio.PrintTick` | Overlap. Keep `WallAudio`. |
| `SFX_CourierDepart` / `SFX_CourierArrive` | not verified in `CeilingPodAudio` | Likely new. Check `CeilingPodAudio` cue enum before deciding. |
| `SFX_PortalOpen` / `SFX_PortalClose` | `WallAudio.PortalOpen` / `PortalClose` | Duplicate. Keep `WallAudio` (Focus mute, rate limit). |
| `SFX_ProjectComplete` | none exact (`Approve` partial) | New. Gate on real project completion. |
| `SFX_TaskFailure` | `FeedbackController.Error` / `Reject` (partial) | Partial overlap. Keep prod unless the art cue is preferred by owner. |

Rule: `WallAudio` and `FeedbackController` stay the sole owners. Art components should keep `AudioClip` slots empty unless the owner picks a cue. Do not add a second AudioSource path that bypasses Focus-mode mute.

## 6. Wall art hookup (what replaces proxy boxes)

- Prod placement mechanism: `HabitatArtManifest` (`Scripts/Wall/HabitatArtManifest.cs`). Each entry has `socketId`, `prefab`, `localOffset`, `localEuler`, `uniformScale`, `hideProxies` (proxy object names), `enabled`, and `full`/`compact`. `ApplyTo` looks up `view.layout.TryGetSocket(socketId)`, so only **layout** sockets are valid anchors. Colliders on art are disabled.
- Current manifest asset (`Assets/MuseDesk/Wall/HabitatArtManifest.asset`) has zero entries in both trees.
- Layout socket IDs that art prefabs also carry (checked by grep of `m_Name` in `Art/Prefabs/WallHabitat/*`): `ElevatorCarSocket`, `ElevatorStop_Lower`, `ElevatorStop_Mid` (3-tier), `ElevatorStop_Manager`, `TaskPrinterSocket`, `AttentionBeaconSocket`, `ManagerOfficeSocket`, `ResearchCubicleSocket`, `DeveloperCubicleSocket`, `TesterCubicleSocket`, `PortalDepartureSocket`, `CourierDispatchSocket`, `SharedMeetingSocket`, `DeliverySocket`, `ManagerDeskSocket`. These match the required list in `Scripts/Spatial/Core/Wall/HabitatLayout.cs` lines 213–217 and `WallHabitatSocketRegistry.cs` lines 14–35. `WallContact` was not confirmed.
- Prefab-internal sockets (`PathNode_*`, `ElevatorEntry_*`, `ElevatorExit_*`, `AgentStand_*`, `CardOutput_*`, `Handoff_*`) exist in art prefabs and in the registry's `extendedIds`. Prod code does not read them (grep of `Scripts/Spatial/Core/Wall` finds no references). Manifest placement cannot anchor to them.
- Geometry: walker and elevator geometry is computed in `HabitatLayoutBuilder` (`HabitatLayout.cs`), not from prefab transforms. Numeric comparison of prefab socket positions against layout positions was **not** done (needs Editor or YAML transform parsing). Treat as unverified.
- Not in handoff: which proxy names to hide, which entries to enable, and the fit check. The handoff does not specify these. Authoring manifest entries is design judgement and needs an owner decision. Do not enable any entry in this merge.

## 7. Recommended merge order

Do this on a throwaway branch cut from current prod `feature/living-world-polish`, not from 7f87991.

0. Pre-flight: run numstat against merge-base. Stop and report if any of these are non-empty: `backend/`, `ProjectSettings/`, `Packages/`, `Assets/MuseDesk/Scripts/Agents/**`, `Assets/MuseDesk/Scripts/Spatial/**` (non-Art), `Assets/MuseDesk/Scripts/Wall/**`, `Assets/MuseDesk/Scripts/Spatial/Core/Wall/**`, `MainMRScene.unity` (unless R1 is explained).
1. Non-runtime art, no code: `docs/Art/**`, `Screenshots/**`, `ArtSource/**`. Keep `.blend` out of competition branch unless LFS.
2. Audio assets only: `Assets/MuseDesk/Art/Audio/*.wav` (+ .meta). No attachments.
3. VFX prefabs and materials: `Assets/MuseDesk/Art/Prefabs/VFX/**`, new materials/textures.
4. Mask files: `Assets/MuseDesk/Art/Controllers/Mask_UpperBody.mask`, `Mask_HeadGaze.mask`. Weight stays 0.
5. Desk art, after R1–R3 are cleared: `Agent_*.fbx` (+meta), `MuseDesk_Agent.controller`, `Art/Prefabs/Characters/Agent_*.prefab`. Run section 4 Play checks before continuing.
6. New runtime scripts in `Scripts/Art/` (10 files + meta): compile only. Do not attach.
7. Modified live Art scripts (`WorkstationVisualController`, `WorkstationStatusDisplay`, `FactoryEventController`, `TestingStationController`, `SoftwareFactoryVisualController`): merge only after diff review (R3).
8. Editor pipeline (`Editor/ArtPipeline/*`): compile only. Do not run.
9. Wall art (`Art/Prefabs/WallHabitat/**`, `Art/Controllers/WallHabitat/**`, `Scenes/ArtGalleries/**`): merge as inert assets, or hold for a later branch. Keep `HabitatArtManifest.asset` at zero entries.
10. Scenes: `MainMRScene.unity` only if R1 is explained. Gallery scenes are not in build settings; they can merge as inert scenes.

Conflict policy: for any path under `Scripts/Wall/**` or `Scripts/Spatial/Core/Wall/**` where art has no change, keep prod's version. Do not let a `-X theirs` strategy touch them.

## 8. Verification steps

1. **Compile.** Unity recompile with zero errors, and no "Missing script" or "Missing prefab" in the console after opening `MainMRScene`, `Prefabs/Agents/Agent_*`, and `Art/Prefabs/Characters/*`.
2. **EditMode groups** (run all, at minimum these):
   - `AgentInteraction`: AgentInteractionLogic, BubbleProvenance, WalkClearance, AgentWalkPath, AgentWalkCorners, ReactionArbiter, DeskPolishLogic.
   - `Motion`: MotionTests, PresenceTests, ReducedMotionPreferenceTests, TransformOwnershipTests.
   - `Wall` (all 13 files), including the LivingOffice tests.
   - Spatial ceiling group: CeilingPodAudio, CeilingPull, CeilingScanReveal, CeilingVfxMap, CeilingPolish.
   - `Ui/PresentationTests`, `Real/RealTypeDriftTests` (fonts changed in main working tree), `HandOwnershipTests`, `PlacementGuideLogicTests`, `FloorAimTests`, `StampPoofTests`.
   - Backend: no backend change is expected. If numstat shows none, pytest is not required.
3. **Desk Play** (`MainMRScene`, Play mode, Editor only):
   - For each desk agent, log `AgentAnimatorBridge.PlayingStateHash` and the Animator state. Confirm: Idle to Walk via MoveSpeed; `PlayOffer` then `EndOffer`; `PlaySuccess`, `PlayFailure`, `Error` leading to Worried; `PlayHandoffGive` (Exchange_Package) and `PlayHandoffReceive` (Receive_Document); `PlayConfirmedNod` (Nodding); Turn_Left/Right on sharp corners.
   - Confirm no state silently falls back to Idle that should play (HasState false). Add a temporary log, not committed.
   - Compare workstation, factory and testing station visuals against a pre-merge screenshot.
   - Check agent height against the pre-merge height (R11).
4. **Wall Play, flags off** (`WallHabitatController` as configured): no new wall objects spawn; manifest produces no "Art manifest: no socket" warnings; proxies still render.
5. **Wall Play, flags on** (only if owner requests): confirm CloneVisual still scales agents by height; no art colliders active.
6. **Scene hygiene:** `EditorBuildSettings` still lists only `MainMRScene.unity`.
7. **Evidence label:** report as PLAY or AUTO at most. WORN is not claimable until the owner verifies on the headset.

## 9. Keep out of the competition build

- Gallery scenes (`Assets/MuseDesk/Scenes/ArtGalleries/*`). Not in build settings already; exclude from the release branch.
- Editor pipeline scripts (`Editor/ArtPipeline/*` new files). Keep them compiled but never run `BuildAll` against prod.
- All Wall art: `Art/Prefabs/WallHabitat/**`, `Art/Controllers/WallHabitat/**`, `MuseDesk_WallAgent.controller`. Wall flags are off; no manifest entry is enabled.
- New Art runtime components, unattached: `MuseVfxPoolPlayer`, `MusePhysicalCardFeedback`, `SpatialQualityPresetAdapter`, `WallHabitat*`. Keep them out of prefabs.
- Art `.wav` cues until one owner per cue is chosen (section 5). Procedural audio stays.
- `UpperBody` and `HeadGaze` layers: keep weight 0. No code raises them.
- Handoff section 2 suggestions that wire stamp/approve/reject feedback or delegation beats (R4, R8).
- `ArtSource/**` (`.blend`), `Screenshots/**`, `docs/Art/**`: repo bloat. Keep on the art branch, not on the release branch.
- Anything from the art worktree's `Library/` folder. Never commit it.

## 10. Open items (not verifiable without git or Editor)

- Exact diff of `MainMRScene.unity` (R1).
- Exact diffs of the five live Art scripts and `AgentAnimatorBridge` in `Scripts/Art` (R3).
- Whether any art change landed in `Scripts/Wall/**` or `Scripts/Spatial/Core/Wall/**` (R5).
- Base Layer transitions and MoveSpeed thresholds in the regenerated desk controller.
- Prefab socket positions vs layout positions (section 6).
- File sizes (R13) and whether any single file exceeds 10 MB.
- `WallContact` socket presence in art prefabs.
- Whether `CeilingPodAudio` already covers the courier cues.
