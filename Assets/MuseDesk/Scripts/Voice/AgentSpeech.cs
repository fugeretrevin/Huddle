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

        /// <summary>
        /// rc21 (owner, worn rc20: "make it cuter and a bit more robotic, but not just like a voice assistant, a bit more
        /// unique but not annoying"). One row per agent; tweak here. Each agent keeps its own Wit voice (all four were heard
        /// working in rc20), lifted in pitch and given a small-robot colour on its AudioSource:
        ///  - pitch / speed: Wit percentages (pitch 25..200, speed 50..200, 100 = natural). ~115-125 pitch reads small and
        ///    friendly without going chipmunk.
        ///  - comb: a very short echo (ms, 10..20) at low mix: the metallic "little tin body" ring. Higher mix = more robot.
        ///  - chorus: a tiny detuned double (0..1 mix): a soft synthetic shimmer.
        ///  - lowCut: high-pass (Hz): a small speaker, not a big chest.
        ///  - chirp: the agent's two-note "bip-bop" before a line (Hz of the first note; 0 = none). Quiet, under 0.15 s.
        /// Other Wit voices that exist (see the TTS Lip Sync sample): wit$Cael, wit$Cam, wit$Rubie, wit$Railey, wit$Remi,
        /// wit$Cody, wit$Colin, wit$Connor, wit$Carl; character voices (wit$Whimsical, wit$Cartoon Kid, ...) are louder in
        /// personality and were judged too much for lines heard all day.
        /// </summary>
        public struct VoiceStyle
        {
            public string voice;
            public int pitch, speed;
            public float combMs, combMix, chorusMix, lowCutHz, chirpHz;
            public VoiceStyle(string voice, int pitch, int speed, float combMs, float combMix, float chorusMix, float lowCutHz, float chirpHz)
            {
                this.voice = voice; this.pitch = pitch; this.speed = speed; this.combMs = combMs; this.combMix = combMix;
                this.chorusMix = chorusMix; this.lowCutHz = lowCutHz; this.chirpHz = chirpHz;
            }
        }

        public static readonly Dictionary<string, VoiceStyle> Styles = new Dictionary<string, VoiceStyle>(StringComparer.OrdinalIgnoreCase)
        {
            //                                   voice          pitch speed comb ms  mix   chorus lowCut chirp
            { "manager",    new VoiceStyle("wit$Rebecca", 114, 103,  11f,  0.16f, 0.14f, 170f,  880f) },   // calm lead
            { "developer",  new VoiceStyle("wit$Charlie", 124, 107,  14f,  0.20f, 0.12f, 190f,  660f) },   // quick, bright
            { "researcher", new VoiceStyle("wit$Rosie",   118, 100,  12f,  0.18f, 0.18f, 170f,  990f) },   // thoughtful
            { "tester",     new VoiceStyle("wit$Cooper",  128, 106,  16f,  0.20f, 0.12f, 200f,  740f) },   // chirpy checker
        };
        static readonly VoiceStyle Fallback = new VoiceStyle("wit$Charlie", 120, 104, 13f, 0.18f, 0.12f, 180f, 760f);

        /// <summary>The style for <paramref name="agent"/> (a default for an unknown one).</summary>
        public static VoiceStyle StyleFor(string agent)
        {
            VoiceStyle s;
            return !string.IsNullOrEmpty(agent) && Styles.TryGetValue(agent, out s) ? s : Fallback;
        }

        /// <summary>Play the little chirp before each line.</summary>
        public static bool ChirpEnabled = true;
        const float ChirpVolume = 0.16f;

        TTSWit _tts;
        readonly Dictionary<string, AudioClip> _chirps = new Dictionary<string, AudioClip>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, AudioSource> _chirpSources = new Dictionary<string, AudioSource>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, TTSSpeaker> _speakers = new Dictionary<string, TTSSpeaker>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Say a short line as <paramref name="agent"/> (manager / developer / researcher / tester), heard from
        /// <paramref name="at"/>. Any line the same agent is still saying is cut off.</summary>
        public static bool Say(string agent, string text, Vector3 at)
        {
            string line = SpeakableLine(text);
            if (!Enabled || string.IsNullOrEmpty(line) || string.IsNullOrEmpty(agent)) return false;
            LastSaid = line; LastSpeaker = agent; SaidCount++;
            try { LineStarted?.Invoke(agent, line); } catch (Exception e) { Debug.LogWarning("[AgentSpeech] listener: " + e.Message); }
            AgentSpeech s = Ensure();
            if (s == null) return false;
            try { return s.SayNow(agent, line, at); }
            catch (Exception e) { Debug.LogWarning("[AgentSpeech] could not speak: " + e.GetType().Name); return false; }
        }

        /// <summary>True while <paramref name="agent"/>'s line is loading or playing (for its speech bubble and head bob).</summary>
        public static bool IsSpeaking(string agent)
        {
            if (_instance == null || string.IsNullOrEmpty(agent)) return false;
            TTSSpeaker sp;
            return _instance._speakers.TryGetValue(agent, out sp) && sp != null && (sp.IsSpeaking || sp.IsLoading);
        }

        /// <summary>Raised on the main thread whenever a line is handed to the voice: (agent, the line). Speech bubbles use it.</summary>
        public static event Action<string, string> LineStarted;

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
            AppVoiceExperience app = FindAnyObjectByType<AppVoiceExperience>(FindObjectsInactive.Include);
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
                var src = go.AddComponent<AudioSource>();       // first AudioSource: the one the TTS player uses
                src.playOnAwake = false;
                src.spatialBlend = 0.65f;                       // from the agent, but never too quiet to follow
                src.minDistance = 0.6f; src.maxDistance = 6f;
                src.rolloffMode = AudioRolloffMode.Linear;
                src.volume = 0.95f;
                VoiceStyle style = StyleFor(agent);
                AddRobotColour(go, style);
                // The chirp has its own source on a child: the TTS player stops its source when a line starts, which
                // would cut a one-shot on the same source. (The TTS player takes the GameObject's own AudioSource first.)
                var chirpGo = new GameObject("Chirp");
                chirpGo.transform.SetParent(go.transform, false);
                var chirpSrc = chirpGo.AddComponent<AudioSource>();
                chirpSrc.playOnAwake = false;
                chirpSrc.spatialBlend = src.spatialBlend;
                chirpSrc.minDistance = src.minDistance; chirpSrc.maxDistance = src.maxDistance;
                chirpSrc.rolloffMode = src.rolloffMode;
                _chirpSources[agent] = chirpSrc;
                sp = go.AddComponent<TTSSpeaker>();
                sp.customWitVoiceSettings = new TTSWitVoiceSettings
                {
                    SettingsId = agent,
                    voice = style.voice,
                    speed = Mathf.Clamp(style.speed, 50, 200),
                    pitch = Mathf.Clamp(style.pitch, 25, 200),
                };
                go.SetActive(true);
                _speakers[agent] = sp;
            }
            sp.transform.position = at;
            sp.Stop();
            Chirp(agent, sp);
            sp.Speak(line);
            return true;
        }

        /// <summary>Unity's built-in filters on the speaker's AudioSource (they run on the Quest's audio mixer too). Subtle on
        /// purpose: the words must stay as clear as before.</summary>
        static void AddRobotColour(GameObject go, VoiceStyle s)
        {
            if (s.lowCutHz > 0f)
            {
                var hp = go.AddComponent<AudioHighPassFilter>();
                hp.cutoffFrequency = Mathf.Clamp(s.lowCutHz, 10f, 22000f);
                hp.highpassResonanceQ = 1.2f;
            }
            if (s.combMix > 0f)
            {
                var comb = go.AddComponent<AudioEchoFilter>();
                comb.delay = Mathf.Clamp(s.combMs, 10f, 40f);   // 10 ms is the filter's minimum
                comb.decayRatio = 0.3f;
                comb.dryMix = 1f;
                comb.wetMix = Mathf.Clamp01(s.combMix);
            }
            if (s.chorusMix > 0f)
            {
                var ch = go.AddComponent<AudioChorusFilter>();
                ch.dryMix = 0.85f;
                ch.wetMix1 = Mathf.Clamp01(s.chorusMix);
                ch.wetMix2 = 0f;
                ch.wetMix3 = 0f;
                ch.delay = 9f;
                ch.rate = 0.6f;
                ch.depth = 0.04f;
            }
        }

        void Chirp(string agent, TTSSpeaker sp)
        {
            if (!ChirpEnabled) return;
            VoiceStyle s = StyleFor(agent);
            if (s.chirpHz <= 0f) return;
            AudioSource src;
            if (!_chirpSources.TryGetValue(agent, out src) || src == null) return;
            AudioClip clip;
            if (!_chirps.TryGetValue(agent, out clip) || clip == null)
            {
                clip = MakeChirp("Chirp_" + agent, s.chirpHz);
                _chirps[agent] = clip;
            }
            if (clip != null) src.PlayOneShot(clip, ChirpVolume);
        }

        /// <summary>A soft two-note "bip-bop": a short note, a tiny gap, a slightly lower one, each with a smooth envelope
        /// (no clicks) and a touch of square for the robot edge.</summary>
        static AudioClip MakeChirp(string name, float hz)
        {
            try
            {
                const int rate = 44100;
                float[] notes = { hz, hz * 0.75f };
                const float noteSec = 0.045f, gapSec = 0.018f;
                int noteN = Mathf.RoundToInt(noteSec * rate), gapN = Mathf.RoundToInt(gapSec * rate);
                var data = new float[noteN * 2 + gapN];
                int at = 0;
                for (int k = 0; k < notes.Length; k++)
                {
                    float f = notes[k];
                    for (int i = 0; i < noteN; i++)
                    {
                        float t = i / (float)rate;
                        float env = Mathf.Sin(Mathf.PI * i / noteN);                 // smooth in and out
                        float glide = f * (1f + 0.06f * (1f - i / (float)noteN));    // a little drop in each note
                        float ph = 2f * Mathf.PI * glide * t;
                        float tone = 0.8f * Mathf.Sin(ph) + 0.2f * Mathf.Sign(Mathf.Sin(ph));
                        data[at + i] = 0.5f * env * env * tone;
                    }
                    at += noteN + (k == 0 ? gapN : 0);
                }
                var clip = AudioClip.Create(name, data.Length, 1, rate, false);
                clip.SetData(data, 0);
                return clip;
            }
            catch (Exception) { return null; }
        }

        void OnDestroy() { if (_instance == this) _instance = null; }
    }
}
