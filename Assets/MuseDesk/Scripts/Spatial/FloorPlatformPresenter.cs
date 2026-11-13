using UnityEngine;
using MuseDesk.Art;
using MuseDesk.Workspace;

namespace MuseDesk.Spatial
{
    /// <summary>
    /// Shows the FloorOfficePlatform art under the office in Floor mode. The platform (with the
    /// courier station on it) is a child of WorkspaceRoot, so it travels with the office and this
    /// component never moves it: it only reveals it by an eased scale once the office has ARRIVED
    /// on the floor, and removes it at once the moment the office is asked to leave (the state
    /// change that starts a transition arrives before the root's first move), so the platform is
    /// never seen flying across the room. Outside Floor mode its object is inactive.
    ///
    /// The platform's embedded status strip follows the attention level (teal at rest, amber for a
    /// pending decision, green for delivered work, coral for a failure) through the art-side
    /// FloorOfficePlatformController, which only writes a property block. The strip is an extra:
    /// the beacon above the office carries the same state in words.
    ///
    /// Presentation only; it reads <see cref="SpatialWorkspaceState"/> and
    /// <see cref="AttentionController"/> and holds no reference to the placement components.
    /// </summary>
    public class FloorPlatformPresenter : MonoBehaviour
    {
        [Tooltip("Holder of the platform art, a child of WorkspaceRoot. Revealed by scale; inactive outside Floor mode.")]
        public Transform platform;
        public Vector3 platformShownScale = Vector3.one;
        [Tooltip("The art-side controller on the platform instance (status strip only).")]
        public FloorOfficePlatformController art;
        public AttentionController attention;
        public float revealSeconds = 0.35f;
        [Range(0f, 1f)] public float stripEmission = 0.25f;

        readonly ScaleEase _ease = new ScaleEase();
        bool _ready, _bound, _stripSet;
        AttentionLevel _stripLevel;

        /// <summary>True while the platform is shown (or easing in).</summary>
        public bool Shown => _ready && _ease.Shown;

        void Awake()
        {
            if (platform != null)
            {
                _ease.Init(platform, ScaleEase.ResolveFull(platform, platformShownScale), revealSeconds, false);
                platform.gameObject.SetActive(false);
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
            if (_bound && attention != null) attention.Changed -= OnAttentionChanged;
            _bound = false;
        }

        void Update()
        {
            if (!_bound && attention != null)
            {
                attention.Changed += OnAttentionChanged;
                _bound = true;
                ApplyStrip(false);
            }
            _ease.Tick(Time.unscaledDeltaTime);     // does nothing at rest
        }

        void OnAttentionChanged(AttentionLevel level) { ApplyStrip(false); }

        void Refresh()
        {
            if (!_ready || platform == null) return;
            bool show = SpatialWorkspaceState.Mode == WorkspaceMode.Floor && !SpatialWorkspaceState.IsTransitioning;
            if (show)
            {
                if (!platform.gameObject.activeSelf)
                {
                    platform.gameObject.SetActive(true);
                    ApplyStrip(true);
                }
                _ease.Show();
            }
            else if (platform.gameObject.activeSelf)
            {
                // Leaving (or never arrived): gone before the root moves.
                _ease.Snap(false);
                platform.gameObject.SetActive(false);
            }
        }

        void ApplyStrip(bool force)
        {
            if (art == null || platform == null || !platform.gameObject.activeSelf) return;
            AttentionLevel level = attention != null ? attention.Level : AttentionLevel.None;
            if (!force && _stripSet && level == _stripLevel) return;
            _stripSet = true;
            _stripLevel = level;
            Color c;
            switch (level)
            {
                case AttentionLevel.Blocking: c = FloorOfficePlatformController.AmberColor; break;
                case AttentionLevel.Completed: c = FloorOfficePlatformController.GreenColor; break;
                case AttentionLevel.Error: c = FloorOfficePlatformController.RedColor; break;
                default: c = FloorOfficePlatformController.DefaultTeal; break;
            }
            art.SetStatusStripColor(c, stripEmission);
        }
    }
}
