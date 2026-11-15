# D. Characters, animation, behaviour and art inventory (read-only audit, 2026-11-15)

Evidence only. Paths relative to `C:\dev\MuseDesk\MuseDesk`. Not run in Editor; no evidence level above AUTO/static is claimed.

## 1. Agent runtime: presentation state machines

| Component | File:line | Role | Driver |
|---|---|---|---|
| AgentView | Assets/MuseDesk/Scripts/Agents/AgentView.cs:167 (`SetState`) | Holds backend state string, colour, label, procedural bob/arm motion, raises `StateChanged` (:49, :185) | Reducer via AgentRegistry.OnAgentChanged (AgentRegistry.cs:140-144) |
| AgentAnimatorBridge (Agents ns) | Scripts/Agents/AgentAnimatorBridge.cs | Maps state to Animator clip state (DIRECT mode: crossfade by state name; PARAMS mode: StateId int + triggers) | AgentView.StateChanged + AgentMovement.IsMoving; one-shots from AgentRegistry / HandoffController / CompletionSequence |
| AgentAnimatorBridge (Art ns, duplicate class name) | Scripts/Art/AgentAnimatorBridge.cs:4,13,76,138 | Older generic bridge, `MuseDesk.Art` namespace, own crossfade | Unclear which prefab uses it (not traced) |
| AgentMovement | Scripts/Agents/AgentMovement.cs | Fixed-duration walk home<->point, SmoothStep along polyline | Real events only (decision requested/resolved, handoff) |
| AgentRegistry | Scripts/Agents/AgentRegistry.cs:51-101 | Researcher walks to deliveryAnchor on DecisionRequested, offers paper, reacts + returns on DecisionResolved | Backend events (reducer) |
| AgentHandoffController | Scripts/Agents/AgentHandoffController.cs:195-255 | Source walks to receiver, Give/Receive clips, walks home | Backend handoff events; `exchangeHold` WaitForSeconds (:239) is timer |
| CompletionSequence | Scripts/Agents/CompletionSequence.cs:302,339,359,410 | Success / Failure / acknowledge nods + look-at on run completion/failure | Backend run state |
| AgentGaze | Scripts/Agents/AgentGaze.cs | Head/neck aim | State + proximity + timers (see section 3) |
| AgentIdleLife | Scripts/Agents/AgentIdleLife.cs:148-210 | Random fidgets on Spine/Chest | Random timer (`fidgetInterval`), not backend |
| AgentPresence / AgentAttention | Scripts/Agents/AgentPresence.cs:211; AgentAttention.cs:786-826 | Idle gestures: look at worker, stretch, look at colleague, present paper | Random timer (`NextIdleAt`), gated by status kind |
| AgentTilt, AgentArmNudge, AgentTalkRig, AgentSpeechBubble, AgentStatusChip | Scripts/Agents/ | Procedural lean / arm nudge / speech UI | Mixed |
| Wall: HabitatWalker | Scripts/Spatial/Core/Wall/HabitatWalker.cs | Wall-mode walker state machine (Home, Leasing, Walking, WaitingElevator, Riding, Dwelling, SoftTransfer, AtRest); `WalkerAnim` Work/Idle/Walk/Wait/Ride/Present (:10) | Stage events from real reducer events; timeouts as fallbacks (:13-20) |
| Wall: WallHabitatView.SetAnim | Scripts/Wall/WallHabitatView.cs:449-465 | WalkerAnim -> Animator state name, crossfade 0.2 s | HabitatWalker.Anim |
| Spatial couriers | Scripts/Spatial/CourierVisual.cs:40-49; RemoteDeliveryMachine.cs:37-46 | Document courier (separate non-humanoid rig/controller) | Delivery machine timers (offerDwell 0.7 s, flight 1.1 s) |

State to clip mapping (bridge, DIRECT): AgentAnimatorBridge.cs:275-289

| Backend state | Clip state | Note |
|---|---|---|
| researching | Reading | |
| coding | Typing | |
| testing | Inspecting | |
| reporting | Talking | |
| waiting | Waiting_For_Input | |
| error | Worried (+ Error one-shot on entry, :345-348) | |
| complete | Idle_Breathing after Task_Completed one-shot (:349-352) | |
| approaching | Idle (walk is driven by IsMoving) | |
| "thinking"/"blocked"/"listening"/"talking"/"failed" | Thinking/Blocked/Listening/Talking/Worried | Code comment (:273-274): backend does not emit these today |
| (offer) | Offer_Document held until EndOffer (:404-420) | |

