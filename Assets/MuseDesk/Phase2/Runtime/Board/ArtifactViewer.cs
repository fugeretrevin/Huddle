using System;
using System.Threading;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using MuseDesk.Phase2.Contracts;

namespace MuseDesk.Phase2.Board
{
    /// <summary>
    /// Shows REAL artifact content paged from the backend (GET /v2/.../artifacts/{id}). When the
    /// board gateway reports an artifact.created (or artifact.page_ready), this fetches the first page and
    /// displays it; Next/Prev page through the rest. Content is never synthesized — a missing/empty
    /// response just shows nothing. The display surfaces (TMP labels, root) are optional and wired in
    /// the scene; without them it still fetches and logs, so the backend path is exercised either way.
    /// </summary>
    public class ArtifactViewer : MonoBehaviour
    {
        [Tooltip("Board bootstrap that owns the v2 gateway. Auto-found if null.")]
        public Phase2Bootstrap phase2;

        [Header("Display (optional, wired in scene)")]
        [Tooltip("Root shown only while an artifact is open.")]
        public GameObject root;
        public TMP_Text titleLabel;
        public TMP_Text bodyText;
        public TMP_Text pageLabel;   // e.g. "2 / 5"

        [Tooltip("Automatically open the newest artifact when the backend creates one.")]
        public bool autoOpenOnCreate = true;

        IPhase2Gateway _gw;
        CancellationTokenSource _cts;
        string _artifactId;
        int _page;            // 0-based, matching the backend route and the demo gateway
        int _totalPages = 1;
        bool _bound;

        void OnEnable()
        {
            _cts = new CancellationTokenSource();
            if (root != null) root.SetActive(false);
        }

        void Update() { if (!_bound) Bind(); }

        void Bind()
        {
            if (phase2 == null) phase2 = UnityEngine.Object.FindAnyObjectByType<Phase2Bootstrap>();
            if (phase2 == null || !phase2.Ready || phase2.Gateway == null) return;
            _gw = phase2.Gateway;
            _gw.EventReceived += OnEvent;
            _bound = true;
        }

        void OnDisable()
        {
            if (_bound && _gw != null) _gw.EventReceived -= OnEvent;
            _bound = false;
            try { _cts?.Cancel(); } catch { }
        }

        void OnEvent(EventEnvelopeV2 e)
        {
            if (e == null || !autoOpenOnCreate) return;
            if ((e.Type == Phase2EventTypes.ArtifactCreated || e.Type == Phase2EventTypes.ArtifactPageReady)
                && !string.IsNullOrEmpty(e.ArtifactId))
                Open(e.ArtifactId);
        }

        /// <summary>Open an artifact at its first page.</summary>
        public void Open(string artifactId)
        {
            _artifactId = artifactId;
            _page = 0;
            _ = LoadAsync();
        }

        public void NextPage() { if (_page < _totalPages - 1) { _page++; _ = LoadAsync(); } }
        public void PrevPage() { if (_page > 0) { _page--; _ = LoadAsync(); } }
        public void Close() { _artifactId = null; if (root != null) root.SetActive(false); }

        async Task LoadAsync()
        {
            if (_gw == null || string.IsNullOrEmpty(_artifactId)) return;
            ArtifactPageResponse resp;
            try
            {
                resp = await _gw.GetArtifactPageAsync(_artifactId, _page, 1, _cts.Token);
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex) { Debug.LogWarning($"[MuseDesk.P2] artifact fetch failed: {ex.Message}"); return; }
            if (this == null || resp == null) return;

            _totalPages = Mathf.Max(1, resp.TotalPages);
            _page = Mathf.Clamp(resp.Page, 0, _totalPages - 1);

            string title = resp.Summary ?? resp.Kind ?? "Artifact";
            string body = "";
            if (resp.Pages != null && resp.Pages.Count > 0)
            {
                var pg = resp.Pages[0];
                if (!string.IsNullOrEmpty(pg.Title)) title = pg.Title;
                body = pg.Content ?? "";
            }

            if (root != null) root.SetActive(true);
            if (titleLabel != null) titleLabel.text = title;
            if (bodyText != null) bodyText.text = body;
            if (pageLabel != null) pageLabel.text = $"{_page + 1} / {_totalPages}";
            Debug.Log($"[MuseDesk.P2] artifact {_artifactId} page {_page + 1}/{_totalPages}: {title}");
        }
    }
}
