using System.Text;
using UnityEngine;
using MuseDesk.Spatial;
using MuseDesk.Spatial.Wall;
using MuseDesk.Wall;

/// <summary>
/// Play Mode driver for rc9's two fixes (evidence level PLAY: Editor, synthetic room, injected gaze; never on-head):
///   the guided Wall card (PlacementGuide.BeginWall / EndWall, WallHabitatController.Guide.cs), and
///   the stranded near-user panels (NearUserKit: the office left the desk before any placed desk pose).
/// Each method is ONE step and keeps no static state ('script run' compiles a fresh assembly every call). Presses go
/// through the Spaces dock (ModeConsole.PressByName) exactly as a poke would. The camera is never moved: the gaze is
/// injected into the guide.
///
///   1  Setup         flags on (driver only), synthetic room, one dock          wait until the desk is placed (15-40 s)
///   2  PressWall     expect: wall card step 1 "Look at a wall", no card keys
///   3  LookAtSpot    gaze on the outline; wait 1 s of frames, then
///   4  Report        expect: step 2 "Point and pinch to move it", body "Then press Place on the dock"
///   5  Place         expect: habitat placed, card hidden (step 0, not shown)
///   6  StrandDock    (Wall at rest) replay "left the desk before the first desk pose": rig thrown 3 m behind the head
///   7  DockReach     expect: every dock key within 0.8 m of the head (PASS / FAIL per key)
///   8  Desk          back to the desk
/// Also: CancelFlow (PressWall then Cancel: card hidden), LookAway (level gaze: the ring drains), ClearGaze.
/// </summary>
public static class WallGuideDrive
{
    static T One<T>() where T : Object => Object.FindAnyObjectByType<T>(FindObjectsInactive.Include);
    static ModeConsole Con => One<ModeConsole>();
    static SpatialWorkspaceDirector Dir => One<SpatialWorkspaceDirector>();
    static WallHabitatController WallC => One<WallHabitatController>();
    static PlacementGuide Guide => One<PlacementGuide>();
    static NearUserKit Kit => One<NearUserKit>();

    const string StandInName = "WallGuideDrive_HeadStandIn";

    public static string Help()
    {
        return "WallGuideDrive: Setup, PressWall, LookAtSpot, (wait 1 s) Report, Place, StrandDock, DockReach, Desk. Also CancelFlow, LookAway, ClearGaze.";
    }

    // ───────────────────────── steps ─────────────────────────

    public static string Setup()
    {
        if (!Application.isPlaying) return "not playing: enter Play Mode first";
        var d = Dir; var w = WallC;
        if (d == null || Con == null) return "no SpatialWorkspaceDirector or ModeConsole";
        d.SetFlags(true, true);
        d.UseSyntheticTestRoom();
        if (w != null && !w.WallEnabled)
            w.SetFlags(new WallFeatureFlags { wallHabitatEnabled = true, wallManagerEnabled = true, agentSelectEnabled = true });
        var g = Guide; if (g != null) g.InjectGaze(null);
        var p = One<MuseDesk.Workspace.WorkspacePlacement>();
        string placed = p == null ? "no WorkspacePlacement" : p.State.ToString();
        return "flags on (driver only), synthetic room. desk placement=" + placed + " (wait for PlacedManual / PlacedFromTable before PressWall)\n" + Report();
    }

    public static string PressWall()
    {
        if (!Application.isPlaying) return "not playing";
        var g = Guide; if (g != null) g.InjectGaze(null);
        Con.PressByName("Wall");
        var sb = new StringBuilder();
        Check(sb, "wall preview open", WallC != null && WallC.WallPreviewing);
        Check(sb, "card shown at step 1", g != null && g.CardShown && g.WallStep == 1);
        Check(sb, "title 'Look at a wall'", g != null && g.TitleText == PlacementGuideLogic.WallLookTitle);
        Check(sb, "no keys on the card (the dock carries Place / Next spot / Cancel)", g != null && !g.AnyKeyShown);
        return sb.ToString() + Report();
    }

