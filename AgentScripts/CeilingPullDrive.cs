using System.Text;
using UnityEngine;
using MuseDesk.Spatial;

/// <summary>
/// Play Mode driver for the ceiling pull gesture. It stands in for a hand through
/// CeilingPullGesture.SimulateHand (the same Step the real hands go through) and steps the gesture
/// with a simulated clock, so each method runs a whole gesture synchronously. Get into Ceiling mode
/// first (SpatialDrive.Enable, Ceiling, Confirm) and wait for the pod to dock.
/// PullDown() needs the pod Docked; PushUp() needs it Engaged (after the descent).
/// </summary>
public static class CeilingPullDrive
{
    const float Dt = 1f / 72f;
    static T One<T>() where T : Object => Object.FindAnyObjectByType<T>(FindObjectsInactive.Include);

    static string Prep(out CeilingPullGesture g, out SpatialWorkspaceDirector d)
    {
        g = One<CeilingPullGesture>(); d = One<SpatialWorkspaceDirector>();
        if (!Application.isPlaying) return "not playing";
        if (g == null) return "no CeilingPullGesture in the scene (run BuildCeilingPullV1)";
        if (d == null) return "no director";
        if (d.Mode != WorkspaceMode.Ceiling) return "not in Ceiling mode (mode=" + d.Mode + ")";
        return null;
    }

    public static string Report()
    {
        var g = One<CeilingPullGesture>(); var d = One<SpatialWorkspaceDirector>();
        if (!Application.isPlaying) return "not playing";
        if (g == null) return "no CeilingPullGesture";
        return g.Describe() + " | mode=" + (d != null ? d.Mode.ToString() : "?") + " pod=" + (d != null && d.pod != null ? d.pod.State.ToString() : "?")
               + " held=" + SpatialWorkspaceState.TransitionHeld + " status='" + (d != null ? d.StatusText : "") + "'";
    }

    // A hand 0.5 m below the mast bottom, aimed at the mast, then a pinch that moves by `dy` metres
    // (negative = down) and sideways by `dx`, taking `seconds`, then released. Returns the outcome.
    static string Run(float dy, float dx, float seconds, bool release, bool aimAtMast, bool busy, int hand = 1)
    {
        CeilingPullGesture g; SpatialWorkspaceDirector d;
        string bad = Prep(out g, out d);
        if (bad != null) return bad;
        g.Step(Dt);                                               // geometry for this frame
        Vector3 top, bottom, centre;
        if (!g.TryGetGeometry(out top, out bottom, out centre)) return "pod not pullable now (pod=" + (d.pod != null ? d.pod.State.ToString() : "?") + "): " + Report();

        Vector3 mid = (top + bottom) * 0.5f;
        Vector3 start = new Vector3(bottom.x, bottom.y - 0.5f, bottom.z + 0.35f);
        Vector3 dir = aimAtMast ? (mid - start).normalized : Vector3.down;      // down at the floor: points away from the pod
        var sb = new StringBuilder();
        int fired0 = g.FiredCount, refused0 = g.RefusedCount;
        CeilingPodState before = d.pod.State;
        g.SimulateBusy(hand, busy);

        for (int i = 0; i < 4; i++) { g.SimulateHand(hand, start, dir, false); g.Step(Dt); }
        g.SimulateHand(hand, start, dir, true); g.Step(Dt);          // the pinch starts here
        sb.Append("armed=").Append(g.Machine(hand).State);
        int n = Mathf.Max(2, Mathf.RoundToInt(seconds / Dt));
        for (int i = 1; i <= n; i++)
        {
            float k = i / (float)n;
            g.SimulateHand(hand, start + new Vector3(dx * k, dy * k, 0f), dir, true);
            g.Step(Dt);
        }
        sb.Append(" pulled=").Append(g.Machine(hand).State).Append(" p=").Append(g.Machine(hand).Progress.ToString("F2"));
        if (release) { g.SimulateHand(hand, start + new Vector3(dx, dy, 0f), dir, false); g.Step(Dt); }
        sb.Append(" end=").Append(g.Machine(hand).State).Append("/").Append(g.Machine(hand).LastCancel);
        g.Step(Dt);
        g.ClearSimulation();
        sb.Append(" | firedNow=").Append(g.FiredCount - fired0).Append(" refusedNow=").Append(g.RefusedCount - refused0)
          .Append(" pod ").Append(before).Append("->").Append(d.pod.State).Append(" reason='").Append(g.Reason).Append("'");
        return sb.ToString();
    }

    /// <summary>Docked: pinch on the mast, pull down 14 cm in 0.6 s, release. Expect Fired and the pod PreparingToDescend.</summary>
    public static string PullDown() { return Run(-0.14f, 0f, 0.6f, true, true, false); }

    /// <summary>Engaged: pinch on the pod, push up 14 cm in 0.6 s, release. Expect Fired and the pod PreparingToRetract.</summary>
    public static string PushUp() { return Run(0.14f, 0f, 0.6f, true, true, false); }

    /// <summary>Release after 6 cm: expect Cancelled/Released, nothing moves.</summary>
    public static string EarlyRelease() { return Run(-0.06f, 0f, 0.4f, true, true, false); }

    /// <summary>Drift 30 cm sideways while pulling: expect Cancelled/Sideways.</summary>
    public static string Sideways() { return Run(-0.14f, 0.30f, 0.6f, true, true, false); }

    /// <summary>Pull 14 cm over 2 s: expect Cancelled/Timeout.</summary>
    public static string TooSlow() { return Run(-0.14f, 0f, 2.0f, true, true, false); }

    /// <summary>Aim away from the pod and pull: expect no arm.</summary>
    public static string NotTargeted() { return Run(-0.14f, 0f, 0.6f, true, false, false); }

    /// <summary>The hand is hovering a paper (simulated): the pinch is ignored.</summary>
    public static string BusyHand() { return Run(-0.14f, 0f, 0.6f, true, true, true); }

    /// <summary>A transition hold is set (a paper in hand): the pinch is ignored. Releases the hold afterwards.</summary>
    public static string Held()
    {
        SpatialWorkspaceState.AcquireTransitionHold();
        string r = Run(-0.14f, 0f, 0.6f, true, true, false);
        SpatialWorkspaceState.ReleaseTransitionHold();
        return r;
    }

    public static string Clear() { var g = One<CeilingPullGesture>(); if (g != null) g.ClearSimulation(); return "simulation cleared"; }
}