### Where activity may not be backed by real state

| Item | File:line | Risk |
|---|---|---|
| Idle gestures (look at a worker, stretch, look at colleague) fire on random timers for Idle/Complete agents | AgentAttention.cs:786-826 | Visual only, gated by kind, but "looks at worker" can occur when that worker is merely demo-scripted. Low |
| `PresentPaper` periodically while WaitingForUser | AgentAttention.cs:796-802 | Backed by waiting state; repeat is timed. OK |
| AgentView procedural motion Read/Type/Examine/Hop/Shake and arm sway runs in every state, including with rigged Animator | AgentView.cs:71-165 | AgentView writes `bob`/`armL`/`armR` locals; if the rigged prefab also assigns them, it competes with the Animator (not verified which prefab wires them) |
| Say() starts "speaking" nod for any caller text | AgentAttention.cs:354-369 | Comment says caller must pass true text; no enforcement |
| Wall Manager plays `Talking` whenever idle-at-desk Work anim | WallHabitatView.cs:459 | Manager "talks" continuously at its desk irrespective of any plan activity; Interaction.cs:492 also forces Talking |
| Wall walkers' `Work` anim (Typing/Reading/Inspecting) shows whenever walker is Home, regardless of the agent's real state | WallHabitatView.cs:459, HabitatWalker.cs:178-180 | Idle agent appears to work. Needs check vs stage state; medium |
| Wall visit marker / status light | WallHabitatView.cs:129-139, 429 | Colours from authoritative state (statusColors), OK |

## 2. Animator controllers

Controllers under Assets/MuseDesk (none in Antigravity-named folders):

| Controller | States | Params / transitions | Root motion |
|---|---|---|---|
| Art/Controllers/MuseDesk_Agent.controller | 39 (listed below), default Idle_Breathing (:320) | One float `MoveSpeed` (:744). Only transitions: Idle_Breathing -> Walk_Loop when MoveSpeed > 0.02 (:666-670), Walk_Loop -> Idle_Breathing when < 0.015 (:537-539); duration 0.15 s, no exit time. All other states have zero transitions | All clips come from Agent_Researcher.fbx (guid 9a68e311..., Characters/Agent_Researcher.fbx.meta). No `m_ApplyRootMotion` in any character prefab (grep). Wall code sets applyRootMotion=false (WallHabitatView.cs:149) |
| Art/Controllers/MuseDesk_Factory.controller | Factory station clips (10 per docs/Art/ASSET_MANIFEST.json summary) | not inspected | MainMRScene.unity:148079 and SoftwareFactory_Complete.prefab:328 = 0 |
| Spatial/MuseDesk_AttentionBeacon | Beacon_Idle, Beacon_Incoming, Beacon_Working, Beacon_Suggestion, Beacon_Blocking_Decision, Beacon_Completed, Beacon_Error | none inspected | 0 (prefab:160) |
| Spatial/MuseDesk_CeilingPod | Pod_Docked_Idle, Pod_Begin_Descent, Pod_Descending, Pod_Mechanical_Settle, Pod_Shelf_Open, Pod_Shelf_Close, Pod_Deployed_Idle, Pod_Attention_Ack, Pod_Stabilize, Pod_Begin_Ascent, Pod_Dock_Lock, Pod_Returning_Dock, Pod_Unlock | | 0 |
| Spatial/MuseDesk_CeilingMount | Mount_Docked_Idle, Mount_Unlock, Mount_Lock, Mount_Spool_Spin | | 0 |
| Spatial/MuseDesk_ConversationPerch | Perch_Folded_Idle, Perch_Unfold_Arrive, Perch_Listening_Idle, Perch_Speaking_Pulse, Perch_Fold_Return; params Deployed, UnfoldArrive, FoldReturn, SpeakPulse | has param-driven transitions | 0 |
| Spatial/MuseDesk_DocumentCourier | Courier_Docked_Idle, Courier_Load_Document, Courier_Fly_Loop, Courier_Arrive_Present, Courier_Waiting_Idle, Courier_Return_Dock | | 0 |
| Spatial/MuseDesk_FloorCourierStation | Station_Idle, Station_Load_Lift, Station_Dispatch_Release, Station_Dock_Receive | | 0 |

