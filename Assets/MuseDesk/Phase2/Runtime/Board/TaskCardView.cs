using System;
using System.Reflection;
using TMPro;
using UnityEngine;
using MuseDesk.Phase2.Contracts;

namespace MuseDesk.Phase2.Board
{
    /// <summary>
    /// A single physical task card on the board (P2-01). Pure view + data holder: it renders a
    /// TaskCardV2 as a title, one state line in plain words, a role chip and the LIVE / SCRIPTED DEMO
    /// mode words (always shown: honesty invariant), and exposes grab state. Priority appears only
    /// when it is not the default, as a word. The id and revision are not on the card face. The drag
    /// state machine lives in TaskDragController; the authoritative task truth always comes from the
    /// Phase2Reducer, never from this view.
    ///
    /// Grab state is read by REFLECTION off the project's Oculus.Interaction.Grabbable
    /// (SelectingPointsCount), mirroring the existing MuseDesk.Workspace.GrabbableRigidbody /
    /// GrabReadout pattern, so this runtime assembly keeps NO hard ISDK dependency and cannot break
    /// if the package version shifts. The Grabbable + HandGrabInteractable components are added to
    /// the card prefab by the integrator (Editor builder / ISDK Quick Actions) and assigned here.
    /// </summary>
    public class TaskCardView : MonoBehaviour
    {
        [Header("Text (TMP)")]
        public TMP_Text titleLabel;
        public TMP_Text idRevisionLabel;   // no longer on the card face: kept empty (reference detail)
        public TMP_Text roleLabel;         // assignee role or "Unassigned"
        public TMP_Text priorityLabel;     // a word ("High"), only when the priority is not the default
        public TMP_Text stateLabel;        // one state line in plain words
        public TMP_Text sourceLabel;       // "LIVE" / "SCRIPTED DEMO": always visible (invariant #1)

        [Header("Optional visuals")]
        [Tooltip("Shown while the card is inspected/expanded (pinch to expand). Optional.")]
        public GameObject detailsRoot;
        [Tooltip("Status marker renderer tinted by task state. Optional.")]
        public Renderer statusMarker;
        [Tooltip("Colour the role and priority words to suit the role strip under them (light text, or " +
                 "dark text on the Tester's amber). Set by the builder only when the strip is role-tinted.")]
        public bool tintHeaderText;

        [Header("Interaction (set by the Editor builder / ISDK Quick Actions)")]
        [Tooltip("The Oculus.Interaction.Grabbable on this card. Read by reflection only.")]
        public Component grabbable;

        // Resting slot on the board (set by BoardPresenter during layout). Stored as a parent + local
        // pose rather than a Transform so ReturnHome is stable even while the card itself is moved by
        // the hand. Never self-reference the card's own transform here.
        Transform _homeParent;
        Vector3 _homeLocalPos;
        Quaternion _homeLocalRot = Quaternion.identity;
        bool _hasHome;

        /// <summary>Raised by the accessible no-drag fallback (poke 'Assign to X'). The presenter
        /// turns this into an idempotent assign command exactly like a physical drop would.</summary>
        public event Action<TaskCardView, string> AssignRequested;   // (card, targetAgentId)

        /// <summary>Poke the pause/resume control: the presenter pauses a running task or resumes a
        /// paused one (idempotent, canonical-reconciled, exactly like assign).</summary>
        public event Action<TaskCardView> PauseResumeRequested;
        /// <summary>Poke the priority chip: the presenter cycles the task priority 0→1→2→3→0.</summary>
        public event Action<TaskCardView> PriorityCycleRequested;

        public TaskCardV2 Data { get; private set; }
        public string TaskId => Data?.TaskId;
        public bool IsExpanded { get; private set; }

        Func<int> _selCount;
        bool _selResolved;

