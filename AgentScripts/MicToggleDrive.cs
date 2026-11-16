using System.Text;
using UnityEngine;
using MuseDesk.Voice;

/// <summary>
/// Play Mode driver for the hands-free mic key (MicToggleV1). It CANNOT hear: Play Mode has no real audio path to the
/// speech service, so it checks the key, the state machine, the saved choice and the guard rails, not recognition.
/// What it does show: pressing the key flips muted/unmuted and the label, the choice is saved per device, an unavailable
/// build says "Voice offline" and refuses to pretend, and a spoken "approve" injected as ambient speech goes through the
/// same undo/pending path (never sent at once). Always finish with Cleanup().
///
///   scripts\drv.ps1 MicToggleDrive State
///   scripts\drv.ps1 MicToggleDrive Press          one finger press (mute <-> unmute); run twice to see both ways
///   scripts\drv.ps1 MicToggleDrive Persist        saved value vs live state
///   scripts\drv.ps1 MicToggleDrive Ambient "what a lovely day"    non-command room speech: silent, nothing sent
///   scripts\drv.ps1 MicToggleDrive Ambient "approve"              decision words: still the paper/undo rules
///   scripts\drv.ps1 MicToggleDrive Cleanup
/// </summary>
public static class MicToggleDrive
{
    static MicToggleKey Key() => Object.FindFirstObjectByType<MicToggleKey>(FindObjectsInactive.Include);
    static VoiceCommandCenter Center() => VoiceCommandCenter.Instance != null ? VoiceCommandCenter.Instance : Object.FindFirstObjectByType<VoiceCommandCenter>(FindObjectsInactive.Include);

    public static string State()
    {
        if (!Application.isPlaying) return "not playing (saved=" + PlayerPrefs.GetInt(MicPreference.Key, -1) + ")";
        var c = Center(); var k = Key();
        var sb = new StringBuilder();
        sb.Append("saved=" + PlayerPrefs.GetInt(MicPreference.Key, -1));
        if (c == null) return sb.Append(" | NO VoiceCommandCenter").ToString();
        sb.Append(" | unmuted=" + c.MicUnmuted + " phase=" + c.MicPhase + " blocked=" + c.MicBlocked + " begins=" + c.MicBeginCount
                  + " errors=" + c.MicConsecutiveErrors + " listening=" + c.IsListening + " pendingQuestion=" + c.HasPendingQuestion);
        if (k == null) return sb.Append(" | NO MicToggleKey in the scene").ToString();
        var cap = k.cap != null ? k.cap.sharedMaterial : null;
        sb.Append(" | key label='" + k.ShownText.Replace("\n", "/") + "' cap=" + (cap != null ? cap.name : "?")
                  + " scale=" + k.transform.localScale.x.ToString("F5") + " active=" + k.gameObject.activeInHierarchy);
        return sb.ToString();
    }

    /// <summary>The press a finger makes.</summary>
    public static string Press()
    {
        if (!Application.isPlaying) return "not playing";
        var k = Key();
        if (k == null) return "no MicToggleKey";
        var c = Center();
        bool before = c != null && c.MicUnmuted;
        k.Press();
        return "press: unmuted " + before + " -> " + (c != null && c.MicUnmuted) + " | " + State();
    }

    /// <summary>Saved choice must match the live one (a restart reads it back).</summary>
    public static string Persist()
    {
        if (!Application.isPlaying) return "not playing";
        var c = Center();
        if (c == null) return "no VoiceCommandCenter";
        int saved = PlayerPrefs.GetInt(MicPreference.Key, -1);
        bool ok = (saved == 1) == c.MicUnmuted && (saved != -1 || !c.MicUnmuted);
        return (ok ? "PASS" : "FAIL") + ": saved=" + saved + " unmuted=" + c.MicUnmuted;
    }

    /// <summary>Ambient room speech as the hands-free loop would deliver it (the recogniser is not involved).</summary>
    public static string Ambient(string text)
    {
        if (!Application.isPlaying) return "not playing";
        var c = Center();
        if (c == null) return "no VoiceCommandCenter";
        c.InjectAmbientFinal(text ?? "");
        return "ambient '" + text + "' -> " + c.Describe();
    }

    public static string Cleanup()
    {
        PlayerPrefs.DeleteKey(MicPreference.Key);
        PlayerPrefs.Save();
        if (!Application.isPlaying) return "not playing; saved choice removed";
        var c = Center();
        if (c != null) { c.SetMicUnmuted(false); PlayerPrefs.DeleteKey(MicPreference.Key); PlayerPrefs.Save(); }
        return "cleaned (muted, saved choice removed): " + State();
    }
}
