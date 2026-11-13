using UnityEngine;
using MuseDesk.Voice;

/// <summary>
/// Play Mode driver for voice: every command can be exercised without a microphone, a token or
/// internet. Say() injects a FINAL transcript exactly where the recogniser's would arrive
/// (VoiceCommandCenter.InjectFinal -> the same parse, gates, undo window and actions).
///   unity command script run --file AgentScripts/VoiceDrive.cs --entry VoiceDrive.State
/// Typical sequence: PlayDrive.Start, wait for the paper, PlayDrive.TakePaper (or
/// VoiceDrive.Say("show me the paper")), VoiceDrive.Say("approve"), wait 2 s, VoiceDrive.State.
/// </summary>
public static class VoiceDrive
{
    static VoiceCommandCenter Center()
    {
        var c = VoiceCommandCenter.Instance;
        if (c == null) c = Object.FindAnyObjectByType<VoiceCommandCenter>(FindObjectsInactive.Include);
        return c;
    }

    const string Missing = "no VoiceCommandCenter in the scene (run BuildVoiceV2.Apply, then enter Play Mode)";

    /// <summary>A final transcript, as if recognised. Returns the state afterwards.</summary>
    public static string Say(string transcript)
    {
        if (!Application.isPlaying) return "not playing";
        var c = Center(); if (c == null) return Missing;
        c.InjectFinal(transcript);
        return c.Describe();
    }

    /// <summary>A partial transcript (display only, except "stop" while moving and "cancel" while asked).</summary>
    public static string Partial(string transcript)
    {
        if (!Application.isPlaying) return "not playing";
        var c = Center(); if (c == null) return Missing;
        c.InjectPartial(transcript);
        return c.Describe();
    }

    /// <summary>What voice shows and knows right now.</summary>
    public static string State()
    {
        if (!Application.isPlaying) return "not playing";
        var c = Center(); if (c == null) return Missing;
        return c.Describe();
    }

    /// <summary>Press the microphone key (opens the real microphone if a speech service is present).</summary>
    public static string Mic()
    {
        if (!Application.isPlaying) return "not playing";
        var c = Center(); if (c == null) return Missing;
        c.PressMic();
        return c.Describe();
    }

    /// <summary>Press the readout's Confirm key.</summary>
    public static string Confirm()
    {
        if (!Application.isPlaying) return "not playing";
        var c = Center(); if (c == null) return Missing;
        c.PressConfirm();
        return c.Describe();
    }

    /// <summary>Press the readout's Cancel key.</summary>
    public static string Cancel()
    {
        if (!Application.isPlaying) return "not playing";
        var c = Center(); if (c == null) return Missing;
        c.PressCancel();
        return c.Describe();
    }

    /// <summary>What the grammar makes of a sentence in the current context, without acting on it.</summary>
    public static string Parse(string transcript)
    {
        if (!Application.isPlaying) return VoiceIntentParser.Parse(transcript, VoiceContext.None).ToString() + " (not playing: no context)";
        var c = Center(); if (c == null) return Missing;
        c.Office.Find(true);
        var ctx = c.Office.Context(false, false);
        return VoiceIntentParser.Parse(transcript, ctx) + "   context=" + ctx;
    }

    /// <summary>Switch the optional pinch-by-the-mouth gesture on or off for this Play session.</summary>
    public static string Gesture(bool on)
    {
        var g = Object.FindAnyObjectByType<VoiceGestureActivator>(FindObjectsInactive.Include);
        if (g == null) return "no VoiceGestureActivator";
        g.gestureEnabled = on;
        return "gesture enabled=" + on;
    }
}
