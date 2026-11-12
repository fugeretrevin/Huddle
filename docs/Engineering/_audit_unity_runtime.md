# MuseDesk Unity runtime audit (static, read-only)

Scope: every C# file under `Assets/MuseDesk/Scripts` and `Assets/MuseDesk/Phase2/Runtime`, traced from backend event to visible result. Date: 2026-11-12, against commit `c7b31e8` plus the uncommitted facing flip and self-stamp work.

Method: code reading only. Nothing here was run in the Editor or on the headset. "CONFIRMED" means the defect follows directly from the code (and, where noted, the scene YAML or backend source). "SUSPECTED" means it depends on scene values, asset properties or device behaviour I could not read statically.

Severity uses the mandate's P0 to P4 scale.

---

## 0. Two corrections to current working assumptions

**0.1 The artifact panel cannot have been the "big occlusion on approve".** `ArtifactViewer` opens only on a v2 `artifact.created` or `artifact.page_ready` from the Phase 2 gateway (`ArtifactViewer.cs:63-69`). In the current build that gateway is always `Phase2DemoGateway` (see finding 2), there is no `phase2_demo` fixture in `Resources` (only `offline_demo_fixture.json` exists), and `Phase2DemoFixture.BuiltInDefault()` has an empty timeline (`Phase2DemoGateway.cs:409`). So no v2 artifact event is ever raised and the panel never opens.

The likelier occluder is the agent's auto-stamp prop. `AgentScripts/BuildApprovalStamp.cs:33-37` parents `Doc_ApprovalStamp` to the decision paper at `localScale = 3.0` and `localPosition.z = -0.05` (between the paper and the reader), and `ApprovalStampController` activates it on `decision.resolved`. It was invisible on device until the mesh-bounds fix, so it would have appeared for the first time in the last few builds. `FixFoundation` already nulled and disabled it, so this is probably fixed as a side effect. Moving the artifact panel is still sensible, but it is not the fix for that report.

**0.2 The facing flip may have turned the stationed agents' backs to the user.** Three runtime scripts assume the agent rig's face is local **+Z**: `FaceUserWhileDelivering.cs:32-33` ("Character front is +Z"), `AgentMovement.cs:81-84` (turns +Z along the walk direction), and the drop-zone offset in `BuildBoard.cs:150`. The workstation geometry agrees: each desk sits on the agent's +Z side, so the agent faces its desk. With `FacingUserYaw` now pointing root +Z away from the user, the props and screens face the user but each stationed agent faces away. SUSPECTED, on-head only. If confirmed, the choice is between an over-the-shoulder view (current) and rotating the three `Agent_*` roots 180° at their stations, which turns them away from their desks.

---

## 1. Top 10 confirmed defects, ranked by likelihood of being seen on device

| # | Sev | Defect | Where |
|---|-----|--------|-------|
| 1 | P1 | Self-stamps glide to a stale world position instead of staying on the desk | `StampTool.cs:74-76, 108-113` |
| 2 | P1 | Live token never stored, so the task board is always the demo board, even in live mode | `LiveAgentGateway.cs:26, 30, 51` |
| 3 | P2 | On approve, two controllers drive the researcher at once; the handoff walk is cut off after 1.2 s | `AgentRegistry.cs:74-84`, `AgentHandoffController.cs:62-67, 105-110` |
| 4 | P2 | Developer to tester handoff is silently dropped in the demo | `AgentHandoffController.cs:77` |
| 5 | P2 | Rejecting in the demo shows success: shelf token, PASSED lamp, factory delivery, completion chime | `DemoAgentGateway.cs:124-131` |
| 6 | P2 | "+ New task" creates a blank, P0 card in the demo | `Phase2DemoGateway.cs:165`, `Phase2Reducer.cs:183-184, 209-214` |
| 7 | P3 | Agents never restore their station rotation after a delivery or handoff | `AgentMovement.cs:33-37, 86-91` |
| 8 | P2 | A human approval plays the factory's "test success" stage before any test ran | `FactoryEventController.cs:84-90` |
| 9 | P2 | A failed respond leaves the paper stuck on "Saving your decision…", and the stamp can never retry | `SessionController.cs:177-187`, `DecisionPanel.cs:93`, `StampTool.cs:59, 137` |
| 10 | P2 | Artifact viewer is 1-based; the backend and the demo gateway are 0-based | `ArtifactViewer.cs:75, 79-80, 96, 110` |

