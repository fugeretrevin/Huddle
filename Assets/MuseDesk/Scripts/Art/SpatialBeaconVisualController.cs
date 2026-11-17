using UnityEngine;

namespace MuseDesk.Art
{
    /// <summary>
    /// Controls WorkspaceAttentionBeacon across all 7 accessibility-friendly visual states.
    /// Uses MaterialPropertyBlock for light ring and orb tinting (no per-object material instances)
    /// and toggles separate 3D symbolic icon meshes so states never rely solely on color.
    /// </summary>
    [DisallowMultipleComponent]
    public class SpatialBeaconVisualController : MonoBehaviour
    {
        public enum BeaconState
        {
            WorkingNormally,
            CompletedWork,
            OptionalSuggestion,
            BlockingDecision,
            Error,
            Disconnected,
            Inactive
        }

        // Calm, restrained peripheral-safe colors aligned with MUSEDESK_DESIGN_SYSTEM.md
        private static readonly Color ColWorking      = new Color(0.184f, 0.639f, 0.604f, 1f); // Teal
        private static readonly Color ColCompleted    = new Color(0.180f, 0.608f, 0.373f, 1f); // Success Green
        private static readonly Color ColSuggestion   = new Color(0.549f, 0.482f, 0.839f, 1f); // Lavender
        private static readonly Color ColBlocking     = new Color(0.878f, 0.627f, 0.188f, 1f); // Warm Amber
        private static readonly Color ColError        = new Color(0.851f, 0.325f, 0.290f, 1f); // Muted Coral-Red
        private static readonly Color ColDisconnected = new Color(0.320f, 0.350f, 0.400f, 1f); // Slate Gray
        private static readonly Color ColInactive     = new Color(0.140f, 0.155f, 0.180f, 1f); // Dim Charcoal

        [Header("Animator & Light Renderers")]
        [SerializeField] private Animator beaconAnimator;
        [SerializeField] private Renderer lightRingRenderer;
        [SerializeField] private Renderer crownOrbRenderer;
        [SerializeField] private Renderer flagTipRenderer;

        [Header("Symbolic 3D Icon Meshes (Accessibility)")]
        [SerializeField] private GameObject iconQuestion;
        [SerializeField] private GameObject iconDecisionDoc;
        [SerializeField] private GameObject iconWarningTri;
        [SerializeField] private GameObject iconCheckBadge;

        [Header("Sockets")]
        [SerializeField] private Transform iconSocket;
        [SerializeField] private Transform labelSocket;

        private BeaconState _currentState = BeaconState.Inactive;
        private MaterialPropertyBlock _mpb;
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        public BeaconState CurrentState => _currentState;
        public Transform IconSocket => iconSocket;
        public Transform LabelSocket => labelSocket;

        private void Awake()
        {
            ResolveReferences();
            SetState(_currentState, immediate: true);
        }

        public void ResolveReferences()
        {
            if (beaconAnimator == null)
                beaconAnimator = GetComponentInChildren<Animator>(true);

            Transform[] all = GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                Transform t = all[i];
                switch (t.name)
                {
                    case "Beacon_LightRing":         lightRingRenderer = t.GetComponent<Renderer>(); break;
                    case "Beacon_CrownOrb":          crownOrbRenderer = t.GetComponent<Renderer>(); break;
                    case "Beacon_MechanicalFlagTip": flagTipRenderer = t.GetComponent<Renderer>(); break;
                    case "Icon_Question":            iconQuestion = t.gameObject; break;
                    case "Icon_DecisionDoc":         iconDecisionDoc = t.gameObject; break;
                    case "Icon_WarningTri":          iconWarningTri = t.gameObject; break;
                    case "Icon_CheckBadge":          iconCheckBadge = t.gameObject; break;
                    case "IconSocket":               iconSocket = t; break;
                    case "LabelSocket":              labelSocket = t; break;
                }
            }
        }

        public void SetState(BeaconState state, bool immediate = false)
        {
            _currentState = state;
            ResolveIfNeeded();

            // Symbolic icon visibility (non-color accessibility indicator)
            if (iconQuestion != null)    iconQuestion.SetActive(state == BeaconState.OptionalSuggestion);
            if (iconDecisionDoc != null) iconDecisionDoc.SetActive(state == BeaconState.BlockingDecision);
            if (iconWarningTri != null)  iconWarningTri.SetActive(state == BeaconState.Error);
            if (iconCheckBadge != null)  iconCheckBadge.SetActive(state == BeaconState.CompletedWork);

            Color col;
            float emission;
            string clip;
            switch (state)
            {
                case BeaconState.WorkingNormally:
                    col = ColWorking; emission = 0.28f; clip = "Beacon_Working";
                    break;
                case BeaconState.CompletedWork:
                    col = ColCompleted; emission = 0.45f; clip = "Beacon_Completed";
                    break;
                case BeaconState.OptionalSuggestion:
                    col = ColSuggestion; emission = 0.35f; clip = "Beacon_Suggestion";
                    break;
                case BeaconState.BlockingDecision:
                    col = ColBlocking; emission = 0.55f; clip = "Beacon_Blocking_Decision";
                    break;
                case BeaconState.Error:
                    col = ColError; emission = 0.48f; clip = "Beacon_Error";
                    break;
                case BeaconState.Disconnected:
                    col = ColDisconnected; emission = 0.05f; clip = "Beacon_Idle";
                    break;
                default:
                    col = ColInactive; emission = 0.0f; clip = "Beacon_Idle";
                    break;
            }

            ApplyColorMpb(lightRingRenderer, col, emission);
            ApplyColorMpb(crownOrbRenderer, col, emission);
            ApplyColorMpb(flagTipRenderer, col, emission * 0.8f);

            if (beaconAnimator != null && beaconAnimator.runtimeAnimatorController != null)
            {
                int hash = Animator.StringToHash(clip);
                if (beaconAnimator.HasState(0, hash))
                    beaconAnimator.CrossFadeInFixedTime(hash, immediate ? 0f : 0.18f, 0);
            }
        }

        /// <summary>Soft incoming information ripple without changing the persistent state icon.</summary>
        public void PulseIncoming()
        {
            if (beaconAnimator == null || beaconAnimator.runtimeAnimatorController == null) return;
            int hash = Animator.StringToHash("Beacon_Incoming");
            if (beaconAnimator.HasState(0, hash))
                beaconAnimator.CrossFadeInFixedTime(hash, 0.10f, 0);
        }

        private void ResolveIfNeeded()
        {
            if (lightRingRenderer == null || crownOrbRenderer == null)
                ResolveReferences();
        }

        private void ApplyColorMpb(Renderer r, Color baseCol, float emissionIntensity)
        {
            if (r == null) return;
            if (_mpb == null) _mpb = new MaterialPropertyBlock();
            r.GetPropertyBlock(_mpb);
            _mpb.SetColor(BaseColorId, baseCol);
            _mpb.SetColor(EmissionColorId, baseCol * emissionIntensity);
            r.SetPropertyBlock(_mpb);
        }
    }
}

