using System;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json.Linq;
using MuseDesk.Phase2.Contracts;

/// <summary>
/// §13 automated self-test suite for MuseDesk's pure projection logic.
///
/// Delivered as a script-run harness (run via `unity command script run --file
/// AgentScripts/MuseDeskSelfTest.cs --entry MuseDeskSelfTest.RunAll`) rather than an NUnit
/// EditMode assembly, because the runtime code compiles into the predefined Assembly-CSharp,
/// which an NUnit test asmdef cannot reference without carving the runtime into its own asmdef
/// — a structural refactor we are not doing days before the deadline. This harness exercises the
/// REAL Phase2Reducer / TaskStateSets against the FROZEN v2 contract and reports PASS/FAIL per
/// assertion, so it gives genuine regression coverage with zero project-structure change and can
/// be promoted to NUnit later if the runtime is ever split into an asmdef.
/// </summary>
public static class MuseDeskSelfTest
{
    // ─── tiny assertion harness ───
    static int _pass, _fail;
    static readonly List<string> _failures = new List<string>();

    static void Check(string name, bool cond)
    {
        if (cond) { _pass++; }
        else { _fail++; _failures.Add(name); }
    }

    static void Eq(string name, object expected, object actual)
        => Check($"{name} (expected '{expected}', got '{actual}')", Equals(expected, actual));

    // ─── event builder ───
    static EventEnvelopeV2 Ev(long seq, string type, string eventId = null,
                              string taskId = null, string handoffId = null, string artifactId = null,
                              params (string k, object v)[] payload)
    {
        var p = new JObject();
        if (payload != null)
            foreach (var kv in payload)
                p[kv.k] = kv.v == null ? JValue.CreateNull() : JToken.FromObject(kv.v);
        return new EventEnvelopeV2
        {
            EventId = eventId ?? $"evt-{type}-{seq}",
            Seq = seq,
            Type = type,
            TaskId = taskId,
            HandoffId = handoffId,
            ArtifactId = artifactId,
            Payload = p,
        };
    }

    static Phase2Reducer FreshReducer(long lastSeq = 0, string mode = SourceModes.Demo)
    {
        var r = new Phase2Reducer();
        r.LoadSnapshot(new SnapshotV2
        {
            SessionId = "s-test",
            SourceMode = mode,
            LastSeq = lastSeq,
            Tasks = new List<TaskCardV2>(),
        });
        return r;
    }

    public static string RunAll()
    {
        _pass = 0; _fail = 0; _failures.Clear();

        SnapshotHydration();
        SeqOrdering();
        DuplicateBySeq();
        DuplicateByEventId();
        GapTriggersResync();
        TaskDeltas();
        HandoffDedupAndState();
        ArtifactDedup();
        FocusChange();
        ReplayConverges();
        StateSetHelpers();

        var sb = new StringBuilder();
        sb.AppendLine($"MuseDesk self-test: {_pass} passed, {_fail} failed.");
        foreach (var f in _failures) sb.AppendLine("  FAIL: " + f);
        sb.Append(_fail == 0 ? "RESULT=PASS" : "RESULT=FAIL");
        return sb.ToString();
    }

    // ─── 1. snapshot hydration sets seq + source mode ───
    static void SnapshotHydration()
    {
        var r = new Phase2Reducer();
        bool snapFired = false;
        r.SnapshotLoaded += () => snapFired = true;
        r.LoadSnapshot(new SnapshotV2
        {
            SessionId = "s1", SourceMode = SourceModes.Live, LastSeq = 42,
            Tasks = new List<TaskCardV2> { new TaskCardV2 { TaskId = "t1", Title = "seed", TaskState = TaskStates.Queued } },
        });
        Check("hydration: SnapshotLoaded fired", snapFired);
        Eq("hydration: LastAppliedSeq", 42L, r.LastAppliedSeq);
        Eq("hydration: SessionId", "s1", r.SessionId);
        Eq("hydration: SourceMode", SourceModes.Live, r.SourceMode);
        Eq("hydration: seeded task present", "seed", r.GetTask("t1")?.Title);
        Check("hydration: null snapshot is a no-op", SafeLoadNull(r));
    }

    static bool SafeLoadNull(Phase2Reducer r)
    {
        try { r.LoadSnapshot(null); return true; } catch { return false; }
    }

    // ─── 2. Apply advances only on strictly newer seq ───
    static void SeqOrdering()
    {
        var r = FreshReducer(lastSeq: 0);
        var ok1 = r.Apply(Ev(1, Phase2EventTypes.TaskCreated, taskId: "t1", payload: ("title", "A")));
        Check("ordering: seq 1 applied", ok1);
        Eq("ordering: LastAppliedSeq advanced to 1", 1L, r.LastAppliedSeq);
        var ok2 = r.Apply(Ev(2, Phase2EventTypes.TaskStarted, taskId: "t1"));
        Check("ordering: seq 2 applied", ok2);
        Eq("ordering: LastAppliedSeq advanced to 2", 2L, r.LastAppliedSeq);
    }

