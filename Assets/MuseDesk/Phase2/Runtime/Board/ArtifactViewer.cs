using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using MuseDesk.Phase2.Contracts;

namespace MuseDesk.Phase2.Board
{
    /// <summary>
    /// A paper sheet that shows REAL artifact content paged from the board gateway
    /// (GET /v2/.../artifacts/{id}; the offline gateway serves its labelled scripted samples).
    ///
    /// It never opens by itself. An artifact.created / artifact.page_ready event only records that
    /// the artifact is available; the sheet opens when the user asks (the receipt's View key calls
    /// <see cref="OpenDelivered"/>, anything else may call <see cref="Open"/>), in front of the user,
    /// and stays linked to the artifact and task it was opened for until it is closed. It closes on
    /// a session reset and when a new run starts.
    ///
    /// Honesty rules it follows:
    ///   - Title, body, kind, task, hash and page counts are the gateway's response, never synthesized.
    ///     When a fetch fails the sheet says so in words and shows nothing in its place.
    ///   - LIVE / SCRIPTED DEMO is printed in words from the artifact's own source_mode.
    ///   - The body is shown as plain text (no rich-text parsing), so a diff is shown as it was sent.
    ///
    /// Paging: the backend page index is 0-based (as before). One backend page can be longer than
    /// the sheet, so the body is also split into "parts" that fit (TextMeshPro page overflow); Next
    /// and Previous step through the parts, then the backend pages, then the other artifacts of the
    /// same task. Nothing is cut off silently.
    ///
    /// The sheet is hidden by scale, never deactivated, so its poke keys register with the
    /// Interaction SDK at start-up. Without display fields it still fetches and logs.
    /// </summary>
    public class ArtifactViewer : MonoBehaviour
    {
        [Tooltip("Board bootstrap that owns the v2 gateway. Auto-found if null.")]
        public Phase2Bootstrap phase2;
        [Tooltip("The session, so the sheet closes on a session reset and when a new run starts. " +
                 "Taken from the bootstrap if null.")]
        public MuseDesk.Networking.SessionController session;

        [Header("Sheet (wired in scene)")]
        [Tooltip("The sheet and its keys. Stays ACTIVE; shown and hidden by scale.")]
        public GameObject root;
        [Tooltip("Scale of the sheet when shown (used when the builder saved it hidden).")]
        public Vector3 shownScale = Vector3.one;
        public float easeSeconds = 0.25f;
        public TMP_Text titleLabel;
        public TMP_Text bodyText;
        [Tooltip("'Page 2 of 5'.")]
        public TMP_Text pageLabel;
        [Tooltip("LIVE or SCRIPTED DEMO, in words, from the artifact's own source_mode.")]
        public TMP_Text modeLabel;
        [Tooltip("Kind, task and content hash, as the gateway sent them.")]
        public TMP_Text provenanceLabel;
        [Tooltip("Colour band that repeats the mode.")]
        public Renderer modeBand;

        [Header("Keys (children of the sheet)")]
        [Tooltip("Hidden by scale on the first page.")]
        public Transform prevKey;
        [Tooltip("Hidden by scale on the last page.")]
        public Transform nextKey;
        [Tooltip("Scale of the Previous / Next keys when shown.")]
        public Vector3 keyScale = new Vector3(0.03f, 0.03f, 0.03f);

        [Tooltip("No longer used: the sheet never opens by itself. Kept so older builders and the " +
                 "saved scene still load; an artifact event only records the artifact as available.")]
        public bool autoOpenOnCreate = false;

        sealed class Known
        {
            public string Id, TaskId, Kind, Summary, Mode;
        }

        const string ModeLive = "LIVE";
        const string ModeDemo = "SCRIPTED DEMO";
        const string ModeUnknown = "MODE NOT REPORTED";

        readonly List<Known> _known = new List<Known>();                 // announced on the v2 stream, oldest first
        readonly Dictionary<string, int> _totals = new Dictionary<string, int>();
        readonly List<string> _set = new List<string>();                 // artifacts of the task the sheet is open for
        readonly StringBuilder _sb = new StringBuilder(160);
        readonly MuseDesk.Workspace.ScaleEase _ease = new MuseDesk.Workspace.ScaleEase();
        readonly MuseDesk.Workspace.ScaleEase _prevEase = new MuseDesk.Workspace.ScaleEase();
        readonly MuseDesk.Workspace.ScaleEase _nextEase = new MuseDesk.Workspace.ScaleEase();

