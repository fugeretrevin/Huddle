using System.Text;
using UnityEngine;
using MuseDesk.Voice;

/// <summary>
/// rc21 Play Mode probes for voice (no microphone, no network needed except for Say).
///
/// Readout placement ("the panel for speech is still clipping"):
///   ShowReadout        show the readout with a long line (as while listening)
///   ShowPrompt         show it with the Confirm / Cancel keys (as for "new task")
///   Readout            where it is (vs the eyes and the mic key) and every renderer its box still intersects
///   HideReadout
///
/// One sentence across a pause ("it's not waiting and reading my full sentence"), through the real WitVoiceSource +
/// VoiceCommandCenter path with fake service events:
///   Stitch "Manager, build a program"     open a request; the service calls that piece final (and repeats it)
///   StitchMore "that prints stars"        the microphone reopens and the user goes on (partial, then final)
///   StitchState                            call ~2 s later: the sentence the command centre received, in one piece
///   Short "stop"                           a short command: must be delivered at once (StitchState right after)
///   Verdict "Manager"                      the command centre's opinion of a piece (Complete / Incomplete / Unknown)
///
/// Agent voices ("cuter and a bit more robotic"):
///   Voices                                 the per-agent table
///   Say "developer|The build is green."   speak a line as that agent, 1 m in front of the camera (needs internet)
/// </summary>
public static class Rc21VoiceProbe
{
    static VoiceHud Hud => Object.FindAnyObjectByType<VoiceHud>(FindObjectsInactive.Include);
    static WitVoiceSource Src => Object.FindAnyObjectByType<WitVoiceSource>(FindObjectsInactive.Include);

    public static string ShowReadout()
    {
        if (!Application.isPlaying) return "not playing";
        var h = Hud; if (h == null) return "no VoiceHud";
        h.Show("Listening", "", "\"Manager, build a program that prints stars in a triangle and asks how tall it should be\"",
               "Try: \"Researcher, why?\"", VoiceTone.Listening);
        return "shown; placedForEye=" + h.PlacedForEye + " placeCount=" + h.PlaceCount + " (call Readout in a moment)";
    }

    public static string ShowPrompt()
    {
        if (!Application.isPlaying) return "not playing";
        var h = Hud; if (h == null) return "no VoiceHud";
        h.Show("New task", "", "\"Write the release notes\"\nSay confirm or cancel.", "", VoiceTone.Warn, "Confirm", "Cancel");
        return "shown with keys; placedForEye=" + h.PlacedForEye + " (call Readout in a moment)";
    }

    public static string HideReadout()
    {
        var h = Hud; if (h == null) return "no VoiceHud";
        h.Hide();
        return "hidden";
    }

