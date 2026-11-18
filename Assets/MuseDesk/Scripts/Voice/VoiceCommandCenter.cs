using System;
using System.Collections.Generic;
using UnityEngine;
using MuseDesk.Models;
using MuseDesk.Networking;
using MuseDesk.Spatial;
using MuseDesk.Workspace;

namespace MuseDesk.Voice
{
    /// <summary>
    /// Voice, end to end: the microphone key (or the gesture, or the paper's Voice chip) opens the
    /// microphone; the recogniser returns words; the local grammar (VoiceIntentParser) turns the
    /// final sentence into an intent; this component carries it out through the office's existing
    /// public methods (VoiceOffice) and says on the readout what it heard and what it did.
    ///
    /// Rules it keeps:
    ///  - Partial words are shown, not acted on. Two exceptions, both of which only ever halt
    ///    something: "stop" while the office is moving, and "cancel" while a question is open.
    ///  - A decision is sent only while one is pending and the paper has been taken and opened,
    ///    only on the word, and only after an undo window ("Approving... say cancel"). What is sent
    ///    is the paper's own key method, so the backend still decides; the readout says "sent",
    ///    never "approved".
    ///  - Everything an agent "says" is read from the run state; in the scripted demo the readout
    ///    labels it SCRIPTED DEMO.
    ///  - A new task is created only on the live server, after the heard title was confirmed.
    ///  - With no recogniser, no token, no internet or no permission, nothing here throws, nothing
    ///    is blocked and the keys work as before; the reason is shown when the user asks for voice.
    ///
    /// AgentScripts/VoiceDrive drives it without a microphone (InjectFinal / InjectPartial).
    /// </summary>
    public class VoiceCommandCenter : MonoBehaviour
    {
        [Header("Wiring (all optional; found at run time when empty)")]
        public SessionController session;
        [Tooltip("The recogniser: a component implementing IVoiceTranscriptSource (WitVoiceSource).")]
        public MonoBehaviour sourceBehaviour;
        public VoiceHud hud;
        [Tooltip("The old policy component; told what was heard so its listeners keep working.")]
        public VoiceDecisionController policy;

        [Header("Timing")]
        [Tooltip("Longest the microphone stays open for one sentence (s).")]
        public float maxListenSeconds = 6f;
        [Tooltip("Longest to wait for the speech service after the microphone closed (s).")]
        public float maxThinkSeconds = 8f;
        [Tooltip("How long a spoken decision can be cancelled before it is sent (s).")]
        public float undoSeconds = 1.5f;
        [Tooltip("Extra time the undo window stays open while the user is still speaking (s).")]
        public float undoSpeechGraceSeconds = 3f;
        [Tooltip("How long a new task waits for Confirm before it is dropped (s).")]
        public float confirmSeconds = 14f;
        public float understoodSeconds = 3f;
        public float notUnderstoodSeconds = 4.5f;
        public float answerSeconds = 6f;
        public float helpSeconds = 10f;

        [Header("Behaviour")]
        [Tooltip("Act on the word 'stop' as soon as it is heard (before the sentence is final) while the office is moving.")]
        public bool stopOnPartial = true;
        [Tooltip("Open the microphone again by itself while a question (confirm / cancel) is open.")]
        public bool relistenWhileAsking = true;
        [Tooltip("Show 'say approve or reject' once per run when a decision is ready and voice can be used.")]
        public bool showDecisionHint = true;

        public static VoiceCommandCenter Instance { get; private set; }

        enum Display { None, Listening, Thinking, Result, Prompt, Info }
        enum Pending { None, Decision, Task }

        readonly VoiceOffice _office = new VoiceOffice();
        IVoiceTranscriptSource _source;
        bool _hooked;

        Display _display;
        float _displayUntil;
        bool _listening, _thinking, _autoListen;
        float _listenStart, _thinkStart, _relistenAt = -1f;
        string _partial = "";
        VoiceIntentKind _actedOnPartial;

        Pending _pending;
        string _pendingAction, _pendingDecisionId, _pendingTitle;
        float _pendingDeadline;

        bool _hintShown;
        float _nextSlowTick;
        string _paperLine;
        readonly HashSet<int> _loggedProblems = new HashSet<int>();

        // For the driver.
        public string LastTranscript { get; private set; } = "";
        public string LastIntent { get; private set; } = "";
        public string LastMessage { get; private set; } = "";
        public int HandledCount { get; private set; }

        public VoiceOffice Office => _office;
        public bool IsListening => _listening;
        /// <summary>rc21: the longest the microphone stays open for one sentence, pauses included.</summary>
        public float ListenCapSeconds => Mathf.Max(maxListenSeconds, SentenceStitcher.DefaultMaxSeconds + 2f);

        /// <summary>rc21: given the words so far of a piece the speech service called final, whether they already read as a
        /// whole command (act now: "stop", "approve", "floor mode"), clearly go on ("Manager," alone; "new task" with no
        /// title), or neither (the stitcher waits a short grace for more words when the piece is more than two words).</summary>
        public PieceVerdict SentenceVerdict(string text)
        {
            string heard = WakeWord.Canonical(VoiceAnswers.Ascii(text ?? "").Trim());
            if (heard.Length == 0) return PieceVerdict.Unknown;
            if (_pending != Pending.None) return PieceVerdict.Complete;          // an open question: answers are short
            // rc22: only someone starting to talk ("okay so", "hey", "um"): wait for the rest rather than act on it.
            if (VoiceFiller.IsOnlyFiller(heard)) return PieceVerdict.Incomplete;
            VoiceAgentTarget who; string rest;
            bool named = WakeWord.Parse(heard, out who, out rest);
            if (named && (string.IsNullOrWhiteSpace(rest) || VoiceFiller.IsOnlyFiller(rest))) return PieceVerdict.Incomplete;   // "Manager," / "Manager, um" then a pause
            string via;
            VoiceIntent intent = ParseForgiving(heard, out via);                 // rc22: after the name, without filler
            switch (intent.Kind)
            {
                case VoiceIntentKind.None:
                case VoiceIntentKind.AskAgent:
                case VoiceIntentKind.AskWhy:
                case VoiceIntentKind.Start:
                    return PieceVerdict.Unknown;                                 // free speech: may go on
                case VoiceIntentKind.CreateTask:
                    return string.IsNullOrEmpty(intent.Text) ? PieceVerdict.Incomplete : PieceVerdict.Unknown;
                default:
                    return PieceVerdict.Complete;                                // a closed command
            }
        }
        public bool HasPendingQuestion => _pending != Pending.None;

        // ───────────────────────── hands-free mic ─────────────────────────
        // Muted by default (privacy). Unmuted: the microphone re-arms by itself after each sentence, silence or
        // error (HandsFreeMic does the timing and backoff). Every sentence still goes through VoiceIntentParser
        // and the same confirmation rules: a spoken decision still needs the paper taken and opened, the undo
        // window and the word "cancel"; a new task still needs "confirm". Ambient speech that is not a command is
        // dropped silently (no "Not understood" card), and the Manager classifier is not consulted.

        readonly HandsFreeMic _mic = new HandsFreeMic();
        readonly IMicPrefStore _micPrefs = new PlayerPrefsMicStore();
        bool _micLoaded;
        bool _ambient;          // the open request was started by the hands-free loop
        bool _ambientQuiet;     // ... and nobody has spoken yet: no card, no listening animation

        public bool MicUnmuted => _mic.Unmuted;
        public MicPhase MicPhase => _mic.Phase;
        public MicProblem MicBlocked => _mic.Blocked;
        public int MicBeginCount => _mic.BeginCount;
        public int MicConsecutiveErrors => _mic.ConsecutiveErrors;
        /// <summary>Raised when the mic key should redraw (phase or mute changed).</summary>
        public event Action MicStateChanged;
        MicPhase _micShown = (MicPhase)(-1);

        void LoadMicPref()
        {
            if (_micLoaded) return;
            _micLoaded = true;
            bool on = false;
            try { on = MicPreference.Load(_micPrefs); } catch { }
            _mic.SetUnmuted(on, Time.unscaledTime);
        }

        /// <summary>Mute or unmute and remember it on this device. Returns the resulting state: unmuting is refused
        /// (and the reason shown) when this build has no speech service or token, so the key never pretends.</summary>
        public bool SetMicUnmuted(bool on)
        {
            LoadMicPref();
            if (on)
            {
                if (!_hooked) Hook();
                VoiceProblem p = _source == null ? VoiceProblem.NoRecognizer : _source.Availability;
                if (p == VoiceProblem.NoRecognizer || p == VoiceProblem.NoToken)
                {
                    _mic.SetUnmuted(false, Time.unscaledTime);
                    ShowProblem(p, null);
                    NotifyMic();
                    return false;
                }
            }
            _mic.SetUnmuted(on, Time.unscaledTime);
            try { MicPreference.Save(_micPrefs, on); } catch { }
            if (!on)
            {
                // Muted: nothing listens. Drop an open ambient request; a manual one is left to finish.
                if (_ambient && (_listening || _thinking)) Abort();
                _ambient = _ambientQuiet = false;
                EndDisplay();
            }
            RefreshMic();
            NotifyMic();
            return on;
        }

