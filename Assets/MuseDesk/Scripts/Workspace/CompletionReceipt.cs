using TMPro;
using UnityEngine;
using MuseDesk.Models;
using MuseDesk.Networking;

namespace MuseDesk.Workspace
{
    /// <summary>
    /// A small receipt card that stands on the desk when a run ends: what was delivered (or that
    /// the run failed), whether that was LIVE work or the SCRIPTED DEMO, and two keys: "View"
    /// (opens the artifact sheet on what was delivered; only when something was) and "Run again".
    ///
    /// Honesty rules it follows:
    ///   - It appears only after the session reducer applied the confirming event
    ///     (WorkDelivered: a task.completed that references an artifact; or TaskFailed).
    ///   - Every word about the work is taken from that event or from the artifact.created event
    ///     the completion references. Nothing is invented; when the backend sent no summary the
    ///     receipt says so.
    ///   - The mode is printed in words, from the event's own source_mode.
    ///   - "Run again" does exactly what the old Replay key did (SessionController.ResetDemo),
    ///     which is only valid in the scripted demo. Against a live backend there is no key, only
    ///     a pointer to the board.
    ///
    /// The receipt is hidden by scale, never deactivated, so its poke key registers at start-up
    /// (brief, gotcha 2). It clears on SessionReset and when a new run starts.
    /// </summary>
    public class CompletionReceipt : MonoBehaviour
    {
        public SessionController controller;
        [Tooltip("Optional: asked to show the 'Press Start' line again after Run again.")]
        public FirstUseGuide guide;

        [Header("Card")]
        [Tooltip("Scale of the receipt when shown (used when the builder saved it hidden).")]
        public Vector3 shownScale = Vector3.one;
        public float easeSeconds = 0.28f;
        [Tooltip("'Delivered' or 'Run failed'.")]
        public TMP_Text heading;
        [Tooltip("LIVE or SCRIPTED DEMO, in words.")]
        public TMP_Text modeText;
        [Tooltip("What was delivered, in the backend's words.")]
        public TMP_Text body;
        [Tooltip("Test counts and files, when the artifact reported them.")]
        public TMP_Text detail;
        [Tooltip("Shown where the key would be when Run again is not valid (live mode).")]
        public TMP_Text hint;
        [Tooltip("Colour strip that repeats the mode.")]
        public Renderer modeStrip;

        [Header("Run again")]
        [Tooltip("The poke key (a child of the receipt). Hidden by scale in live mode.")]
        public Transform runAgainKey;
        [Tooltip("Scale of the key when shown.")]
        public Vector3 runAgainKeyScale = new Vector3(0.04f, 0.04f, 0.04f);
        [Tooltip("Off (default): Run again resets the demo, exactly like the old Replay key, and the user " +
                 "presses Start. On: it also starts the next run at once.")]
        public bool startAfterReset = false;

        [Header("View")]
        [Tooltip("The artifact sheet the View key opens. Without it there is no View key.")]
        public MuseDesk.Phase2.Board.ArtifactViewer viewer;
        [Tooltip("The poke key (a child of the receipt). Hidden by scale when nothing was delivered.")]
        public Transform viewKey;
        [Tooltip("Scale of the View key when shown.")]
        public Vector3 viewKeyScale = new Vector3(0.028f, 0.028f, 0.028f);

        [Header("Settle")]
        [Tooltip("The card and its stand drop this far (metres, along the root's up) onto the desk as they appear.")]
        public float settleHeight = 0.012f;
        public float settleSeconds = 0.38f;

        const string HintLive = "New task on the board";

        readonly ScaleEase _ease = new ScaleEase();
        readonly ScaleEase _keyEase = new ScaleEase();
        readonly ScaleEase _viewEase = new ScaleEase();
        MaterialPropertyBlock _block;
        bool _bound;
        bool _shown;
        string _lastResolved;   // state of the most recent decision.resolved (approved / rejected / ...)
        bool _wasAnimating;
        bool _eventLive;
        Vector3 _restLocalPosition, _settleAxis;
        float _settle = 1f;     // 0..1 progress of the settle, 1 = at rest

        // The artifact the current run produced (from artifact.created), for the detail line.
        string _artifactId, _artifactSummary;
        int _testsPassed = -1, _testsFailed = -1, _filesChanged = -1;
        // The artifact the run's task.completed referenced: what the View key opens.
        string _deliveredArtifactId;

        /// <summary>True while the receipt is on show.</summary>
        public bool IsShown => _shown;
        /// <summary>True while the receipt offers the View key (something was delivered and the
        /// artifact sheet is wired).</summary>
        public bool CanView => _shown && viewer != null && !string.IsNullOrEmpty(_deliveredArtifactId);

