using UnityEngine;
using MuseDesk.Art;
using MuseDesk.Feedback;
using MuseDesk.Workspace;

namespace MuseDesk.Spatial
{
    /// <summary>
    /// The visual attention cue for a far-away office: a small plate above the concerned agent's
    /// station, sized from the viewing distance so it subtends about the same angle wherever the
    /// office is, plus an optional tiny mirror of it at the edge of the near-user area.
    ///
    /// Each level has its own shape AND words, never colour alone:
    ///   Blocking  = amber plate "Needs you" with a diamond "!" marker above it;
    ///   Completed = plain green plate "Done";
    ///   Error     = coral plate "Problem" with a bar across its top.
    /// Passive (and the unreachable Suggestion) show nothing.
    /// With the art model assigned (<see cref="artBeacon"/>) the WorkspaceAttentionBeacon stands on
    /// top of the plate and carries the shape (its own icon per level, light and clip) while the
    /// plate keeps the words; the model is sized for distance with the plate.
    ///
    /// Restrained: it appears with one eased pop, pulses once when the level changes, then stays
    /// still. Sound only for Blocking and Error, once per change, through
    /// FeedbackController.PlayCue (which already refuses to repeat a cue within its repeat window,
    /// so the cue the feedback controller itself plays for the same event is not doubled).
    ///
    /// It only reads <see cref="AttentionController"/>, so it shows a level only after the
    /// reducer confirmed its cause. In Desk mode it stays hidden unless
    /// <see cref="showInDeskMode"/> is set: the Researcher already walks up with the paper and
    /// the existing "!" above the agent (AgentView.alert) is in plain view. In remote modes that
    /// small "!" is still shown by AgentRegistry; this plate is the version that can be read
    /// from across the room.
    /// </summary>
    public class AttentionBeacon : MonoBehaviour
    {
        public AttentionController attention;

        [Header("Modes")]
        [Tooltip("Show the office cue in Desk mode too. Off by default.")]
        public bool showInDeskMode = false;
        [Tooltip("Show the small mirror cue near the user in remote modes.")]
        public bool showMirror = true;

        [Header("Office cue")]
        [Tooltip("Root of the cue at the office (NOT under WorkspaceRoot). Shown and hidden by scale.")]
        public Transform officeCue;
        public Vector3 officeCueShownScale = Vector3.one;
        [Tooltip("Child of the office cue that is scaled for the viewing distance.")]
        public Transform officeSizer;
        public GameObject officeBlocking, officeCompleted, officeError;
        [Tooltip("Stations, in the same order as stationAgentIds. The cue sits above the concerned one.")]
        public Transform[] stationAnchors;
        public string[] stationAgentIds;
        [Tooltip("Height of the cue above the station's origin, in office-root metres.")]
        public float heightAboveStation = 0.46f;
        [Tooltip("Height of the plate as built (m), at sizer scale 1.")]
        public float plateHeight = 0.05f;
        [Tooltip("Visual angle the plate's height should subtend (degrees).")]
        [Range(1.5f, 2.5f)] public float targetDegrees = 2f;
        public float minSizerScale = 0.5f;
        public float maxSizerScale = 5f;
        [Tooltip("Seconds the Done plate stays before it leaves by itself. Blocking and Problem stay.")]
        public float completedSeconds = 20f;

        [Header("Mirror cue (near the user)")]
        public Transform mirrorCue;
        public Vector3 mirrorCueShownScale = Vector3.one;
        public GameObject mirrorBlocking, mirrorCompleted, mirrorError;
        [Tooltip("Offset from the near-user point: x = to the user's right, y = up, z = away from the user.")]
        public Vector3 mirrorOffset = new Vector3(0.17f, 0.13f, 0.03f);

        [Header("Art (optional)")]
        [Tooltip("The WorkspaceAttentionBeacon model inside the office cue's sizer. It shows the level with its own icon, light and clip; the words plates stay (the model has no words). Active only while the cue is shown.")]
        public SpatialBeaconVisualController artBeacon;
        [Tooltip("Also show the model (alone, no plate) while agents report progress and nothing needs the user. Off: the cue appears only for Needs you / Done / Problem.")]
        public bool showWorkingState = false;

