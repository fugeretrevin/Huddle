using System.Linq;
using System.Text;
using UnityEngine;

/// <summary>Local-space layout of each Desk workstation mesh: renderers, their local bounds, and a z-histogram of
/// vertices above seat height vs below (desk top vs chair), plus the agent body's extents in station space.</summary>
public static class DeskMeshProbe
{
    public static string Run()
    {
        var sb = new StringBuilder();
        Transform ws = Resources.FindObjectsOfTypeAll<Transform>().FirstOrDefault(t => t.name == "WorkspaceRoot" && t.gameObject.scene.IsValid());
        foreach (string s in new[] { "DevStation" })
        {
            Transform st = ws.Find(s), wk = st.Find("Workstation");
            sb.AppendLine(s + " wk local pos " + wk.localPosition.ToString("F3") + " yaw " + wk.localEulerAngles.y.ToString("F0") + " scale " + wk.lossyScale.ToString("F3"));
            foreach (Transform c in wk) sb.AppendLine("  child " + c.name + " lp=" + c.localPosition.ToString("F3") + " active=" + c.gameObject.activeSelf);
            foreach (var mf in wk.GetComponentsInChildren<MeshFilter>(true))
            {
                var m = mf.sharedMesh; if (m == null) continue;
                var v = m.vertices;
                // in Workstation local space
                var pts = v.Select(p => wk.InverseTransformPoint(mf.transform.TransformPoint(p))).ToArray();
                sb.AppendLine("  mesh " + mf.name + " '" + m.name + "' verts=" + v.Length + " sub=" + m.subMeshCount
                              + " min=" + new Vector3(pts.Min(p => p.x), pts.Min(p => p.y), pts.Min(p => p.z)).ToString("F3")
                              + " max=" + new Vector3(pts.Max(p => p.x), pts.Max(p => p.y), pts.Max(p => p.z)).ToString("F3"));
                // z slices: what heights occupy each z band
                for (float z = -0.12f; z < 0.16f; z += 0.02f)
                {
                    var band = pts.Where(p => p.z >= z && p.z < z + 0.02f).ToArray();
                    if (band.Length == 0) continue;
                    sb.AppendLine("    z[" + z.ToString("F2") + "] n=" + band.Length + " y " + band.Min(p => p.y).ToString("F3") + ".." + band.Max(p => p.y).ToString("F3")
                                  + " x " + band.Min(p => p.x).ToString("F3") + ".." + band.Max(p => p.x).ToString("F3"));
                }
            }
            Transform agent = st.Cast<Transform>().First(c => c.name.StartsWith("Agent_"));
            foreach (var r in agent.GetComponentsInChildren<Renderer>(true).Take(12))
            {
                Bounds b = r.bounds;
                Vector3 mn = st.InverseTransformPoint(b.min), mx = st.InverseTransformPoint(b.max);
                sb.AppendLine("  agent part " + r.name + " z " + Mathf.Min(mn.z, mx.z).ToString("F3") + ".." + Mathf.Max(mn.z, mx.z).ToString("F3") + " y " + mn.y.ToString("F3") + ".." + mx.y.ToString("F3"));
            }
        }
        return sb.ToString();
    }
}
