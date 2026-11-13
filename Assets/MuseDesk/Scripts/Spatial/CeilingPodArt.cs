using UnityEngine;
using MuseDesk.Art;
using MuseDesk.Workspace;

namespace MuseDesk.Spatial
{
    /// <summary>
    /// Dresses the ceiling pod in its art. It moves nothing through the room:
    ///
    ///  - the SuspendedOfficePod model is a child of WorkspaceRoot (the office sits on its deck and
    ///    it travels with the office); this component only reveals it by scale while the mode is
    ///    Ceiling and switches it off otherwise;
    ///  - the CeilingMount and the mast are placed by <see cref="CeilingPodController"/>;
    ///  - the pod's and the mount's own clips (unlock, descent wobble, stabilise, shelf open and
    ///    close, ascent, dock lock, settle; mount unlock, spool, lock) are cross-faded when the
    ///    pod STATE MACHINE changes state (its StateChanged event; nothing is polled).
    ///
    /// The art-side CeilingWorkspacePodController is deliberately NOT used: it interpolates its
    /// pod root between its own docked and inspection poses and stretches its own tether, which
    /// would be a second state machine moving the office's visuals. Only its clip names are
    /// borrowed. The pod model already contains its inspection shelf (bone-driven by
    /// Pod_Shelf_Open / Pod_Shelf_Close), so the shelf opens on Engaged and closes in
    /// PreparingToRetract, before the office starts back up.
    /// </summary>
    public class CeilingPodArt : MonoBehaviour
    {
        public CeilingPodController pod;
        [Tooltip("Holder of the SuspendedOfficePod model, a child of WorkspaceRoot. Revealed by scale; inactive outside Ceiling mode.")]
        public Transform podVisual;
        public Vector3 podShownScale = Vector3.one;
        [Tooltip("Animator of the SuspendedOfficePod model (controller MuseDesk_CeilingPod).")]
        public Animator podAnimator;
        [Tooltip("Animator of the CeilingMount model (controller MuseDesk_CeilingMount). It lives under the pod controller's visuals root.")]
        public Animator mountAnimator;
        public float revealSeconds = 0.35f;
        [Tooltip("Seconds of a one-shot clip before the loop or idle that follows it.")]
        public float beginSeconds = 0.5f;
        public float shelfSeconds = 0.85f;
        public float settleSeconds = 0.7f;

        [Header("Props with nowhere to stand on the pod")]
        [Tooltip("Loose desk props that stand beside the office on the real desk, outside the pod's deck (measured by the builder). On the pod they would hang in the air beside it or over the folding wings, so they are scaled away while the mode is Ceiling and put back afterwards. Their physics state is not touched.")]
        public Transform[] offDeckProps;

        Vector3[] _propScales;
        bool _propsHidden;

        /// <summary>True while the off-deck props are scaled away (Ceiling mode).</summary>
        public bool OffDeckPropsHidden => _propsHidden;

        static readonly int PodDockedIdle = Animator.StringToHash(CeilingWorkspacePodController.ClipDockedIdle);
        static readonly int PodUnlock = Animator.StringToHash(CeilingWorkspacePodController.ClipUnlock);
        static readonly int PodBeginDescent = Animator.StringToHash(CeilingWorkspacePodController.ClipBeginDescent);
        static readonly int PodDescending = Animator.StringToHash(CeilingWorkspacePodController.ClipDescending);
        static readonly int PodStabilize = Animator.StringToHash(CeilingWorkspacePodController.ClipStabilize);
        static readonly int PodShelfOpen = Animator.StringToHash(CeilingWorkspacePodController.ClipShelfOpen);
        static readonly int PodDeployedIdle = Animator.StringToHash(CeilingWorkspacePodController.ClipDeployedIdle);
        static readonly int PodShelfClose = Animator.StringToHash(CeilingWorkspacePodController.ClipShelfClose);
        static readonly int PodBeginAscent = Animator.StringToHash(CeilingWorkspacePodController.ClipBeginAscent);
        static readonly int PodReturningDock = Animator.StringToHash(CeilingWorkspacePodController.ClipReturningDock);
        static readonly int PodDockLock = Animator.StringToHash(CeilingWorkspacePodController.ClipDockLock);
        static readonly int PodSettle = Animator.StringToHash(CeilingWorkspacePodController.ClipMechanicalSettle);
        static readonly int MountIdle = Animator.StringToHash(CeilingWorkspacePodController.MountIdle);
        static readonly int MountUnlock = Animator.StringToHash(CeilingWorkspacePodController.MountUnlock);
        static readonly int MountSpool = Animator.StringToHash(CeilingWorkspacePodController.MountSpool);
        static readonly int MountLock = Animator.StringToHash(CeilingWorkspacePodController.MountLock);

        readonly ScaleEase _ease = new ScaleEase();
        CeilingPodMachine _machine;
        bool _ready, _bound;
        CeilingPodState _state = CeilingPodState.Docked;
        bool _stateKnown;

        // What each animator should be playing, what it was last told, and one queued follow-up.
        int _podWant, _podPlaying, _podNext;
        int _mountWant, _mountPlaying;
        float _podNextAt;

        /// <summary>True while the pod model is shown (or easing in).</summary>
        public bool Shown => _ready && _ease.Shown;
        /// <summary>The pod state the clips were last chosen for.</summary>
        public CeilingPodState ArtState => _state;

        void Awake()
        {
            if (podVisual != null)
            {
                _ease.Init(podVisual, ScaleEase.ResolveFull(podVisual, podShownScale), revealSeconds, false);
                podVisual.gameObject.SetActive(false);
            }
            _ready = true;
        }

