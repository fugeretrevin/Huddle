using UnityEngine;
using MuseDesk.Wall;

/// <summary>
/// Play Mode probes for the rc21 speech bubble + head bob. First perch an agent (WallDrive: Enable, Enter, Place, Select,
/// Hand developer, Pinch developer, ToPerch) or open the portal (MeetLarger, PortalOpen).
///   Info            bubble state, position vs head bone, scale, font size at the viewing distance, bob strength/angle
///   Say [text]      speak a line as the visiting agent through AgentSpeech.Say (same path as a real answer)
///   Long            a long line (checks the 120-char cut and the wrapped plate)
///   Wait            Info after the line (call a few seconds later: bubble should be gone ~1.5 s after speech)
/// </summary>
public static class Rc21TalkProbe
{
    static WallHabitatController C => Object.FindAnyObjectByType<WallHabitatController>(FindObjectsInactive.Include);

    public static string Info()
    {
        if (!Application.isPlaying) return "not playing";
        var c = C; return c == null ? "no WallHabitatController" : c.TalkInfo();
    }

    public static string Say(string text = null)
    {
        if (!Application.isPlaying) return "not playing";
        var c = C; if (c == null) return "no WallHabitatController";
        return c.TalkTest(string.IsNullOrEmpty(text) ? "The login flow is in review. I found two edge cases and I am writing tests for both." : text);
    }

    public static string Long()
    {
        return Say("I compared three approaches for the sync layer. The first is simple but loses edits under a bad connection. "
                   + "The second keeps a queue and replays it, which is safe but needs a server change. The third merges on the device and I would pick it.");
    }

    public static string Wait() { return Info(); }

    public static string ShortCheck()
    {
        return WallHabitatController.ShortTalkLine("One. Two sentences here that are fairly long and keep going well past the limit so the cut has to happen somewhere sensible. And a third one after that.");
    }
}
