using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using MuseDesk.Motion;
using MuseDesk.Models;
using MuseDesk.Networking;

namespace MuseDesk.Agents
{
    /// <summary>
    /// Agent-to-agent handoff choreography driven by REAL backend events (never fake progress).
    /// On an approved decision the researcher carries the work to the developer; on a new artifact
    /// the developer carries it to the tester. Movement uses AgentMovement.DeliverTo/ReturnHome and
    /// the reactions use the validated AgentAnimatorBridge give/receive triggers. If the backend
    /// workflow fires without a visual (e.g. an interrupted animation), state is unaffected — this
    /// is presentation only. One handoff runs at a time.
    /// </summary>
    public class AgentHandoffController : MonoBehaviour
    {
        public SessionController controller;
        public AgentRegistry registry;

        [Tooltip("Optional carried token; reparented from source hand to target hand on give. May be null.")]
        public Transform artifactProp;

        [Tooltip("How close (m) the courier stops to the receiver before the give/receive beat.")]
        public float stopDistance = 0.18f;
        [Tooltip("Metres (station-local, scaled with the workspace) the stop point is pulled back toward the agents' " +
                 "side so the courier does not stand on the desk edge.")]
        public float backOffset = 0.015f;
        [Tooltip("Seconds the give/receive pose holds before the courier walks home (the token's flight counts toward it).")]
        public float exchangeHold = 1.8f;
        [Tooltip("Owner (worn rc17): the handoff 'goes really quick between them'. The walk to the receiver takes at least " +
                 "this many seconds (0 = the agent's usual walk time).")]
        public float minApproachSeconds = 2.2f;
        [Tooltip("After arriving, both agents face each other for this long before the give (a readable 'ready' beat).")]
        public float readyBeat = 0.4f;
        [Tooltip("After an approval the Researcher plays its success reaction. The walk to the Developer " +
                 "waits for that reaction, at most this many seconds, so the reaction is not cut to a walk.")]
        public float approvalReactionWait = 0.9f;

        /// <summary>
        /// An authored handoff between two agents (desk choreography layout): where the courier stands, and the
        /// waypoints that take it there without crossing a desk. All points are scene transforms under the office
        /// root, set by a builder; nothing here is a position in code. A pair with no lane uses the old rule (a
        /// straight walk to <see cref="stopDistance"/> in front of the receiver).
        /// </summary>
        [System.Serializable]
        public class Lane
        {
            public string sourceId;
            public string targetId;
            [Tooltip("Where the courier stands for the give / receive beat.")]
            public Transform stand;
            [Tooltip("Waypoints from the courier's home slot to the stand, in order. The walk home reverses them.")]
            public Transform[] fromHome;
            [Tooltip("Waypoints from the delivery spot (AgentRegistry.deliveryAnchor) to the stand, in order. " +
                     "Used when the courier is standing at the delivery spot when the handoff starts.")]
            public Transform[] fromDelivery;
            [Tooltip("Seconds for the walk from the delivery spot to the stand; 0 keeps the agent's usual walk time.")]
            public float fromDeliverySeconds;
        }

        [Header("Authored lanes (optional; set by AgentScripts/DeskChoreographyLayoutV1)")]
        public Lane[] lanes;
        [Tooltip("With a lane: the courier and the receiver turn to face each other for the exchange.")]
        public bool faceEachOther = true;
        [Tooltip("With a lane: a courier that is neither at home nor at the delivery spot walks home first. " +
                 "This is the longest that is waited for, in seconds; after it the handoff is shown no more.")]
        public float homeFirstMaxSeconds = 10f;
        [Tooltip("How close (m, planar) the courier must be to the delivery spot to count as standing on it.")]
        public float deliverySpotTolerance = 0.03f;

        AgentMovement _facedReceiver;

        Lane LaneFor(string sourceId, string targetId)
        {
            if (lanes == null) return null;
            for (int i = 0; i < lanes.Length; i++)
            {
                var l = lanes[i];
                if (l != null && l.stand != null && l.sourceId == sourceId && l.targetId == targetId) return l;
            }
            return null;
        }

        static Transform[] Reversed(Transform[] a)
        {
            if (a == null || a.Length == 0) return null;
            var r = new Transform[a.Length];
            for (int i = 0; i < a.Length; i++) r[i] = a[a.Length - 1 - i];
            return r;
        }