        // Transient command feedback shown in the state line ("Sending…", "Assigned to Developer",
        // "Running: pause it first"), then the canonical state text returns.
        float _noticeUntil;
        bool _noticeActive;
        Color _stateBaseColor;
        bool _stateColorCaptured;
        // Dark enough to read on the paper-coloured card face (nothing lighter than "ink soft" there):
        // ink soft while waiting, a dark green for confirmed, a dark coral for refused. The words
        // carry the meaning; the colour only reinforces it.
        static readonly Color NoticePending = new Color(0.294f, 0.333f, 0.388f);   // #4B5563
        static readonly Color NoticeOk = new Color(0.10f, 0.42f, 0.24f);
        static readonly Color NoticeError = new Color(0.66f, 0.18f, 0.15f);
        static readonly Color RoleTextOnDark = new Color(0.953f, 0.957f, 0.965f);  // #F3F4F6
        static readonly Color RoleTextOnLight = new Color(0.122f, 0.145f, 0.188f); // #1F2530

        /// <summary>Raised whenever any card shows command feedback: (card, words, outcome).
        /// outcome is null while the command is pending, true once the gateway confirmed it, false
        /// when it was refused. Lets the card toolbar, the assignment pads and the task dock repeat
        /// the same words the card shows. Subscribers must unsubscribe in OnDisable.</summary>
        public static event Action<TaskCardView, string, bool?> NoticeShown;

        /// <summary>The command was sent and the backend has not answered yet.</summary>
        public void ShowPending(string text = "Sending")
        {
            Notice(text, NoticePending, 8f);
            RaiseNotice(text, null);
        }

        /// <summary>The backend answered: confirmed (ok) or rejected with a reason.</summary>
        public void ShowResult(bool ok, string text)
        {
            Notice(text, ok ? NoticeOk : NoticeError, ok ? 2f : 4f);
            RaiseNotice(text, ok);
        }

        void RaiseNotice(string text, bool? outcome)
        {
            var handler = NoticeShown;
            if (handler == null || string.IsNullOrEmpty(text)) return;
            try { handler(this, text, outcome); }
            catch (Exception ex) { Debug.LogError($"[MuseDesk.P2] card notice listener error: {ex.Message}"); }
        }

        void Notice(string text, Color color, float seconds)
        {
            if (stateLabel == null || string.IsNullOrEmpty(text)) return;
            if (!_stateColorCaptured) { _stateBaseColor = stateLabel.color; _stateColorCaptured = true; }
            stateLabel.text = Truncate(text, 40);
            stateLabel.color = color;
            _noticeUntil = Time.unscaledTime + seconds;
            _noticeActive = true;
        }

        void Update()
        {
            if (!_noticeActive || Time.unscaledTime < _noticeUntil) return;
            _noticeActive = false;
            if (stateLabel != null)
            {
                if (_stateColorCaptured) stateLabel.color = _stateBaseColor;
                stateLabel.text = Data != null ? StateLine(Data) : "";
            }
        }

        void OnDisable()
        {
            // Pooled cards must not come back showing another task's notice.
            _noticeActive = false;
            if (stateLabel != null && _stateColorCaptured) stateLabel.color = _stateBaseColor;
        }

        /// <summary>True while at least one hand is selecting (grabbing) this card. Reflection read of
        /// Grabbable.SelectingPointsCount &gt; 0 — the same property the existing workspace scripts use.</summary>
        public bool IsHeld
        {
            get
            {
                if (grabbable == null) return false;
                if (!_selResolved)
                {
                    // VERIFY: property name 'SelectingPointsCount' matches the installed
                    // Oculus.Interaction.Grabbable (confirmed in use by GrabbableRigidbody.cs / GrabReadout.cs).
                    // Bound once to a typed delegate: PropertyInfo.GetValue boxed an int per card per frame.
                    var prop = grabbable.GetType().GetProperty("SelectingPointsCount",
                        BindingFlags.Instance | BindingFlags.Public);
                    var getter = prop != null ? prop.GetGetMethod() : null;
                    if (getter != null && prop.PropertyType == typeof(int))
                        _selCount = (Func<int>)Delegate.CreateDelegate(typeof(Func<int>), grabbable, getter);
                    _selResolved = true;
                }
                if (_selCount == null) return false;
                try { return _selCount() > 0; }
                catch { return false; }
            }
        }

        public void SetData(TaskCardV2 data)
        {
            Data = data;
            Render();
        }

