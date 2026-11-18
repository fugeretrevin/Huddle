using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif
using Oculus.Voice;
using Meta.WitAi;
using Meta.WitAi.Configuration;
using Meta.WitAi.Json;
using Meta.WitAi.Requests;

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

        [Header("rc21: one sentence across pauses")]
        [Tooltip("Join pieces the speech service cut at a pause into one sentence (see SentenceStitcher).")]
        public bool stitchSentences = true;
        [Tooltip("Microphone level (0..1) that counts as the user starting to talk again while a piece is held.")]
        // rc22 (owner: "less picky"): 0.1 -> 0.05, so a softly spoken continuation also extends the wait for more words. It
        // can only ever make the stitcher wait longer, and never past its hold cap (4 s after a piece).
        public float resumeSoundLevel = 0.05f;
        /// <summary>Set by VoiceCommandCenter: whether a piece already reads as a whole command (act now) or clearly goes on.</summary>
        public Func<string, PieceVerdict> SentenceVerdict;

        readonly SentenceStitcher _stitch = new SentenceStitcher();
        bool _pieceLive;            // stitching: the reopened microphone has started, events now belong to the new piece
        bool _rearmIssued;          // stitching: the microphone was asked to open again
        float _nextRearmTry;
        bool _endAsked;             // the user (or a cap) ended the request: the next final is the sentence
        float _pieceCap = 20f;
        bool Stitching => _stitch.Holding;

        /// <summary>For drivers: sentences that were joined from more than one piece, and the last one's piece count.</summary>
        public int StitchedSentences { get; private set; }
        public int LastPieces { get; private set; }
        public string LastSentence { get; private set; } = "";
        public string StitchState => "stitching=" + Stitching + " pieces=" + _stitch.Pieces + " resumed=" + _stitch.Resumed
            + " live=" + _pieceLive + " rearm=" + _rearmIssued + " text='" + _stitch.Text + "' holds=" + _stitch.HoldCount
            + " stitched=" + StitchedSentences + " lastPieces=" + LastPieces + " last='" + LastSentence + "'";

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
                    // (This object is the one WitService reads on every audio sample, so the values do apply. They only
                    // govern the CLIENT's closing; the service's own end-of-utterance is handled by the stitcher, rc21.)
                    rc.maxRecordingTime = Mathf.Max(rc.maxRecordingTime, maxRecordingSeconds, MinSentenceSeconds);
                    if (silenceSeconds > 0f) rc.minKeepAliveTimeInSeconds = Mathf.Max(rc.minKeepAliveTimeInSeconds, silenceSeconds, WordGapSeconds);
                    rc.minTranscriptionKeepAliveTimeInSeconds = Mathf.Max(rc.minTranscriptionKeepAliveTimeInSeconds, WordGapSeconds);
                    _pieceCap = rc.maxRecordingTime;
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
            _stitch.Reset();
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
            _endAsked = false;
            _driverMode = false;
            _pieceLive = _rearmIssued = false;
            _stitch.Begin(Time.unscaledTime);
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
            _endAsked = true;
            // Between two pieces (nothing new said yet): the sentence is what is in.
            if (Stitching && (!_pieceLive || !_stitch.Resumed)) { Deliver(_stitch.Finish(), true); return; }
            try { appVoice.Deactivate(); } catch { }
        }

        public void AbortRequest()
        {
            if (appVoice == null) return;
            _aborted = true;
            _requestOpen = false;
            _stitch.Reset();
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
            if (_requestOpen && Time.unscaledTime - _requestAt > Mathf.Max(maxRecordingSeconds, _pieceCap) + 12f)
            {
                // Words already joined are not thrown away because the last piece never came back.
                if (Stitching) { Deliver(_stitch.Finish(), true); return; }
                _requestOpen = false;
                _stitch.Reset();
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
            TickStitch(Time.unscaledTime);
        }

        // ───────────────────────── rc21: one sentence across the service's pauses ─────────────────────────

        void TickStitch(float now)
        {
            if (!_requestOpen || !Stitching) return;
            // Open the microphone again as soon as the service lets go of the previous piece (it refuses while that one is
            // still active, returning null, so this simply retries a few times a second).
            if (!_rearmIssued && now >= _nextRearmTry)
            {
                _nextRearmTry = now + 0.1f;
                if (_driverMode) _rearmIssued = true;      // the driver says when the microphone is open again
                else try
                {
                    var r = appVoice != null ? appVoice.Activate(new WitRequestOptions(), new VoiceServiceRequestEvents()) : null;
                    if (r != null) { _rearmIssued = true; _requestAt = now; }
                }
                catch (Exception e) { Debug.LogWarning("[MuseDesk] voice: could not reopen the microphone (" + e.GetType().Name + ")."); _rearmIssued = true; }
            }
            if (_pieceLive && !_stitch.Resumed && _level >= resumeSoundLevel) _stitch.OnSound(now);
            if (_stitch.Tick(now) == StitchStep.Emit) Deliver(_stitch.Finish(), true);     // Finish keeps words still in progress
        }

        /// <summary>The sentence is finished: close everything and hand it on (empty: nothing was heard).</summary>
        void Deliver(string sentence, bool stopMicrophone)
        {
            int pieces = Mathf.Max(1, _piecesPending);
            _piecesPending = 0;
            _gotFinal = true;
            _requestOpen = false;
            _pieceLive = _rearmIssued = false;
            if (stopMicrophone)
            {
                _aborted = true;        // the reopened microphone's own stop / abort events are not news
                if (!_driverMode) { try { appVoice.DeactivateAndAbortRequest(); } catch { } }
            }
            SetListening(false);
            if (string.IsNullOrWhiteSpace(sentence)) { Raise(VoiceProblem.NoSpeech, null); return; }
            LastSentence = sentence.Trim();
            LastPieces = pieces;
            if (pieces > 1) StitchedSentences++;
            FinalTranscript?.Invoke(LastSentence);
        }

        int _piecesPending;
        bool _driverMode;           // fake service events from a driver: never open the real microphone

        /// <summary>For Play Mode drivers (AgentScripts/Rc21VoiceProbe): open a request with fake service events, no microphone
        /// and no network. Follow with DriverPiece / DriverMicReopened / DriverPartial; the stitcher and VoiceCommandCenter run
        /// exactly as with the real service.</summary>
        public void DriverOpen()
        {
            if (_requestOpen) AbortRequest();
            _driverMode = true;
            _requestOpen = true;
            _requestAt = Time.unscaledTime;
            _gotFinal = _aborted = _endAsked = false;
            _pieceLive = _rearmIssued = false;
            _stitch.Begin(Time.unscaledTime);
            Push(Ev.Start);
        }
        public void DriverPartial(string text) { Push(Ev.Partial, text); }
        /// <summary>The service calls a piece final (and repeats it with its response, as the real one does).</summary>
        public void DriverPiece(string text) { Push(Ev.Partial, text); Push(Ev.Full, text); Push(Ev.Full, text); Push(Ev.Stop); Push(Ev.Done); }
        public void DriverMicReopened() { Push(Ev.Start); }
        public void DriverSilence() { Push(Ev.Stop); Push(Ev.Done); }

        void Handle(Queued q)
        {
            float now = Time.unscaledTime;
            switch (q.Kind)
            {
                case Ev.Start:
                    if (_aborted) break;
                    if (Stitching)
                    {
                        // The microphone is open again for the rest of the sentence (the app never stopped "listening").
                        if ((_rearmIssued || _driverMode) && !_pieceLive) { _rearmIssued = true; _pieceLive = true; _stitch.OnMicReopened(now); }
                        break;
                    }
                    SetListening(true);
                    break;
                case Ev.Stop:
                    if (Stitching && _requestOpen) break;      // between two pieces the user is still being listened to
                    SetListening(false);
                    break;
                case Ev.Partial:
                    if (!_requestOpen || string.IsNullOrWhiteSpace(q.A)) break;
                    if (Stitching && !_pieceLive) break;       // late words of the piece already taken
                    _stitch.OnPartial(q.A.Trim(), now);
                    PartialTranscript?.Invoke(_stitch.Display(q.A));
                    break;
                case Ev.Full:
                    {
                        if ((!_requestOpen || _gotFinal) && !string.IsNullOrWhiteSpace(q.A)) LogLatePiece(q.A);
                        if (!_requestOpen || _gotFinal || string.IsNullOrWhiteSpace(q.A)) break;
                        if (Stitching && !_pieceLive) break;   // the previous piece's response repeating its transcript
                        string piece = q.A.Trim();
                        bool closing = !stitchSentences || _endAsked;
                        PieceVerdict verdict = PieceVerdict.Unknown;
                        if (!closing && SentenceVerdict != null)
                        {
                            try { verdict = SentenceVerdict(_stitch.Display(piece)); } catch (Exception) { verdict = PieceVerdict.Unknown; }
                        }
                        StitchStep step = _stitch.OnFinal(piece, now, verdict, closing);
                        if (step == StitchStep.Ignore) break;
                        _piecesPending = _stitch.Pieces;
                        if (step == StitchStep.Emit) { Deliver(_stitch.TakeSentence(), false); break; }
                        // Held: the sentence may go on. Keep listening and open the microphone again for the next piece.
                        _pieceLive = false;
                        _rearmIssued = false;
                        _nextRearmTry = now;
                        _requestAt = now;
                        PartialTranscript?.Invoke(_stitch.Text);
                        break;
                    }
                case Ev.Inactive:
                    // The SDK closed the microphone because nobody spoke; Done follows.
                    break;
                case Ev.Done:
                    if (!_requestOpen) break;
                    if (Stitching)
                    {
                        // The reopened microphone closed with no new final (silence): the sentence is what is in.
                        // (A Done before the reopened microphone started is the previous piece completing: not news.)
                        if (_pieceLive) Deliver(_stitch.Finish(), false);
                        break;
                    }
                    _requestOpen = false;
                    SetListening(false);
                    if (!_gotFinal && !_aborted) Raise(VoiceProblem.NoSpeech, null);
                    break;
                case Ev.Error:
                    {
                        string code = q.A ?? "", message = q.B ?? "";
                        if (Stitching && _requestOpen)
                        {
                            // Words are already in: act on them rather than show an error. An error from the previous piece
                            // (after its transcript) is not news at all.
                            Debug.LogWarning("[MuseDesk] voice: speech service error while joining a sentence: " + Scrub(code) + " " + Scrub(message));
                            if (_pieceLive) Deliver(_stitch.Finish(), true);
                            break;
                        }
                        bool was = _requestOpen;
                        _requestOpen = false;
                        SetListening(false);
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

        string _lastLateLogged = "";

        /// <summary>rc22 diagnostics: words the service finalised after the sentence was already handed on (the user ended the
        /// request between pieces, or a cap closed it). They are not acted on; the log says so, once per distinct text, so a
        /// headset session shows whether words were lost. (The owner's own speech, on their own device: Debug.Log only.)</summary>
        void LogLatePiece(string text)
        {
            string t = text.Trim();
            if (t.Length == 0 || t == _lastLateLogged) return;
            if (LastSentence.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0) return;     // the repeat of a piece already used
            _lastLateLogged = t;
            if (t.Length > 160) t = t.Substring(0, 157) + "...";
            Debug.Log("[MuseDesk.Voice] dropped late piece \"" + t.Replace('"', '\'') + "\": it arrived after the sentence \""
                      + LastSentence.Replace('"', '\'') + "\" was already handed on" + (_aborted ? " (request closed)" : ""));
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
