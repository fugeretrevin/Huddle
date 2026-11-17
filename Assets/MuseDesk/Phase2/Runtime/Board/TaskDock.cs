using System;
using System.Text;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using MuseDesk.Networking;
using MuseDesk.Phase2.Contracts;

namespace MuseDesk.Phase2.Board
{
    /// <summary>
    /// The compact task dock on the desk: the always-visible summary of the task board, so the full
    /// board can stay closed. It shows, in words only:
    ///   a one-line summary        "4 tasks   1 ready   1 needs you"
    ///   the most relevant task    the one running, else one waiting for the user, else the first
    ///                             card that is ready to run
    ///   a state line              what that task is doing, or the outcome of the last key press
    ///   the mode                  LIVE or SCRIPTED DEMO (honesty invariant: always shown)
    /// and has at most two keys:
    ///   Start   visible only when there is a ready executable card and no run in progress. It sends
    ///           the same assign-to-Researcher command a hand drop on the Researcher sends, through
    ///           BoardPresenter.RequestAssignAsync, so the gateway (server or labelled demo) stays the
    ///           authority. The dock shows "Starting", then the confirmed or refused outcome.
    ///   Board   opens / closes the full board (label reads "Close" while it is open).
    ///
    /// Everything shown comes from the Phase2Reducer projection (TaskChanged / SnapshotLoaded); the
    /// dock never invents progress. Text is rebuilt only when a reducer event or a key press changes
    /// it, never per frame.
    /// </summary>
    public class TaskDock : MonoBehaviour
    {
        [Header("Sources (wired by AgentScripts/BuildTaskDockV2)")]
        public BoardPresenter presenter;
        public BoardExpander board;
        [Tooltip("Optional. Its RunInProgress also hides Start when a run was started without a card.")]
        public SessionController session;

        [Header("Text")]
        public TMP_Text summaryLabel;
        public TMP_Text titleLabel;
        public TMP_Text stateLabel;
        public TMP_Text modeLabel;
        [Tooltip("Plate behind the mode words: amber for SCRIPTED DEMO, teal for LIVE (words always shown).")]
        public Renderer modeChip;

        [Header("Keys")]
        [Tooltip("The Start key. Hidden by scale unless a card is ready and nothing is running.")]
        public ScaleReveal startKey;
        [Tooltip("Label on the Board key: Board / Close.")]
        public TMP_Text boardKeyLabel;

        [Header("Colours")]
        public Color demoChipColor = new Color(0.878f, 0.627f, 0.188f);    // amber  #E0A030
        public Color liveChipColor = new Color(0.184f, 0.639f, 0.604f);    // teal   #2FA39A
        public Color startingChipColor = new Color(0.165f, 0.184f, 0.227f); // housing #2A2F3A
        public Color textColor = new Color(0.953f, 0.957f, 0.965f);        // on plate #F3F4F6
        // Outcome words are always said in full; the colour only reinforces them.
        public Color okColor = new Color(0.180f, 0.608f, 0.373f);          // success #2E9B5F
        public Color refusedColor = new Color(0.851f, 0.325f, 0.290f);     // coral   #D9534A

        /// <summary>The task the dock is describing right now, or null.</summary>
        public TaskCardV2 FocusTask { get; private set; }
        /// <summary>True while a Start press is waiting for the gateway's answer.</summary>
        public bool StartPending => _startPending;

        Phase2Reducer _reducer;
        bool _bound;
        bool _sessionHooked;
        bool _boardHooked;
        float _nextBindTry;

        bool _startPending;
        bool _noticeActive;
        float _noticeUntil;
        bool _dirty = true;

        readonly StringBuilder _sb = new StringBuilder(64);
        string _summaryShown, _titleShown, _stateShown, _modeShown, _boardKeyShown;
        Color _stateColorShown;
        bool _stateColorSet;
        int _modeShownKind = -1;         // 0 starting, 1 demo, 2 live
        MaterialPropertyBlock _block;
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int ColorId = Shader.PropertyToID("_Color");

        // ─────────────────────────── binding ───────────────────────────

        void OnEnable()
        {
            _dirty = true;
            if (board != null && !_boardHooked) { board.Changed += OnBoardChanged; _boardHooked = true; }
        }

        void OnDisable()
        {
            Unbind();
            if (_boardHooked && board != null) board.Changed -= OnBoardChanged;
            _boardHooked = false;
        }

