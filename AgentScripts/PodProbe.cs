using System.Linq;
using System.Text;
using UnityEngine;
using MuseDesk.Spatial;

/// <summary>Read-only Play Mode probe for the ceiling pod deck: rugs/pads under the office and the pod's sockets.</summary>
public static class PodProbe
{
    public static string Rugs(string filter)
    {
        var ws = SpatialWorkspaceState.WorkspaceRoot;
        if (ws == null) return "no WorkspaceRoot";
        string f = (filter ?? "").Replace('_', ' ').ToLowerInvariant();
        var sb = new StringBuilder();
        foreach (var r in ws.GetComponentsInChildren<Renderer>(false))
        {
            string n = r.name.ToLowerInvariant();
            if (f.Length > 0 && !f.Split('|').Any(k => n.Contains(k))) continue;
            Bounds b = r.bounds;
            Vector3 lc = ws.InverseTransformPoint(b.center);
            sb.AppendLine(Path(r.transform, ws) + "  localCenter=" + lc.ToString("F3") + " worldSize=" + b.size.ToString("F3") + " active=" + r.gameObject.activeInHierarchy);
        }
        return sb.Length == 0 ? "none" : sb.ToString();
    }

    /// <summary>Flat, wide renderers (rugs, pads, plates) on the office.</summary>
    public static string Flat()
    {
        var ws = SpatialWorkspaceState.WorkspaceRoot;
        if (ws == null) return "no WorkspaceRoot";
        var sb = new StringBuilder();
        foreach (var r in ws.GetComponentsInChildren<Renderer>(false))
        {
            Bounds b = r.bounds;
            if (b.size.y > 0.012f || Mathf.Max(b.size.x, b.size.z) < 0.08f || r is SkinnedMeshRenderer) continue;
            Vector3 lc = ws.InverseTransformPoint(b.center);
            Vector3 ls = ws.InverseTransformVector(b.size);
            sb.AppendLine(Path(r.transform, ws) + "  localCenter=" + lc.ToString("F3") + " localSize~" + new Vector3(Mathf.Abs(ls.x), Mathf.Abs(ls.y), Mathf.Abs(ls.z)).ToString("F3") + " mat=" + (r.sharedMaterial != null ? r.sharedMaterial.name : "-"));
        }
        System.IO.File.WriteAllText("Temp/podprobe_flat.txt", sb.ToString()); return "wrote Temp/podprobe_flat.txt lines=" + sb.ToString().Split('\n').Length;
    }

    /// <summary>Every renderer and named transform under the pod visual, with materials.</summary>
    public static string Pod()
    {
        var art = Object.FindAnyObjectByType<CeilingPodArt>(FindObjectsInactive.Include);
        if (art == null || art.podVisual == null) return "no pod art";
        var ws = SpatialWorkspaceState.WorkspaceRoot;
        var sb = new StringBuilder();
        foreach (var t in art.podVisual.GetComponentsInChildren<Transform>(true))
        {
            var r = t.GetComponent<Renderer>();
            string mats = r != null ? string.Join(",", r.sharedMaterials.Select(m => m != null ? m.name : "-")) : "";
            string extra = r != null ? " bounds c=" + (ws != null ? ws.InverseTransformPoint(r.bounds.center) : r.bounds.center).ToString("F3") + " s=" + r.bounds.size.ToString("F3") + " mats=" + mats : "";
            if (r == null && t.childCount > 0 && !t.name.ToLower().Contains("socket") && !t.name.ToLower().Contains("anchor")) continue;
            sb.AppendLine(Path(t, art.podVisual) + " wsLocal=" + (ws != null ? ws.InverseTransformPoint(t.position) : t.position).ToString("F3") + extra);
        }
        System.IO.File.WriteAllText("Temp/podprobe_pod.txt", sb.ToString());
        return "wrote Temp/podprobe_pod.txt";
    }

