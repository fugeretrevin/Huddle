using System.Linq;
using System.Text;
using UnityEngine;
using MuseDesk.Wall;

/// <summary>rc20 checks in Play Mode: what floats above the agents, where the tubes cross the slabs.</summary>
public static class Rc20Probe
{
    static WallHabitatController C => Object.FindAnyObjectByType<WallHabitatController>();

    /// <summary>Small boxes (any renderer under 6 cm) in the habitat that sit above an agent's desk area, with their path.</summary>
    public static string SmallThings()
    {
        var root = GameObject.Find("WallHabitatRoot");
        if (root == null) return "no habitat";
        var sb = new StringBuilder();
        int n = 0;
        foreach (var r in root.GetComponentsInChildren<Renderer>())
        {
            if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
            Vector3 s = r.bounds.size;
            if (s.x > 0.03f || s.y > 0.03f || s.z > 0.03f || s.x < 0.006f) continue;
            if (!(r is MeshRenderer)) continue;
            var mf = r.GetComponent<MeshFilter>();
            string mesh = mf != null && mf.sharedMesh != null ? mf.sharedMesh.name : "?";
            if (mesh != "Cube" && !mesh.ToLower().Contains("cube") && !mesh.ToLower().Contains("box")) continue;
            Vector3 l = root.transform.InverseTransformPoint(r.bounds.center);
            sb.Append(Path(r.transform, root.transform)).Append(" mesh=").Append(mesh).Append(" local=").Append(l.ToString("F3"))
              .Append(" size=").Append(s.ToString("F3")).Append('\n');
            if (++n > 40) break;
        }
        return n + " small boxes\n" + sb;
    }

    public static string Collars()
    {
        var c = C; if (c == null || c.View == null) return "no view";
        var v = c.View;
        var sb = new StringBuilder("collars=" + v.TubeCollars + "\n");
        foreach (var b in v.layout.boxes)
            if (b.kind == MuseDesk.Spatial.Wall.HabitatBoxKind.Deck || b.kind == MuseDesk.Spatial.Wall.HabitatBoxKind.Roof || b.kind == MuseDesk.Spatial.Wall.HabitatBoxKind.Partition)
                sb.Append(b.kind).Append(' ').Append(b.id).Append(" min=").Append(b.Min.ToString("F3")).Append(" max=").Append(b.Max.ToString("F3")).Append('\n');
        return sb.ToString();
    }

    /// <summary>Art renderers whose local bounds the given tube's polyline passes through (names + local bounds).</summary>
    public static string ArtCrossings(string role)
    {
        var c = C; if (c == null || c.View == null) return "no view";
        var root = c.View.root;
        var tube = root.Find("CardTube_" + role);
        if (tube == null) return "no tube";
        var mf = tube.GetComponent<MeshFilter>();
        var sb = new StringBuilder();
        foreach (var r in root.GetComponentsInChildren<MeshRenderer>())
        {
            if (r.transform.IsChildOf(tube) || r.name.StartsWith("CardTube") || !r.enabled) continue;
            var f = r.GetComponent<MeshFilter>(); if (f == null || f.sharedMesh == null) continue;
            Bounds lb = LocalBounds(r.transform, f.sharedMesh.bounds, root);
            if (lb.size.x > 0.9f && lb.size.y > 0.5f) continue;        // the whole backplate
            Bounds tb = LocalBounds(tube, mf.sharedMesh.bounds, root);
            if (!lb.Intersects(tb)) continue;
            sb.Append(Path(r.transform, root)).Append(" ").Append(lb.min.ToString("F3")).Append("..").Append(lb.max.ToString("F3")).Append('\n');
        }
        return sb.ToString();
    }

    static Bounds LocalBounds(Transform t, Bounds mb, Transform root)
    {
        Bounds o = new Bounds(); bool first = true;
        for (int i = 0; i < 8; i++)
        {
            Vector3 p = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
            Vector3 l = root.InverseTransformPoint(t.TransformPoint(p));
            if (first) { o = new Bounds(l, Vector3.zero); first = false; } else o.Encapsulate(l);
        }
        return o;
    }

