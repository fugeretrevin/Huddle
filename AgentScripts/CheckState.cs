using System.Text;
using UnityEngine;
using MuseDesk.Networking;
using MuseDesk.DebugTools;

public static class CheckState
{
    public static string Now()
    {
        var sb = new StringBuilder();
        var sc = Object.FindAnyObjectByType<SessionController>();
        if (sc == null) return "no SessionController";
        sb.AppendLine($"live={sc.IsLive} lastSeq={sc.Reducer.LastSeq} pending={sc.PendingDecisionId}");
        foreach (var a in sc.Reducer.Snapshot.Agents)
            sb.AppendLine($"  {a.AgentId}: {a.State}");
        var diag = Object.FindAnyObjectByType<DiagnosticsOverlay>();
        if (diag != null) sb.AppendLine($"note='{diag.note}'  mode='{diag.modeLabel}'");
        return sb.ToString();
    }

    public static string Approve()
    {
        var sc = Object.FindAnyObjectByType<SessionController>();
        if (sc == null) return "no SessionController";
        var before = sc.PendingDecisionId;
        sc.ApprovePending();
        return $"approved pending={before}";
    }
}