        [Header("The small '!' over the agent")]
        [Tooltip("AgentView.alert objects. While the office is remote they are scaled away (this cue replaces them) and put back afterwards. Their active state stays with AgentRegistry.")]
        public Transform[] agentAlerts;
        public bool hideAgentAlertsWhenRemote = true;

        [Header("Sound")]
        [Tooltip("FeedbackController sounds Attention / Error itself when the reducer reports the event. This cue stays silent for a level that appeared within this many seconds of that event, whatever the order of the handlers or the frame.")]
        public float feedbackOwnsCueSeconds = 1f;

        readonly ScaleEase _officeEase = new ScaleEase();
        readonly ScaleEase _mirrorEase = new ScaleEase();
        bool _bound, _eased;
        AttentionLevel _lastLevel = AttentionLevel.None;
        bool _completedExpired;
        float _hideCompletedAt;
        Transform _anchor;
        float _sizerScale = -1f;
        Vector3 _mirrorPlaced;
        bool _mirrorHasPlace;
        SpatialBeaconVisualController.BeaconState _artState;
        bool _artStateSet;
        Vector3[] _alertScales;
        bool _alertsHidden;

        /// <summary>The art model's state for an attention level. Suggestion is unreachable (no
        /// backend event); Disconnected is used only when there is no session to read.</summary>
        public static SpatialBeaconVisualController.BeaconState BeaconStateFor(AttentionLevel level, bool hasSession)
        {
            if (!hasSession) return SpatialBeaconVisualController.BeaconState.Disconnected;
            switch (level)
            {
                case AttentionLevel.Blocking: return SpatialBeaconVisualController.BeaconState.BlockingDecision;
                case AttentionLevel.Error: return SpatialBeaconVisualController.BeaconState.Error;
                case AttentionLevel.Completed: return SpatialBeaconVisualController.BeaconState.CompletedWork;
                case AttentionLevel.Suggestion: return SpatialBeaconVisualController.BeaconState.OptionalSuggestion;
                case AttentionLevel.Passive: return SpatialBeaconVisualController.BeaconState.WorkingNormally;
                default: return SpatialBeaconVisualController.BeaconState.Inactive;
            }
        }

        /// <summary>True while the art model is switched on (the office cue is shown and has a model).</summary>
        public bool ArtBeaconShown => artBeacon != null && artBeacon.gameObject.activeSelf;

        /// <summary>True while the office cue is shown (or on its way to being shown).</summary>
        public bool OfficeCueShown => _eased && _officeEase.Shown;
        /// <summary>True while the mirror cue is shown.</summary>
        public bool MirrorCueShown => _eased && _mirrorEase.Shown;
        /// <summary>The level the cue is currently presenting (None when hidden).</summary>
        public AttentionLevel ShownLevel => OfficeCueShown ? _lastLevel : AttentionLevel.None;

        void Awake()
        {
            if (officeCue != null)
                _officeEase.Init(officeCue, ScaleEase.ResolveFull(officeCue, officeCueShownScale), 0.25f, false);
            if (mirrorCue != null)
                _mirrorEase.Init(mirrorCue, ScaleEase.ResolveFull(mirrorCue, mirrorCueShownScale), 0.25f, false);
            _eased = true;
            // The model animates (ten skinned parts): it is switched on only while the cue is shown.
            if (artBeacon != null && artBeacon.gameObject.activeSelf) artBeacon.gameObject.SetActive(false);
        }

        void OnEnable() { SpatialWorkspaceState.Changed += Refresh; }

        void OnDisable()
        {
            SpatialWorkspaceState.Changed -= Refresh;
            if (_bound && attention != null) attention.Changed -= OnAttentionChanged;
            _bound = false;
            HideAgentAlerts(false);          // never leave the agents' own markers scaled away
        }

