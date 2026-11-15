namespace MuseDesk.Agents
{
    /// <summary>
    /// The provenance line under an agent speech bubble. It uses the same words as the status chip and the decision paper
    /// (LIVE or SCRIPTED DEMO) and comes from the run's own mode, never from a default: when the mode is not known there is
    /// no footer at all rather than a guess.
    /// </summary>
    public static class BubbleProvenance
    {
        /// <summary>Footer size relative to the bubble text: 6.5 mm text gives a 5.5 mm footer (the type floor).</summary>
        public const int FooterPercent = 85;

        /// <summary>"LIVE" or "SCRIPTED DEMO" for a known mode, null for an unknown one (no footer).</summary>
        public static string Words(bool modeKnown, bool live)
        {
            if (!modeKnown) return null;
            return live ? "LIVE" : "SCRIPTED DEMO";
        }

        /// <summary>The bubble text with its footer on a new line (small, InkSoft grey). No footer: the text unchanged.</summary>
        public static string Compose(string text, string footer)
        {
            if (string.IsNullOrEmpty(footer) || string.IsNullOrEmpty(text)) return text;
            return text + "\n<size=" + FooterPercent + "%><color=#4B5563>" + footer + "</color></size>";
        }

        /// <summary>Lines the bubble may use: three of text, plus one for the footer.</summary>
        public static int MaxLines(string footer) { return string.IsNullOrEmpty(footer) ? 3 : 4; }
    }
}