### 1. Stamps do not follow the workspace (P1, CONFIRMED)
`Awake` stores `_homePos` and `_homeRot` in world space. The stamps are children of `WorkspaceRoot`, which `WorkspacePlacement` moves and rotates after head tracking settles, then keeps re-aiming for 2.5 s. Every frame a stamp is not held, `Update` lerps it back to the world pose captured at load, which is the authored position near the tracking origin. The stamps will sit at roughly (±0.22, 1.02, 0.18) in tracking space, not on the desk in front of the user.

Fix: store the home in parent space.
```csharp
Transform _homeParent; Vector3 _homeLocalPos; Quaternion _homeLocalRot;
// Awake
_homeParent = transform.parent;
_homeLocalPos = transform.localPosition;
_homeLocalRot = transform.localRotation;
// Update, not-held branch
Vector3 hp = _homeParent ? _homeParent.TransformPoint(_homeLocalPos) : _homeLocalPos;
Quaternion hr = _homeParent ? _homeParent.rotation * _homeLocalRot : _homeLocalRot;
transform.SetPositionAndRotation(Vector3.Lerp(transform.position, hp, k),
                                 Quaternion.Slerp(transform.rotation, hr, k));
```

### 2. Board never goes live (P1, CONFIRMED)
`LiveAgentGateway` declares `_token` and exposes it as `Token`, but `StartSessionAsync` only sets `_rest.BearerToken`. `SessionController.LiveToken` is therefore always null, and `Phase2Bootstrap.ResolveLiveGatewayAsync` (`Phase2Bootstrap.cs:168-174`) logs "v1 session is live but exposes no id/token; using demo board" and falls back. In the planned live test the cards will read SCRIPTED DEMO and no assign, pause, priority, create, focus, diagram or artifact call will reach `/v2`.

Fix: add `_token = created.Token;` after `LiveAgentGateway.cs:51`.

### 3. Handoff and return-home fight over the researcher (P2, CONFIRMED)
`decision.resolved` (approved) triggers both `AgentHandoffController.TryHandoff("researcher","developer")`, which starts a 1.6 s walk, and `AgentRegistry.ReactAndReturn`, which calls `ReturnHome()` after 1.2 s. The return overrides the walk at about 75% of the way. The handoff routine then waits for `IsMoving` to clear, plays give and receive with the researcher back at its own desk, and moves the token into the developer's hand from across the workspace.

Fix: one owner. Add `public static bool AnyBusy` to `AgentHandoffController` (set alongside `_busy`), and in `AgentRegistry.cs:83` skip the return while a handoff owns the agent:
```csharp
if (AgentHandoffController.AnyBusy) yield break;   // the handoff returns the courier home
if (_researcherMove != null) _researcherMove.ReturnHome();
```

### 4. Second handoff dropped (P2, CONFIRMED)
`TryHandoff` returns when `_busy`. In the demo, `artifact.created` arrives 2.4 s after the approval (two 1.2 s steps in `DemoAgentGateway.PlayBranchAsync`), while the first handoff takes at least 4.2 s (walk, 1.0 s hold, walk home). The developer to tester handoff therefore never plays offline.

Fix: queue instead of dropping.
```csharp
readonly Queue<(string src, string dst)> _queue = new Queue<(string, string)>();
void TryHandoff(string s, string t) {
    if (FocusModeController.Active) return;
    _queue.Enqueue((s, t));
    if (!_busy) StartCoroutine(Drain());
}
IEnumerator Drain() {
    _busy = AnyBusy = true;
    while (_queue.Count > 0) {
        var (s, t) = _queue.Dequeue();
        if (_byId.TryGetValue(s, out var a) && a && _byId.TryGetValue(t, out var b) && b)
            yield return HandoffRoutine(a, b);
    }
    _busy = AnyBusy = false;
}
```
Remove the `_busy` writes inside `HandoffRoutine`, and add `_busy = AnyBusy = false; _queue.Clear();` to `OnDisable` (today a disable mid-routine latches `_busy` true forever and leaks the `HandoffDest` object).

