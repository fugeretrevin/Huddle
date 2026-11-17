using TMPro;
using UnityEngine;
using MuseDesk.Art;
using MuseDesk.Models;
using MuseDesk.Networking;
using MuseDesk.Workspace;
using AgentDto = MuseDesk.Models.AgentView;

namespace MuseDesk.Spatial
{
    /// <summary>
    /// A summary perch: a small stand at the near-user area that says what one agent is doing
    /// while the office is far away. It prints the agent's name, its activity in the same words
    /// the desk nameplate and the touch card use (WorkstationStatusDisplay.ActivityWords), its own
    /// latest progress line, and LIVE or SCRIPTED DEMO, with the agent's role colour on a strip.
    /// Everything comes from the session reducer's snapshot; nothing is invented, and an agent
    /// the reducer does not know is not shown at all.
    ///
    /// It deliberately does NOT move or copy the character. Only one visual of an agent may
    /// exist, and moving the real one across the room would fight AgentMovement, the delivery
    /// walk and the hand-off choreography; that is deferred (see the integration document).
    ///
    /// Opened by <see cref="SummonRequests.AskAgent"/> (one agent) or
    /// <see cref="SummonRequests.ShowLatestUpdate"/> (the agent the current attention level
    /// concerns, else the most recent one to report). Closes by itself after
    /// <see cref="openSeconds"/>, on session reset, and when the office is no longer remote.
    /// </summary>
    public class PerchController : MonoBehaviour
    {
        public SessionController controller;
        [Tooltip("Optional: tells ShowLatestUpdate which agent to show, and is acknowledged when it does.")]
        public AttentionController attention;

        [Header("Stand")]
        [Tooltip("The stand (NOT under WorkspaceRoot). Shown and hidden by scale.")]
        public Transform stand;
        public Vector3 standShownScale = Vector3.one;
        public TMP_Text nameText;
        public TMP_Text stateText;
        public TMP_Text progressText;
        [Tooltip("LIVE or SCRIPTED DEMO, in words.")]
        public TMP_Text modeText;
        [Tooltip("Strip tinted with the agent's role colour.")]
        public Renderer roleStrip;
        [Tooltip("Offset from the near-user point: x = to the user's right, y = up, z = away from the user.")]
        public Vector3 standOffset = new Vector3(0.21f, 0.0f, 0.03f);
        [Tooltip("Optional art: the AgentConversationPerch model the summary plate stands on. Only its unfold / fold clips are triggered; no agent is mounted on it. Switched off while the perch is closed.")]
        public AgentConversationPerchController artPerch;

        [Header("Behaviour")]
        [Tooltip("Seconds the perch stays open.")]
        public float openSeconds = 10f;
        [Tooltip("Allow the perch in Desk mode (off: the agents are right there).")]
        public bool allowInDeskMode = false;

        readonly ScaleEase _ease = new ScaleEase();
        MaterialPropertyBlock _block;
        SessionReducer _reducer;
        bool _bound, _eased, _open;
        string _agentId;
        float _closeAt;
        string _lastState, _lastProgress;
        bool _lastWaiting, _lastLive;

        /// <summary>True while the perch is open.</summary>
        public bool IsOpen => _open;
        /// <summary>The agent the perch is showing, or null.</summary>
        public string ShownAgentId => _open ? _agentId : null;

        void Awake()
        {
            _block = new MaterialPropertyBlock();
            if (stand != null)
                _ease.Init(stand, ScaleEase.ResolveFull(stand, standShownScale), 0.25f, false);
            _eased = true;
            if (artPerch != null && artPerch.gameObject.activeSelf) artPerch.gameObject.SetActive(false);
        }

        void OnEnable()
        {
            SummonRequests.AskAgent += Open;
            SummonRequests.ShowLatestUpdate += OpenLatest;
            SpatialWorkspaceState.Changed += OnSpatialChanged;
        }

        void OnDisable()
        {
            SummonRequests.AskAgent -= Open;
            SummonRequests.ShowLatestUpdate -= OpenLatest;
            SpatialWorkspaceState.Changed -= OnSpatialChanged;
            if (_bound && _reducer != null)
            {
                _reducer.AgentChanged -= OnAgentChanged;
                _reducer.DecisionRequested -= OnDecisionRequested;
                _reducer.DecisionResolved -= OnDecisionResolved;
                _reducer.SessionReset -= Close;
                _reducer.SnapshotLoaded -= Close;
            }
            _reducer = null;
            _bound = false;
            _open = false;
        }

        void Update()
        {
            if (!_bound && controller != null) Bind();
            _ease.Tick(Time.unscaledDeltaTime);
            if (_open && Time.unscaledTime >= _closeAt) Close();
            // Closed and fully hidden: the model (and its animator) is switched off.
            if (artPerch != null && !_open && !_ease.Shown && !_ease.Animating && artPerch.gameObject.activeSelf)
                artPerch.gameObject.SetActive(false);
        }

        void Bind()
        {
            _reducer = controller.Reducer;
            _reducer.AgentChanged += OnAgentChanged;
            _reducer.DecisionRequested += OnDecisionRequested;
            _reducer.DecisionResolved += OnDecisionResolved;
            _reducer.SessionReset += Close;
            _reducer.SnapshotLoaded += Close;
            _bound = true;
        }

        // ── opening ──