        public bool ToggleMic() { LoadMicPref(); return SetMicUnmuted(!_mic.Unmuted); }

        void TickMic(float now)
        {
            LoadMicPref();
            VoiceProblem avail = _source == null ? VoiceProblem.NoRecognizer : _source.Availability;
            MicAction a = _mic.Tick(now, (MicProblem)(int)avail, _listening, _thinking, _pending == Pending.None && !_aborting);
            if (a == MicAction.Begin) BeginListening(true, true);
            if (_mic.Phase != _micShown) { _micShown = _mic.Phase; NotifyMic(); }
        }

        void NotifyMic() { MicStateChanged?.Invoke(); }

        // ───────────────────────── lifecycle ─────────────────────────

        void OnEnable()
        {
            Instance = this;
            _office.Session = session;
            _office.RunChanged += OnRunChanged;
            Hook();
            LoadMicPref();
        }

        void OnDisable()
        {
            if (Instance == this) Instance = null;
            _office.RunChanged -= OnRunChanged;
            _office.Unbind();
            Unhook();
            _office.AgentsListen(false);
        }

        void Hook()
        {
            if (_hooked) return;
            if (sourceBehaviour == null)
            {
                foreach (var mb in GetComponentsInChildren<MonoBehaviour>(true))
                    if (mb is IVoiceTranscriptSource) { sourceBehaviour = mb; break; }
            }
            _source = sourceBehaviour as IVoiceTranscriptSource;
            if (_source == null) return;
            _source.PartialTranscript += OnPartial;
            _source.FinalTranscript += OnFinal;
            _source.ListeningChanged += OnListeningChanged;
            _source.MicLevelChanged += OnLevel;
            _source.Problem += OnProblem;
            var wit = sourceBehaviour as WitVoiceSource;
            if (wit != null) { wit.ToggleHandler = PressMic; wit.SentenceVerdict = SentenceVerdict; }
            _hooked = true;
        }

        void Unhook()
        {
            if (!_hooked || _source == null) { _hooked = false; return; }
            _source.PartialTranscript -= OnPartial;
            _source.FinalTranscript -= OnFinal;
            _source.ListeningChanged -= OnListeningChanged;
            _source.MicLevelChanged -= OnLevel;
            _source.Problem -= OnProblem;
            var wit = sourceBehaviour as WitVoiceSource;
            if (wit != null && wit.ToggleHandler == (Action)PressMic) wit.ToggleHandler = null;
            if (wit != null && wit.SentenceVerdict == (Func<string, PieceVerdict>)SentenceVerdict) wit.SentenceVerdict = null;
            _hooked = false;
        }

        void Update()
        {
            float now = Time.unscaledTime;
            _office.Find(false);
            VoiceAddressing.Shared.Tick(now);

            // The microphone never stays open longer than one sentence. rc21 (worn rc20, "it's not waiting for my full sentence"):
            // the scene's maxListenSeconds (6) ended every request after 6 s, mid-goal, whatever the speech service did. A
            // sentence may now run as long as the stitcher allows (pauses included); this cap is only the backstop.
            if (_listening && now - _listenStart > ListenCapSeconds && _source != null) _source.EndRequest();

            // The speech service never keeps the readout on "Thinking" for ever.
            if (_thinking && now - _thinkStart > maxThinkSeconds)
            {
                Abort();
                OnProblem(VoiceProblem.Timeout, null);
            }

            TickMic(now);

            if (_relistenAt >= 0f && now >= _relistenAt)
            {
                _relistenAt = -1f;
                if (_pending != Pending.None && !_listening && !_thinking) BeginListening(true);
            }

            if (_pending == Pending.Decision && now >= _pendingDeadline)
            {
                // Still speaking (perhaps saying "cancel"): wait for the sentence, within reason.
                bool speaking = (_listening || _thinking) && _partial.Length > 0;
                if (!speaking || now >= _pendingDeadline + undoSpeechGraceSeconds) CommitDecision();
            }
            else if (_pending == Pending.Task && now >= _pendingDeadline)
            {
                ClearPending();
                Result("New task", "Not sent: nobody confirmed it.", VoiceTone.Warn, understoodSeconds);
            }

            if (_display != Display.None && _display != Display.Listening && _display != Display.Thinking
                && _display != Display.Prompt && now >= _displayUntil)
                EndDisplay();

            if (now >= _nextSlowTick)
            {
                _nextSlowTick = now + 0.5f;
                RefreshMic();
                MaybeHint();
            }
        }

        // ───────────────────────── activation ─────────────────────────

        /// <summary>The microphone key, the paper's Voice chip and the gesture: open the microphone,
        /// or close it and transcribe when it is open.</summary>
        public void PressMic()
        {
            // A hand on the old key takes over an ambient request: it behaves like a normal press.
            if (_ambient) { PromoteAmbient(); _ambient = false; }
            if (_listening) { if (_source != null) _source.EndRequest(); return; }
            if (_thinking)
            {
                Abort();
                EndDisplay();
                RefreshMic();
                return;
            }
            BeginListening(false);
        }

        /// <summary>Open the microphone if it is closed (the gesture: it never closes it).</summary>
        public void BeginFromGesture()
        {
            if (!_listening && !_thinking) BeginListening(false);
        }

        void BeginListening(bool auto, bool ambient = false)
        {
            if (!_hooked) Hook();
            VoiceProblem p = _source == null ? VoiceProblem.NoRecognizer : _source.Availability;
            if (p != VoiceProblem.None && p != VoiceProblem.PermissionDenied)
            {
                if (!auto) ShowProblem(p, null);
                return;
            }
            _ambient = ambient;
            _ambientQuiet = ambient;
            _autoListen = auto;
            _partial = "";
            _actedOnPartial = VoiceIntentKind.None;
            _source.BeginRequest();     // ListeningChanged(true) follows once the microphone is open
        }

        // ───────────────────────── recogniser events ─────────────────────────

        void OnListeningChanged(bool on)
        {
            _listening = on;
            _mic.NotifyListeningChanged(on, Time.unscaledTime);
            if (!(_ambientQuiet && on)) _office.AgentsListen(on);
            else _office.AgentsListen(false);
            if (on)
            {
                _thinking = false;
                _listenStart = Time.unscaledTime;
                if (_pending == Pending.None && !_ambientQuiet)
                {
                    _display = Display.Listening;
                    if (hud != null) hud.Show("Listening", ModeWord(), "", TrySaying(), VoiceTone.Listening);
                }
            }
            else if (!_aborting)
            {
                // Closed: the sentence (or a reason there is none) follows.
                _thinking = true;
                _thinkStart = Time.unscaledTime;
                if (_pending == Pending.None && _display == Display.Listening)
                {
                    _display = Display.Thinking;
                    if (hud != null) hud.Show("Thinking", ModeWord(), Quote(_partial), "", VoiceTone.Neutral);
                }
            }
            RefreshMic();
        }

        void OnLevel(float level) { if (hud != null) hud.SetLevel(level); }

        void OnPartial(string text)
        {
            _partial = WakeWord.Canonical(VoiceAnswers.Ascii(text ?? "").Trim());
            // Hands-free: the card only appears once the sentence starts with a name (WakeWord); chatter stays silent.
            if (_partial.Length > 0) { _mic.NotifyPartial(); if (_ambientQuiet && (_pending != Pending.None || WakeWord.IsAddressed(_partial))) PromoteAmbient(); }
            if (_display == Display.Listening && hud != null) hud.SetBody(Quote(VoiceAnswers.Clip(_partial, 90)));
            if (_partial.Length > 0) _office.HearForAddressing(_partial);
            if (_actedOnPartial != VoiceIntentKind.None || _partial.Length == 0) return;

            // Halting is the only thing done before the sentence is final.
            bool moving = _office.Moving;
            if (!(moving && stopOnPartial) && _pending == Pending.None) return;
            VoiceIntent intent = VoiceIntentParser.Parse(_partial, CurrentContext());
            if (intent.Kind == VoiceIntentKind.Stop && (moving || _pending != Pending.None))
            {
                _actedOnPartial = VoiceIntentKind.Stop;
                Result("Heard: stop", DoStop(), VoiceTone.Warn, understoodSeconds);
            }
            else if (intent.Kind == VoiceIntentKind.Cancel && _pending != Pending.None)
            {
                _actedOnPartial = VoiceIntentKind.Cancel;
                Result("Heard: cancel", DoCancel(), VoiceTone.Warn, understoodSeconds);
            }
        }

        /// <summary>Words arrived on an ambient request: from now on it looks and behaves like a normal one.</summary>
        void PromoteAmbient()
        {
            if (!_ambientQuiet) return;
            _ambientQuiet = false;
            if (_listening)
            {
                _office.AgentsListen(true);
                if (_pending == Pending.None)
                {
                    _display = Display.Listening;
                    if (hud != null) hud.Show("Listening", ModeWord(), "", TrySaying(), VoiceTone.Listening);
                }
            }
        }