    // ─── 3. duplicate / already-applied seq dropped ───
    static void DuplicateBySeq()
    {
        var r = FreshReducer(lastSeq: 5);
        var dup = r.Apply(Ev(5, Phase2EventTypes.TaskCreated, taskId: "t1"));
        Check("dup-seq: seq == lastApplied dropped", !dup);
        var old = r.Apply(Ev(3, Phase2EventTypes.TaskCreated, taskId: "t1"));
        Check("dup-seq: seq < lastApplied dropped", !old);
        Eq("dup-seq: seq unchanged", 5L, r.LastAppliedSeq);
    }

    // ─── 4. duplicate event_id dropped even at a valid next seq ───
    static void DuplicateByEventId()
    {
        var r = FreshReducer(lastSeq: 0);
        r.Apply(Ev(1, Phase2EventTypes.TaskCreated, eventId: "E1", taskId: "t1"));
        // Same id arriving at seq 2 must be dropped and NOT advance state.
        var dup = r.Apply(Ev(2, Phase2EventTypes.TaskStarted, eventId: "E1", taskId: "t1"));
        Check("dup-id: repeated event_id dropped", !dup);
        Eq("dup-id: seq did not advance past the dup", 1L, r.LastAppliedSeq);
    }

    // ─── 5. a seq gap applies nothing and raises ResyncNeeded ───
    static void GapTriggersResync()
    {
        var r = FreshReducer(lastSeq: 1);
        long expected = -1, got = -1; int resyncs = 0;
        r.ResyncNeeded += (e, g) => { resyncs++; expected = e; got = g; };
        var applied = r.Apply(Ev(5, Phase2EventTypes.TaskCreated, taskId: "t9"));
        Check("gap: event beyond next seq not applied", !applied);
        Eq("gap: ResyncNeeded fired once", 1, resyncs);
        Eq("gap: expected seq reported", 2L, expected);
        Eq("gap: got seq reported", 5L, got);
        Check("gap: no speculative task created", r.GetTask("t9") == null);
        Eq("gap: lastApplied unchanged", 1L, r.LastAppliedSeq);
    }

    // ─── 6. task deltas applied field-by-field ───
    static void TaskDeltas()
    {
        var r = FreshReducer(lastSeq: 0);
        r.Apply(Ev(1, Phase2EventTypes.TaskCreated, taskId: "t1", payload: ("title", "Build auth"), ("task_state", TaskStates.Queued)));
        Eq("delta: title set", "Build auth", r.GetTask("t1")?.Title);
        Eq("delta: initial state queued", TaskStates.Queued, r.GetTask("t1")?.TaskState);

        r.Apply(Ev(2, Phase2EventTypes.TaskAssigned, taskId: "t1", payload: ("target_agent_id", Phase2AgentIds.Developer), ("revision", 3)));
        Eq("delta: assigned agent", Phase2AgentIds.Developer, r.GetTask("t1")?.AgentId);
        Eq("delta: revision adopted", 3, r.GetTask("t1")?.Revision);

        r.Apply(Ev(3, Phase2EventTypes.TaskPriorityChanged, taskId: "t1", payload: ("priority", 2)));
        Eq("delta: priority changed", 2, r.GetTask("t1")?.Priority);

        r.Apply(Ev(4, Phase2EventTypes.TaskPauseRequested, taskId: "t1"));
        Eq("delta: pause_requested", TaskStates.PauseRequested, r.GetTask("t1")?.TaskState);
        r.Apply(Ev(5, Phase2EventTypes.TaskPaused, taskId: "t1"));
        Eq("delta: paused", TaskStates.Paused, r.GetTask("t1")?.TaskState);
        r.Apply(Ev(6, Phase2EventTypes.TaskResumed, taskId: "t1", payload: ("task_state", TaskStates.Running)));
        Eq("delta: resumed->running", TaskStates.Running, r.GetTask("t1")?.TaskState);
        r.Apply(Ev(7, Phase2EventTypes.TaskCompleted, taskId: "t1"));
        Eq("delta: completed", TaskStates.Completed, r.GetTask("t1")?.TaskState);

        // A delta for an unseen task seeds a card (task.created may have been missed pre-snapshot).
        r.Apply(Ev(8, Phase2EventTypes.TaskAssigned, taskId: "t2", payload: ("target_agent_id", Phase2AgentIds.Tester)));
        Check("delta: unseen task seeded", r.GetTask("t2") != null);
        Eq("delta: seeded task agent", Phase2AgentIds.Tester, r.GetTask("t2")?.AgentId);
    }

