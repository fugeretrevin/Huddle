using System.Linq;
using System.Text;
using UnityEngine;
using MuseDesk.Spatial;
using MuseDesk.Spatial.Wall;
using MuseDesk.Wall;

/// <summary>
/// Play Mode driver for the Wall Habitat (synthetic room, simulated hands). Each method is one step; let a
/// second or so of frames run between steps. Evidence level: PLAY (Editor), never on-head.
///   Enable, Enter, Place, Report, Hand <Role>, Pinch <Role>, ToPerch, Open, Lost, Ask <0-2>, MeetLarger,
///   PortalOpen, SendBack, Return, Plan <0-2>, Approve, Event <kind:from:to>, Tasks, Desk, Disable
/// </summary>
public static class WallDrive
{
    static T One<T>() where T : Object => Object.FindAnyObjectByType<T>(FindObjectsInactive.Include);
    static WallHabitatController C => One<WallHabitatController>();

    public static string Report()
    {
        if (!Application.isPlaying) return "not playing";
        var c = C;
        if (c == null) return "no WallHabitatController";
        var sb = new StringBuilder(c.Describe());
        var root = c.transform.Find("WallHabitatRoot");
        if (root != null) sb.Append(" | habitat@" + root.position.ToString("F2") + " fwd=" + root.forward.ToString("F2") + " active=" + root.gameObject.activeInHierarchy);
        var cam = Camera.main;
        if (cam != null) sb.Append(" head=" + cam.transform.position.ToString("F2"));
        var dock = c.nearUserRig != null ? c.nearUserRig.Find("WallDock") : null;
        if (dock != null)
        {
            var shown = dock.Cast<Transform>().Where(t => t.name.StartsWith("Key_") && t.localScale.x > 0.001f).Select(t => t.name.Substring(4));
            sb.Append(" | dock keys=[" + string.Join(",", shown) + "]");
        }
        if (c.HasProxy) sb.Append(" | proxy@" + c.ProxyPosition.ToString("F2") + " perch@" + c.PerchWorld.ToString("F2") + " d=" + (c.ProxyPosition - c.PerchWorld).magnitude.ToString("F3"));
        if (c.Side != null && c.Side.History.Count > 0) { var x = c.Side.History[c.Side.History.Count - 1]; sb.Append(" | btw[" + x.provenance + "/" + x.status + "] " + Trim(x.reply, 90)); }
        if (c.VisitCard != null && c.VisitCard.Visible) sb.Append(" | visitCard='" + Trim(c.VisitCard.Title.text, 40) + "' keys=" + Keys(c.VisitCard));
        if (c.PlanCard != null && c.PlanCard.Visible) sb.Append(" | planCard='" + Trim(c.PlanCard.Title.text, 50) + "' keys=" + Keys(c.PlanCard));
        if (c.HasPortalAvatar) sb.Append(" | portalAvatar h=" + c.PortalAvatarHeight.ToString("F2") + " at " + c.Portal.Spot.position.ToString("F2"));
        return sb.ToString();
    }

    static string Keys(WallCard card) { var l = Enumerable.Range(0, card.KeyCount).Select(card.KeyLabel).Where(s => s != null); return "[" + string.Join(",", l) + "]"; }
    static string Trim(string s, int n) { if (string.IsNullOrEmpty(s)) return ""; s = s.Replace("\n", " / "); return s.Length <= n ? s : s.Substring(0, n) + "..."; }

    public static string Enable()
    {
        var c = C;
        c.SetFlags(new WallFeatureFlags { wallHabitatEnabled = true, wallManagerEnabled = true, agentSelectEnabled = true, agentBtwEnabled = true, largeAgentPortalEnabled = true, wallElevatorVfxEnabled = false });
        var d = One<SpatialWorkspaceDirector>();
        d.UseSyntheticTestRoom();
        return "wall flags on (driver only), synthetic room. " + Report();
    }

    public static string Disable() { C.SetFlags(new WallFeatureFlags()); return Report(); }
    public static string Enter() { C.PressWall(); return Report(); }
    public static string Other() { C.PressTryAnother(); return Report(); }
    public static string Layout() { C.PressLayout(); return Report(); }
    public static string Cancel() { C.PressCancel(); return Report(); }
    public static string Place() { C.PressPlaceHere(); return Report(); }
    public static string Move() { C.PressMove(); return Report(); }
    public static string Desk() { C.PressDesk(); return Report(); }
    public static string Select() { C.PressSelectAgents(); return Report(); }

    static Vector3 Head() { var cam = Camera.main; return cam != null ? cam.transform.position : new Vector3(0, 1.2f, 0); }

    /// <summary>Point the right hand at an agent of the habitat, pinch open.</summary>
    public static string Hand(string role) { return Point(role, false); }
    /// <summary>Same, pinched.</summary>
    public static string Pinch(string role) { return Point(role, true); }

    static string Point(string role, bool pinch)
    {
        HabitatRole r; if (!HabitatStage.TryRole(role, out r)) return "unknown role " + role;
        Vector3 at; if (!C.TryGetAvatarCenter(r, out at)) return "no avatar";
        Vector3 o = Head() + Vector3.down * 0.25f + Vector3.right * 0.15f;
        _lastO = o; _lastD = at - o; C.SimulateHand(1, o, at - o, pinch);
        return "hand -> " + role + " pinch=" + pinch + " | " + Report();
    }