        void OnFinal(string text)
        {
            _thinking = false;
            _mic.NotifyFinal(Time.unscaledTime);
            var wit = sourceBehaviour as WitVoiceSource;
            HandleFinal(text, wit != null ? Mathf.Max(1, wit.LastPieces) : 1);
        }

        void OnProblem(VoiceProblem problem, string detail)
        {
            _thinking = false;
            _mic.NotifyProblem((MicProblem)(int)problem, Time.unscaledTime);
            if (_ambient && _pending == Pending.None)
            {
                // Hands-free never pops a card for silence or a failing service; the mic key shows the state.
                _ambient = _ambientQuiet = false;
                if (problem != VoiceProblem.NoSpeech && _loggedProblems.Add((int)problem)) Debug.Log("[MuseDesk] voice (hands-free) unavailable: " + problem);
                if (_display == Display.Listening || _display == Display.Thinking) EndDisplay();
                RefreshMic();
                return;
            }
            if (problem == VoiceProblem.NoSpeech)
            {
                if (!_autoListen) Debug.Log("[MuseDesk.Voice] heard nothing (mic key): the speech service returned no words");
                if (_pending != Pending.None) { ShowPrompt(); RefreshMic(); return; }
                if (_autoListen) EndDisplay();
                else Result("Heard nothing", TrySaying(), VoiceTone.Neutral, 2.5f);
                RefreshMic();
                return;
            }
            if (_autoListen && _pending != Pending.None) { ShowPrompt(); RefreshMic(); return; }
            ShowProblem(problem, detail);
        }

        // ───────────────────────── a finished sentence ─────────────────────────

        /// <summary>A final transcript, exactly as if the recogniser had produced it (the driver).</summary>
        public void InjectFinal(string transcript) { _thinking = false; HandleFinal(transcript, 1); }

        /// <summary>A final transcript as if the hands-free loop had heard it (the driver): same path, but quiet when it is not a command.</summary>
        public void InjectAmbientFinal(string transcript) { _thinking = false; _ambient = true; _ambientQuiet = false; HandleFinal(transcript, 1); }

        /// <summary>A partial transcript, exactly as if the recogniser had produced it (the driver).</summary>
        public void InjectPartial(string transcript)
        {
            if (_display == Display.None && _pending == Pending.None)
            {
                _display = Display.Listening;
                if (hud != null) hud.Show("Listening", ModeWord(), "", TrySaying(), VoiceTone.Listening);
            }
            OnPartial(transcript);
        }

        /// <summary>Set by the Manager's owner (the Wall habitat). Given the words after "Manager, "; returns the line to show, or
        /// null when the Manager is not available (the request then falls through to "Not understood").</summary>
        public static Func<string, string> ManagerRequestHandler;

        /// <summary>Set by the Wall habitat while it is on the wall with the portal flag on (Meet). Given the agent a sentence such
        /// as "Researcher, come here" or "meet the developer" names (<see cref="PortalVoiceRequest"/>); returns the line to show, or
        /// null when it is not handled (the sentence then goes through the normal grammar, exactly as before).</summary>
        public static Func<VoiceAgentTarget, string> PortalRequestHandler;

        /// <summary>rc18: set by the Wall habitat. The agent that is with the user right now (perched on the desk, or out of the
        /// portal), else None. While it is not None, natural speech that is not a command goes to that agent.</summary>
        public static Func<VoiceAgentTarget> CompanionAgent;
        /// <summary>rc18: set by the Wall habitat. Asks the companion a free question (read-only side conversation); returns
        /// the line to show ("Asking the Developer…"), or null when it could not be asked (the sentence then goes on as before).</summary>
        public static Func<string, string> CompanionQuestionHandler;

        /// <summary>rc22: set by the Wall habitat (optional). Whether the Manager can take a goal right now (on the Wall, Floor or
        /// Ceiling). Used only for sentences that do NOT name the Manager (the mic-key fallback below): when it says no, such a
        /// sentence gets the usual "Not understood" card instead of "the Manager works on the Wall...". Null: assume it can.</summary>
        public static Func<bool> ManagerReady;

        // ── rc22 diagnostics: one log line per final sentence (the owner's own speech, on their own device, Debug.Log only) ──
        string _logRoute, _logOutcome, _logDrop;
        bool _logAddressed;

        void Route(string route, string outcome) { _logRoute = route; _logOutcome = outcome ?? ""; }
        void Drop(string route, string reason) { _logRoute = route; _logOutcome = "dropped"; _logDrop = reason; }

        static string LogText(string s, int max = 160)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = s.Replace('"', '\'').Replace('\n', ' ').Replace('\r', ' ');
            return s.Length > max ? s.Substring(0, max - 3) + "..." : s;
        }

        void HandleFinal(string transcript, int pieces)
        {
            bool ambient = _ambient;
            _logRoute = "None"; _logOutcome = ""; _logDrop = null; _logAddressed = false;
            try { HandleFinalCore(transcript); }
            finally
            {
                string said = LogText(VoiceAnswers.Ascii(transcript ?? "").Trim());
                string read = LogText(LastTranscript);
                Debug.Log("[MuseDesk.Voice] heard \"" + said + "\" (pieces=" + pieces + ", hands-free=" + (ambient ? "true" : "false")
                          + ", addressed=" + (_logAddressed ? "true" : "false") + ") -> " + _logRoute
                          + (string.IsNullOrEmpty(_logOutcome) ? "" : " " + LogText(_logOutcome, 120))
                          + (read != said && read.Length > 0 ? " [read as \"" + read + "\"]" : ""));
                if (_logDrop != null) Debug.Log("[MuseDesk.Voice] dropped \"" + said + "\": " + _logDrop);
            }
        }

        static bool IsDecisionKind(VoiceIntentKind k)
        {
            return k == VoiceIntentKind.Approve || k == VoiceIntentKind.Reject || k == VoiceIntentKind.RequestChanges
                || k == VoiceIntentKind.DecisionNotPending;
        }

        /// <summary>rc22 (owner, worn rc21: "the mic needs to be less picky"): the grammar, tried on the sentence as heard
        /// first; when that means nothing, on the words after an address ("Hey team, open the board"), then without filler
        /// and politeness ("um, could you open the, uh, board for me please"). A cleaned sentence never becomes a decision:
        /// "can you approve it" stays a question, as the grammar's tests require. <paramref name="via"/> says which reading won.</summary>
        VoiceIntent ParseForgiving(string heard, out string via)
        {
            VoiceContext ctx = CurrentContext();
            via = "";
            VoiceIntent intent = VoiceIntentParser.Parse(heard, ctx);
            if (intent.Kind != VoiceIntentKind.None) return intent;

            VoiceAgentTarget who; string rest;
            bool named = WakeWord.Parse(heard, out who, out rest) && rest.Length > 0;
            if (named)
            {
                VoiceIntent r = VoiceIntentParser.Parse(rest, ctx);
                if (r.Kind != VoiceIntentKind.None) { via = " (after the name)"; return r; }
            }
            string clean = VoiceFiller.Clean(heard);
            if (clean != heard.Trim())
            {
                VoiceIntent r = VoiceIntentParser.Parse(clean, ctx);
                if (r.Kind != VoiceIntentKind.None && !IsDecisionKind(r.Kind)) { via = " (without filler: \"" + clean + "\")"; return r; }
            }
            if (named)
            {
                string cleanRest = VoiceFiller.Clean(rest);
                if (cleanRest != rest.Trim())
                {
                    VoiceIntent r = VoiceIntentParser.Parse(cleanRest, ctx);
                    if (r.Kind != VoiceIntentKind.None && !IsDecisionKind(r.Kind)) { via = " (after the name, without filler)"; return r; }
                }
            }
            return intent;
        }