### 5. Reject looks like success in the demo (P2, CONFIRMED)
The non-approve branch of `PlayBranchAsync` still emits `task.completed`. Every consumer treats that as a real completion: `ProjectShelfController.cs:57` places a token and counts toward the trophy, `TestingStationController.cs:65` pulses PASSED, `FactoryEventController.cs:70` plays completed delivery, `FeedbackController.cs:58` plays the completion chime, and the diagnostics line reads "DONE". This is a fabricated success shown in the judged build.

Fix: in the `else` branch at `DemoAgentGateway.cs:124-131`, drop the `task.completed` emit and keep only the developer status change. If `DemoDirector.loop` needs a cycle-end signal, have it also listen to `DecisionResolved` for non-approved states.

### 6. Blank card from "+ New task" (P2, CONFIRMED)
`Phase2DemoGateway.CreateTaskAsync` emits `task.created` with a payload of only `source_mode`. The reducer seeds a card and reads the title only from the payload, so the card has no title. Priority is read only on `task.priority_changed`, so it shows P0. `BoardPresenter.RequestCreateTaskAsync` discards the returned canonical card. The live backend does send title, state, priority and revision (`backend/app/phase2/task_graph.py:56-62`), so live gets the title but still shows the wrong priority.

Fix:
- `Phase2DemoGateway.cs:165`: `EmitTask(Phase2EventTypes.TaskCreated, card, new JObject { ["title"] = card.Title, ["task_state"] = card.TaskState, ["priority"] = card.Priority, ["revision"] = card.Revision, ["source_mode"] = SourceModes.Demo }, agentId: card.AgentId);`
- `Phase2Reducer.cs`, in the `TaskCreated`/`TaskStarted` case: `if (e.TryPayloadInt("priority", out var cp)) t.Priority = cp;`

### 7. Station rotation never restored (P3, CONFIRMED)
`AgentMovement` records only `_homeLocal` (position). During a walk it turns +Z along the travel direction; on arriving home it resets position and stops. `FaceUserWhileDelivering` is inactive at home. After the first delivery the researcher stays turned along its walk-home direction, roughly 60° off its desk with the new placement numbers, for the rest of the session.

Fix:
```csharp
Quaternion _homeLocalRot;                                   // Awake: = transform.localRotation
// Update, arrival block (line 90)
if (_returning) {
    if (_parent != null) { transform.localPosition = _homeLocal; transform.localRotation = _homeLocalRot; }
    AtHome = true;
}
```
For a smooth finish, when `_returning && k > 0.75f` slerp toward `_parent.rotation * _homeLocalRot` instead of the travel direction.

### 8. Approval shown as a test pass (P2, CONFIRMED)
`OnDecisionResolved` maps approved to `factory.PlayTestSuccess()`. A human approving a research recommendation is not a test result, and at that point nothing has been built. Two of the ten factory hooks, `PlayBlueprintTransfer` and `PlayDevelopmentComplete`, are never called.

Fix: `FactoryEventController.cs:88` should call `factory.PlayBlueprintTransfer()` for approved. Drive `PlayTestSuccess` from real test evidence, for example in `OnArtifactCreated` when `tests_failed == 0 && tests_passed > 0`, or immediately before `PlayCompletedDelivery` in `OnTaskCompleted`. Call `PlayDevelopmentComplete` when the developer's agent state becomes `complete`.

### 9. Stuck paper and locked stamp after a failed respond (P2, CONFIRMED)
`DocumentDeliveryController.OnApprove` hides the buttons and shows "Saving your decision…". `SessionController.Respond` catches every exception and only logs. `DecisionPanel.SetError` has no caller anywhere in the project, so after a network failure the paper has no buttons and no error. Separately, `StampTool` sets the static `s_handledDecision` before the call and never clears it, so the same decision can never be stamped again.