        void TryBind()
        {
            if (presenter == null || !presenter.Ready || presenter.Reducer == null) return;
            _reducer = presenter.Reducer;
            _reducer.SnapshotLoaded += OnSnapshotLoaded;
            _reducer.TaskChanged += OnTaskChanged;
            presenter.AssignResolved += OnCommandResolved;
            if (session != null)
            {
                session.RunStateChanged += MarkDirty;
                // Which agent is working right now, from the session reducer's own agent states.
                var sr = session.Reducer;
                sr.AgentChanged += OnAgentChanged;
                sr.SessionReset += OnSessionReset;
                sr.SnapshotLoaded += OnSessionSnapshot;
                _sessionHooked = true;
                OnSessionSnapshot();
            }
            _bound = true;
            _dirty = true;
        }

        // ─────────────────────────── who is working ───────────────────────────

        string _activeAgentId;           // the agent whose latest reducer state is a working state, or null

        static bool IsWorkingState(string state)
            => state == MuseDesk.Models.AgentStates.Researching || state == MuseDesk.Models.AgentStates.Coding
            || state == MuseDesk.Models.AgentStates.Testing || state == MuseDesk.Models.AgentStates.Reporting;

        void OnAgentChanged(MuseDesk.Models.AgentView agent)
        {
            if (agent == null || string.IsNullOrEmpty(agent.AgentId)) return;
            if (IsWorkingState(agent.State))
            {
                if (_activeAgentId == agent.AgentId) return;
                _activeAgentId = agent.AgentId;
            }
            else
            {
                if (_activeAgentId != agent.AgentId) return;
                _activeAgentId = null;
            }
            _dirty = true;
        }

        void OnSessionReset()
        {
            _activeAgentId = null;
            _dirty = true;
        }

        // State that predates the event stream (start-up, resume, the switch to live).
        void OnSessionSnapshot()
        {
            _activeAgentId = null;
            var snap = session != null ? session.Reducer.Snapshot : null;
            var agents = snap != null ? snap.Agents : null;
            if (agents != null)
                for (int i = 0; i < agents.Count; i++)
                    if (agents[i] != null && IsWorkingState(agents[i].State)) { _activeAgentId = agents[i].AgentId; break; }
            _dirty = true;
        }

        /// <summary>The running line: names the agent that is working when the session reducer
        /// says one is; otherwise the general words.</summary>
        string RunningWords()
        {
            switch (_activeAgentId)
            {
                case Phase2AgentIds.Researcher: return "In progress: Researcher is on it";
                case Phase2AgentIds.Developer: return "In progress: Developer is on it";
                case Phase2AgentIds.Tester: return "In progress: Tester is on it";
                default: return "In progress: the agents are on it";
            }
        }

        void Unbind()
        {
            if (_bound)
            {
                if (_reducer != null)
                {
                    _reducer.SnapshotLoaded -= OnSnapshotLoaded;
                    _reducer.TaskChanged -= OnTaskChanged;
                }
                if (presenter != null) presenter.AssignResolved -= OnCommandResolved;
            }
            if (_sessionHooked && session != null)
            {
                session.RunStateChanged -= MarkDirty;
                var sr = session.Reducer;
                sr.AgentChanged -= OnAgentChanged;
                sr.SessionReset -= OnSessionReset;
                sr.SnapshotLoaded -= OnSessionSnapshot;
            }
            _sessionHooked = false;
            _activeAgentId = null;
            _bound = false;
            _reducer = null;
        }

        void MarkDirty() => _dirty = true;
        void OnTaskChanged(TaskCardV2 card) => _dirty = true;
        void OnCommandResolved(TaskCardView card, Phase2CommandResult<TaskCardV2> result) => _dirty = true;
        void OnBoardChanged(bool expanded) => _dirty = true;

        // A reload (demo replay, switch to live, resync) replaces every card: drop any stale notice.
        void OnSnapshotLoaded()
        {
            _noticeActive = false;
            _dirty = true;
        }

        void Update()
        {
            if (!_bound && Time.unscaledTime >= _nextBindTry)
            {
                _nextBindTry = Time.unscaledTime + 0.25f;
                TryBind();
            }
            if (_noticeActive && Time.unscaledTime >= _noticeUntil)
            {
                _noticeActive = false;
                _dirty = true;
            }
            if (!_dirty) return;
            _dirty = false;
            Refresh();
        }

        // ─────────────────────────── view ───────────────────────────