        void HandleFinalCore(string transcript)
        {
            // A leading name the service misheard ("Research your why") is read as the name ("Researcher, why").
            string heard = WakeWord.Canonical(VoiceAnswers.Ascii(transcript ?? "").Trim());
            _office.Find(true);
            HandledCount++;
            LastTranscript = heard;
            if (heard.Length == 0) { LastIntent = "None"; Drop("None", "empty sentence"); OnProblem(VoiceProblem.NoSpeech, null); return; }
            bool ambient = _ambient;        // this sentence came from the hands-free loop
            _ambient = _ambientQuiet = false;

            // Who the sentence starts with, if anyone (rc22: forgiving names, see WakeWord). Read once for every path below.
            VoiceAgentTarget addrWho; string addrRest;
            bool addressedAny = WakeWord.Parse(heard, out addrWho, out addrRest);
            _logAddressed = addressedAny;

            string via;
            VoiceIntent intent = ParseForgiving(heard, out via);

            // Wall portal (Meet): "Researcher, come here" / "meet the developer" / (rc22) "come down here, developer". Only while
            // the Wall registered for it. Read before the companion: a "come here" that names an agent is a Meet request, never
            // a question for the agent already beside the user.
            VoiceAgentTarget meetWho;
            bool meetAsked = PortalVoiceRequest.Routes(heard, PortalRequestHandler != null, _pending != Pending.None, out meetWho);
            bool meetOnlyAddress = false;     // hands-free: the sentence passed the name rule only as a portal request

            // rc22 polish (owner: "agents respond to basic conversation even if they don't have context: small talk, playful
            // banter, flirting platonically reciprocated"): a sentence that is ONLY small talk ("hey developer, how are you",
            // "tell me a joke", "thanks!") gets a short scripted line in that agent's voice, spoken and shown, labelled scripted.
            // Never a command (halts and commands keep their routes), never changes anything. Hands-free: only when addressed
            // by name or to the agent beside the user (the privacy rule).
            if (_pending == Pending.None && !meetAsked && TrySmallTalk(heard, intent, ambient, addressedAny, addrWho, addrRest))
            {
                RefreshMic();
                return;
            }

            // rc18 (owner): talk naturally to the agent that is WITH you (perched on the desk or out of the portal), no name
            // needed: "hey, why are you doing this instead of that?". It goes to that agent's side conversation (live: the real
            // model answers as that agent; offline: the scripted reply), shown on its visit card. Read-only, as the keys are.
            if (_pending == Pending.None && !meetAsked && CompanionQuestionHandler != null && CompanionAgent != null)
            {
                VoiceAgentTarget companion = CompanionAgent();
                bool command = !(intent.Kind == VoiceIntentKind.None || intent.Kind == VoiceIntentKind.AskAgent || intent.Kind == VoiceIntentKind.AskWhy
                                 || intent.Kind == VoiceIntentKind.Status || intent.Kind == VoiceIntentKind.WhoNeedsMe);
                string question;
                if (CompanionAddressing.ForCompanion(heard, companion, command, out question))
                {
                    string reply = CompanionQuestionHandler(question);
                    if (reply != null)
                    {
                        _ambient = _ambientQuiet = false;
                        _actedOnPartial = VoiceIntentKind.None;
                        _partial = "";
                        LastIntent = "CompanionQuestion";
                        LastMessage = reply;
                        Route("CompanionQuestion:" + companion, "asked: " + reply);
                        if (policy != null) policy.NotifyHeard(LastMessage);
                        Result("You: " + VoiceAnswers.Clip(question, 60), reply, VoiceTone.Agent, 3.5f);
                        RefreshMic();
                        return;
                    }
                }
            }

            // Hands-free sentences must start with a name ("Researcher, ...", "Hey MuseDesk, ..."), unless they halt
            // something or answer a conversation the user already opened. The name itself is not a command, so when the
            // whole sentence parses to nothing, the words after the name are tried.
            // (rc22: the words after the name are already tried by ParseForgiving, for the mic key too.)
            bool addressed = false;
            if (ambient)
            {
                addressed = addressedAny;
                bool halting = intent.Kind == VoiceIntentKind.Stop || intent.Kind == VoiceIntentKind.Cancel;
                meetOnlyAddress = meetAsked && !WakeWord.Allows(addressed, _pending != Pending.None, halting);
                if (!PortalVoiceRequest.AllowsHandsFree(addressed, _pending != Pending.None, halting, meetAsked))
                {
                    LastIntent = "Unaddressed";
                    _actedOnPartial = VoiceIntentKind.None;
                    _partial = "";
                    if (WakeWord.MentionsAgent(heard))
                    {
                        // Worn rc15: a sentence about an agent that did not START with its name used to vanish silently.
                        // Nothing is acted on; the user is only told what was heard and how to address it.
                        LastIntent = "UnaddressedHint";
                        Result("Heard: " + VoiceAnswers.Clip(heard, 60), WakeWord.Hint, VoiceTone.Neutral, 3.5f);
                        Drop("UnaddressedHint", "hands-free sentence mentions an agent but does not start with a name (hint shown, nothing done; grammar read " + intent + ")");
                    }
                    else
                    {
                        if (_display == Display.Listening || _display == Display.Thinking) EndDisplay();
                        Drop("Unaddressed", "hands-free sentence does not start with a name and is not stop/cancel (privacy rule; grammar read " + intent + ")");
                    }
                    RefreshMic();
                    return;
                }
            }
            LastIntent = intent.ToString();

            // The halting word was already acted on while it was still a partial.
            VoiceIntentKind acted = _actedOnPartial;
            _actedOnPartial = VoiceIntentKind.None;
            _partial = "";
            if (acted != VoiceIntentKind.None && (intent.Kind == acted || intent.Kind == VoiceIntentKind.Stop || intent.Kind == VoiceIntentKind.Cancel))
            {
                Route(intent.ToString(), "already acted on while it was still a partial (" + acted + ")");
                RefreshMic();
                return;
            }

            // While a decision counts down, only words about it matter; chatter does not stop the clock.
            if (_pending == Pending.Decision && intent.Kind == VoiceIntentKind.None)
            {
                Drop("None", "a spoken decision is counting down; only cancel / stop / the other decision word count");
                ShowPrompt(); ScheduleRelisten(); return;
            }

            // Meet: the Wall opens the portal PREVIEW for that agent (nothing opens until Open here is pressed). It comes before
            // the grammar because "come here" is also the office's Bring here: while the habitat is on the wall (the only time a
            // handler is registered), a sentence that names one agent and asks it to come means that agent.
            if (meetAsked && _pending == Pending.None)
            {
                string reply = PortalRequestHandler != null ? PortalRequestHandler(meetWho) : null;
                if (reply != null)
                {
                    LastIntent = "PortalRequest";
                    LastMessage = reply;
                    Route("PortalRequest:" + meetWho, "sent: " + reply);
                    if (policy != null) policy.NotifyHeard(LastMessage);
                    Result("Heard: " + VoiceAnswers.Clip(heard, 60), reply, VoiceTone.Good, 4f);
                    RefreshMic();
                    return;
                }
                if (meetOnlyAddress)
                {
                    // Nobody took it, and without the portal exception this hands-free sentence was not addressed: stay quiet, as before.
                    LastIntent = "Unaddressed";
                    Drop("Unaddressed", "hands-free meet request the Wall did not take, and the sentence does not start with a name");
                    if (_display == Display.Listening || _display == Display.Thinking) EndDisplay();
                    RefreshMic();
                    return;
                }
            }

            // "Manager, <goal>": the local grammar has no such command; the A12 classifier recognises it. It is handed to whoever
            // registered as the Manager (the Wall habitat, when it is active and its flag is on). It only ASKS for a plan:
            // nothing is created until the user approves the plan paper.
            // (Hands-free: only when the sentence was addressed by name, e.g. "Manager, plan a login page".)
            // rc20 (worn rc19): "Manager, build a program that prints stars" parsed as the board's "new task" command
            // (CreateTask) and never reached the Manager. A sentence that NAMES the Manager is the Manager's, unless it halts.
            bool managerMayTake = intent.Kind == VoiceIntentKind.None || intent.Kind == VoiceIntentKind.CreateTask
                                  || intent.Kind == VoiceIntentKind.Start || intent.Kind == VoiceIntentKind.AskAgent;
            if ((!ambient || addressed) && managerMayTake && _pending == Pending.None && ManagerRequestHandler != null)
            {
                VoiceClassification c = VoiceIntentClassifier.Classify(heard);
                if (c.Kind == VoiceAction.ManagerRequest && !string.IsNullOrEmpty(c.Question) && c.Question.Trim().Length >= 3)
                {
                    // rc22: "Manager, um, could you plan a login page" -> "plan a login page".
                    string goal = VoiceFiller.Clean(c.Question.Trim());
                    if (goal.Length < 3) goal = c.Question.Trim();
                    string reply = ManagerRequestHandler(goal);
                    if (reply != null)
                    {
                        LastIntent = "ManagerRequest";
                        LastMessage = reply;
                        Route("ManagerRequest", "goal \"" + goal + "\": " + reply);
                        if (policy != null) policy.NotifyHeard(LastMessage);
                        Result("Heard: " + VoiceAnswers.Clip(heard, 60), reply, VoiceTone.Agent, 4f);
                        RefreshMic();
                        return;
                    }
                }
            }

            // rc22 (owner, worn rc21: "the mic needs to be less picky"): with the MIC KEY (or a hands-free sentence addressed to
            // everyone, "Hey MuseDesk, ..."), a sentence that is no command and names no agent is not thrown away as "Not
            // understood": it goes to the Manager as a goal or question. (The agent that is WITH the user already took natural
            // speech above.) Safe: the Manager only drafts a plan; nothing runs until the user approves the plan paper.
            // Hands-free room chatter (no name) never reaches this: it was dropped by the name rule above.
            bool unnamed = (addressedAny ? addrWho == VoiceAgentTarget.None : true)
                           && VoiceIntentClassifier.ResolveTarget(heard) == VoiceAgentTarget.None;
            bool comeNoName = PortalVoiceRequest.IsUnnamedComeRequest(addressedAny ? addrRest : heard);   // "come down here": say who
            if (intent.Kind == VoiceIntentKind.None && _pending == Pending.None && unnamed && !comeNoName && (!ambient || addressed)
                && ManagerRequestHandler != null && (ManagerReady == null || ManagerReady()))
            {
                string goal = VoiceFiller.Clean(addressedAny ? addrRest : heard);
                if (VoiceFiller.ContentWords(goal) >= 2 && goal.Length >= 6)
                {
                    string reply = ManagerRequestHandler(goal);
                    if (reply != null)
                    {
                        LastIntent = "ManagerRequest(unnamed)";
                        LastMessage = reply;
                        Route("ManagerRequest(unnamed)", "goal \"" + goal + "\": " + reply);
                        if (policy != null) policy.NotifyHeard(LastMessage);
                        Result("Heard: " + VoiceAnswers.Clip(heard, 60), reply, VoiceTone.Agent, 4.5f);
                        RefreshMic();
                        return;
                    }
                }
            }

            VoiceTone tone; float seconds; bool understood;
            string message = Execute(intent, out tone, out seconds, out understood);
            LastMessage = message ?? "";
            if (policy != null) policy.NotifyHeard(LastMessage);
            if (understood) Route(intent.ToString() + via, string.IsNullOrEmpty(message) ? (_pending != Pending.None ? "asked to confirm" : "done") : "done: " + message);
            else Route("None", "Not understood (" + WhyNotUnderstood(ambient, addressed, unnamed) + ")");

            if (_pending != Pending.None)
            {
                ShowPrompt();
                ScheduleRelisten();
            }
            else if (ambient && !addressed && !understood)
            {
                // Room chatter that is not a command: no card, no paper line, nothing sent. (Addressed by name and not
                // understood: the user meant to talk to us, so the normal "Not understood" card below answers.)
                if (_display == Display.Listening || _display == Display.Thinking) EndDisplay();
                Drop("None", "hands-free, not addressed, not a command (allowed through only as an open conversation's follow-up)");
            }
            else if (message != null)
            {
                string shown = VoiceAnswers.Clip(heard, 60);
                if (understood) Result("Heard: " + shown, message, tone, seconds);
                else
                {
                    // rc22 (owner): not a bare "Not understood": the agent the sentence was for (or the one beside the user, or
                    // the Manager) says it can't, and 2-3 things that would work now. Shown and spoken. (Hands-free room chatter
                    // never gets here: it was dropped above.)
                    FallbackFacts facts = BuildFallbackFacts(heard, addressedAny ? addrWho : VoiceAgentTarget.None, comeNoName);
                    string line = VoiceFallbackLine.Line(facts);
                    string who = VoiceFallbackLine.SpeakerId(facts);
                    LastMessage = line;
                    Result(VoiceAnswers.RoleName(who) + ": not understood", "Heard: " + Quote(shown) + "\n" + line, tone, Mathf.Max(seconds, 6f), TrySaying());
                    bool spoke = false;
                    try { spoke = AgentSpeech.Say(who, line, _office.SpeakerPosition(who)); } catch (Exception e) { Debug.LogWarning("[MuseDesk] voice: fallback line not spoken (" + e.GetType().Name + ")."); }
                    _logOutcome += " -> " + who + " says \"" + line + "\"" + (spoke ? "" : " (not spoken: speech off or unavailable)");
                }
                _paperLine = _office.PaperStatus(understood ? message : "Voice heard: " + Quote(shown));
            }
            RefreshMic();
        }