        void Update()
        {
            if (!_bound && attention != null)
            {
                attention.Changed += OnAttentionChanged;
                _bound = true;
                Refresh();
            }
            float dt = Time.unscaledDeltaTime;
            _officeEase.Tick(dt);
            _mirrorEase.Tick(dt);

            if (_lastLevel == AttentionLevel.Completed && !_completedExpired && Time.unscaledTime >= _hideCompletedAt)
            {
                _completedExpired = true;
                Refresh();
            }
            if (officeCue != null && _officeEase.Shown) PlaceOfficeCue();
            if (mirrorCue != null && _mirrorEase.Shown) PlaceMirrorCue();

            // The cue has finished leaving: switch the model (and its animator) off.
            if (artBeacon != null && !_officeEase.Shown && !_officeEase.Animating && artBeacon.gameObject.activeSelf)
            {
                artBeacon.gameObject.SetActive(false);
                _artStateSet = false;
            }
        }

        void OnAttentionChanged(AttentionLevel level) { Refresh(); }

        void Refresh()
        {
            if (!_eased || attention == null) return;
            AttentionLevel level = attention.Level;
            bool levelChanged = level != _lastLevel;
            if (levelChanged)
            {
                _lastLevel = level;
                _completedExpired = false;
                if (level == AttentionLevel.Completed) _hideCompletedAt = Time.unscaledTime + completedSeconds;
            }

            bool remote = SpatialWorkspaceState.IsRemote;
            bool cue = level == AttentionLevel.Blocking || level == AttentionLevel.Error
                       || (level == AttentionLevel.Completed && !_completedExpired);
            // Optional: the model alone (no plate) while agents are simply working.
            bool working = showWorkingState && artBeacon != null && level == AttentionLevel.Passive;
            bool show = (cue || working) && (remote || showInDeskMode) && officeCue != null;

            // The agents' own small "!" is replaced by this cue while the office is far away.
            HideAgentAlerts(remote && hideAgentAlertsWhenRemote && officeCue != null);

            if (show)
            {
                _anchor = AnchorFor(attention.AgentId);
                SetFaces(officeBlocking, officeCompleted, officeError, level);   // Passive: no plate
                ShowArtState(BeaconStateFor(level, attention.controller != null));
                bool was = _officeEase.Shown;
                _officeEase.Show();
                if (levelChanged || !was) _officeEase.Pulse(0.08f, 0.35f);     // once, then steady
                PlaceOfficeCue();
                // Sound only for the two levels that need the user, and only when the level changed.
                // FeedbackController sounds the same cue itself when the reducer reports the event;
                // that is detected from the event's time stamp, not from handler order or frame, so
                // one event is one sound. The cue sounds here only when the level appeared without
                // a fresh event (a loaded snapshot, or a failure uncovered when a decision resolved).
                if (levelChanged)
                {
                    float now = Time.unscaledTime;
                    bool feedbackListens = FeedbackController.Instance != null;
                    if (level == AttentionLevel.Blocking)
                    {
                        if (!feedbackListens || now - attention.LastDecisionRequestedAt > feedbackOwnsCueSeconds)
                            FeedbackController.PlayCue(FeedbackCue.Attention);
                    }
                    else if (level == AttentionLevel.Error)
                    {
                        if (!feedbackListens || now - attention.LastTaskFailedAt > feedbackOwnsCueSeconds)
                            FeedbackController.PlayCue(FeedbackCue.Error);
                    }
                }
            }
            else _officeEase.Hide();

            bool mirror = show && cue && showMirror && remote && mirrorCue != null;   // never an empty mirror for "working"
            if (mirror)
            {
                SetFaces(mirrorBlocking, mirrorCompleted, mirrorError, level);
                bool was = _mirrorEase.Shown;
                _mirrorEase.Show();
                if (levelChanged || !was) { _mirrorEase.Pulse(0.08f, 0.35f); _mirrorHasPlace = false; }
                PlaceMirrorCue();
            }
            else _mirrorEase.Hide();
        }