    /// <summary>Keep pinching, point so the carried projection comes over the desk perch.</summary>
    public static string ToPerch()
    {
        Vector3 perch = C.PerchWorld + Vector3.up * 0.02f;
        Vector3 o = perch - (perch - Head()).normalized * 0.14f;      // the carry point is 0.14 m along the ray
        _lastO = o; _lastD = perch - o; C.SimulateHand(1, o, perch - o, true);
        return Report();
    }

    public static string ToNowhere()
    {
        Vector3 o = Head() + Vector3.down * 0.2f;
        _lastO = o; _lastD = Vector3.up + Vector3.forward; C.SimulateHand(1, o, _lastD, true);
        return Report();
    }

    static Vector3 _lastO, _lastD = Vector3.forward;
    /// <summary>Open the pinch where the hand is (a real hand barely moves when the fingers open).</summary>
    public static string Open() { C.SimulateRelease(1); return Report(); }
    public static string Lost() { C.SimulateHandLost(1); return Report(); }
    public static string HandsReal() { C.StopSimulating(); return Report(); }

    public static string Ask(string i) { int n; int.TryParse(i, out n); bool ok = C.Ask(n); return "ask " + n + " sent=" + ok + " | " + Report(); }
    public static string AskChange() { bool ok = C.AskText("Stop that and switch to OAuth instead"); return "change request sent=" + ok + " | " + Report(); }
    public static string MeetLarger() { C.PressMeetLarger(); return Report(); }
    public static string PortalOpen() { C.Portal.Confirm(); return Report(); }
    public static string SendBack() { C.PressReturn(); return Report(); }
    public static string Return() { C.PressReturn(); return Report(); }

    public static string Plan(string i) { int n; int.TryParse(i, out n); C.PressManager(); if (!C.PlanCard.Visible) C.PressManager(); bool ok = C.RequestPlan(n == 1 ? "Find and fix the failing tests" : n == 2 ? "Deploy the app to production" : "Build the login flow, use the existing repo, and have Tester verify it"); return "plan requested=" + ok + " | " + Report() + " | " + PlanReport(); }
    public static string Approve() { bool ok = C.ApprovePlan(); return "approve=" + ok + " | " + PlanReport(); }
    public static string ConfirmRisk() { C.ConfirmRisky(); return PlanReport(); }
    public static string Toggle(string i) { int n; int.TryParse(i, out n); C.ToggleItem(n); return PlanReport(); }

    public static string PlanReport()
    {
        var m = C.Manager;
        var sb = new StringBuilder("manager=" + m.State + " plan=" + m.PlanId + " rev=" + m.Revision + " prov=" + m.Provenance + " committed=" + m.CommittedCount + " printed=" + m.PrintedCount + " msg='" + m.Message + "'");
        foreach (var it in m.Items) sb.Append(" | " + it.clientItemId + ":" + it.title + "->" + it.proposedAgent + (it.approved ? "[x]" : "[ ]") + (it.dangerous ? "(risk " + (it.authorized ? "ok" : "needed") + ")" : ""));
        var v = C.View;
        if (v != null) sb.Append(" | desk cards R=" + v.CardsOnDesk(HabitatRole.Researcher) + " D=" + v.CardsOnDesk(HabitatRole.Developer) + " T=" + v.CardsOnDesk(HabitatRole.Tester));
        return sb.ToString();
    }

    /// <summary>TEST ONLY: inject a habitat event straight into the stage (labelled; the real path is WallEventBridge).
    /// Format kind:from[:to], e.g. Handoff:researcher:manager, TaskFailed:tester.</summary>
    public static string Event(string spec)
    {
        var p = spec.Split(':');
        HabitatEventKind k; if (!System.Enum.TryParse(p[0], out k)) return "bad kind";
        HabitatRole a, b = HabitatRole.Manager;
        if (!HabitatStage.TryRole(p[1], out a)) return "bad role";
        bool hasB = p.Length > 2 && HabitatStage.TryRole(p[2], out b);
        var e = new HabitatEvent { eventId = "drive-" + System.Guid.NewGuid().ToString("N"), kind = k, agent = a, target = b, hasTarget = hasB };
        bool ok = C.Stage != null && C.Stage.Accept(e, Time.unscaledTime);
        return "injected(test) " + spec + " accepted=" + ok + " | " + Walkers();
    }

    public static string Walkers()
    {
        var s = C.Stage; if (s == null) return "no stage";
        var sb = new StringBuilder();
        for (int r = 0; r < 4; r++)
        {
            var w = s.Walker((HabitatRole)r);
            sb.Append((HabitatRole)r + ":" + w.State + "@" + w.Position.ToString("F3") + " soft=" + w.SoftTransfers + (w.LastBlock != null ? " block=" + w.LastBlock : "") + "; ");
        }
        sb.Append("elevator=" + s.elevator.State + " y=" + s.elevator.CabinY.ToString("F3") + " completed=" + s.Completed + " dropped=" + s.Dropped);
        return sb.ToString();
    }

    /// <summary>The canonical task state of every agent (from the reducers). Compare before/after a visit: must not change.</summary>
    public static string Tasks()
    {
        var bridge = One<WallEventBridge>();
        var sb = new StringBuilder("tasks(" + bridge.SourceModeLabel + "): ");
        foreach (var a in new[] { "researcher", "developer", "tester", "manager" })
        {
            string id, title, state; int rev;
            sb.Append(a + "=" + (bridge.TryGetAgentTask(a, out id, out title, out state, out rev) ? id + "/" + state + "/r" + rev : "none") + "; ");
        }
        return sb.ToString();
    }
}
