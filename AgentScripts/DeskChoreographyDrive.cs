using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;
using MuseDesk.Agents;

/// <summary>
/// Play Mode driver for the desk choreography layout (AgentScripts/DeskChoreographyLayoutV1): sends the desk agents on
/// each authored walk without a backend event, so every route (and a walk that is re-targeted part-way) can be traced with
/// DeskClearanceDrive TraceFor / TraceReport. Presentation only: it calls the same public walk methods the registry and the
/// handoff controller call, and never touches task or session state.
///
///   scripts\drv.ps1 DeskChoreographyDrive Where                 where each agent is (WorkspaceRoot-local), walking / home / facing
///   scripts\drv.ps1 DeskChoreographyDrive Deliver               the Researcher's delivery walk (exit waypoints -> delivery spot)
///   scripts\drv.ps1 DeskChoreographyDrive Home researcher       send an agent home
///   scripts\drv.ps1 DeskChoreographyDrive Handoff "researcher,developer"   queue a handoff the way a reducer event does
/// </summary>
public static class DeskChoreographyDrive
{
    static Transform Root() =>
        Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault(t => t.name == "WorkspaceRoot");

    static AgentMovement Move(string id)
    {
        var v = Object.FindObjectsByType<AgentView>(FindObjectsInactive.Exclude, FindObjectsSortMode.None).FirstOrDefault(a => a.agentId == id);
        return v != null ? v.GetComponent<AgentMovement>() : null;
    }

    public static string Where()
    {
        if (!Application.isPlaying) return "not playing";
        var root = Root(); if (root == null) return "no WorkspaceRoot";
        var sb = new StringBuilder();
        foreach (var id in new[] { "researcher", "developer", "tester" })
        {
            var m = Move(id);
            if (m == null) { sb.Append(id + ": missing | "); continue; }
            Vector3 p = root.InverseTransformPoint(m.transform.position);
            float yaw = Mathf.DeltaAngle(root.eulerAngles.y, m.transform.eulerAngles.y);
            sb.Append(id + " (" + p.x.ToString("F3") + "," + p.z.ToString("F3") + ") yaw " + yaw.ToString("F0") + (m.IsMoving ? (m.IsReturning ? " walking home" : " walking out") : (m.AtHome ? " at home" : " standing away"))
                      + (m.IsTurningInPlace ? " turning" : "") + (m.IsFacingPoint ? " facing a colleague" : "") + " | ");
        }
        sb.Append("handoff busy=" + AgentHandoffController.AnyBusy + " exchangePending=" + AgentHandoffController.ExchangePending + " focus=" + MuseDesk.Workspace.FocusModeController.Active);
        return sb.ToString();
    }

    public static string Deliver()
    {
        if (!Application.isPlaying) return "not playing";
        var reg = Object.FindObjectsByType<AgentRegistry>(FindObjectsInactive.Exclude, FindObjectsSortMode.None).FirstOrDefault();
        var m = Move("researcher");
        if (reg == null || reg.deliveryAnchor == null || m == null) return "registry / anchor / researcher missing";
        m.DeliverTo(reg.deliveryAnchor, null, true);
        return "researcher walking to the delivery spot";
    }

    public static string Home(string id)
    {
        if (!Application.isPlaying) return "not playing";
        var m = Move(id);
        if (m == null) return "no agent " + id;
        m.ReturnHome();
        return id + " sent home";
    }

    /// <summary>Freeze the game clock a moment (seconds, default 0.45) after the next give / receive beat begins (an agent
    /// starts facing a colleague), so the exchange can be captured. Unfreeze puts the clock back. Editor Play Mode only.</summary>
    public static string FreezeAtExchange(string delay)
    {
        if (!Application.isPlaying) return "not playing";
        float.TryParse(delay, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float d);
        if (d <= 0f) d = 0.45f;
        var movers = Object.FindObjectsByType<AgentMovement>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        float seen = -1f, giveUp = Time.realtimeSinceStartup + 30f;
        UnityEditor.EditorApplication.CallbackFunction tick = null;
        tick = () =>
        {
            if (!Application.isPlaying || Time.realtimeSinceStartup > giveUp) { UnityEditor.EditorApplication.update -= tick; return; }
            if (seen < 0f) { if (movers.Any(m => m != null && m.AtHome && m.IsFacingPoint)) seen = Time.realtimeSinceStartup; return; }
            if (Time.realtimeSinceStartup - seen >= d) { Time.timeScale = 0f; UnityEditor.EditorApplication.update -= tick; }
        };
        UnityEditor.EditorApplication.update += tick;
        return "will freeze " + d.ToString("F2") + " s into the next exchange";
    }

    public static string Unfreeze() { Time.timeScale = 1f; return "timeScale 1"; }

    public static string Handoff(string pair)
    {
        if (!Application.isPlaying) return "not playing";
        var h = Object.FindObjectsByType<AgentHandoffController>(FindObjectsInactive.Exclude, FindObjectsSortMode.None).FirstOrDefault();
        if (h == null) return "no AgentHandoffController";
        var ids = (pair ?? "").Split(',');
        if (ids.Length != 2) return "use \"source,target\"";
        var mi = typeof(AgentHandoffController).GetMethod("TryHandoff", BindingFlags.Instance | BindingFlags.NonPublic);
        if (mi == null) return "TryHandoff not found";
        mi.Invoke(h, new object[] { ids[0].Trim(), ids[1].Trim(), 0f });
        return "queued " + ids[0].Trim() + " -> " + ids[1].Trim() + " (lanes authored: " + (h.lanes != null ? h.lanes.Length : 0) + ")";
    }
}
