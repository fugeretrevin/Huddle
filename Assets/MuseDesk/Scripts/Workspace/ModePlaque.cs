using System;
using TMPro;
using UnityEngine;
using MuseDesk.DebugTools;
using MuseDesk.Networking;

namespace MuseDesk.Workspace
{
    /// <summary>
    /// The small desk plaque that says, at all times and in words, whether the office is running a
    /// SCRIPTED DEMO or LIVE work, plus a short connection word. It replaces the large floating
    /// diagnostics sign. This is an honesty requirement (docs/AGENTS.md invariant 1): the words are
    /// taken from the session itself (SessionController.IsLive / Initialized, the event socket's
    /// connection flag and the mode label SessionController reports to DiagnosticsOverlay), never
    /// from a timer script, and the colour strip only repeats what the words say.
    ///
    /// The connection word, in the scripted demo:
    ///   "Connecting..."   only while SessionController.LiveAttemptInProgress is true, i.e. the
    ///                     background attempt to reach the desktop is really running (and dropped at
    ///                     once when a demo run starts, because from then on a late connection is
    ///                     discarded). It can never stay: the attempt has its own timeout, and as a
    ///                     backstop the word is withdrawn a few seconds after that timeout.
    ///   "Offline: demo"   no attempt was made, or it ended without a connection.
    /// In live mode, from the event socket's real state:
    ///   "Connected"       for a few seconds after the socket connects, then the line is left empty;
    ///   "Reconnecting..." while the socket is down and retrying; "Disconnected" once it has been
    ///                     down for a while (it keeps retrying; nothing is simulated meanwhile).
    /// Before a session exists the plaque reads STARTING (or NO SESSION if start-up failed).
    /// </summary>
    public class ModePlaque : MonoBehaviour
    {
        public SessionController controller;
        [Tooltip("Where SessionController reports the mode label. Falls back to controller.diagnostics.")]
        public DiagnosticsOverlay diagnostics;

        [Header("Plaque")]
        [Tooltip("Mode in words: SCRIPTED DEMO / LIVE.")]
        public TMP_Text modeText;
        [Tooltip("Short connection word: Connecting... / Offline: demo / Connected / Reconnecting...")]
        public TMP_Text statusText;
        [Tooltip("Colour strip that repeats the mode (amber demo, green live, coral no session).")]
        public Renderer strip;

        enum Mode { Unknown, Starting, Demo, Live, NoSession }

        const string WordsDemo = "SCRIPTED DEMO";
        const string WordsLive = "LIVE";
        const string WordsStarting = "STARTING";
        const string WordsNoSession = "NO SESSION";

        readonly ScaleEase _ease = new ScaleEase();
        MaterialPropertyBlock _block;
        DiagnosticsOverlay _boundDiagnostics;
        bool _bound;
        bool _seenInitialized;
        bool _attemptSeen;            // LiveAttemptInProgress as last read
        float _attemptStarted;        // unscaled time the attempt was first seen running
        bool _attemptGaveUp;          // backstop: the attempt outlived its own timeout by a margin
        bool _lastSocketConnected;
        bool _socketKnown;
        float _socketChangedAt = -100f;   // unscaled time of the last socket flip (or of going live)
        Mode _mode = Mode.Unknown;
        string _status;

        [Header("Connection word")]
        [Tooltip("Seconds \"Connected\" stays on the plaque after the socket connects.")]
        public float connectedSeconds = 4f;
        [Tooltip("Seconds the socket must be down before \"Reconnecting...\" becomes \"Disconnected\".")]
        public float disconnectedAfterSeconds = 20f;

        const string WordConnecting = "Connecting...";
        const string WordOffline = "Offline: demo";
        const string WordConnected = "Connected";
        const string WordReconnecting = "Reconnecting...";
        const string WordDisconnected = "Disconnected";
        const float AttemptBackstopSeconds = 5f;

        void Awake()
        {
            _block = new MaterialPropertyBlock();
            _ease.Init(transform, ScaleEase.ResolveFull(transform, Vector3.one), 0.2f, true);
        }

        void OnEnable() { Refresh(); }

        void Update()
        {
            if (!_bound && controller != null) Bind();
            _ease.Tick(Time.deltaTime);
            if (!_bound) return;

            float now = Time.unscaledTime;

            if (!_seenInitialized && controller.Initialized)
            {
                _seenInitialized = true;
                Refresh();
            }

            // The real background attempt (also polled, in case this plaque bound after it began).
            bool attempt = controller.LiveAttemptInProgress;
            if (attempt != _attemptSeen)
            {
                _attemptSeen = attempt;
                if (attempt) { _attemptStarted = now; _attemptGaveUp = false; }
                Refresh();
            }
            // Backstop so the word can never stay: the attempt cancels itself at the connect
            // timeout; if it is somehow still flagged well after that, stop saying "Connecting...".
            if (attempt && !_attemptGaveUp)
            {
                float limit = (controller.settings != null ? controller.settings.connectTimeoutSeconds : 6f)
                              + AttemptBackstopSeconds;
                if (now - _attemptStarted >= limit) { _attemptGaveUp = true; Refresh(); }
            }

            // The socket flips its flag on a background thread; notice it here (a bool read).
            if (controller.IsLive)
            {
                bool connected = controller.dispatcher == null || controller.dispatcher.Connected;
                if (!_socketKnown || connected != _lastSocketConnected)
                {
                    _socketKnown = true;
                    _lastSocketConnected = connected;
                    _socketChangedAt = now;
                    Refresh();
                }
                else if (_nextTimedRefresh > 0f && now >= _nextTimedRefresh)
                {
                    // "Connected" has been shown long enough, or the socket has been down long
                    // enough to say "Disconnected": one refresh at that moment, not every frame.
                    _nextTimedRefresh = 0f;
                    Refresh();
                }
            }
            else if (_socketKnown)
            {
                _socketKnown = false;
                _nextTimedRefresh = 0f;
            }
        }