        static void SetFaces(GameObject blocking, GameObject completed, GameObject error, AttentionLevel level)
        {
            if (blocking != null) blocking.SetActive(level == AttentionLevel.Blocking);
            if (completed != null) completed.SetActive(level == AttentionLevel.Completed);
            if (error != null) error.SetActive(level == AttentionLevel.Error);
        }

        /// <summary>Switch the model on (if it was off) and give it the state. The art controller
        /// only swaps its icon, tints through a property block and cross-fades its own clip.</summary>
        void ShowArtState(SpatialBeaconVisualController.BeaconState state)
        {
            if (artBeacon == null) return;
            if (!artBeacon.gameObject.activeSelf)
            {
                artBeacon.gameObject.SetActive(true);     // its animator restarts in its default state
                _artStateSet = false;
            }
            if (_artStateSet && state == _artState) return;
            _artState = state;
            _artStateSet = true;
            artBeacon.SetState(state, false);
        }

        /// <summary>Scale the agents' "!" markers away (true) or put their scale back (false).
        /// Only the scale is touched: AgentRegistry keeps switching the objects on and off.</summary>
        void HideAgentAlerts(bool hide)
        {
            if (hide == _alertsHidden || agentAlerts == null) return;
            if (_alertScales == null || _alertScales.Length != agentAlerts.Length) _alertScales = new Vector3[agentAlerts.Length];
            for (int i = 0; i < agentAlerts.Length; i++)
            {
                Transform t = agentAlerts[i];
                if (t == null) continue;
                if (hide)
                {
                    _alertScales[i] = t.localScale;
                    t.localScale = _alertScales[i] * ScaleEase.HiddenFactor;
                }
                else t.localScale = _alertScales[i];
            }
            _alertsHidden = hide;
        }

        Transform AnchorFor(string agentId)
        {
            if (stationAnchors == null || stationAgentIds == null || string.IsNullOrEmpty(agentId)) return null;
            int n = Mathf.Min(stationAnchors.Length, stationAgentIds.Length);
            for (int i = 0; i < n; i++)
                if (stationAgentIds[i] == agentId) return stationAnchors[i];
            return null;
        }

        void PlaceOfficeCue()
        {
            Transform ws = SpatialWorkspaceState.WorkspaceRoot;
            Vector3 basePos;
            if (_anchor != null) basePos = _anchor.position;
            else if (ws != null) basePos = ws.position;          // agent unknown: above the office centre
            else return;
            float rootScale = ws != null ? Mathf.Abs(ws.lossyScale.y) : 1f;
            Vector3 pos = basePos + Vector3.up * (heightAboveStation * rootScale);
            officeCue.SetPositionAndRotation(pos, RemoteNearUser.FacingHead(pos, officeCue.rotation));

            // Size for the viewing distance: the plate's height subtends targetDegrees.
            Transform head = RemoteNearUser.Head();
            if (head == null || officeSizer == null) return;
            float d = Vector3.Distance(head.position, pos);
            float wanted = 2f * d * Mathf.Tan(targetDegrees * 0.5f * Mathf.Deg2Rad) / Mathf.Max(1e-4f, plateHeight);
            wanted = Mathf.Clamp(wanted, minSizerScale, maxSizerScale);
            if (_sizerScale < 0f || Mathf.Abs(wanted - _sizerScale) > 0.03f * _sizerScale)
            {
                _sizerScale = wanted;
                officeSizer.localScale = new Vector3(wanted, wanted, wanted);
            }
        }

        void PlaceMirrorCue()
        {
            if (!RemoteNearUser.TryGetFrame(out Vector3 origin, out Vector3 right, out Vector3 away)) return;
            Vector3 pos = origin + right * mirrorOffset.x + Vector3.up * mirrorOffset.y + away * mirrorOffset.z;
            // Stay put unless the near-user area itself moved (it must not chase the head).
            if (_mirrorHasPlace && (pos - _mirrorPlaced).sqrMagnitude < 0.15f * 0.15f) return;
            _mirrorPlaced = pos;
            _mirrorHasPlace = true;
            mirrorCue.SetPositionAndRotation(pos, RemoteNearUser.FacingHead(pos, mirrorCue.rotation));
        }
    }
}