        /// <summary>Show one agent's summary (agent id: researcher / developer / tester). Does
        /// nothing in Desk mode, or for an agent the reducer has not reported.</summary>
        public void Open(string agentId)
        {
            if (!_eased || stand == null || controller == null || string.IsNullOrEmpty(agentId)) return;
            if (!SpatialWorkspaceState.IsRemote && !allowInDeskMode) return;
            if (FindAgent(agentId) == null) return;          // never show an agent the backend did not report

            bool sameAgent = _open && _agentId == agentId;
            _agentId = agentId;
            _closeAt = Time.unscaledTime + openSeconds;
            Render(true);
            if (!_open)
            {
                _open = true;
                Place();
                if (artPerch != null)
                {
                    // Its animator restarts folded; the unfold clip then runs into the listening idle.
                    if (!artPerch.gameObject.activeSelf) artPerch.gameObject.SetActive(true);
                    artPerch.TriggerUnfoldArrive();
                }
                _ease.Show();
            }
            else if (!sameAgent) _ease.Pulse(0.05f, 0.25f);
        }

        /// <summary>Show the agent the current attention level concerns, else the most recent one
        /// to report, else the first agent the reducer knows. Looking at it acknowledges completed,
        /// error and quiet updates; a pending decision is never cleared this way.</summary>
        public void OpenLatest()
        {
            string id = null;
            if (attention != null)
            {
                id = attention.AgentId;
                if (string.IsNullOrEmpty(id)) id = attention.LatestAgentId;
            }
            if (string.IsNullOrEmpty(id) && controller != null)
            {
                var agents = controller.Reducer.Snapshot != null ? controller.Reducer.Snapshot.Agents : null;
                if (agents != null && agents.Count > 0 && agents[0] != null) id = agents[0].AgentId;
            }
            if (string.IsNullOrEmpty(id)) return;
            Open(id);
            if (_open && attention != null) attention.Acknowledge();
        }

        /// <summary>Close the perch (also called on session reset and when a snapshot is loaded).</summary>
        public void Close()
        {
            if (!_open) return;
            _open = false;
            if (artPerch != null && artPerch.gameObject.activeInHierarchy) artPerch.TriggerFoldReturn();
            _ease.Hide();
        }

        void OnSpatialChanged()
        {
            if (_open && !SpatialWorkspaceState.IsRemote && !allowInDeskMode) Close();
        }

        // ── reducer events: keep the open perch current, rebuild text only when something changed ──

        void OnAgentChanged(AgentDto agent)
        {
            if (_open && agent != null && agent.AgentId == _agentId) Render(false);
        }

        void OnDecisionRequested(string decisionId, EventEnvelope e) { if (_open) Render(false); }
        void OnDecisionResolved(string decisionId, string state) { if (_open) Render(false); }

        AgentDto FindAgent(string agentId)
        {
            var snap = controller != null ? controller.Reducer.Snapshot : null;
            var agents = snap != null ? snap.Agents : null;
            if (agents == null) return null;
            for (int i = 0; i < agents.Count; i++)
                if (agents[i] != null && agents[i].AgentId == agentId) return agents[i];
            return null;
        }

        void Render(bool force)
        {
            AgentDto agent = FindAgent(_agentId);
            if (agent == null) { Close(); return; }

            DecisionView pending = controller.Reducer.FirstPendingDecision();
            bool waiting = pending != null && pending.AgentId == _agentId;
            bool live = controller.IsLive;
            if (!force && agent.State == _lastState && agent.Progress == _lastProgress
                && waiting == _lastWaiting && live == _lastLive) return;
            _lastState = agent.State; _lastProgress = agent.Progress; _lastWaiting = waiting; _lastLive = live;

            if (nameText != null)
                nameText.text = string.IsNullOrEmpty(agent.DisplayName) ? RoleName(_agentId) : agent.DisplayName;
            if (stateText != null)
            {
                // The same words as the desk nameplate and the touch card (one mapping everywhere).
                stateText.text = WorkstationStatusDisplay.ActivityWords(agent.State, waiting);
                stateText.color = waiting ? OfficeTokens.Amber : OfficeTokens.OnPlate;
            }
            if (progressText != null)
                progressText.text = string.IsNullOrEmpty(agent.Progress) ? "" : agent.Progress;
            if (modeText != null) modeText.text = live ? "" : "SCRIPTED DEMO";      // rc20 (owner): no LIVE tag
            if (roleStrip != null) OfficeTokens.Tint(roleStrip, _block, RoleColor(_agentId));
        }

        void Place()
        {
            if (!RemoteNearUser.TryGetFrame(out Vector3 origin, out Vector3 right, out Vector3 away)) return;
            Vector3 pos = origin + right * standOffset.x + Vector3.up * standOffset.y + away * standOffset.z;
            stand.SetPositionAndRotation(pos, RemoteNearUser.FacingHead(pos, stand.rotation));
        }

        /// <summary>The role colours of the design system (Researcher blue, Developer green, Tester amber).</summary>
        public static Color RoleColor(string agentId)
        {
            if (agentId == AgentIds.Researcher || agentId == AgentIds.Developer || agentId == AgentIds.Tester) return OfficeTokens.RoleColor(agentId);
            return OfficeTokens.Neutral;
        }

        static string RoleName(string agentId)
        {
            if (agentId == AgentIds.Researcher) return "Researcher";
            if (agentId == AgentIds.Developer) return "Developer";
            if (agentId == AgentIds.Tester) return "Tester";
            return agentId;
        }
    }
}