        // The receiver (and a courier still standing at the stand) stop facing each other. Safe to call any time.
        void EndFacing()
        {
            if (_facedReceiver != null) _facedReceiver.StopFacing();
            _facedReceiver = null;
            if (_courier != null) _courier.StopFacing();
        }

        readonly Dictionary<string, AgentView> _byId = new Dictionary<string, AgentView>();
        readonly Queue<(string src, string dst, float settle)> _queue = new Queue<(string, string, float)>();
        bool _bound;
        bool _busy;
        GameObject _destGo;
        Vector3 _propWorldScale = Vector3.one;
        bool _propScaleCaptured;

        /// <summary>True while a handoff owns an agent's movement. AgentRegistry reads this so its
        /// own "react, then walk home" does not cut a handoff walk short.</summary>
        public static bool AnyBusy { get; private set; }

        /// <summary>True from the moment a handoff is queued until its give / receive beat has been
        /// held and the courier turns for home (and no further handoff is queued). The completion
        /// sequence waits for this, so a celebration does not start under an exchange.</summary>
        public static bool ExchangePending { get; private set; }

        enum Stage { Idle, Settling, Approaching, Exchanging, Returning }
        Stage _stage = Stage.Idle;
        AgentMovement _courier;

        /// <summary>
        /// The work has been delivered: a handoff that has not reached its give / receive beat is now
        /// out of date (it would show work being carried to the Tester after the Tester passed it).
        /// Queued handoffs are dropped and a courier that is still waiting or walking over is sent
        /// home; an exchange that is already being held, and a courier already walking home, are left
        /// to finish. Presentation only: the backend transfer happened long before.
        /// </summary>
        public void AbandonPending()
        {
            _queue.Clear();
            if (!_busy) { ExchangePending = false; return; }
            if (_stage == Stage.Settling || _stage == Stage.Approaching)
            {
                StopAllCoroutines();
                EndFacing();
                if (artifactProp != null) artifactProp.gameObject.SetActive(false);
                if (_courier != null && !_courier.AtHome) _courier.ReturnHome();
                if (_destGo != null) { Destroy(_destGo); _destGo = null; }
                _busy = AnyBusy = false;
                _stage = Stage.Idle;
                _courier = null;
                ExchangePending = false;
            }
            else if (_stage != Stage.Exchanging) ExchangePending = false;
        }

        void Update()
        {
            if (!_bound && controller != null) Bind();
        }

        void Bind()
        {
            // Reducer is always constructed on the controller, so it is safe to read here.
            _byId.Clear();
            if (registry != null && registry.agents != null)
                foreach (var a in registry.agents)
                    if (a != null && !string.IsNullOrEmpty(a.agentId)) _byId[a.agentId] = a;

            controller.Reducer.DecisionResolved += OnDecisionResolved;
            controller.Reducer.ArtifactCreated += OnArtifactCreated;
            controller.Reducer.SessionReset += OnSessionReset;
            _bound = true;
        }

        void OnDisable()
        {
            if (_bound && controller != null)
            {
                controller.Reducer.DecisionResolved -= OnDecisionResolved;
                controller.Reducer.ArtifactCreated -= OnArtifactCreated;
                controller.Reducer.SessionReset -= OnSessionReset;
            }
            _bound = false;
            // A disabled component's coroutines stop; don't stay latched busy or leak the marker.
            EndFacing();
            _queue.Clear();
            if (_busy) { AnyBusy = false; ExchangePending = false; }
            _busy = false;
            _stage = Stage.Idle;
            _courier = null;
            if (_destGo != null) { Destroy(_destGo); _destGo = null; }
        }

        // Replay: abandon any hand-off in flight, send couriers home, put the token away.
        void OnSessionReset()
        {
            StopAllCoroutines();
            EndFacing();
            _queue.Clear();
            if (_busy) { AnyBusy = false; ExchangePending = false; }
            _busy = false;
            _stage = Stage.Idle;
            _courier = null;
            if (_destGo != null) { Destroy(_destGo); _destGo = null; }
            if (artifactProp != null) artifactProp.gameObject.SetActive(false);
            foreach (var a in _byId.Values)
            {
                var m = a != null ? a.GetComponent<AgentMovement>() : null;
                if (m != null && !m.AtHome) m.ReturnHome();
            }
        }

