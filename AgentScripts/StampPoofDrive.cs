using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;
using MuseDesk.Documents;
using MuseDesk.Networking;

/// <summary>Play Mode driver for the stamps leaving after a decision and coming back (StampPoof).
/// The real path is PlayDrive.Start() ... PlayDrive.Approve()/Reject() and then State() here over
/// the next three seconds. The other methods are test aids: they move only the stamps and never
/// send or imply a decision.</summary>
public static class StampPoofDrive
{
    static Transform Find(string n) => Enumerable.Range(0, SceneManager.sceneCount).Select(SceneManager.GetSceneAt)
        .SelectMany(s => s.GetRootGameObjects()).SelectMany(r => r.GetComponentsInChildren<Transform>(true)).FirstOrDefault(t => t.name == n);

    static StampPoof Poof(string stamp) { var t = Find(stamp); return t != null ? t.GetComponent<StampPoof>() : null; }
    static StampPoof[] Both() => new[] { Poof("StampApprove"), Poof("StampReject") };

    /// <summary>Phase, scales, collider, body and pose of both stamps, plus what is pending.</summary>
    public static string State()
    {
        if (!Application.isPlaying) return "not playing";
        var sb = new StringBuilder();
        var sc = Object.FindAnyObjectByType<SessionController>();
        sb.AppendLine($"pending='{(sc != null ? sc.PendingDecisionId : "?")}' timeScale={Time.timeScale}");
        foreach (var n in new[] { "StampApprove", "StampReject" })
        {
            var p = Poof(n);
            sb.AppendLine(p != null ? p.DriverReport() : n + ": no StampPoof (run BuildStampPoofV1.Apply)");
        }
        var pad = Find("Doc_StampRestPad");
        if (pad != null) sb.AppendLine($"Doc_StampRestPad scale={pad.localScale.y:F3} (the pad stays)");
        var kit = Object.FindAnyObjectByType<MuseDesk.Spatial.NearUserKit>();
        if (kit != null) sb.AppendLine($"NearUserKit detached={kit.Detached} decisionItemsShown={kit.DecisionItemsShown}");
        return sb.ToString();
    }

    /// <summary>Both stamps leave after the usual two seconds, as after a confirmed decision.
    /// Use with nothing pending (a pending decision keeps its stamps).</summary>
    public static string Dismiss()
    {
        if (!Application.isPlaying) return "not playing";
        foreach (var p in Both()) if (p != null) p.Dismiss();
        return "dismissed; " + State();
    }

    /// <summary>Both stamps pop back in on the pad, as when a decision is requested.</summary>
    public static string Summon()
    {
        if (!Application.isPlaying) return "not playing";
        foreach (var p in Both()) if (p != null) p.Summon();
        return "summoned; " + State();
    }

    /// <summary>Pretend a hand holds the approve stamp 15 cm above its pad (the poof waits for the release).</summary>
    public static string HoldApprove()
    {
        if (!Application.isPlaying) return "not playing";
        var p = Poof("StampApprove"); if (p == null) return "no StampPoof on StampApprove";
        p.DriverHold(true);
        p.transform.localPosition += new Vector3(0.05f, 0.15f, -0.05f);
        return "holding StampApprove (simulated); " + State();
    }

    /// <summary>Let go of the approve stamp: if its time is up it goes from where it is, then is put back on the pad unseen.</summary>
    public static string ReleaseApprove()
    {
        if (!Application.isPlaying) return "not playing";
        var p = Poof("StampApprove"); if (p == null) return "no StampPoof on StampApprove";
        p.DriverHold(false);
        return "released StampApprove (simulated); " + State();
    }

    /// <summary>Grab while gone (must not be possible by hand; the fail-safe shows the stamp again).</summary>
    public static string HoldWhileGone()
    {
        if (!Application.isPlaying) return "not playing";
        var p = Poof("StampApprove"); if (p == null) return "no StampPoof on StampApprove";
        p.DriverHold(true);
        return "simulated hold on a gone stamp; " + State();
    }

    /// <summary>Editor check, outside Play Mode too: is everything the builder makes in place?</summary>
    public static string Wiring()
    {
        var sb = new StringBuilder();
        foreach (var n in new[] { "StampApprove", "StampReject" })
        {
            var t = Find(n); if (t == null) { sb.AppendLine(n + " MISSING"); continue; }
            var p = t.GetComponent<StampPoof>();
            if (p == null) { sb.AppendLine(n + ": no StampPoof"); continue; }
            var pr = p.puff != null ? p.puff.GetComponent<ParticleSystemRenderer>() : null;
            var rr = p.ring != null ? p.ring.GetComponent<ParticleSystemRenderer>() : null;
            sb.AppendLine($"{n}: controller={(p.controller != null)} tool={(p.tool != null)} body={(p.body != null)} visuals={(p.visuals != null ? string.Join("+", p.visuals.Where(v => v != null).Select(v => v.name)) : "none")} "
                        + $"puff={(p.puff != null ? p.puff.main.maxParticles + " max, mat " + (pr != null && pr.sharedMaterial != null ? pr.sharedMaterial.shader.name : "NONE") : "none")} "
                        + $"ring={(p.ring != null ? "mat " + (rr != null && rr.sharedMaterial != null ? rr.sharedMaterial.shader.name : "NONE") : "none")} "
                        + $"linger={p.lingerSeconds} leave={p.leaveSeconds} arrive={p.arriveSeconds} startHidden={p.startHidden}");
        }
        return sb.ToString();
    }
}
