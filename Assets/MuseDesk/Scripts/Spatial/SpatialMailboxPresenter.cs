using TMPro;
using UnityEngine;
using MuseDesk.Art;
using MuseDesk.Workspace;

namespace MuseDesk.Spatial
{
    /// <summary>
    /// The SpatialMailbox at the near-user area while the office is out of reach: a quiet count of
    /// what is waiting. Both numbers are true counts from <see cref="AttentionController"/>:
    /// decisions the reducer has pending, and deliveries the reducer reported since the last run
    /// start / failure / acknowledgement. Suggestions are always zero (the backend has no such
    /// event) and are never mentioned. The art model shows only a total, so a caption under it
    /// says which is which in words ("1 to decide, 2 done").
    ///
    /// Presentation only: it holds no document and creates none; papers still arrive by courier.
    /// Shown by scale in remote modes; its model is switched off otherwise. The art-side
    /// SpatialMailboxController only writes its label and a property block.
    /// </summary>
    public class SpatialMailboxPresenter : MonoBehaviour
    {
        public AttentionController attention;
        [Tooltip("Holder of the mailbox (NOT under WorkspaceRoot). Shown and hidden by scale.")]
        public Transform mailbox;
        public Vector3 mailboxShownScale = Vector3.one;
        [Tooltip("Holder of the SpatialMailbox model; switched off while hidden.")]
        public GameObject model;
        public SpatialMailboxController art;
        [Tooltip("The counts in words, under the mailbox.")]
        public TMP_Text caption;
        [Tooltip("Offset from the near-user point: x = to the user's right, y = up, z = away from the user.")]
        public Vector3 offset = new Vector3(-0.20f, 0.07f, 0.04f);
        [Tooltip("The mailbox moves only after the near-user point moved this far (m).")]
        public float rePlaceDistance = 0.15f;
        [Tooltip("Off since rc18 (owner: the counts boxes were unclear). The agents' own '!' and the Manager board say the same.")]
        public static bool mailboxEnabled = false;

        readonly ScaleEase _ease = new ScaleEase();
        bool _ready, _bound, _placed, _countsSet;
        int _completed = -1, _pending = -1;
        Vector3 _placedAt;

        /// <summary>True while the mailbox is shown.</summary>
        public bool Shown => _ready && _ease.Shown;
        /// <summary>The counts last written to the mailbox (-1 before the first write).</summary>
        public int ShownCompleted => _completed;
        public int ShownPending => _pending;

        void Awake()
        {
            if (mailbox != null)
                _ease.Init(mailbox, ScaleEase.ResolveFull(mailbox, mailboxShownScale), 0.25f, false);
            _ready = true;
            if (model != null && model.activeSelf) model.SetActive(false);
        }

        void OnEnable()
        {
            SpatialWorkspaceState.Changed += Refresh;
            Refresh();
        }

        void OnDisable()
        {
            SpatialWorkspaceState.Changed -= Refresh;
            if (_bound && attention != null) attention.Changed -= OnAttentionChanged;
            _bound = false;
        }

        void Update()
        {
            if (!_bound && attention != null)
            {
                attention.Changed += OnAttentionChanged;
                _bound = true;
                Refresh();
            }
            _ease.Tick(Time.unscaledDeltaTime);
            if (mailbox != null && _ease.Shown) Place(false);
            if (model != null && !_ease.Shown && !_ease.Animating && model.activeSelf) model.SetActive(false);
        }

        void OnAttentionChanged(AttentionLevel level) { Refresh(); }

        void Refresh()
        {
            if (!_ready || mailbox == null) return;
            // A tray only when there is something in it: an empty mailbox would just clutter the desk.
            // Owner (worn rc17): the little "1 done" boxes were unclear ("weird and unclear on what it is or does"): off.
            bool show = mailboxEnabled && SpatialWorkspaceState.IsRemote && attention != null
                        && (attention.CompletedCount > 0 || attention.PendingDecisionCount > 0);
            if (!show) { _ease.Hide(); return; }

            if (model != null && !model.activeSelf)
            {
                model.SetActive(true);          // the art controller's Awake writes 0 the first time
                _countsSet = false;
            }
            WriteCounts();
            if (!_ease.Shown) { _placed = false; Place(true); }
            _ease.Show();
        }

        void WriteCounts()
        {
            int completed = attention.CompletedCount;
            int pending = attention.PendingDecisionCount;
            if (_countsSet && completed == _completed && pending == _pending) return;
            bool changed = _countsSet;
            _countsSet = true;
            _completed = completed;
            _pending = pending;
            if (art != null) art.UpdateCounts(completed, 0, pending);     // suggestions: always zero
            if (caption != null) caption.text = Words(completed, pending);
            if (changed) _ease.Pulse(0.06f, 0.3f);                        // once per change; ignored while hidden
        }

        /// <summary>The counts in words. Built only when a count changed.</summary>
        public static string Words(int completed, int pending)
        {
            if (pending > 0 && completed > 0) return pending + " to decide, " + completed + " done";
            if (pending > 0) return pending + " to decide";
            if (completed > 0) return completed + " done";
            return "Nothing waiting";
        }

        void Place(bool force)
        {
            if (!RemoteNearUser.TryGetFrame(out Vector3 origin, out Vector3 right, out Vector3 away)) return;
            Vector3 pos = origin + right * offset.x + Vector3.up * offset.y + away * offset.z;
            if (!force && _placed && (pos - _placedAt).sqrMagnitude < rePlaceDistance * rePlaceDistance) return;
            _placed = true;
            _placedAt = pos;
            mailbox.SetPositionAndRotation(pos, RemoteNearUser.FacingHead(pos, mailbox.rotation));
        }
    }
}
