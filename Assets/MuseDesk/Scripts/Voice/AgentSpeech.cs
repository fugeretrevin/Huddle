using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Meta.WitAi.TTS.Integrations;
using Meta.WitAi.TTS.Utilities;
using Oculus.Voice;
using UnityEngine;

namespace MuseDesk.Voice
{
    /// <summary>
    /// rc20 (owner, worn rc19): "the agents should not just answer on the card but be voicing things, we need text to
    /// speech". Each agent speaks with its own Wit voice (the same Wit app the microphone already uses; its configuration is
    /// only referenced, never read or logged). Built at runtime the first time something is said, so scenes need no change.
    /// Short lines only: the first sentences of an answer, the card keeps the full text. Without a Wit configuration,
    /// internet, or with <see cref="Enabled"/> off, Say does nothing and returns false (the card still shows everything).
    /// </summary>
    public sealed class AgentSpeech : MonoBehaviour
    {
        public static bool Enabled = true;
        /// <summary>For drivers and tests: the last line handed to the voice, and how many were.</summary>
        public static string LastSaid { get; private set; } = "";
        public static string LastSpeaker { get; private set; } = "";
        public static int SaidCount { get; private set; }

        const int MaxChars = 240;
        static AgentSpeech _instance;
        static bool _failed;

        static readonly Dictionary<string, string> Voices = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "manager", "wit$Rebecca" },
            { "developer", "wit$Charlie" },
            { "researcher", "wit$Rosie" },
            { "tester", "wit$Cooper" },
        };

        TTSWit _tts;
        readonly Dictionary<string, TTSSpeaker> _speakers = new Dictionary<string, TTSSpeaker>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Say a short line as <paramref name="agent"/> (manager / developer / researcher / tester), heard from
        /// <paramref name="at"/>. Any line the same agent is still saying is cut off.</summary>
        public static bool Say(string agent, string text, Vector3 at)
        {
            string line = SpeakableLine(text);
            if (!Enabled || string.IsNullOrEmpty(line) || string.IsNullOrEmpty(agent)) return false;
            LastSaid = line; LastSpeaker = agent; SaidCount++;
            AgentSpeech s = Ensure();
            if (s == null) return false;
            try { return s.SayNow(agent, line, at); }
            catch (Exception e) { Debug.LogWarning("[AgentSpeech] could not speak: " + e.GetType().Name); return false; }
        }

        /// <summary>Stop every agent's voice (a visit ended, the user left the Wall).</summary>
        public static void StopAll()
        {
            if (_instance == null) return;
            foreach (var kv in _instance._speakers) if (kv.Value != null) kv.Value.Stop();
        }

        /// <summary>The words worth saying: markup and file paths removed, cut at a sentence end within the limit.</summary>
        public static string SpeakableLine(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            string t = Regex.Replace(text, "<[^>]+>", " ");                       // TMP rich text
            t = Regex.Replace(t, "```.*?```", " ", RegexOptions.Singleline);       // code
            t = Regex.Replace(t, @"[`*_#>\[\]]", " ");
            t = t.Replace("…", " ").Replace("...", " ");
            t = Regex.Replace(t, @"\s+", " ").Trim();
            // The card carries the mode label; it is not read aloud.
            if (t.StartsWith("SCRIPTED DEMO", StringComparison.Ordinal)) t = t.Substring("SCRIPTED DEMO".Length).TrimStart(' ', ':', '.', '-', '·');
            if (t.Length <= MaxChars) return t;
            int cut = -1;
            for (int i = Mathf.Min(MaxChars, t.Length - 1); i > 40; i--)
                if ((t[i] == '.' || t[i] == '!' || t[i] == '?') && (i + 1 >= t.Length || t[i + 1] == ' ')) { cut = i + 1; break; }
            if (cut < 0) { cut = t.LastIndexOf(' ', MaxChars); if (cut < 40) cut = MaxChars; }
            return t.Substring(0, cut).Trim();
        }

        static AgentSpeech Ensure()
        {
            if (_instance != null) return _instance;
            if (_failed) return null;
            AppVoiceExperience app = FindFirstObjectByType<AppVoiceExperience>(FindObjectsInactive.Include);
            var config = app != null && app.RuntimeConfiguration != null ? app.RuntimeConfiguration.witConfiguration : null;
            if (config == null) { _failed = true; Debug.Log("[AgentSpeech] no Wit configuration: agents stay silent"); return null; }

            var go = new GameObject("AgentSpeech");
            go.SetActive(false);                       // configure before TTSWit's OnEnable
            DontDestroyOnLoad(go);
            var s = go.AddComponent<AgentSpeech>();
            s._tts = go.AddComponent<TTSWit>();
            s._tts.Configuration = config;
            go.SetActive(true);
            _instance = s;
            return s;
        }

        bool SayNow(string agent, string line, Vector3 at)
        {
            TTSSpeaker sp;
            if (!_speakers.TryGetValue(agent, out sp) || sp == null)
            {
                var go = new GameObject("Voice_" + agent);
                go.transform.SetParent(transform, false);
                go.SetActive(false);
                var src = go.AddComponent<AudioSource>();
                src.playOnAwake = false;
                src.spatialBlend = 0.65f;                       // from the agent, but never too quiet to follow
                src.minDistance = 0.6f; src.maxDistance = 6f;
                src.rolloffMode = AudioRolloffMode.Linear;
                src.volume = 0.95f;
                sp = go.AddComponent<TTSSpeaker>();
                string voice;
                if (!Voices.TryGetValue(agent, out voice)) voice = "wit$Charlie";
                sp.customWitVoiceSettings = new TTSWitVoiceSettings { SettingsId = agent, voice = voice };
                go.SetActive(true);
                _speakers[agent] = sp;
            }
            sp.transform.position = at;
            sp.Stop();
            sp.Speak(line);
            return true;
        }

        void OnDestroy() { if (_instance == this) _instance = null; }
    }
}
