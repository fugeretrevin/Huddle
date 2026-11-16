using System;
using System.Collections.Generic;
using UnityEngine;

namespace MuseDesk.Spatial.Wall
{
    /// <summary>How an agent is being PRESENTED. Never how it is working: the backend task is untouched by all of these.</summary>
    public enum AgentPresentation { Home = 0, Selected = 1, CarriedProxy = 2, DeskVisit = 3, LargePortal = 4, Returning = 5 }

    /// <summary>
    /// One presentation per logical agent, and a global cap on how many agents may be away from home at once
    /// (1 initially). A desk visit and a portal of the same agent can never coexist, because an agent has a
    /// single presentation value. Nothing here can create, stop or duplicate a backend worker.
    /// </summary>
    public sealed class AgentPresentationRegistry
    {
        readonly Dictionary<string, AgentPresentation> _state = new Dictionary<string, AgentPresentation>();
        public int maxAway = 1;
        public event Action<string, AgentPresentation> Changed;

        public AgentPresentation Of(string agent)
        {
            AgentPresentation p;
            return agent != null && _state.TryGetValue(agent, out p) ? p : AgentPresentation.Home;
        }

        public int AwayCount
        {
            get { int c = 0; foreach (var kv in _state) if (kv.Value != AgentPresentation.Home) c++; return c; }
        }

        /// <summary>Start presenting an agent away from home. Refused (with a reason) when it is already away or the cap is reached.</summary>
        public bool TryBegin(string agent, AgentPresentation p, out string reason)
        {
            reason = null;
            if (string.IsNullOrEmpty(agent)) { reason = "No agent"; return false; }
            if (p == AgentPresentation.Home) { reason = "Not a visit"; return false; }
            AgentPresentation cur = Of(agent);
            if (cur != AgentPresentation.Home) { reason = Pretty(agent) + " is already " + Words(cur); return false; }
            if (AwayCount >= maxAway) { reason = "One visit at a time: send the other agent back first"; return false; }
            Set(agent, p);
            return true;
        }

        /// <summary>Move an away agent to another away presentation (carried -> desk visit -> returning).</summary>
        public bool Move(string agent, AgentPresentation to)
        {
            if (Of(agent) == AgentPresentation.Home || to == AgentPresentation.Home) return false;
            Set(agent, to);
            return true;
        }

        public void End(string agent)
        {
            if (agent == null || !_state.ContainsKey(agent)) return;
            _state.Remove(agent);
            var h = Changed; if (h != null) h(agent, AgentPresentation.Home);
        }

        public void EndAll()
        {
            var keys = new List<string>(_state.Keys);
            foreach (var k in keys) End(k);
        }

        void Set(string agent, AgentPresentation p)
        {
            _state[agent] = p;
            var h = Changed; if (h != null) h(agent, p);
        }

        public static string Pretty(string agent)
        {
            if (string.IsNullOrEmpty(agent)) return "";
            return char.ToUpperInvariant(agent[0]) + agent.Substring(1);
        }

        public static string Words(AgentPresentation p)
        {
            switch (p)
            {
                case AgentPresentation.Selected: return "selected";
                case AgentPresentation.CarriedProxy: return "being carried";
                case AgentPresentation.DeskVisit: return "visiting your desk";
                case AgentPresentation.LargePortal: return "in the portal";
                case AgentPresentation.Returning: return "on the way home";
                default: return "at home";
            }
        }
    }

    public enum VisitState { Off = 0, Armed = 1, Carrying = 2, Perched = 3, Returning = 4 }

    public enum VisitEndReason { None = 0, UserReturn = 1, InvalidDrop = 2, TrackingLost = 3, Cancelled = 4, Timeout = 5, Paused = 6, Recentered = 7, ModeChanged = 8, PortalTakesOver = 9, Preempted = 10 }

