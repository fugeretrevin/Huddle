namespace MuseDesk.Spatial
{
    /// <summary>What a spoken phrase asks the office to do.</summary>
    public enum SummonVoiceCommand { None = 0, BringOfficeHere = 1, SendOfficeBack = 2 }

    /// <summary>
    /// Maps a transcript to a summon request. Whole phrases only, and "send back" is checked
    /// first, so a sentence that mentions both never brings the office. Plain static class (no
    /// Unity dependency) so it is tested without a scene. These phrases never touch a decision.
    /// </summary>
    public static class SummonVoicePhrases
    {
        static readonly string[] SendBack =
        {
            "send the office back", "send it back", "send office back", "put the office back",
            "office go back",
        };
        static readonly string[] Bring =
        {
            "bring down the office", "bring the office down", "bring the office here",
            "bring the office", "bring it here", "bring it down", "lower the office",
        };

        public static SummonVoiceCommand Match(string transcript)
        {
            if (string.IsNullOrEmpty(transcript)) return SummonVoiceCommand.None;
            string t = transcript.ToLowerInvariant();
            for (int i = 0; i < SendBack.Length; i++)
                if (t.IndexOf(SendBack[i], System.StringComparison.Ordinal) >= 0) return SummonVoiceCommand.SendOfficeBack;
            for (int i = 0; i < Bring.Length; i++)
                if (t.IndexOf(Bring[i], System.StringComparison.Ordinal) >= 0) return SummonVoiceCommand.BringOfficeHere;
            return SummonVoiceCommand.None;
        }
    }
}
