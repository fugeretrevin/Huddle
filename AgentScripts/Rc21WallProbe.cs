using System.Reflection;
using System.Text;
using UnityEngine;
using MuseDesk.Spatial.Wall;
using MuseDesk.Wall;

/// <summary>
/// rc21 Play Mode probes for the Wall Habitat (owner, worn rc20): the carried paper rides the hand, no more walk to the lift
/// for a hand-off (tubes), a question walk faces you with a label, tube labels. Evidence level: PLAY (Editor) only.
/// Set up first with WallDrive: Enable, Enter, Place. Then, letting a second or more of frames run between calls:
///   Report                        - mail / label counters, the last label, each role's carry bone + grip, walkers
///   Paper from:to:kind            - TEST ONLY: a real-shaped HabitatEvent through the controller's own routing
///                                   (OnHabitatEvent, by reflection). kind = Handoff | TaskCompleted | TaskFailed | ApprovalRequested.
///                                   e.g. "developer:tester:Handoff" (must go by TUBE now, no walk), "tester:manager:TaskFailed",
///                                   "developer:manager:ApprovalRequested" (the one walk left: a question label, faces you)
///   Label from:to                 - the view's labelled tube directly: "researcher:manager"
///   Burst                         - five labelled tubes at once: at most 3 labels live
///   Prop role                     - the carried paper vs the hand bone right now (call several times during a walk:
///                                   'grip' stays near-constant while 'world rot' changes = attached)
/// </summary>
public static class Rc21WallProbe
{
    static T One<T>() where T : Object => Object.FindAnyObjectByType<T>(FindObjectsInactive.Include);
    static WallHabitatController C => One<WallHabitatController>();

    public static string Report()
    {
        if (!Application.isPlaying) return "not playing";
        var c = C; if (c == null || c.View == null || c.Stage == null) return "no habitat (WallDrive Enable, Enter, Place first)";
        WallHabitatView v = c.View;
        var sb = new StringBuilder();
        sb.Append("mailSent=" + v.MailSent + " inFlight=" + v.MailInFlight + " papersByTube=" + c.PapersByTube);
        sb.Append(" | labelsShown=" + v.LabelsShown + " live=" + v.LabelsLive + " tubeLive=" + v.TubeLabelsLive + " last='" + v.LastLabel + "'");
        for (int r = 0; r < 4; r++)
        {
            var role = (HabitatRole)r;
            var w = c.Stage.Walker(role);
            var act = c.Stage.Running(role);
            sb.Append(" | " + role + ": " + w.State + " yaw=" + w.Yaw.ToString("F0") + (act != null ? " act=" + act.kind : "")
                      + " hand=" + (v.CarryHandName(role) ?? "none") + " grip=" + v.CarryGripReady(role));
        }
        return sb.ToString();
    }

    public static string Paper(string spec)
    {
        var c = C; if (c == null || c.Stage == null) return "no habitat";
        var p = spec.Split(':');
        if (p.Length < 3) return "format from:to:kind";
        HabitatRole a, b; HabitatEventKind k;
        if (!HabitatStage.TryRole(p[0], out a) || !HabitatStage.TryRole(p[1], out b)) return "bad role";
        if (!System.Enum.TryParse(p[2], out k)) return "bad kind";
        var e = new HabitatEvent { eventId = "rc21probe-" + System.Guid.NewGuid().ToString("N"), kind = k, agent = a, target = b, hasTarget = k == HabitatEventKind.Handoff };
        int before = c.PapersByTube;
        MethodInfo m = typeof(WallHabitatController).GetMethod("OnHabitatEvent", BindingFlags.Instance | BindingFlags.NonPublic);
        if (m == null) return "OnHabitatEvent not found";
        m.Invoke(c, new object[] { e });
        bool tubed = c.PapersByTube > before;
        var act = c.Stage.Running(a);
        return "injected(test) " + spec + " byTube=" + tubed + " walkQueued=" + (c.Stage.Pending(a) > 0 || act != null) + " | " + Report();
    }

    public static string Label(string spec)
    {
        var c = C; if (c == null || c.View == null) return "no habitat";
        var p = spec.Split(':');
        HabitatRole a, b;
        if (p.Length < 2 || !HabitatStage.TryRole(p[0], out a) || !HabitatStage.TryRole(p[1], out b)) return "format from:to";
        c.View.labelFont = c.font;
        c.View.labelPlateMaterial = WallUi.PlateMaterial;
        bool ok = c.View.SendUpTube(a, b, WallHabitatView.Paper, a + " → " + b + ": probe label (test)");
        return "sent=" + ok + " | " + Report();
    }

    public static string Burst()
    {
        var c = C; if (c == null || c.View == null) return "no habitat";
        c.View.labelFont = c.font;
        c.View.labelPlateMaterial = WallUi.PlateMaterial;
        HabitatRole[] from = { HabitatRole.Researcher, HabitatRole.Developer, HabitatRole.Tester, HabitatRole.Researcher, HabitatRole.Developer };
        for (int i = 0; i < from.Length; i++) c.View.SendUpTube(from[i], HabitatRole.Manager, WallHabitatView.Paper, from[i] + " → Manager: burst " + (i + 1) + " (test)");
        return "burst of 5; tube labels live=" + c.View.TubeLabelsLive + " (expect 3) | " + Report();
    }

    public static string Prop(string role)
    {
        var c = C; if (c == null || c.View == null) return "no habitat";
        HabitatRole r; if (!HabitatStage.TryRole(role, out r)) return "bad role";
        Transform prop = c.View.CarriedProp(r), av = c.View.Avatar(r);
        if (prop == null || av == null) return "no prop/avatar";
        string handName = c.View.CarryHandName(r);
        Transform hand = null;
        if (handName != null) foreach (var t in av.GetComponentsInChildren<Transform>(true)) if (t.name == handName) { hand = t; break; }
        var sb = new StringBuilder(role + " carrying=" + prop.gameObject.activeSelf + " gripReady=" + c.View.CarryGripReady(r));
        sb.Append(" propLocal=" + prop.localPosition.ToString("F3") + " propLocalRot=" + prop.localEulerAngles.ToString("F0") + " world rot=" + prop.rotation.eulerAngles.ToString("F0"));
        if (hand != null)
        {
            sb.Append(" | hand=" + hand.name + " dist=" + (prop.position - hand.position).magnitude.ToString("F4") + "m");
            sb.Append(" grip(rot in hand frame)=" + (Quaternion.Inverse(hand.rotation) * prop.rotation).eulerAngles.ToString("F0"));
        }
        var w = c.Stage.Walker(r);
        sb.Append(" | walker " + w.State + " anim=" + w.Anim + " speed=" + w.Speed.ToString("F3") + " yaw=" + w.Yaw.ToString("F0"));
        return sb.ToString();
    }
}