    [Serializable]
    public sealed class VisitSettings
    {
        [Tooltip("Select Agents switches itself off after this long without a pick (s).")]
        public float armedTimeout = 20f;
        [Tooltip("Seconds the projection takes to fly back home.")]
        public float returnSeconds = 0.8f;
        [Tooltip("A carry that lasts longer than this is cancelled (s).")]
        public float carryTimeout = 30f;
    }

    /// <summary>
    /// Select Agents and the small desk visit, pure logic. The user arms Select Agents (explicitly), points at
    /// an agent and pinches: a PROJECTION of that agent is carried (the home avatar stays at its desk with a
    /// "visiting" marker). Releasing over a valid desk perch places it there; releasing anywhere else, losing
    /// tracking, a timeout, pause, recenter or a mode change sends the projection home. Only a seen-open pinch
    /// on a tracked hand counts as a release (the hand arbiter guarantees this); a lost hand is a cancel.
    /// </summary>
    public sealed class AgentVisitFlow
    {
        readonly AgentPresentationRegistry _registry;
        float _timer;
        public VisitSettings settings = new VisitSettings();
        public VisitState State { get; private set; }
        public string Agent { get; private set; }
        public string Message { get; private set; }
        public VisitEndReason LastEnd { get; private set; }
        public event Action<VisitState> StateChanged;

        public AgentVisitFlow(AgentPresentationRegistry registry) { _registry = registry; }

        public bool IsArmed => State == VisitState.Armed;
        public bool Busy => State != VisitState.Off;

        /// <summary>The Select Agents control. Refused while something is already being carried or visiting.</summary>
        public bool Arm()
        {
            if (State == VisitState.Carrying || State == VisitState.Returning) { Message = "Finish the current visit first"; return false; }
            if (State == VisitState.Perched) { Message = "Send " + AgentPresentationRegistry.Pretty(Agent) + " back first"; return false; }
            Go(VisitState.Armed);
            Message = "Pinch to bring an agent over";
            return true;
        }

        public void Disarm() { if (State == VisitState.Armed) { Go(VisitState.Off); Message = ""; } }

        /// <summary>A pinch was claimed on an agent while armed. Starts the carry or says why not.</summary>
        public bool Grab(string agent)
        {
            if (State != VisitState.Armed) { Message = "Press Select Agents first"; return false; }
            string reason;
            if (!_registry.TryBegin(agent, AgentPresentation.CarriedProxy, out reason)) { Message = reason; return false; }
            Agent = agent;
            Go(VisitState.Carrying);
            Message = "Let go over your desk";
            return true;
        }

        /// <summary>The claimed pinch was seen to open. <paramref name="overValidPerch"/>: the projection is over a desk perch.</summary>
        public void Release(bool overValidPerch)
        {
            Release(overValidPerch ? VisitDrop.Perch : VisitDrop.Home);
        }

        /// <summary>
        /// The claimed pinch was seen to open. Perch: it (re)lands on the desk perch. Home: a fresh carry was let go away
        /// from the desk (it goes home, nothing changed), or a projection picked up off the perch was let go toward the
        /// wall: the user put it back.
        /// </summary>
        public void Release(VisitDrop drop)
        {
            if (State != VisitState.Carrying) return;
            bool regrab = IsRegrab;
            if (drop == VisitDrop.Perch)
            {
                _registry.Move(Agent, AgentPresentation.DeskVisit);
                IsRegrab = false;
                Go(VisitState.Perched);
                Message = "";
                return;
            }
            if (regrab) BeginReturn(VisitEndReason.UserReturn, "Back to work on the wall");
            else BeginReturn(VisitEndReason.InvalidDrop, "Not a desk spot: going home");
        }

        /// <summary>True while the carried projection was picked up off the perch (not from the wall).</summary>
        public bool IsRegrab { get; private set; }

        /// <summary>Pick the perched projection up again (to move it, or to put it back on the wall). Perched only.</summary>
        public bool Regrab()
        {
            if (State != VisitState.Perched || Agent == null) { Message = "Nothing on your desk to pick up"; return false; }
            if (!_registry.Move(Agent, AgentPresentation.CarriedProxy)) return false;
            IsRegrab = true;
            Go(VisitState.Carrying);
            Message = "Let go over your desk to keep it, or toward the wall to send it back";
            return true;
        }