        /// <summary>rc22: set by the Wall habitat (optional): a Manager plan card is open, waiting for approval.</summary>
        public static Func<bool> ManagerPlanOpen;

        [Tooltip("rc22 polish: agents answer small talk (greetings, how are you, thanks, jokes, banter) with short scripted lines.")]
        public bool smallTalkEnabled = true;
        int _smallTalkTurn;
        /// <summary>For drivers: how many small-talk lines were answered, and the last one.</summary>
        public int SmallTalkCount { get; private set; }
        public string LastSmallTalk { get; private set; }

        bool TrySmallTalk(string heard, VoiceIntent intent, bool ambient, bool addressedAny, VoiceAgentTarget addrWho, string addrRest)
        {
            if (!smallTalkEnabled) return false;
            bool chatty = intent.Kind == VoiceIntentKind.None || intent.Kind == VoiceIntentKind.Status || intent.Kind == VoiceIntentKind.AskAgent
                          || intent.Kind == VoiceIntentKind.AskWhy || intent.Kind == VoiceIntentKind.WhoNeedsMe;
            if (!chatty) return false;
            string text = addressedAny ? addrRest : heard;
            SmallTalkKind kind = SmallTalk.Classify(text);
            if (kind == SmallTalkKind.None) return false;

            VoiceAgentTarget companion = VoiceAgentTarget.None;
            try { if (CompanionAgent != null) companion = CompanionAgent(); } catch { }
            if (ambient && !addressedAny && companion == VoiceAgentTarget.None) return false;   // hands-free room chatter stays private
            VoiceAgentTarget who = addressedAny ? addrWho : VoiceAgentTarget.None;
            if (who == VoiceAgentTarget.None) who = VoiceIntentClassifier.ResolveTarget(heard);   // "thanks, developer"
            if (who == VoiceAgentTarget.None) who = companion;
            if (who == VoiceAgentTarget.None) who = VoiceAgentTarget.Manager;

            string line = SmallTalk.Reply(kind, who, _smallTalkTurn++);
            if (string.IsNullOrEmpty(line)) return false;
            string id = VoiceIntentClassifier.AgentId(who) ?? "manager";
            SmallTalkCount++;
            LastSmallTalk = id + ": " + line;
            LastIntent = "SmallTalk";
            LastMessage = line;
            _ambient = _ambientQuiet = false;
            _actedOnPartial = VoiceIntentKind.None;
            _partial = "";
            if (policy != null) policy.NotifyHeard(LastMessage);
            Result(VoiceAnswers.RoleName(id), line, VoiceTone.Agent, 4f, "Small talk (scripted). Nothing changed.");
            bool spoke = false;
            try { spoke = AgentSpeech.Say(id, line, _office.SpeakerPosition(id)); } catch (Exception e) { Debug.LogWarning("[MuseDesk] voice: small talk not spoken (" + e.GetType().Name + ")."); }
            Route("SmallTalk:" + kind + ":" + who, "\"" + line + "\"" + (spoke ? "" : " (not spoken: speech off or unavailable)"));
            return true;
        }

        static bool SafeCall(Func<bool> f) { try { return f != null && f(); } catch { return false; } }

        FallbackFacts BuildFallbackFacts(string heard, VoiceAgentTarget addressedTo, bool comeNoName)
        {
            VoiceContext ctx = CurrentContext();
            VoiceHelpState h = _office.HelpState(ctx);
            VoiceAgentTarget named = addressedTo != VoiceAgentTarget.None ? addressedTo : VoiceIntentClassifier.ResolveTarget(heard);
            VoiceAgentTarget companion = VoiceAgentTarget.None;
            try { if (CompanionAgent != null) companion = CompanionAgent(); } catch { }
            bool managerCan = ManagerRequestHandler != null && (ManagerReady == null || SafeCall(ManagerReady));
            return new FallbackFacts
            {
                Speaker = named != VoiceAgentTarget.None ? named : (companion != VoiceAgentTarget.None ? companion : VoiceAgentTarget.Manager),
                Companion = companion,
                Mode = h.Mode,
                Live = h.Live,
                Moving = (ctx & VoiceContext.Moving) != 0,
                Previewing = (ctx & VoiceContext.Previewing) != 0,
                DecisionWaiting = (ctx & VoiceContext.DecisionPending) != 0,
                PaperOpen = _office.PaperOpen,
                ManagerPlanOpen = SafeCall(ManagerPlanOpen),
                ManagerCanPlan = managerCan,
                PortalAvailable = PortalRequestHandler != null,
                BoardAvailable = _office.Dock != null && _office.Dock.board != null,
                FloorEnabled = h.FloorEnabled,
                CeilingEnabled = h.CeilingEnabled,
                HasResult = h.HasResult,
                AskedToComeWithoutName = comeNoName,
            };
        }

        /// <summary>For the log: why a sentence that reached the grammar was not understood.</summary>
        string WhyNotUnderstood(bool ambient, bool addressed, bool unnamed)
        {
            if (_pending != Pending.None) return "an open question is waiting for confirm / cancel";
            if (ambient && !addressed) return "hands-free, not addressed";
            if (!unnamed) return "names an agent but is not a command or question the grammar knows";
            if (ManagerRequestHandler == null) return "no grammar match; the Manager is not registered (Wall Manager off)";
            if (ManagerReady != null && !ManagerReady()) return "no grammar match; the Manager is not available here";
            return "no grammar match; too short or only small talk for the Manager";
        }

        VoiceContext CurrentContext()
        {
            return _office.Context(_pending == Pending.Task, _pending == Pending.Decision);
        }

