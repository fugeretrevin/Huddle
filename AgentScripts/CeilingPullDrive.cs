using System.Text;
using UnityEngine;
using MuseDesk.Spatial;

/// <summary>
/// Play Mode driver for the ceiling pull gesture. It stands in for a hand through
/// CeilingPullGesture.SimulateHand (the same Step the real hands go through) and steps the gesture
/// with a simulated clock, so each method runs a whole gesture synchronously. Get into Ceiling mode
/// first (SpatialDrive.Enable, Ceiling, Confirm) and wait for the pod to dock.
/// PullDown() needs the pod Docked; PushUp() needs it Engaged (after the descent).
///
/// rc6 hand ownership: a pinch arms only when it is fresh (hand seen open), held 0.15 s, nothing is held,
/// and the hand was clear of grabs / pokes / UI rays for 0.3 s; so every run opens the hand for 0.5 s,
/// then holds the pinch 0.25 s on the target before it travels. The gaze is simulated as looking at the
/// floor unless a method says otherwise, so only the hand ray targets the pod.
///   HeldPaperPinch    paper transition hold + a grab interactor holding: no arm, nothing moves
///   UiThenPinch       the hand was over a UI key 0.05 s before pinching: no arm
///   TargetLost        armed on the mast, then the hand ray and gaze leave the pod mid-pull: Cancelled/TargetLost
///   TrackingLostMidPull  past the full distance, then the hand is lost 0.5 s: Cancelled/Lost, never fired
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

    enum Twist { None, TargetLost, TrackingLost, UiBefore }

    // A hand 0.5 m below the mast bottom, aimed at the mast, open 0.5 s, then a pinch held 0.25 s at the
    // start, then moved by `dy` metres (negative = down) and sideways by `dx`, taking `seconds`, then released.
    static string Run(float dy, float dx, float seconds, bool release, bool aimAtMast, bool busy, int hand = 1,
                      bool holding = false, Twist twist = Twist.None)
    {
        CeilingPullGesture g; SpatialWorkspaceDirector d;
        string bad = Prep(out g, out d);
        if (bad != null) return bad;
        g.SimulateGaze(Vector3.down);                             // looking at the floor: only the hand ray targets
        g.Step(Dt);                                               // geometry for this frame
        Vector3 top, bottom, centre;
        if (!g.TryGetGeometry(out top, out bottom, out centre)) { g.ClearSimulation(); return "pod not pullable now (pod=" + (d.pod != null ? d.pod.State.ToString() : "?") + "): " + Report(); }

        Vector3 mid = (top + bottom) * 0.5f;
        Vector3 start = new Vector3(bottom.x, bottom.y - 0.5f, bottom.z + 0.35f);
        Vector3 dir = aimAtMast ? (mid - start).normalized : Vector3.down;      // down at the floor: points away from the pod
        var sb = new StringBuilder();
        int fired0 = g.FiredCount, refused0 = g.RefusedCount;
        CeilingPodState before = d.pod.State;
        g.SimulateInteraction(hand, holding, busy, false);

        for (int i = 0; i < 36; i++)
        {
            if (twist == Twist.UiBefore) g.SimulateInteraction(hand, holding, busy, i < 32);   // over a key until 0.05 s before the pinch
            g.SimulateHand(hand, start, dir, false); g.Step(Dt);
        }
        for (int i = 0; i < 18; i++) { g.SimulateHand(hand, start, dir, true); g.Step(Dt); }   // the pinch starts and debounces
        sb.Append("armed=").Append(g.Machine(hand).State).Append(" owner=").Append(g.Arbiter.Owner(hand)).Append(" block=").Append(g.Arbiter.LastBlock(hand));
        int n = Mathf.Max(2, Mathf.RoundToInt(seconds / Dt));
        Vector3 away = (mid - start).normalized; away = Quaternion.AngleAxis(60f, Vector3.up) * new Vector3(away.x, -0.6f, away.z);
        for (int i = 1; i <= n; i++)
        {
            float k = i / (float)n;
            Vector3 p = start + new Vector3(dx * k, dy * k, 0f);
            if (twist == Twist.TargetLost && k > 0.4f) g.SimulateHand(hand, p, away, true);
            else g.SimulateHand(hand, p, dir, true);
            g.Step(Dt);
        }
        sb.Append(" pulled=").Append(g.Machine(hand).State).Append(" p=").Append(g.Machine(hand).Progress.ToString("F2"));
        if (twist == Twist.TrackingLost)
        {
            for (int i = 0; i < 36; i++) { g.SimulateHandLost(hand); g.Step(Dt); }
            sb.Append(" afterLoss=").Append(g.Machine(hand).State).Append("/").Append(g.Machine(hand).LastCancel);
        }
        if (release) { for (int i = 0; i < 12; i++) { g.SimulateHand(hand, start + new Vector3(dx, dy, 0f), dir, false); g.Step(Dt); } }
        sb.Append(" end=").Append(g.Machine(hand).State).Append("/").Append(g.Machine(hand).LastCancel);
        g.Step(Dt);
        g.ClearSimulation();
        sb.Append(" | firedNow=").Append(g.FiredCount - fired0).Append(" refusedNow=").Append(g.RefusedCount - refused0)
          .Append(" cancels=").Append(g.Machine(hand).Cancels)
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

    /// <summary>Aim away from the pod (gaze on the floor too) and pull: expect no arm (armed=Idle), nothing moves.</summary>
    public static string NotTargeted() { return Run(-0.14f, 0f, 0.6f, true, false, false); }

    /// <summary>The hand is hovering a paper (simulated): the pinch is spent (block=CloseInteraction), nothing moves.</summary>
    public static string BusyHand() { return Run(-0.14f, 0f, 0.6f, true, true, true); }

    /// <summary>A paper in hand: its transition hold AND a grab interactor holding on the pinching hand.
    /// Expect armed=Idle owner=Holding block=Holding, firedNow=0, pod unchanged. Releases the hold afterwards.</summary>
    public static string HeldPaperPinch()
    {
        SpatialWorkspaceState.AcquireTransitionHold();
        try { return "HeldPaperPinch: " + Run(-0.14f, 0f, 0.6f, true, true, false, 1, true); }
        finally { SpatialWorkspaceState.ReleaseTransitionHold(); }
    }

    /// <summary>A transition hold only (a paper in hand, no grab interactor): the pinch is ignored. Releases the hold afterwards.</summary>
    public static string Held()
    {
        SpatialWorkspaceState.AcquireTransitionHold();
        try { return Run(-0.14f, 0f, 0.6f, true, true, false); }
        finally { SpatialWorkspaceState.ReleaseTransitionHold(); }
    }

    /// <summary>The finger was over a UI key until 0.05 s before the pinch. Expect armed=Idle block=Ui, firedNow=0.</summary>
    public static string UiThenPinch() { return "UiThenPinch: " + Run(-0.14f, 0f, 0.6f, true, true, false, 1, false, Twist.UiBefore); }

    /// <summary>Armed on the mast; after 40% of the pull the hand ray swings 60 degrees off the pod (gaze on the floor).
    /// Expect end=Cancelled/TargetLost, firedNow=0, pod unchanged.</summary>
    public static string TargetLost() { return "TargetLost: " + Run(-0.14f, 0f, 0.8f, true, true, false, 1, false, Twist.TargetLost); }

    /// <summary>Pull the full 14 cm, then the hand is untracked for 0.5 s and comes back open.
    /// Expect afterLoss=Cancelled/Lost, firedNow=0, pod unchanged (a lost hand is never a release).</summary>
    public static string TrackingLostMidPull() { return "TrackingLostMidPull: " + Run(-0.14f, 0f, 0.6f, true, true, false, 1, false, Twist.TrackingLost); }

    public static string Clear() { var g = One<CeilingPullGesture>(); if (g != null) g.ClearSimulation(); return "simulation cleared"; }
}