    // ─── 7. handoff created is deduped; received/failed transition state ───
    static void HandoffDedupAndState()
    {
        var r = FreshReducer(lastSeq: 0);
        int createdFires = 0;
        r.HandoffCreated += _ => createdFires++;
        r.Apply(Ev(1, Phase2EventTypes.HandoffCreated, eventId: "h-a", handoffId: "H1",
            payload: ("from_agent_id", Phase2AgentIds.Researcher), ("to_agent_id", Phase2AgentIds.Developer), ("kind", HandoffKinds.Blueprint)));
        // A second handoff.created for the SAME handoff_id (different event_id, next seq) must not
        // fire a second courier cue.
        r.Apply(Ev(2, Phase2EventTypes.HandoffCreated, eventId: "h-b", handoffId: "H1",
            payload: ("from_agent_id", Phase2AgentIds.Researcher), ("to_agent_id", Phase2AgentIds.Developer)));
        Eq("handoff: created fired exactly once per id", 1, createdFires);

        string state = null;
        r.HandoffChanged += h => state = h.State;
        r.Apply(Ev(3, Phase2EventTypes.HandoffReceived, handoffId: "H1"));
        Eq("handoff: received state", HandoffStates.Received, state);
        r.Apply(Ev(4, Phase2EventTypes.HandoffFailed, handoffId: "H1"));
        Eq("handoff: failed state", HandoffStates.Failed, state);
    }

    // ─── 8. artifact created is deduped ───
    static void ArtifactDedup()
    {
        var r = FreshReducer(lastSeq: 0);
        int fires = 0;
        r.ArtifactAdded += _ => fires++;
        r.Apply(Ev(1, Phase2EventTypes.ArtifactCreated, eventId: "a1", artifactId: "A1", payload: ("kind", ArtifactKinds.CodeDiff)));
        r.Apply(Ev(2, Phase2EventTypes.ArtifactCreated, eventId: "a2", artifactId: "A1", payload: ("kind", ArtifactKinds.CodeDiff)));
        Eq("artifact: added fired once per id", 1, fires);
        Eq("artifact: kind captured", ArtifactKinds.CodeDiff, (r.Artifacts.TryGetValue("A1", out var a) ? a.Kind : null));
    }

    // ─── 9. focus change updates projection + fires ───
    static void FocusChange()
    {
        var r = FreshReducer(lastSeq: 0);
        string mode = null;
        r.FocusChanged += f => mode = f.Mode;
        r.Apply(Ev(1, Phase2EventTypes.FocusChanged, payload: ("mode", FocusModes.Focus), ("revision", 1)));
        Eq("focus: event fired with mode", FocusModes.Focus, mode);
        Eq("focus: projection updated", FocusModes.Focus, r.Focus?.Mode);
    }

    // ─── 10. replaying the same ordered stream converges (idempotent) ───
    static void ReplayConverges()
    {
        var stream = new List<EventEnvelopeV2>
        {
            Ev(1, Phase2EventTypes.TaskCreated, taskId: "t1", payload: ("title", "X"), ("task_state", TaskStates.Queued)),
            Ev(2, Phase2EventTypes.TaskAssigned, taskId: "t1", payload: ("target_agent_id", Phase2AgentIds.Developer)),
            Ev(3, Phase2EventTypes.TaskCompleted, taskId: "t1"),
        };
        var a = FreshReducer(0); foreach (var e in stream) a.Apply(e);
        var b = FreshReducer(0); foreach (var e in stream) b.Apply(e);
        // Replaying the stream again into b must change nothing (all seq <= lastApplied).
        foreach (var e in stream) b.Apply(e);
        Eq("replay: same final seq", a.LastAppliedSeq, b.LastAppliedSeq);
        Eq("replay: same final task state", a.GetTask("t1")?.TaskState, b.GetTask("t1")?.TaskState);
        Eq("replay: same final agent", a.GetTask("t1")?.AgentId, b.GetTask("t1")?.AgentId);
    }

    // ─── 11. terminal / safe-assign helpers ───
    static void StateSetHelpers()
    {
        Check("helper: completed is terminal", TaskStateSets.IsTerminal(TaskStates.Completed));
        Check("helper: failed is terminal", TaskStateSets.IsTerminal(TaskStates.Failed));
        Check("helper: cancelled is terminal", TaskStateSets.IsTerminal(TaskStates.Cancelled));
        Check("helper: running is NOT terminal", !TaskStateSets.IsTerminal(TaskStates.Running));
        Check("helper: queued is a safe assign checkpoint", TaskStateSets.IsSafeAssignCheckpoint(TaskStates.Queued));
        Check("helper: paused is a safe assign checkpoint", TaskStateSets.IsSafeAssignCheckpoint(TaskStates.Paused));
        Check("helper: blocked is a safe assign checkpoint", TaskStateSets.IsSafeAssignCheckpoint(TaskStates.Blocked));
        Check("helper: running is NOT a safe assign checkpoint", !TaskStateSets.IsSafeAssignCheckpoint(TaskStates.Running));
        Check("helper: IsRunning true for running", TaskStateSets.IsRunning(TaskStates.Running));
    }
}