        // ───────────────────────── carrying an intent out ─────────────────────────

        string Execute(VoiceIntent intent, out VoiceTone tone, out float seconds, out bool understood)
        {
            tone = VoiceTone.Good; seconds = understoodSeconds; understood = true;
            var o = _office;
            var director = o.Director;
            switch (intent.Kind)
            {
                case VoiceIntentKind.Stop:
                    tone = VoiceTone.Warn;
                    return DoStop();

                case VoiceIntentKind.Cancel:
                    tone = VoiceTone.Warn;
                    return DoCancel();

                case VoiceIntentKind.Confirm:
                    return DoConfirm(ref tone);

                case VoiceIntentKind.Approve: return BeginDecision(DecisionActions.Approve, ref tone);
                case VoiceIntentKind.Reject: return BeginDecision(DecisionActions.Reject, ref tone);
                case VoiceIntentKind.RequestChanges: return BeginDecision(DecisionActions.RequestChanges, ref tone);

                case VoiceIntentKind.DecisionNotPending:
                    tone = VoiceTone.Warn;
                    return "Nothing is waiting for a decision.";

                case VoiceIntentKind.AskWhy:
                    if (o.DecisionState() == VoiceDecisionState.None)
                    {
                        if (!string.IsNullOrEmpty(intent.Role)) return AnswerAgent(intent.Role, ref tone, ref seconds);
                        tone = VoiceTone.Warn;
                        return "No decision is waiting, so there is nothing to explain.";
                    }
                    if (!o.AskWhy(intent.Raw, intent.Role, null)) { tone = VoiceTone.Warn; return "The question could not be asked."; }
                    return o.PaperOpen ? "Asked. The answer appears on the paper." : "Asked. Take the paper to read the answer.";

                case VoiceIntentKind.ShowDetails:
                case VoiceIntentKind.HideDetails:
                    {
                        bool show = intent.Kind == VoiceIntentKind.ShowDetails;
                        if (!o.PaperOpen) { tone = VoiceTone.Warn; return o.DecisionState() == VoiceDecisionState.NotTaken ? "Take the paper first: say \"show me the paper\"." : "No paper is open."; }
                        if (o.DetailsShown == show) return show ? "The details are already open." : "The details are already closed.";
                        o.SetDetails(show);
                        return show ? "Details opened." : "Details closed.";
                    }

                case VoiceIntentKind.TakePaper:
                    if (o.PaperOpen) return "The paper is already open.";
                    if (o.TakePaper()) return "Paper opened.";
                    tone = VoiceTone.Warn;
                    return o.DecisionState() == VoiceDecisionState.None ? "No paper is waiting." : "The paper is not here yet.";

                case VoiceIntentKind.Start:
                    {
                        VoiceRunFacts run = o.Facts();
                        if (run.RunInProgress || run.DecisionPending) { tone = VoiceTone.Warn; return "A run is already in progress."; }
                        if (o.Dock != null && o.Dock.StartPending) return "Start is already being sent.";
                        if (o.Dock == null && o.Session == null) { tone = VoiceTone.Warn; return "The session is not ready yet."; }
                        o.Start();
                        return "Start requested. The task dock shows the answer.";
                    }

                case VoiceIntentKind.RunAgain:
                    if (o.IsLive) { tone = VoiceTone.Warn; return "Run again is for the scripted demo. In live mode, start a card from the board."; }
                    if (o.Receipt == null || !o.Receipt.IsShown) { tone = VoiceTone.Warn; return "Nothing has finished yet, so there is nothing to run again."; }
                    o.Receipt.RunAgain();
                    return "Demo reset. Say start to run it again.";

                case VoiceIntentKind.OpenBoard:
                case VoiceIntentKind.CloseBoard:
                    {
                        bool open = intent.Kind == VoiceIntentKind.OpenBoard;
                        if (o.Dock == null || o.Dock.board == null) { tone = VoiceTone.Warn; return "The task board is not available."; }
                        if (o.BoardOpen == open) return open ? "The board is already open." : "The board is already closed.";
                        if (!o.ToggleBoard()) { tone = VoiceTone.Warn; return "Put the card down first."; }
                        return open ? "Board opened." : "Board closed.";
                    }

                case VoiceIntentKind.ViewResult:
                    if (o.ViewerOpen) return "The result is already open.";
                    if (o.ViewResult()) return "Opening the result.";
                    tone = VoiceTone.Warn;
                    return "There is no result to show yet.";

                case VoiceIntentKind.Close:
                    if (o.ViewerOpen) { o.Viewer.Close(); return "Result closed."; }
                    if (o.BoardOpen) { if (o.ToggleBoard()) return "Board closed."; tone = VoiceTone.Warn; return "Put the card down first."; }
                    if (o.DetailsShown && o.PaperOpen) { o.SetDetails(false); return "Details closed."; }
                    tone = VoiceTone.Warn;
                    return "Nothing is open.";

                case VoiceIntentKind.Next:
                    if (o.Previewing) { director.NextCandidate(); return Words(director.StatusText, "Next spot."); }
                    if (o.ViewerOpen) { o.Viewer.NextPage(); return "Next page."; }
                    tone = VoiceTone.Warn;
                    return "There is nothing to page through.";

                case VoiceIntentKind.Previous:
                    if (o.ViewerOpen) { o.Viewer.PrevPage(); return "Previous page."; }
                    tone = VoiceTone.Warn;
                    return "There is nothing to page through.";

                case VoiceIntentKind.GoToPage:
                    {
                        if (!o.ViewerOpen) { tone = VoiceTone.Warn; return "No result is open."; }
                        int delta = intent.Number - 1 - o.Viewer.PageIndex;
                        if (delta == 0) return "That page is already showing.";
                        if (delta == 1) { o.Viewer.NextPage(); return "Next page."; }
                        if (delta == -1) { o.Viewer.PrevPage(); return "Previous page."; }
                        tone = VoiceTone.Warn;
                        return "One page at a time: say next or previous.";
                    }

                case VoiceIntentKind.FocusOn:
                case VoiceIntentKind.FocusOff:
                    {
                        bool on = intent.Kind == VoiceIntentKind.FocusOn;
                        if (o.Focus == null) { tone = VoiceTone.Warn; return "Focus mode is not available."; }
                        if (FocusModeController.Active == on) return on ? "Focus is already on." : "Focus is already off.";
                        o.Focus.SetFocus(on);
                        return on ? "Focus on. Routine updates are held back." : "Focus off.";
                    }

                case VoiceIntentKind.ModeDesk:
                case VoiceIntentKind.ModeFloor:
                case VoiceIntentKind.ModeCeiling:
                case VoiceIntentKind.ModeWall:
                    return DoMode(intent.Kind, ref tone);

                case VoiceIntentKind.NextSpot:
                    if (!o.Previewing) { tone = VoiceTone.Warn; return "Say floor mode or ceiling mode first."; }
                    director.NextCandidate();
                    return Words(director.StatusText, "Next spot.");

                case VoiceIntentKind.SpotNumber:
                    {
                        if (!o.Previewing || director.Core == null) { tone = VoiceTone.Warn; return "Say floor mode or ceiling mode first."; }
                        int count = director.Core.CandidateCount;
                        if (intent.Number < 1 || intent.Number > count) { tone = VoiceTone.Warn; return count > 0 ? "There are " + count + " spots." : "There are no other spots."; }
                        for (int i = 0; i < count && director.Core.CandidateIndex != intent.Number - 1; i++) director.NextCandidate();
                        return Words(director.StatusText, "Spot " + intent.Number + ".");
                    }

                case VoiceIntentKind.LowerPod:
                case VoiceIntentKind.RaisePod:
                    {
                        bool down = intent.Kind == VoiceIntentKind.LowerPod;
                        if (director == null || SpatialWorkspaceState.Mode != WorkspaceMode.Ceiling) { tone = VoiceTone.Warn; return "The pod is used in ceiling mode only."; }
                        if (o.Moving) { tone = VoiceTone.Warn; return "The office is moving. Say stop to halt it."; }
                        if (down == !SpatialWorkspaceState.IsRemote) return down ? "The pod is already down." : "The pod is already up.";
                        if (down) director.BringPodDown(); else director.SendPodUp();
                        return Words(director.StatusText, down ? "Lowering the pod." : "Raising the pod.");
                    }

                case VoiceIntentKind.BringHere:
                case VoiceIntentKind.SendBack:
                    {
                        bool bring = intent.Kind == VoiceIntentKind.BringHere;
                        if (director == null) { tone = VoiceTone.Warn; return "Floor and ceiling modes are not available."; }
                        if (o.Moving) { tone = VoiceTone.Warn; return "The office is moving. Say stop to halt it."; }
                        if (bring)
                        {
                            if (!SpatialWorkspaceState.IsRemote) return "The office is already within reach.";
                            SummonRequests.RaiseBringOfficeHere();
                            return Words(director.StatusText, "Bringing the office to you.");
                        }
                        if (SpatialWorkspaceState.Mode == WorkspaceMode.Desk && !director.HasParkedFloorSpot)
                        { tone = VoiceTone.Warn; return "The office lives on the desk. Say floor mode or ceiling mode to move it."; }
                        if (SpatialWorkspaceState.Mode != WorkspaceMode.Desk && SpatialWorkspaceState.IsRemote) return "The office is already away.";
                        SummonRequests.RaiseSendOfficeBack();
                        return Words(director.StatusText, "Sending the office back.");
                    }

                case VoiceIntentKind.Status:
                    {
                        tone = VoiceTone.Agent; seconds = answerSeconds;
                        VoiceRunFacts run = o.Facts();
                        if (SpatialWorkspaceState.IsRemote) SummonRequests.RaiseShowLatestUpdate();
                        return VoiceAnswers.Status(run);
                    }

                case VoiceIntentKind.WhoNeedsMe:
                    {
                        tone = VoiceTone.Agent; seconds = answerSeconds;
                        VoiceRunFacts run = o.Facts();
                        string line = VoiceAnswers.WhoNeedsMe(run);
                        if (run.DecisionPending)
                            o.AgentSpeak(string.IsNullOrEmpty(run.DecisionAgentId) ? AgentIds.Researcher : run.DecisionAgentId, "I need your decision.", answerSeconds);
                        return line;
                    }

                case VoiceIntentKind.AskAgent:
                    return AnswerAgent(intent.Role, ref tone, ref seconds);

                case VoiceIntentKind.ResetLayout:
                    if (o.ResetLayout()) return "Layout reset.";
                    tone = VoiceTone.Warn;
                    return "Reset layout is not available in this build.";

                case VoiceIntentKind.Help:
                    {
                        _display = Display.Info;
                        _displayUntil = Time.unscaledTime + helpSeconds;
                        if (hud != null) hud.Show("You can say", ModeWord(), VoiceAnswers.HelpCard(o.HelpState(CurrentContext()), 8), "", VoiceTone.Neutral);
                        return null;        // the card is the answer
                    }

                case VoiceIntentKind.CreateTask:
                    return BeginTask(intent, ref tone);

                default:
                    understood = false; tone = VoiceTone.Warn; seconds = notUnderstoodSeconds;
                    return "";
            }
        }

