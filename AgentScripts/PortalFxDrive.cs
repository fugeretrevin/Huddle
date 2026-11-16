using System.Text;
using UnityEngine;
using MuseDesk.Spatial;
using MuseDesk.Spatial.Wall;
using MuseDesk.Wall;

/// <summary>rc12 portal look-and-life checks, one call at a time so frames can be captured in between (PLAY, no hands).
///   scripts\drv.ps1 PortalFxDrive Setup            flags on, synthetic room, Wall preview
///   scripts\drv.ps1 PortalFxDrive Place            Place on the shown spot (call until placed)
///   scripts\drv.ps1 PortalFxDrive Slow 4           portal phases 4x longer (drivers only), 1 = normal
///   scripts\drv.ps1 PortalFxDrive Meet researcher  Meet -> the agent -> preview
///   scripts\drv.ps1 PortalFxDrive Open             Open here
///   scripts\drv.ps1 PortalFxDrive Touch            a fingertip pushes the visitor's chest from the user's side for 1.5 s
///   scripts\drv.ps1 PortalFxDrive Ask              ask the first preset question
///   scripts\drv.ps1 PortalFxDrive Back             Send back
///   scripts\drv.ps1 PortalFxDrive Aim user|side    aim __ShotCam (then: unity command capture game_view --camera __ShotCam)
///   scripts\drv.ps1 PortalFxDrive State</summary>
public static class PortalFxDrive
{
    static T One<T>() where T : Object => Object.FindAnyObjectByType<T>(FindObjectsInactive.Include);
    static WallHabitatController C => One<WallHabitatController>();
    static PortalTimings _normal;

    public static string Setup()
    {
        var c = C; if (c == null) return "no WallHabitatController";
        c.SetFlags(new WallFeatureFlags { wallHabitatEnabled = true, wallManagerEnabled = true, agentSelectEnabled = true, agentBtwEnabled = true, largeAgentPortalEnabled = true, wallElevatorVfxEnabled = false });
        var d = One<SpatialWorkspaceDirector>(); if (d != null) d.UseSyntheticTestRoom();
        c.StopSimulating();
        if (!c.WallActive) c.PressWall();
        return State();
    }

    public static string Place()
    {
        var c = C;
        if (c.WallActive && !c.WallPreviewing) return "placed | " + State();
        if (c.Flow != null && c.Flow.HasShown && c.Flow.Shown.valid) c.PressPlaceHere();
        return State();
    }

    public static string Slow(string factor)
    {
        var c = C; float f = 1f; float.TryParse(factor, out f); f = Mathf.Max(0.2f, f);
        var t = c.Portal.timings;
        if (_normal == null) _normal = new PortalTimings { opening = t.opening, emerging = t.emerging, returning = t.returning, closing = t.closing };
        t.opening = _normal.opening * f; t.emerging = _normal.emerging * f; t.returning = _normal.returning * f; t.closing = _normal.closing * f;
        return "portal timings x" + f;
    }

    public static string Meet(string agent)
    {
        var c = C;
        var dock = One<ModeConsole>();
        if (dock == null || !dock.PressByName("Meet")) return "no Meet key";
        var card = c.MeetCard;
        string want = string.IsNullOrEmpty(agent) ? "Researcher" : char.ToUpperInvariant(agent[0]) + agent.Substring(1).ToLowerInvariant();
        for (int i = 0; i < card.KeyCount; i++) if (card.KeyLabel(i) == want) { card.Press(i); return "chose " + want + " | " + State(); }
        return "no key " + want;
    }

    public static string Open() { return Press(C.VisitCard, "Open here"); }
    public static string Back() { C.PressReturn(); return State(); }
    public static string Ask() { return "asked=" + C.Ask(0) + " | " + State(); }

    public static string Touch()
    {
        var life = C.PortalLife;
        if (life == null) return "no visitor out";
        Camera cam = Camera.main;
        Vector3 root = life.transform.position;
        float h = life.height;
        Vector3 toUser = (cam != null ? cam.transform.position : root + Vector3.forward) - root; toUser.y = 0f; toUser.Normalize();
        life.SimulateTouch(root + Vector3.up * (h * 0.62f) + toUser * (h * 0.08f), 1.5f);
        return "touching | " + life.Describe();
    }

