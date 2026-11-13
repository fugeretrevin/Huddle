using TMPro;
using UnityEngine;
using MuseDesk.Documents;
using MuseDesk.Models;
using MuseDesk.Networking;

namespace MuseDesk.Workspace
{
    /// <summary>
    /// First-use guidance as ONE short, self-dismissing line beside the mode plaque. It never blocks
    /// anything and never nags:
    ///   - each step is shown at most once per session;
    ///   - a step hides as soon as it has been done, or after about ten seconds;
    ///   - after the first full run (work delivered, a rejection, or a failed run) it retires and
    ///     does not come back, except through a deliberate action (<see cref="ShowTips"/>, or
    ///     <see cref="ShowStartHint"/> which the completion receipt calls after "Run again").
    ///
    /// Steps follow what has actually happened (session reducer events, the run state and whether
    /// the paper was taken), never a timer script, so the line is true in the scripted demo and
    /// against a live backend alike:
    ///   "Press Start on the task dock."  ->  "Take the paper from the Researcher."  ->
    ///   "Approve or Reject."  ->  done.
    /// </summary>
    public class FirstUseGuide : MonoBehaviour
    {
        public SessionController controller;
        [Tooltip("The decision paper, to know when the user has taken it.")]
        public HeldPaperController paper;
        public TMP_Text label;
        [Tooltip("The guide line (plate and text). Shown and hidden by scale.")]
        public GameObject root;
        [Tooltip("Legacy pulsing ring on the old Start key. Kept so older scenes still load; always hidden.")]
        public GameObject startHint;

        [Header("Behaviour")]
        [Tooltip("Seconds a step stays if the user does not act on it.")]
        public float stepSeconds = 10f;
        [Tooltip("Seconds after start-up before the first line appears (the workspace is still being placed).")]
        public float firstDelay = 1.5f;
        [Tooltip("Scale of the guide line when shown (used when the builder saved it hidden).")]
        public Vector3 shownScale = Vector3.one;

        enum Step { None = 0, Start = 1, TakePaper = 2, Decide = 3 }

        const string TextStart = "Press Start on the task dock.";
        const string TextTakePaper = "Take the paper from the Researcher.";
        const string TextDecide = "Approve or Reject.";

        readonly ScaleEase _ease = new ScaleEase();
        Step _step = Step.None;
        float _stepTime;
        float _enabledAt;
        int _seenMask;
        bool _bound;
        bool _retired;
        bool _hiddenByUser;
        bool _watchTake;
        bool _labelOutsideRoot;

        void Awake()
        {
            if (startHint != null) startHint.SetActive(false);
            if (root != null)
            {
                if (!root.activeSelf) root.SetActive(true);
                _ease.Init(root.transform, ScaleEase.ResolveFull(root.transform, shownScale), 0.22f, false);
            }
            _labelOutsideRoot = label != null && (root == null || !label.transform.IsChildOf(root.transform));
            if (_labelOutsideRoot) label.text = "";
        }

        void OnEnable() { _enabledAt = Time.time; }

        void Update()
        {
            if (!_bound && controller != null) Bind();
            _ease.Tick(Time.deltaTime);
            if (!_bound) return;

            if (_step != Step.None)
            {
                _stepTime += Time.deltaTime;
                if (_stepTime >= stepSeconds) EndStep();
            }
            else if (!Seen(Step.Start) && CanGuide && controller.Initialized
                     && !controller.RunInProgress && !controller.HasStartedRun
                     && Time.time - _enabledAt >= firstDelay)
            {
                Show(Step.Start);
            }

            // The paper has no "taken" event; while a decision is waiting, read its flag (a bool).
            if (_watchTake && paper != null && paper.isActiveAndEnabled && paper.IsTaken)
            {
                _watchTake = false;
                if (_step == Step.TakePaper) EndStep();
                if (CanGuide && !Seen(Step.Decide)) Show(Step.Decide);
            }
        }

        bool CanGuide => !_retired && !_hiddenByUser;
        bool Seen(Step step) => (_seenMask & (1 << (int)step)) != 0;

        void Bind()
        {
            var r = controller.Reducer;
            r.DecisionRequested += OnDecisionRequested;
            r.DecisionResolved += OnDecisionResolved;
            r.WorkDelivered += OnRunEnded;
            r.TaskFailed += OnRunEnded;
            r.SessionReset += OnSessionReset;
            controller.RunStateChanged += OnRunStateChanged;
            _bound = true;
        }

        void OnDisable()
        {
            if (!_bound || controller == null) return;
            var r = controller.Reducer;
            r.DecisionRequested -= OnDecisionRequested;
            r.DecisionResolved -= OnDecisionResolved;
            r.WorkDelivered -= OnRunEnded;
            r.TaskFailed -= OnRunEnded;
            r.SessionReset -= OnSessionReset;
            controller.RunStateChanged -= OnRunStateChanged;
            _bound = false;
        }

        void OnRunStateChanged()
        {
            if (controller == null || !controller.RunInProgress) return;
            // A run has started (by the dock, a card or anything else): the Start step is done.
            _seenMask |= 1 << (int)Step.Start;
            if (_step == Step.Start) EndStep();
        }

        void OnDecisionRequested(string id, EventEnvelope e)
        {
            _watchTake = true;
            if (_step == Step.Start) EndStep();
            if (CanGuide && !Seen(Step.TakePaper)) Show(Step.TakePaper);
        }

        void OnDecisionResolved(string id, string state)
        {
            _watchTake = false;
            if (_step == Step.TakePaper || _step == Step.Decide) EndStep();
            // A rejection ends the run without a delivery: that is a full first run too. A request
            // for changes is not; the run continues with a revised paper.
            string s = state ?? "";
            bool approved = s.IndexOf("approv", System.StringComparison.OrdinalIgnoreCase) >= 0;
            bool changes = s.IndexOf("change", System.StringComparison.OrdinalIgnoreCase) >= 0;
            if (!approved && !changes) _retired = true;
        }

        void OnRunEnded(EventEnvelope e)
        {
            _retired = true;
            _watchTake = false;
            EndStep();
        }

        void OnSessionReset()
        {
            _watchTake = false;
            EndStep();
        }

        /// <summary>Turn the tips off for this session.</summary>
        public void Hide()
        {
            _hiddenByUser = true;
            EndStep();
        }

        /// <summary>Deliberate action: turn the tips back on and let every step show once more.</summary>
        public void ShowTips()
        {
            _hiddenByUser = false;
            _retired = false;
            _seenMask = 0;
            if (_bound && controller.Initialized && !controller.RunInProgress) Show(Step.Start);
        }

        /// <summary>Deliberate action: show the "Press Start" line once more (for example after the
        /// user chose "Run again" on the completion receipt). Does nothing while a run is in progress.</summary>
        public void ShowStartHint()
        {
            if (_hiddenByUser || controller == null || !controller.Initialized || controller.RunInProgress) return;
            Show(Step.Start);
        }

        void Show(Step step)
        {
            _seenMask |= 1 << (int)step;
            _step = step;
            _stepTime = 0f;
            if (label != null)
                label.text = step == Step.Start ? TextStart
                           : step == Step.TakePaper ? TextTakePaper
                           : TextDecide;
            _ease.Show();
        }

        void EndStep()
        {
            if (_step == Step.None) return;
            _step = Step.None;
            _ease.Hide();
            if (_labelOutsideRoot && label != null) label.text = "";
        }
    }
}