### MuseDesk_Agent: ALL 39 state/clip names (state name == clip name)

Idle_Breathing, Idle_Fidget, Happy_Idle, Seated_Idle, Walk_Loop, Turn_Left, Turn_Right, Sit_Down, Stand_Up, Typing, Writing, Reading, Inspecting, Using_Workstation, Thinking, Listening, Talking, Nodding, Shaking_Head, Waiting_For_Input, Blocked, Worried, Confused, Waving, Pointing, Offer_Document, Pick_Up_Document, Hold_Document, Put_Document_Down, Receive_Document, Carry_Package, Exchange_Package, Celebrate_Handoff, Cheer, Excited, Task_Completed, Success, Failure, Error.

Verification of the Wall habitat names (WallHabitatView.cs:27-29): Idle_Breathing (:876), Walk_Loop (:849), Typing (:955), Reading (:354), Inspecting (:823), Waiting_For_Input (:406), Offer_Document (:1163), Talking (:1007) - all 8 exist in MuseDesk_Agent.controller. Note `S_Wait` is hashed but `SetAnim` maps Wait/Ride/Idle to S_Idle (WallHabitatView.cs:457), so Waiting_For_Input is never played in Wall mode.

Clips never referenced by C# string/hash anywhere in Scripts (grep): Idle_Fidget, Happy_Idle, Seated_Idle, Turn_Left, Turn_Right, Sit_Down, Stand_Up, Writing, Using_Workstation, Nodding, Shaking_Head, Confused, Waving, Pointing, Hold_Document, Pick_Up_Document, Put_Document_Down, Carry_Package, Celebrate_Handoff, Cheer, Excited. Used by bridge only: Listening, Thinking, Blocked, Worried, Task_Completed, Success, Failure, Error, Exchange_Package, Receive_Document (AgentAnimatorBridge.cs:73-90).

### Clips available vs transitions useful for polish

| Need | Existing clip in controller | Status |
|---|---|---|
| Start walk / settle | none dedicated; only 0.15 s MoveSpeed blend to/from Walk_Loop (bridge uses own 0.2 s min crossfade, :58) | Missing start/stop clips; Wall view has no settle |
| Turn in place | Turn_Left, Turn_Right | Present, unused; HabitatWalker rotates at 420 deg/s while moving (HabitatWalker.cs:16,338) |
| Step in/out of elevator | none (Ride uses Idle_Breathing, WallHabitatView.cs:457) | Missing |
| Nod / acknowledge | Nodding, Task_Completed (used by PlayAcknowledge :431-439) | Nodding unused |
| Listen | Listening | Bridge maps "listening" (:285) but nothing sets that state; voice uses AgentGaze head tilt only |
| Blocked | Blocked, Worried, Confused, Shaking_Head | Blocked mapped (:284) but backend does not emit it |
| Completed | Task_Completed, Success, Cheer, Celebrate_Handoff, Excited | Cheer/Excited/Celebrate_Handoff unused |
| Sit | Sit_Down, Seated_Idle, Stand_Up | Unused; no controller transitions at all besides walk |

## 3. Gaze / head look

| Item | Evidence |
|---|---|
| Component | AgentGaze.cs:29-31, `[DefaultExecutionOrder(310)]`, LateUpdate (:404) after Animator |
| Writes | Neck and Head via `BoneRotationLayer` (:82), additive world-delta applied on top of animated pose, released when output ~0 (:465-470) |
| Clamps | Yaw 55 deg, pitch 30 deg serialized (:48-49), hard caps 60/30 (:102-103), behind-agent (>120 deg yaw) lets go (:428); expression roll/pitch/yaw clamped 14/12/15 (:163) |
| Smoothing | Exponential, time constant 0.2 s (:51, :442-443); neck share 0.35 (:55) |
| Decision rate | Target re-chosen every 0.1 s (:411-415) |
| Priorities | listen/speak > explicit attention > walking (none) > away at user/paper > fingertip within 0.15 m > colleague > waiting/error > desk glance every 6-14 s (:292-348) |
| Other bone writers (same LateUpdate, all through BoneRotationLayer, ordered) | AgentIdleLife 300 (Spine/Chest), AgentGaze 310 (Neck/Head), AgentTilt 400, AgentArmNudge 410, AgentPresence 420 (Spine/Chest/arms; head expression is routed through AgentGaze.SetExpression :168), AgentAttention 430 (:49) |
| Potential conflict | AgentIdleLife and AgentPresence both own Spine/Chest layers (IdleLife.cs:60, Presence.cs:88) - stacked additive, not exclusive. AgentView writes `bob`, `armL`, `armR` in Update (AgentView.cs:111-129, 162-164) which, if present on the rigged prefab, is before Animator evaluation and overwritten, but not both known |
| Wall habitat | No gaze: WallHabitatView only crossfades clips (:464); avatars are cloned visuals (CloneVisual :311) and AgentGaze/IdleLife are not part of the Wall path (not verified on the clone: Instantiate of agentSources may copy these components) |
| Hand look | Only fingertip proximity (HandFingertips) |

