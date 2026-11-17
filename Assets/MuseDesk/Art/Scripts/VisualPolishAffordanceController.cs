using UnityEngine;
using MuseDesk.Models;
using MuseDesk.Networking;
using MuseDesk.Phase2.Board;
using MuseDesk.Phase2.Contracts;

namespace MuseDesk.Art.VisualPolish
{
    public enum AssignmentVisualState
    {
        Hidden = 0,
        ValidTarget = 1,
        HoveredWorkstation = 2,
        SelectedAgent = 3,
        PendingAssignment = 4,
        ConfirmedAssignment = 5,
        InvalidAssignment = 6,
        CompletedTask = 7,
    }

    public enum MascotFeedbackState
    {
        None = 0,
        Listening = 1,
        Speaking = 2,
        Thinking = 3,
        NeedsAttention = 4,
        Error = 5,
        Success = 6,
    }

    /// <summary>
    /// Event-driven visual affordance controller for workstation task assignment (Section 3).
    /// Supports all 7 states (ValidTarget, HoveredWorkstation, SelectedAgent, PendingAssignment,
    /// ConfirmedAssignment, InvalidAssignment, CompletedTask) using MaterialPropertyBlock so no
    /// runtime material instances are allocated. Does not mutate task or backend state.
    /// </summary>
    public class AssignmentAffordanceView : MonoBehaviour
    {
        [Header("Target Station")]
        public string agentId = Phase2AgentIds.Researcher;
        public StationDropZone stationDropZone;
        public BoardPresenter boardPresenter;

        [Header("Visual Renderers")]
        public GameObject haloRoot;
        public Renderer haloOuterRenderer;
        public Renderer receivePadStatusRenderer;
        public Renderer statePuckCoreRenderer;

        [Header("Current State")]
        public AssignmentVisualState currentState = AssignmentVisualState.ValidTarget;

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int ColorId = Shader.PropertyToID("_Color");
        static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        MaterialPropertyBlock _mpb;

        void Awake()
        {
            _mpb = new MaterialPropertyBlock();
            ApplyVisualState(currentState);
        }

        void OnEnable()
        {
            if (boardPresenter != null)
                boardPresenter.AssignResolved += OnAssignResolved;
            ApplyVisualState(currentState);
        }

        void OnDisable()
        {
            if (boardPresenter != null)
                boardPresenter.AssignResolved -= OnAssignResolved;
        }

        // Signature matches BoardPresenter.AssignResolved (Action<TaskCardView, Phase2CommandResult<TaskCardV2>>).
        void OnAssignResolved(TaskCardView card, Phase2CommandResult<TaskCardV2> result)
        {
            if (card == null || card.Data == null || card.Data.AgentId != agentId) return;
            if (result != null && result.Ok)
                SetState(AssignmentVisualState.ConfirmedAssignment);
            else
                SetState(AssignmentVisualState.InvalidAssignment);
        }

        public void SetState(AssignmentVisualState state)
        {
            currentState = state;
            ApplyVisualState(state);
        }

        public void ApplyVisualState(AssignmentVisualState state)
        {
            if (_mpb == null) _mpb = new MaterialPropertyBlock();

            if (state == AssignmentVisualState.Hidden)
            {
                if (haloRoot != null) haloRoot.SetActive(false);
                return;
            }

            if (haloRoot != null) haloRoot.SetActive(true);

            Color c = GetColorForState(state, out float emissionMul);
            ApplyTint(haloOuterRenderer, c, emissionMul);
            ApplyTint(receivePadStatusRenderer, c, emissionMul);
            ApplyTint(statePuckCoreRenderer, c, emissionMul);
        }

        void ApplyTint(Renderer r, Color c, float emissionMul)
        {
            if (r == null) return;
            r.GetPropertyBlock(_mpb);
            _mpb.SetColor(BaseColorId, c);
            _mpb.SetColor(ColorId, c);
            _mpb.SetColor(EmissionColorId, c * emissionMul);
            r.SetPropertyBlock(_mpb);
        }

