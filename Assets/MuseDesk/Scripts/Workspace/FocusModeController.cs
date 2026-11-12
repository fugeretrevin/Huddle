using System.Collections.Generic;
using TMPro;
using UnityEngine;
using MuseDesk.Models;
using MuseDesk.Networking;

namespace MuseDesk.Workspace
{
    /// <summary>
    /// Focus mode: when on, routine agent approach/handoff choreography is suppressed and
    /// non-urgent updates (new artifacts, completed tasks) accumulate in the document inbox
    /// instead of interrupting the user. IMPORTANT: blocking decisions are never suppressed and
    /// are never auto-answered — the user still approves/rejects them by hand. Disabling focus
    /// restores ordinary notifications. Other presentation controllers read the static Active
    /// flag to decide whether to play a routine walk-up; the backend is unaffected either way.
    /// </summary>
    public class FocusModeController : MonoBehaviour
    {
        public SessionController controller;

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

        void OnEnable()
        {
            SetFocus(startFocused);
        }

        void Update() { if (!_bound && controller != null) Bind(); }

        void Bind()
        {
            controller.Reducer.ArtifactCreated += OnArtifact;
            controller.Reducer.TaskCompleted += OnTaskCompleted;
            controller.Reducer.SessionReset += OnReset;
            _bound = true;
        }

        void OnDisable()
        {
            if (_bound && controller != null)
            {
                controller.Reducer.ArtifactCreated -= OnArtifact;
                controller.Reducer.TaskCompleted -= OnTaskCompleted;
                controller.Reducer.SessionReset -= OnReset;
            }
            _bound = false;
            Active = false;   // never leave the gate latched when this object goes away
        }

        void OnArtifact(EventEnvelope e) { if (Active) Collect("New artifact produced"); }
        void OnTaskCompleted(EventEnvelope e) { if (Active) Collect("Task completed"); }

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
            Active = on;
            if (focusIndicator != null) focusIndicator.SetActive(on);
            if (!on) { _pending = 0; _log.Clear(); }   // catching up clears the queue
            RefreshInbox();
        }

        void RefreshInbox()
        {
            if (inboxBadge != null)
                inboxBadge.text = Active ? (_pending > 0 ? _pending.ToString() : "0") : "";
            if (inboxLog != null)
                inboxLog.text = Active && _log.Count > 0 ? string.Join("\n", _log) : "";
        }
    }
}