        void Refresh()
        {
            bool expanded = board != null && board.IsExpanded;
            SetText(boardKeyLabel, ref _boardKeyShown, expanded ? "Close" : "Board");

            if (!_bound || _reducer == null)
            {
                FocusTask = null;
                SetText(summaryLabel, ref _summaryShown, "Task board");
                SetText(titleLabel, ref _titleShown, "Starting");
                SetState("Connecting to the task board", textColor);
                SetMode(0);
                if (startKey != null) startKey.Set(false);
                return;
            }

            // One pass over the reducer's tasks: counts for the summary and the most relevant task.
            int total = 0, ready = 0, running = 0, needsYou = 0, done = 0;
            TaskCardV2 bestRunning = null, bestWaiting = null, bestReady = null, bestOther = null;
            foreach (var kv in _reducer.Tasks)
            {
                var c = kv.Value;
                if (c == null) continue;
                total++;
                if (c.TaskState == TaskStates.Running || c.TaskState == TaskStates.PauseRequested)
                {
                    running++;
                    if (Better(c, bestRunning)) bestRunning = c;
                }
                else if (c.TaskState == TaskStates.AwaitingApproval)
                {
                    needsYou++;
                    if (Better(c, bestWaiting)) bestWaiting = c;
                }
                else if (TaskCardView.IsReadyToRun(c))
                {
                    ready++;
                    if (Better(c, bestReady)) bestReady = c;
                }
                else
                {
                    if (TaskStateSets.IsTerminal(c.TaskState)) done++;
                    // Fallback when nothing is active: prefer a card a pipeline ran (its outcome).
                    if (bestOther == null || (c.Executable && !bestOther.Executable) ||
                        (c.Executable == bestOther.Executable && Better(c, bestOther)))
                        bestOther = c;
                }
            }

            TaskCardV2 focus = bestRunning != null ? bestRunning
                             : bestWaiting != null ? bestWaiting
                             : bestReady != null ? bestReady
                             : bestOther;
            FocusTask = focus;

            // Summary.
            _sb.Length = 0;
            if (total == 0) _sb.Append("No tasks yet");
            else
            {
                _sb.Append(total).Append(total == 1 ? " task" : " tasks");
                if (ready > 0) _sb.Append("   ").Append(ready).Append(" ready");
                if (running > 0) _sb.Append("   ").Append(running).Append(" running");
                if (needsYou > 0) _sb.Append("   ").Append(needsYou).Append(needsYou == 1 ? " needs you" : " need you");
                if (ready == 0 && running == 0 && needsYou == 0 && done > 0) _sb.Append("   ").Append(done).Append(" done");
            }
            SetText(summaryLabel, ref _summaryShown, _sb);

            // Title of the most relevant task.
            if (focus == null) SetText(titleLabel, ref _titleShown, "Nothing to do yet");
            else SetText(titleLabel, ref _titleShown, string.IsNullOrEmpty(focus.Title) ? "(untitled task)" : focus.Title);

            // State line: the outcome of the last key press while it is fresh, else the task's state.
            if (!_noticeActive)
                SetState(focus != null && focus.TaskState == TaskStates.Running ? RunningWords() : DockStateWords(focus), textColor);

            // Mode, from the snapshot the reducer loaded (the gateway's own value before that).
            string mode = _reducer.SourceMode;
            if (string.IsNullOrEmpty(mode) && presenter != null && presenter.Gateway != null) mode = presenter.Gateway.SourceMode;
            SetMode(mode == SourceModes.Live ? 2 : 1);

            // Start: only when a card is ready and nothing is running or being started.
            bool runInProgress = running > 0 || needsYou > 0 || (session != null && session.RunInProgress);
            if (startKey != null) startKey.Set(bestReady != null && !runInProgress && !_startPending);
        }

        // Higher priority first; ties broken by task id so the choice is stable between refreshes.
        static bool Better(TaskCardV2 candidate, TaskCardV2 current)
        {
            if (current == null) return true;
            if (candidate.Priority != current.Priority) return candidate.Priority > current.Priority;
            return string.CompareOrdinal(candidate.TaskId, current.TaskId) < 0;
        }

        /// <summary>What the dock says about a task, from its canonical state only.</summary>
        static string DockStateWords(TaskCardV2 d)
        {
            if (d == null) return "Open the board to add a task";
            switch (d.TaskState)
            {
                case TaskStates.Queued:
                    if (TaskCardView.IsReadyToRun(d)) return "Ready. Press Start to begin";
                    return d.Executable ? "Queued" : "Planned. No agent runs it yet";
                case TaskStates.Running: return "In progress: the agents are on it";
                case TaskStates.AwaitingApproval: return "Needs your decision on the paper";
                case TaskStates.Blocked: return "Waiting on another task";
                case TaskStates.PauseRequested: return "Pausing";
                case TaskStates.Paused: return "Paused. Resume it on the board";
                case TaskStates.Completed: return "Completed";
                case TaskStates.Failed: return "Failed. Nothing more will run";
                case TaskStates.Cancelled: return "Cancelled. Nothing more will run";
                default: return TaskCardView.StateWords(d);
            }
        }

