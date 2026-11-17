using System.Linq;
using System.Text;
using UnityEngine;

/// <summary>rc17 worn feedback probe 2: nameplates, alerts, agent heights, pushable props (pose in WorkspaceRoot space).</summary>
public static class FeedbackProbe2
{
    public static string Run()
    {
        var sb = new StringBuilder();
        Transform ws = Resources.FindObjectsOfTypeAll<Transform>().FirstOrDefault(t => t.name == "WorkspaceRoot" && t.gameObject.scene.IsValid());
        foreach (var np in Resources.FindObjectsOfTypeAll<MuseDesk.Workspace.AgentNameplate>().Where(n => n.gameObject.scene.IsValid()))
            sb.AppendLine("plate " + np.name + " parent=" + np.transform.parent.name + " lp=" + np.transform.localPosition.ToString("F3") + " le=" + np.transform.localEulerAngles.ToString("F1") + " face=" + np.faceViewer);
        foreach (var a in ws.GetComponentsInChildren<Transform>(true).Where(t => t.name.StartsWith("Agent_") && t.GetComponent<Animator>() != null))
        {
            var rs = a.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            float top = rs.Length > 0 ? rs.Max(r => r.bounds.max.y) : 0f;
            var alert = a.Find("Alert");
            sb.AppendLine("agent " + a.name + " rootY=" + a.position.y.ToString("F3") + " topY=" + top.ToString("F3") + " height=" + (top - a.position.y).ToString("F3") + " lossy=" + a.lossyScale.x.ToString("F3")
                          + (alert ? " alert lp=" + alert.localPosition.ToString("F3") + " worldAbove=" + (alert.position.y - a.position.y).ToString("F3") + " scale=" + alert.localScale.ToString("F3") : ""));
        }
        foreach (var p in Resources.FindObjectsOfTypeAll<MuseDesk.Workspace.PushableProp>().Where(n => n.gameObject.scene.IsValid()))
        {
            var t = p.transform;
            Vector3 fwdWs = ws.InverseTransformDirection(t.forward);
            float yaw = Mathf.Atan2(fwdWs.x, fwdWs.z) * Mathf.Rad2Deg;
            var rb = t.GetComponent<Rigidbody>();
            sb.AppendLine("prop " + t.name + " parent=" + t.parent.name + " posWs=" + ws.InverseTransformPoint(t.position).ToString("F3") + " yawWs=" + yaw.ToString("F0") + " le=" + t.localEulerAngles.ToString("F0") + " kin=" + (rb ? rb.isKinematic.ToString() : "-") + " active=" + t.gameObject.activeInHierarchy);
        }
        return sb.ToString();
    }
}