        float _nextTimedRefresh;

        void OnLiveAttemptChanged()
        {
            bool attempt = controller != null && controller.LiveAttemptInProgress;
            if (attempt != _attemptSeen)
            {
                _attemptSeen = attempt;
                if (attempt) { _attemptStarted = Time.unscaledTime; _attemptGaveUp = false; }
            }
            Refresh();
        }

        void Bind()
        {
            controller.ModeChanged += Refresh;
            controller.LiveAttemptChanged += OnLiveAttemptChanged;
            controller.RunStateChanged += Refresh;
            controller.Reducer.SnapshotLoaded += Refresh;
            controller.Reducer.SessionReset += Refresh;
            _boundDiagnostics = diagnostics != null ? diagnostics : controller.diagnostics;
            if (_boundDiagnostics != null) _boundDiagnostics.ModeLabelChanged += Refresh;
            _bound = true;
            Refresh();
        }

        void OnDisable()
        {
            if (!_bound) return;
            if (controller != null)
            {
                controller.ModeChanged -= Refresh;
                controller.LiveAttemptChanged -= OnLiveAttemptChanged;
                controller.RunStateChanged -= Refresh;
                controller.Reducer.SnapshotLoaded -= Refresh;
                controller.Reducer.SessionReset -= Refresh;
            }
            if (_boundDiagnostics != null) _boundDiagnostics.ModeLabelChanged -= Refresh;
            _boundDiagnostics = null;
            _bound = false;
        }

        void Refresh()
        {
            Mode mode;
            string status;

            if (controller == null || !controller.Initialized)
            {
                // No session yet. SessionController reports a failed start through the mode label.
                DiagnosticsOverlay d = _boundDiagnostics != null ? _boundDiagnostics : diagnostics;
                string label = d != null ? d.modeLabel : null;
                bool failed = label != null && label.StartsWith("ERROR", StringComparison.Ordinal);
                mode = failed ? Mode.NoSession : Mode.Starting;
                status = failed ? "Could not start" : "";
            }
            else if (controller.IsLive)
            {
                mode = Mode.Live;
                float now = Time.unscaledTime;
                bool connected = controller.dispatcher == null || controller.dispatcher.Connected;
                if (!_socketKnown || connected != _lastSocketConnected)
                {
                    // First look at the socket in live mode, or it flipped since Update last looked.
                    _socketKnown = true;
                    _lastSocketConnected = connected;
                    _socketChangedAt = now;
                }
                float since = now - _socketChangedAt;
                if (connected)
                {
                    bool fresh = since < connectedSeconds;
                    status = fresh ? WordConnected : "";
                    _nextTimedRefresh = fresh ? _socketChangedAt + connectedSeconds : 0f;
                }
                else
                {
                    bool recent = since < disconnectedAfterSeconds;
                    status = recent ? WordReconnecting : WordDisconnected;
                    _nextTimedRefresh = recent ? _socketChangedAt + disconnectedAfterSeconds : 0f;
                }
            }
            else
            {
                mode = Mode.Demo;
                // "Connecting..." only while the real attempt is running. Once a demo run has
                // started, a late desktop connection is not adopted, so the word is dropped.
                bool connecting = controller.LiveAttemptInProgress && !_attemptGaveUp
                                  && !controller.HasStartedRun && !controller.RunInProgress;
                status = connecting ? WordConnecting : WordOffline;
            }

            bool changed = false;
            if (mode != _mode)
            {
                _mode = mode;
                changed = true;
                if (modeText != null)
                    modeText.text = mode == Mode.Live ? WordsLive
                                  : mode == Mode.Demo ? WordsDemo
                                  : mode == Mode.NoSession ? WordsNoSession
                                  : WordsStarting;
                if (strip != null)
                    OfficeTokens.Tint(strip, _block,
                        mode == Mode.Live ? OfficeTokens.Success
                        : mode == Mode.NoSession ? OfficeTokens.Coral
                        : OfficeTokens.Amber);
            }
            if (!string.Equals(status, _status, StringComparison.Ordinal))
            {
                _status = status;
                changed = true;
                if (statusText != null) statusText.text = status;
            }
            if (changed && Application.isPlaying) _ease.Pulse(0.04f, 0.3f);
        }
    }
}
