using TMPro;
using UnityEngine;
using MuseDesk.Phase2.Contracts;

namespace MuseDesk.Phase2.Board
{
    /// <summary>
    /// Shows the assignment pads only while a task card is in the user's hand, and says in words what
    /// a drop will do or why it was refused.
    ///
    ///   holding a card            "Drop the card on a name"
    ///   over a pad that accepts   "Let go to give it to the Developer"
    ///   over a pad that refuses   the reason ("Starts with the Researcher", "Already running" ...)
    ///   after the drop            the card's own outcome words: pending, then the gateway's
    ///                             confirmation or refusal ("Not assigned: Another task is running")
    ///
    /// The pads are hidden by scale (ScaleReveal), never deactivated, and stay up for a moment after
    /// a refusal so the reason can be read. Presentation only: the drop itself still goes through
    /// TaskDragController, BoardPresenter and the gateway, which stays the authority.
    /// </summary>
    public class AssignDockFeedback : MonoBehaviour
    {
        public BoardPresenter presenter;
        [Tooltip("Root of the pads. Shown only while a card is held (and briefly after a drop).")]
        public ScaleReveal reveal;
        [Tooltip("One line of guidance / outcome above the pads.")]
        public TMP_Text caption;

        public string idleCaption = "Drop the card on a name";
        [Tooltip("Seconds the pads stay after the card is let go with nothing to report.")]
        public float lingerSeconds = 0.6f;
        [Tooltip("Longest the pads wait for the gateway's answer to a drop before hiding.")]
        public float pendingSeconds = 6f;
        [Tooltip("Seconds a confirmation stays readable.")]
        public float confirmSeconds = 2f;
        [Tooltip("Seconds a refusal stays readable.")]
        public float refusalSeconds = 4f;

        TaskCardView _lastHeld;
        bool _wasHeld;
        float _hideAt;
        string _shown;
        bool _bound;

        void OnEnable()
        {
            StationDropZone.HighlightChanged += OnHighlightChanged;
            TaskCardView.NoticeShown += OnCardNotice;
            _bound = true;
        }

        void OnDisable()
        {
            if (_bound)
            {
                StationDropZone.HighlightChanged -= OnHighlightChanged;
                TaskCardView.NoticeShown -= OnCardNotice;
            }
            _bound = false;
            _wasHeld = false;
            _lastHeld = null;
            _hideAt = 0f;
        }

        void Update()
        {
            if (presenter == null || !presenter.Ready)
            {
                if (reveal != null) reveal.Set(false);
                return;
            }

            float now = Time.unscaledTime;
            var held = presenter.HeldCard;
            if (held != null)
            {
                if (!_wasHeld || held != _lastHeld)
                {
                    _wasHeld = true;
                    _lastHeld = held;
                    Show(idleCaption);
                }
                if (_hideAt < now + lingerSeconds) _hideAt = now + lingerSeconds;
            }
            else _wasHeld = false;

            if (reveal != null) reveal.Set(now < _hideAt);
        }

        void OnHighlightChanged(StationDropZone zone, bool on, bool valid)
        {
            // Only while a card is in the hand: zones also clear their halo when they are disabled.
            if (!_wasHeld || _lastHeld == null || _lastHeld.Data == null || zone == null) return;
            if (!on) { Show(idleCaption); return; }
            if (valid) { Show(AcceptWords(zone.agentId)); return; }
            string why = TaskDragController.LocalRefusalWords(_lastHeld.Data, zone);
            Show(string.IsNullOrEmpty(why) ? "Can't assign here" : why);
        }

        void OnCardNotice(TaskCardView card, string words, bool? outcome)
        {
            // Only the card that was just dropped, and only while the pads are still up: the same
            // card's Pause / Priority answers belong to the card toolbar, not here.
            float now = Time.unscaledTime;
            if (card == null || card != _lastHeld || now >= _hideAt) return;
            if (!outcome.HasValue)
            {
                // Sent, not answered yet: stay up long enough for a slow live answer to be shown here.
                Show(words);
                if (_hideAt < now + pendingSeconds) _hideAt = now + pendingSeconds;
            }
            else if (outcome.Value)
            {
                Show(words);
                _hideAt = now + confirmSeconds;
            }
            else
            {
                Show("Not assigned: " + words);
                _hideAt = now + refusalSeconds;
            }
        }

        static string AcceptWords(string agentId)
        {
            switch (agentId)
            {
                case Phase2AgentIds.Researcher: return "Let go to give it to the Researcher";
                case Phase2AgentIds.Developer: return "Let go to give it to the Developer";
                case Phase2AgentIds.Tester: return "Let go to give it to the Tester";
                default: return "Let go to assign it";
            }
        }

        void Show(string text)
        {
            if (caption == null || text == _shown) return;
            _shown = text;
            caption.text = text;
        }
    }
}
