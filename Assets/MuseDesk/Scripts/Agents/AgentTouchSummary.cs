using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using MuseDesk.Networking;
using MuseDesk.Phase2;
using MuseDesk.Phase2.Contracts;

namespace MuseDesk.Agents
{
    /// <summary>
    /// Touch an agent to ask what it is doing. When a fingertip rests on the agent's head for a
    /// moment, a small card appears above it with the agent's name, its real current state and
    /// progress line, the task card it owns on the board (if any), and whether this is live work or
    /// the scripted demo. The card closes by itself a few seconds after the hand leaves.
    ///
    /// It deliberately does NOT use a grab: the agent is not a grabbable object, so this cannot
    /// compete with picking up the paper, a stamp or a task card. Everything shown is read from
    /// state the backend (or the labelled demo) already reported; nothing is invented.
    /// </summary>
    public class AgentTouchSummary : MonoBehaviour
    {
        public AgentView view;
        public SessionController controller;
        [Tooltip("Board bootstrap, to name the task this agent owns. Auto-found if empty.")]
        public Phase2Bootstrap board;

        [Header("Touch")]
        [Tooltip("Centre of the touch volume (the head). Falls back to a point above the agent root.")]
        public Transform touchPoint;
        [Tooltip("Fingertip distance that counts as touching (m).")]
        public float touchRadius = 0.075f;
        [Tooltip("Seconds the fingertip must stay before the card opens (avoids opening on a pass-by).")]
        public float dwellSeconds = 0.25f;
        [Tooltip("Seconds the card stays after the hand leaves.")]
        public float lingerSeconds = 4f;

        [Header("Card")]
        public GameObject cardRoot;
        public TMP_Text cardText;

        float _dwell, _hideAt;
        bool _open;
        string _lastState, _lastProgress;
        readonly StringBuilder _sb = new StringBuilder(160);

        void Awake()
        {
            if (view == null) view = GetComponent<AgentView>();
            if (cardRoot != null) cardRoot.SetActive(false);
        }

        void OnDisable()
        {
            _open = false; _dwell = 0f;
            if (cardRoot != null) cardRoot.SetActive(false);
        }

        void Update()
        {
            if (view == null || cardRoot == null) return;

            bool touching = false;
            IReadOnlyList<Transform> tips = HandFingertips.Get();
            if (tips.Count > 0)
            {
                Vector3 c = touchPoint != null ? touchPoint.position : transform.position + Vector3.up * 0.24f;
                float r2 = (_open ? touchRadius * 1.3f : touchRadius);     // hysteresis: easier to keep than to start
                r2 *= r2;
                for (int i = 0; i < tips.Count; i++)
                {
                    var t = tips[i];
                    if (t != null && (t.position - c).sqrMagnitude <= r2) { touching = true; break; }
                }
            }

            if (touching)
            {
                _dwell += Time.deltaTime;
                _hideAt = Time.time + lingerSeconds;
                if (!_open && _dwell >= dwellSeconds) Open();
            }
            else _dwell = 0f;

            if (_open)
            {
                if (Time.time >= _hideAt) { Close(); return; }
                // Keep it current while open, but only rebuild the text when something changed.
                if (view.State != _lastState || view.Progress != _lastProgress) Render();
            }
        }

        /// <summary>A fingertip held on an agent long enough to open its card. Used by first-use hints only.</summary>
        public static event System.Action<AgentTouchSummary> Touched;

        void Open()
        {
            _open = true;
            Touched?.Invoke(this);
            Render();
            cardRoot.SetActive(true);
            // The user asked this agent something: it looks up at them for a moment (then back to
            // the hand or its work, as AgentGaze decides).
            if (!_gazeLooked)
            {
                _gaze = view != null ? view.GetComponent<AgentGaze>() : GetComponent<AgentGaze>();
                _gazeLooked = true;
            }
            if (_gaze != null && _gaze.isActiveAndEnabled) _gaze.SetAttentionOnUser(1.4f, 0.85f);
        }

        AgentGaze _gaze;
        bool _gazeLooked;

        void Close()
        {
            _open = false;
            cardRoot.SetActive(false);
        }

        void Render()
        {
            _lastState = view.State;
            _lastProgress = view.Progress;
            if (cardText == null) return;

            bool live = controller != null && controller.IsLive;
            _sb.Clear();
            _sb.Append("<b>").Append(view.displayName).Append("</b>   <size=80%>")
               .Append(live ? "LIVE" : "SCRIPTED DEMO").Append("</size>\n");
            // Same words as the desk nameplate (one mapping for every surface).
            bool waitingOnUser = false;
            if (controller != null)
            {
                var pending = controller.Reducer.FirstPendingDecision();
                waitingOnUser = pending != null && pending.AgentId == view.agentId;
            }
            _sb.Append(MuseDesk.Art.WorkstationStatusDisplay.ActivityWords(view.State, waitingOnUser)).Append('\n');
            if (!string.IsNullOrEmpty(view.Progress))
                _sb.Append("<size=85%>").Append(view.Progress).Append("</size>\n");

            string task = OwnedTaskLine();
            _sb.Append("<size=80%>").Append(task ?? "No task card assigned.").Append("</size>");
            cardText.text = _sb.ToString();
        }

        // The card this agent owns on the board: prefer one that is active, else any assigned to it.
        string OwnedTaskLine()
        {
            if (board == null) board = FindAnyObjectByType<Phase2Bootstrap>();
            var reducer = board != null ? board.Reducer : null;
            if (reducer == null || reducer.Tasks == null) return null;

            TaskCardV2 best = null;
            foreach (var kv in reducer.Tasks)
            {
                var t = kv.Value;
                if (t == null || t.AgentId != view.agentId) continue;
                bool active = t.TaskState == TaskStates.Running || t.TaskState == TaskStates.AwaitingApproval;
                if (best == null || active) best = t;
                if (active) break;
            }
            if (best == null) return null;
            string title = string.IsNullOrEmpty(best.Title) ? "(untitled)" : best.Title;
            if (title.Length > 40) title = title.Substring(0, 37) + "...";
            string state = best.TaskState == TaskStates.AwaitingApproval ? "needs your decision"
                         : best.TaskState == TaskStates.Running ? "in progress"
                         : (best.TaskState ?? "").Replace('_', ' ');
            return "Task: " + title + " (" + state + ")";
        }
    }
}
