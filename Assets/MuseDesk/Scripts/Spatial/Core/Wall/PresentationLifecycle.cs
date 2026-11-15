using System;
using System.Collections.Generic;
using UnityEngine;

namespace MuseDesk.Spatial.Wall
{
    public enum PresentationPhase { Idle = 0, Opening = 1, Presenting = 2, Closing = 3 }

    public enum PresentationKind { DeskVisit = 0, Portal = 1 }

    public enum PresentationCloseReason { Finished = 0, Cancelled = 1, BackendLost = 2 }

    public enum PresentationOpenResult { Started = 0, QueuedUntilClosed = 1, AlreadyOpen = 2, RejectedBusy = 3, RejectedInvalid = 4 }

    public struct PresentationClosed
    {
        public string presentationId;
        public string agentId;
        public PresentationKind kind;
        public PresentationCloseReason reason;
        /// <summary>Increments per Open that actually started. A session has exactly one Closed.</summary>
        public int session;
    }

    [Serializable]
    public sealed class PresentationTimings
    {
        public float opening = 0.7f;
        public float closing = 0.6f;
        [Tooltip("Reduced motion / comfort: animation times are multiplied by this.")]
        public float comfortScale = 0.4f;
    }

    /// <summary>
    /// presentation id -> logical agent id, with the invariant that a logical agent has at most ONE live
    /// presentation (the desk-visit avatar and the large portal avatar are two views of one agent, never two
    /// workers). Share one instance between the visit and portal lifecycles.
    /// </summary>
    public sealed class PresentationIdentityMap
    {
        readonly Dictionary<string, string> _agentOf = new Dictionary<string, string>();
        readonly Dictionary<string, string> _presentationOf = new Dictionary<string, string>();

        public int Count => _agentOf.Count;

        /// <summary>Register (idempotent for the same pair). False when the id or the agent is already bound otherwise.</summary>
        public bool TryRegister(string presentationId, string agentId, out string reason)
        {
            reason = null;
            if (string.IsNullOrEmpty(presentationId) || string.IsNullOrEmpty(agentId)) { reason = "Missing id"; return false; }
            string existingAgent, existingPres;
            if (_agentOf.TryGetValue(presentationId, out existingAgent))
            {
                if (existingAgent == agentId) return true;
                reason = "Presentation id belongs to another agent";
                return false;
            }
            if (_presentationOf.TryGetValue(agentId, out existingPres))
            {
                reason = agentId + " already has a presentation (" + existingPres + ")";
                return false;
            }
            _agentOf[presentationId] = agentId;
            _presentationOf[agentId] = presentationId;
            return true;
        }

        public void Release(string presentationId)
        {
            string agent;
            if (presentationId == null || !_agentOf.TryGetValue(presentationId, out agent)) return;
            _agentOf.Remove(presentationId);
            _presentationOf.Remove(agent);
        }

        public string LogicalAgentOf(string presentationId)
        {
            string a; return presentationId != null && _agentOf.TryGetValue(presentationId, out a) ? a : null;
        }

        public string PresentationOf(string agentId)
        {
            string p; return agentId != null && _presentationOf.TryGetValue(agentId, out p) ? p : null;
        }

        /// <summary>True when the two directions agree and no agent has two presentations.</summary>
        public bool IsConsistent()
        {
            if (_agentOf.Count != _presentationOf.Count) return false;
            foreach (var kv in _agentOf)
            {
                string p;
                if (!_presentationOf.TryGetValue(kv.Value, out p) || p != kv.Key) return false;
            }
            return true;
        }

        public void Clear() { _agentOf.Clear(); _presentationOf.Clear(); }
    }

    /// <summary>Data for the view's tether line from the agent's home anchor to its presentation.</summary>
    public struct PresentationTether
    {
        public bool active;
        public Vector3 from;     // home anchor (where the real agent stays)
        public Vector3 to;       // the presentation attachment point
        public float alpha;      // 0..1 line opacity
        public float length;

        public static PresentationTether Compute(PresentationPhase phase, float progress, Vector3 homeAnchor, Vector3 presentationPoint)
        {
            var t = new PresentationTether { from = homeAnchor, to = presentationPoint, length = (presentationPoint - homeAnchor).magnitude };
            switch (phase)
            {
                case PresentationPhase.Opening: t.alpha = Mathf.Clamp01(progress); break;
                case PresentationPhase.Presenting: t.alpha = 1f; break;
                case PresentationPhase.Closing: t.alpha = 1f - Mathf.Clamp01(progress); break;
                default: t.alpha = 0f; break;
            }
            t.active = t.alpha > 0.001f && t.length > 0.02f;
            return t;
        }
    }

    /// <summary>
    /// Presentation lifecycle for the desk visit and the large portal, pure logic.
    /// Idle -> Opening -> Presenting -> Closing -> Idle. Cancel works at any point; a backend loss or
    /// disconnect cleans up immediately; opening while closing queues the request (latest wins) and starts it
    /// after the close finishes. Every started session raises <see cref="Closed"/> exactly once and
    /// <see cref="ReturnHome"/> exactly once (just before it), so the view can always put the presentation
    /// back at its home / hidden state. This class holds no reference to the backend: it cannot start,
    /// pause, stop or duplicate an agent's work; it only reports which presentation is shown.
    /// </summary>
    public sealed class PresentationLifecycle
    {
        readonly PresentationIdentityMap _map;
        float _timer;
        bool _hasPending;
        string _pendingPres, _pendingAgent;
        Vector3 _pendingHome;
        PresentationCloseReason _closeReason;

        public readonly PresentationKind kind;
        public PresentationTimings timings = new PresentationTimings();
        public bool comfortMode;