A related case: the demo decision id is constant (`demo-auth-approval-1` in the fixture) and `DemoAgentGateway._resolvedAction` is never reset, so a second demo cycle (`DemoDirector.loop`) can be neither stamped nor resolved.

Fix:
- `SessionController`: `public event Action<string, string> RespondFailed;` raised from the `catch` in `Respond`.
- `DocumentDeliveryController.Bind`: subscribe and call `panel.SetError(null)`.
- `StampTool`: clear `s_handledDecision` on `RespondFailed`, or time-box it (allow a retry if the same decision is still pending 4 s later).
- `DemoAgentGateway.StartTaskAsync`: reset `_resolvedAction = null` and suffix the decision id with a cycle counter.

### 10. Artifact paging off by one (P2, CONFIRMED)
The viewer starts at `_page = 1`, clamps to `[1, total]` and labels "1 / N". The backend route defaults to `page = 0` with `Field(ge=0)` (`backend/app/phase2/routes.py:227`, `models.py:218`), and `Phase2DemoGateway.GetArtifactPageAsync` indexes `page * pageSize`. Once finding 2 is fixed, the first page of every artifact is skipped and a one-page artifact shows an empty body.

Fix: go 0-based internally. `Open`: `_page = 0`. `NextPage`: `if (_page < _totalPages - 1)`. `PrevPage`: `if (_page > 0)`. After load: `_page = Mathf.Clamp(resp.Page, 0, _totalPages - 1)`. Label: `$"{_page + 1} / {_totalPages}"`.

---

## 2. Further findings by workflow

### A. Launch
- **P2 CONFIRMED. Snapshot is never projected to the views.** `SessionReducer.LoadSnapshot` raises no events (`SessionReducer.cs:30-39`). `AgentRegistry.TryBind` hydrates from `Snapshot.Agents` on its first frame, before the session exists (`AgentRegistry.cs:86-99`). `SessionController.PendingDecisionId` is only set by a live `decision.requested` event (`SessionController.cs:90`). After an app restart or resume against a live backend with work in flight, agents show idle, no paper is delivered for a decision that is already pending, and approve reports "no decision awaiting". Fix: add `public event Action SnapshotLoaded` to the reducer; in `AdoptGateway` set `PendingDecisionId` from the first snapshot decision whose state is `requested` and raise `DecisionRequested` for it; have `AgentRegistry` re-run its agent loop on `SnapshotLoaded`. Scenario H.
- **P2 CONFIRMED. No disconnect feedback.** `EventDispatcher.ConnectionChanged` and `Phase2EventChannel.ConnectionChanged` have no subscribers. The badge keeps reading "LIVE · Connected to desktop" while the socket is down. Fix: `SessionController` subscribes and calls `diagnostics.SetMode("LIVE · Reconnecting…", endpoint)`. Scenario F.
- **P3 CONFIRMED. Unprotected `async void Start`.** `SessionController.cs:47-51` awaits `InitializeAsync` with no try/catch. A missing or malformed demo fixture throws inside `DemoAgentGateway.StartSessionAsync`, `Initialized` never becomes true, and every dependent system waits silently. Fix: wrap and log.
- **P3 CONFIRMED. Subscription order.** `SessionController` adds its own `DecisionRequested` handler in `AdoptGateway`, after components that bind on frame 1. Any handler that reads `controller.PendingDecisionId` synchronously sees the previous value. No current handler does, but it is a trap. Fix: set `PendingDecisionId` in `OnEvent` before `Reducer.Apply`, or subscribe in `Awake`.
- **P4 CONFIRMED, Editor only.** `Phase2Bootstrap.cs:177` uses `GetComponent<Phase2EventChannel>() ?? AddComponent`. In the Editor a missing component is a fake-null object, so `??` keeps it. Use an explicit `if (x == null)`.
- **P3 note.** `RuntimeSettings.asset` ships `preferOfflineDemo: 0` and `showDiagnosticsOverlay: 1`. The public build attempts a loopback connection on every launch (up to 5 s) and shows the debug overlay.
- `DemoDirector` ordering is sound: it waits for `Initialized`, stands down when `IsLive`, and does not double-start.

