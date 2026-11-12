using TMPro;
using UnityEngine;

namespace MuseDesk.Phase2.Board
{
    /// <summary>
    /// Desk-side controls for the task card the user last picked up. Per-card poke buttons were
    /// unusable (1.4 cm targets on a small grabbable card, and poking competed with grabbing), so the
    /// pause/resume and priority actions live here as full-size buttons within easy reach:
    /// grab a card to select it, then press Pause or Priority on the desk.
    ///
    /// The console only forwards to the card's existing request events; BoardPresenter issues the
    /// idempotent, revision-checked command and reconciles to the backend's canonical answer, so
    /// nothing here changes task state on its own.
    /// </summary>
    public class CardActionConsole : MonoBehaviour
    {
        public BoardPresenter presenter;
        [Tooltip("Shows which card the buttons will act on.")]
        public TMP_Text selectedLabel;

        TaskCardView _selected;
        float _nextRefresh;
        string _shown;

        void Update()
        {
            if (presenter == null || !presenter.Ready) return;

            foreach (var v in presenter.ActiveViews)
            {
                if (v != null && v.IsHeld) { _selected = v; break; }
            }

            if (Time.unscaledTime < _nextRefresh) return;
            _nextRefresh = Time.unscaledTime + 0.25f;
            Show(Describe());
        }

        bool HasSelection =>
            _selected != null && _selected.gameObject.activeInHierarchy && _selected.Data != null;

        string Describe()
        {
            if (!HasSelection) return "Grab a card to select it";
            var d = _selected.Data;
            string title = string.IsNullOrEmpty(d.Title) ? "(untitled)" : d.Title;
            if (title.Length > 26) title = title.Substring(0, 25) + "…";
            return $"{title}  ·  P{Mathf.Clamp(d.Priority, 0, 3)}  ·  {d.TaskState}";
        }

        /// <summary>Show a hint and hold it for a couple of seconds before the status line returns.</summary>
        void Hint(string text)
        {
            Show(text);
            _nextRefresh = Time.unscaledTime + 2f;
        }

        void Show(string text)
        {
            if (selectedLabel == null || text == _shown) return;
            _shown = text;
            selectedLabel.text = text;
        }

        /// <summary>Poke target: pause the selected task if it is running, resume it if paused.</summary>
        public void PauseResume()
        {
            if (HasSelection) _selected.PokePauseResume();
            else Hint("Grab a card first, then press Pause");
        }

        /// <summary>Poke target: step the selected task's priority (P0 → P1 → P2 → P3 → P0).</summary>
        public void CyclePriority()
        {
            if (HasSelection) _selected.PokeCyclePriority();
            else Hint("Grab a card first, then press Priority");
        }
    }
}