        IPhase2Gateway _gw;
        CancellationTokenSource _cts;
        MaterialPropertyBlock _block;
        string _artifactId, _taskId;
        int _setIndex;
        int _page;            // 0-based, matching the backend route and the demo gateway
        int _totalPages = 1;
        int _screen;          // 0-based part of the current backend page that fits the sheet
        int _screens = 1;
        int _serial;          // drops answers that arrive after the sheet moved on
        bool _open, _visible, _loading, _failed;
        bool _bound, _sessionBound, _runWasActive, _wasAnimating, _hasRoot;
        float _nextFindAt;

        /// <summary>True while the sheet is open (or opening) for an artifact.</summary>
        public bool IsOpen => _open;
        /// <summary>The artifact the sheet is linked to, or null when closed.</summary>
        public string OpenArtifactId => _open ? _artifactId : null;
        /// <summary>The task of the artifact the sheet is linked to, when the gateway reported one.</summary>
        public string OpenTaskId => _open ? _taskId : null;
        /// <summary>True when the current run has announced at least one artifact on the board stream.</summary>
        public bool HasAvailable => _known.Count > 0;
        /// <summary>0-based backend page on show.</summary>
        public int PageIndex => _page;
        /// <summary>Backend pages of the artifact on show.</summary>
        public int TotalPages => _totalPages;

        void Awake()
        {
            _block = new MaterialPropertyBlock();
            if (root != null && root.activeSelf)
            {
                _ease.Init(root.transform, MuseDesk.Workspace.ScaleEase.ResolveFull(root.transform, shownScale), easeSeconds, false);
                _hasRoot = true;
            }
            if (prevKey != null)
                _prevEase.Init(prevKey, MuseDesk.Workspace.ScaleEase.ResolveFull(prevKey, keyScale), 0.12f, false);
            if (nextKey != null)
                _nextEase.Init(nextKey, MuseDesk.Workspace.ScaleEase.ResolveFull(nextKey, keyScale), 0.12f, false);
            if (bodyText != null)
            {
                bodyText.richText = false;                 // a diff is full of angle brackets
                bodyText.enableAutoSizing = false;
                bodyText.overflowMode = TextOverflowModes.Page;
                bodyText.pageToDisplay = 1;
            }
            if (titleLabel != null) titleLabel.richText = false;
            if (provenanceLabel != null) provenanceLabel.richText = false;
        }

        void OnEnable()
        {
            _cts = new CancellationTokenSource();
        }

        void Update()
        {
            if (!_bound) Bind();
            float dt = Time.deltaTime;
            _ease.Tick(dt);
            _prevEase.Tick(dt);
            _nextEase.Tick(dt);
            // When a show has finished, let the keys' press visuals re-read their resting offsets
            // at the final scale (PokeVisualRestGuard.SyncToCurrentScale, as the receipt does).
            bool animating = _ease.Animating || _prevEase.Animating || _nextEase.Animating;
            if (_wasAnimating && !animating && _visible && _hasRoot)
                root.BroadcastMessage("SyncToCurrentScale", SendMessageOptions.DontRequireReceiver);
            _wasAnimating = animating;
        }

        void Bind()
        {
            if (phase2 == null)
            {
                if (Time.unscaledTime < _nextFindAt) return;
                _nextFindAt = Time.unscaledTime + 0.5f;
                phase2 = UnityEngine.Object.FindAnyObjectByType<Phase2Bootstrap>();
            }
            if (phase2 == null || !phase2.Ready || phase2.Gateway == null) return;
            _gw = phase2.Gateway;
            _gw.EventReceived += OnEvent;
            if (session == null) session = phase2.sessionController;
            if (session != null)
            {
                session.Reducer.SessionReset += OnSessionReset;
                session.RunStateChanged += OnRunStateChanged;
                _runWasActive = session.RunInProgress;
                _sessionBound = true;
            }
            _bound = true;
        }

        void OnDisable()
        {
            if (_bound && _gw != null) _gw.EventReceived -= OnEvent;
            if (_sessionBound && session != null)
            {
                session.Reducer.SessionReset -= OnSessionReset;
                session.RunStateChanged -= OnRunStateChanged;
            }
            _bound = false;
            _sessionBound = false;
            _serial++;
            _loading = false;
            try { _cts?.Cancel(); } catch { }
        }

