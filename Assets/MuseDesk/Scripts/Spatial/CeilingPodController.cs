using UnityEngine;

namespace MuseDesk.Spatial
{
    /// <summary>
    /// Scene side of the ceiling pod. Wraps one <see cref="CeilingPodMachine"/> (Docked,
    /// PreparingToDescend, Descending, Stabilizing, Engaged, PreparingToRetract, Retracting,
    /// ReturningToDock, ErrorRecovery), which moves the office only through the shared
    /// WorkspaceTransitionController, and keeps the optional pod visuals in step: a mast stretched
    /// from the ceiling to the office over the ACTUAL distance, optional telescoping segments, and
    /// a shelf under the office. Every visual reference may be null: the pod works with the
    /// placeholder primitives the builder creates, with final art, or with nothing at all.
    /// The visuals are not children of WorkspaceRoot and contain no interactables.
    /// </summary>
    public class CeilingPodController : MonoBehaviour
    {
        public WorkspaceTransitionController transition;

        [Header("Timing")]
        public float prepareSeconds = 0.4f;
        public float stabilizeSeconds = 0.5f;
        public float settleSeconds = 0.4f;
        public float metresPerSecond = 0.45f;
        public float minTravelSeconds = 1.2f;
        public float maxTravelSeconds = 4.0f;

        [Header("Visuals (all optional)")]
        [Tooltip("Parent of the pod visuals; shown only in Ceiling mode.")]
        public GameObject visualsRoot;
        [Tooltip("A unit-height mesh (pivot at its centre) stretched from the ceiling to the office.")]
        public Transform mast;
        [Tooltip("Optional telescoping segments (unit height, pivot at centre). When set they share the length.")]
        public Transform[] mastSegments;
        [Tooltip("Small plate that stays on the ceiling above the dock.")]
        public Transform ceilingMount;
        [Tooltip("A unit cube used as the platform under the office.")]
        public Transform shelf;
        public float mastThickness = 0.02f;
        [Tooltip("Platform size (width, thickness, depth) at office scale 1.")]
        public Vector3 shelfSize = new Vector3(1.0f, 0.012f, 0.7f);
        [Tooltip("Platform centre in root-local XZ at scale 1 (the office is not centred on its root).")]
        public Vector2 shelfCenterOffset = new Vector2(0f, 0.12f);
        [Tooltip("Where the mast meets the office, above the root, at scale 1.")]
        public float mastAttachHeight = 0.46f;

        /// <summary>Set by the director, which then ticks the machine itself (fixed order).</summary>
        [System.NonSerialized] public bool tickedByDirector;

        CeilingPodMachine _machine;
        float _ceilingHeight;
        bool _hasCeilingHeight;
        bool _visualsShown;

        public CeilingPodMachine Machine
        {
            get
            {
                if (_machine == null && transition != null)
                {
                    _machine = new CeilingPodMachine(new UnityUnscaledClock(), transition.Machine, IsHeld);
                    _machine.prepareSeconds = prepareSeconds;
                    _machine.stabilizeSeconds = stabilizeSeconds;
                    _machine.settleSeconds = settleSeconds;
                    _machine.metresPerSecond = metresPerSecond;
                    _machine.minTravelSeconds = minTravelSeconds;
                    _machine.maxTravelSeconds = maxTravelSeconds;
                }
                return _machine;
            }
        }

        public CeilingPodState State => _machine != null ? _machine.State : CeilingPodState.Docked;
        public bool Active => _machine != null && _machine.Active;

        static bool IsHeld() { return SpatialWorkspaceState.TransitionHeld; }

        /// <summary>World height of the real ceiling above the dock (from the room data).</summary>
        public void SetCeilingHeight(float height) { _ceilingHeight = height; _hasCeilingHeight = true; }

        /// <summary>Bring the office down beside the user. Does nothing unless Ceiling mode is active and docked.</summary>
        public void BringDown() { if (_machine != null) _machine.RequestDescend(); }

        /// <summary>Send the office back up. Waits while the user holds something (TransitionHeld).</summary>
        public void SendUp() { if (_machine != null) _machine.RequestRetract(); }

        void Awake()
        {
            if (visualsRoot != null) visualsRoot.SetActive(false);
        }

        void Update()
        {
            if (!tickedByDirector && _machine != null) _machine.Tick();
        }

        void LateUpdate()
        {
            bool show = _machine != null && _machine.Active && _hasCeilingHeight
                        && transition != null && transition.workspaceRoot != null;
            if (show != _visualsShown)
            {
                _visualsShown = show;
                if (visualsRoot != null) visualsRoot.SetActive(show);
            }
            if (!show) return;

            Transform root = transition.workspaceRoot;
            float scale = root.localScale.x;
            Vector3 dock = _machine.DockPose.position;
            Vector3 top = new Vector3(dock.x, _ceilingHeight, dock.z);
            Vector3 bottom = root.position + root.rotation * new Vector3(shelfCenterOffset.x * scale, mastAttachHeight * scale, shelfCenterOffset.y * scale);

            if (ceilingMount != null) ceilingMount.position = top;

            Vector3 span = bottom - top;
            float length = span.magnitude;
            Quaternion along = length > 1e-4f ? Quaternion.FromToRotation(Vector3.up, -span / length) : Quaternion.identity;

            int segments = mastSegments != null ? mastSegments.Length : 0;
            if (segments > 0)
            {
                // Telescoping: each segment covers an equal share of the real distance, thinner toward the office.
                float each = length / segments;
                for (int i = 0; i < segments; i++)
                {
                    Transform seg = mastSegments[i];
                    if (seg == null) continue;
                    float mid = (i + 0.5f) * each;
                    float thick = mastThickness * (1f + 0.35f * (segments - 1 - i));
                    seg.SetPositionAndRotation(length > 1e-4f ? top + span * (mid / length) : top, along);
                    seg.localScale = new Vector3(thick, Mathf.Max(0.001f, each), thick);
                }
                if (mast != null && mast.gameObject.activeSelf) mast.gameObject.SetActive(false);
            }
            else if (mast != null)
            {
                mast.SetPositionAndRotation(top + span * 0.5f, along);
                mast.localScale = new Vector3(mastThickness, Mathf.Max(0.001f, length), mastThickness);
            }

            if (shelf != null)
            {
                Vector3 centre = root.position + root.rotation * new Vector3(shelfCenterOffset.x * scale, -shelfSize.y * 0.5f * scale, shelfCenterOffset.y * scale);
                shelf.SetPositionAndRotation(centre, root.rotation);
                shelf.localScale = shelfSize * scale;
            }
        }
    }
}
