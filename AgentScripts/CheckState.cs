using System.Linq;
using System.Text;
using UnityEngine;
using MuseDesk.Networking;
using MuseDesk.DebugTools;
using MuseDesk.Documents;
using MuseDesk.Agents;

public static class CheckState
{
    public static string MoveState()
    {
        var sb = new StringBuilder();
        foreach (var m in Object.FindObjectsByType<AgentMovement>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            var v = m.GetComponent<AgentView>();
            sb.AppendLine($"{(v ? v.agentId : m.name)}: atHome={m.AtHome} pos={m.transform.position}");
        }
        return sb.ToString();
    }

    public static string DocState()
    {
        var sb = new StringBuilder();
        var del = Object.FindAnyObjectByType<DocumentDeliveryController>();
        if (del == null) return "no DocumentDeliveryController";
        var doc = del.paperRoot;
        sb.AppendLine($"doc active={doc != null && doc.activeInHierarchy}");
        var panel = del.panel;
        if (panel != null)
        {
            sb.AppendLine($"  title='{(panel.title ? panel.title.text : "")}'");
            sb.AppendLine($"  summary='{(panel.summary ? panel.summary.text : "")}'");
            sb.AppendLine($"  body='{(panel.body ? panel.body.text.Replace("\n", " | ") : "")}'");
            sb.AppendLine($"  status='{(panel.status ? panel.status.text : "")}'");
            sb.AppendLine($"  answer='{(panel.answer ? panel.answer.text : "")}'");
            sb.AppendLine($"  actions active={(panel.actionsRoot ? panel.actionsRoot.activeSelf.ToString() : "n/a")}");
        }
        return sb.ToString();
    }

    public static string PanelApprove()
    {
        var del = Object.FindAnyObjectByType<DocumentDeliveryController>();
        if (del == null) return "no DocumentDeliveryController";
        del.OnApprove();
        return "panel OnApprove called";
    }

    public static string PanelAsk()
    {
        var del = Object.FindAnyObjectByType<DocumentDeliveryController>();
        if (del == null) return "no DocumentDeliveryController";
        del.OnAskWhy();
        return "panel OnAskWhy called";
    }

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
