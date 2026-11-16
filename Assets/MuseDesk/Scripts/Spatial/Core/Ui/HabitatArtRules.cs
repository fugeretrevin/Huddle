namespace MuseDesk.Spatial.Wall
{
    /// <summary>
    /// Pure naming rules for the Wall Habitat art. Antigravity's complete habitat prefabs
    /// (WallHabitat_Complete_3Tier / _2Tier) embed the four character prefabs as nested instances
    /// (Agent_Manager, Bay_Researcher_Agent, Bay_Developer_Agent, Bay_Tester_Agent). The habitat's walkers are run from
    /// code (HabitatWalker -> WallHabitatView avatar clones), so those baked characters would be a SECOND copy of each agent at
    /// a different scale. They are recognised by name here and switched off when the art is placed.
    /// </summary>
    public static class HabitatArtRules
    {
        /// <summary>True for a transform that is a baked character root or its holder (Agent_X, Bay_X_Agent).</summary>
        public static bool IsBakedCharacterName(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            return name.StartsWith("Agent_", System.StringComparison.Ordinal)
                || name.EndsWith("_Agent", System.StringComparison.Ordinal);
        }

        /// <summary>The role word a character-ish name carries, or null (Researcher, Developer, Tester, Manager).</summary>
        public static string RoleWordOf(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            string[] roles = { "Researcher", "Developer", "Tester", "Manager" };
            for (int i = 0; i < roles.Length; i++)
                if (name.IndexOf(roles[i], System.StringComparison.OrdinalIgnoreCase) >= 0) return roles[i];
            return null;
        }
    }
}