### B. Task assignment
- **P2 CONFIRMED. Assign has no visible outcome in the demo.** `Phase2DemoGateway.AssignAsync` only sets `AgentId`; the task stays queued, so `LayoutAllRegions` puts the card straight back in Backlog (`BoardPresenter.cs:171-200`), undoing the snap to the station. The only change is the role label and header tint. The v2 demo board and the v1 demo session are separate worlds, so no agent starts working. Scenario B cannot pass offline. Minimal improvement: after a successful assign, have the demo gateway emit `task.started` with `task_state = running` after one step so the card moves to Working.
- **P3 CONFIRMED. No hover or result feedback.** `BuildBoard.cs:146-158` never assigns `StationDropZone.halo`, so `SetHighlight` is a no-op. `BoardPresenter.AssignResolved` has no subscribers, and `TaskDragController.State` drives no visual. A rejected drop just returns home with no reason.
- **P3 CONFIRMED. Per-frame reflection boxing.** `IsHeld` calls `PropertyInfo.GetValue` every frame for every card (`TaskDragController.cs:56`), as do `StampTool`, `HeldPaperController` and `GrabbableRigidbody`. Each call boxes an int. Fix once: `(Func<int>)Delegate.CreateDelegate(typeof(Func<int>), grabbable, prop.GetGetMethod())`.
- **P4.** `HoldThenIdle` awaits `Task.Delay` with no cancellation and touches the card afterwards; a card destroyed in that window throws in a continuation.
- **SUSPECTED, backend cross-check.** `Phase2Reducer.Apply` treats any non-consecutive seq as a gap and applies nothing (`Phase2Reducer.cs:103-108`). If the v2 stream is not dense per session, every event triggers a snapshot refetch and typed events such as `FocusChanged` never fire.
- Pooling, idempotency keys, 409 reconciliation and the no-command-on-invalid-drop rule read correctly.

### C. Approval
- **P2 CONFIRMED. Dismiss race.** `DocumentDeliveryController.DismissAfter` (3 s) is not cancelled by a new `decision.requested`. A second decision arriving inside that window has its paper hidden and `_currentDecisionId` nulled, after which its own resolve is ignored. Fix: keep the `Coroutine` handle and stop it in `OnDecisionRequested`.
- **P3 CONFIRMED. Stale mark on the next decision.** `ApprovalStampController` is a component on `DecisionDocument` (scene line 33285). It unsubscribes when the paper is deactivated and rebinds a frame after reactivation, so it misses the `decision.requested` that reactivated it and the previous APPROVED mark is still showing. Fix: `void OnEnable() { HideAll(); }`.
- **P3 CONFIRMED. Delivery is hard-wired to the researcher.** `AgentRegistry.cs:48-56` ignores `e.AgentId`. Correct for today's orchestrators, wrong for any other requester.
- **P3 CONFIRMED. Stamp zone is live on the unread paper.** `StampZone` is a direct child of the document, not in `hideWhileHeld`, so the blank paper in the agent's hand can be stamped before it is taken or read. Decide whether that is intended.
- **P4.** `HeldPaperController` still logs `MDGRAB`. `DocumentDeliveryController.cs:85-90` uses `controller?.` on a `MonoBehaviour`.
- **SUSPECTED, on-head.** `DecisionPanel.SetResolved` and `WorkstationStatusDisplay` use ✓ ✗ ↻, and the newer button labels use ＋ ◀ ▶. If the TMP font asset lacks these they render as boxes.
- Dedup by decision id, the paper's reparent and scale lock, and stamp press detection (held gate, 0.12 s dwell, cooldown) read correctly.