## 4. Walking outside the Wall habitat

| Mode / flow | Walks? | Path | Collision/clearance | File:line |
|---|---|---|---|---|
| Desk: researcher to user with paper ("approach with exclamation": SetAlert + Offer) | Yes | `AgentMovement.DeliverTo(deliveryAnchor, null, viaExit:true)` through `exitWaypoints` (authored Transforms); fallback `DeliverInFrontOfUser(0.4)` straight line at agent height | None. No physics query anywhere in Agents/Spatial/Wall/Art scripts (grep Physics./Raycast/NavMesh = no matches). Waypoints authored by hand | AgentRegistry.cs:51-75; AgentMovement.cs:64-87,107-114 |
| Desk: handoff Researcher->Developer->Tester | Yes | Straight line to a point `stopDistance` from receiver (+ backOffset) | None; only a 6 s guard on arrival | AgentHandoffController.cs:208-251 |
| Floor / Ceiling modes | Agents stay at their stations; the office platform/pod moves via swept-volume route planner (FloorRoutePlanner.cs:38; CeilingPlacementEvaluator.SweepRoute) | Planned, volume-swept against room geometry | Yes, for the moving workspace (not for agent feet) | FloorRoutePlanner.cs:5-37 |
| Remote delivery (courier flies document to user) | Courier, not agents | Timed states (0.45 s platform, 1.1 s flight) | Timeout fallback only | RemoteDeliveryMachine.cs:37-46 |
| Wall habitat | Yes | Nav graph nodes + segment leasing, elevator machine | Yes: reservations, lease timeout 6 s, elevator timeout 20 s, trip timeout 45 s, soft transfer (fade + rehome) | HabitatWalker.cs:13-20,264-284 |

Foot-skating risk:

| Where | Evidence | Risk |
|---|---|---|
| Desk walks use fixed duration, SmoothStep speed profile | AgentMovement.cs:17,129-130 (`travelSeconds` 1.6 s, `SmoothStep`) | Ground speed ramps 0 -> peak -> 0; bridge scales Animator speed to measured PlanarSpeed but clamps 0.7-1.4x of 0.15 m/s (AgentAnimatorBridge.cs:49-54,180-192); at start/end of every walk speed is below the 0.7x floor so feet slide. Unmeasured `walkCycleMeters` = 0.15 is an estimate ("Tune on the headset", :49-50) |
| Unrouted walk speed varies with distance | `travelSeconds` fixed regardless of length; routed walks capped at `maxRoutedSpeed` 0.25 (AgentMovement.cs:25-27,121) | Long straight handoffs exceed the clamp |
| Facing | Slerp toward segment direction at rate 6-9 per second (:153-155); no Turn_Left/Right clip | Root turns while translating; start-walk pops |
| Wall: constant speed 0.14 m/s (HabitatWalker.cs:15) vs clip estimate 0.15 m/s | WallHabitatView never sets `animator.speed`; avatar scale differs from 0.31 m desk character, so cycle length mismatch possible | Low-medium; instant stop at node (HabitatWalker.cs:341 snaps to target), crossfade 0.2 s |
| AgentView.Walk bounce (arm swing + bob) | AgentView.cs:77,115-124 | Procedural walk on top of clip walk if `bob`/arms wired |

## 5. Art inventory

Sources: docs/Art/ASSET_MANIFEST.json (61 FBX; 3 chars; 39 shared clips; 3 workstations; 54 props/diagrams; 10 factory clips; 73,074 tris), docs/Art/SPATIAL_WORKSPACE_ASSET_MANIFEST.md, docs/Art/FINAL_ASSET_REFINEMENT_MANIFEST.json. Assets/MuseDesk/Wall/HabitatArtManifest.asset is empty: `artVersion: proxy-0`, `entries: []` (asset:15-16).