    public static string Readout()
    {
        if (!Application.isPlaying) return "not playing";
        var h = Hud; if (h == null || h.readout == null) return "no VoiceHud / readout";
        var sb = new StringBuilder();
        Transform r = h.readout;
        Camera cam = Camera.main;
        Bounds b = BoundsOf(r);
        sb.Append("shown=").Append(h.ReadoutShown).Append(" placedForEye=").Append(h.PlacedForEye).Append(" placeCount=").Append(h.PlaceCount)
          .Append(" nudge=").Append(h.NudgeMetres.ToString("F3"))
          .Append(" scale=").Append(r.localScale.x.ToString("F3"))
          .Append(" centre=").Append(b.center.ToString("F3")).Append(" size=").Append(b.size.ToString("F3"));
        sb.Append(" micKey=").Append(h.transform.position.ToString("F3"));
        if (cam != null)
        {
            Vector3 e = cam.transform.position;
            sb.Append(" eye=").Append(e.ToString("F3")).Append(" dist=").Append(Vector3.Distance(e, b.center).ToString("F3"))
              .Append(" belowEye=").Append((e.y - b.center.y).ToString("F3"))
              .Append(" facing=").Append(Vector3.Dot(r.forward, (b.center - e).normalized).ToString("F2"));
        }
        if (h.confirmKey != null) sb.Append(" confirm=").Append(h.confirmKey.position.ToString("F3")).Append(h.ConfirmKeyShown ? "(up)" : "(hidden)");
        if (h.cancelKey != null) sb.Append(" cancel=").Append(h.cancelKey.position.ToString("F3")).Append(h.CancelKeyShown ? "(up)" : "(hidden)");
        sb.Append("\nintersecting:");
        int n = 0;
        foreach (var rr in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
        {
            if (!rr.enabled || !rr.gameObject.activeInHierarchy) continue;
            Transform t = rr.transform;
            if (t.IsChildOf(r) || (h.confirmKey != null && t.IsChildOf(h.confirmKey)) || (h.cancelKey != null && t.IsChildOf(h.cancelKey))) continue;
            Bounds o = rr.bounds;
            if (o.size.sqrMagnitude < 1e-8f || o.size.x > 3f || o.size.z > 3f) continue;      // room meshes, passthrough planes
            if (!o.Intersects(b)) continue;
            sb.Append("\n  ").Append(Path(t)).Append(" size=").Append(o.size.ToString("F3"));
            if (++n >= 25) { sb.Append("\n  ..."); break; }
        }
        if (n == 0) sb.Append(" none");
        return sb.ToString();
    }

    static Bounds BoundsOf(Transform root)
    {
        var rs = root.GetComponentsInChildren<Renderer>();
        if (rs.Length == 0) return new Bounds(root.position, Vector3.zero);
        Bounds b = rs[0].bounds;
        for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
        return b;
    }

    static string Path(Transform t)
    {
        string p = t.name;
        for (int i = 0; i < 4 && t.parent != null; i++) { t = t.parent; p = t.name + "/" + p; }
        return p;
    }

    // ───────────────────────── stitching ─────────────────────────

    /// <summary>"first piece|second piece" in one go (the service cuts at the pause, the user goes on); or just a first piece
    /// (then StitchMore within ~1.5 s, or StitchState to see it delivered alone after the grace).</summary>
    public static string Stitch(string pieces = null)
    {
        if (!Application.isPlaying) return "not playing";
        var s = Src; if (s == null) return "no WitVoiceSource";
        string arg = string.IsNullOrEmpty(pieces) ? "Manager, build a program|that prints stars" : pieces;
        string[] parts = arg.Split('|');
        s.DriverOpen();
        s.DriverPiece(parts[0].Trim());
        for (int i = 1; i < parts.Length; i++)
        {
            string p = parts[i].Trim();
            s.DriverMicReopened();
            s.DriverPartial(p.Split(' ')[0]);
            s.DriverPiece(p);
        }
        return "queued " + parts.Length + " piece(s); call StitchState after ~2 s. " + s.StitchState;
    }

    public static string StitchMore(string piece = null)
    {
        if (!Application.isPlaying) return "not playing";
        var s = Src; if (s == null) return "no WitVoiceSource";
        string p = string.IsNullOrEmpty(piece) ? "that prints stars" : piece;
        s.DriverMicReopened();
        s.DriverPartial(p.Split(' ')[0]);
        s.DriverPiece(p);
        return "reopened + piece queued. " + s.StitchState;
    }

    public static string Short(string word = null)
    {
        if (!Application.isPlaying) return "not playing";
        var s = Src; if (s == null) return "no WitVoiceSource";
        s.DriverOpen();
        s.DriverPiece(string.IsNullOrEmpty(word) ? "stop" : word);
        return "queued (call StitchState now: it must already be delivered). " + s.StitchState;
    }

    public static string StitchState()
    {
        var s = Src; if (s == null) return "no WitVoiceSource";
        var cc = VoiceCommandCenter.Instance;
        return s.StitchState + " listening=" + s.IsListening
               + (cc != null ? " | centre: lastHeard='" + cc.LastTranscript + "' intent=" + cc.LastIntent + " msg='" + cc.LastMessage + "'" : " | no command centre");
    }

    public static string Verdict(string text = null)
    {
        var cc = VoiceCommandCenter.Instance; if (cc == null) return "no command centre";
        string t = string.IsNullOrEmpty(text) ? "Manager" : text;
        return "'" + t + "' -> " + cc.SentenceVerdict(t) + " (dangling end=" + SentenceStitcher.EndsDangling(t) + ", words=" + SentenceStitcher.CountWords(t) + ")";
    }

    // ───────────────────────── voices ─────────────────────────

    public static string Voices()
    {
        var sb = new StringBuilder("chirp=" + AgentSpeech.ChirpEnabled + " enabled=" + AgentSpeech.Enabled);
        foreach (var kv in AgentSpeech.Styles)
        {
            var v = kv.Value;
            sb.Append("\n").Append(kv.Key).Append(": ").Append(v.voice).Append(" pitch=").Append(v.pitch).Append(" speed=").Append(v.speed)
              .Append(" comb=").Append(v.combMs).Append("ms/").Append(v.combMix).Append(" chorus=").Append(v.chorusMix)
              .Append(" lowCut=").Append(v.lowCutHz).Append(" chirp=").Append(v.chirpHz);
        }
        return sb.ToString();
    }

    public static string Say(string arg = null)
    {
        if (!Application.isPlaying) return "not playing";
        string agent = "developer", text = "The build is green. I added two tests for the login page.";
        if (!string.IsNullOrEmpty(arg))
        {
            int bar = arg.IndexOf('|');
            if (bar > 0) { agent = arg.Substring(0, bar).Trim(); text = arg.Substring(bar + 1).Trim(); }
            else agent = arg.Trim();
        }
        Camera cam = Camera.main;
        Vector3 at = cam != null ? cam.transform.position + cam.transform.forward : Vector3.zero;
        bool ok = AgentSpeech.Say(agent, text, at);
        return "say(" + agent + ") -> " + ok + " last='" + AgentSpeech.LastSaid + "' speaking=" + AgentSpeech.IsSpeaking(agent);
    }
}
