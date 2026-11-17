using TMPro;
using UnityEngine;
using MuseDesk.Art;
using MuseDesk.Models;
using MuseDesk.Networking;

namespace MuseDesk.Workspace
{
    /// <summary>
    /// A small nameplate on the front edge of an agent's desk: the role on the first line and what
    /// the agent is doing, in plain words, on the second. It replaces the floating status panel
    /// and the floating name tag.
    ///
    /// The words come only from session reducer state (agent.status_changed, plus whether a
    /// decision from this agent is waiting for the user) through the mapping shared with
    /// WorkstationStatusDisplay. The text is rebuilt only when an event changes it; nothing here
    /// runs per frame except a short scale pulse that marks a change. Presentation only.
    /// </summary>
    public class AgentNameplate : MonoBehaviour
    {
        public SessionController controller;
        [Tooltip("Backend agent id this plate mirrors: researcher / developer / tester.")]
        public string agentId = AgentIds.Researcher;
        [Tooltip("Role name printed on the first line.")]
        public string roleName = "Researcher";

        [Header("Plate")]
        public TMP_Text roleText;
        public TMP_Text activityText;
        [Tooltip("Scale of the plate when shown (used when the builder saved it hidden).")]
        public Vector3 shownScale = Vector3.one;

        readonly ScaleEase _ease = new ScaleEase();
        bool _bound;
        bool _decisionPending;
        string _state = AgentStates.Idle;
        string _progress;
        string _line;
        WorkstationStatusDisplay.Activity _activity = WorkstationStatusDisplay.Activity.Idle;
        bool _rendered;

        /// <summary>The agent's own progress line is printed instead of the generic activity words
        /// when it is at most this many characters (the plate holds about that many at 5.5 mm).</summary>
        public const int MaxProgressChars = 28;

        [Header("Turn toward the viewer")]
        [Tooltip("Owner (worn rc15): plates should tilt naturally toward the user instead of sitting at a fixed angle. The plate " +
                 "turns (yaw) and tilts (pitch) toward the head, damped and clamped around its rest pose.")]
        public bool faceViewer = true;
        [Range(0f, 90f)] public float maxYaw = 60f;
        [Range(0f, 60f)] public float maxPitch = 35f;
        [Tooltip("How quickly it follows (1/s); low = lazy and natural.")]
        public float followRate = 4f;

        Quaternion _rest;
        bool _restSet;
        public bool FacingActive { get; private set; }

        /// <summary>The plate's local rotation that shows its readable face (the side opposite its forward) to a viewer
        /// in direction <paramref name="toViewerLocal"/> (parent space), clamped to ±maxYaw / ±maxPitch around the rest
        /// yaw (pitch is measured from level). Pure: used by the plate every frame and by EditMode tests.</summary>
        public static Quaternion TiltToward(Quaternion rest, Vector3 toViewerLocal, float maxYaw, float maxPitch)
        {
            if (toViewerLocal.sqrMagnitude < 1e-8f) return rest;
            Vector3 away = -toViewerLocal.normalized;
            float restYaw = rest.eulerAngles.y;
            float yaw = Mathf.Atan2(away.x, away.z) * Mathf.Rad2Deg;
            float dYaw = Mathf.Clamp(Mathf.DeltaAngle(restYaw, yaw), -maxYaw, maxYaw);
            float flat = new Vector2(away.x, away.z).magnitude;
            float pitch = -Mathf.Atan2(away.y, flat) * Mathf.Rad2Deg;    // + = forward tips down = face tips up to a viewer above
            pitch = Mathf.Clamp(pitch, -maxPitch, maxPitch);
            return Quaternion.Euler(pitch, restYaw + dYaw, 0f);
        }

        void LateUpdate()
        {
            FacingActive = false;
            if (!faceViewer) return;
            if (!_restSet) { _rest = transform.localRotation; _restSet = true; }
            Camera cam = Camera.main;
            if (cam == null) return;
            Transform parent = transform.parent;
            Vector3 to = cam.transform.position - transform.position;
            if (parent != null) to = parent.InverseTransformDirection(to);
            Quaternion target = TiltToward(_rest, to, maxYaw, maxPitch);
            float k = 1f - Mathf.Exp(-Mathf.Max(0.1f, followRate) * Mathf.Min(Time.deltaTime, 0.1f));
            transform.localRotation = Quaternion.Slerp(transform.localRotation, target, k);
            FacingActive = true;
        }

        void Awake()
        {
            _rest = transform.localRotation; _restSet = true;
            _ease.Init(transform, ScaleEase.ResolveFull(transform, shownScale), 0.2f, true);
            if (roleText != null && roleText.text != roleName) roleText.text = roleName;
            TintRoleStrips();
        }

