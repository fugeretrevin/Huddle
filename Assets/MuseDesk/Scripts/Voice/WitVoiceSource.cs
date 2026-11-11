using System;
using UnityEngine;
using Oculus.Voice;
using Meta.WitAi;
using Meta.WitAi.Json;

namespace MuseDesk.Voice
{
    /// <summary>
    /// Wit.ai-backed IVoiceCommandSource using the Meta Voice SDK's AppVoiceExperience. On each NLU
    /// response it reads the transcription (and any recognized intent) and maps it to approve / reject /
    /// ask-why. It drives off the spoken TEXT so it works with a brand-new, untrained Wit app (no custom
    /// intents required); recognized built-in intents are used as a bonus. This only RAISES events —
    /// VoiceDecisionController routes them through SessionController -> backend, so the backend still
    /// authorizes every action (a spoken "approve" is not itself approval). Requires network + a linked
    /// WitConfiguration on the AppVoiceExperience, so it is live-mode / connected only, never the offline demo.
    /// </summary>
    [RequireComponent(typeof(AppVoiceExperience))]
    public class WitVoiceSource : MonoBehaviour, IVoiceCommandSource
    {
        public AppVoiceExperience appVoice;

        public event Action Approve;
        public event Action Reject;
        public event Action<string> AskWhy;
        public event Action<bool> ListeningChanged;

        static readonly string[] YesWords = { "approve", "approved", "yes", "ship it", "ship", "confirm", "confirmed", "go ahead", "accept", "sounds good", "do it", "sure" };
        static readonly string[] NoWords  = { "reject", "rejected", "no", "deny", "denied", "don't", "do not", "stop", "cancel", "decline" };
        static readonly string[] WhyWords = { "why", "explain", "reason", "tell me", "what if", "how come", "justify" };

        public bool IsAvailable => appVoice != null;

        void Reset() { appVoice = GetComponent<AppVoiceExperience>(); }

        void OnEnable()
        {
            if (appVoice == null) appVoice = GetComponent<AppVoiceExperience>();
            if (appVoice == null) return;
            appVoice.VoiceEvents.OnResponse.AddListener(OnResponse);
            appVoice.VoiceEvents.OnError.AddListener(OnError);
        }

        void OnDisable()
        {
            if (appVoice == null) return;
            appVoice.VoiceEvents.OnResponse.RemoveListener(OnResponse);
            appVoice.VoiceEvents.OnError.RemoveListener(OnError);
        }

        public void StartListening()
        {
            if (appVoice == null) return;
            appVoice.Activate();
            ListeningChanged?.Invoke(true);
        }

        public void StopListening()
        {
            if (appVoice == null) return;
            appVoice.Deactivate();
            ListeningChanged?.Invoke(false);
        }

        void OnResponse(WitResponseNode node)
        {
            ListeningChanged?.Invoke(false);
            if (node == null) return;

            string intent = SafeIntent(node);
            string text = (node.GetTranscription() ?? "").Trim().ToLowerInvariant();

            // Intent first (if the Wit app has built-in confirmation/negation), else keyword on the text.
            if (intent.Contains("confirm") || (string.IsNullOrEmpty(intent) && MatchesAny(text, YesWords)))
                Approve?.Invoke();
            else if (intent.Contains("reject") || intent.Contains("negat") || (string.IsNullOrEmpty(intent) && MatchesAny(text, NoWords)))
                Reject?.Invoke();
            else if (MatchesAny(text, WhyWords))
                AskWhy?.Invoke(node.GetTranscription());
            // else: unrecognized — intentionally do nothing (no action without a clear command).
        }

        void OnError(string error, string message)
        {
            ListeningChanged?.Invoke(false);
            Debug.LogWarning($"[MuseDesk] voice error: {error} {message}");
        }

        static string SafeIntent(WitResponseNode node)
        {
            try { return (node.GetIntentName() ?? "").ToLowerInvariant(); }
            catch { return ""; }
        }

        static bool MatchesAny(string text, string[] words)
        {
            if (string.IsNullOrEmpty(text)) return false;
            foreach (var w in words)
                if (text == w || text.Contains(" " + w) || text.StartsWith(w) || text.EndsWith(w)) return true;
            return false;
        }
    }
}
