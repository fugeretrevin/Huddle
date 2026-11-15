using System.Linq;
using System.Text;
using UnityEngine;
using UnityEditor.SceneManagement;

/// <summary>Read-only probe used while designing the desk choreography layout (DeskChoreographyLayoutV1): where things are
/// in WorkspaceRoot-local metres, and what renders under a named object. Changes nothing.</summary>
public static class DeskLayoutProbe
{
    static Transform Root() =>
        EditorSceneManager.GetActiveScene().GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true)).FirstOrDefault(t => t.name == "WorkspaceRoot");

    /// <summary>Renderers under the named object: enabled, active, root-local bounds.</summary>
    public static string Renderers(string name)
    {
        var root = Root(); if (root == null) return "no WorkspaceRoot";
        var sb = new StringBuilder();
        foreach (var t in EditorSceneManager.GetActiveScene().GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true)).Where(t => t.name == name))
        {
            sb.Append("\n" + Path(t) + " active=" + t.gameObject.activeInHierarchy + " local=" + V(root.InverseTransformPoint(t.position)) + " scale=" + t.lossyScale.x.ToString("F3"));
            foreach (var r in t.GetComponentsInChildren<Renderer>(true))
            {
                var b = r.bounds; Vector3 mn = root.InverseTransformPoint(b.min), mx = root.InverseTransformPoint(b.max);
                sb.Append("\n   " + r.name + " on=" + (r.enabled && r.gameObject.activeInHierarchy) + " x[" + Mathf.Min(mn.x, mx.x).ToString("F3") + "," + Mathf.Max(mn.x, mx.x).ToString("F3") + "] y[" + mn.y.ToString("F3") + "," + mx.y.ToString("F3")
                          + "] z[" + Mathf.Min(mn.z, mx.z).ToString("F3") + "," + Mathf.Max(mn.z, mx.z).ToString("F3") + "] lossy=" + r.transform.lossyScale.x.ToString("F4"));
            }
        }
        return sb.Length == 0 ? "not found: " + name : sb.ToString();
    }

    /// <summary>Every direct child of WorkspaceRoot and of the listed groups: root-local position, yaw, active, renderer bounds.</summary>
    public static string Tree()
    {
        var root = Root(); if (root == null) return "no WorkspaceRoot";
        var sb = new StringBuilder();
        void Line(Transform t, int depth)
        {
            var rs = t.GetComponentsInChildren<Renderer>(false).Where(r => r.enabled && !(r is ParticleSystemRenderer)).ToArray();
            string bb = "";
            if (rs.Length > 0)
            {
                var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
                Vector3 mn = root.InverseTransformPoint(b.min), mx = root.InverseTransformPoint(b.max);
                bb = " x[" + Mathf.Min(mn.x, mx.x).ToString("F3") + "," + Mathf.Max(mn.x, mx.x).ToString("F3") + "] y[" + mn.y.ToString("F3") + "," + mx.y.ToString("F3") + "] z[" + Mathf.Min(mn.z, mx.z).ToString("F3") + "," + Mathf.Max(mn.z, mx.z).ToString("F3") + "]";
            }
            sb.Append("\n" + new string(' ', depth * 2) + t.name + (t.gameObject.activeSelf ? "" : " [off]") + " " + V(root.InverseTransformPoint(t.position)) + " yaw " + t.eulerAngles.y.ToString("F0") + " s " + t.lossyScale.x.ToString("F3") + bb);
        }
        foreach (Transform c in root)
        {
            Line(c, 0);
            if (c.name == "Phase6" || c.name == "OfficeV2" || c.name.EndsWith("Station") || c.name == "AgentTalk" || c.name == "OfficeProps_Collection")
                foreach (Transform g in c) if (g.gameObject.activeSelf) Line(g, 1);
        }
        return sb.ToString();
    }

    /// <summary>The Floor platform and Ceiling pod decks (their BoxCollider, at the holder's shown scale 1) in root-local XZ.</summary>
    public static string Decks()
    {
        var root = Root(); if (root == null) return "no WorkspaceRoot";
        var sb = new StringBuilder();
        foreach (var holderName in new[] { "SpatialArt_FloorPlatform", "SpatialArt_CeilingPod" })
        {
            var h = root.Find(holderName);
            if (h == null) { sb.Append("\n" + holderName + ": missing"); continue; }
            sb.Append("\n" + holderName + " localPos " + V(h.localPosition) + " localScale " + V(h.localScale) + " active=" + h.gameObject.activeSelf);
            foreach (Transform c in h)
            {
                var bc = c.GetComponent<BoxCollider>();
                sb.Append("\n   " + c.name + " active=" + c.gameObject.activeSelf + " localPos " + V(c.localPosition) + " localScale " + V(c.localScale));
                if (bc != null)
                {
                    Vector3 ctr = h.localPosition + Vector3.Scale(c.localPosition + Vector3.Scale(bc.center, c.localScale), Vector3.one);
                    Vector3 half = Vector3.Scale(bc.size, c.localScale) * 0.5f;
                    sb.Append("  deck x[" + (ctr.x - half.x).ToString("F3") + "," + (ctr.x + half.x).ToString("F3") + "] z[" + (ctr.z - half.z).ToString("F3") + "," + (ctr.z + half.z).ToString("F3") + "] top y " + (ctr.y + half.y).ToString("F3"));
                }
                foreach (Transform g in c) if (g.name.Contains("Socket") || g.name.Contains("Dock") || g.name.Contains("Point")) sb.Append("\n      " + g.name + " " + V(h.localPosition + Vector3.Scale(c.localPosition + Vector3.Scale(g.localPosition, c.localScale), Vector3.one)));
            }
        }
        foreach (var mb in Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (mb == null) continue;
            string n = mb.GetType().Name;
            if (n == "RemoteDocumentDeliveryController") sb.Append("\nRemoteDocumentDeliveryController.platformLocal = " + V((Vector3)mb.GetType().GetField("platformLocal").GetValue(mb)));
            if (n == "SpatialWorkspaceDirector")
            {
                var t = mb.GetType();
                sb.Append("\nSpatialWorkspaceDirector.footprint = " + t.GetField("footprint").GetValue(mb) + " flags floor/ceiling: " + UnityEditor.EditorJsonUtility.ToJson(t.GetField("flags").GetValue(mb)));
            }
            if (n == "CeilingPodArt")
            {
                var arr = mb.GetType().GetField("offDeckProps").GetValue(mb) as Transform[];
                sb.Append("\nCeilingPodArt.offDeckProps = " + (arr == null ? "null" : string.Join(", ", arr.Where(a => a != null).Select(a => a.name))));
            }
            if (n == "AgentHandoffController")
            {
                var t = mb.GetType();
                sb.Append("\nAgentHandoffController stopDistance=" + t.GetField("stopDistance").GetValue(mb) + " backOffset=" + t.GetField("backOffset").GetValue(mb) + " on " + Path(mb.transform));
            }
            if (n == "AgentRegistry") sb.Append("\nAgentRegistry on " + Path(mb.transform));
        }
        return sb.ToString();
    }

    /// <summary>Visible renderers that are NOT under WorkspaceRoot but reach into the agents' walking volume
    /// (root-local x -0.65..0.65, z -0.3..0.55, y 0.015..0.25). Grouped by their top-level parent. Works in Edit and Play Mode.</summary>
    public static string Intruders()
    {
        var root = Root(); if (root == null) return "no WorkspaceRoot";
        var sb = new StringBuilder();
        var groups = new System.Collections.Generic.Dictionary<string, Bounds>();
        foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (!r.enabled || r.transform.IsChildOf(root) || r is ParticleSystemRenderer) continue;
            if (r.transform.lossyScale.magnitude < 0.01f) continue;
            var b = r.bounds; if (b.size.magnitude < 0.004f) continue;
            Vector3 mn = Vector3.positiveInfinity, mx = Vector3.negativeInfinity;
            for (int i = 0; i < 8; i++)
            {
                var c = root.InverseTransformPoint(b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1)));
                mn = Vector3.Min(mn, c); mx = Vector3.Max(mx, c);
            }
            if (mx.x < -0.65f || mn.x > 0.65f || mx.z < -0.3f || mn.z > 0.55f || mx.y < 0.015f || mn.y > 0.25f) continue;
            Transform top = r.transform; int depth = 0;
            while (top.parent != null && top.parent.parent != null) { top = top.parent; depth++; }
            string key = (top.parent != null ? top.parent.name + "/" : "") + top.name;
            var lb = new Bounds((mn + mx) * 0.5f, mx - mn);
            if (groups.TryGetValue(key, out var have)) { have.Encapsulate(lb); groups[key] = have; } else groups[key] = lb;
        }
        foreach (var kv in groups.OrderBy(k => k.Key))
            sb.Append("\n" + kv.Key + " x[" + kv.Value.min.x.ToString("F3") + "," + kv.Value.max.x.ToString("F3") + "] y[" + kv.Value.min.y.ToString("F3") + "," + kv.Value.max.y.ToString("F3") + "] z[" + kv.Value.min.z.ToString("F3") + "," + kv.Value.max.z.ToString("F3") + "]");
        return sb.Length == 0 ? "none" : sb.ToString();
    }

    /// <summary>Direct children (two levels) of the named object with their visible renderer bounds, root-local.</summary>
    public static string Under(string name)
    {
        var root = Root(); if (root == null) return "no WorkspaceRoot";
        var t0 = EditorSceneManager.GetActiveScene().GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true)).FirstOrDefault(t => t.name == name);
        if (t0 == null) return "not found " + name;
        var sb = new StringBuilder();
        void Line(Transform t, int depth)
        {
            var rs = t.GetComponentsInChildren<Renderer>(false).Where(r => r.enabled && !(r is ParticleSystemRenderer) && r.transform.lossyScale.magnitude > 0.01f).ToArray();
            if (rs.Length == 0) return;
            Vector3 mn = Vector3.positiveInfinity, mx = Vector3.negativeInfinity;
            foreach (var r in rs)
                for (int i = 0; i < 8; i++)
                {
                    var c = root.InverseTransformPoint(r.bounds.center + Vector3.Scale(r.bounds.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1)));
                    mn = Vector3.Min(mn, c); mx = Vector3.Max(mx, c);
                }
            sb.Append("\n" + new string(' ', depth * 2) + t.name + " x[" + mn.x.ToString("F3") + "," + mx.x.ToString("F3") + "] y[" + mn.y.ToString("F3") + "," + mx.y.ToString("F3") + "] z[" + mn.z.ToString("F3") + "," + mx.z.ToString("F3") + "]");
        }
        foreach (Transform c in t0) { if (!c.gameObject.activeSelf) continue; Line(c, 0); foreach (Transform g in c) if (g.gameObject.activeSelf) Line(g, 1); }
        return sb.ToString();
    }

    static string V(Vector3 v) => "(" + v.x.ToString("F3") + "," + v.y.ToString("F3") + "," + v.z.ToString("F3") + ")";
    static string Path(Transform t) { string s = t.name; for (var a = t.parent; a != null; a = a.parent) s = a.name + "/" + s; return s; }
}
