using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
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
    /// The Meta Voice SDK (Wit.ai) as a transcript source. It opens the microphone, reports the
    /// level, the partial words and the final sentence, and nothing else: no intent matching is
    /// done here or expected from Wit, so any app token works. Needs internet and a client token in
    /// the WitConfiguration; it does not need the desktop backend.
    ///
    /// The microphone permission is requested on the first request, never at launch.
    /// The token is never read into a log or a message; only whether one is present is checked.
    /// </summary>
    public class WitVoiceSource : MonoBehaviour, IVoiceTranscriptSource, IVoiceCommandSource
    {
        [Tooltip("The Voice SDK service. Optional: without it the source reports NoRecognizer and stays inert.")]
        public AppVoiceExperience appVoice;
        [Tooltip("Longest one request may record (s).")]
        public float maxRecordingSeconds = 6f;
        [Tooltip("Silence after speech that ends the request (s).")]
        public float silenceSeconds = 1.4f;
        /// <summary>A pause this long between words still belongs to the same sentence.</summary>
        public const float WordGapSeconds = 2.2f;
        /// <summary>A sentence (a goal for the Manager) may run this long.</summary>
        public const float MinSentenceSeconds = 15f;

        public event Action<string> PartialTranscript;
        public event Action<string> FinalTranscript;
        public event Action<bool> ListeningChanged;
        public event Action<float> MicLevelChanged;
        public event Action<VoiceProblem, string> Problem;

        // IVoiceCommandSource: kept for the paper's chip and the old router; never raised.
#pragma warning disable 67
        public event Action Approve;
        public event Action Reject;
        public event Action RequestChanges;
        public event Action<string> AskWhy;
        public event Action<string> StatusChanged;
#pragma warning restore 67

        /// <summary>The microphone key calls this when it exists, so every way of starting voice
        /// goes through the same checks and the same feedback. Set by VoiceCommandCenter.</summary>
        public Action ToggleHandler;

        public bool IsListening { get; private set; }
        public bool IsAvailable => Availability == VoiceProblem.None;

        public VoiceProblem Availability
        {
            get
            {
                if (appVoice == null || !appVoice.isActiveAndEnabled) return VoiceProblem.NoRecognizer;
                if (!HasToken()) return VoiceProblem.NoToken;
                if (Application.internetReachability == NetworkReachability.NotReachable) return VoiceProblem.NoInternet;
                if (_permissionDenied) return VoiceProblem.PermissionDenied;
                return VoiceProblem.None;
            }
        }

        // SDK callbacks are queued and handed out in Update, so listeners always run on the main
        // thread whatever thread the SDK used.
        enum Ev { Partial, Full, Start, Stop, Error, Done, Inactive }
        struct Queued { public Ev Kind; public string A, B; }
        readonly Queue<Queued> _queue = new Queue<Queued>();
        readonly object _lock = new object();
        volatile float _level;
        float _levelShown = -1f;

        bool _hooked;
        bool _requestOpen;          // between BeginRequest and its transcript / problem
        bool _gotFinal;
        bool _aborted;
        bool _permissionDenied;
        bool _tokenChecked, _hasToken;

        void Reset() { appVoice = GetComponent<AppVoiceExperience>(); }

        void OnEnable()
        {
            if (appVoice == null) appVoice = GetComponent<AppVoiceExperience>();
            if (appVoice == null) return;
            try
            {
                var rc = appVoice.RuntimeConfiguration;
                if (rc != null)
                {
                    // rc20 (worn rc19): the request stopped between two words and the sentence came back half said. The SDK
                    // closes the request 1 s after the last new word by default; a goal for the Manager needs pauses to think.
                    if (maxRecordingSeconds > 0f) rc.maxRecordingTime = Mathf.Max(maxRecordingSeconds, MinSentenceSeconds);
                    if (silenceSeconds > 0f) rc.minKeepAliveTimeInSeconds = Mathf.Max(silenceSeconds, WordGapSeconds);
                    rc.minTranscriptionKeepAliveTimeInSeconds = WordGapSeconds;
                }
                var ev = appVoice.VoiceEvents;
                ev.OnPartialTranscription.AddListener(SdkPartial);
                ev.OnFullTranscription.AddListener(SdkFull);
                ev.OnStartListening.AddListener(SdkStart);
                ev.OnStoppedListening.AddListener(SdkStop);
                ev.OnStoppedListeningDueToInactivity.AddListener(SdkInactive);
                ev.OnResponse.AddListener(SdkResponse);
                ev.OnError.AddListener(SdkError);
                ev.OnRequestCompleted.AddListener(SdkDone);
                ev.OnAborted.AddListener(SdkDone);
                ev.OnMicLevelChanged.AddListener(SdkLevel);
                _hooked = true;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[MuseDesk] voice: the speech service could not be hooked (" + e.GetType().Name + "). Voice stays off.");
                _hooked = false;
            }
        }

        void OnDisable()
        {
            if (appVoice != null && _hooked)
            {
                try
                {
                    var ev = appVoice.VoiceEvents;
                    ev.OnPartialTranscription.RemoveListener(SdkPartial);
                    ev.OnFullTranscription.RemoveListener(SdkFull);
                    ev.OnStartListening.RemoveListener(SdkStart);
                    ev.OnStoppedListening.RemoveListener(SdkStop);
                    ev.OnStoppedListeningDueToInactivity.RemoveListener(SdkInactive);
                    ev.OnResponse.RemoveListener(SdkResponse);
                    ev.OnError.RemoveListener(SdkError);
                    ev.OnRequestCompleted.RemoveListener(SdkDone);
                    ev.OnAborted.RemoveListener(SdkDone);
                    ev.OnMicLevelChanged.RemoveListener(SdkLevel);
                }
                catch { }
            }
            _hooked = false;
            _requestOpen = false;
            SetListening(false);
            lock (_lock) _queue.Clear();
        }

        bool HasToken()
        {
            if (_tokenChecked) return _hasToken;
            _tokenChecked = true;
            _hasToken = false;
            try
            {
                var cfg = appVoice != null ? appVoice.Configuration : null;
                _hasToken = cfg != null && !string.IsNullOrEmpty(cfg.GetClientAccessToken());
            }
            catch { _hasToken = false; }
            return _hasToken;
        }

        // ───────────────────────── IVoiceTranscriptSource ─────────────────────────

        public void BeginRequest()
        {
            if (IsListening || _requestOpen) return;
            VoiceProblem p = Availability;
            // A denied permission is asked for again: the user may have changed their mind.
            if (p != VoiceProblem.None && p != VoiceProblem.PermissionDenied) { Raise(p, null); return; }
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!Permission.HasUserAuthorizedPermission(Permission.Microphone))
            {
                var cb = new PermissionCallbacks();
                cb.PermissionGranted += _ => { _permissionDenied = false; Activate(); };
                cb.PermissionDenied += _ => { _permissionDenied = true; Raise(VoiceProblem.PermissionDenied, null); };
                Permission.RequestUserPermission(Permission.Microphone, cb);
                return;
            }
            _permissionDenied = false;
#endif
            Activate();
        }

        void Activate()
        {
            if (appVoice == null || !_hooked) { Raise(VoiceProblem.NoRecognizer, null); return; }
            _requestOpen = true;
            _requestAt = Time.unscaledTime;
            _gotFinal = false;
            _aborted = false;
            try { appVoice.Activate(); }
            catch (Exception e)
            {
                _requestOpen = false;
                Debug.LogWarning("[MuseDesk] voice: could not open the microphone (" + e.GetType().Name + ").");
                Raise(VoiceProblem.ServiceError, "The microphone could not be opened.");
            }
        }

        public void EndRequest()
        {
            if (appVoice == null || !_requestOpen) return;
            try { appVoice.Deactivate(); } catch { }
        }

        public void AbortRequest()
        {
            if (appVoice == null) return;
            _aborted = true;
            _requestOpen = false;
            try { appVoice.DeactivateAndAbortRequest(); } catch { }
            SetListening(false);
        }

        // ───────────────────────── IVoiceCommandSource (the paper's Voice chip) ─────────────────────────

        /// <summary>The paper's Voice chip: the same thing as the microphone key.</summary>
        public void StartListening()
        {
            if (ToggleHandler != null) { ToggleHandler(); return; }
            if (IsListening) EndRequest(); else BeginRequest();
        }

        public void StopListening() { EndRequest(); }

        // ───────────────────────── SDK callbacks (any thread) ─────────────────────────

        void Push(Ev kind, string a = null, string b = null)
        {
            lock (_lock) _queue.Enqueue(new Queued { Kind = kind, A = a, B = b });
        }

        void SdkPartial(string text) { Push(Ev.Partial, text); }
        void SdkFull(string text) { Push(Ev.Full, text); }
        void SdkStart() { Push(Ev.Start); }
        void SdkStop() { Push(Ev.Stop); }
        void SdkInactive() { Push(Ev.Inactive); }
        void SdkError(string error, string message) { Push(Ev.Error, error, message); }
        void SdkDone() { Push(Ev.Done); }
        void SdkLevel(float level) { _level = level; }

        void SdkResponse(WitResponseNode node)
        {
            // Some paths deliver the sentence only with the response.
            string text = null;
            try { text = node != null ? node.GetTranscription() : null; } catch { }
            Push(Ev.Full, text);
        }

        // ───────────────────────── main thread ─────────────────────────

        float _requestAt;

        void Update()
        {
            // A request the service never acknowledged (no microphone opened, no answer, no error)
            // must not block the next one.
            if (_requestOpen && Time.unscaledTime - _requestAt > maxRecordingSeconds + 12f)
            {
                _requestOpen = false;
                try { appVoice.DeactivateAndAbortRequest(); } catch { }
                SetListening(false);
                Raise(VoiceProblem.Timeout, null);
            }
            if (IsListening)
            {
                float level = Mathf.Clamp01(_level);
                if (Mathf.Abs(level - _levelShown) > 0.01f) { _levelShown = level; MicLevelChanged?.Invoke(level); }
            }
            while (true)
            {
                Queued q;
                lock (_lock)
                {
                    if (_queue.Count == 0) break;
                    q = _queue.Dequeue();
                }
                try { Handle(q); }
                catch (Exception e) { Debug.LogException(e); }
            }
        }

        void Handle(Queued q)
        {
            switch (q.Kind)
            {
                case Ev.Start:
                    if (!_aborted) SetListening(true);
                    break;
                case Ev.Stop:
                    SetListening(false);
                    break;
                case Ev.Partial:
                    if (_requestOpen && !string.IsNullOrWhiteSpace(q.A)) PartialTranscript?.Invoke(q.A.Trim());
                    break;
                case Ev.Full:
                    if (!_requestOpen || _gotFinal || string.IsNullOrWhiteSpace(q.A)) break;
                    _gotFinal = true;
                    _requestOpen = false;
                    SetListening(false);
                    FinalTranscript?.Invoke(q.A.Trim());
                    break;
                case Ev.Inactive:
                    // The SDK closed the microphone because nobody spoke; Done follows.
                    break;
                case Ev.Done:
                    if (!_requestOpen) break;
                    _requestOpen = false;
                    SetListening(false);
                    if (!_gotFinal && !_aborted) Raise(VoiceProblem.NoSpeech, null);
                    break;
                case Ev.Error:
                    {
                        bool was = _requestOpen;
                        _requestOpen = false;
                        SetListening(false);
                        string code = q.A ?? "", message = q.B ?? "";
                        Debug.LogWarning("[MuseDesk] voice: speech service error: " + Scrub(code) + " " + Scrub(message));
                        if (!was || _gotFinal || _aborted) break;
                        string all = (code + " " + message).ToLowerInvariant();
                        if (Application.internetReachability == NetworkReachability.NotReachable
                            || all.Contains("resolve") || all.Contains("network") || all.Contains("connect"))
                            Raise(VoiceProblem.NoInternet, null);
                        else if (all.Contains("401") || all.Contains("unauthor") || all.Contains("token") || all.Contains("403"))
                            Raise(VoiceProblem.NoToken, "The speech service refused this build's token.");
                        else if (all.Contains("timeout") || all.Contains("timed out"))
                            Raise(VoiceProblem.Timeout, null);
                        else if (all.Contains("permission") || all.Contains("mic"))
                            Raise(VoiceProblem.ServiceError, "The microphone could not be used.");
                        else Raise(VoiceProblem.ServiceError, null);
                        break;
                    }
            }
        }

        void SetListening(bool on)
        {
            if (IsListening == on) return;
            IsListening = on;
            if (!on) { _level = 0f; _levelShown = -1f; }
            ListeningChanged?.Invoke(on);
        }

        void Raise(VoiceProblem problem, string detail)
        {
            Problem?.Invoke(problem, detail);
        }

        // Nothing that looks like a credential ever reaches the log.
        static readonly Regex LongToken = new Regex("[A-Za-z0-9_\\-]{20,}");
        static string Scrub(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            if (s.Length > 160) s = s.Substring(0, 160);
            return LongToken.Replace(s, "***");
        }
    }
}