        // The role strip(s) on the plate (the builder-made "Strip" and the art skin's "RoleStrip_Art") take the role colour
        // from the design token through a property block (competition build: role colours are soft pastels).
        void TintRoleStrips()
        {
            var block = new MaterialPropertyBlock();
            Color c = OfficeTokens.RoleColor(agentId);
            var rends = GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < rends.Length; i++)
            {
                string n = rends[i].name;
                if (n == "Strip" || n == "RoleStrip_Art") OfficeTokens.Tint(rends[i], block, c);
            }
        }

        void OnEnable() { Render(false); }

        void Update()
        {
            if (!_bound && controller != null) Bind();
            _ease.Tick(Time.deltaTime);
        }

        void Bind()
        {
            var r = controller.Reducer;
            r.AgentChanged += OnAgentChanged;
            r.DecisionRequested += OnDecisionRequested;
            r.DecisionResolved += OnDecisionResolved;
            r.SessionReset += OnSessionReset;
            r.SnapshotLoaded += ProjectSnapshot;
            _bound = true;
            ProjectSnapshot();
        }

        void OnDisable()
        {
            if (!_bound || controller == null) return;
            var r = controller.Reducer;
            r.AgentChanged -= OnAgentChanged;
            r.DecisionRequested -= OnDecisionRequested;
            r.DecisionResolved -= OnDecisionResolved;
            r.SessionReset -= OnSessionReset;
            r.SnapshotLoaded -= ProjectSnapshot;
            _bound = false;
        }

        // State that predates the event stream (start-up, resume, the switch to live).
        void ProjectSnapshot()
        {
            var r = controller.Reducer;
            _state = AgentStates.Idle;
            _progress = null;
            var agents = r.Snapshot != null ? r.Snapshot.Agents : null;
            if (agents != null)
                for (int i = 0; i < agents.Count; i++)
                    if (agents[i] != null && agents[i].AgentId == agentId)
                    {
                        if (!string.IsNullOrEmpty(agents[i].State)) _state = agents[i].State;
                        _progress = agents[i].Progress;
                        break;
                    }
            DecisionView pending = r.FirstPendingDecision();
            _decisionPending = pending != null && pending.AgentId == agentId;
            Render(true);
        }

        void OnAgentChanged(AgentView agent)
        {
            if (agent == null || agent.AgentId != agentId) return;
            _state = string.IsNullOrEmpty(agent.State) ? _state : agent.State;
            _progress = agent.Progress;
            Render(true);
        }

        void OnDecisionRequested(string decisionId, EventEnvelope e)
        {
            // The agent that asks is named on the event; the researcher is the one who asks today.
            string asker = e != null && !string.IsNullOrEmpty(e.AgentId) ? e.AgentId : AgentIds.Researcher;
            if (asker != agentId) return;
            _decisionPending = true;
            Render(true);
        }

        void OnDecisionResolved(string decisionId, string state)
        {
            if (!_decisionPending) return;
            _decisionPending = false;
            Render(true);
        }

        void OnSessionReset()
        {
            _state = AgentStates.Idle;
            _progress = null;
            _decisionPending = false;
            Render(true);
        }

        /// <summary>
        /// The second line. The agent's own progress text (from agent.status_changed) when it has
        /// one that is short and plain enough for the plate; otherwise the shared activity words.
        /// Three states always keep their words, because the words are the message: idle, waiting
        /// for the user's decision, and an error.
        /// </summary>
        static string LineFor(WorkstationStatusDisplay.Activity activity, string progress)
        {
            string words = WorkstationStatusDisplay.Words(activity);
            if (activity == WorkstationStatusDisplay.Activity.Idle
                || activity == WorkstationStatusDisplay.Activity.WaitingForUser
                || activity == WorkstationStatusDisplay.Activity.Error)
                return words;
            if (string.IsNullOrEmpty(progress) || progress.Length > MaxProgressChars) return words;
            // Plain ASCII only: no rich-text tags, and nothing the font may not have.
            for (int i = 0; i < progress.Length; i++)
            {
                char c = progress[i];
                if (c < ' ' || c > '~' || c == '<' || c == '>') return words;
            }
            return progress;
        }

        void Render(bool pulse)
        {
            var activity = WorkstationStatusDisplay.Classify(_state, _decisionPending);
            string line = LineFor(activity, _progress);
            if (_rendered && activity == _activity && string.Equals(line, _line, System.StringComparison.Ordinal)) return;
            // A new activity gets the usual small swell; a new progress line within the same
            // activity (it changes more often) a smaller one.
            bool activityChanged = !_rendered || activity != _activity;
            _rendered = true;
            _activity = activity;
            _line = line;
            if (activityText != null)
            {
                activityText.text = line;
                // Colour only repeats what the words already say.
                activityText.color =
                    activity == WorkstationStatusDisplay.Activity.WaitingForUser ? OfficeTokens.Amber
                    : activity == WorkstationStatusDisplay.Activity.Error ? OfficeTokens.Coral
                    : OfficeTokens.OnPlate;
            }
            if (pulse) _ease.Pulse(activityChanged ? 0.05f : 0.03f, 0.28f);
        }
    }
}
