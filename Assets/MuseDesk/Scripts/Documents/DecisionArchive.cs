using System.Collections;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using MuseDesk.Models;
using MuseDesk.Networking;
using MuseDesk.Workspace;

namespace MuseDesk.Documents
{
    /// <summary>
    /// The filing tray: every decision the user has made stays in the workspace. Each decided paper
    /// adds a sheet to the out-tray stack, and poking the stack (ArchiveStackPoke) pages through the
    /// record of what was decided and what came of it: the question, the outcome, whether it was
    /// live or a scripted demo, and the resulting artifact (files changed, tests passed) if there
    /// was one. The record is shown on a small paper card that rises from the stack; it stays up
    /// for a few seconds after a poke, then goes back by itself. A poke after the oldest record
    /// puts it away at once.
    ///
    /// Everything shown comes from events the session reducer applied (decision.requested,
    /// decision.resolved, artifact.created, task.completed). Nothing is invented, and a rejected
    /// decision is filed as rejected with no result. The records and the stack survive a demo
    /// replay on purpose (the workspace keeps its history for the session); only the open readout
    /// is closed by a session reset.
    /// </summary>
    public class DecisionArchive : MonoBehaviour
    {
        public SessionController controller;

        [Header("Stack in the out-tray")]
        [Tooltip("Where filed sheets pile up.")]
        public Transform stackAnchor;
        [Tooltip("Inactive template sheet (a small quad); one copy per filed decision.")]
        public GameObject sheetTemplate;
        public int maxSheets = 8;
        [Tooltip("Seconds after a decision resolves before its sheet appears (the paper glides here first).")]
        public float fileDelay = 3.6f;

        [Header("Readout (the archive card)")]
        [Tooltip("The archive card. Stays active; shown and hidden by scale.")]
        public GameObject readoutRoot;
        [Tooltip("Legacy single text block. Used only when the card fields below are not wired.")]
        public TMP_Text readout;
        [Tooltip("Seconds the readout stays open after the last poke.")]
        public float readoutSeconds = 8f;
        [Tooltip("Scale of the readout when open (used when the builder saved it hidden).")]
        public Vector3 readoutShownScale = Vector3.one;

        [Header("Archive card fields")]
        [Tooltip("The decision's title.")]
        public TMP_Text cardTitle;
        [Tooltip("What the user decided, in words.")]
        public TMP_Text cardOutcome;
        [Tooltip("What came of it: result, tests, files.")]
        public TMP_Text cardResult;
        [Tooltip("The research provenance label the decision carried, when it had one.")]
        public TMP_Text cardProvenance;
        [Tooltip("'Filed 1 of 2'.")]
        public TMP_Text cardCount;
        [Tooltip("LIVE or SCRIPTED DEMO, in words, from the decision's own source_mode.")]
        public TMP_Text cardMode;
        [Tooltip("Colour band that repeats the mode.")]
        public Renderer cardBand;
        [Tooltip("The card rises from here (local position in its parent's space) to its saved position.")]
        public Vector3 riseFromLocal;
        [Tooltip("Use riseFromLocal. Off: the card only scales in place.")]
        public bool riseFromStack = false;
        public float riseSeconds = 0.3f;

        public class Record
        {
            public string DecisionId, Title, Summary, Mode, State, ProvenanceLabel;
            public string ArtifactSummary, ResultSummary;
            public int TestsPassed = -1, TestsFailed = -1;
            public List<string> Files;
            public bool Delivered;
        }

        readonly List<Record> _records = new List<Record>();
        readonly List<GameObject> _sheets = new List<GameObject>();
        readonly StringBuilder _sb = new StringBuilder(256);
        bool _bound;
        int _shown = -1;
        bool _open;
        float _hideAt;

        /// <summary>Decisions made this session, oldest first.</summary>
        public IReadOnlyList<Record> Records => _records;

        /// <summary>How many decisions have been decided and filed this session.</summary>
        public int FiledCount
        {
            get
            {
                int filed = 0;
                for (int i = 0; i < _records.Count; i++) if (_records[i].State != "requested") filed++;
                return filed;
            }
        }

        /// <summary>Raised when a decided paper has reached the out-tray (after <see cref="fileDelay"/>),
        /// so <see cref="FiledCount"/> has a new value to show.</summary>
        public event System.Action FiledCountChanged;

        /// <summary>True while the readout is open.</summary>
        public bool ReadoutOpen => _open;

        // The card's own show / hide: scale and (optionally) a rise from the stack, one eased value.
        Transform _card;
        Vector3 _cardFull = Vector3.one, _cardRest;
        float _k, _goal;          // 0 put away .. 1 up
        bool _moving;
        MaterialPropertyBlock _block;