        static string Words(string status, string fallback)
        {
            return string.IsNullOrEmpty(status) ? fallback : VoiceAnswers.Ascii(status);
        }

        string AnswerAgent(string agentId, ref VoiceTone tone, ref float seconds)
        {
            if (string.IsNullOrEmpty(agentId)) agentId = AgentIds.Researcher;
            tone = VoiceTone.Agent; seconds = answerSeconds;
            string line = _office.AgentLine(agentId);
            _office.AgentSpeak(agentId, line, answerSeconds);
            // The readout repeats the bubble and carries the honesty label.
            return VoiceAnswers.RoleName(agentId) + ": " + VoiceAnswers.Labelled(line, _office.IsLive);
        }

        // ── stop / cancel / confirm ──

        string DoStop()
        {
            bool hadPending = _pending != Pending.None;
            ClearPending();
            if (_office.Moving && _office.Director != null)
            {
                _office.Director.StopMove();
                return Words(_office.Director.StatusText, "Stopped.");
            }
            if (_office.Previewing) { _office.Director.Cancel(); return "Preview closed."; }
            return hadPending ? "Cancelled. Nothing was sent." : "Nothing is moving.";
        }

        string DoCancel()
        {
            if (_pending != Pending.None) { ClearPending(); return "Cancelled. Nothing was sent."; }
            if (_office.Previewing) { _office.Director.Cancel(); return "Preview closed."; }
            if (_office.Moving && _office.Director != null) { _office.Director.StopMove(); return Words(_office.Director.StatusText, "Stopped."); }
            return "Nothing to cancel.";
        }

        string DoConfirm(ref VoiceTone tone)
        {
            if (_pending == Pending.Task) { string title = _pendingTitle; ClearPending(); CreateTask(title); return "Sending the task to the server."; }
            if (_pending == Pending.Decision) { string sent = CommitDecision(); tone = _commitTone; return sent; }
            if (_office.Previewing)
            {
                bool ok = _office.Director.Confirm();
                if (!ok) tone = VoiceTone.Warn;
                return Words(_office.Director.StatusText, ok ? "Confirmed. The office is moving." : "That spot cannot be used.");
            }
            tone = VoiceTone.Warn;
            return _office.DecisionState() != VoiceDecisionState.None ? "For the decision, say approve or reject." : "Nothing to confirm.";
        }

        // ── modes ──

        string DoMode(VoiceIntentKind kind, ref VoiceTone tone)
        {
            var director = _office.Director;
            if (director == null) { tone = VoiceTone.Warn; return "Floor and ceiling modes are not available."; }
            if (_office.Moving) { tone = VoiceTone.Warn; return "The office is moving. Say stop to halt it."; }
            if (kind == VoiceIntentKind.ModeDesk)
            {
                if (SpatialWorkspaceState.Mode == WorkspaceMode.Desk && !director.IsPreviewing) return "The office is already on the desk.";
                director.RequestDesk();
                return Words(director.StatusText, "Returning the office to the desk.");
            }
            if (kind == VoiceIntentKind.ModeWall)
            {
                // Opens the wall preview only (nothing moves until Place on the dock). Wall is offered from an idle Desk.
                if (director.RequestMode(WorkspaceMode.Wall)) return "Wall preview. Use the dock to place it, or Cancel.";
                tone = VoiceTone.Warn;
                return SpatialWorkspaceState.Mode == WorkspaceMode.Desk && !director.IsPreviewing
                    ? "Wall mode is not available in this build."
                    : "Say desk mode first, then wall mode.";
            }
            bool floor = kind == VoiceIntentKind.ModeFloor;
            bool enabled = director.flags != null && (floor ? director.flags.floorModeEnabled : director.flags.ceilingModeEnabled);
            if (!enabled) { tone = VoiceTone.Warn; return (floor ? "Floor" : "Ceiling") + " mode is switched off in this build."; }
            // Same guided step as the console keys: a card asks the user to look at the surface first.
            var guide = PlacementGuide.Instance;
            if (guide != null && guide.TryBegin(floor ? WorkspaceMode.Floor : WorkspaceMode.Ceiling))
                return floor ? "Look down at the floor where you'd like the office." : "Look up at the ceiling.";
            if (floor) director.RequestFloor(); else director.RequestCeiling();
            if (director.IsPreviewing) return (floor ? "Floor" : "Ceiling") + " preview. Say next spot, confirm or cancel.";
            if (_office.Moving) return Words(director.StatusText, "The office is moving.");
            tone = VoiceTone.Warn;
            return Words(director.StatusText, (floor ? "Floor" : "Ceiling") + " mode could not start here.");
        }

        // ── decisions ──

        string BeginDecision(string action, ref VoiceTone tone)
        {
            tone = VoiceTone.Warn;
            switch (_office.DecisionState())
            {
                case VoiceDecisionState.None: return "Nothing is waiting for a decision.";
                case VoiceDecisionState.NotTaken: return "Take the paper first: say \"show me the paper\".";
                case VoiceDecisionState.Opening: return "The paper is still opening. Say it again in a moment.";
                case VoiceDecisionState.Busy: return "A decision is already being sent.";
            }
            if (_pending == Pending.Decision)
            {
                if (_pendingAction == action) return "";            // said twice: the clock keeps running
                ClearPending();
                return "Cancelled: two different answers. Nothing was sent.";
            }
            ClearPending();
            _pending = Pending.Decision;
            _pendingAction = action;
            _pendingDecisionId = _office.PendingDecisionId;
            _pendingDeadline = Time.unscaledTime + undoSeconds;
            return "";
        }

        string CommitDecision()
        {
            string action = _pendingAction, id = _pendingDecisionId;
            ClearPending();
            Abort();
            _relistenAt = -1f;
            string message; VoiceTone tone;
            if (_office.DecisionState() != VoiceDecisionState.Ready || _office.PendingDecisionId != id)
            { message = "Not sent: the decision changed."; tone = VoiceTone.Warn; }
            else if (_office.Respond(action))
            { message = ActionWord(action) + " sent. The paper shows the result once it is confirmed."; tone = VoiceTone.Good; }
            else { message = "Not sent. Use the paper's keys."; tone = VoiceTone.Bad; }
            LastMessage = message;
            _commitTone = tone;
            Result(ActionWord(action), message, tone, understoodSeconds);
            return message;
        }

        VoiceTone _commitTone;
        bool _aborting;

        /// <summary>Close the microphone and drop whatever was being said (no "thinking" follows).</summary>
        void Abort()
        {
            if (_source != null && (_listening || _thinking))
            {
                _aborting = true;
                try { _source.AbortRequest(); } finally { _aborting = false; }
            }
            _listening = false;
            _thinking = false;
            _office.AgentsListen(false);
        }