        void Render()
        {
            if (Data == null) return;
            // The card face: title, one state line, role chip, mode words. Nothing else by default.
            SetText(titleLabel, Truncate(Data.Title, 48));
            SetText(idRevisionLabel, "");                        // id / revision are off the card face
            SetText(roleLabel, RoleWord(Data.AgentId));
            // Priority only when it is not the default, and as a word.
            SetText(priorityLabel, Data.Priority == DefaultPriority ? "" : PriorityWord(Data.Priority));
            if (tintHeaderText)
            {
                // Both sit on the role strip. The Tester's strip is amber, where dark text reads;
                // the other strips are dark enough for light text.
                Color want = Data.AgentId == Phase2AgentIds.Tester ? RoleTextOnLight : RoleTextOnDark;
                if (roleLabel && roleLabel.color != want) roleLabel.color = want;
                if (priorityLabel && priorityLabel.color != want) priorityLabel.color = want;
            }
            if (!_noticeActive) SetText(stateLabel, StateLine(Data));
            SetText(sourceLabel, ModeWords(Data.SourceMode));
            if (statusMarker) ApplyStateTint();
        }

        static void SetText(TMP_Text label, string text)
        {
            if (label == null) return;
            if (text == null) text = "";
            if (label.text != text) label.text = text;
        }

        MaterialPropertyBlock _tintBlock;

        void ApplyStateTint()
        {
            // Non-authoritative color hint; never a substitute for the text marker.
            Color c = Data.TaskState switch
            {
                TaskStates.Completed => new Color(0.30f, 0.70f, 0.35f),
                TaskStates.Failed => new Color(0.80f, 0.30f, 0.30f),
                TaskStates.Running => new Color(0.30f, 0.55f, 0.85f),
                TaskStates.AwaitingApproval => new Color(0.90f, 0.70f, 0.20f),
                TaskStates.Blocked => new Color(0.55f, 0.55f, 0.60f),
                TaskStates.Paused or TaskStates.PauseRequested => new Color(0.65f, 0.60f, 0.35f),
                _ => new Color(0.75f, 0.75f, 0.78f),
            };
            // Property block, not renderer.material: that created a material instance per card.
            if (_tintBlock == null) _tintBlock = new MaterialPropertyBlock();
            statusMarker.GetPropertyBlock(_tintBlock);
            _tintBlock.SetColor("_BaseColor", c);
            _tintBlock.SetColor("_Color", c);
            statusMarker.SetPropertyBlock(_tintBlock);
        }

        public void SetExpanded(bool expanded)
        {
            IsExpanded = expanded;
            if (detailsRoot) detailsRoot.SetActive(expanded);
        }

        public void ToggleExpanded() => SetExpanded(!IsExpanded);

        /// <summary>Record the card's resting slot in its region (parent + local pose).</summary>
        public void SetHome(Transform parent, Vector3 localPos, Quaternion localRot)
        {
            _homeParent = parent;
            _homeLocalPos = localPos;
            _homeLocalRot = localRot;
            _hasHome = true;
        }

        /// <summary>Snap back to the recorded resting slot (used on an invalid drop / 409 / cancel).</summary>
        public void ReturnHome()
        {
            if (!_hasHome) return;
            if (_homeParent != null && transform.parent != _homeParent)
                transform.SetParent(_homeParent, worldPositionStays: false);
            transform.localPosition = _homeLocalPos;
            transform.localRotation = _homeLocalRot;
        }

        // ─── Accessible no-drag fallback (hands-only poke). Wire these to poke buttons on the
        //     expanded card. They raise AssignRequested; the presenter issues the same idempotent
        //     assign command a physical drop would (master §4 requirement). ───
        public void PokeAssignResearcher() => AssignRequested?.Invoke(this, Phase2AgentIds.Researcher);
        public void PokeAssignDeveloper() => AssignRequested?.Invoke(this, Phase2AgentIds.Developer);
        public void PokeAssignTester() => AssignRequested?.Invoke(this, Phase2AgentIds.Tester);

        /// <summary>Poke target: pause a running task / resume a paused one (presenter decides which).</summary>
        public void PokePauseResume() => PauseResumeRequested?.Invoke(this);
        /// <summary>Poke target: cycle this task's priority.</summary>
        public void PokeCyclePriority() => PriorityCycleRequested?.Invoke(this);

