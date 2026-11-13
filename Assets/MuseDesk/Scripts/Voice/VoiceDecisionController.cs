using System;
using UnityEngine;
using MuseDesk.Networking;

namespace MuseDesk.Voice
{
    /// <summary>
    /// Optional voice layer for responding to the pending decision. It is PURELY ADDITIVE: it calls
    /// the exact same SessionController commands the poke buttons do, so the backend still authorizes
    /// every action (AGENTS invariant #2). Surfaces listening/heard/error status on DecisionPanel.
    /// </summary>
    public class VoiceDecisionController : MonoBehaviour
    {
        [Header("Wiring")]
        public SessionController controller;
        [Tooltip("A component implementing IVoiceCommandSource (e.g. the Wit bridge). Auto-found if left null.")]
        public MonoBehaviour sourceBehaviour;

        [Header("Policy")]
        [Tooltip("Only act on voice while a live backend is connected (offline demo stays hands-only).")]
        public bool requireLiveMode = true;
        [Tooltip("Master switch; can be driven by RuntimeSettings.")]
        public bool voiceEnabled = false;
        [Tooltip("Decision paper that shows listening status and spoken question answers. Auto-found if left null.")]
        public MuseDesk.Documents.DecisionPanel panel;

        /// <summary>Raised with a short human-readable note ("heard: approve", "listening…") for UI.</summary>
        public event Action<string> Heard;

        IVoiceCommandSource _source;
        bool _bound;

        void OnEnable()
        {
            _source = ResolveSource();
            if (_source == null) return;
            _source.Approve += OnApprove;
            _source.Reject += OnReject;
            _source.RequestChanges += OnRequestChanges;
            _source.AskWhy += OnAskWhy;
            _source.ListeningChanged += OnListeningChanged;
            _source.StatusChanged += OnStatusChanged;
            _bound = true;
        }

        void OnDisable()
        {
            if (!_bound || _source == null) return;
            _source.Approve -= OnApprove;
            _source.Reject -= OnReject;
            _source.RequestChanges -= OnRequestChanges;
            _source.AskWhy -= OnAskWhy;
            _source.ListeningChanged -= OnListeningChanged;
            _source.StatusChanged -= OnStatusChanged;
            _bound = false;
        }

        IVoiceCommandSource ResolveSource()
        {
            if (sourceBehaviour is IVoiceCommandSource s) return s;
            foreach (var mb in GetComponentsInChildren<MonoBehaviour>(true))
                if (mb is IVoiceCommandSource found) { sourceBehaviour = mb; return found; }
            return null;
        }

        MuseDesk.Documents.DecisionPanel ResolvePanel()
        {
            if (panel == null)
                panel = FindAnyObjectByType<MuseDesk.Documents.DecisionPanel>(FindObjectsInactive.Include);
            return panel;
        }

        bool CanAct()
        {
            if (!voiceEnabled || controller == null) return false;
            if (_source == null || !_source.IsAvailable) return false;
            if (requireLiveMode && !controller.IsLive) return false;
            return !string.IsNullOrEmpty(controller.PendingDecisionId);
        }

        void OnApprove()
        {
            if (!CanAct()) return;
            NotifyStatus("Heard: approve");
            controller.ApprovePending();
        }

        void OnReject()
        {
            if (!CanAct()) return;
            NotifyStatus("Heard: reject");
            controller.RejectPending();
        }

        void OnRequestChanges()
        {
            if (!CanAct()) return;
            NotifyStatus("Heard: request changes");
            controller.RequestChangesPending();
        }

        void OnAskWhy(string question)
        {
            if (!CanAct()) return;
            NotifyStatus("Asking: " + (string.IsNullOrEmpty(question) ? "why?" : question));
            var p = ResolvePanel();
            if (p != null) { p.Expand(); p.SetAnswer("Asking…"); }
            controller.AskPending(
                string.IsNullOrEmpty(question) ? "Why do you recommend this?" : question,
                ans => { if (p != null) p.SetAnswer(ans); NotifyStatus(""); },
                AgentFor(question));
        }

        /// <summary>"Developer, what are you changing?" targets the developer. Unaddressed questions
        /// go to the agent that raised the decision (the researcher).</summary>
        public static string AgentFor(string question)
        {
            string q = (question ?? "").ToLowerInvariant();
            int best = int.MaxValue; string who = "researcher";
            foreach (var id in new[] { "researcher", "developer", "tester" })
            {
                int at = q.IndexOf(id, StringComparison.Ordinal);
                if (at >= 0 && at < best) { best = at; who = id; }
            }
            return who;
        }

        void OnListeningChanged(bool listening)
        {
            if (listening) NotifyStatus("Listening… say Approve, Reject, Changes, or Why?");
        }

        void OnStatusChanged(string msg) => NotifyStatus(msg);

        void NotifyStatus(string msg)
        {
            Heard?.Invoke(msg);
            var p = ResolvePanel();
            if (p != null) p.SetVoiceStatus(msg);
        }
    }
}