        void OnDecisionResolved(string decisionId, string state)
        {
            string s = (state ?? "").ToLowerInvariant();
            if (!s.Contains("approv")) return;                 // only a greenlit decision hands work onward
            // The Researcher reacts to the approval first (AgentRegistry plays its success clip on
            // this same event); starting the walk at once cut that reaction after a single frame.
            TryHandoff("researcher", "developer", Mathf.Max(0f, approvalReactionWait));
        }

        void OnArtifactCreated(EventEnvelope e)
        {
            // A produced artifact moves from the developer to the tester for verification.
            TryHandoff("developer", "tester", 0f);
        }

        void TryHandoff(string sourceId, string targetId, float settle)
        {
            // Focus mode suppresses routine handoff choreography (the backend transfer already
            // happened; this is presentation only, so skipping it changes no task state).
            if (MuseDesk.Workspace.FocusModeController.Active) return;
            if (!_byId.ContainsKey(sourceId) || !_byId.ContainsKey(targetId)) return;
            // Queue rather than drop: the developer→tester handoff routinely arrives while the
            // researcher→developer one is still walking. Bounded so a burst can't back up forever.
            if (_queue.Count >= 4) return;
            _queue.Enqueue((sourceId, targetId, settle));
            ExchangePending = true;
            if (!_busy) StartCoroutine(Drain());
        }

        IEnumerator Drain()
        {
            _busy = AnyBusy = true;
            while (_queue.Count > 0)
            {
                var (s, t, settle) = _queue.Dequeue();
                if (_byId.TryGetValue(s, out var a) && a != null && _byId.TryGetValue(t, out var b) && b != null)
                    yield return HandoffRoutine(a, b, settle);
            }
            _busy = AnyBusy = false;
            ExchangePending = false;
            _stage = Stage.Idle;
            _courier = null;
        }