### D. Pipeline, factory, stations, shelf
- **P3 CONFIRMED. Tester lamp stays on Testing.** `TestingStationController.OnAgentChanged` has no branch for `complete`, so after the baseline tests finish the scanner keeps spinning until `task.completed`. Fix: `else if (s.Contains("complete") || s.Contains("idle")) Set(Idle);`.
- **P3 CONFIRMED. Lamps differ only by size.** `WorkstationVisualController.SetLightActive` sets `localScale` to 1.25 or 0.85 and nothing else, and it overwrites whatever scale the lamp was authored with. SUSPECTED consequence: if the `Light_*` nodes are not at unit scale they are mis-sized from the first frame. Fix: cache each lamp's base scale and multiply; also switch emission through a `MaterialPropertyBlock`.
- **P3 CONFIRMED. Real progress text is dropped.** `SessionReducer.ApplyAgentStatus` ignores `progress_message`, and `Models.AgentView` has no field for it. Monitors can only show generic lines such as "Researching…". Fix: add `Progress` to the DTO, pass it through `AgentRegistry` to `AgentView.SetState(state, progress)`, and render it in `WorkstationStatusDisplay`.
- **P3 SUSPECTED. Stage animations restart on every status event.** `FactoryEventController.OnAgentChanged` fires on each `agent.status_changed`, including progress-only updates with an unchanged state. Fix: remember the last state per agent and act on transitions.
- **P3 CONFIRMED. Shelf is not restored.** `ProjectShelfController` counts only live `TaskCompleted` events; nothing reads completed tasks from a snapshot.
- **P3 CONFIRMED. Carried token never rides with the courier.** The token is shown only at the give beat, already in the receiver's hand, and is never hidden again. SUSPECTED: it keeps `localScale = 1` under a rig bone, so its world size depends on that bone's scale.
- Movement guards (6 s) mean an interrupted animation cannot block backend state. Seq-based replay protection covers every reducer-driven controller.

### E. Agent state mapping

| Backend state | `StateId` | Visual | Notes |
|---|---|---|---|
| idle | 0 | idle | |
| researching | 1 | research clip | |
| coding | 2 | typing clip | |
| testing | 3 | testing clip | |
| reporting | 4 | report clip | not emitted by either orchestrator |
| approaching | 5 | approach clip | not emitted; walking is driven by `IsMoving` |
| waiting | 6 | waiting clip | |
| complete | 7 | `TrSuccess` | re-fires on a repeated `complete` |
| error | 8 | `TrError` | |
| thinking, talking, listening, carrying, blocked, failed | 0 | idle | not in the backend `AgentState` enum; no visual exists |

Every state the backend can emit is mapped. Listening and speaking have no visual, and carrying exists only as the give and receive triggers.

### F. Focus, diagrams, persistence, artifacts
- **P3 CONFIRMED. Focus log is erased on exit.** `FocusModeController.SetFocus(false)` clears `_pending` and `_log`, and `RefreshInbox` blanks both labels when inactive. The queued updates can only be read while still in focus mode, so Scenario G's "disable, then review" step cannot pass. Fix: keep the log until the next time focus is enabled.
- **P3 CONFIRMED. Persistence never saves.** `WorkspaceLayoutStore.Save` is reached only through `SetVisible` and `ResetToDefault`, neither of which has a caller. Loading and corrupt-data handling are sound, but nothing is ever written. Fix: save from `OnApplicationPause(true)`.
- **P3 CONFIRMED by builder code. Diagram and inbox text face the wrong way.** `BuildPhase6.cs:47, 113` add `FaceUser` with the default `forward = true` to the inbox readout and the diagram host. TMP reads from -Z, so both are mirrored. The scene values need a check.
- **P3 SUSPECTED. Diagram labels are tiny.** Font size 6 at a compound scale of 0.02 × 0.6 × 0.5 is roughly 3.6 mm tall (`ArchitectureDiagramController.cs:267-271`, `BuildPhase6.cs:112`).
- **P3 CONFIRMED.** `SelectNode` has no caller, so nodes cannot be selected by hand. The fallback example is shown without an "example" label when the backend returns no graph.