        // ── events: record, never open ──

        void OnEvent(EventEnvelopeV2 e)
        {
            if (e == null) return;
            if (e.Type == Phase2EventTypes.SessionReset)
            {
                Close();
                _known.Clear();
                _totals.Clear();
                return;
            }
            if (e.Type != Phase2EventTypes.ArtifactCreated && e.Type != Phase2EventTypes.ArtifactPageReady) return;
            string id = !string.IsNullOrEmpty(e.ArtifactId) ? e.ArtifactId : e.PayloadString("artifact_id");
            if (string.IsNullOrEmpty(id)) return;

            var k = FindKnown(id);
            if (k == null)
            {
                k = new Known { Id = id };
                _known.Add(k);
            }
            string task = !string.IsNullOrEmpty(e.TaskId) ? e.TaskId : e.PayloadString("task_id");
            if (!string.IsNullOrEmpty(task)) k.TaskId = task;
            string kind = e.PayloadString("kind");
            if (!string.IsNullOrEmpty(kind)) k.Kind = kind;
            string summary = e.PayloadString("summary");
            if (!string.IsNullOrEmpty(summary)) k.Summary = summary;
            string mode = e.PayloadString("source_mode");
            if (!string.IsNullOrEmpty(mode)) k.Mode = mode;
            if (e.TryPayloadInt("total_pages", out int total) && total > 0) _totals[id] = total;

            // The sheet is open on this very artifact and more of it arrived: refresh the page on show.
            if (_open && !_loading && id == _artifactId && e.Type == Phase2EventTypes.ArtifactPageReady)
                _ = LoadAsync(_artifactId, _setIndex, _page, false, false);
        }

        void OnSessionReset()
        {
            Close();
            _known.Clear();
            _totals.Clear();
        }

        void OnRunStateChanged()
        {
            bool active = session != null && session.RunInProgress;
            if (active && !_runWasActive)
            {
                // A new run: the sheet belonged to the previous one, and so did the recorded artifacts.
                Close();
                _known.Clear();
            }
            _runWasActive = active;
        }

        Known FindKnown(string id)
        {
            for (int i = 0; i < _known.Count; i++)
                if (_known[i].Id == id) return _known[i];
            return null;
        }

        // ── commands (poke keys and other views call these) ──

        /// <summary>Open an artifact at its first page. Only ever called because the user asked.</summary>
        public void Open(string artifactId)
        {
            if (string.IsNullOrEmpty(artifactId)) return;
            var k = FindKnown(artifactId);
            _set.Clear();
            if (k != null && !string.IsNullOrEmpty(k.TaskId))
                for (int i = 0; i < _known.Count; i++)
                    if (_known[i].TaskId == k.TaskId) _set.Add(_known[i].Id);
            if (!_set.Contains(artifactId))
            {
                _set.Clear();
                _set.Add(artifactId);
            }
            _artifactId = artifactId;
            _taskId = k != null ? k.TaskId : null;
            _setIndex = _set.IndexOf(artifactId);
            _page = 0;
            _screen = 0;
            _screens = 1;
            _totalPages = 1;
            _failed = false;
            _open = true;
            _ = LoadAsync(artifactId, _setIndex, 0, false, true);
        }

        /// <summary>
        /// Open the artifact a completed run delivered (the receipt's View key). The id comes from
        /// the session's task.completed event; when the board stream announced that id it is opened,
        /// otherwise the first artifact the board announced for the run that just ended, otherwise
        /// the id is asked of the gateway as it is. Returns false when there is nothing to open.
        /// </summary>
        public bool OpenDelivered(string artifactId)
        {
            string id = null;
            if (!string.IsNullOrEmpty(artifactId) && FindKnown(artifactId) != null) id = artifactId;
            else if (_known.Count > 0)
            {
                var last = _known[_known.Count - 1];
                id = last.Id;
                if (!string.IsNullOrEmpty(last.TaskId))
                    for (int i = 0; i < _known.Count; i++)
                        if (_known[i].TaskId == last.TaskId) { id = _known[i].Id; break; }
            }
            else id = artifactId;
            if (string.IsNullOrEmpty(id)) return false;
            Open(id);
            return true;
        }

