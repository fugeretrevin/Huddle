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
    /// The one approximation: SessionController does not expose whether its background attempt to
    /// reach the desktop is still running, so "Connecting..." is shown for the configured
    /// connect timeout after start-up (and dropped at once when a demo run starts, because from
    /// then on a late connection is discarded). The mode words themselves are never approximate.
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
        bool _connecting;
        float _connectUntil;
        bool _lastSocketConnected;
        Mode _mode = Mode.Unknown;
        string _status;

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

            if (!_seenInitialized && controller.Initialized)
            {
                _seenInitialized = true;
                // A live connection is attempted in the background unless the settings prefer the
                // offline demo. Until that attempt can have timed out, say so.
                bool wantsLive = controller.settings != null && !controller.settings.preferOfflineDemo
                                 && controller.dispatcher != null && !controller.IsLive;
                if (wantsLive)
                {
                    _connecting = true;
                    _connectUntil = Time.unscaledTime + controller.settings.connectTimeoutSeconds + 0.5f;
                }
                Refresh();
            }
            if (_connecting && Time.unscaledTime >= _connectUntil)
            {
                _connecting = false;
                Refresh();
            }
            // The socket flips its flag on a background thread; notice it here (a bool read).
            if (controller.IsLive && controller.dispatcher != null)
            {
                bool connected = controller.dispatcher.Connected;
                if (connected != _lastSocketConnected) Refresh();
            }
        }

        void Bind()
        {
            controller.ModeChanged += Refresh;
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
                _connecting = false;
                _lastSocketConnected = controller.dispatcher == null || controller.dispatcher.Connected;
                status = _lastSocketConnected ? "Connected" : "Reconnecting...";
            }
            else
            {
                mode = Mode.Demo;
                // Once a demo run has started, a late desktop connection is not adopted.
                if (_connecting && (controller.HasStartedRun || controller.RunInProgress)) _connecting = false;
                status = _connecting ? "Connecting..." : "Offline: demo";
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