        void OnEnable()
        {
            SpatialWorkspaceState.Changed += Refresh;
            Refresh();
        }

        void OnDisable()
        {
            SpatialWorkspaceState.Changed -= Refresh;
            if (_bound && _machine != null) _machine.StateChanged -= OnPodChanged;
            _machine = null;
            _bound = false;
            HideOffDeckProps(false);         // never leave a desk prop scaled away
        }

        /// <summary>Scale the off-deck props away (true) or put their scale back (false). Only
        /// the scale is touched, the same way the rest of the office hides things.</summary>
        void HideOffDeckProps(bool hide)
        {
            if (hide == _propsHidden || offDeckProps == null) return;
            if (_propScales == null || _propScales.Length != offDeckProps.Length) _propScales = new Vector3[offDeckProps.Length];
            for (int i = 0; i < offDeckProps.Length; i++)
            {
                Transform t = offDeckProps[i];
                if (t == null) continue;
                if (hide)
                {
                    _propScales[i] = t.localScale;
                    t.localScale = _propScales[i] * ScaleEase.HiddenFactor;
                }
                else if (_propScales[i].sqrMagnitude > 0f) t.localScale = _propScales[i];
            }
            _propsHidden = hide;
        }

        void Update()
        {
            if (!_bound && pod != null)
            {
                _machine = pod.Machine;                 // null until the pod has its transition controller
                if (_machine != null)
                {
                    _machine.StateChanged += OnPodChanged;
                    _bound = true;
                    OnPodChanged();
                }
            }
            _ease.Tick(Time.unscaledDeltaTime);

            // Fully hidden after leaving Ceiling mode: switch the model off.
            if (podVisual != null && !_ease.Shown && !_ease.Animating && podVisual.gameObject.activeSelf)
                podVisual.gameObject.SetActive(false);

            if (_podNext != 0 && Time.unscaledTime >= _podNextAt) { _podWant = _podNext; _podNext = 0; }

            // An animator that was inactive when the state changed (the mount's parent is switched on
            // in LateUpdate) or that restarted (re-activation resets it) gets its clip here.
            Drive(podAnimator, _podWant, ref _podPlaying);
            Drive(mountAnimator, _mountWant, ref _mountPlaying);
        }

        static void Drive(Animator a, int want, ref int playing)
        {
            if (a == null) return;
            if (!a.isActiveAndEnabled) { playing = 0; return; }
            if (want == 0 || want == playing) return;
            playing = want;
            if (a.runtimeAnimatorController != null && a.HasState(0, want)) a.CrossFadeInFixedTime(want, 0.16f, 0);
        }

        void Refresh()
        {
            if (!_ready) return;
            // With the pod: the props that have no deck under them leave when it appears and come
            // back when it goes (the pod appears at the desk before the office leaves, and is hidden
            // again the moment the mode stops being Ceiling).
            HideOffDeckProps(podVisual != null && SpatialWorkspaceState.Mode == WorkspaceMode.Ceiling);
            if (podVisual == null) return;
            if (SpatialWorkspaceState.Mode == WorkspaceMode.Ceiling)
            {
                if (!podVisual.gameObject.activeSelf) podVisual.gameObject.SetActive(true);
                _ease.Show();
            }
            else _ease.Hide();
        }

        /// <summary>The pod machine raised StateChanged (also raised for status text: only a real
        /// state change picks new clips).</summary>
        void OnPodChanged()
        {
            if (_machine == null) return;
            CeilingPodState next = _machine.State;
            if (_stateKnown && next == _state) return;
            CeilingPodState prev = _state;
            bool first = !_stateKnown;
            _state = next;
            _stateKnown = true;

            _podNext = 0;
            switch (next)
            {
                case CeilingPodState.PreparingToDescend:
                    _podWant = PodUnlock; _mountWant = MountUnlock;
                    break;
                case CeilingPodState.Descending:
                    _podWant = PodBeginDescent; Queue(PodDescending, beginSeconds); _mountWant = MountSpool;
                    break;
                case CeilingPodState.Stabilizing:
                    _podWant = PodStabilize; _mountWant = MountIdle;
                    break;
                case CeilingPodState.Engaged:
                    // Beside the user and steady: the inspection shelf slides out.
                    _podWant = PodShelfOpen; Queue(PodDeployedIdle, shelfSeconds); _mountWant = MountIdle;
                    break;
                case CeilingPodState.PreparingToRetract:
                    // The shelf closes during the wind-up, before the office starts back up. On the
                    // first trip up from the desk there is no open shelf to close.
                    _podWant = !first && prev == CeilingPodState.Engaged ? PodShelfClose : PodBeginAscent;
                    _mountWant = MountIdle;
                    break;
                case CeilingPodState.Retracting:
                    _podWant = PodBeginAscent; Queue(PodReturningDock, beginSeconds); _mountWant = MountSpool;
                    break;
                case CeilingPodState.ReturningToDock:
                    _podWant = PodDockLock; _mountWant = MountLock;
                    break;
                case CeilingPodState.ErrorRecovery:
                    _podWant = PodReturningDock; _mountWant = MountSpool;
                    break;
                default:    // Docked
                    if (!first && prev == CeilingPodState.ReturningToDock) { _podWant = PodSettle; Queue(PodDockedIdle, settleSeconds); }
                    else _podWant = PodDockedIdle;
                    _mountWant = MountIdle;
                    break;
            }
        }

        void Queue(int state, float after)
        {
            _podNext = state;
            _podNextAt = Time.unscaledTime + after;
        }
    }
}