### G. Lifecycle hygiene
- Subscribe and unsubscribe are symmetric in every reducer-bound controller. Exceptions: `LiveAgentGateway` and `Phase2LiveGateway` add lambdas to their channels that are never removed (harmless today, one gateway per session), and `SessionController` never detaches from its gateway.
- No `FindObject` or `Camera.main` call sits in a hot path; all are cached or throttled. `GrabReadout` is the exception: every 0.5 s it calls `GameObject.Find`, reads `Camera.main` twice, writes a `Debug.Log`, and overwrites the same TMP that `DiagnosticsOverlay` writes. It is marked for removal in its own header.
- `async void` methods other than `SessionController.Start` all catch.

### Voice (voice build only)
- **P1 CONFIRMED. A negated phrase approves.** `WitVoiceSource.OnResponse` tests the yes list before the no list using substring matching (`WitVoiceSource.cs:86-89, 107-113`). "Don't approve" contains " approve", and "not sure" contains " sure", so both raise `Approve`. Fix: test the no list first, match whole words, and ignore long utterances for approve.
- **P2 CONFIRMED. Spoken answers are discarded.** `VoiceDecisionController.OnAskWhy` passes an empty callback to `AskPending` (`VoiceDecisionController.cs:91-93`). Fix: route the answer to `DecisionPanel.SetAnswer`.
- Partial: questions always go to the researcher, only work while a decision is pending, and `Heard` has no subscriber, so there is no listening or heard indicator. No runtime code calls `StartListening`.

---

## 3. Feature inventory

| Feature | Status | Evidence |
|---|---|---|
| Live or demo selection, mode label | functional | `SessionController.InitializeAsync`; label comes from the adopted gateway |
| Offline scripted session (v1) | functional | `DemoAgentGateway`, every event stamped demo |
| Demo auto-director | functional, untested on device | `DemoDirector`; human gate preserved |
| Agent state to animation | functional | section E |
| Agent walk-up delivery | functional | `AgentRegistry` + `AgentMovement`; rotation defect (7) |
| Paper grab, expand, detach | functional, verified on device earlier | `HeldPaperController` |
| Approve or reject by poke | functional | routes through `SessionController.Respond` |
| Approve or reject by physical stamp | broken as built | finding 1; logic otherwise sound |
| Approval mark | partial | driven by `decision.resolved`; stale on a second decision |
| Respond error handling | missing | `SetError` unused (9) |
| Researcher to developer handoff | mis-integrated | finding 3 |
| Developer to tester handoff | broken in demo | finding 4 |
| Software factory | mis-integrated | approval mapped to test success; 2 of 10 hooks unused |
| Testing station lamps | partial | stuck on Testing; size-only cue |
| Workstation monitors | partial | generic state text; real progress dropped |
| Completed-project shelf | partial | counts rejected runs in demo; not restored |
| Task board rendering | functional | labels now wired; verify legibility on device |
| Task board live commands | broken | finding 2 |
| Drag to assign | partial | command path sound; no visible outcome in demo; no halo |
| Pause, resume, priority pokes | untested | wired; on-head only |
| Create task | broken in demo | finding 6 |
| Artifact viewer | visual only | never opens offline; paging defect (10) |
| Focus mode | partial | gating works; review-after-exit lost |
| Architecture diagram | partial | builds; labels likely mirrored and too small; no selection |
| Layout persistence | visual only | never saves |
| Reconnect and resume | partial | after_seq resume works; no snapshot projection, no disconnect UI |
| Voice decisions | partial, unsafe matching | voice section |
| Agent lean, finger collision | missing or parked | `AgentTilt` unused on rigged agents; `DynamicFingerCollision.GloballyEnabled = false` |

---

## 4. Can only be settled on the headset
1. Whether stationed agents now face away from the user (0.2).
2. Whether the approve-time occluder is gone now that the agent stamp is disabled (0.1).
3. Card pause and priority pokes alongside card grab; stamp press feel and the 6 cm contact radius.
4. Glyph coverage for ✓ ✗ ↻ ＋ ◀ ▶.
5. Diagram and inbox text orientation and size; testing lamp sizes; carried token size.
6. Frame rate with the added per-card interactables.
