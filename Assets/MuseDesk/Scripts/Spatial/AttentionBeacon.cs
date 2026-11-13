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
    ///
    /// Where the office cue is (decided per frame, placed in LateUpdate after the office's mover
    /// has written the root, so it travels with the office with no lag):
    ///   Desk             above the concerned station, as before (only if showInDeskMode);
    ///   Floor            above the office's front edge, <see cref="aboveClearance"/> over the
    ///                    tallest thing in the office;
    ///   Ceiling, up high hanging just below the pod's underside at its front edge (the side that
    ///                    faces the user), so it reads as "the pod wants you" from the seat;
    ///   Ceiling, lowered above the front edge, as on the floor.
    /// "Up high" is the office root being above the user's eyes (with hysteresis). When the place
    /// changes while the cue is shown it pops out and pops in again at the new place; it never
    /// slides through the pod.
    ///
    /// Both cues are always upright: world up, turned about the vertical only to face the head.
    /// They are never pitched or rolled and are not children of anything that animates.
    /// </summary>
    [DefaultExecutionOrder(9000)]
    public class AttentionBeacon : MonoBehaviour
    {
        /// <summary>Where the office cue is anchored.</summary>
        public enum CuePlacement { Station = 0, AboveFront = 1, BelowPod = 2 }

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

        [Header("Office cue placement (remote modes)")]
        [Tooltip("Anchor the cue to the office itself in Floor and Ceiling mode. Off: above the concerned station in every mode (the old behaviour).")]
        public bool anchorToOffice = true;
        [Tooltip("Root-local height of the tallest thing in the office (m). Measured by the builder.")]
        public float officeTopHeight = 0.36f;
        [Tooltip("Height of the cue's plate above the tallest thing, Floor mode and the lowered pod (m).")]
        public float aboveClearance = 0.35f;
        [Tooltip("Root-local x of the office's centre and z of its front edge (the side toward the user). Measured by the builder.")]
        public Vector2 officeFrontLocal = new Vector2(0f, 0.36f);
        [Tooltip("Above the front edge, stand over the concerned station's x instead of the office centre.")]
        public bool followStationX = true;
        [Tooltip("Root-local y of the ceiling pod's underside (negative: below the office root). Measured by the builder.")]
        public float podUndersideY = -0.06f;
        [Tooltip("Root-local z of the pod's front edge. Measured by the builder.")]
        public float podFrontZ = 0.34f;
        [Tooltip("How far in from the pod's front edge the cue hangs (m).")]
        public float hangInset = 0.03f;
        [Tooltip("Gap between the pod's underside and the top of the hanging cue (m).")]
        public float hangGap = 0.04f;
        [Tooltip("Top of the whole cue (plate and model) above the cue's origin at sizer scale 1 (m). Measured by the builder.")]
        public float cueTopAtUnitScale = 0.16f;
        [Tooltip("Ceiling mode: the cue hangs below the pod once the office root is this far above the eyes (m)...")]
        public float hangWhenAboveEyes = 0.05f;
        [Tooltip("...and goes back above the front edge once the root is this far below the eyes (m, positive).")]
        public float standWhenBelowEyes = 0.15f;

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
        CuePlacement _placement = CuePlacement.Station;
        bool _placementSet;
        bool _wantOffice;        // the office cue should be showing (set by Refresh)
        bool _swapping;          // popping out before moving to a new placement

        /// <summary>Where the office cue is anchored right now.</summary>
        public CuePlacement Placement => _placement;
        /// <summary>The sizer scale chosen for the viewing distance (or -1 before the first placement).</summary>
        public float SizerScale => _sizerScale;

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
        public bool OfficeCueShown => _eased && (_officeEase.Shown || (_swapping && _wantOffice));
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
            UpdatePlacement();

            // The cue has finished leaving: switch the model (and its animator) off.
            if (artBeacon != null && !_swapping && !_officeEase.Shown && !_officeEase.Animating && artBeacon.gameObject.activeSelf)
            {
                artBeacon.gameObject.SetActive(false);
                _artStateSet = false;
            }
        }

        // After every mover (the director, the transition controller and the pod all write the
        // office in Update), so the cue is exactly on the office in the frame that is drawn.
        void LateUpdate()
        {
            if (officeCue != null && (_officeEase.Shown || _officeEase.Animating)) PlaceOfficeCue();
            if (mirrorCue != null && (_mirrorEase.Shown || _mirrorEase.Animating)) PlaceMirrorCue();
        }

        /// <summary>Where the cue belongs for the current mode and height of the office.</summary>
        CuePlacement WantedPlacement()
        {
            WorkspaceMode mode = SpatialWorkspaceState.Mode;
            Transform ws = SpatialWorkspaceState.WorkspaceRoot;
            if (!anchorToOffice || mode == WorkspaceMode.Desk || ws == null) return CuePlacement.Station;
            if (mode == WorkspaceMode.Ceiling)
            {
                Transform head = RemoteNearUser.Head();
                if (head == null)
                    return SpatialWorkspaceState.IsRemote ? CuePlacement.BelowPod : CuePlacement.AboveFront;
                float above = ws.position.y - head.position.y;      // office root over the eyes
                bool hanging = _placementSet && _placement == CuePlacement.BelowPod;
                if (hanging ? above > -standWhenBelowEyes : above > hangWhenAboveEyes) return CuePlacement.BelowPod;
            }
            return CuePlacement.AboveFront;
        }

        /// <summary>Follow the wanted placement. A change while the cue is visible is a pop out,
        /// a move, and a pop in; it never slides from under the pod to above it.</summary>
        void UpdatePlacement()
        {
            if (officeCue == null) return;
            CuePlacement want = WantedPlacement();
            if (_swapping)
            {
                if (_officeEase.Animating) return;               // still popping out
                _swapping = false;
                _placement = want; _placementSet = true;
                _sizerScale = -1f;                               // re-size for the new distance at once
                if (_wantOffice)
                {
                    ShowArtState(BeaconStateFor(_lastLevel, attention != null && attention.controller != null));
                    _officeEase.Show();
                    PlaceOfficeCue();
                }
                return;
            }
            if (_placementSet && want == _placement) return;
            bool visible = _officeEase.Shown || _officeEase.Animating;
            if (!_placementSet || !visible)
            {
                _placement = want; _placementSet = true;
                _sizerScale = -1f;
                return;
            }
            _swapping = true;
            _officeEase.Hide();
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

            _wantOffice = show;
            if (show)
            {
                _anchor = AnchorFor(attention.AgentId);
                SetFaces(officeBlocking, officeCompleted, officeError, level);   // Passive: no plate
                ShowArtState(BeaconStateFor(level, attention.controller != null));
                bool was = _officeEase.Shown;
                if (!_swapping)
                {
                    // Not yet visible: take the right place before it pops in.
                    if (!was && !_officeEase.Animating) { _placement = WantedPlacement(); _placementSet = true; _sizerScale = -1f; }
                    _officeEase.Show();
                    if (levelChanged || !was) _officeEase.Pulse(0.08f, 0.35f);     // once, then steady
                    PlaceOfficeCue();
                }
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
            else
            {
                _swapping = false;
                _officeEase.Hide();
            }

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

        /// <summary>
        /// Upright rotation that turns an object's local -Z (its printed side) to the head: a turn
        /// about the world vertical only. The object's up is exactly world up whatever the height
        /// difference to the head, so a cue high above or far below the eyes is never pitched or
        /// rolled. With no head, or the head straight above or below, the current heading is kept
        /// (made upright).
        /// </summary>
        public static Quaternion UprightFacing(Vector3 position, Vector3 headPosition, bool hasHead, Quaternion current)
        {
            Vector3 d = hasHead ? position - headPosition : Vector3.zero;
            d.y = 0f;
            if (d.sqrMagnitude < 1e-6f)
            {
                d = current * Vector3.forward;
                d.y = 0f;
                if (d.sqrMagnitude < 1e-6f) d = Vector3.forward;
            }
            return Quaternion.LookRotation(d.normalized, Vector3.up);
        }

        static Quaternion Upright(Vector3 position, Quaternion current)
        {
            Transform head = RemoteNearUser.Head();
            return UprightFacing(position, head != null ? head.position : Vector3.zero, head != null, current);
        }

        /// <summary>The point the cue is anchored to for a placement, before the cue's own size is
        /// taken into account. False when there is nothing to anchor to.</summary>
        bool AnchorPoint(CuePlacement placement, out Vector3 point)
        {
            Transform ws = SpatialWorkspaceState.WorkspaceRoot;
            point = Vector3.zero;
            float rootScale = ws != null ? Mathf.Abs(ws.lossyScale.y) : 1f;
            if (placement == CuePlacement.Station || ws == null)
            {
                if (_anchor != null) point = _anchor.position;
                else if (ws != null) point = ws.position;        // agent unknown: above the office centre
                else return false;
                point += Vector3.up * (heightAboveStation * rootScale);
                return true;
            }
            if (placement == CuePlacement.BelowPod)
            {
                // Under the pod's front edge: the side of the office that faces the user.
                point = ws.TransformPoint(new Vector3(officeFrontLocal.x, podUndersideY, podFrontZ - hangInset));
                return true;
            }
            float x = officeFrontLocal.x;
            if (followStationX && _anchor != null) x = ws.InverseTransformPoint(_anchor.position).x;
            point = ws.TransformPoint(new Vector3(x, officeTopHeight + aboveClearance, officeFrontLocal.y));
            return true;
        }

        void PlaceOfficeCue()
        {
            if (!AnchorPoint(_placement, out Vector3 pos)) return;

            // Size for the viewing distance: the plate's height subtends targetDegrees.
            Transform head = RemoteNearUser.Head();
            if (head != null && officeSizer != null)
            {
                float d = Vector3.Distance(head.position, pos);
                float wanted = 2f * d * Mathf.Tan(targetDegrees * 0.5f * Mathf.Deg2Rad) / Mathf.Max(1e-4f, plateHeight);
                wanted = Mathf.Clamp(wanted, minSizerScale, maxSizerScale);
                if (_sizerScale < 0f || Mathf.Abs(wanted - _sizerScale) > 0.03f * _sizerScale)
                {
                    _sizerScale = wanted;
                    officeSizer.localScale = new Vector3(wanted, wanted, wanted);
                }
            }

            if (_placement == CuePlacement.BelowPod)
            {
                // Hang: the top of the whole cue (model included, at its current size) stays a
                // fixed gap under the pod, so nothing pokes up into the deck.
                float size = _sizerScale > 0f ? _sizerScale : 1f;
                pos += Vector3.down * (hangGap + cueTopAtUnitScale * size);
            }
            officeCue.SetPositionAndRotation(pos, Upright(pos, officeCue.rotation));
        }

        void PlaceMirrorCue()
        {
            if (!RemoteNearUser.TryGetFrame(out Vector3 origin, out Vector3 right, out Vector3 away)) return;
            Vector3 pos = origin + right * mirrorOffset.x + Vector3.up * mirrorOffset.y + away * mirrorOffset.z;
            // Stay put unless the near-user area itself moved (it must not chase the head).
            if (_mirrorHasPlace && (pos - _mirrorPlaced).sqrMagnitude < 0.15f * 0.15f) return;
            _mirrorPlaced = pos;
            _mirrorHasPlace = true;
            mirrorCue.SetPositionAndRotation(pos, Upright(pos, mirrorCue.rotation));
        }
    }
}
