using UnityEngine;

namespace MuseDesk.Art
{
    /// <summary>
    /// Controls the single-mascot AgentConversationPerch floating platform:
    /// unfolding/folding local animations, conversation state ring tinting via MaterialPropertyBlock,
    /// and world-scale-safe attachment of one mascot and one optional presentation document.
    /// Runtime placement/routing sets this perch's world pose near the seated user.
    /// </summary>
    [DisallowMultipleComponent]
    public class AgentConversationPerchController : MonoBehaviour
    {
        public enum PerchState
        {
            Folded,
            Arriving,
            Listening,
            Speaking,
            Returning
        }

        private static readonly Color ColIdle      = new Color(0.184f, 0.639f, 0.604f, 1f); // Teal
        private static readonly Color ColListening = new Color(0.298f, 0.522f, 0.859f, 1f); // Info Blue
        private static readonly Color ColSpeaking  = new Color(0.180f, 0.608f, 0.373f, 1f); // Success Green
        private static readonly Color ColDim       = new Color(0.200f, 0.220f, 0.260f, 1f);

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        private static readonly int AnimTrigUnfold = Animator.StringToHash("UnfoldArrive");
        private static readonly int AnimTrigFold = Animator.StringToHash("FoldReturn");
        private static readonly int AnimTrigSpeakPulse = Animator.StringToHash("SpeakPulse");
        private static readonly int AnimBoolDeployed = Animator.StringToHash("Deployed");

        [Header("Animator & Indicator")]
        [SerializeField] private Animator perchAnimator;
        [SerializeField] private Renderer conversationIndicatorRenderer;

        [Header("Sockets")]
        [SerializeField] private Transform perchRoot;
        [SerializeField] private Transform agentStandSocket;
        [SerializeField] private Transform documentPresentationSocket;
        [SerializeField] private Transform conversationIndicator;

        private PerchState _state = PerchState.Folded;
        private MaterialPropertyBlock _mpb;

        public PerchState State => _state;
        public Transform PerchRoot => perchRoot;
        public Transform AgentStandSocket => agentStandSocket;
        public Transform DocumentPresentationSocket => documentPresentationSocket;
        public Transform ConversationIndicator => conversationIndicator;

        private void Awake()
        {
            ResolveReferences();
            ApplyIndicatorColor(ColIdle, 0.2f);
        }

        public void ResolveReferences()
        {
            if (perchAnimator == null)
                perchAnimator = GetComponentInChildren<Animator>(true);

            Transform[] all = GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                Transform t = all[i];
                switch (t.name)
                {
                    case "PerchRoot":                  perchRoot = t; break;
                    case "AgentStandSocket":           agentStandSocket = t; break;
                    case "DocumentPresentationSocket": documentPresentationSocket = t; break;
                    case "ConversationIndicator":      conversationIndicator = t; break;
                    case "Perch_ConvIndicatorRing":    conversationIndicatorRenderer = t.GetComponent<Renderer>(); break;
                }
            }
        }

        /// <summary>
        /// Mounts a single agent onto AgentStandSocket while preserving the mascot's exact world scale.
        /// </summary>
        public void AttachAgent(Transform agentTransform, Vector3 desiredWorldScale)
        {
            if (agentTransform == null) return;
            ResolveIfNeeded();
            Transform target = agentStandSocket != null ? agentStandSocket : transform;
            SpatialScaleUtility.MountPreservingWorldScale(agentTransform, target, desiredWorldScale, resetLocalPose: true);
        }

        /// <summary>
        /// Attaches a document prop onto DocumentPresentationSocket while preserving world scale.
        /// </summary>
        public void AttachDocument(Transform docTransform, Vector3 desiredWorldScale)
        {
            if (docTransform == null) return;
            ResolveIfNeeded();
            Transform target = documentPresentationSocket != null ? documentPresentationSocket : transform;
            SpatialScaleUtility.MountPreservingWorldScale(docTransform, target, desiredWorldScale, resetLocalPose: true);
        }

        public void TriggerUnfoldArrive()
        {
            ResolveIfNeeded();
            _state = PerchState.Arriving;
            if (perchAnimator != null)
            {
                perchAnimator.SetBool(AnimBoolDeployed, true);
                perchAnimator.SetTrigger(AnimTrigUnfold);
            }
            ApplyIndicatorColor(ColListening, 0.45f);
        }

        public void SetListening(bool active)
        {
            ResolveIfNeeded();
            _state = active ? PerchState.Listening : PerchState.Folded;
            if (perchAnimator != null)
                perchAnimator.SetBool(AnimBoolDeployed, active);
            ApplyIndicatorColor(active ? ColListening : ColDim, active ? 0.4f : 0.05f);
        }

        public void TriggerSpeakingPulse()
        {
            ResolveIfNeeded();
            _state = PerchState.Speaking;
            if (perchAnimator != null)
            {
                perchAnimator.SetBool(AnimBoolDeployed, true);
                perchAnimator.SetTrigger(AnimTrigSpeakPulse);
            }
            ApplyIndicatorColor(ColSpeaking, 0.6f);
        }

        public void TriggerFoldReturn()
        {
            ResolveIfNeeded();
            _state = PerchState.Returning;
            if (perchAnimator != null)
            {
                perchAnimator.SetBool(AnimBoolDeployed, false);
                perchAnimator.SetTrigger(AnimTrigFold);
            }
            ApplyIndicatorColor(ColDim, 0.1f);
        }

        /// <summary>
        /// Called cleanly if an animation event was missed or navigation completed without event delivery.
        /// </summary>
        public void RecoverToDeployedIdle()
        {
            ResolveIfNeeded();
            _state = PerchState.Listening;
            if (perchAnimator != null)
            {
                perchAnimator.SetBool(AnimBoolDeployed, true);
                perchAnimator.Play("Perch_Listening_Idle", 0, 0f);
            }
            ApplyIndicatorColor(ColListening, 0.4f);
        }

        private void ApplyIndicatorColor(Color col, float emissionStrength)
        {
            if (conversationIndicatorRenderer == null) return;
            if (_mpb == null) _mpb = new MaterialPropertyBlock();

            conversationIndicatorRenderer.GetPropertyBlock(_mpb);
            _mpb.SetColor(BaseColorId, col);
            _mpb.SetColor(EmissionColorId, col * emissionStrength);
            conversationIndicatorRenderer.SetPropertyBlock(_mpb);
        }

        private void ResolveIfNeeded()
        {
            if (perchRoot == null || conversationIndicatorRenderer == null)
                ResolveReferences();
        }
    }
}