        void Awake()
        {
            _block = new MaterialPropertyBlock();
            if (readoutRoot != null)
            {
                // Shown and hidden by scale, so the card can rise and go back; it holds no keys.
                _card = readoutRoot.transform;
                _cardFull = ScaleEase.ResolveFull(_card, readoutShownScale);
                _cardRest = _card.localPosition;
                if (!readoutRoot.activeSelf) readoutRoot.SetActive(true);
                _k = _goal = 0f;
                ApplyCard();
            }
            if (sheetTemplate != null) sheetTemplate.SetActive(false);
        }

        void Update()
        {
            if (!_bound && controller != null) Bind();
            if (_moving)
            {
                _k = Mathf.MoveTowards(_k, _goal, Time.deltaTime / Mathf.Max(0.05f, riseSeconds));
                ApplyCard();
                if (Mathf.Approximately(_k, _goal)) _moving = false;
            }
            if (_open && Time.time >= _hideAt) HideReadout();
        }

        void ApplyCard()
        {
            if (_card == null) return;
            float inv = 1f - _k;
            float eased = 1f - inv * inv * inv;                       // ease-out cubic
            _card.localScale = _cardFull * Mathf.Lerp(ScaleEase.HiddenFactor, 1f, eased);
            if (riseFromStack) _card.localPosition = Vector3.Lerp(riseFromLocal, _cardRest, eased);
        }

        void MoveCard(bool up)
        {
            float goal = up ? 1f : 0f;
            if (Mathf.Approximately(goal, _goal) && !_moving) return;
            _goal = goal;
            _moving = true;
        }

        void Bind()
        {
            var r = controller.Reducer;
            r.DecisionRequested += OnRequested;
            r.DecisionResolved += OnResolved;
            r.ArtifactCreated += OnArtifact;
            r.WorkDelivered += OnDelivered;
            r.SessionReset += HideReadout;
            _bound = true;
        }

        void OnDisable()
        {
            if (!_bound || controller == null) return;
            var r = controller.Reducer;
            r.DecisionRequested -= OnRequested;
            r.DecisionResolved -= OnResolved;
            r.ArtifactCreated -= OnArtifact;
            r.WorkDelivered -= OnDelivered;
            r.SessionReset -= HideReadout;
            _bound = false;
        }

        void RaiseFiledCountChanged()
        {
            var handler = FiledCountChanged;
            if (handler == null) return;
            try { handler(); }
            catch (System.Exception ex) { Debug.LogError($"[MuseDesk] filed-count listener error: {ex.Message}"); }
        }

        Record Find(string decisionId)
        {
            for (int i = _records.Count - 1; i >= 0; i--)
                if (_records[i].DecisionId == decisionId) return _records[i];
            return null;
        }

        Record LatestApproved()
        {
            for (int i = _records.Count - 1; i >= 0; i--)
                if (_records[i].State == "approved") return _records[i];
            return null;
        }

        void OnRequested(string decisionId, EventEnvelope e)
        {
            if (string.IsNullOrEmpty(decisionId) || Find(decisionId) != null) return;
            _records.Add(new Record
            {
                DecisionId = decisionId,
                Title = e != null ? e.PayloadString("title", "Approval requested") : "Approval requested",
                Summary = e != null ? e.PayloadString("summary", "") : "",
                Mode = e != null ? e.PayloadString("source_mode", controller.IsLive ? SourceModes.Live : SourceModes.Demo)
                                 : (controller.IsLive ? SourceModes.Live : SourceModes.Demo),
                ProvenanceLabel = e != null ? e.PayloadString("research_provenance_label", null) : null,
                State = "requested",
            });
        }

        void OnResolved(string decisionId, string state)
        {
            var rec = Find(decisionId);
            if (rec == null || rec.State != "requested") return;      // unknown, or already filed (replay)
            rec.State = (state ?? "resolved").ToLowerInvariant();
            if (isActiveAndEnabled) StartCoroutine(AddSheetAfter(fileDelay));
            else RaiseFiledCountChanged();
        }

        void OnArtifact(EventEnvelope e)
        {
            var rec = LatestApproved();
            if (rec == null || e == null || rec.ArtifactSummary != null) return;
            rec.ArtifactSummary = e.PayloadString("summary", "Artifact produced");
            rec.TestsPassed = e.PayloadValue<int>("tests_passed", -1);
            rec.TestsFailed = e.PayloadValue<int>("tests_failed", -1);
            rec.Files = e.PayloadValue<List<string>>("files_changed", null);
        }

        void OnDelivered(EventEnvelope e)
        {
            var rec = LatestApproved();
            if (rec == null || e == null) return;
            rec.Delivered = true;
            rec.ResultSummary = e.PayloadString("result_summary", rec.ResultSummary);
            if (_shown >= 0) Render();
        }