        /// <summary>Open the first artifact of the most recent task the board announced. Returns
        /// false when the current run has announced none.</summary>
        public bool OpenLatest() => _known.Count > 0 && OpenDelivered(null);

        /// <summary>Poke target: the next part, page or artifact of the same task. Ignored at the end.</summary>
        public void NextPage()
        {
            if (!_open || _loading || _failed) return;
            if (_screen < _screens - 1) { _screen++; ApplyScreen(); return; }
            if (_page < _totalPages - 1) { _ = LoadAsync(_artifactId, _setIndex, _page + 1, false, false); return; }
            if (_setIndex < _set.Count - 1) _ = LoadAsync(_set[_setIndex + 1], _setIndex + 1, 0, false, false);
        }

        /// <summary>Poke target: the previous part, page or artifact of the same task. Ignored at the start.</summary>
        public void PrevPage()
        {
            if (!_open || _loading || _failed) return;
            if (_screen > 0) { _screen--; ApplyScreen(); return; }
            if (_page > 0) { _ = LoadAsync(_artifactId, _setIndex, _page - 1, true, false); return; }
            if (_setIndex > 0)
            {
                string id = _set[_setIndex - 1];
                int last = _totals.TryGetValue(id, out int total) ? Mathf.Max(0, total - 1) : 0;
                _ = LoadAsync(id, _setIndex - 1, last, true, false);
            }
        }

        /// <summary>Poke target: put the sheet away and forget what it was linked to.</summary>
        public void Close()
        {
            _serial++;
            _loading = false;
            _open = false;
            _failed = false;
            _artifactId = null;
            _taskId = null;
            _set.Clear();
            _setIndex = 0;
            _page = 0;
            _screen = 0;
            if (!_visible) return;
            _visible = false;
            _ease.Hide();
        }

        // ── loading and presentation ──

        async Task LoadAsync(string id, int setIndex, int page, bool wantLastPart, bool opening)
        {
            int serial = ++_serial;
            if (_gw == null)
            {
                if (opening) ShowFailure(id, "The task board is not ready yet.");
                return;
            }
            _loading = true;
            ArtifactPageResponse resp = null;
            string error = null;
            try
            {
                resp = await _gw.GetArtifactPageAsync(id, page, 1, _cts.Token);
            }
            catch (OperationCanceledException)
            {
                if (serial == _serial) _loading = false;
                return;
            }
            catch (Exception ex) { error = ex.Message; }
            if (this == null) return;
            if (serial != _serial) return;          // closed, or another request replaced this one
            _loading = false;
            if (error == null && resp == null) error = "The gateway returned nothing.";
            if (error != null)
            {
                Debug.LogWarning($"[MuseDesk.P2] artifact fetch failed: {error}");
                if (opening) ShowFailure(id, error);
                else if (pageLabel != null) pageLabel.text = "That page could not be loaded.";
                return;
            }

            _artifactId = id;
            _setIndex = setIndex;
            _failed = false;
            _totalPages = Mathf.Max(1, resp.TotalPages);
            _page = Mathf.Clamp(resp.Page, 0, _totalPages - 1);
            _totals[id] = _totalPages;
            var known = FindKnown(id);
            if (!string.IsNullOrEmpty(resp.TaskId)) _taskId = resp.TaskId;
            else if (known != null) _taskId = known.TaskId;

            string kindWords = KindWords(resp.Kind);
            string title = !string.IsNullOrEmpty(resp.Summary) ? resp.Summary
                         : !string.IsNullOrEmpty(kindWords) ? kindWords : "Artifact";
            string body = "";
            if (resp.Pages != null && resp.Pages.Count > 0)
            {
                var pg = resp.Pages[0];
                if (pg != null)
                {
                    if (!string.IsNullOrEmpty(pg.Title)) title = pg.Title;
                    body = pg.Content ?? "";
                }
            }
            if (body.Length == 0) body = "(This page is empty.)";

            // Provenance: what the gateway said this is and where it belongs.
            _sb.Clear();
            if (!string.IsNullOrEmpty(resp.Summary) && resp.Summary != title) _sb.Append(resp.Summary).Append(". ");
            if (!string.IsNullOrEmpty(kindWords)) _sb.Append(kindWords).Append(". ");
            if (!string.IsNullOrEmpty(_taskId)) _sb.Append("Task ").Append(_taskId).Append(". ");
            if (!string.IsNullOrEmpty(resp.ContentHash)) _sb.Append("Hash ").Append(resp.ContentHash).Append(". ");
            if (resp.Stale) _sb.Append("Marked stale by the backend.");
            if (_sb.Length == 0) _sb.Append("No provenance was sent with this artifact.");
            string provenance = _sb.ToString();

            string mode = !string.IsNullOrEmpty(resp.SourceMode) ? resp.SourceMode
                        : known != null && !string.IsNullOrEmpty(known.Mode) ? known.Mode
                        : _gw.SourceMode;

            if (titleLabel != null) titleLabel.text = title;
            if (provenanceLabel != null) provenanceLabel.text = provenance;
            ApplyMode(mode);
            _screens = 1;
            if (bodyText != null)
            {
                bodyText.text = body;
                bodyText.pageToDisplay = 1;
                bodyText.ForceMeshUpdate(true, true);
                _screens = Mathf.Max(1, bodyText.textInfo.pageCount);
            }
            _screen = wantLastPart ? _screens - 1 : 0;
            ApplyScreen();
            Reveal();
            Debug.Log($"[MuseDesk.P2] artifact {_artifactId} page {_page + 1}/{_totalPages}: {title}");
        }

