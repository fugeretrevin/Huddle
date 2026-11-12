using System;
using UnityEngine;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif
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
#if UNITY_ANDROID && !UNITY_EDITOR
            // Mic is a dangerous permission on Quest; request on first use. The user re-triggers
            // after granting (a one-time flow), so we return rather than listen without permission.
            if (!Permission.HasUserAuthorizedPermission(Permission.Microphone))
            {
                Permission.RequestUserPermission(Permission.Microphone);
                return;
            }
#endif
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

            switch (Classify(text, intent))
            {
                case VoiceCommand.Approve: Approve?.Invoke(); break;
                case VoiceCommand.Reject:  Reject?.Invoke(); break;
                case VoiceCommand.Ask:     AskWhy?.Invoke(node.GetTranscription()); break;
                // None: unrecognized or ambiguous. Intentionally do nothing.
            }
        }

        public enum VoiceCommand { None, Approve, Reject, Ask }

        /// <summary>
        /// Map a transcript (lower-case) and optional Wit intent to a command. Approval is the
        /// consequential action, so it is the HARDEST to trigger: questions are checked first, then
        /// any negation ("don't approve", "not sure") rejects or does nothing, and only a short,
        /// unambiguous affirmative approves. Whole-word matching, never substrings.
        /// </summary>
        public static VoiceCommand Classify(string text, string intent)
        {
            text = (text ?? "").Trim().ToLowerInvariant();
            intent = (intent ?? "").ToLowerInvariant();
            if (text.Length == 0 && intent.Length == 0) return VoiceCommand.None;

            bool question = HasAny(text, WhyWords) || text.EndsWith("?");
            bool negated = HasAny(text, NegationWords);
            bool no = HasAny(text, NoWords);
            bool yes = HasAny(text, YesWords);

            if (question) return VoiceCommand.Ask;
            if (no && !yes) return VoiceCommand.Reject;
            if (intent.Contains("reject") || intent.Contains("negat")) return VoiceCommand.Reject;
            // "don't approve", "not sure", "no, approve it": contradictory or hedged. Do nothing
            // rather than guess; the user can repeat a clear command or use their hands.
            if (negated || (no && yes)) return VoiceCommand.None;
            int words = text.Length == 0 ? 0 : text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Length;
            if ((yes || intent.Contains("confirm")) && words <= 6) return VoiceCommand.Approve;
            return VoiceCommand.None;
        }

        static readonly string[] NegationWords = { "don't", "dont", "do not", "not", "never", "no", "isn't", "wait", "hold on", "unsure", "maybe" };

        static bool HasAny(string text, string[] phrases)
        {
            if (string.IsNullOrEmpty(text)) return false;
            string padded = " " + System.Text.RegularExpressions.Regex.Replace(text, @"[^\w' ]+", " ") + " ";
            padded = System.Text.RegularExpressions.Regex.Replace(padded, @"\s+", " ");
            foreach (var p in phrases)
                if (padded.Contains(" " + p + " ")) return true;
            return false;
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

    }
}
