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

        // The card view currently drawn as selected (thicker teal edge, small lift).
        TaskCardView _marked;

        void Mark(TaskCardView view)
        {
            if (_marked == view) return;
            if (_marked != null) _marked.SetSelected(false);
            _marked = view;
            if (view != null) view.SetSelected(true);
        }

        void Update()
        {
            if (presenter == null || !presenter.Ready)
            {
                Mark(null);
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
                // rc22: a finished card stays selected so it can be removed (Pause / Priority hide themselves by state).
                if (gone || now >= _hideAt)
                    Deselect();
            }

            Mark(_selected);
            ApplyActions(_selected != null ? _selected.Data : null);
            UpdateRemoveKey();
            if (_selected != null && now >= _noticeUntil) ShowTitle(_selected.Data);
        }

        // ─── rc22: Remove key (two-press confirm: "Remove" -> "Sure?" for 3 s -> removed) ───

        ConfirmKey _removeKey;
        string _removeKeyForId;
        bool _removeBuildTried;

        /// <summary>The Remove key, or null until the toolbar is bound (probes read its label and state).</summary>
        public ConfirmKey RemoveKey => _removeKey;

        void UpdateRemoveKey()
        {
            if (_removeKey == null)
            {
                if (_removeBuildTried || priorityKey == null || pauseResumeKey == null) return;
                _removeBuildTried = true;
                BuildRemoveKey();
                if (_removeKey == null) return;
            }
            bool has = HasSelection;
            if (has && _removeKeyForId != _selectedTaskId) { _removeKey.Disarm(); _removeKeyForId = _selectedTaskId; }
            if (!has) _removeKeyForId = null;
            _removeKey.SetVisible(has);
            _removeKey.Tick();
            if (has && (_removeKey.Armed || _removeKey.Busy))
                _hideAt = Mathf.Max(_hideAt, Time.unscaledTime + 2f);   // the toolbar must not vanish between the two presses
        }

        void BuildRemoveKey()
        {
            Transform prio = priorityKey.transform;
            Transform parent = prio.parent;
            if (parent == null) return;
            Vector3 step = prio.localPosition - pauseResumeKey.transform.localPosition;     // one key pitch along the row
            if (step.sqrMagnitude < 1e-10f) step = new Vector3(Mathf.Abs(prio.localScale.x) * 1.25f, 0f, 0f);
            Vector3 pos = prio.localPosition + step;
            float scale = priorityKey.AuthoredScale.x;
            if (scale < 1e-5f) return;
            _removeKey = new ConfirmKey(prio, parent, "RemoveKey", pos, scale, 1.15f, "Remove", "Sure?", RemoveSelectedAsync);
            if (!_removeKey.Built) _removeKey = null;
        }

        async System.Threading.Tasks.Task RemoveSelectedAsync()
        {
            string id = _selectedTaskId;
            if (id == null || presenter == null) return;
            _hideAt = Time.unscaledTime + idleHideSeconds + 4f;
            await presenter.RequestRemoveTaskAsync(id);   // a refusal is shown by the card (and so by the caption) and the status line
        }

        /// <summary>Press the Remove key exactly as a poke does (the first press arms it, the second removes). For drivers.</summary>
        public bool PressRemoveKey() => _removeKey != null && _removeKey.Press();

        /// <summary>Build / refresh the Remove key now instead of waiting for the next Update. For Play Mode drivers.</summary>
        public ConfirmKey EnsureRemoveKey() { UpdateRemoveKey(); return _removeKey; }

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
            if (_simulated != null) { _simulated.SetSimulatedHold(false); _simulated = null; }
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
            Mark(null);
        }

        // ─── selection without a grab (Play Mode driver, tests, future poke-to-select) ───

        /// <summary>Select the card for a task id, as picking it up does: the toolbar opens for it
        /// and the card shows its selected edge. Returns false when no such card is on the board.</summary>
        public bool SelectCard(string taskId)
        {
            if (presenter == null || !presenter.Ready) return false;
            var view = presenter.FindView(taskId);
            if (view == null || view.Data == null) return false;
            Select(view);
            _hideAt = Time.unscaledTime + idleHideSeconds;
            return true;
        }

        /// <summary>Select the first card that is not finished (lowest task id, so the choice is
        /// stable). For a Play Mode driver that has no hands.</summary>
        public void SelectFirstCard()
        {
            var view = FirstOpenCard();
            if (view != null) SelectCard(view.Data.TaskId);
        }

        /// <summary>Drop the selection: the toolbar hides and the card goes back to its resting edge.</summary>
        public void ClearSelection() => Deselect();

        TaskCardView _simulated;

        /// <summary>Capture hook for a Play Mode driver with no hands: the first unfinished card
        /// reports itself as held and comes 6 cm toward the reader, so the assignment pads and the
        /// held state can be seen. End with CancelSimulatedHold (nothing is sent) or
        /// EndSimulatedHold (lets go where the card is, exactly like a hand).</summary>
        public void SimulateHoldFirstCard()
        {
            if (_simulated != null && _simulated.IsHeld) return;       // already holding one
            _simulated = null;                                         // (a reload recycles the card and drops the hold)
            var view = FirstOpenCard();
            if (view == null) return;
            _simulated = view;
            view.SetSimulatedHold(true);
            view.transform.position -= view.transform.forward * 0.06f;
        }

        /// <summary>As SimulateHoldFirstCard, then moves the held card onto the first pad that would
        /// accept it, so the valid-target highlight shows. EndSimulatedHold after this sends the
        /// assignment for real (in the demo it starts the run); CancelSimulatedHold does not.</summary>
        public void SimulateHoldOverPad()
        {
            SimulateHoldFirstCard();
            if (_simulated == null || _simulated.Data == null) return;
            var zones = StationDropZone.Active;
            for (int i = 0; i < zones.Count; i++)
            {
                var z = zones[i];
                if (z == null || !z.isActiveAndEnabled || !z.IsCompatible(_simulated.Data) || !z.WouldReassign(_simulated.Data)) continue;
                if (!z.name.StartsWith("Dock_")) continue;             // the pads on the board, not the far stations
                _simulated.transform.position = z.RestAnchor.position;
                return;
            }
        }

        /// <summary>Let go of the simulated hold where the card is. Over a pad this is a real drop.</summary>
        public void EndSimulatedHold()
        {
            if (_simulated == null) return;
            _simulated.SetSimulatedHold(false);
            _simulated = null;
        }

        /// <summary>Put the simulated card back in its slot and let go there: no command is sent.</summary>
        public void CancelSimulatedHold()
        {
            if (_simulated == null) return;
            // Cancel the drag first (halos cleared, card sent home, no command), then let go.
            var drag = _simulated.GetComponent<TaskDragController>();
            if (drag != null) drag.CancelHold();
            _simulated.ReturnHome();
            _simulated.SetSimulatedHold(false);
            _simulated = null;
        }

        TaskCardView FirstOpenCard()
        {
            if (presenter == null || !presenter.Ready) return null;
            TaskCardView best = null;
            foreach (var v in presenter.ActiveViews)
            {
                if (v == null || v.Data == null || !v.gameObject.activeInHierarchy) continue;
                if (TaskStateSets.IsTerminal(v.Data.TaskState)) continue;
                if (best == null || string.CompareOrdinal(v.Data.TaskId, best.Data.TaskId) < 0) best = v;
            }
            return best;
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
