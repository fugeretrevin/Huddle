using System.Linq;
using System.Text;
using UnityEngine;

/// <summary>Print where named objects are in a root's local space: Where.In("WorkspaceRoot","A,B,C").</summary>
public static class Where
{
    public static string In(string root, string names)
    {
        Transform r = Resources.FindObjectsOfTypeAll<Transform>().FirstOrDefault(t => t.name == root && t.gameObject.scene.IsValid());
        if (r == null) return "no " + root;
        var sb = new StringBuilder();
        foreach (string n in names.Split(','))
            foreach (var t in r.GetComponentsInChildren<Transform>(true).Where(x => x.name == n.Trim()))
            {
                Vector3 f = r.InverseTransformDirection(t.forward);
                var rends = t.GetComponentsInChildren<Renderer>(true);
                string b = "";
                if (rends.Length > 0)
                {
                    Bounds bb = rends[0].bounds; foreach (var x in rends) bb.Encapsulate(x.bounds);
                    b = " bmin=" + r.InverseTransformPoint(bb.min).ToString("F3") + " bmax=" + r.InverseTransformPoint(bb.max).ToString("F3");
                }
                sb.AppendLine(n + " pos=" + r.InverseTransformPoint(t.position).ToString("F3") + " fwdYaw=" + (Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg).ToString("F0") + " active=" + t.gameObject.activeInHierarchy + b);
            }
        return sb.ToString();
    }
}
