using System;
using System.Reflection;
using TMPro;
using UnityEngine;
using MuseDesk.Phase2.Contracts;

namespace MuseDesk.Phase2.Board
{
    /// <summary>
    /// A single physical task card on the board (P2-01). Pure view + data holder: it renders a
    /// TaskCardV2 (short title, status marker, role, compact priority, and a conspicuous live/demo
    /// source label) and exposes grab state. The drag state machine lives in TaskDragController;
    /// the authoritative task truth always comes from the Phase2Reducer, never from this view.
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
        public TMP_Text idRevisionLabel;   // e.g. "t2-ab12 · r3"
        public TMP_Text roleLabel;         // assignee role or "Unassigned"
        public TMP_Text priorityLabel;     // compact: "P2"
        public TMP_Text stateLabel;        // status marker text
        public TMP_Text sourceLabel;       // "LIVE" / "SCRIPTED DEMO" — always visible (invariant #1)

        [Header("Optional visuals")]
        [Tooltip("Shown while the card is inspected/expanded (pinch to expand). Optional.")]
        public GameObject detailsRoot;
        [Tooltip("Status marker renderer tinted by task state. Optional.")]
        public Renderer statusMarker;

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

        PropertyInfo _selProp;
        bool _selResolved;

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
                    _selProp = grabbable.GetType().GetProperty("SelectingPointsCount",
                        BindingFlags.Instance | BindingFlags.Public);
                    _selResolved = true;
                }
                if (_selProp == null) return false;
                try { return (int)_selProp.GetValue(grabbable) > 0; }
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
            if (titleLabel) titleLabel.text = Truncate(Data.Title, 48);
            if (idRevisionLabel) idRevisionLabel.text = $"{ShortId(Data.TaskId)} · r{Data.Revision}";
            if (roleLabel) roleLabel.text = string.IsNullOrEmpty(Data.AgentId) ? "Unassigned" : Capitalize(Data.AgentId);
            if (priorityLabel) priorityLabel.text = $"P{Mathf.Clamp(Data.Priority, 0, 3)}";
            if (stateLabel) stateLabel.text = PrettyState(Data.TaskState);
            if (sourceLabel) sourceLabel.text = Data.SourceMode == SourceModes.Demo ? "SCRIPTED DEMO" : "LIVE";
            if (statusMarker) ApplyStateTint();
        }

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
            if (statusMarker.material != null) statusMarker.material.color = c;
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
        static string Truncate(string s, int max)
            => string.IsNullOrEmpty(s) ? "" : (s.Length <= max ? s : s.Substring(0, max - 1) + "…");
        static string ShortId(string id)
            => string.IsNullOrEmpty(id) ? "?" : (id.Length <= 10 ? id : id.Substring(0, 10));
        static string Capitalize(string s)
            => string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);
        static string PrettyState(string s) => s switch
        {
            TaskStates.AwaitingApproval => "Needs me",
            TaskStates.PauseRequested => "Pausing…",
            null => "",
            _ => Capitalize(s ?? "")
        };
    }
}
