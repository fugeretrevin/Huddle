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
    ///   "Your AI team is ready." then "Press Start on the task dock."  ->
    ///   "Take the paper from the Researcher."  ->  "Approve or Reject."  ->  done.
    /// (The first step says what this is, then what to do, as two short lines one after the other:
    /// together they do not fit the 12.8 cm line at the 5.5 mm type floor.)
    ///
    /// The line stays out of the way of the paper:
    ///   - it is never shown while the expanded decision paper is in the user's hand (the paper's
    ///     own keys say what to do; the line returns if the paper is put down undecided);
    ///   - "Take the paper" waits until the agent bringing it has stopped walking, so the line does
    ///     not appear in front of a paper that is still on its way.
    /// When a step appears, the Researcher (the agent every step is about) glances at the user.
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

        [Tooltip("Seconds the opening words (\"Your AI team is ready.\") stay before the Start instruction replaces them.")]
        public float introSeconds = 2.6f;

        enum Step { None = 0, Start = 1, TakePaper = 2, Decide = 3 }

        const string TextIntro = "Your AI team is ready.";
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
        bool _intro;              // the Start step is still showing its opening words
        bool _suppressed;         // the line is held back because the paper is in the user's hand
        bool _takePending;        // "Take the paper" waits for the courier to arrive
        float _takeRequestedAt;
        MuseDesk.Agents.AgentGaze _courierGaze;
        bool _courierLooked;

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

            // The expanded paper in the user's hand: the line stays away (three bool reads).
            bool inHand = paper != null && paper.isActiveAndEnabled && paper.IsTaken && paper.IsHeld;
            if (inHand != _suppressed)
            {
                _suppressed = inHand;
                if (_step != Step.None)
                {
                    if (inHand) _ease.Hide(); else _ease.Show();
                }
            }

            if (_step != Step.None)
            {
                // The step's time does not run while the line is held back.
                if (!_suppressed) _stepTime += Time.deltaTime;
                if (_intro && _stepTime >= introSeconds)
                {
                    // Opening words done: the instruction takes their place, with its own full time.
                    _intro = false;
                    _stepTime = 0f;
                    if (label != null) label.text = TextStart;
                    _ease.Pulse(0.04f, 0.3f);
                }
                else if (_stepTime >= stepSeconds) EndStep();
            }
            else if (!Seen(Step.Start) && CanGuide && controller.Initialized
                     && !controller.RunInProgress && !controller.HasStartedRun
                     && Time.time - _enabledAt >= firstDelay)
            {
                Show(Step.Start, true);
            }

            // "Take the paper" appears once the agent bringing it has stopped walking (or after a
            // few seconds whatever happens), never in front of a paper that is still on its way.
            if (_takePending)
            {
                float waited = Time.time - _takeRequestedAt;
                var move = CourierMovement();
                bool arrived = waited >= 0.4f && (move == null || !move.IsMoving);
                if (arrived || waited >= 6f)
                {
                    _takePending = false;
                    if (_watchTake && CanGuide && !Seen(Step.TakePaper)) Show(Step.TakePaper, false);
                }
            }

            // The paper has no "taken" event; while a decision is waiting, read its flag (a bool).
            if (_watchTake && paper != null && paper.isActiveAndEnabled && paper.IsTaken)
            {
                _watchTake = false;
                _takePending = false;
                if (_step == Step.TakePaper) EndStep();
                if (CanGuide && !Seen(Step.Decide)) Show(Step.Decide, false);
            }
        }

        // The agent that brings the paper (the Researcher). Looked up once, on first need.
        MuseDesk.Agents.AgentGaze CourierGaze()
        {
            if (_courierGaze == null && !_courierLooked)
            {
                _courierLooked = true;
                _courierGaze = MuseDesk.Agents.AgentGaze.ForAgent(AgentIds.Researcher);
            }
            return _courierGaze;
        }

        MuseDesk.Agents.AgentMovement CourierMovement()
        {
            var gaze = CourierGaze();
            return gaze != null ? gaze.movement : null;
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
            // Shown from Update once the agent has walked up (see _takePending there).
            if (CanGuide && !Seen(Step.TakePaper))
            {
                _takePending = true;
                _takeRequestedAt = Time.time;
            }
        }

        void OnDecisionResolved(string id, string state)
        {
            _watchTake = false;
            _takePending = false;
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
            _takePending = false;
            EndStep();
        }

        void OnSessionReset()
        {
            _watchTake = false;
            _takePending = false;
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
            if (_bound && controller.Initialized && !controller.RunInProgress) Show(Step.Start, true);
        }

        /// <summary>Deliberate action: show the "Press Start" line once more (for example after the
        /// user chose "Run again" on the completion receipt). Does nothing while a run is in progress.</summary>
        public void ShowStartHint()
        {
            if (_hiddenByUser || controller == null || !controller.Initialized || controller.RunInProgress) return;
            Show(Step.Start, false);   // the user has been here before: the instruction only
        }

        void Show(Step step, bool withIntro)
        {
            _seenMask |= 1 << (int)step;
            _step = step;
            _stepTime = 0f;
            _intro = step == Step.Start && withIntro && introSeconds > 0.05f;
            if (label != null)
                label.text = step == Step.Start ? (_intro ? TextIntro : TextStart)
                           : step == Step.TakePaper ? TextTakePaper
                           : TextDecide;
            // Held back while the paper is in the user's hand; Update shows it if the paper is put down.
            if (!_suppressed) _ease.Show();

            // The agent this step is about glances at the user: a look, nothing more. (While it
            // stands in front of the user with the paper it is already looking at them.)
            var gaze = CourierGaze();
            if (gaze != null && gaze.isActiveAndEnabled) gaze.SetAttentionOnUser(1.4f, 0.85f);
        }

        void EndStep()
        {
            if (_step == Step.None) return;
            _step = Step.None;
            _intro = false;
            _ease.Hide();
            if (_labelOutsideRoot && label != null) label.text = "";
        }
    }
}