        public static Color GetColorForState(AssignmentVisualState state, out float emissionMul)
        {
            switch (state)
            {
                case AssignmentVisualState.ValidTarget:
                    emissionMul = 0.45f;
                    return new Color(0.28f, 0.78f, 0.56f, 1f); // Soft mint-green
                case AssignmentVisualState.HoveredWorkstation:
                    emissionMul = 0.95f;
                    return new Color(0.25f, 0.85f, 0.96f, 1f); // Bright cyan highlight
                case AssignmentVisualState.SelectedAgent:
                    emissionMul = 0.75f;
                    return new Color(0.56f, 0.49f, 0.82f, 1f); // Lavender selection ring
                case AssignmentVisualState.PendingAssignment:
                    emissionMul = 0.85f;
                    return new Color(0.98f, 0.72f, 0.24f, 1f); // Warm amber pending pulse
                case AssignmentVisualState.ConfirmedAssignment:
                    emissionMul = 1.10f;
                    return new Color(0.22f, 0.84f, 0.45f, 1f); // Crisp emerald confirmation
                case AssignmentVisualState.InvalidAssignment:
                    emissionMul = 1.05f;
                    return new Color(0.92f, 0.26f, 0.24f, 1f); // Coral-red rejection
                case AssignmentVisualState.CompletedTask:
                    emissionMul = 0.90f;
                    return new Color(0.92f, 0.76f, 0.30f, 1f); // Warm gold completion
                default:
                    emissionMul = 0.20f;
                    return new Color(0.65f, 0.68f, 0.72f, 1f);
            }
        }
    }

    /// <summary>
    /// Optional 3D mascot feedback indicator group (Section 8).
    /// Mounts above Head_Attach or at a workstation corner without modifying character rigs.
    /// Can be driven explicitly via SetFeedback() or bound to SessionController reducer events.
    /// </summary>
    public class MascotFeedbackBadgeGroup : MonoBehaviour
    {
        public string agentId = AgentIds.Researcher;
        public SessionController sessionController;

        [Header("Badge Objects")]
        public GameObject listeningBadge;
        public GameObject speakingBadge;
        public GameObject thinkingBadge;
        public GameObject needsAttentionBadge;
        public GameObject errorBadge;
        public GameObject successBadge;

        [Header("State")]
        public MascotFeedbackState currentState = MascotFeedbackState.None;
        public bool gentleBob = true;
        public float bobAmplitude = 0.004f;
        public float bobFrequency = 2.2f;

        Vector3 _baseLocalPos;
        bool _capturedBase;
        bool _bound;

        void Awake()
        {
            _baseLocalPos = transform.localPosition;
            _capturedBase = true;
            ApplyState(currentState);
        }

        void OnEnable()
        {
            if (!_capturedBase) { _baseLocalPos = transform.localPosition; _capturedBase = true; }
            ApplyState(currentState);
        }

        void Update()
        {
            if (!_bound && sessionController != null && sessionController.Reducer != null)
            {
                sessionController.Reducer.AgentChanged += OnAgentChanged;
                _bound = true;
            }

            if (gentleBob && currentState != MascotFeedbackState.None)
            {
                float dy = Mathf.Sin(Time.time * bobFrequency) * bobAmplitude;
                transform.localPosition = _baseLocalPos + new Vector3(0f, dy, 0f);
            }
        }

        void OnDisable()
        {
            if (_bound && sessionController != null && sessionController.Reducer != null)
            {
                sessionController.Reducer.AgentChanged -= OnAgentChanged;
            }
            _bound = false;
        }

        // The reducer raises AgentChanged(AgentView) for every applied agent.status_changed.
        void OnAgentChanged(AgentView agent)
        {
            if (agent == null || agent.AgentId != agentId) return;
            string s = (agent.State ?? "").ToLowerInvariant();
            if (s == AgentStates.Waiting || s == AgentStates.Approaching)
                SetFeedback(MascotFeedbackState.NeedsAttention);
            else if (s == AgentStates.Researching || s == AgentStates.Coding || s == AgentStates.Testing)
                SetFeedback(MascotFeedbackState.Thinking);
            else if (s == AgentStates.Reporting)
                SetFeedback(MascotFeedbackState.Speaking);
            else if (s == AgentStates.Complete)
                SetFeedback(MascotFeedbackState.Success);
            else if (s == AgentStates.Error)
                SetFeedback(MascotFeedbackState.Error);
            else
                SetFeedback(MascotFeedbackState.None);
        }

        public void SetFeedback(MascotFeedbackState state)
        {
            currentState = state;
            ApplyState(state);
        }

        public void ApplyState(MascotFeedbackState state)
        {
            if (listeningBadge != null)      listeningBadge.SetActive(state == MascotFeedbackState.Listening);
            if (speakingBadge != null)       speakingBadge.SetActive(state == MascotFeedbackState.Speaking);
            if (thinkingBadge != null)       thinkingBadge.SetActive(state == MascotFeedbackState.Thinking);
            if (needsAttentionBadge != null) needsAttentionBadge.SetActive(state == MascotFeedbackState.NeedsAttention);
            if (errorBadge != null)          errorBadge.SetActive(state == MascotFeedbackState.Error);
            if (successBadge != null)        successBadge.SetActive(state == MascotFeedbackState.Success);
        }
    }
}