        void ShowFailure(string id, string error)
        {
            _failed = true;
            _screens = 1;
            _screen = 0;
            var known = FindKnown(id);
            if (titleLabel != null) titleLabel.text = "Could not open the artifact";
            if (provenanceLabel != null)
                provenanceLabel.text = known != null && !string.IsNullOrEmpty(known.Summary) ? known.Summary : "Artifact " + id;
            if (bodyText != null)
            {
                bodyText.text = "The artifact could not be loaded.\n" + error + "\nNothing is shown in its place.";
                bodyText.pageToDisplay = 1;
            }
            if (pageLabel != null) pageLabel.text = "";
            ApplyMode(known != null && !string.IsNullOrEmpty(known.Mode) ? known.Mode : (_gw != null ? _gw.SourceMode : null));
            _prevEase.Hide();
            _nextEase.Hide();
            Reveal();
        }

        void Reveal()
        {
            if (_visible) return;
            _visible = true;
            _ease.Show();
        }

        void ApplyMode(string mode)
        {
            bool live = mode == SourceModes.Live;
            bool demo = mode == SourceModes.Demo;
            if (modeLabel != null) modeLabel.text = live ? ModeLive : demo ? ModeDemo : ModeUnknown;
            if (modeBand != null)
                MuseDesk.Workspace.OfficeTokens.Tint(modeBand, _block,
                    live ? MuseDesk.Workspace.OfficeTokens.Success : MuseDesk.Workspace.OfficeTokens.Amber);
        }

        /// <summary>Show the current part of the current page, the page line, and the keys that apply.</summary>
        void ApplyScreen()
        {
            if (bodyText != null) bodyText.pageToDisplay = _screen + 1;
            _sb.Clear();
            _sb.Append("Page ").Append(_page + 1).Append(" of ").Append(_totalPages);
            if (_screens > 1) _sb.Append(", part ").Append(_screen + 1).Append(" of ").Append(_screens);
            if (_set.Count > 1) _sb.Append("\nDocument ").Append(_setIndex + 1).Append(" of ").Append(_set.Count);
            if (pageLabel != null) pageLabel.text = _sb.ToString();

            bool atStart = _screen <= 0 && _page <= 0 && _setIndex <= 0;
            bool atEnd = _screen >= _screens - 1 && _page >= _totalPages - 1 && _setIndex >= _set.Count - 1;
            if (atStart) _prevEase.Hide(); else _prevEase.Show();
            if (atEnd) _nextEase.Hide(); else _nextEase.Show();
        }

        static string KindWords(string kind)
        {
            if (string.IsNullOrEmpty(kind)) return null;
            switch (kind)
            {
                case ArtifactKinds.CodeDiff: return "Code diff";
                case ArtifactKinds.TestReport: return "Test report";
                case ArtifactKinds.ResearchReport: return "Research report";
                case ArtifactKinds.ImplementationPlan: return "Implementation plan";
                case ArtifactKinds.Completion: return "Completion";
                default: return kind.Replace('_', ' ');
            }
        }
    }
}