        IEnumerator HandoffRoutine(AgentView source, AgentView target, float settle)
        {
            var srcMove = source.GetComponent<AgentMovement>();
            var srcBridge = source.GetComponent<AgentAnimatorBridge>();
            var dstBridge = target.GetComponent<AgentAnimatorBridge>();

            if (srcMove == null) yield break;
            _courier = srcMove;
            _stage = Stage.Settling;

            // Let the courier's own reaction to the event that started this handoff read before it
            // walks (walking always wins over a reaction). One frame first: the reaction may be
            // started by another listener of the same event, after this routine began.
            if (settle > 0f && srcBridge != null)
            {
                yield return null;
                float waited = 0f;
                while (waited < settle && srcBridge.ReactionPlaying) { waited += Time.deltaTime; yield return null; }
            }

            // An authored lane for this pair (desk choreography layout): the courier keeps to its waypoints.
            Lane lane = LaneFor(source.agentId, target.agentId);
            Transform[] laneVia = null;
            float laneSeconds = 0f;
            if (lane != null)
            {
                // Where is the courier? At home: the lane from home. Standing on the delivery spot: the lane from there.
                // Anywhere else (walking, or stopped part-way): it walks home first along the lane it is on, so it never
                // cuts across a desk; the handoff then starts from home. Bounded: past the limit the visual is skipped.
                bool onSpot = !srcMove.AtHome && !srcMove.IsMoving && registry != null && registry.deliveryAnchor != null
                              && PlanarDistance(source.transform.position, registry.deliveryAnchor.position) <= deliverySpotTolerance * LaneScale(source.transform);
                if (onSpot) { laneVia = lane.fromDelivery; laneSeconds = lane.fromDeliverySeconds; }
                else
                {
                    if (!srcMove.AtHome || srcMove.IsMoving)
                    {
                        if (!srcMove.IsReturning) srcMove.ReturnHome();
                        float waitHome = 0f;
                        yield return null;
                        while ((srcMove.IsMoving || !srcMove.AtHome) && waitHome < homeFirstMaxSeconds)
                        {
                            // Something else sent it out again (a new decision): this handoff is no longer shown.
                            if (srcMove.IsMoving && !srcMove.IsReturning) yield break;
                            waitHome += Time.deltaTime; yield return null;
                        }
                        if (!srcMove.AtHome || srcMove.IsMoving) yield break;
                    }
                    laneVia = lane.fromHome;
                }
            }

            // The courier carries the token for the whole walk (not just at the give beat).
            if (artifactProp != null)
            {
                var srcHand = FindChild(source.transform, "RightHand_Attach");
                AttachProp(srcHand != null ? srcHand : source.transform, true);
            }

            GameObject destGo = null;
            _stage = Stage.Approaching;
            if (lane != null)
            {
                // The way home from the stand is always the home lane, backwards.
                srcMove.WalkRoute(lane.stand, laneVia, Reversed(lane.fromHome), null, laneSeconds, minApproachSeconds);
            }
            else
            {
                // Destination a short stop-distance in front of the receiver (kept alive for the whole move).
                if (_destGo != null) Destroy(_destGo);
                destGo = _destGo = new GameObject("HandoffDest");
                var dest = destGo.transform;
                Vector3 toward = source.transform.position - target.transform.position;
                toward.y = 0f;
                if (toward.sqrMagnitude < 1e-4f) toward = Vector3.back;
                dest.position = target.transform.position + toward.normalized * stopDistance;
                var frame = source.transform.parent != null ? source.transform.parent : source.transform;
                dest.position += frame.TransformVector(Vector3.back * backOffset);
                dest.position = new Vector3(dest.position.x, source.transform.position.y, dest.position.z);
                srcMove.DeliverTo(dest, null);
            }

            // Wait for arrival (IsMoving falls to false), with a safety guard.
            float guard = 0f;
            yield return null; // let DeliverTo set IsMoving=true first
            while (srcMove.IsMoving && guard < 8f) { guard += Time.deltaTime; yield return null; }

            // Give / receive beat.
            _stage = Stage.Exchanging;
            if (lane != null && faceEachOther)
            {
                // Both turn on the spot to face each other (heading only; the receiver turns back when the beat is over).
                srcMove.FacePoint(target.transform.position);
                var dstMove = target.GetComponent<AgentMovement>();
                if (dstMove != null && dstMove.AtHome && !dstMove.IsMoving)
                {
                    dstMove.FacePoint(source.transform.position);
                    _facedReceiver = dstMove;
                }
            }
            // A short beat while they turn to each other, so the give does not fire the moment the courier stops.
            if (readyBeat > 0f) yield return new WaitForSeconds(readyBeat);
            if (srcBridge != null) srcBridge.PlayHandoffGive();
            if (dstBridge != null) dstBridge.PlayHandoffReceive();

            float held = 0f;
            if (artifactProp != null)
            {
                var targetHand = FindChild(target.transform, "RightHand_Attach");
                var flightFrame = source.transform.parent != null ? source.transform.parent : source.transform;
                float t0 = Time.time;
                yield return FlyToken(targetHand != null ? targetHand : target.transform, flightFrame);
                held = Time.time - t0;
            }

            // Same total hold as before: the flight counts toward it.
            yield return new WaitForSeconds(Mathf.Max(0f, exchangeHold - held));

            // The receiver has the work now; put the token away so it doesn't hang in its hand forever.
            HideToken();

            // The exchange itself is over; only the walk home remains.
            if (_queue.Count == 0) ExchangePending = false;

            _stage = Stage.Returning;
            if (_facedReceiver != null) { _facedReceiver.StopFacing(); _facedReceiver = null; }
            srcMove.ReturnHome();
            guard = 0f;
            yield return null;
            while (srcMove.IsMoving && guard < 6f) { guard += Time.deltaTime; yield return null; }

            if (destGo != null) Destroy(destGo);
            if (_destGo == destGo) _destGo = null;
        }

