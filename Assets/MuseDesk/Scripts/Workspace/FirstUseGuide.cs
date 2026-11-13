using TMPro;
using UnityEngine;
using MuseDesk.Documents;
using MuseDesk.Models;
using MuseDesk.Networking;

namespace MuseDesk.Workspace
{
    /// <summary>
    /// A short first-use guide: one line of in-world text that always says the next useful thing to
    /// do. It follows what has actually happened (events from the session reducer and the state of
    /// the paper), never a timer script, so it is always true for the current moment and works the
    /// same in the scripted demo and against a live backend.
    ///
    /// Optional and skippable: Hide() (wired to a small key) turns it off, and it retires itself
    /// after the first completed run.
    /// </summary>
    public class FirstUseGuide : MonoBehaviour
    {
        public SessionController controller;
        [Tooltip("The decision paper, to know when the user has taken it.")]
        public HeldPaperController paper;
        public TMP_Text label;
        [Tooltip("Backing plate and anything else shown with the prompt.")]
        public GameObject root;
        [Tooltip("Shown while the user should start a run; pulses to draw the eye (e.g. an arrow over the task card).")]
        public GameObject startHint;

        enum Step { Welcome, Start, Working, TakePaper, Decide, Approved, Rejected, Done, Hidden }

        Step _step = Step.Welcome;
        float _stepTime;
        bool _bound;
        bool _hiddenByUser;

        void OnEnable() { Show(Step.Welcome); }

        void Update()
        {
            if (!_bound && controller != null) Bind();
            _stepTime += Time.deltaTime;

            switch (_step)
            {
                case Step.Welcome:
                    if (_stepTime > 4f) Show(Step.Start);
                    break;
                case Step.TakePaper:
                    if (paper != null && paper.isActiveAndEnabled && paper.IsTaken) Show(Step.Decide);
                    break;
                case Step.Approved:
                case Step.Rejected:
                    if (_step == Step.Rejected && _stepTime > 7f) Show(Step.Done);
                    break;
                case Step.Done:
                    if (_stepTime > 12f) Show(Step.Hidden);
                    break;
            }

            if (startHint != null && startHint.activeSelf)
            {
                float s = 1f + 0.12f * Mathf.Sin(Time.time * 4f);
                startHint.transform.localScale = _hintScale * s;
            }
        }

        Vector3 _hintScale = Vector3.one;

        void Awake() { if (startHint != null) _hintScale = startHint.transform.localScale; }

        void Bind()
        {
            var r = controller.Reducer;
            r.AgentChanged += OnAgentChanged;
            r.DecisionRequested += OnDecisionRequested;
            r.DecisionResolved += OnDecisionResolved;
            r.WorkDelivered += OnWorkDelivered;
            r.SessionReset += OnSessionReset;
            _bound = true;
        }

        void OnDisable()
        {
            if (!_bound || controller == null) return;
            var r = controller.Reducer;
            r.AgentChanged -= OnAgentChanged;
            r.DecisionRequested -= OnDecisionRequested;
            r.DecisionResolved -= OnDecisionResolved;
            r.WorkDelivered -= OnWorkDelivered;
            r.SessionReset -= OnSessionReset;
            _bound = false;
        }

        void OnAgentChanged(AgentView a)
        {
            if (a == null) return;
            bool working = a.State == AgentStates.Researching || a.State == AgentStates.Coding || a.State == AgentStates.Testing;
            if (working && (_step == Step.Welcome || _step == Step.Start)) Show(Step.Working);
        }

        void OnDecisionRequested(string id, EventEnvelope e) => Show(Step.TakePaper);

        void OnDecisionResolved(string id, string state)
        {
            string s = (state ?? "").ToLowerInvariant();
            if (s.Contains("change"))
            {
                Show(Step.Working);
                return;
            }
            Show(s.Contains("approv") ? Step.Approved : Step.Rejected);
        }

        void OnWorkDelivered(EventEnvelope e) => Show(Step.Done);

        void OnSessionReset() => Show(Step.Start);

        /// <summary>Poke target: turn the tips off for this session.</summary>
        public void Hide() { _hiddenByUser = true; Show(Step.Hidden); }

        /// <summary>Poke target: turn the tips back on.</summary>
        public void ShowTips() { _hiddenByUser = false; Show(Step.Start); }

        void Show(Step step)
        {
            if (_hiddenByUser) step = Step.Hidden;
            _step = step;
            _stepTime = 0f;
            bool live = controller != null && controller.IsLive;

            string text = step switch
            {
                Step.Welcome => "Welcome to your agent office.",
                Step.Start => live
                    ? "Drop the task card on Researcher to start, or press Start."
                    : "Drop the task card on Researcher to start the demo, or press Start.",
                Step.Working => "Your agents are working. The Researcher will bring you a decision.",
                Step.TakePaper => "The Researcher needs a decision. Take the paper from its hand.",
                Step.Decide => "Read it, then press Approve or Reject, or stamp it.",
                Step.Approved => "Approved. Watch the hand-offs: Researcher, Developer, Tester.",
                Step.Rejected => "Rejected. Nothing was built.",
                Step.Done => "Done. The result is on the shelf. Press Replay to run it again.",
                _ => "",
            };

            if (label != null) label.text = text;
            if (root != null) root.SetActive(step != Step.Hidden);
            if (startHint != null) startHint.SetActive(step == Step.Start);
        }
    }
}
