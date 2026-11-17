using System.Text;
using UnityEngine;
using MuseDesk.Wall;

/// <summary>rc19 checks in Play Mode: papers by tube, the plan paper's step ticks, the portal's size against the visitor.</summary>
public static class Rc19Probe
{
    static WallHabitatController C => Object.FindAnyObjectByType<WallHabitatController>();

    public static string Tubes()
    {
        var c = C; if (c == null) return "no controller";
        var v = c.View;
        return "papersByTube=" + c.PapersByTube + (v != null ? " mailSent=" + v.MailSent + " inFlight=" + v.MailInFlight : " no view");
    }

    /// <summary>Open the Manager paper and ask for a plan (offline demo); then call Ticks.</summary>
    public static string AskPlan()
    {
        var c = C; if (c == null) return "no controller";
        if (!c.WallActive) return "wall not active";
        return c.RequestPlanByVoice("Build the login flow, use the existing repo, and have Tester verify it") ?? "null";
    }

    public static string Ticks()
    {
        var c = C; if (c == null) return "no controller";
        var p = c.PlanPaper; if (p == null) return "no paper";
        var sb = new StringBuilder("state=" + c.Manager.State + " paperVisible=" + p.Visible + " ticks=" + p.ChecksShown + " rows=" + p.RowCount + ":");
        for (int i = 0; i < p.ChecksShown; i++) sb.Append(" [item ").Append(p.CheckItems[i] + 1).Append(" '").Append(p.CheckLabel(i)).Append("']");
        sb.Append(" | items:");
        for (int i = 0; i < c.Manager.Items.Count; i++) sb.Append(' ').Append(c.Manager.Items[i].approved ? "kept" : "skipped");
        for (int i = 0; i < p.RowCount; i++) sb.Append("\n  row ").Append(i).Append(": ").Append(p.RowText(i));
        return sb.ToString();
    }

    /// <summary>Press tick <paramref name="i"/> through its key's own action (what a poke does).</summary>
    public static string PressTick(string i)
    {
        var c = C; if (c == null) return "no controller";
        int n; int.TryParse(i, out n);
        var p = c.PlanPaper; if (p == null || n >= p.ChecksShown) return "no tick " + n;
        c.ToggleItem(p.CheckItems[n]);
        return Ticks();
    }

    static bool PressKey(WallCard card, string label)
    {
        if (card == null) return false;
        for (int i = 0; i < card.KeyCount; i++) if (card.KeyLabel(i) == label) return card.Press(i);
        return false;
    }

    /// <summary>Meet -> Researcher -> (preview) ; call MeetOpen next once the spot is confirmable.</summary>
    public static string MeetResearcher()
    {
        var c = C; if (c == null) return "no controller";
        var dock = Object.FindAnyObjectByType<MuseDesk.Spatial.ModeConsole>();
        if (dock == null) return "no dock";
        dock.PressByName("Meet");
        bool ok = PressKey(c.MeetCard, "Researcher");
        return "chose=" + ok + " portal=" + c.Portal.State + " msg='" + c.Portal.Message + "'";
    }

    public static string MeetOpen()
    {
        var c = C; if (c == null) return "no controller";
        bool ok = PressKey(c.VisitCard, "Open here");
        return "open=" + ok + " portal=" + c.Portal.State;
    }

    /// <summary>The visitor against the column and the pad: head top vs column top, half width vs column radius.</summary>
    public static string PortalFit()
    {
        var c = C; if (c == null) return "no controller";
        var root = GameObject.Find("PortalColumn");
        var pad = GameObject.Find("PortalPad");
        var plat = GameObject.Find("VisitorPlatform");
        Bounds av = new Bounds(); bool any = false;
        var life = c.PortalLife;
        if (life != null)
            foreach (var r in life.GetComponentsInChildren<Renderer>())
            { if (!r.enabled) continue; if (!any) { av = r.bounds; any = true; } else av.Encapsulate(r.bounds); }
        var sb = new StringBuilder("portal=" + c.Portal.State + " h=" + c.Portal.Spot.avatarHeight.ToString("F2") + " lift=" + c.PortalPlatformLift.ToString("F3"));
        if (any) sb.Append(" avatar y ").Append(av.min.y.ToString("F2")).Append("..").Append(av.max.y.ToString("F2")).Append(" width ").Append(av.size.x.ToString("F2")).Append("x").Append(av.size.z.ToString("F2"));
        if (root != null) sb.Append(" column scale ").Append(root.transform.localScale.ToString("F2")).Append(" base y ").Append(root.transform.position.y.ToString("F2")).Append(" active=").Append(root.activeInHierarchy);
        if (pad != null) sb.Append(" pad scale ").Append(pad.transform.localScale.x.ToString("F2"));
        if (plat != null) sb.Append(" platform y ").Append(plat.transform.position.y.ToString("F2"));
        return sb.ToString();
    }

    /// <summary>Aim __ShotCam at the portal from the seated user's side.</summary>
    public static string AimPortal()
    {
        var pad = GameObject.Find("PortalPad");
        Transform t = pad != null ? pad.transform : null;
        if (t == null) return "no pad";
        var cam0 = Camera.main;
        Vector3 eye = cam0 != null ? cam0.transform.position : t.position + new Vector3(1.2f, 1.2f, 0f);
        var go = GameObject.Find("__ShotCam") ?? new GameObject("__ShotCam");
        var cam = go.GetComponent<Camera>() ?? go.AddComponent<Camera>();
        cam.enabled = true; cam.depth = -100f;
        Vector3 look = t.position + Vector3.up * 0.6f;
        Vector3 dir = (look - eye); dir.y = 0f;
        cam.transform.position = look - dir.normalized * 2.2f + Vector3.up * 0.1f;
        cam.transform.rotation = Quaternion.LookRotation(look - cam.transform.position, Vector3.up);
        cam.fieldOfView = 45f; cam.nearClipPlane = 0.01f;
        cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.33f, 0.34f, 0.36f);
        return "aimed at portal";
    }

    /// <summary>Aim __ShotCam at the plan paper (from the eye side).</summary>
    public static string AimPaper()
    {
        var c = C; if (c == null || c.PlanPaper == null) return "no paper";
        Transform t = c.PlanPaper.card.root;
        var go = GameObject.Find("__ShotCam") ?? new GameObject("__ShotCam");
        var cam = go.GetComponent<Camera>() ?? go.AddComponent<Camera>();
        cam.enabled = true; cam.depth = -100f;
        cam.transform.position = t.position - t.forward * 0.42f;
        cam.transform.rotation = Quaternion.LookRotation(t.forward, t.up);
        cam.fieldOfView = 44f; cam.nearClipPlane = 0.01f;
        cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.33f, 0.34f, 0.36f);
        return "aimed at paper";
    }
}