        void Awake()
        {
            _block = new MaterialPropertyBlock();
            _restLocalPosition = transform.localPosition;
            // "Up" of the space the card stands in, expressed in the parent's space.
            _settleAxis = transform.parent != null ? transform.parent.InverseTransformDirection(Vector3.up) : Vector3.up;
            _ease.Init(transform, ScaleEase.ResolveFull(transform, shownScale), easeSeconds, false);
            if (runAgainKey != null)
                _keyEase.Init(runAgainKey, ScaleEase.ResolveFull(runAgainKey, runAgainKeyScale), 0.15f, true);
            if (viewKey != null)
                _viewEase.Init(viewKey, ScaleEase.ResolveFull(viewKey, viewKeyScale), 0.15f, false);
        }

        void Update()
        {
            if (!_bound && controller != null) Bind();
            float dt = Time.deltaTime;
            _ease.Tick(dt);
            _keyEase.Tick(dt);
            _viewEase.Tick(dt);
            bool settling = _settle < 1f;
            if (settling)
            {
                // Drops onto the desk, dips a little, comes to rest: a small settle, no bounce loop.
                _settle = Mathf.Min(1f, _settle + dt / Mathf.Max(0.05f, settleSeconds));
                float inv = 1f - _settle;
                float offset = settleHeight * inv * inv * Mathf.Cos(_settle * Mathf.PI * 1.5f);
                transform.localPosition = _restLocalPosition + _settleAxis * offset;
            }
            // When the pop has finished, let the keys' press visuals re-read their resting offsets at
            // the final scale (PokeVisualRestGuard.SyncToCurrentScale, as the decision paper does).
            bool animating = settling || _ease.Animating || _keyEase.Animating || _viewEase.Animating;
            if (_wasAnimating && !animating && _shown)
                BroadcastMessage("SyncToCurrentScale", SendMessageOptions.DontRequireReceiver);
            _wasAnimating = animating;
        }

        void Bind()
        {
            var r = controller.Reducer;
            r.ArtifactCreated += OnArtifactCreated;
            r.WorkDelivered += OnWorkDelivered;
            r.TaskFailed += OnTaskFailed;
            r.DecisionResolved += OnDecisionResolved;
            r.SessionReset += Clear;
            r.SnapshotLoaded += Clear;
            controller.RunStateChanged += OnRunStateChanged;
            controller.ModeChanged += OnModeChanged;
            _bound = true;
        }

        void OnDisable()
        {
            if (!_bound || controller == null) return;
            var r = controller.Reducer;
            r.ArtifactCreated -= OnArtifactCreated;
            r.WorkDelivered -= OnWorkDelivered;
            r.TaskFailed -= OnTaskFailed;
            r.DecisionResolved -= OnDecisionResolved;
            r.SessionReset -= Clear;
            r.SnapshotLoaded -= Clear;
            controller.RunStateChanged -= OnRunStateChanged;
            controller.ModeChanged -= OnModeChanged;
            _bound = false;
        }

        // ── reducer events ──

        void OnArtifactCreated(EventEnvelope e)
        {
            if (e == null) return;
            _artifactId = e.PayloadString("artifact_id", null);
            _artifactSummary = e.PayloadString("summary", null);
            _testsPassed = e.PayloadValue<int>("tests_passed", -1);
            _testsFailed = e.PayloadValue<int>("tests_failed", -1);
            var files = e.PayloadValue<System.Collections.Generic.List<string>>("files_changed", null);
            _filesChanged = files != null ? files.Count : -1;
        }

        void OnWorkDelivered(EventEnvelope e)
        {
            if (e == null) return;
            // Only describe the artifact this completion actually references.
            bool sameArtifact = !string.IsNullOrEmpty(_artifactId) && e.PayloadString("artifact_id", null) == _artifactId;

            string what = e.PayloadString("result_summary", null);
            if (string.IsNullOrEmpty(what) && sameArtifact) what = _artifactSummary;
            if (string.IsNullOrEmpty(what)) what = "Work was delivered. No summary was sent.";

            string more = "";
            if (sameArtifact)
            {
                if (_testsPassed >= 0)
                    more = "Tests: " + _testsPassed + " passed, " + Mathf.Max(0, _testsFailed) + " failed";
                if (_filesChanged > 0)
                    more += (more.Length > 0 ? ". " : "") + "Files changed: " + _filesChanged;
            }
            _deliveredArtifactId = e.PayloadString("artifact_id", null);
            Present("Delivered", what, more, e);
        }