        /// <summary>The claim was taken away (tracking lost, something picked up, preempted). Never a placement: a fresh
        /// carry goes home; a projection picked up off the perch goes back to the perch it came from (its previous state).</summary>
        public void CancelCarry(VisitEndReason why)
        {
            if (State != VisitState.Carrying) return;
            if (IsRegrab)
            {
                _registry.Move(Agent, AgentPresentation.DeskVisit);
                IsRegrab = false;
                LastEnd = why;
                Go(VisitState.Perched);
                Message = why == VisitEndReason.TrackingLost ? "Hand lost: it stays on your desk" : "Cancelled: it stays on your desk";
                return;
            }
            BeginReturn(why, why == VisitEndReason.TrackingLost ? "Hand lost: going home" : "Cancelled: going home");
        }

        /// <summary>The Return key, or any interruption (pause, recenter, mode change). Safe from every state.</summary>
        public void Return(VisitEndReason why = VisitEndReason.UserReturn)
        {
            if (State == VisitState.Armed) { Go(VisitState.Off); return; }
            if (State == VisitState.Carrying || State == VisitState.Perched) BeginReturn(why, "Going home");
        }

        /// <summary>Hand the agent over to the large portal: the desk visit ends without the fly-home (the portal takes it).</summary>
        public bool HandOverToPortal(out string agent)
        {
            agent = null;
            if (State != VisitState.Perched) return false;
            agent = Agent;
            _registry.End(Agent);
            LastEnd = VisitEndReason.PortalTakesOver;
            Agent = null;
            Go(VisitState.Off);
            return true;
        }

        /// <summary>
        /// Meet (the dock's Meet key, its chooser card, or "Researcher, come here"): make way for a large-portal preview of
        /// <paramref name="agent"/> WITHOUT a hand carry. The caller then opens the preview with <see cref="PortalFlow.BeginPreview"/>,
        /// which reserves the agent in the registry (one presentation per agent, one agent away at a time), so the agent leaves
        /// home once and comes home once; no transient desk visit is created on the way.
        ///   Off ........................ nothing to do.
        ///   Armed ...................... Select Agents is switched off (the user chose the other way in).
        ///   Perched, the same agent .... the desk visit hands over (<see cref="HandOverToPortal"/>, no fly-home);
        ///                                <paramref name="handedOver"/> is true and the caller keeps the conversation.
        ///   Perched, another agent ..... refused: one visit at a time; nothing is sent home behind the user's back.
        ///   Carrying / Returning ....... refused with words.
        /// Never touches the backend task.
        /// </summary>
        public bool PrepareMeet(string agent, out bool handedOver)
        {
            handedOver = false;
            if (string.IsNullOrEmpty(agent)) { Message = "No agent"; return false; }
            switch (State)
            {
                case VisitState.Off:
                    return true;
                case VisitState.Armed:
                    Go(VisitState.Off);
                    Message = "";
                    return true;
                case VisitState.Perched:
                    if (Agent == agent)
                    {
                        string a;
                        handedOver = HandOverToPortal(out a);
                        return handedOver;
                    }
                    Message = "Send " + AgentPresentationRegistry.Pretty(Agent) + " back first";
                    return false;
                case VisitState.Carrying:
                    Message = "Finish the current visit first";
                    return false;
                default:
                    Message = "Wait until " + AgentPresentationRegistry.Pretty(Agent) + " is home";
                    return false;
            }
        }

        void BeginReturn(VisitEndReason why, string msg)
        {
            LastEnd = why;
            Message = msg;
            IsRegrab = false;
            if (Agent != null) _registry.Move(Agent, AgentPresentation.Returning);
            Go(VisitState.Returning);
        }

