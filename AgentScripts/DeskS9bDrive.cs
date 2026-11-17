using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;
using Newtonsoft.Json.Linq;
using MuseDesk.Agents;
using MuseDesk.Feedback;
using MuseDesk.Models;
using MuseDesk.Networking;
using MuseDesk.Phase2.Board;
using MuseDesk.Workspace;

/// <summary>Play Mode driver for Polish S9b (turn clips, confirmed nod, Focus gestures, held-card hide, desk cues). No hands.</summary>
public static class DeskS9bDrive
{
    static readonly string[] ClipNames = { "Idle_Breathing", "Walk_Loop", "Turn_Left", "Turn_Right", "Nodding", "Task_Completed", "Success",
        "Reading", "Typing", "Inspecting", "Talking", "Listening", "Waiting_For_Input", "Offer_Document", "Error", "Worried", "Exchange_Package", "Receive_Document" };

    static string Clip(int hash)
    {
        foreach (var n in ClipNames) if (Animator.StringToHash(n) == hash) return n;
        return "#" + hash;
    }

    /// <summary>Logs every agent's turn-in-place change and the clip the bridge then plays, and every task.completed with the
    /// clip the completing agent then plays. Lines start with [S9b].</summary>
    public static string Hook()
    {
        int n = 0;
        foreach (var m in Object.FindObjectsByType<AgentMovement>(FindObjectsInactive.Include))
        {
            var mv = m; var br = m.GetComponent<AgentAnimatorBridge>();
            m.TurnInPlaceChanged += d => Debug.Log("[S9b] TURN " + mv.name + " dir=" + d + " clip=" + (br != null ? Clip(br.PlayingStateHash) : "?") + " t=" + Time.time.ToString("F2"));
            n++;
        }
        var sc = Object.FindFirstObjectByType<SessionController>();
        if (sc != null)
        {
            sc.Reducer.TaskCompleted += e =>
            {
                string clip = "?";
                foreach (var v in Object.FindObjectsByType<MuseDesk.Agents.AgentView>(FindObjectsInactive.Include))
                    if (v.agentId == e.AgentId) { var b = v.GetComponent<AgentAnimatorBridge>(); if (b != null) clip = Clip(b.PlayingStateHash); }
                Debug.Log("[S9b] TASK.COMPLETED agent=" + e.AgentId + " artifact=" + e.PayloadString("artifact_id") + " clipNow=" + clip + " t=" + Time.time.ToString("F2"));
            };
            sc.Reducer.DecisionRequested += (id, e) => Debug.Log("[S9b] DECISION.REQUESTED t=" + Time.time.ToString("F2"));
        }
        return "hooked " + n + " movements, reducer=" + (sc != null);
    }

    public static string Report()
    {
        var sb = new StringBuilder();
        foreach (var b in Object.FindObjectsByType<AgentAnimatorBridge>(FindObjectsInactive.Include))
        {
            var mv = b.GetComponent<AgentMovement>();
            sb.Append(b.name).Append(": clip=").Append(Clip(b.PlayingStateHash)).Append(" moving=").Append(mv != null && mv.IsMoving)
              .Append(" turn=").Append(mv != null ? mv.TurnInPlaceDirection : 0).Append(" | ");
        }
        var reg = Object.FindFirstObjectByType<AgentRegistry>();
        var at = AgentAttention.Instance;
        var fb = FeedbackController.Instance;
        sb.Append("nodsPlayed=").Append(reg != null ? reg.NodsPlayed : -1)
          .Append(" focus=").Append(FocusModeController.Active)
          .Append(" ambientSuppressed=").Append(at != null ? at.AmbientSuppressed : -1)
          .Append(" handsBusy=").Append(at != null && at.HandsBusy)
          .Append(" cues=").Append(fb != null ? fb.PlayedCount : -1).Append(" last=").Append(fb != null ? fb.LastPlayedCue.ToString() : "?");
        return sb.ToString();
    }

    /// <summary>Arg "on" / "off".</summary>
    public static string Focus(string arg)
    {
        var f = Object.FindFirstObjectByType<FocusModeController>(FindObjectsInactive.Include);
        if (f == null) return "no FocusModeController";
        f.SetFocus(arg == "on");
        return "focus=" + FocusModeController.Active;
    }

    /// <summary>Applies a task.completed WITHOUT an artifact (a bookkeeping completion) for the agent to the live reducer, to prove
    /// the confirmed-completion nod. Driver injection only; nothing is shown to a user as live. Arg: agent id.</summary>
    public static string InjectCompleted(string agentId)
    {
        var sc = Object.FindFirstObjectByType<SessionController>();
        if (sc == null) return "no SessionController";
        var e = new EventEnvelope { Seq = sc.Reducer.LastSeq + 1, Type = EventTypes.TaskCompleted, TaskId = "s9b-drive", AgentId = agentId };
        e.Payload = new JObject { ["task_state"] = "completed", ["result_summary"] = "driver bookkeeping completion" };
        sc.Reducer.Apply(e);
        return "applied seq=" + e.Seq;
    }

    /// <summary>Simulates the user holding the first board card, then drops its task from the reducer (a snapshot without it).
    /// Reports whether the hide waited (HidePending) and the card kept its colliders.</summary>
    public static string HoldAndRemove()
    {
        var b = Object.FindFirstObjectByType<BoardPresenter>(FindObjectsInactive.Include);
        if (b == null) return "no BoardPresenter";
        TaskCardView first = null;
        foreach (var v in b.ActiveViews) { first = v; break; }
        if (first == null) return "no cards";
        first.SetSimulatedHold(true);
        var tasks = (System.Collections.Generic.Dictionary<string, MuseDesk.Phase2.Contracts.TaskCardV2>)
            typeof(MuseDesk.Phase2.Contracts.Phase2Reducer).GetField("_tasks", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(b.Reducer);
        string id = first.TaskId;
        tasks.Remove(id);
        typeof(BoardPresenter).GetMethod("RebuildAll", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(b, null);
        int cols = first.GetComponentsInChildren<Collider>(true).Count(c => c.enabled);
        return "held " + id + " pending=" + first.HidePending + " leaving=" + first.IsLeaving + " taskGone=" + first.TaskGone
             + " collidersOn=" + cols + " active=" + first.gameObject.activeSelf + " drag=" + first.GetComponent<TaskDragController>()?.State;
    }

    /// <summary>The user lets go of the card held by HoldAndRemove.</summary>
    public static string Release()
    {
        foreach (var v in Object.FindObjectsByType<TaskCardView>(FindObjectsInactive.Include))
            if (v.HidePending) { v.SetSimulatedHold(false); return "released " + v.TaskId; }
        return "no card with a pending hide";
    }

    public static string Cards()
    {
        var sb = new StringBuilder();
        foreach (var v in Object.FindObjectsByType<TaskCardView>(FindObjectsInactive.Include))
            sb.Append(v.TaskId).Append(v.gameObject.activeSelf ? " on" : " OFF").Append(" pending=").Append(v.HidePending).Append(" leaving=").Append(v.IsLeaving)
              .Append(" scale=").Append(v.transform.localScale.x.ToString("F3")).Append(" drag=").Append(v.GetComponent<TaskDragController>()?.State).Append(" | ");
        return sb.ToString();
    }
}