        // Parent the token to a hand while keeping its authored WORLD size (rig bones are not unit scale).
        // reveal: start collapsed and grow with panelAppear (the first visible frame is never full size).
        void AttachProp(Transform hand, bool reveal = false)
        {
            if (!_propScaleCaptured)
            {
                _propWorldScale = artifactProp.lossyScale;
                // Guard against props authored directly inside Blender's 100x rig bones in scene.
                if (_propWorldScale.magnitude > 3f) _propWorldScale = Vector3.one;
                _propScaleCaptured = true;
            }
            if (_tokenAnim != null) { StopCoroutine(_tokenAnim); _tokenAnim = null; }
            artifactProp.SetParent(hand, false);
            artifactProp.localPosition = Vector3.zero;
            artifactProp.localRotation = Quaternion.identity;
            Vector3 full = FullLocalScale(hand);
            // Scale is set BEFORE activation so a hidden token never shows one frame at full size.
            artifactProp.localScale = reveal ? full * 0.001f : full;
            artifactProp.gameObject.SetActive(true);
            if (reveal) _tokenAnim = StartCoroutine(ScaleToken(full * 0.001f, full, MuseMotionProfile.Active.panelAppear, false));
        }

        Vector3 FullLocalScale(Transform parent)
        {
            Vector3 p = parent.lossyScale;
            return new Vector3(
                _propWorldScale.x / Mathf.Max(1e-5f, Mathf.Abs(p.x)),
                _propWorldScale.y / Mathf.Max(1e-5f, Mathf.Abs(p.y)),
                _propWorldScale.z / Mathf.Max(1e-5f, Mathf.Abs(p.z)));
        }

        // ---- Token presentation (visual only; the exchange timing and fallbacks are unchanged) ----
        const float FlightSeconds = 0.7f;   // was 0.42 (rc17 worn: too quick to follow)
        Coroutine _tokenAnim;

        IEnumerator ScaleToken(Vector3 from, Vector3 to, MotionToken token, bool deactivateAtEnd)
        {
            var clock = new MotionClock();
            clock.Start(token);
            Transform t = artifactProp;
            while (t != null && clock.Running)
            {
                clock.Step(Time.deltaTime);
                t.localScale = Vector3.LerpUnclamped(from, to, clock.Value);
                yield return null;
            }
            if (t != null)
            {
                t.localScale = to;
                if (deactivateAtEnd) t.gameObject.SetActive(false);
            }
            _tokenAnim = null;
        }

        // The token leaves the giver's hand on a short arc and lands in the receiver's hand socket (world-space,
        // InOutCubic, a fixed time), instead of re-parenting hand to hand in one frame.
        IEnumerator FlyToken(Transform toHand, Transform frame)
        {
            Transform t = artifactProp;
            if (_tokenAnim != null) { StopCoroutine(_tokenAnim); _tokenAnim = null; }
            if (t == null || !t.gameObject.activeInHierarchy) { AttachProp(toHand, true); yield break; }
            Vector3 p0 = t.position; Quaternion r0 = t.rotation;
            t.SetParent(frame, true);                       // keeps its world pose and world size
            float arc = 0.045f * (frame != null ? Mathf.Abs(frame.lossyScale.y) : 1f);
            var token = new MotionToken(FlightSeconds, EaseKind.InOutCubic);
            var clock = new MotionClock();
            clock.Start(token);
            while (clock.Running && toHand != null)
            {
                clock.Step(Time.deltaTime);
                float e = clock.Value;
                Vector3 pos = Vector3.LerpUnclamped(p0, toHand.position, e) + Vector3.up * (arc * 4f * e * (1f - e));
                t.SetPositionAndRotation(pos, Quaternion.Slerp(r0, toHand.rotation, e));
                yield return null;
            }
            // Settle exactly in the socket; the pose is already there, so no jump.
            AttachProp(toHand, false);
        }

        // Shrink away (panelHide) then deactivate; never a pop-out.
        void HideToken()
        {
            if (artifactProp == null || !artifactProp.gameObject.activeSelf) return;
            if (_tokenAnim != null) { StopCoroutine(_tokenAnim); _tokenAnim = null; }
            Vector3 cur = artifactProp.localScale;
            _tokenAnim = StartCoroutine(ScaleToken(cur, cur * 0.001f, MuseMotionProfile.Active.panelHide, true));
        }

        static float PlanarDistance(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x, dz = a.z - b.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        // Lane distances are authored at office scale 1; the office may be scaled (Floor mode options).
        static float LaneScale(Transform agent)
        {
            var p = agent.parent;
            return p != null ? Mathf.Max(1e-4f, Mathf.Abs(p.lossyScale.x)) : 1f;
        }

        static Transform FindChild(Transform root, string childName)
        {
            var all = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
                if (all[i].name == childName) return all[i];
            return null;
        }
    }
}