        static string ActionWord(string action)
        {
            return action == DecisionActions.Approve ? "Approve" : action == DecisionActions.Reject ? "Reject" : "Request changes";
        }

        static string ActionProgressive(string action)
        {
            return action == DecisionActions.Approve ? "Approving" : action == DecisionActions.Reject ? "Rejecting" : "Requesting changes";
        }

        // ── new task ──

        string BeginTask(VoiceIntent intent, ref VoiceTone tone)
        {
            tone = VoiceTone.Warn;
            if (!_office.IsLive) return "New tasks need the live server. This is the scripted demo.";
            if (_office.Board == null || _office.Board.boardPresenter == null || !_office.Board.Ready) return "The task board is not ready yet.";
            if (string.IsNullOrEmpty(intent.Text)) return "Say \"new task\" and then what it should do.";
            ClearPending();
            _pending = Pending.Task;
            _pendingTitle = intent.Text;
            _pendingDeadline = Time.unscaledTime + confirmSeconds;
            return "";
        }

        async void CreateTask(string title)
        {
            var board = _office.Board;
            var presenter = board != null ? board.boardPresenter : null;
            if (presenter == null) { Result("New task", "The task board is not available.", VoiceTone.Bad, understoodSeconds); return; }
            try
            {
                var card = await presenter.RequestCreateTaskAsync(title, null, 1, presenter.StableCreateKey(title, "voice-create"));
                if (this == null) return;
                if (card != null) Result("New task", "Added to the board: " + VoiceAnswers.Clip(string.IsNullOrEmpty(card.Title) ? title : card.Title, 60), VoiceTone.Good, answerSeconds);
                else Result("New task", "The server did not take the task. Nothing was added.", VoiceTone.Bad, answerSeconds);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[MuseDesk] voice: create task failed (" + e.GetType().Name + ").");
                if (this != null) Result("New task", "The server did not take the task. Nothing was added.", VoiceTone.Bad, answerSeconds);
            }
        }

        void ClearPending()
        {
            _pending = Pending.None;
            _pendingAction = _pendingDecisionId = _pendingTitle = null;
            _relistenAt = -1f;
        }

        void ScheduleRelisten()
        {
            if (!relistenWhileAsking || _source == null || _source.Availability != VoiceProblem.None) return;
            _relistenAt = Time.unscaledTime + 0.25f;
        }

        // ───────────────────────── keys on the readout ─────────────────────────

        /// <summary>The readout's Confirm key.</summary>
        public void PressConfirm() { KeyIntent(VoiceIntentKind.Confirm); }

        /// <summary>The readout's Cancel key.</summary>
        public void PressCancel() { KeyIntent(VoiceIntentKind.Cancel); }

        void KeyIntent(VoiceIntentKind kind)
        {
            if (_pending == Pending.None) { EndDisplay(); return; }
            Abort();
            VoiceTone tone = VoiceTone.Good; float seconds; bool understood;
            string message = Execute(new VoiceIntent { Kind = kind, Raw = "" }, out tone, out seconds, out understood);
            LastIntent = kind + " (key)";
            LastMessage = message ?? "";
            if (_pending != Pending.None) ShowPrompt();
            else if (!string.IsNullOrEmpty(message) && _display == Display.Prompt)
                Result(kind == VoiceIntentKind.Cancel ? "Cancelled" : "Confirmed", message, tone, seconds);
            RefreshMic();
        }

        // ───────────────────────── readout ─────────────────────────

        string ModeWord() { return _office.IsLive ? "" : "SCRIPTED DEMO"; }      // rc20 (owner): no LIVE tag

        string TrySaying() { return VoiceAnswers.TrySaying(_office.HelpState(CurrentContext())); }

        static string Quote(string text) { return string.IsNullOrEmpty(text) ? "" : "\"" + text + "\""; }

        void Result(string title, string body, VoiceTone tone, float seconds, string foot = "")
        {
            _display = Display.Result;
            _displayUntil = Time.unscaledTime + seconds;
            if (hud != null) hud.Show(title, ModeWord(), body, foot, tone);
        }

        void ShowPrompt()
        {
            if (_pending == Pending.None) return;
            _display = Display.Prompt;
            if (hud == null) return;
            if (_pending == Pending.Decision)
                hud.Show(ActionProgressive(_pendingAction) + "...", ModeWord(), "Say cancel to stop.", "", VoiceTone.Warn, null, "Cancel");
            else
                // The two keys sit over the foot line, so the instruction goes in the body.
                hud.Show("New task", ModeWord(), Quote(VoiceAnswers.Clip(_pendingTitle, 70)) + "\nSay confirm or cancel.", "", VoiceTone.Warn, "Confirm", "Cancel");
        }

        void ShowProblem(VoiceProblem problem, string detail)
        {
            string body;
            switch (problem)
            {
                case VoiceProblem.NoRecognizer: body = "This build has no speech service. The keys do everything."; break;
                case VoiceProblem.NoToken: body = string.IsNullOrEmpty(detail) ? "This build has no speech token. The keys do everything." : detail; break;
                case VoiceProblem.NoInternet: body = "Voice needs internet. The keys still work."; break;
                case VoiceProblem.PermissionDenied: body = "Microphone permission was denied. Allow it in the headset's settings, then press Mic."; break;
                case VoiceProblem.Timeout: body = "The speech service did not answer. Try again."; break;
                default: body = string.IsNullOrEmpty(detail) ? "The speech service had a problem. Try again." : detail; break;
            }
            // Quietly, and in the log only once for each reason.
            if (_loggedProblems.Add((int)problem)) Debug.Log("[MuseDesk] voice unavailable: " + problem);
            LastMessage = body;
            Result("Voice unavailable", body, VoiceTone.Bad, 4.5f);
            RefreshMic();
        }

        void EndDisplay()
        {
            if (_pending != Pending.None) { ShowPrompt(); return; }
            _display = Display.None;
            if (hud != null) hud.Hide();
            if (_paperLine != null) { _office.ClearPaperStatus(_paperLine); _paperLine = null; }
        }

        void RefreshMic()
        {
            if (hud == null) return;
            VoiceMicVisual v;
            if (_listening) v = VoiceMicVisual.Listening;
            else if (_thinking) v = VoiceMicVisual.Thinking;
            else
            {
                VoiceProblem p = _source == null ? VoiceProblem.NoRecognizer : _source.Availability;
                v = p == VoiceProblem.None ? VoiceMicVisual.Idle : VoiceMicVisual.Off;
            }
            hud.SetMic(v);
        }

        // ───────────────────────── the once-per-run hint ─────────────────────────

        void OnRunChanged()
        {
            _hintShown = false;
            if (_pending == Pending.Decision) { ClearPending(); EndDisplay(); }
        }

        void MaybeHint()
        {
            if (!showDecisionHint || _hintShown || _display != Display.None || _listening || _thinking) return;
            if (_source == null || _source.Availability != VoiceProblem.None) return;
            if (_office.DecisionState() != VoiceDecisionState.Ready) return;
            _hintShown = true;
            _display = Display.Info;
            _displayUntil = Time.unscaledTime + 5f;
            if (hud != null) hud.Show("Voice", ModeWord(), "Mic on, then start with a name: \"Researcher, why?\" Or stamp the paper.", "", VoiceTone.Neutral);
        }

        // ───────────────────────── driver ─────────────────────────

        /// <summary>One line with everything voice currently shows and knows (the Play Mode driver).</summary>
        public string Describe()
        {
            _office.Find(true);
            VoiceProblem p = _source == null ? VoiceProblem.NoRecognizer : _source.Availability;
            var sb = new System.Text.StringBuilder(320);
            sb.Append("availability=").Append(p)
              .Append(" listening=").Append(_listening).Append(" thinking=").Append(_thinking)
              .Append(" display=").Append(_display)
              .Append(" pending=").Append(_pending);
            if (_pending == Pending.Decision) sb.Append('(').Append(_pendingAction).Append(", ").Append(Mathf.Max(0f, _pendingDeadline - Time.unscaledTime).ToString("F1")).Append(" s)");
            if (_pending == Pending.Task) sb.Append("('").Append(_pendingTitle).Append("')");
            sb.Append(" context=").Append(CurrentContext())
              .Append(" decision=").Append(_office.DecisionState())
              .Append(" mode=").Append(ModeWord())
              .Append(" | handled=").Append(HandledCount)
              .Append(" lastHeard='").Append(LastTranscript).Append("' lastIntent=").Append(LastIntent)
              .Append(" lastMessage='").Append(LastMessage).Append('\'');
            if (hud != null)
                sb.Append(" | hud: shown=").Append(hud.ReadoutShown).Append(" mic=").Append(hud.Mic)
                  .Append(" title='").Append(hud.Title).Append("' body='").Append(hud.Body.Replace("\n", " / "))
                  .Append("' foot='").Append(hud.Foot).Append("' confirmKey=").Append(hud.ConfirmKeyShown)
                  .Append(" cancelKey=").Append(hud.CancelKeyShown);
            else sb.Append(" | hud: none");
            return sb.ToString();
        }
    }
}