    /// <summary>Inject a gaze at the outline's centre (from the guide's head). Wait ~1 s of frames, then Report.</summary>
    public static string LookAtSpot()
    {
        if (!Application.isPlaying) return "not playing";
        var g = Guide; var w = WallC;
        if (g == null || w == null) return "no guide or wall controller";
        Vector3 spot; bool valid;
        if (!w.TryGetShownSpotForDriver(out spot, out valid)) return "no outline shown: press Next spot (Con 'Next') and try again\n" + Report();
        Transform h = GuideHead(g);
        if (h == null) return "no head";
        g.InjectGaze(spot - h.position);
        return "gaze injected at the outline (" + (valid ? "fits" : "does NOT fit: the ring only fills on a spot that fits") + "). Wait 1 s, then Report.\n" + Report();
    }

    /// <summary>Level gaze straight ahead (the monitor cone: no wall spot fits there), so the ring drains.</summary>
    public static string LookAway()
    {
        var g = Guide; if (g == null) return "no guide";
        Transform h = GuideHead(g);
        Vector3 f = h != null ? h.forward : Vector3.forward; f.y = 0f;
        g.InjectGaze(f.sqrMagnitude < 1e-4f ? Vector3.forward : f.normalized);
        return Report();
    }

    public static string ClearGaze() { var g = Guide; if (g != null) g.InjectGaze(null); return Report(); }

    public static string Place()
    {
        if (!Application.isPlaying) return "not playing";
        var w = WallC;
        if (w != null && w.WallPreviewing && !w.WallCanPlace) Con.PressByName("Next");
        Con.PressByName("Place");
        var g = Guide;
        var sb = new StringBuilder();
        Check(sb, "habitat placed (Wall at rest)", w != null && w.WallActive && !w.WallPreviewing);
        Check(sb, "card hidden", g != null && !g.CardShown && g.WallStep == 0);
        if (g != null) g.InjectGaze(null);
        return sb.ToString() + Report();
    }

    public static string CancelFlow()
    {
        if (!Application.isPlaying) return "not playing";
        var g = Guide; if (g != null) g.InjectGaze(null);
        Con.PressByName("Wall");
        bool shown = g != null && g.CardShown && g.WallStep == 1;
        Con.PressByName("Cancel");
        var w = WallC;
        var sb = new StringBuilder();
        Check(sb, "card was shown at step 1 after Wall", shown);
        Check(sb, "preview closed by Cancel", w != null && !w.WallPreviewing);
        Check(sb, "card hidden after Cancel", g != null && !g.CardShown && g.WallStep == 0);
        return sb.ToString() + Report();
    }

    /// <summary>
    /// Wall at rest (after Place): replay rc9's launch path. The near-user rig forgets that it ever stood at a placed
    /// desk, is thrown 3 m behind the head, and the office "leaves the desk" again through NearUserKit's own Detach. The
    /// office is not touched. In the Editor the camera may sit at the tracking origin (not a usable head): a stand-in
    /// head object is then given to the kit (driver only), and DockReach measures from it.
    /// </summary>
    public static string StrandDock()
    {
        if (!Application.isPlaying) return "not playing";
        var kit = Kit;
        if (kit == null) return "no NearUserKit";
        string headNote;
        Transform h = UsableHead(kit, out headNote);
        if (h == null) return "no head: " + headNote;
        Vector3 f = h.forward; f.y = 0f; f = f.sqrMagnitude < 1e-4f ? Vector3.forward : f.normalized;
        Vector3 strandAt = h.position - f * 3f + Vector3.down * 1.0f;
        string r = kit.DriverLeaveDeskWithoutDeskPose(strandAt);
        return headNote + "\n" + r + "\n" + kit.DescribeReach() + "\n" + DockReach();
    }