        void SetMode(int kind)
        {
            if (kind == _modeShownKind) return;
            _modeShownKind = kind;
            SetText(modeLabel, ref _modeShown, kind == 2 ? "LIVE" : kind == 1 ? "SCRIPTED DEMO" : "STARTING");
            if (modeChip == null) return;
            Color c = kind == 2 ? liveChipColor : kind == 1 ? demoChipColor : startingChipColor;
            if (_block == null) _block = new MaterialPropertyBlock();
            modeChip.GetPropertyBlock(_block);
            _block.SetColor(BaseColorId, c);
            _block.SetColor(ColorId, c);
            modeChip.SetPropertyBlock(_block);
            // Dark ink on the amber / teal chip; light text on the neutral "starting" chip.
            if (modeLabel != null) modeLabel.color = kind == 0 ? textColor : new Color(0.122f, 0.145f, 0.188f);
        }

        void SetState(string text, Color color)
        {
            SetText(stateLabel, ref _stateShown, text);
            if (stateLabel == null) return;
            if (_stateColorSet && _stateColorShown == color) return;
            _stateColorSet = true;
            _stateColorShown = color;
            stateLabel.color = color;
        }

        static void SetText(TMP_Text label, ref string shown, string text)
        {
            if (label == null || text == shown) return;
            shown = text;
            label.text = text;
        }

        // Compares against what is shown first, so an unchanged summary allocates nothing.
        static void SetText(TMP_Text label, ref string shown, StringBuilder sb)
        {
            if (label == null) return;
            if (shown != null && shown.Length == sb.Length)
            {
                bool same = true;
                for (int i = 0; i < sb.Length; i++)
                    if (shown[i] != sb[i]) { same = false; break; }
                if (same) return;
            }
            shown = sb.ToString();
            label.text = shown;
        }

        void Notice(string text, Color color, float seconds)
        {
            _noticeActive = true;
            _noticeUntil = Time.unscaledTime + seconds;
            SetState(text, color);
        }

        // ─────────────────────────── keys ───────────────────────────

        /// <summary>Poke target for the Board key: open the full board, or close it.</summary>
        public void ToggleBoard()
        {
            if (board == null) return;
            if (board.IsExpanded && presenter != null && presenter.Ready && presenter.HeldCard != null)
            {
                Notice("Put the card down first", refusedColor, 3f);
                return;
            }
            board.Toggle();
            _dirty = true;
        }

        /// <summary>Poke target for the Start key: send the ready card to the Researcher, exactly as
        /// dropping it on the Researcher does. Does nothing while a Start is already pending.</summary>
        public void StartReadyTask()
        {
            if (_startPending) return;
            _ = StartAsync();
        }

        async Task StartAsync()
        {
            if (!_bound || _reducer == null || presenter == null || !presenter.Ready)
            {
                Notice("The task board is still loading", refusedColor, 3f);
                return;
            }

            // The same card Refresh offers: the highest-priority ready executable card.
            TaskCardV2 target = null;
            foreach (var kv in _reducer.Tasks)
            {
                var c = kv.Value;
                if (TaskCardView.IsReadyToRun(c) && Better(c, target)) target = c;
            }
            var view = target != null ? presenter.FindView(target.TaskId) : null;
            if (view == null)
            {
                Notice("No task is ready to start", refusedColor, 3f);
                _dirty = true;
                return;
            }

            _startPending = true;
            if (startKey != null) startKey.Set(false);
            Notice("Starting", textColor, 12f);

            Phase2CommandResult<TaskCardV2> result = null;
            try
            {
                // The presenter path a drop uses: pending on the card, one idempotent assign, the
                // card reconciled to the gateway's canonical answer. Nothing is shown as started
                // until the gateway has answered.
                result = await presenter.RequestAssignAsync(view, Phase2AgentIds.Researcher, presenter.StableKey(view.Data.TaskId, "assign", Phase2AgentIds.Researcher, "start"));
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[MuseDesk.P2] dock start failed: {ex.Message}");
            }
            if (this == null) return;                                  // scene torn down while waiting

            _startPending = false;
            if (result != null && result.Ok)
                Notice(BoardPresenter.DescribeConfirmation("assign", result.Value), okColor, 3f);
            else
                Notice("Not started: " + BoardPresenter.DescribeRefusal(result != null ? result.Error : null), refusedColor, 5f);
            _dirty = true;
        }
    }
}
