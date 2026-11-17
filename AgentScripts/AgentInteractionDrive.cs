using UnityEngine;
using MuseDesk.Agents;

/// <summary>
/// Play Mode driver for the "characters respond to the person" pass: exercises it without hands
/// or a headset. Poke, HandNear and Gaze feed a synthetic fingertip / gaze into AgentAttention so
/// the real detection code runs; the result shows a frame or two later (call Report after).
/// Roles: "Researcher", "Developer", "Tester".
/// </summary>
public static class AgentInteractionDrive
{
    static AgentAttention A => Application.isPlaying ? AgentAttention.Instance : null;
    const string NotPlaying = "not playing";

    /// <summary>A fingertip enters the character's poke volume (first poke: bounce, turn, wave or
    /// nod, status bubble). Call again within 2 s for the playful second reaction.</summary>
    public static string Poke(string role) { var a = A; return a == null ? NotPlaying : a.DebugPoke(role); }

    /// <summary>The user looks at the character for <paramref name="seconds"/> (1.2 s shows the chip
    /// and the highlight; they leave 0.6 s after the gaze ends).</summary>
    public static string Gaze(string role, float seconds) { var a = A; return a == null ? NotPlaying : a.DebugGaze(role, seconds); }

    /// <summary>The voice side addressed the character.</summary>
    public static string Address(string role)
    {
        var a = A; if (a == null) return NotPlaying;
        a.Address(role);
        var p = a.PresenceOf(role);
        return p == null ? "no agent '" + role + "'" : role + ": addressed (busy=" + p.PipelineBusy + ", so " + (p.PipelineBusy ? "queued up to 6 s" : "playing") + ")";
    }

    /// <summary>A speech bubble for 4 seconds.</summary>
    public static string Say(string role, string text)
    {
        var a = A; if (a == null) return NotPlaying;
        a.Say(role, text, 4f);
        return role + " says '" + text + "'";
    }

    /// <summary>A fingertip 8 cm in front of the character for 1.5 s (awareness glance; if the
    /// character is idle and the tip is within 3 cm of its volume, the reach-up tap).</summary>
    public static string HandNear(string role) { var a = A; return a == null ? NotPlaying : a.DebugHandNear(role); }

    /// <summary>As if an open hand had waved at the office.</summary>
    public static string Wave() { var a = A; return a == null ? NotPlaying : a.DebugWave(); }

    /// <summary>The status sentence the character would say now.</summary>
    public static string Status(string role) { var a = A; return a == null ? NotPlaying : a.DescribeStatus(role); }

    /// <summary>Who needs the user now.</summary>
    public static string Who() { var a = A; return a == null ? NotPlaying : a.WhoNeedsUser(); }

    /// <summary>Poke, then say the status, for all three (a quick look at every bubble at once).</summary>
    public static string StatusAll()
    {
        var a = A; if (a == null) return NotPlaying;
        var sb = new System.Text.StringBuilder();
        foreach (string role in new[] { "Researcher", "Developer", "Tester" })
        {
            string s = a.DescribeStatus(role);
            a.Say(role, s, 5f);
            sb.AppendLine(role + ": " + s);
        }
        return sb.ToString();
    }

    /// <summary>Everything the pass decides, one line per character.</summary>
    public static string Report() { var a = A; return a == null ? NotPlaying : a.DebugReport(); }
}