Main tree (branch feature/wall-habitat). Status column: used = referenced by runtime scripts/scene in main tree (not fully traced for every item; "present" = file exists).

| Item | Path (Assets/MuseDesk/...) | Status |
|---|---|---|
| Character FBX x3 (Researcher, Developer, Tester) | Art/Models/Characters/Agent_*.fbx | Present & used (prefabs in Art/Prefabs/Characters/) |
| Character prefabs x3 | Art/Prefabs/Characters/Agent_*.prefab | Present & used (agent controller bridge) |
| Manager model | none in main tree. Wall code comment: "Placeholder Manager (until Antigravity's model): Developer mesh with a lavender cap and brass badge" (Scripts/Wall/WallHabitatView.cs:374); agentSources[1] reused for r==3 (:142) | Absent in main; exists only in worktree (below) |
| Workstations x3 | Art/Models/Workstations/*.fbx | Present & used |
| Factory | Art/Models/Factory/SoftwareFactory_Complete.fbx (+ Polished variant in ArtSource) | Present |
| Task board | Art/Models/TaskBoard/ (PriorityBadge, StatusMarker, OwnerIcon, DependencyLink); FinalRefinement TaskBoard_KanbanFrame_V2, TaskCard_V2_Polished, TaskDock_Plate in ArtSource | Present (usage not traced) |
| Paper / documents | Art/Models/Documents/ (ApprovalPaper, ResearchDocument, TestReport, CodeReviewSheet, BlueprintSheet, RolledBlueprint, Folder, Envelope, Clipboard, SignaturePen, Inbox/Outbox tray, Archive, PackageBox, ArtifactToken) | Present; paper used via DecisionPaper |
| Stamps | Models/Documents/Doc_Stamp_Approve.fbx, Doc_Stamp_Reject.fbx, Doc_StampRestPad.fbx, Doc_ApprovalStamp; prefabs Prefabs/VisualPolish/Doc_Stamp_*_Polished.prefab, Doc_StampRestPad.prefab, Documents/Doc_ApprovalStamp.prefab | Present (use not traced) |
| Receipt card | Models/Documents/Doc_ReceiptCard.fbx; Prefabs/FinalRefinement/Doc_ReceiptCard_Polished.prefab | Present |
| Nameplates / plaques / signs | Art/Models/Plates/Plate_AgentNameplate, Plate_ModePlaque, Plate_UtilitySign (.fbx) | Present; AgentNameplate.cs exists (Scripts/Workspace) |
| Diagrams x14 | Art/Models/Diagrams/ | Present |
| Decorations x16 | Art/Models/Decorations/ | Present |
| Document courier | Models/Spatial/DocumentCourier.fbx, Prefabs/Spatial/DocumentCourier.prefab, controller MuseDesk_DocumentCourier; script CourierVisual.cs | Present & used |
| Floor courier station / floor office / compact | Models/Spatial/FloorCourierStation, FloorOfficePlatform(+_Compact); Prefabs/Spatial/Spatial_FloorOffice_Complete, Spatial_FloorCourierSystem | Present & used |
| Ceiling pod / mount / inspection shelf | Models/Spatial/SuspendedOfficePod, CeilingMount, CeilingInspectionShelf; Prefabs Spatial_CeilingPod_Complete, Spatial_CeilingWorkspaceAssembly | Present & used |
| Attention beacon | Models/Spatial/WorkspaceAttentionBeacon.fbx; prefab; controller MuseDesk_AttentionBeacon | Present & used |
| Conversation perch, mailbox, placement ghosts | Models/Spatial/AgentConversationPerch, SpatialMailbox, PlacementGhostPreview_Floor/_Ceiling | Present |
| Wall habitat modules (backplate, cubicle bay, hallway, planning nook, manager lookout, elevator spine/car, task printer, task cards x4, desk visit perch, selection halo, large agent portal, placement ghost) | not in main tree (no Art/Models/WallHabitat, no Art/Prefabs/WallHabitat; no WallHabitat* scripts in Scripts/Art) | Absent in main |
| Portal VFX cone (Mat_Spatial_PortalCone, SpatialPortalCone.shader, VFX_PortalConeMesh) | not in main tree | Absent in main (worktree only) |
| Manager animation | no Manager-specific clips; shared 39-clip set only | Absent |
| Elevator animation | none in MuseDesk_Agent controller; elevator is code-driven (WallHabitatView.cs:395-403 moves door boxes) | Absent (proxy boxes) |

### Worktree `.worktrees/wall-habitat` (branch art/wall-habitat): files not in main tree

New (Glob-only; git state unavailable, so "uncommitted" vs committed on the art branch is not determinable):

| Path (under .worktrees/wall-habitat/Assets/MuseDesk) | Kind |
|---|---|
| Art/Models/WallHabitat/Agent_Manager.fbx | Manager character model |
| Art/Models/WallHabitat/WallHabitat_Backplate_Full.fbx, _Backplate_Compact.fbx | Wall backplate |
| WallHabitat_CubicleBay.fbx, _CubicleBay_Compact.fbx | Cubicle bays |
| WallHabitat_HallwaySegment.fbx, _MidPlanningNook.fbx, _ManagerLookout.fbx | Corridor / planning / manager spots |
| WallHabitat_ElevatorSpine_3Tier.fbx, _ElevatorSpine_2Tier.fbx, _ElevatorCar.fbx | Elevator |
| WallHabitat_TaskPrinter.fbx, _TaskCard_Researcher/Developer/Tester/Manager.fbx | Task printer and cards |
| WallHabitat_DeskVisitPerch.fbx, _SelectionHalo.fbx, _LargeAgentPortal.fbx, _PlacementGhost.fbx | Visit perch, select ring, portal, ghost |
| Art/Materials/Mat_Spatial_PortalCone.mat, Mat_Spatial_PortalCone_Lite.mat | Portal materials |
| Art/Models/Spatial/VFX_PortalConeMesh.asset | Portal VFX mesh |
| Art/Shaders/SpatialPortalCone.shader | Portal shader |
| Scripts/Art/WallHabitatSocketRegistry.cs, WallHabitatControllers.cs (WallHabitatElevatorVisualController :29, PlacementGhostController :168, AgentSelectionVisual :231, LargePortalVisualController :307) | Art-side scripts |

Not delivered in worktree (Glob): no Wall docs/manifest (docs/ has no Wall/Habitat files), no Agent_Manager prefab, no Manager animator controller or clips (controllers dir identical to main: Agent, Factory, 6 Spatial), no stamps/receipt/paper/nameplate/beacon Wall variants (these exist already in main). Manager controller would reuse MuseDesk_Agent states.

## 6. Top behaviour issues

| # | Issue | File:line |
|---|---|---|
| 1 | MuseDesk_Agent controller has 39 states but only Idle<->Walk transitions; everything else is script CrossFade. No start-walk / settle / turn clips used, so walks begin/end with a 0.15-0.2 s blend | MuseDesk_Agent.controller:537-539,666-670; AgentAnimatorBridge.cs:58 |
| 2 | Foot-skating: SmoothStep ramp + fixed duration vs. walk-rate clamp 0.7-1.4x of an unmeasured 0.15 m/s | AgentMovement.cs:17,129-130; AgentAnimatorBridge.cs:49-54,180-192 |
| 3 | No collision/clearance for any agent walk outside Wall (straight line / hand-authored waypoints, no physics) | AgentMovement.cs:107-114; AgentHandoffController.cs:208-221 |
| 4 | Wall: Manager is a Developer clone with placeholder cap; plays Talking permanently at desk; no Waiting_For_Input / elevator step clips used | WallHabitatView.cs:142-145,374,457-459 |
| 5 | Wall: Anim `Work` shown whenever the walker is Home, independent of the agent's real task state; Wait/Ride map to Idle | HabitatWalker.cs:178-180; WallHabitatView.cs:454-459 |
| 6 | Wall art (Manager FBX, elevator, portal, 20+ modules) sits only in worktree `.worktrees/wall-habitat`; HabitatArtManifest is empty `proxy-0`, so Wall runs on primitive boxes | HabitatArtManifest.asset:15-16; WallHabitatView.cs:125-136 |
| 7 | Two classes named AgentAnimatorBridge (Agents and Art namespaces) and AgentView procedural bob/arm motion running alongside the Animator | Scripts/Art/AgentAnimatorBridge.cs:4,13; AgentView.cs:71-165 |