        public void Tick(float dt)
        {
            _timer += Mathf.Max(0f, dt);
            switch (State)
            {
                case VisitState.Armed:
                    if (_timer > settings.armedTimeout) { LastEnd = VisitEndReason.Timeout; Go(VisitState.Off); Message = "Select Agents timed out"; }
                    return;
                case VisitState.Carrying:
                    if (_timer > settings.carryTimeout) BeginReturn(VisitEndReason.Timeout, "Took too long: going home");
                    return;
                case VisitState.Returning:
                    if (_timer >= settings.returnSeconds)
                    {
                        if (Agent != null) _registry.End(Agent);
                        Agent = null;
                        Go(VisitState.Off);
                    }
                    return;
            }
        }

        /// <summary>0..1 progress of the fly-home (for the runtime's animation).</summary>
        public float ReturnProgress => State == VisitState.Returning ? Mathf.Clamp01(_timer / Mathf.Max(0.01f, settings.returnSeconds)) : 0f;

        void Go(VisitState s)
        {
            _timer = 0f;
            if (s != VisitState.Carrying) IsRegrab = false;
            if (State == s) return;
            State = s;
            var h = StateChanged; if (h != null) h(s);
        }
    }

    /// <summary>What the Meet key does now (<see cref="MeetEntry.Decide"/>).</summary>
    public enum MeetKeyAction { Refuse = 0, OpenChooser = 1, CloseChooser = 2, MeetPerched = 3 }

    /// <summary>
    /// The Meet key's table, free of scene access (rc9: "the agents never used the portal and there was no way to access
    /// that as an option"). One poke on the dock while the habitat is on the wall:
    ///   portal flag off / not on the wall / choosing a wall spot .... refused (words)
    ///   the chooser is open .......................................... closes it (the key is a toggle)
    ///   a portal is open or previewed ................................ refused: finish or cancel that one first
    ///   an agent sits on the desk perch .............................. that agent steps out (no chooser)
    ///   an agent is being carried or flying home ..................... refused: finish the current visit first
    ///   otherwise (Select Agents armed or not) ....................... the chooser opens
    /// </summary>
    public static class MeetEntry
    {
        public static MeetKeyAction Decide(bool portalEnabled, bool wallActive, bool wallPreviewing, bool choosing,
                                           PortalState portal, VisitState visit, out string reason)
        {
            reason = null;
            if (!portalEnabled) { reason = MeetCopy.PortalOff; return MeetKeyAction.Refuse; }
            if (!wallActive || wallPreviewing) { reason = MeetCopy.PlaceFirst; return MeetKeyAction.Refuse; }
            if (choosing) return MeetKeyAction.CloseChooser;
            if (portal != PortalState.Hidden)
            {
                reason = portal == PortalState.Preview || portal == PortalState.AwaitConfirm ? MeetCopy.AnswerCardFirst : MeetCopy.SendBackFirst;
                return MeetKeyAction.Refuse;
            }
            if (visit == VisitState.Perched) return MeetKeyAction.MeetPerched;
            if (visit == VisitState.Carrying || visit == VisitState.Returning) { reason = MeetCopy.FinishVisitFirst; return MeetKeyAction.Refuse; }
            return MeetKeyAction.OpenChooser;
        }
    }

    /// <summary>The Meet feature's words (one place, so the tests can hold them to "one short sentence").</summary>
    public static class MeetCopy
    {
        /// <summary>The dock's status line right after the habitat is placed.</summary>
        public const string PlacedHint = "Press Meet to see an agent beside you.";
        public const string ChooserTitle = "Meet an agent";
        public const string ChooserBody = "Who should step out beside you?\nIt is a projection: their work carries on.";
        public const string ChooserFoot = "Nothing opens until you press Open here.";
        public const string PortalOff = "The portal is switched off in this build";
        public const string PlaceFirst = "Place the habitat on a wall first";
        public const string AnswerCardFirst = "Choose Open here or Cancel on the card first";
        public const string SendBackFirst = "Send the visiting agent back first";
        public const string FinishVisitFirst = "Finish the current visit first";
        public const string Cancel = "Cancel";
        public const string ChooseOnCard = "Choose an agent on the card";
        /// <summary>The chooser's agents, in key order (agent ids as the registry uses them).</summary>
        public static readonly string[] Agents = { "researcher", "developer", "tester", "manager" };