        // ─── helpers ───
        // ASCII only (the UI font rule): three dots, not an ellipsis character.
        static string Truncate(string s, int max)
            => string.IsNullOrEmpty(s) ? "" : (s.Length <= max ? s : s.Substring(0, max - 3) + "...");
        static string Capitalize(string s)
            => string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);

        // What the card can honestly say about itself, in one short line.
        static string StateLine(TaskCardV2 d) => StateWords(d);

        // ─── words shared by the card, the card toolbar and the task dock ───

        /// <summary>The priority new cards get (BoardPresenter / Phase2Bootstrap create with 1). A card
        /// at this priority shows no priority word.</summary>
        public const int DefaultPriority = 1;

        /// <summary>Priority 0..3 as a word.</summary>
        public static string PriorityWord(int priority)
        {
            switch (Mathf.Clamp(priority, 0, 3))
            {
                case 0: return "Low";
                case 2: return "High";
                case 3: return "Urgent";
                default: return "Normal";
            }
        }

        /// <summary>The assignee as a word: "Researcher", "Developer", "Tester" or "Unassigned".</summary>
        public static string RoleWord(string agentId)
        {
            switch (agentId)
            {
                case Phase2AgentIds.Researcher: return "Researcher";
                case Phase2AgentIds.Developer: return "Developer";
                case Phase2AgentIds.Tester: return "Tester";
                default: return string.IsNullOrEmpty(agentId) ? "Unassigned" : Capitalize(agentId);
            }
        }

        /// <summary>"SCRIPTED DEMO" or "LIVE" for a source_mode value. Anything that is not
        /// explicitly the demo is reported as LIVE only when it says "live"; an unknown or missing
        /// value is shown as scripted, so nothing is ever presented as live by default.</summary>
        public static string ModeWords(string sourceMode)
            => sourceMode == SourceModes.Live ? "LIVE" : "SCRIPTED DEMO";

        /// <summary>True for a queued executable card whose run has not been started: the card that
        /// Start (or a drop on the Researcher) launches.</summary>
        public static bool IsReadyToRun(TaskCardV2 d)
            => d != null && d.Executable && d.TaskState == TaskStates.Queued && string.IsNullOrEmpty(d.RunTaskId);

        /// <summary>Local hint for showing a Pause action (the gateway still decides). Waiting cards
        /// can be paused; a card whose pipeline run is in progress has no safe pause point.</summary>
        public static bool IsPausable(TaskCardV2 d)
            => d != null && (d.TaskState == TaskStates.Queued || d.TaskState == TaskStates.Blocked);

        /// <summary>Local hint for showing a Resume action.</summary>
        public static bool IsResumable(TaskCardV2 d)
            => d != null && (d.TaskState == TaskStates.Paused || d.TaskState == TaskStates.PauseRequested);

        /// <summary>One state line in plain words for a card, from its canonical task state.</summary>
        public static string StateWords(TaskCardV2 d)
        {
            if (d == null) return "";
            switch (d.TaskState)
            {
                case TaskStates.Queued:
                    if (IsReadyToRun(d)) return "Ready for Researcher";
                    if (d.Executable) return "Queued";
                    // A planned card: nothing runs it, and the card says so.
                    switch (d.AgentId)
                    {
                        case Phase2AgentIds.Researcher: return "Planned for Researcher";
                        case Phase2AgentIds.Developer: return "Planned for Developer";
                        case Phase2AgentIds.Tester: return "Planned for Tester";
                        default: return "Planned";
                    }
                case TaskStates.Running: return "In progress";
                case TaskStates.AwaitingApproval: return "Needs your decision";
                case TaskStates.Blocked: return "Waiting on a task";
                case TaskStates.PauseRequested: return "Pausing";
                case TaskStates.Paused: return "Paused";
                case TaskStates.Completed: return "Completed";
                case TaskStates.Failed: return "Failed";
                case TaskStates.Cancelled: return "Cancelled";
                default: return string.IsNullOrEmpty(d.TaskState) ? "" : Capitalize(d.TaskState);
            }
        }
    }
}
