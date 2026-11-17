namespace MuseDesk.Motion
{
    /// <summary>Minimal int key-value store, so the Reduced Motion preference can be tested without PlayerPrefs.</summary>
    public interface IPrefStore
    {
        int GetInt(string key, int fallback);
        void SetInt(string key, int value);
    }

    /// <summary>
    /// Pure logic of the per-device Reduced Motion ("Calm motion") choice. A saved choice (1 or 0) always beats the build
    /// default; no saved choice (-1) follows the build default (ReleaseConfig.reducedMotion). The in-app toggle key calls
    /// <see cref="Flip"/>, which saves the new value and returns it; the caller applies it to MotionSettings.
    /// </summary>
    public static class ReducedMotionPreference
    {
        /// <summary>Same key ReleaseConfigApplier has always used.</summary>
        public const string Key = "MuseDesk.ReducedMotion";

        /// <summary>The words on the key. The state is always in words, never colour alone.</summary>
        public const string Label = "Calm motion";

        public static string StateWords(bool on) { return on ? "ON" : "OFF"; }

        /// <summary>The key's label, one word per line so it fits a square cap at 5.5 mm: "Calm", "motion", then ON or OFF.</summary>
        public static string KeyText(bool on) { return "Calm\nmotion\n" + StateWords(on); }

        /// <summary>Saved choice if there is one, else the build default.</summary>
        public static bool Resolve(IPrefStore store, bool buildDefault)
        {
            int saved = store != null ? store.GetInt(Key, -1) : -1;
            return saved >= 0 ? saved == 1 : buildDefault;
        }

        /// <summary>Saves the choice and returns it.</summary>
        public static bool Save(IPrefStore store, bool on)
        {
            if (store != null) store.SetInt(Key, on ? 1 : 0);
            return on;
        }

        /// <summary>Flips the CURRENT effective choice, saves it, and returns the new value.</summary>
        public static bool Flip(IPrefStore store, bool current)
        {
            return Save(store, !current);
        }

        /// <summary>The toggle key is shown only when the release flag allows it (the committed release has it off).</summary>
        public static bool KeyVisible(bool flagOn) { return flagOn; }
    }
}
