using System.Linq;
using System.Text;
using UnityEngine;

/// <summary>How each workstation, its chair/monitor and its agent face, in WorkspaceRoot space (+Z = toward the user).
///   unity command script run --file AgentScripts/DeskFacing.cs --entry DeskFacing.Report</summary>
public static class DeskFacing
{
    static string F(Vector3 v) { return "(" + v.x.ToString("F2") + "," + v.y.ToString("F2") + "," + v.z.ToString("F2") + ")"; }

    public static string Report()
    {
        var sb = new StringBuilder();
        var roots = Resources.FindObjectsOfTypeAll<Transform>().Where(t => t.gameObject.scene.IsValid() && (t.name == "WorkspaceRoot" || t.name == "WallHabitatRoot")).ToList();
        foreach (var root in roots)
        {
            sb.AppendLine("== " + root.name + " active=" + root.gameObject.activeInHierarchy);
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                string n = t.name.ToLowerInvariant();
                bool interesting = n.Contains("station") || n.Contains("chair") || n.Contains("monitor") || n.Contains("screen") || n.Contains("desk") && t.parent != null && t.parent.name.Contains("Station")
                                   || n.StartsWith("agent_") || n.Contains("workstation") || n.Contains("seat") || n.Contains("home");
                if (!interesting) continue;
                if (t.GetComponentsInParent<Transform>().Length - root.GetComponentsInParent<Transform>().Length > 5) continue;
                Vector3 p = root.InverseTransformPoint(t.position);
                Vector3 f = root.InverseTransformDirection(t.forward);
                var r = t.GetComponentInChildren<Renderer>();
                string bounds = r != null ? " rCenter=" + F(root.InverseTransformPoint(r.bounds.center)) : "";
                sb.AppendLine(Path(t, root) + " pos=" + F(p) + " fwd=" + F(f) + " yaw=" + Quaternion.LookRotation(f).eulerAngles.y.ToString("F0") + bounds);
            }
        }
        return sb.ToString();
    }

    static string Path(Transform t, Transform root)
    {
        string s = t.name;
        while (t.parent != null && t.parent != root) { t = t.parent; s = t.name + "/" + s; }
        return s;
    }
}
