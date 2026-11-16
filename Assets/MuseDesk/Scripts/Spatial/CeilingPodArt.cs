using System.Collections.Generic;
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
        public float beginSeconds = 0.625f;
        public float shelfSeconds = 0.833f;
        public float settleSeconds = 0.667f;

        [Header("Props with nowhere to stand on the pod")]
        [Tooltip("Loose desk props that stand beside the office on the real desk, outside the pod's deck (measured by the builder). On the pod they would hang in the air beside it or over the folding wings, so they are scaled away while the mode is Ceiling and put back afterwards. Their physics state is not touched.")]
        public Transform[] offDeckProps;

        [Header("Deck rugs (owner rc13: two pads intersect)")]
        [Tooltip("The pod model's station rugs are parts of one mesh. The Tester's and the Developer's overlap by about 7 mm at the same height, so they flicker where they meet. At load a copy of the mesh is made and each side rug is slid away from the middle one until there is this gap between them (a share of the rug's width). 0 turns it off; the model asset is never changed.")]
        [Range(0f, 0.2f)] public float rugGapShare = 0.10f;
        public string centreRugMaterial = "Mat_Developer_Coral";
        public string[] sideRugMaterials = { "Mat_Tester_Mint", "Mat_Researcher_Lavender" };
        /// <summary>How many side rugs were moved at load (0 when nothing overlapped).</summary>
        public int RugsSeparated { get; private set; }

        [Header("Paper hand-over on the lowered pod (owner rc13)")]
        [Tooltip("While the pod is down (Engaged), a decision paper is handed over at the pod's front shelf (its AgentInteractionSocket) instead of at the front of the desks.")]
        public bool handOverAtShelf = true;
        [Tooltip("How far in from the shelf socket the agent stands (m, office scale 1), so its toes stay on the shelf.")]
        public float shelfInset = 0.045f;

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
                try { SeparateRugs(); }
                catch (System.Exception ex) { Debug.LogWarning("[CeilingPodArt] rug separation skipped: " + ex.Message); }
                if (handOverAtShelf && GetComponent<CeilingShelfHandOver>() == null) gameObject.AddComponent<CeilingShelfHandOver>().art = this;
            }
            _ready = true;
        }

        // â”€â”€ deck rugs â”€â”€

        void SeparateRugs()
        {
            if (podVisual == null || rugGapShare <= 0f || sideRugMaterials == null) return;
            var smr = podVisual.GetComponentInChildren<SkinnedMeshRenderer>(true);
            if (smr == null || smr.sharedMesh == null || !smr.sharedMesh.isReadable) return;
            Mesh src = smr.sharedMesh;
            Material[] mats = smr.sharedMaterials;
            int centre = SubmeshOf(mats, centreRugMaterial, src.subMeshCount);
            if (centre < 0) return;
            Vector3[] v = src.vertices;
            List<int> cPiece = LargestFlatPiece(src, centre, v);
            if (cPiece == null) return;
            Bounds cb = BoundsOf(cPiece, v);
            int flat = FlatAxis(cb.size);
            int moved = 0;
            foreach (string name in sideRugMaterials)
            {
                int s = SubmeshOf(mats, name, src.subMeshCount);
                if (s < 0 || s == centre) continue;
                List<int> piece = LargestFlatPiece(src, s, v);
                if (piece == null) continue;
                Bounds sb = BoundsOf(piece, v);
                // Overlap on both deck axes? Push along the deck axis where the centres are farther apart.
                int a0 = (flat + 1) % 3, a1 = (flat + 2) % 3;
                if (!Overlaps(sb, cb, a0) || !Overlaps(sb, cb, a1)) { if (!Near(sb, cb, a0, a1, rugGapShare)) continue; }
                int axis = Mathf.Abs(sb.center[a0] - cb.center[a0]) >= Mathf.Abs(sb.center[a1] - cb.center[a1]) ? a0 : a1;
                float dir = Mathf.Sign(sb.center[axis] - cb.center[axis]);
                float gap = rugGapShare * sb.size[axis];
                float shift = dir > 0f ? (cb.max[axis] + gap) - sb.min[axis] : (cb.min[axis] - gap) - sb.max[axis];
                if (Mathf.Abs(shift) < 1e-9f || Mathf.Sign(shift) != dir) continue;     // already clear by the gap
                Vector3 d = Vector3.zero; d[axis] = shift;
                foreach (int i in piece) v[i] += d;
                moved++;
            }
            if (moved == 0) return;
            Mesh copy = Instantiate(src);
            copy.name = src.name + " (rugs separated)";
            copy.vertices = v;
            smr.sharedMesh = copy;
            RugsSeparated = moved;
        }

        static bool Overlaps(Bounds a, Bounds b, int axis) { return a.min[axis] < b.max[axis] && b.min[axis] < a.max[axis]; }
        static bool Near(Bounds a, Bounds b, int a0, int a1, float share)
        {
            // Touching (within the wanted gap) on one axis while overlapping on the other also counts.
            float g0 = share * a.size[a0], g1 = share * a.size[a1];
            bool o0 = a.min[a0] < b.max[a0] + g0 && b.min[a0] < a.max[a0] + g0;
            bool o1 = a.min[a1] < b.max[a1] + g1 && b.min[a1] < a.max[a1] + g1;
            return o0 && o1;
        }
        static int FlatAxis(Vector3 s) { return s.x <= s.y && s.x <= s.z ? 0 : s.y <= s.z ? 1 : 2; }

        static int SubmeshOf(Material[] mats, string name, int count)
        {
            if (string.IsNullOrEmpty(name) || mats == null) return -1;
            for (int i = 0; i < mats.Length && i < count; i++) if (mats[i] != null && mats[i].name == name) return i;
            return -1;
        }

        static Bounds BoundsOf(List<int> idx, Vector3[] v)
        {
            var b = new Bounds(v[idx[0]], Vector3.zero);
            for (int i = 1; i < idx.Count; i++) b.Encapsulate(v[idx[i]]);
            return b;
        }

        /// <summary>The submesh's connected piece (triangles, welded by position) with the largest deck area.</summary>
        static List<int> LargestFlatPiece(Mesh m, int sub, Vector3[] v)
        {
            int[] t = m.GetTriangles(sub);
            if (t.Length == 0) return null;
            var parent = new Dictionary<int, int>();
            int Find(int x) { int p; if (!parent.TryGetValue(x, out p)) { parent[x] = x; return x; } while (p != x) { int g = parent[p]; parent[x] = g; x = p; p = g; } return x; }
            void Union(int a, int b) { a = Find(a); b = Find(b); if (a != b) parent[b] = a; }
            for (int i = 0; i < t.Length; i += 3) { Union(t[i], t[i + 1]); Union(t[i], t[i + 2]); }
            var weld = new Dictionary<Vector3Int, int>();
            foreach (int k in new List<int>(parent.Keys))
            {
                var key = Vector3Int.RoundToInt(v[k] * 1e6f);
                int o; if (weld.TryGetValue(key, out o)) Union(o, k); else weld[key] = k;
            }
            var groups = new Dictionary<int, List<int>>();
            foreach (int k in new List<int>(parent.Keys))
            {
                int r = Find(k); List<int> g;
                if (!groups.TryGetValue(r, out g)) groups[r] = g = new List<int>();
                g.Add(k);
            }
            List<int> best = null; float bestArea = -1f;
            foreach (var g in groups.Values)
            {
                Vector3 s = BoundsOf(g, v).size;
                int f = FlatAxis(s);
                float area = s[(f + 1) % 3] * s[(f + 2) % 3];
                if (area > bestArea) { bestArea = area; best = g; }
            }
            return best;
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
                    _podWant = PodStabilize; _mountWant = MountSpool;
                    break;
                case CeilingPodState.Engaged:
                    // Beside the user and steady: the inspection shelf slides out while iris bay stays open around mast.
                    _podWant = PodShelfOpen; Queue(PodDeployedIdle, shelfSeconds); _mountWant = MountSpool;
                    break;
                case CeilingPodState.PreparingToRetract:
                    // The shelf closes during the wind-up, before the office starts back up. On the
                    // first trip up from the desk there is no open shelf to close. Keep iris bay open around mast.
                    _podWant = !first && prev == CeilingPodState.Engaged ? PodShelfClose : PodBeginAscent;
                    _mountWant = MountSpool;
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
