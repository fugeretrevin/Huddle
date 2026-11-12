using System;
using System.Collections.Generic;
using System.Threading;
using TMPro;
using UnityEngine;
using MuseDesk.Models;
using MuseDesk.Networking;
using MuseDesk.Phase2;
using MuseDesk.Phase2.Contracts;

namespace MuseDesk.Workspace
{
    /// <summary>
    /// Focus mode: when on, routine agent approach/handoff choreography is suppressed and
    /// non-urgent updates (new artifacts, completed tasks) accumulate in the document inbox
    /// instead of interrupting the user. IMPORTANT: blocking decisions are never suppressed and
    /// are never auto-answered — the user still approves/rejects them by hand. Disabling focus
    /// restores ordinary notifications. Other presentation controllers read the static Active
    /// flag to decide whether to play a routine walk-up. Toggling also sets the REAL backend focus
    /// policy (Phase 2 SetFocus) when the board is wired, and the authoritative focus.changed event
    /// drives the local state back — so focus is never faked when a backend is present; with no
    /// board/backend it falls back to a purely local toggle.
    /// </summary>
    public class FocusModeController : MonoBehaviour
    {
        public SessionController controller;

        [Tooltip("Phase 2 board bootstrap — used to set the REAL backend focus policy. Auto-found if null.")]
        public Phase2Bootstrap phase2;

        [Header("Inbox UI")]
        [Tooltip("Small badge showing the number of accumulated updates while focused.")]
        public TMP_Text inboxBadge;
        [Tooltip("Scrolling list of accumulated update lines.")]
        public TMP_Text inboxLog;
        [Tooltip("Optional object shown only while focus mode is active (a 'Focus' sign/lamp).")]
        public GameObject focusIndicator;

        [Tooltip("Start the session in focus mode.")]
        public bool startFocused;
        [Tooltip("Max update lines kept in the inbox log.")]
        public int maxLogLines = 8;

        /// <summary>True while routine interruptions should be held back. Read by presentation controllers.</summary>
        public static bool Active { get; private set; }

        readonly List<string> _log = new List<string>();
        int _pending;
        bool _bound;
        bool _p2bound;
        bool _applyingFromBackend;   // guard so a backend echo doesn't re-send a command
        int _idem;
        CancellationTokenSource _cts;

        void OnEnable()
        {
            _cts = new CancellationTokenSource();
            SetFocus(startFocused);
        }

        void Update()
        {
            if (!_bound && controller != null) Bind();
            if (!_p2bound) BindPhase2();
        }

        void Bind()
        {
            controller.Reducer.ArtifactCreated += OnArtifact;
            controller.Reducer.TaskCompleted += OnTaskCompleted;
            controller.Reducer.SessionReset += OnReset;
            _bound = true;
        }

        void BindPhase2()
        {
            if (phase2 == null) phase2 = UnityEngine.Object.FindAnyObjectByType<Phase2Bootstrap>();
            if (phase2 == null || !phase2.Ready || phase2.Reducer == null) return;
            // The backend focus policy is authoritative: reflect it locally.
            phase2.Reducer.FocusChanged += OnBackendFocusChanged;
            OnBackendFocusChanged(phase2.Reducer.Focus);
            _p2bound = true;
        }

        void OnDisable()
        {
            if (_bound && controller != null)
            {
                controller.Reducer.ArtifactCreated -= OnArtifact;
                controller.Reducer.TaskCompleted -= OnTaskCompleted;
                controller.Reducer.SessionReset -= OnReset;
            }
            if (_p2bound && phase2 != null && phase2.Reducer != null)
                phase2.Reducer.FocusChanged -= OnBackendFocusChanged;
            _bound = false;
            _p2bound = false;
            try { _cts?.Cancel(); } catch { }
            Active = false;   // never leave the gate latched when this object goes away
        }

        /// <summary>The backend confirmed a focus policy — follow it (don't fake local state).</summary>
        void OnBackendFocusChanged(FocusSnapshotV2 policy)
        {
            if (policy == null) return;
            bool on = policy.Mode == FocusModes.Focus || policy.Mode == FocusModes.DoNotDisturb;
            _applyingFromBackend = true;
            try { SetFocus(on); }
            finally { _applyingFromBackend = false; }
        }

        void OnArtifact(EventEnvelope e)
        {
            if (!Active) return;
            string summary = e != null ? e.PayloadString("summary", null) : null;
            Collect(string.IsNullOrEmpty(summary) ? "New artifact produced" : "Artifact: " + summary);
        }
        void OnTaskCompleted(EventEnvelope e)
        {
            if (!Active) return;
            string summary = e != null ? e.PayloadString("result_summary", null) : null;
            Collect(string.IsNullOrEmpty(summary) ? "Task completed" : "Done: " + summary);
        }

        void OnReset()
        {
            _pending = 0; _log.Clear(); RefreshInbox();
        }

        void Collect(string line)
        {
            _pending++;
            _log.Add(line);
            while (_log.Count > maxLogLines) _log.RemoveAt(0);
            RefreshInbox();
        }

        /// <summary>Poke target / voice hook: flip focus mode.</summary>
        public void Toggle() => SetFocus(!Active);

        public void SetFocus(bool on)
        {
            bool was = Active;
            Active = on;
            if (focusIndicator != null) focusIndicator.SetActive(on);
            // Entering focus starts a fresh queue. Leaving focus KEEPS it, so the updates that were
            // held back can be reviewed afterwards; it is cleared the next time focus is enabled.
            if (on && !was) { _pending = 0; _log.Clear(); }
            RefreshInbox();

            // Tell the real backend (unless this change CAME from the backend). The server/demo is
            // authoritative; the local change above is optimistic and the echoed focus.changed confirms it.
            if (!_applyingFromBackend) _ = SendFocusAsync(on);
        }

        async System.Threading.Tasks.Task SendFocusAsync(bool on)
        {
            var gw = phase2 != null ? phase2.Gateway : null;
            if (gw == null) return;   // no board/backend wired → local-only focus
            try
            {
                await gw.SetFocusAsync(new FocusCommand
                {
                    Mode = on ? FocusModes.Focus : FocusModes.Normal,
                    IdempotencyKey = $"focus-{SystemInfo.deviceUniqueIdentifier[..6]}-{++_idem:D4}",
                }, _cts != null ? _cts.Token : CancellationToken.None);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { Debug.LogWarning($"[MuseDesk] set focus failed: {ex.Message}"); }
        }

        void RefreshInbox()
        {
            if (inboxBadge != null)
                inboxBadge.text = Active ? _pending.ToString() : (_pending > 0 ? _pending.ToString() : "");
            if (inboxLog != null)
                inboxLog.text = _log.Count == 0 ? ""
                    : (Active ? "" : "While you were focused:\n") + string.Join("\n", _log);
        }
    }
}
