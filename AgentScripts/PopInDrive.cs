using System.Reflection;
using System.Text;
using UnityEngine;
using MuseDesk.Phase2.Board;
using MuseDesk.Agents;

/// <summary>Play Mode driver for Polish S9a (pop-in fixes). Reads state and triggers a board resync; no hand needed.</summary>
public static class PopInDrive
{
    static BoardPresenter Board() => Object.FindFirstObjectByType<BoardPresenter>(FindObjectsInactive.Include);

    /// <summary>Cards on the board: id, instance id, local scale and whether each is leaving.</summary>
    public static string Cards()
    {
        var b = Board();
        if (b == null) return "no BoardPresenter";
        var sb = new StringBuilder();
        foreach (var v in b.ActiveViews)
            sb.Append(v.TaskId).Append('#').Append(v.GetEntityId().ToString()).Append(" scale=").Append(v.transform.localScale.x.ToString("F4"))
              .Append(v.IsLeaving ? " LEAVING" : "").Append(" | ");
        return sb.ToString();
    }

    /// <summary>Calls the presenter's RebuildAll (what a snapshot / resync does). Reports which card instances
    /// survived and that nothing else was re-revealed (scales stay 1 on kept cards).</summary>
    public static string Resync()
    {
        var b = Board();
        if (b == null) return "no BoardPresenter";
        string before = Cards();
        typeof(BoardPresenter).GetMethod("RebuildAll", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(b, null);
        return "before: " + before + " ## after (same frame): " + Cards();
    }

    /// <summary>Hides then revives the first card to check cancel-hide: grows back, never deactivated.</summary>
    public static string HideThenRevive()
    {
        var b = Board();
        if (b == null) return "no BoardPresenter";
        TaskCardView first = null;
        foreach (var v in b.ActiveViews) { first = v; break; }
        if (first == null) return "no cards";
        first.BeginHide();
        string a = "leaving=" + first.IsLeaving + " active=" + first.gameObject.activeSelf;
        bool revived = first.CancelHide();
        return a + " revived=" + revived + " leavingAfter=" + first.IsLeaving + " active=" + first.gameObject.activeSelf;
    }

    static System.Collections.Generic.Dictionary<string, MuseDesk.Phase2.Contracts.TaskCardV2> ReducerTasks(BoardPresenter b)
        => (System.Collections.Generic.Dictionary<string, MuseDesk.Phase2.Contracts.TaskCardV2>)
           typeof(MuseDesk.Phase2.Contracts.Phase2Reducer).GetField("_tasks", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(b.Reducer);

    static MuseDesk.Phase2.Contracts.TaskCardV2 _stash;

    /// <summary>Drops the last task from the reducer and rebuilds (a snapshot without that task).</summary>
    public static string RemoveLast()
    {
        var b = Board(); var t = ReducerTasks(b);
        string last = null; foreach (var k in t.Keys) last = k;
        _stash = t[last]; t.Remove(last);
        typeof(BoardPresenter).GetMethod("RebuildAll", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(b, null);
        return "removed " + last + " -> " + Cards() + " ## all: " + AllCards();
    }

    /// <summary>Puts the removed task back (snapshot with it again), possibly while it is still leaving.</summary>
    public static string RestoreStash()
    {
        var b = Board(); var t = ReducerTasks(b);
        if (_stash == null) return "no stash (a script assembly is rebuilt per run: use RemoveRestore)";
        t[_stash.TaskId] = _stash;
        typeof(BoardPresenter).GetMethod("RebuildAll", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(b, null);
        return Cards() + " ## all: " + AllCards();
    }

    /// <summary>Every card object, active or not: scale, active and leaving.</summary>
    public static string AllCards()
    {
        var sb = new StringBuilder();
        foreach (var v in Object.FindObjectsByType<TaskCardView>(FindObjectsInactive.Include))
            sb.Append(v.TaskId).Append(" s=").Append(v.transform.localScale.x.ToString("F3")).Append(v.gameObject.activeSelf ? " on" : " OFF").Append(v.IsLeaving ? " leaving" : "").Append(" | ");
        return sb.ToString();
    }

    /// <summary>Remove the last task and put it back after a few frames (revive while still leaving).</summary>
    public static string RemoveRestoreEarly()
    {
        var b = Board(); var t = ReducerTasks(b);
        string last = null; foreach (var k in t.Keys) last = k;
        var card = t[last];
        var m = typeof(BoardPresenter).GetMethod("RebuildAll", BindingFlags.Instance | BindingFlags.NonPublic);
        t.Remove(last); m.Invoke(b, null);
        string a = AllCards();
        t[last] = card; m.Invoke(b, null);    // same frame: hide had not progressed
        return "after remove: " + a + " ## after restore: " + AllCards();
    }

    /// <summary>Per agent: turning-in-place direction and walking state.</summary>
    public static string Turns()
    {
        var sb = new StringBuilder();
        foreach (var m in Object.FindObjectsByType<AgentMovement>(FindObjectsInactive.Include))
            sb.Append(m.name).Append(": moving=").Append(m.IsMoving).Append(" turn=").Append(m.TurnInPlaceDirection)
              .Append(" speed=").Append(m.PlanarSpeed.ToString("F3")).Append(" | ");
        return sb.ToString();
    }

    /// <summary>Subscribes to every AgentMovement.TurnInPlaceChanged and counts events (read with TurnLog).</summary>
    /// Events are written to the console as "[S9a TURN] name dir t" (a script assembly is rebuilt per run, so no static log).
    public static string HookTurns()
    {
        int n = 0;
        foreach (var m in Object.FindObjectsByType<AgentMovement>(FindObjectsInactive.Include))
        {
            string nm = m.name;
            m.TurnInPlaceChanged += d => Debug.Log("[S9a TURN] " + nm + " dir=" + d + " t=" + Time.time.ToString("F2"));
            n++;
        }
        return "hooked " + n;
    }
}
