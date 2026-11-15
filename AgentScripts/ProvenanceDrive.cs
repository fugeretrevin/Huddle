using System.Text;
using UnityEngine;
using MuseDesk.Agents;

/// <summary>Polish S9c: Play Mode check of the provenance footer under an agent speech bubble. The text shown is the caller's own
/// (here a plain status sentence); the footer comes from the session's real mode. Nothing here is presented as live.</summary>
public static class ProvenanceDrive
{
    /// <summary>Makes the Researcher say a sentence and reports the bubble: the caller's text, what the label holds (with the footer
    /// line), the lines it fits in and whether it is drawn.</summary>
    public static string Bubble()
    {
        if (!Application.isPlaying) return "not playing";
        var at = AgentAttention.Instance;
        if (at == null) return "no AgentAttention";
        at.Say("researcher", "Waiting for your decision", 8f);
        var sb = new StringBuilder();
        foreach (var p in at.agents)
        {
            if (p == null || p.bubble == null || !p.bubble.Wanted) continue;
            var b = p.bubble;
            b.label.ForceMeshUpdate();
            sb.Append("bubble text='" + b.Text + "' label='" + b.label.text.Replace("\n", "\\n") + "' lines=" + (b.label.textInfo != null ? b.label.textInfo.lineCount : -1)
                      + " w=" + b.Width.ToString("F3") + " h=" + b.Height.ToString("F3") + " drawn=" + b.Visible + " | ");
        }
        return sb.Length > 0 ? sb.ToString() : "no bubble up";
    }
}
