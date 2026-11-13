using TMPro;
using UnityEngine;
using MuseDesk.Phase2.Contracts;

namespace MuseDesk.Phase2.Board
{
    /// <summary>
    /// The contextual card toolbar. It appears only while a card is selected (the card the user last
    /// picked up) and offers only the actions that are valid for that card's state:
    ///   Pause     while the card is waiting (queued or blocked),
    ///   Resume    while it is paused,
    ///   Priority  for any card that is not finished.
    /// It hides (scaled to near zero, never deactivated: see ScaleReveal) when nothing is selected,
    /// when the selected card finishes, when the board reloads (snapshot / demo replay), or after
    /// a few seconds without interaction.
    ///
    /// The toolbar only forwards to the card's existing request events; BoardPresenter issues the
    /// idempotent, revision-checked command and reconciles to the gateway's canonical answer, so
    /// nothing here changes task state on its own. The caption repeats the card's own outcome words
    /// (pending, confirmed or refused), so the answer is readable next to the key that was pressed.
    /// </summary>
    public class CardActionConsole : MonoBehaviour
    {
        public BoardPresenter presenter;
        [Tooltip("Caption next to the keys: the selected card's title, then the outcome of an action.")]
        public TMP_Text selectedLabel;

        [Header("Contextual toolbar (wired by AgentScripts/BuildTaskDockV2)")]
        [Tooltip("Root of the toolbar. Shown only while a card is selected.")]
        public ScaleReveal toolbar;
        [Tooltip("The Pause / Resume key. Shown only when one of the two is valid.")]
        public ScaleReveal pauseResumeKey;
        [Tooltip("Label on the Pause / Resume key: reads Pause or Resume to match the card.")]
        public TMP_Text pauseResumeLabel;
        [Tooltip("The Priority key. Shown for any card that is not finished.")]
        public ScaleReveal priorityKey;
        [Tooltip("Seconds without interaction after which the toolbar hides.")]
        public float idleHideSeconds = 8f;

        /// <summary>The selected card, or null.</summary>
        public TaskCardView Selected => _selected;

        TaskCardView _selected;
        string _selectedTaskId;
        float _hideAt;
        float _noticeUntil;
        bool _bound;
        Phase2Reducer _reducer;

        // What the caption and keys currently show, so nothing is rebuilt unless it changed.
        string _shown;
        string _captionTitle;
        string _captionText;
        bool _captionValid;
        bool _labelIsResume;
        bool _labelSet;

        void Update()
        {
            if (presenter == null || !presenter.Ready)
            {
                ApplyActions(null);
                return;
            }
            if (!_bound) Bind();

            float now = Time.unscaledTime;
            var held = presenter.HeldCard;
            if (held != null && held.Data != null)
            {
                if (held.Data.TaskId != _selectedTaskId) Select(held);
                _hideAt = now + idleHideSeconds;                      // holding the card keeps it open
            }

            if (_selectedTaskId != null)
            {
                // Cards are pooled: follow the task id, not the view object.
                _selected = presenter.FindView(_selectedTaskId);
                bool gone = _selected == null || _selected.Data == null || !_selected.gameObject.activeInHierarchy;
                if (gone || TaskStateSets.IsTerminal(_selected.Data.TaskState) || now >= _hideAt)
                    Deselect();
            }

            ApplyActions(_selected != null ? _selected.Data : null);
            if (_selected != null && now >= _noticeUntil) ShowTitle(_selected.Data);
        }

        void Bind()
        {
            _reducer = presenter.Reducer;
            if (_reducer == null) return;
            _reducer.SnapshotLoaded += OnSnapshotLoaded;
            TaskCardView.NoticeShown += OnCardNotice;
            _bound = true;
        }

        void OnDisable()
        {
            if (_bound)
            {
                if (_reducer != null) _reducer.SnapshotLoaded -= OnSnapshotLoaded;
                TaskCardView.NoticeShown -= OnCardNotice;
            }
            _bound = false;
            _reducer = null;
            Deselect();
        }

        // A reload (demo replay, live resync) rebuilds every card: nothing stays selected.
        void OnSnapshotLoaded() => Deselect();

        void OnCardNotice(TaskCardView card, string words, bool? outcome)
        {
            if (card == null || card.Data == null || card.Data.TaskId != _selectedTaskId) return;
            Show(words);
            float hold = outcome.HasValue ? (outcome.Value ? 2f : 4f) : 8f;
            _noticeUntil = Time.unscaledTime + hold;
            _hideAt = Mathf.Max(_hideAt, _noticeUntil + 1f);          // never hide in the middle of an answer
        }

        void Select(TaskCardView card)
        {
            _selected = card;
            _selectedTaskId = card.Data.TaskId;
            _noticeUntil = 0f;
            _captionValid = false;
        }

        void Deselect()
        {
            _selected = null;
            _selectedTaskId = null;
            _noticeUntil = 0f;
            _captionValid = false;
        }

        /// <summary>Show exactly the keys that are valid for this card (null hides the toolbar).</summary>
        void ApplyActions(TaskCardV2 d)
        {
            bool has = d != null;
            bool resume = has && TaskCardView.IsResumable(d);
            bool pause = has && TaskCardView.IsPausable(d);
            bool priority = has && !TaskStateSets.IsTerminal(d.TaskState);

            if (toolbar != null) toolbar.Set(has);
            if (pauseResumeKey != null) pauseResumeKey.Set(pause || resume);
            if (priorityKey != null) priorityKey.Set(priority);

            if (pauseResumeLabel != null && has && (!_labelSet || resume != _labelIsResume))
            {
                _labelSet = true;
                _labelIsResume = resume;
                pauseResumeLabel.text = resume ? "Resume" : "Pause";
            }
        }

        void ShowTitle(TaskCardV2 d)
        {
            if (d == null) return;
            if (!_captionValid || !ReferenceEquals(d.Title, _captionTitle))
            {
                // Rebuilt only when the title object changes (a new canonical card), not per frame.
                _captionValid = true;
                _captionTitle = d.Title;
                string title = string.IsNullOrEmpty(d.Title) ? "(untitled)" : d.Title;
                if (title.Length > 34) title = title.Substring(0, 31) + "...";
                _captionText = title;
            }
            Show(_captionText);
        }

        void Show(string text)
        {
            if (selectedLabel == null || text == _shown) return;
            _shown = text;
            selectedLabel.text = text;
        }

        bool HasSelection => _selected != null && _selected.Data != null;

        /// <summary>Poke target: pause the selected card if it is waiting, resume it if it is paused.
        /// The presenter sends the command; the card and this caption show the gateway's answer.</summary>
        public void PauseResume()
        {
            if (!HasSelection) return;
            _hideAt = Time.unscaledTime + idleHideSeconds;
            _selected.PokePauseResume();
        }

        /// <summary>Poke target: step the selected card's priority (Low, Normal, High, Urgent).</summary>
        public void CyclePriority()
        {
            if (!HasSelection) return;
            _hideAt = Time.unscaledTime + idleHideSeconds;
            _selected.PokeCyclePriority();
        }
    }
}