    /// <summary>Aim __CineCam at a habitat-local point "x,y,z" from <paramref name="dist"/> m in front.</summary>
    public static string AimLocal(string xyz)
    {
        var c = C; if (c == null || c.View == null) return "no view";
        var p = xyz.Split(',');
        Vector3 l = new Vector3(float.Parse(p[0]), float.Parse(p[1]), float.Parse(p[2]));
        float dist = p.Length > 3 ? float.Parse(p[3]) : 0.35f;
        Transform root = c.View.root;
        Vector3 at = root.TransformPoint(l);
        var go = GameObject.Find("__CineCam") ?? new GameObject("__CineCam");
        var cam = go.GetComponent<Camera>(); if (cam == null) cam = go.AddComponent<Camera>();
        cam.enabled = true; cam.depth = 100f; cam.stereoTargetEye = StereoTargetEyeMask.None; cam.nearClipPlane = 0.01f;
        cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.2f, 0.22f, 0.25f);
        Vector3 fwd = root.TransformDirection(Vector3.forward);
        float up = p.Length > 4 ? float.Parse(p[4]) : 0.03f;
        cam.transform.position = at + fwd * dist + Vector3.up * up;
        cam.transform.rotation = Quaternion.LookRotation(at - cam.transform.position, Vector3.up);
        cam.fieldOfView = 40f;
        return "aimed at " + l.ToString("F3") + " collars=" + c.View.TubeCollars;
    }

    /// <summary>The voice readout against the Manager paper and the visit card (world, and in the paper's own frame).</summary>
    public static string Readout()
    {
        var c = C;
        var hud = Object.FindAnyObjectByType<MuseDesk.Voice.VoiceHud>(FindObjectsInactive.Include);
        if (hud == null || hud.readout == null) return "no hud";
        var sb = new StringBuilder("readout world=" + hud.readout.position.ToString("F3") + " scale=" + hud.readout.lossyScale.ToString("F3") + " parent=" + (hud.readout.parent != null ? hud.readout.parent.name : "-"));
        var rends = hud.readout.GetComponentsInChildren<Renderer>(true);
        if (rends.Length > 0) { Bounds b = rends[0].bounds; foreach (var r in rends) b.Encapsulate(r.bounds); sb.Append(" bounds=" + b.center.ToString("F3") + " size=" + b.size.ToString("F3")); }
        if (c != null && c.PlanPaper != null)
        {
            var t = c.PlanPaper.card.root;
            sb.Append(" | paper world=" + t.position.ToString("F3") + " visible=" + c.PlanPaper.Visible + " size=" + c.PlanPaper.card.Size.ToString("F3"));
            sb.Append(" readoutInPaper=" + t.InverseTransformPoint(hud.readout.position).ToString("F3"));
        }
        var cam = Camera.main; if (cam != null) sb.Append(" | head=" + cam.transform.position.ToString("F3"));
        return sb.ToString();
    }

    public static string Screen()
    {
        var c = C; if (c == null) return "no controller";
        c.PressScreen();
        return ScreenState();
    }

    public static string ScreenState()
    {
        var c = C; if (c == null) return "no controller";
        var p = c.ScreenPanel;
        var key = GameObject.Find("Key_Screen");
        return "open=" + c.ScreenOpen + " frames=" + c.ScreenFrames + " problem='" + c.ScreenProblem + "'"
             + (p != null ? " panel@" + p.position.ToString("F2") : " no panel")
             + (key != null ? " key@" + key.transform.position.ToString("F2") + " parent=" + key.transform.parent.name : " no key");
    }

    /// <summary>Aim __CineCam at the screen panel from the eye side.</summary>
    public static string AimScreen(string dist)
    {
        var c = C; if (c == null || c.ScreenPanel == null) return "no panel";
        float d = 0.9f; float.TryParse(dist, out d);
        Transform t = c.ScreenPanel;
        var go = GameObject.Find("__CineCam") ?? new GameObject("__CineCam");
        var cam = go.GetComponent<Camera>(); if (cam == null) cam = go.AddComponent<Camera>();
        cam.enabled = true; cam.depth = 100f; cam.stereoTargetEye = StereoTargetEyeMask.None; cam.nearClipPlane = 0.01f;
        cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.2f, 0.22f, 0.25f);
        cam.transform.position = t.position - t.forward * d + Vector3.down * 0.15f;
        cam.transform.rotation = Quaternion.LookRotation(t.position + Vector3.down * 0.08f - cam.transform.position, Vector3.up);
        cam.fieldOfView = 55f;
        return "aimed at screen";
    }

    public static string Speech()
    {
        return "said=" + MuseDesk.Voice.AgentSpeech.SaidCount + " last[" + MuseDesk.Voice.AgentSpeech.LastSpeaker + "]='" + MuseDesk.Voice.AgentSpeech.LastSaid + "'"
             + " speechObj=" + (GameObject.Find("AgentSpeech") != null);
    }

    /// <summary>The readout's way out of the plan paper: a box at the paper's centre should come out ~5 cm toward the eye.</summary>
    public static string Clearance()
    {
        var c = C; if (c == null || c.PlanPaper == null || !c.PlanPaper.Visible) return "open the plan paper first";
        var f = MuseDesk.Voice.VoiceHud.Clearance;
        if (f == null) return "no clearance hook";
        Transform t = c.PlanPaper.card.root;
        Vector3 n = f(new Bounds(t.position, new Vector3(0.12f, 0.05f, 0.01f)));
        Vector3 far = f(new Bounds(t.position + t.right * 1.0f, new Vector3(0.12f, 0.05f, 0.01f)));
        var cam = Camera.main;
        float towardEye = cam != null ? Vector3.Dot(n, (cam.transform.position - t.position).normalized) : 0f;
        return "overlapping: out " + n.magnitude.ToString("F3") + " m (toward eye " + towardEye.ToString("F3") + ") | clear: " + far.magnitude.ToString("F3");
    }

    public static string DragVisit()
    {
        var c = C; if (c == null || c.VisitCard == null || !c.VisitCard.Visible) return "no visit card";
        Vector3 before = c.VisitCard.root.position;
        bool ok = c.DriverDragVisitCard(new Vector3(0.10f, 0.05f, 0f));
        return "dragged=" + ok + " moved " + (c.VisitCard.root.position - before).magnitude.ToString("F3") + " m; " + "dragged=" + c.DraggedCard;
    }

    static string Path(Transform t, Transform stop)
    {
        string p = t.name;
        for (var x = t.parent; x != null && x != stop; x = x.parent) p = x.name + "/" + p;
        return p;
    }
}