        IEnumerator AddSheetAfter(float seconds)
        {
            yield return new WaitForSeconds(seconds);
            RaiseFiledCountChanged();                          // the paper has landed: the count may show it
            if (sheetTemplate == null || stackAnchor == null) yield break;
            if (_sheets.Count >= maxSheets) yield break;       // the stack stops growing; the records are all kept
            var sheet = Instantiate(sheetTemplate, stackAnchor);
            sheet.name = "FiledSheet_" + _sheets.Count;
            sheet.transform.localPosition = new Vector3(0f, 0.0016f * (_sheets.Count + 1), 0f);
            // A slightly untidy pile reads as paper, not as one block.
            sheet.transform.localRotation = sheetTemplate.transform.localRotation *
                                            Quaternion.Euler(0f, 0f, (_sheets.Count % 2 == 0 ? 1f : -1f) * (3f + 2f * (_sheets.Count % 3)));
            sheet.SetActive(true);
            _sheets.Add(sheet);
        }

        /// <summary>
        /// Poke target: raise the card on the newest filed decision, or step to the next older one.
        /// A poke while the oldest record is on show puts the card away (it does not wrap round).
        /// </summary>
        public void ShowNext()
        {
            int filed = 0;
            for (int i = 0; i < _records.Count; i++) if (_records[i].State != "requested") filed++;
            if (filed == 0)
            {
                _shown = -1;
                Show("Nothing filed yet.", "Decisions you make are kept here.", "", "", "", null);
                return;
            }
            // Walk newest to oldest among filed records.
            int next = -1;
            for (int i = (_open && _shown >= 0 ? _shown : _records.Count) - 1; i >= 0; i--)
                if (_records[i].State != "requested") { next = i; break; }
            if (next < 0)
            {
                HideReadout();          // the oldest record was on show: this poke closes the card
                return;
            }
            _shown = next;
            Render();
        }

        public void HideReadout()
        {
            _shown = -1;
            _open = false;
            MoveCard(false);
        }

        void Render()
        {
            if (_shown < 0 || _shown >= _records.Count) return;
            var r = _records[_shown];
            int filed = 0, ordinal = 0;
            for (int i = 0; i < _records.Count; i++)
                if (_records[i].State != "requested") { filed++; if (i <= _shown) ordinal = filed; }

            string outcome = r.State switch
            {
                "approved" => "You approved it.",
                "rejected" => "You rejected it. Nothing was built.",
                "changes_requested" => "You asked for changes. The run stopped.",
                "expired" => "Expired before it was answered. Nothing was run.",
                _ => "Outcome: " + r.State,
            };

            _sb.Clear();
            if (r.State == "approved")
            {
                if (r.ArtifactSummary == null) _sb.Append("Result: still in progress.");
                else
                {
                    _sb.Append("Result: ").Append(r.ArtifactSummary);
                    if (r.TestsPassed >= 0)
                        _sb.Append("\nTests: ").Append(r.TestsPassed).Append(" passed, ").Append(Mathf.Max(0, r.TestsFailed)).Append(" failed");
                    if (r.Files != null && r.Files.Count > 0)
                        _sb.Append("\nFiles: ").Append(string.Join(", ", r.Files));
                    if (!r.Delivered) _sb.Append("\nNot yet confirmed complete.");
                }
            }
            string result = _sb.ToString();

            _sb.Clear();
            _sb.Append("Filed ").Append(ordinal).Append(" of ").Append(filed);
            Show(r.Title, outcome, result, r.ProvenanceLabel ?? "", _sb.ToString(), r.Mode);
        }

        /// <summary>Write the card (or the legacy single text block) and raise it.</summary>
        void Show(string title, string outcome, string result, string provenance, string count, string mode)
        {
            string modeWords = mode == null ? "" : (mode == SourceModes.Live ? "LIVE" : "SCRIPTED DEMO");
            bool card = cardTitle != null;
            if (card)
            {
                cardTitle.text = title;
                if (cardOutcome != null) cardOutcome.text = outcome;
                if (cardResult != null) cardResult.text = result;
                if (cardProvenance != null) cardProvenance.text = provenance;
                if (cardCount != null) cardCount.text = count;
                if (cardMode != null) cardMode.text = modeWords;
                if (cardBand != null)
                    OfficeTokens.Tint(cardBand, _block, mode == SourceModes.Live ? OfficeTokens.Success : OfficeTokens.Amber);
            }
            else if (readout != null)
            {
                // No size tags: every line is printed at the label's own size.
                _sb.Clear();
                if (count.Length > 0 || modeWords.Length > 0) _sb.Append(count).Append("    ").Append(modeWords).Append('\n');
                _sb.Append(title).Append('\n').Append(outcome);
                if (provenance.Length > 0) _sb.Append('\n').Append(provenance);
                if (result.Length > 0) _sb.Append('\n').Append(result);
                readout.text = _sb.ToString();
            }
            MoveCard(true);
            _hideAt = Time.time + readoutSeconds;
            _open = true;
        }
    }
}