    static string Press(WallCard card, string label)
    {
        if (card == null) return "no card";
        for (int i = 0; i < card.KeyCount; i++) if (card.KeyLabel(i) == label) { card.Press(i); return "pressed " + label + " | " + State(); }
        return "no key " + label;
    }

    public static string Aim(string view)
    {
        var c = C;
        Camera main = Camera.main;
        Vector3 eye = main != null ? main.transform.position : new Vector3(0, 1.2f, 0);
        Vector3 spot = c.Portal.Spot.position;
        float h = Mathf.Max(0.35f, c.Portal.Spot.avatarHeight);
        Vector3 look = spot + Vector3.up * (h * 0.55f);
        Vector3 pos;
        if (view == "side")
        {
            Vector3 toUser = eye - spot; toUser.y = 0f; toUser.Normalize();
            Vector3 side = Vector3.Cross(Vector3.up, toUser);
            pos = look + (toUser * 1.1f + side * 0.9f) * Mathf.Max(1f, h) + Vector3.up * 0.2f;
        }
        else pos = eye;
        var go = GameObject.Find("__ShotCam");
        if (go == null) go = new GameObject("__ShotCam");
        var cam = go.GetComponent<Camera>(); if (cam == null) cam = go.AddComponent<Camera>();
        cam.enabled = true; cam.depth = -100f; cam.fieldOfView = view == "side" ? 45f : 70f; cam.nearClipPlane = 0.01f; cam.farClipPlane = 30f;
        cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.33f, 0.34f, 0.36f);
        go.transform.position = pos;
        go.transform.rotation = Quaternion.LookRotation(look - pos, Vector3.up);
        return "aimed " + view + " at " + look.ToString("F2");
    }

    /// <summary>Aim __ShotCam from the user's eye at a named object (a card), close enough to read it.</summary>
    public static string AimAt(string name)
    {
        Transform t = null;
        foreach (var x in Resources.FindObjectsOfTypeAll<Transform>()) if (x.name == name && x.gameObject.scene.IsValid() && x.gameObject.activeInHierarchy) { t = x; break; }
        if (t == null) return "no active " + name;
        Camera main = Camera.main;
        Vector3 eye = main != null ? main.transform.position : new Vector3(0, 1.2f, 0);
        var go = GameObject.Find("__ShotCam"); if (go == null) go = new GameObject("__ShotCam");
        var cam = go.GetComponent<Camera>(); if (cam == null) cam = go.AddComponent<Camera>();
        cam.enabled = true; cam.depth = -100f; cam.nearClipPlane = 0.01f; cam.farClipPlane = 30f;
        cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.33f, 0.34f, 0.36f);
        float dist = (t.position - eye).magnitude;
        cam.fieldOfView = Mathf.Clamp(2f * Mathf.Atan2(0.17f, dist) * Mathf.Rad2Deg, 12f, 60f);
        go.transform.position = eye;
        go.transform.rotation = Quaternion.LookRotation(t.position - eye, Vector3.up);
        return "aimed at " + name + " from " + dist.ToString("F2") + " m";
    }

    /// <summary>Key sizes on a card: each key's housing width (renderer bounds, card space) and its centre.</summary>
    public static string MeasureKeys(string cardName)
    {
        Transform card = null;
        foreach (var x in Resources.FindObjectsOfTypeAll<Transform>()) if (x.name == cardName && x.gameObject.scene.IsValid()) { card = x; break; }
        if (card == null) return "no " + cardName;
        var sb = new StringBuilder(cardName + " scale=" + card.lossyScale.x.ToString("F3") + "\n");
        foreach (Transform k in card)
        {
            if (!k.name.StartsWith("Key")) continue;
            float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
            foreach (var r in k.GetComponentsInChildren<Renderer>())
            {
                if (r is TMPro.TMP_SubMesh || r.GetComponent<TMPro.TMP_Text>() != null) continue;
                Bounds b = r.bounds;
                foreach (var corner in new[] { b.min, b.max, new Vector3(b.min.x, b.max.y, b.min.z), new Vector3(b.max.x, b.min.y, b.max.z) })
                {
                    Vector3 l = card.InverseTransformPoint(corner);
                    minX = Mathf.Min(minX, l.x); maxX = Mathf.Max(maxX, l.x); minY = Mathf.Min(minY, l.y); maxY = Mathf.Max(maxY, l.y);
                }
            }
            var lbl = k.GetComponentInChildren<TMPro.TMP_Text>();
            sb.Append("  ").Append(k.name).Append(" '").Append(lbl != null ? lbl.text : "").Append("' x=").Append(k.localPosition.x.ToString("F3"))
              .Append(" w=").Append((maxX - minX).ToString("F3")).Append(" h=").Append((maxY - minY).ToString("F3"))
              .Append(" scale=").Append(k.localScale.ToString("F3")).Append('\n');
        }
        return sb.ToString();
    }

    /// <summary>Wall aim: re-open the wall preview and point a simulated right hand from the eye at a wall (yaw degrees from
    /// forward, pitch up), optionally pinched. Reports the beam, the chip and the ghost.</summary>
    public static string Point(string spec)
    {
        var c = C;
        if (!c.WallPreviewing) { if (c.WallActive) c.PressMove(); else c.PressWall(); }
        string[] a = (spec ?? "60").Split(',');
        float yaw = 60f, pitch = 5f; bool pinch = a.Length > 2 && a[2] == "pinch";
        if (a.Length > 0) float.TryParse(a[0], out yaw);
        if (a.Length > 1) float.TryParse(a[1], out pitch);
        Camera cam = Camera.main;
        Vector3 eye = cam != null ? cam.transform.position : new Vector3(0, 1.2f, 0);
        Vector3 fwd = cam != null ? cam.transform.forward : Vector3.forward; fwd.y = 0; fwd.Normalize();
        Vector3 dir = Quaternion.AngleAxis(yaw, Vector3.up) * Quaternion.AngleAxis(-pitch, Vector3.Cross(Vector3.up, fwd)) * fwd;
        Vector3 origin = eye + Vector3.down * 0.25f + Quaternion.AngleAxis(yaw, Vector3.up) * fwd * 0.25f;
        c.SimulateHand(1, origin, dir, pinch);
        return "pointing yaw=" + yaw + " pitch=" + pitch + " pinch=" + pinch + " | beam=" + c.AimBeamShown(1) + " chip='" + c.AimChipText(1) + "' ghost='" + c.WallStatus + "'";
    }

    public static string Release() { C.SimulateRelease(1); return "released | ghost='" + C.WallStatus + "'"; }
    public static string StopHands() { C.StopSimulating(); return "hands off"; }

    // ── the small desk visitor (Select agents): one call per phase ──
    static Vector3 HeadPos() { var cam = Camera.main; return cam != null ? cam.transform.position : new Vector3(0, 1.2f, 0); }
    static Vector3 HandO() { var cam = Camera.main; Vector3 f = cam != null ? cam.transform.forward : Vector3.forward; f.y = 0; f.Normalize(); return HeadPos() + Vector3.down * 0.25f + f * 0.2f; }

    public static string Perch(string phase)
    {
        var c = C;
        Vector3 at;
        switch (phase)
        {
            case "arm":
                if (!c.Visit.IsArmed) c.PressSelectAgents();
                if (!c.TryGetAvatarCenter(HabitatRole.Developer, out at)) return "no developer";
                c.SimulateHand(1, HandO(), at - HandO(), false); break;
            case "pinch":
                if (!c.TryGetAvatarCenter(HabitatRole.Developer, out at)) return "no developer";
                c.SimulateHand(1, HandO(), at - HandO(), true); break;
            case "carry":
            {
                Vector3 perch = c.PerchWorld + Vector3.up * 0.02f;
                Vector3 o = perch - (perch - HeadPos()).normalized * 0.14f;
                c.SimulateHand(1, o, perch - o, true); break;
            }
            case "drop": c.SimulateRelease(1); break;
            case "off": c.StopSimulating(); break;
        }
        var life = c.ProxyLife;
        return "visit=" + c.Visit.State + " proxy=" + c.HasProxy + (life != null ? " | life " + life.Describe() : " | no life");
    }

    public static string PerchTouch()
    {
        var life = C.ProxyLife; if (life == null) return "no perched visitor";
        Vector3 root = life.transform.position; float h = life.height;
        Vector3 toUser = HeadPos() - root; toUser.y = 0; toUser.Normalize();
        life.SimulateTouch(root + Vector3.up * (h * 0.6f) + toUser * (h * 0.05f), 1.2f);
        return "touch | " + life.Describe();
    }

    /// <summary>Rig-local bounds of named objects (comma separated), for laying things out beside each other.</summary>
    public static string Where(string names)
    {
        var rig = C.nearUserRig;
        var sb = new StringBuilder("rig=" + (rig != null ? rig.name : "none") + "\n");
        foreach (string n in names.Split(','))
        {
            Transform t = null;
            foreach (var x in Resources.FindObjectsOfTypeAll<Transform>()) if (x.name == n && x.gameObject.scene.IsValid() && x.gameObject.activeInHierarchy) { t = x; break; }
            if (t == null) { sb.Append("  ").Append(n).Append(": none\n"); continue; }
            Bounds b = new Bounds(t.position, Vector3.zero); bool any = false;
            foreach (var r in t.GetComponentsInChildren<Renderer>()) { if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds); }
            Vector3 lc = rig != null ? rig.InverseTransformPoint(b.center) : b.center;
            Vector3 lmin = rig != null ? rig.InverseTransformPoint(b.min) : b.min, lmax = rig != null ? rig.InverseTransformPoint(b.max) : b.max;
            sb.Append("  ").Append(n).Append(": centre ").Append(lc.ToString("F3")).Append(" min ").Append(Vector3.Min(lmin, lmax).ToString("F3")).Append(" max ").Append(Vector3.Max(lmin, lmax).ToString("F3")).Append('\n');
        }
        return sb.ToString();
    }

    /// <summary>Press a key on a named card by its label (spaces as underscores).</summary>
    public static string Key(string spec)
    {
        string[] a = spec.Split(':');
        if (a.Length != 2) return "use Card:Label";
        var c = C;
        WallCard card = a[0] == "plan" ? c.PlanCard : a[0] == "visit" ? c.VisitCard : a[0] == "meet" ? c.MeetCard : a[0] == "receipt" ? c.ReceiptCard : null;
        return Press(card, a[1].Replace('_', ' '));
    }

    /// <summary>Desk: take the decision paper as a grab + release would (it unfolds to full size).</summary>
    public static string Paper()
    {
        var p = Object.FindAnyObjectByType<MuseDesk.Documents.HeldPaperController>(FindObjectsInactive.Include);
        if (p == null) return "no paper";
        bool active = p.isActiveAndEnabled;
        p.TakeNow();
        return "paper active=" + active + " taken";
    }

    /// <summary>Visible renderers under a named object with their world size (smallest first), to find stray bits.</summary>
    public static string Renderers(string name)
    {
        Transform root = null;
        foreach (var x in Resources.FindObjectsOfTypeAll<Transform>()) if (x.name == name && x.gameObject.scene.IsValid() && x.gameObject.activeInHierarchy) { root = x; break; }
        if (root == null) return "no " + name;
        var list = new System.Collections.Generic.List<Renderer>(root.GetComponentsInChildren<Renderer>());
        list.RemoveAll(r => !r.enabled || r is TMPro.TMP_SubMesh || r.GetComponent<TMPro.TMP_Text>() != null);
        list.Sort((a, b) => a.bounds.size.magnitude.CompareTo(b.bounds.size.magnitude));
        var sb = new StringBuilder();
        for (int i = 0; i < list.Count && i < 25; i++)
        {
            var r = list[i]; string path = r.name; var t = r.transform.parent; for (int k = 0; k < 3 && t != null && t != root; k++) { path = t.name + "/" + path; t = t.parent; }
            sb.Append(path).Append(" size=").Append(r.bounds.size.ToString("F4")).Append(" lossy=").Append(r.transform.lossyScale.x.ToString("G3")).Append(" mat=").Append(r.sharedMaterial != null ? r.sharedMaterial.name : "-").Append('\n');
        }
        return sb.ToString();
    }

    public static string PlanCard() { C.PressManager(); return "manager pressed | planOpen"; }

    public static string State()
    {
        var c = C; if (c == null) return "no controller";
        var sb = new StringBuilder();
        sb.Append("wall active=").Append(c.WallActive).Append(" previewing=").Append(c.WallPreviewing)
          .Append(" | portal=").Append(c.Portal.State).Append(" k=").Append(c.Portal.Progress.ToString("F2"))
          .Append(" h=").Append(c.Portal.Spot.avatarHeight.ToString("F2")).Append(" valid=").Append(c.Portal.Spot.valid)
          .Append(" pad=").Append(c.PortalPadShown).Append(" column=").Append(c.PortalColumnAlpha.ToString("F2"))
          .Append(" avatar=").Append(c.HasPortalAvatar);
        if (c.PortalLife != null) sb.Append(" | life ").Append(c.PortalLife.Describe()).Append(" pokes=").Append(c.PortalLife.Pokes)
                                     .Append(" headYaw=").Append(c.PortalLife.HeadYawFromBody.ToString("F0")).Append(" restores=").Append(c.PortalLife.Restores);
        sb.Append(" | platform=").Append(c.PortalPlatformShown).Append(" lift=").Append(c.PortalPlatformLift.ToString("F3"));
        sb.Append(" | hidden:");
        for (int r = 0; r < 4; r++) if (c.AvatarHidden((HabitatRole)r)) sb.Append(' ').Append((HabitatRole)r);
        sb.Append(" | visit=").Append(c.Visit.State).Append(" proxy=").Append(c.HasProxy);
        return sb.ToString();
    }

    /// <summary>Select ray: arm Select Agents and point a simulated right hand at a role (or "none" = off to the side).</summary>
    public static string SelectAim(string role)
    {
        var c = C;
        if (!c.Visit.IsArmed) c.PressSelectAgents();
        Vector3 o = HandO(), at;
        if (role == "none")
        {
            Vector3 f = Camera.main != null ? Camera.main.transform.forward : Vector3.forward; f.y = 0; f.Normalize();
            c.SimulateHand(1, o, (f * 0.3f + Vector3.down).normalized, false);   // at the floor in front: nothing to pick
        }
        else
        {
            HabitatRole r = (HabitatRole)System.Enum.Parse(typeof(HabitatRole), role, true);
            if (!c.TryGetAvatarCenter(r, out at)) return "no " + role;
            c.SimulateHand(1, o, at - o, false);
        }
        return "armed=" + c.Visit.IsArmed + " rayShown=" + c.SelectAimShown + " target=" + c.SelectAimTarget(1) + " beam=" + c.AimBeamShown(1) + " chip='" + c.AimChipText(1) + "'";
    }

    /// <summary>Stress the look: switch the visitor's Animator off (it stops rewriting the bones) and report how far the
    /// head is from the body, which must stay bounded (before rc14 it grew every frame: the spin).</summary>
    public static string Freeze(string on)
    {
        var life = C.PortalLife ?? C.ProxyLife; if (life == null) return "no visitor";
        var an = life.GetComponentInChildren<Animator>();
        if (an != null) an.enabled = on == "off";      // Freeze on = Animator off (bones left alone); Freeze off = back on
        return "animator enabled=" + (an != null && an.enabled) + " headYaw=" + life.HeadYawFromBody.ToString("F1") + " restores=" + life.Restores + " | " + life.Describe();
    }
}
