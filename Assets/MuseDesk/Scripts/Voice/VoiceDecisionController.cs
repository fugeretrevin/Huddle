using System;
using UnityEngine;
using MuseDesk.Networking;

namespace MuseDesk.Voice
{
    /// <summary>
    /// Optional voice layer for responding to the pending decision. It is PURELY ADDITIVE: it calls
    /// the exact same SessionController commands the poke buttons do, so the backend still authorizes
    /// every action (AGENTS invariant #2 — a spoken "approve" is not proof of approval; the acknowledged
    /// backend state is). Voice is also gated to connected mode by default, because it relies on a cloud
    /// recognizer (Wit.ai) that cannot run in the standalone offline judge demo (invariants #7/#8:
    /// hands-first, every P0 path works without voice). If no provider is present or available, this
    /// component does nothing and the hand UI is unaffected.
    /// </summary>
    public class VoiceDecisionController : MonoBehaviour
    {
        [Header("Wiring")]
        public SessionController controller;
        [Tooltip("A component implementing IVoiceCommandSource (e.g. the Wit bridge). Auto-found if left null.")]
        public MonoBehaviour sourceBehaviour;   // must implement IVoiceCommandSource

        [Header("Policy")]
        [Tooltip("Only act on voice while a live backend is connected (offline demo stays hands-only).")]
        public bool requireLiveMode = true;
        [Tooltip("Master switch; can be driven by RuntimeSettings so voice ships off by default.")]
        public bool voiceEnabled = false;

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
            _source.AskWhy += OnAskWhy;
            _source.ListeningChanged += OnListeningChanged;
            _bound = true;
        }

        void OnDisable()
        {
            if (!_bound || _source == null) return;
            _source.Approve -= OnApprove;
            _source.Reject -= OnReject;
            _source.AskWhy -= OnAskWhy;
            _source.ListeningChanged -= OnListeningChanged;
            _bound = false;
        }

        IVoiceCommandSource ResolveSource()
        {
            if (sourceBehaviour is IVoiceCommandSource s) return s;
            foreach (var mb in GetComponentsInChildren<MonoBehaviour>(true))
                if (mb is IVoiceCommandSource found) { sourceBehaviour = mb; return found; }
            return null;
        }

        bool CanAct()
        {
            if (!voiceEnabled || controller == null) return false;
            if (_source == null || !_source.IsAvailable) return false;
            if (requireLiveMode && !controller.IsLive) return false;
            // Only meaningful when a decision is actually awaiting a human.
            return !string.IsNullOrEmpty(controller.PendingDecisionId);
        }

        void OnApprove()
        {
            if (!CanAct()) return;
            Heard?.Invoke("heard: approve");
            controller.ApprovePending();   // routes through the gateway -> backend authorizes
        }

        void OnReject()
        {
            if (!CanAct()) return;
            Heard?.Invoke("heard: reject");
            controller.RejectPending();
        }

        void OnAskWhy(string question)
        {
            if (!CanAct()) return;
            Heard?.Invoke("asked: " + (string.IsNullOrEmpty(question) ? "why?" : question));
            controller.AskPending(
                string.IsNullOrEmpty(question) ? "Why do you recommend this?" : question,
                _ => { /* answer surfaced by the panel via SessionController.AskPending callback wiring */ });
        }

        void OnListeningChanged(bool listening) => Heard?.Invoke(listening ? "listening…" : "");
    }
}
