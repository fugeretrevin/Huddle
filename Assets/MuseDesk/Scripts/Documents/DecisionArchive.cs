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
    /// was one. The readout stays open for a few seconds after a poke, then closes by itself.
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

        [Header("Readout")]
        public GameObject readoutRoot;
        public TMP_Text readout;
        [Tooltip("Seconds the readout stays open after the last poke.")]
        public float readoutSeconds = 8f;
        [Tooltip("Scale of the readout when open (used when the builder saved it hidden).")]
        public Vector3 readoutShownScale = Vector3.one;

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

        readonly ScaleEase _readoutEase = new ScaleEase();

        void Awake()
        {
            if (readoutRoot != null)
            {
                // Shown and hidden by scale, so the readout can pop in and out; it holds no keys.
                Vector3 full = ScaleEase.ResolveFull(readoutRoot.transform, readoutShownScale);
                if (!readoutRoot.activeSelf) readoutRoot.SetActive(true);
                _readoutEase.Init(readoutRoot.transform, full, 0.2f, false);
            }
            if (sheetTemplate != null) sheetTemplate.SetActive(false);
        }

        void Update()
        {
            if (!_bound && controller != null) Bind();
            _readoutEase.Tick(Time.deltaTime);
            if (_open && Time.time >= _hideAt) HideReadout();
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

        /// <summary>Poke target: open the record, or step to the next filed decision (newest first).</summary>
        public void ShowNext()
        {
            int filed = 0;
            for (int i = 0; i < _records.Count; i++) if (_records[i].State != "requested") filed++;
            if (filed == 0)
            {
                _shown = -1;
                Show("Nothing filed yet.\nDecisions you make are kept here.");
                return;
            }
            // Walk newest to oldest among filed records, wrapping.
            int start = _shown >= 0 ? _shown - 1 : _records.Count - 1;
            for (int n = 0; n < _records.Count; n++)
            {
                int i = ((start - n) % _records.Count + _records.Count) % _records.Count;
                if (_records[i].State != "requested") { _shown = i; break; }
            }
            Render();
        }

        public void HideReadout()
        {
            _shown = -1;
            _open = false;
            _readoutEase.Hide();
        }

        void Render()
        {
            if (_shown < 0 || _shown >= _records.Count) return;
            var r = _records[_shown];
            int filed = 0, ordinal = 0;
            for (int i = 0; i < _records.Count; i++)
                if (_records[i].State != "requested") { filed++; if (i <= _shown) ordinal = filed; }

            _sb.Clear();
            _sb.Append("<size=80%>Filed ").Append(ordinal).Append(" of ").Append(filed).Append("    ")
               .Append(r.Mode == SourceModes.Live ? "LIVE" : "SCRIPTED DEMO").Append("</size>\n");
            _sb.Append("<b>").Append(r.Title).Append("</b>\n");
            _sb.Append(r.State switch
            {
                "approved" => "You approved it.",
                "rejected" => "You rejected it. Nothing was built.",
                "changes_requested" => "You asked for changes. The run stopped.",
                "expired" => "Expired before it was answered. Nothing was run.",
                _ => "Outcome: " + r.State,
            }).Append('\n');
            if (!string.IsNullOrEmpty(r.ProvenanceLabel)) _sb.Append("<size=80%><i>").Append(r.ProvenanceLabel).Append("</i></size>\n");
            if (r.State == "approved")
            {
                if (r.ArtifactSummary == null) _sb.Append("Result: still in progress.");
                else
                {
                    _sb.Append("Result: ").Append(r.ArtifactSummary).Append('\n');
                    if (r.TestsPassed >= 0)
                        _sb.Append("Tests: ").Append(r.TestsPassed).Append(" passed, ").Append(Mathf.Max(0, r.TestsFailed)).Append(" failed\n");
                    if (r.Files != null && r.Files.Count > 0)
                        _sb.Append("<size=80%>Files: ").Append(string.Join(", ", r.Files)).Append("</size>");
                    if (!r.Delivered) _sb.Append("\n<size=80%>Not yet confirmed complete.</size>");
                }
            }
            Show(_sb.ToString());
        }

        void Show(string text)
        {
            if (readout != null) readout.text = text;
            _readoutEase.Show();
            _hideAt = Time.time + readoutSeconds;
            _open = true;
        }
    }
}