        public PresentationPhase Phase { get; private set; }
        public string PresentationId { get; private set; }
        public string AgentId { get; private set; }
        public Vector3 HomeAnchor { get; private set; }
        public int Session { get; private set; }
        public string Message { get; private set; }
        /// <summary>True while the presentation is parked at home / hidden (Idle). The view reads this to guarantee a clean state.</summary>
        public bool IsHome => Phase == PresentationPhase.Idle;
        public bool HasPendingOpen => _hasPending;

        public event Action<string, string> Opened;              // presentationId, agentId (entered Presenting)
        public event Action<string> ReturnHome;                  // presentationId: put the presentation back home / hidden
        public event Action<PresentationClosed> Closed;          // exactly once per session

        public PresentationLifecycle(PresentationKind kind, PresentationIdentityMap sharedMap)
        {
            this.kind = kind;
            _map = sharedMap ?? new PresentationIdentityMap();
        }

        float Dur(float t) { return comfortMode ? t * timings.comfortScale : t; }

        public float Progress
        {
            get
            {
                if (Phase == PresentationPhase.Opening) return Mathf.Clamp01(_timer / Mathf.Max(0.01f, Dur(timings.opening)));
                if (Phase == PresentationPhase.Closing) return Mathf.Clamp01(_timer / Mathf.Max(0.01f, Dur(timings.closing)));
                return Phase == PresentationPhase.Presenting ? 1f : 0f;
            }
        }

        public PresentationOpenResult Open(string presentationId, string agentId, Vector3 homeAnchor)
        {
            if (string.IsNullOrEmpty(presentationId) || string.IsNullOrEmpty(agentId)) { Message = "Missing id"; return PresentationOpenResult.RejectedInvalid; }
            switch (Phase)
            {
                case PresentationPhase.Idle:
                    return Start(presentationId, agentId, homeAnchor);
                case PresentationPhase.Closing:
                    _hasPending = true; _pendingPres = presentationId; _pendingAgent = agentId; _pendingHome = homeAnchor;
                    Message = "Opening after this one closes";
                    return PresentationOpenResult.QueuedUntilClosed;
                default:
                    if (presentationId == PresentationId && agentId == AgentId) return PresentationOpenResult.AlreadyOpen;
                    Message = "One presentation at a time";
                    return PresentationOpenResult.RejectedBusy;
            }
        }

        PresentationOpenResult Start(string presentationId, string agentId, Vector3 homeAnchor)
        {
            string reason;
            if (!_map.TryRegister(presentationId, agentId, out reason)) { Message = reason; return PresentationOpenResult.RejectedBusy; }
            PresentationId = presentationId; AgentId = agentId; HomeAnchor = homeAnchor;
            Session++;
            _timer = 0f;
            Phase = PresentationPhase.Opening;
            Message = "";
            return PresentationOpenResult.Started;
        }

        /// <summary>The view finished its opening animation early (or the timer did).</summary>
        public void MarkReady() { if (Phase == PresentationPhase.Opening) EnterPresenting(); }

        /// <summary>Normal end (the Return key, "Send back"). Opening or Presenting go to Closing; otherwise no-op.</summary>
        public void RequestClose() { BeginClosing(PresentationCloseReason.Finished); }

        /// <summary>Cancel at any point. Also withdraws a queued open. Idle: only clears the queue.</summary>
        public void Cancel()
        {
            _hasPending = false;
            BeginClosing(PresentationCloseReason.Cancelled);
        }

        /// <summary>Backend lost, disconnect, tracking/session teardown: cleanup immediately (no animation), exactly one Closed.</summary>
        public void BackendLost()
        {
            _hasPending = false;
            if (Phase == PresentationPhase.Idle) return;
            _closeReason = PresentationCloseReason.BackendLost;
            Finish();
        }

        void BeginClosing(PresentationCloseReason why)
        {
            if (Phase != PresentationPhase.Opening && Phase != PresentationPhase.Presenting) return;
            _closeReason = why;
            _timer = 0f;
            Phase = PresentationPhase.Closing;
        }

        void EnterPresenting()
        {
            _timer = 0f;
            Phase = PresentationPhase.Presenting;
            var h = Opened; if (h != null) h(PresentationId, AgentId);
        }

        public void Tick(float dt)
        {
            _timer += Mathf.Max(0f, dt);
            if (Phase == PresentationPhase.Opening && _timer >= Dur(timings.opening)) EnterPresenting();
            else if (Phase == PresentationPhase.Closing && _timer >= Dur(timings.closing)) Finish();
        }

        void Finish()
        {
            if (Phase == PresentationPhase.Idle) return;   // exactly-once guard
            var info = new PresentationClosed { presentationId = PresentationId, agentId = AgentId, kind = kind, reason = _closeReason, session = Session };
            Phase = PresentationPhase.Idle;
            _timer = 0f;
            _map.Release(info.presentationId);
            PresentationId = null; AgentId = null;
            var rh = ReturnHome; if (rh != null) rh(info.presentationId);
            var ch = Closed; if (ch != null) ch(info);

            // A handler may already have opened something; a queued request only starts into a free lifecycle.
            if (_hasPending)
            {
                _hasPending = false;
                if (Phase == PresentationPhase.Idle) Start(_pendingPres, _pendingAgent, _pendingHome);
            }
        }

        /// <summary>Tether line data from the home anchor to a point on the presentation.</summary>
        public PresentationTether Tether(Vector3 presentationPoint)
        {
            return PresentationTether.Compute(Phase, Progress, HomeAnchor, presentationPoint);
        }
    }
}