    public static string DockReach()
    {
        if (!Application.isPlaying) return "not playing";
        var kit = Kit; var c = Con;
        if (c == null) return "no ModeConsole";
        Transform h = kit != null && kit.head != null ? kit.head : (Camera.main != null ? Camera.main.transform : null);
        if (h == null) return "no head";
        var sb = new StringBuilder();
        sb.Append("head ").Append(h.name).Append(" @").Append(h.position.ToString("F2")).Append('\n');
        Reach(sb, "dock.Desk", c.deskKey.root, h);
        Reach(sb, "dock.Floor", c.floorKey.root, h);
        Reach(sb, "dock.Ceiling", c.ceilingKey.root, h);
        Reach(sb, "dock.Wall", c.wallKey != null ? c.wallKey.root : null, h);
        var td = One<MuseDesk.Phase2.Board.TaskDock>();
        if (td != null) Reach(sb, "taskDock", td.transform, h);
        sb.Append("mode=").Append(SpatialWorkspaceState.Mode);
        if (kit != null) sb.Append(" | ").Append(kit.DescribeReach());
        return sb.ToString();
    }

    public static string Desk()
    {
        if (!Application.isPlaying) return "not playing";
        Con.PressByName("Desk");
        var g = Guide; if (g != null) g.InjectGaze(null);
        return Report();
    }

    public static string Report()
    {
        if (!Application.isPlaying) return "not playing";
        var sb = new StringBuilder();
        sb.AppendLine("WALL GUIDE DRIVE (evidence PLAY: Editor, synthetic room, injected gaze; not on-head)");
        var g = Guide;
        sb.AppendLine(g != null ? g.Describe() : "no PlacementGuide in the scene");
        var w = WallC;
        if (w != null) sb.AppendLine("wall: " + w.Describe());
        var d = Dir;
        if (d != null) sb.AppendLine("director mode=" + d.Mode + " previewing=" + d.IsPreviewing + " status='" + d.StatusText + "'");
        var c = Con;
        if (c != null) sb.AppendLine("dock status='" + c.StatusShown + "'");
        return sb.ToString();
    }

    // ───────────────────────── helpers ─────────────────────────

    static void Check(StringBuilder sb, string what, bool ok) { sb.Append(ok ? "PASS " : "FAIL ").AppendLine(what); }

    static void Reach(StringBuilder sb, string what, Transform t, Transform h)
    {
        if (t == null) { sb.Append("  ").Append(what).AppendLine(" missing"); return; }
        float d = Vector3.Distance(t.position, h.position);
        sb.Append(d <= StartupReachRule.MaxReach ? "PASS " : "FAIL ").Append(what).Append(' ').Append(d.ToString("F2")).AppendLine(" m from the head (limit 0.80)");
    }

    static Transform GuideHead(PlacementGuide g)
    {
        if (g.head != null) return g.head;
        var cam = Camera.main;
        return cam != null ? cam.transform : null;
    }

    /// <summary>The kit's head when usable; otherwise a stand-in at a seated eye height facing the camera's way (driver only).</summary>
    static Transform UsableHead(NearUserKit kit, out string note)
    {
        Transform h = kit.head != null ? kit.head : (Camera.main != null ? Camera.main.transform : null);
        if (h != null && StartupReachRule.HeadUsable(h.position, h.forward)) { note = "head: " + h.name + " (usable)"; return h; }
        GameObject go = GameObject.Find(StandInName);
        if (go == null) go = new GameObject(StandInName);
        Vector3 f = h != null ? h.forward : Vector3.forward; f.y = 0f; f = f.sqrMagnitude < 1e-4f ? Vector3.forward : f.normalized;
        Vector3 p = h != null ? h.position : Vector3.zero;
        if (p.y < 0.5f) p.y = 1.2f;
        if (p.sqrMagnitude < 0.01f) p = new Vector3(0f, 1.2f, 0f);
        go.transform.SetPositionAndRotation(p, Quaternion.LookRotation(f, Vector3.up));
        kit.head = go.transform;
        note = "head: the Editor camera was not a usable head (" + (h != null ? h.position.ToString("F2") : "none") + "); stand-in " + StandInName + " @" + p.ToString("F2") + " given to the kit (driver only)";
        return go.transform;
    }
}