    /// <summary>The pod mesh: per submesh, its connected pieces (mesh space bounds), flagging flat ones.</summary>
    public static string Pieces()
    {
        var art = Object.FindAnyObjectByType<CeilingPodArt>(FindObjectsInactive.Include);
        var smr = art != null && art.podVisual != null ? art.podVisual.GetComponentInChildren<SkinnedMeshRenderer>(true) : null;
        if (smr == null || smr.sharedMesh == null) return "no pod mesh";
        Mesh m = smr.sharedMesh;
        var v = m.vertices;
        var sb = new StringBuilder();
        sb.AppendLine("mesh " + m.name + " verts=" + v.Length + " submeshes=" + m.subMeshCount + " readable=" + m.isReadable + " bounds=" + m.bounds);
        for (int s = 0; s < m.subMeshCount; s++)
        {
            var tris = m.GetTriangles(s);
            // union-find over vertex indices
            var parent = new System.Collections.Generic.Dictionary<int, int>();
            System.Func<int, int> find = null;
            find = x => { int p; if (!parent.TryGetValue(x, out p)) { parent[x] = x; return x; } if (p == x) return x; int r = find(p); parent[x] = r; return r; };
            for (int i = 0; i < tris.Length; i += 3) { int a = find(tris[i]), b = find(tris[i + 1]), c = find(tris[i + 2]); parent[b] = a; parent[find(c)] = a; }
            // also merge by position (split normals)
            var byPos = new System.Collections.Generic.Dictionary<Vector3Int, int>();
            foreach (var k in parent.Keys.ToList()) { var key = Vector3Int.RoundToInt(v[k] * 10000f); int o; if (byPos.TryGetValue(key, out o)) parent[find(k)] = find(o); else byPos[key] = k; }
            var groups = new System.Collections.Generic.Dictionary<int, Bounds>();
            var counts = new System.Collections.Generic.Dictionary<int, int>();
            foreach (var k in parent.Keys.ToList()) { int r = find(k); Bounds b; if (groups.TryGetValue(r, out b)) { b.Encapsulate(v[k]); groups[r] = b; counts[r]++; } else { groups[r] = new Bounds(v[k], Vector3.zero); counts[r] = 1; } }
            string mat = s < smr.sharedMaterials.Length && smr.sharedMaterials[s] != null ? smr.sharedMaterials[s].name : "-";
            sb.AppendLine("submesh " + s + " " + mat + " tris=" + tris.Length / 3 + " pieces=" + groups.Count);
            foreach (var kv in groups.OrderByDescending(g => g.Value.size.x * g.Value.size.z).Take(8))
                sb.AppendLine("   piece c=" + kv.Value.min.ToString("F5") + " max=" + kv.Value.max.ToString("F5") + " verts=" + counts[kv.Key]);
        }
        sb.AppendLine("rootBone=" + (smr.rootBone != null ? smr.rootBone.name : "-") + " bones=" + smr.bones.Length);
        System.IO.File.WriteAllText("Temp/podprobe_pieces.txt", sb.ToString());
        return "wrote Temp/podprobe_pieces.txt";
    }

    public static string Anchor()
    {
        var reg = Object.FindAnyObjectByType<MuseDesk.Agents.AgentRegistry>(FindObjectsInactive.Include);
        var ws = SpatialWorkspaceState.WorkspaceRoot;
        var art = Object.FindAnyObjectByType<CeilingPodArt>(FindObjectsInactive.Include);
        string s = "anchor=" + (reg != null && reg.deliveryAnchor != null ? Path(reg.deliveryAnchor, null) + " wsLocal=" + ws.InverseTransformPoint(reg.deliveryAnchor.position).ToString("F3") : "none");
        foreach (var a in reg.agents) if (a != null) s += "\n" + a.agentId + " wsLocal=" + ws.InverseTransformPoint(a.transform.position).ToString("F3") + " path=" + Path(a.transform, null);
        if (art != null && art.podVisual != null)
            foreach (var n in new[] { "AgentInteractionSocket", "DocumentPresentationSocket" })
            {
                var t = art.podVisual.Find(n); if (t == null) foreach (var c in art.podVisual.GetComponentsInChildren<Transform>(true)) if (c.name == n) t = c;
                if (t != null) s += "\n" + n + " wsLocal=" + ws.InverseTransformPoint(t.position).ToString("F3") + " fwd(ws)=" + ws.InverseTransformDirection(t.forward).ToString("F2");
            }
        return s;
    }

    public static string Shelf()
    {
        var art = Object.FindAnyObjectByType<CeilingPodArt>(FindObjectsInactive.Include);
        var ho = Object.FindAnyObjectByType<CeilingShelfHandOver>(FindObjectsInactive.Include);
        var reg = Object.FindAnyObjectByType<MuseDesk.Agents.AgentRegistry>(FindObjectsInactive.Include);
        var ws = SpatialWorkspaceState.WorkspaceRoot;
        string s = "rugsSeparated=" + (art != null ? art.RugsSeparated : -1) + " handOver=" + (ho != null) + " atShelf=" + (ho != null && ho.AtShelf)
                 + " pod=" + (art != null && art.pod != null ? art.pod.State.ToString() : "-") + " redeliveries=" + (reg != null ? reg.Redeliveries : -1);
        if (reg != null && ws != null)
        {
            s += " anchor=" + (reg.deliveryAnchor != null ? reg.deliveryAnchor.name + "@" + ws.InverseTransformPoint(reg.deliveryAnchor.position).ToString("F3") : "-");
            foreach (var a in reg.agents) if (a != null && a.agentId == "researcher")
                { var m = a.GetComponent<MuseDesk.Agents.AgentMovement>(); var b = a.GetComponent<MuseDesk.Agents.AgentAnimatorBridge>();
                  s += " researcher@" + ws.InverseTransformPoint(a.transform.position).ToString("F3") + " moving=" + (m != null && m.IsMoving) + " offering=" + (b != null && b.IsOffering); }
        }
        return s;
    }

    public static string Named(string name)
    {
        var sb = new StringBuilder();
        var ws = SpatialWorkspaceState.WorkspaceRoot;
        foreach (var t in Resources.FindObjectsOfTypeAll<Transform>())
        {
            if (!t.gameObject.scene.IsValid() || !t.name.Contains(name.Replace('_', ' '))) continue;
            sb.AppendLine(Path(t, null) + " world=" + t.position.ToString("F3") + (ws != null ? " wsLocal=" + ws.InverseTransformPoint(t.position).ToString("F3") : "") + " active=" + t.gameObject.activeInHierarchy);
        }
        return sb.Length == 0 ? "none" : sb.ToString();
    }

    static string Path(Transform t, Transform stop)
    {
        string p = t.name;
        for (var c = t.parent; c != null && c != stop; c = c.parent) p = c.name + "/" + p;
        return p;
    }
}