        void OnTaskFailed(EventEnvelope e)
        {
            if (e == null) return;
            string why = e.PayloadString("result_summary", null);
            if (string.IsNullOrEmpty(why)) why = e.PayloadString("reason", null);
            if (string.IsNullOrEmpty(why)) why = e.PayloadString("error", null);
            if (string.IsNullOrEmpty(why)) why = "The run stopped. No reason was sent.";
            _deliveredArtifactId = null;
            Present("Run failed", why, "Nothing was delivered.", e);
        }

        void OnDecisionResolved(string decisionId, string state) { _lastResolved = state; }

        void OnRunStateChanged()
        {
            // A run that ended on the user's rejection delivers nothing and fails nothing, so no other
            // event would show a receipt; without one there is no way to run the demo again.
            if (!_shown && controller != null && !controller.RunInProgress && controller.HasStartedRun && _lastResolved == "rejected")
            {
                _lastResolved = null;
                _deliveredArtifactId = null;
                Present("Stopped", "You rejected the proposal.", "Nothing was built.", null);
                return;
            }
            // The receipt belongs to the run that just ended; a new run starting clears it.
            if (_shown && controller != null && controller.RunInProgress) Clear();
        }

        void OnModeChanged() { if (_shown) ApplyMode(); }

        // ── presentation ──

        void Present(string title, string what, string more, EventEnvelope e)
        {
            // The event says which mode produced it; fall back to the session's mode.
            string source = e != null ? e.PayloadString("source_mode", null) : null;
            _eventLive = string.IsNullOrEmpty(source) ? (controller != null && controller.IsLive)
                                                      : source == SourceModes.Live;
            if (heading != null) heading.text = title;
            if (body != null) body.text = what;
            if (detail != null) detail.text = more;
            ApplyMode();
            bool wasShown = _shown;
            _shown = true;
            if (wasShown) _ease.Pulse(0.04f, 0.25f);
            else
            {
                _ease.Show();
                _settle = 0f;       // eased scale-in, with the card settling onto the desk
            }
        }

        void ApplyMode()
        {
            if (modeText != null) modeText.text = _eventLive ? "" : "SCRIPTED DEMO";      // rc20 (owner): no LIVE tag
            if (modeStrip != null)
                OfficeTokens.Tint(modeStrip, _block, _eventLive ? OfficeTokens.Success : OfficeTokens.Amber);
            // Run again is valid only while the session itself is the scripted demo.
            bool canRunAgain = controller != null && !controller.IsLive;
            if (runAgainKey != null)
            {
                if (canRunAgain) _keyEase.Show(); else _keyEase.Hide();
            }
            if (hint != null) hint.text = canRunAgain ? "" : HintLive;
            // View is offered only for work that was delivered (never for a failed or stopped run).
            if (viewKey != null)
            {
                if (viewer != null && !string.IsNullOrEmpty(_deliveredArtifactId)) _viewEase.Show(); else _viewEase.Hide();
            }
        }

        /// <summary>Hide the receipt and forget the run it described (SessionReset, a new run).</summary>
        public void Clear()
        {
            _artifactId = null;
            _artifactSummary = null;
            _deliveredArtifactId = null;
            _testsPassed = _testsFailed = _filesChanged = -1;
            if (!_shown) return;
            _shown = false;
            _ease.Hide();
            if (_settle < 1f)
            {
                _settle = 1f;
                transform.localPosition = _restLocalPosition;
            }
        }

        /// <summary>
        /// Poke target of the "View" key: open the artifact sheet on what this run delivered (the
        /// artifact its task.completed event referenced). Ignored while the receipt is hidden and
        /// when nothing was delivered. The sheet shows the gateway's own content and mode words.
        /// </summary>
        public void View()
        {
            if (!_shown || viewer == null || string.IsNullOrEmpty(_deliveredArtifactId)) return;
            viewer.OpenDelivered(_deliveredArtifactId);
        }

        /// <summary>
        /// Poke target of the "Run again" key. Scripted demo only: resets the demo to its start
        /// (SessionController.ResetDemo, the same call the old Replay key made). Ignored while the
        /// receipt is hidden and in live mode, where the backend has no reset.
        /// </summary>
        public void RunAgain()
        {
            if (!_shown || controller == null || controller.IsLive) return;
            controller.ResetDemo();          // raises SessionReset, which clears this receipt
            if (startAfterReset) controller.StartDemo();
            else if (guide != null) guide.ShowStartHint();
        }
    }
}