        /// <summary>The voice card's answer when the preview opened.</summary>
        public static string VoiceOpened(string prettyName) { return prettyName + " can step out: press Open here on the card, or Cancel."; }
    }

    public enum SideAskState { Idle = 0, Asking = 1, Answered = 2, Failed = 3 }

    /// <summary>One /btw exchange as shown on the near-user card.</summary>
    public struct SideExchange
    {
        public string requestId;
        public string question;
        public string reply;
        public string label;           // backend-supplied provenance line (A8); shown instead of the derived words when present
        public string provenance;      // LIVE_MODEL, TASK_SNAPSHOT, SCRIPTED_DEMO, OFFLINE
        public string status;          // answered, unavailable, timeout, amendment_suggested, busy, failed
        public bool staleContext;
        public int contextRevision;
        public string amendment;       // a suggested change, never applied
    }

    /// <summary>
    /// The client side of a read-only side conversation: request ids, idempotency keys, one question in flight,
    /// a client deadline, and the short visit history (last N exchanges, cleared on Return). It only builds
    /// requests and records replies; it has no way to change a task.
    /// </summary>
    public sealed class SideConversationSession
    {
        readonly List<SideExchange> _history = new List<SideExchange>();
        int _counter;
        float _askedAt;
        public readonly string agentId;
        public readonly string visitSessionId;
        public string taskId;
        public int contextRevision = -1;
        public int maxHistory = 8;
        public float clientTimeout = 15f;
        public SideAskState State { get; private set; }
        public string PendingRequestId { get; private set; }
        public string PendingQuestion { get; private set; }
        public IList<SideExchange> History => _history;

        public SideConversationSession(string agentId, string visitSessionId)
        {
            this.agentId = agentId;
            this.visitSessionId = visitSessionId;
        }

        /// <summary>Start a question. False (nothing sent) when one is already in flight or the text is empty.</summary>
        public bool TryBegin(string question, float now, out string requestId, out string idempotencyKey)
        {
            requestId = idempotencyKey = null;
            if (State == SideAskState.Asking) return false;
            if (string.IsNullOrEmpty(question) || question.Trim().Length == 0) return false;
            _counter++;
            requestId = visitSessionId + "-q" + _counter;
            idempotencyKey = "btw-" + requestId;
            PendingRequestId = requestId;
            PendingQuestion = question.Trim();
            _askedAt = now;
            State = SideAskState.Asking;
            return true;
        }

        /// <summary>A reply arrived. Ignored unless it answers the question in flight (late replies are dropped).</summary>
        public bool Complete(SideExchange reply)
        {
            if (State != SideAskState.Asking || reply.requestId != PendingRequestId) return false;
            reply.question = PendingQuestion;
            if (reply.contextRevision > contextRevision) contextRevision = reply.contextRevision;
            _history.Add(reply);
            while (_history.Count > maxHistory) _history.RemoveAt(0);
            State = reply.status == "answered" || reply.status == "amendment_suggested" ? SideAskState.Answered : SideAskState.Failed;
            PendingRequestId = null;
            return true;
        }

        /// <summary>No reply in time: the card says so; the task is not affected either way.</summary>
        public bool CheckTimeout(float now)
        {
            if (State != SideAskState.Asking || now - _askedAt < clientTimeout) return false;
            Complete(new SideExchange { requestId = PendingRequestId, reply = "No answer in time. The agent's work is not affected.", provenance = "OFFLINE", status = "timeout" });
            return true;
        }

        public void Clear()
        {
            _history.Clear();
            State = SideAskState.Idle;
            PendingRequestId = null;
            PendingQuestion = null;
        }
    }
}
