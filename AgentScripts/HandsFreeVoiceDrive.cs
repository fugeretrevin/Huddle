using System.Text;
using MuseDesk.Voice;

/// <summary>Worn rc15: "I asked the Researcher why and nothing happened" (hands-free mic on). Feeds the sentences the
/// speech service really returned for "Researcher, why?" (see AgentScripts/WitProbe.cs) through the hands-free path
/// (InjectAmbientFinal) and reports what the user would see.
///   unity command script run --file AgentScripts/HandsFreeVoiceDrive.cs --entry HandsFreeVoiceDrive.Run</summary>
public static class HandsFreeVoiceDrive
{
    public static string Run()
    {
        var vc = VoiceCommandCenter.Instance;
        if (vc == null) return "no VoiceCommandCenter (enter Play Mode)";
        var sb = new StringBuilder();
        foreach (var s in new[] { "Researcher why", "Research your why", "Research, why?", "why researcher", "what's for lunch", "research shows it works" })
        {
            vc.InjectAmbientFinal(s);
            sb.AppendLine("'" + s + "' -> heard='" + vc.LastTranscript + "' intent=" + vc.LastIntent + " message='" + vc.LastMessage + "'");
        }
        return sb.ToString();
    }
}
