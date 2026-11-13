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
    /// request-changes / ask-why. Drives off spoken text so it works with an untrained Wit app, while
    /// preserving the rule that voice NEVER bypasses backend authorization.
    /// </summary>
    [RequireComponent(typeof(AppVoiceExperience))]
    public class WitVoiceSource : MonoBehaviour, IVoiceCommandSource
    {
        public AppVoiceExperience appVoice;

        public event Action Approve;
        public event Action Reject;
        public event Action RequestChanges;
        public event Action<string> AskWhy;
        public event Action<bool> ListeningChanged;
        public event Action<string> StatusChanged;

        static readonly string[] YesWords =
        {
            "approve", "approved", "yes", "yes please", "ship it", "ship", "confirm", "confirmed",
            "go ahead", "accept", "sounds good", "looks good", "lgtm", "do it", "sure"
        };
        static readonly string[] NoWords =
        {
            "reject", "rejected", "no", "no thanks", "nope", "deny", "denied", "don't", "do not",
            "stop", "cancel", "decline"
        };
        static readonly string[] ChangeWords =
        {
            "request changes", "request change", "make changes", "changes", "change", "revise",
            "revision", "modify", "edit"
        };
        static readonly string[] WhyWords =
        {
            "why", "explain", "reason", "tell me", "what if", "how come", "justify"
        };

        public bool IsAvailable => appVoice != null;

        bool _listening;
        bool _gotResult;

        void Reset() { appVoice = GetComponent<AppVoiceExperience>(); }

        void OnEnable()
        {
            if (appVoice == null) appVoice = GetComponent<AppVoiceExperience>();
            if (appVoice == null) return;
            appVoice.VoiceEvents.OnResponse.AddListener(OnResponse);
            appVoice.VoiceEvents.OnError.AddListener(OnError);
            appVoice.VoiceEvents.OnStartListening.AddListener(OnSdkStartListening);
            appVoice.VoiceEvents.OnStoppedListening.AddListener(OnSdkStoppedListening);
            appVoice.VoiceEvents.OnAborted.AddListener(OnSdkStopped);
            appVoice.VoiceEvents.OnCanceled.AddListener(OnSdkCanceled);
            appVoice.VoiceEvents.OnPartialTranscription.AddListener(OnPartial);
            appVoice.VoiceEvents.OnFullTranscription.AddListener(OnFull);
        }

        void OnDisable()
        {
            if (appVoice == null) return;
            appVoice.VoiceEvents.OnResponse.RemoveListener(OnResponse);
            appVoice.VoiceEvents.OnError.RemoveListener(OnError);
            appVoice.VoiceEvents.OnStartListening.RemoveListener(OnSdkStartListening);
            appVoice.VoiceEvents.OnStoppedListening.RemoveListener(OnSdkStoppedListening);
            appVoice.VoiceEvents.OnAborted.RemoveListener(OnSdkStopped);
            appVoice.VoiceEvents.OnCanceled.RemoveListener(OnSdkCanceled);
            appVoice.VoiceEvents.OnPartialTranscription.RemoveListener(OnPartial);
            appVoice.VoiceEvents.OnFullTranscription.RemoveListener(OnFull);
        }

        public void StartListening()
        {
            if (appVoice == null)
            {
                StatusChanged?.Invoke("Voice error: Voice SDK missing");
                return;
            }
            if (_listening)
            {
                StopListening();
                return;
            }
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!Permission.HasUserAuthorizedPermission(Permission.Microphone))
            {
                StatusChanged?.Invoke("Mic permission needed — allow and tap Voice again");
                var cb = new PermissionCallbacks();
                cb.PermissionGranted += _ => ActivateNow();
                cb.PermissionDenied += _ => StatusChanged?.Invoke("Mic permission denied");
                Permission.RequestUserPermission(Permission.Microphone, cb);
                return;
            }
#endif
            ActivateNow();
        }

        void ActivateNow()
        {
            if (appVoice == null) return;
            _gotResult = false;
            SetListening(true);
            StatusChanged?.Invoke("Listening… say Approve, Reject, Changes, or Why?");
            appVoice.Activate();
        }

        public void StopListening()
        {
            if (appVoice == null) return;
            appVoice.Deactivate();
            SetListening(false);
            StatusChanged?.Invoke("");
        }

        void SetListening(bool on)
        {
            if (_listening == on) return;
            _listening = on;
            ListeningChanged?.Invoke(on);
        }

        void OnSdkStartListening() => SetListening(true);

        void OnSdkStoppedListening()
        {
            SetListening(false);
        }

        void OnSdkStopped()
        {
            SetListening(false);
            if (!_gotResult) StatusChanged?.Invoke("");
        }

        void OnSdkCanceled(string _) => OnSdkStopped();

        void OnPartial(string text)
        {
            if (!string.IsNullOrWhiteSpace(text))
                StatusChanged?.Invoke("Hearing: “" + text.Trim() + "”");
        }

        void OnFull(string text)
        {
            if (!string.IsNullOrWhiteSpace(text))
                StatusChanged?.Invoke("Heard: “" + text.Trim() + "”");
        }

        void OnResponse(WitResponseNode node)
        {
            SetListening(false);
            if (node == null) return;

            string intent = SafeIntent(node);
            string raw = node.GetTranscription() ?? "";
            string text = raw.Trim().ToLowerInvariant();
            var cmd = Classify(text, intent);
            _gotResult = cmd != VoiceCommand.None;

            switch (cmd)
            {
                case VoiceCommand.Approve:        Approve?.Invoke(); break;
                case VoiceCommand.Reject:         Reject?.Invoke(); break;
                case VoiceCommand.RequestChanges: RequestChanges?.Invoke(); break;
                case VoiceCommand.Ask:            AskWhy?.Invoke(raw); break;
                default:
                    if (!string.IsNullOrEmpty(text))
                        StatusChanged?.Invoke($"Heard “{raw.Trim()}” (say approve, reject, changes, or why?)");
                    break;
            }
        }

        public enum VoiceCommand { None, Approve, Reject, RequestChanges, Ask }

        /// <summary>
        /// Map a transcript (lower-case) and optional Wit intent to a command. Approval is the
        /// consequential action, so it is the HARDEST to trigger: questions first, then changes,
        /// then negation/reject, then short unambiguous affirmative. Whole-word/phrase matching only.
        /// </summary>
        public static VoiceCommand Classify(string text, string intent)
        {
            text = (text ?? "").Trim().ToLowerInvariant();
            intent = (intent ?? "").ToLowerInvariant();
            if (text.Length == 0 && intent.Length == 0) return VoiceCommand.None;

            bool question = HasAny(text, WhyWords) || text.EndsWith("?");
            bool changes = HasAny(text, ChangeWords);
            bool negated = HasAny(text, NegationWords);
            bool no = HasAny(text, NoWords);
            bool yes = HasAny(text, YesWords);

            if (question) return VoiceCommand.Ask;
            if (changes && !negated) return VoiceCommand.RequestChanges;
            if (no && !yes) return VoiceCommand.Reject;
            if (intent.Contains("reject") || intent.Contains("negat")) return VoiceCommand.Reject;
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
            SetListening(false);
            Debug.LogWarning($"[MuseDesk] voice error: {error} {message}");
            StatusChanged?.Invoke($"Voice error: {(string.IsNullOrEmpty(error) ? message : error)}");
        }

        static string SafeIntent(WitResponseNode node)
        {
            try { return (node.GetIntentName() ?? "").ToLowerInvariant(); }
            catch { return ""; }
        }
    }
}
