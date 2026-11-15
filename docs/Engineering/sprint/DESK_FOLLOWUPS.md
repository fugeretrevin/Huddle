# Desk follow-ups: truthful Listening, perf, idempotency

Status: written, NOT compiled, NOT run (no Editor access).

## 1. Per-agent Listening
- `AgentAnimatorBridge` subscribes to `VoiceAddressing.Shared.ListeningStarted/Ended` (target = `VoiceIntentClassifier.FromId(view.agentId)`); adopts current state in OnEnable.
- Listening clip is the base clip only while `ListeningGate.Show(...)`: addressed with mic open, and not walking, not holding the paper (offer), not in a one-shot, not in Error. When a one-shot or walk ends, the existing GoBase picks Listening again if still addressed; on end it returns to the task-state clip.
- Pure gate: `Scripts/Agents/Core/ListeningGate.cs`; tests `Tests/EditMode/AgentInteraction/ListeningGateTests.cs`.
- Only source of the clip is the voice events. Nothing else triggers it.

## 2. Perf
- `TaskCardView.Render`: already change-checks every text write (`SetText`) and uses MaterialPropertyBlock at this base (8c2ca85); nothing to change.
- `AgentView.SetState`: label string rebuilt only when state or display name changed.
- `GrabReadout.Update`: returns immediately unless `Debug.isDebugBuild` (no per-0.5 s string building or `MDGRAB` logcat in release). Scene untouched.

## 3. Idempotency audit
Already on `CommandLedger`: pad/chip/drag assign, pause, resume, priority (`StableKey` + lock in `RequestAssignAsync`/`RunCardCommandAsync`), `SessionController.Respond`, focus toggle, diagrams.
Bypassed it, now fixed:
- `TaskDock` Start and the Phase2Bootstrap start path: minted a fresh key per press, so a retry never reused one. Now `StableKey(taskId,"assign",Researcher,"start")` (same action id the drag uses).
- `Phase2Bootstrap.AssignWithoutViewAsync`: direct gateway call with no lock/stable key. Now locks via `BoardPresenter.TryBeginCommand/ReleaseCommand` with the stable key.
- Create (`Phase2Bootstrap.CreateTask`, `CreateExecutableTask`, voice "new task"): fresh key and no lock. `RequestCreateTaskAsync` now locks per title and releases with answered=card!=null; callers use `StableCreateKey(title)`. Executable create uses lock "create-exec".
- `SessionController.StartSampleTask`: fresh key and no lock. Now a ledger lock plus stable key, key dropped only after an answer.
Left as is: `NextIdemKey` still mints with `++` (it is only the mint inside the ledger). `DemoBoardRunBridge` (`++_keyCounter`) is a scripted demo path, not touched.
Test: `Tests/EditMode/DeskCommandKeyTests.cs`.

## Risks
- Compile unverified. `Phase2Bootstrap` finally block builds `Phase2CommandResult<TaskCardV2>.Success(null)` only to signal "answered".
- A create refused while another same-title create is in flight returns null; voice then says "server did not take the task. Nothing was added", which is true.
