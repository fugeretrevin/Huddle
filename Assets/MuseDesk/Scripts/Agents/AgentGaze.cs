using System.Collections.Generic;
using UnityEngine;
using MuseDesk.Models;

namespace MuseDesk.Agents
{
    /// <summary>
    /// Head and gaze: a small, clamped, smoothed ADDITIVE aim on the Neck and Head bones, applied in
    /// LateUpdate so it layers on top of whatever clip the Animator just wrote (the clip's own head
    /// motion is kept). Purely presentation: it reads the acknowledged agent state and never changes
    /// or delays it. If the head bone cannot be found the component switches itself off.
    ///
    /// What the agent looks at, highest priority first:
    ///   1. listening / speaking (<see cref="SetListening"/>, <see cref="SetSpeaking"/>) and
    ///      <see cref="SetAttention"/> / <see cref="SetAttentionOnUser"/>: the user (or the given target);
    ///   2. walking: straight ahead, which is where it is going (the body already turns that way);
    ///   3. standing away from its desk: the agent it is handing work to during a handoff; otherwise
    ///      the user, with a short look down at the decision paper first while it still holds it;
    ///   4. a fingertip within reach of its head;
    ///   5. during a handoff, the colleague that has walked up to it;
    ///   6. waiting / error: the user (it needs them), weaker in focus mode;
    ///   7. at its desk: its work (the clip's own head pose), with a short glance at the user every
    ///      6 to 14 s, timed independently per agent. Fewer and weaker in focus mode.
    /// The aim fades out during one-shot reaction clips and while walking.
    ///
    /// Also owns the voice visuals: listening is a still head tilt toward the user; speaking adds a
    /// small nod. Neither does anything unless a caller turns it on.
    /// </summary>
    [DefaultExecutionOrder(310)]
    [DisallowMultipleComponent]
    public class AgentGaze : MonoBehaviour
    {
        [Header("Rig (assigned by AgentScripts/BuildAgentLiveliness; found by name if left empty)")]
        public Transform head;
        public Transform neck;

        [Header("Wiring (all optional except the head)")]
        public AgentView view;
        public AgentMovement movement;
        public AgentAnimatorBridge bridge;
        [Tooltip("The decision paper (scene object DecisionDocument). While it is parented under this agent, the agent is holding it.")]
        public Transform paper;
        [Tooltip("Optional: something to look at while working (a monitor). Empty = the working clip's own head pose.")]
        public Transform workTarget;

        [Header("Limits and feel")]
        [Tooltip("Largest head turn left/right, degrees.")]
        public float maxYaw = 55f;
        [Tooltip("Largest head tilt up/down, degrees.")]
        public float maxPitch = 30f;
        [Tooltip("Smoothing time constant, seconds. The head never snaps.")]
        public float smoothTime = 0.2f;
        [Range(0f, 1f)]
        [Tooltip("Share of the turn taken by the neck; the head takes the rest.")]
        public float neckShare = 0.35f;

        [Header("Glances at the user while at the desk")]
        public float glanceIntervalMin = 6f;
        public float glanceIntervalMax = 14f;
        public float glanceLengthMin = 0.8f;
        public float glanceLengthMax = 1.4f;
        [Tooltip("In focus mode glances come this many times less often.")]
        public float focusGlanceSlowdown = 2.5f;

        [Header("Hands and other agents")]
        [Tooltip("A fingertip closer than this to the head draws the gaze, metres.")]
        public float handRadius = 0.15f;
        [Tooltip("Another agent closer than this counts as standing with this one, metres. " +
                 "A handoff courier stops 0.18 m from the receiver.")]
        public float agentRadius = 0.26f;

        [Header("Voice visuals")]
        [Tooltip("Head tilt while listening, degrees.")]
        public float listenTilt = 7f;
        [Tooltip("Nod amplitude while speaking, degrees.")]
        public float speakBob = 2.2f;
        [Tooltip("Nods per second while speaking.")]
        public float speakBobRate = 2.6f;

        static readonly List<AgentGaze> All = new List<AgentGaze>(4);

        BoneRotationLayer _neckLayer, _headLayer;
        Transform _user;
        float _nextUserTry;

        // decided a few times a second
        Transform _target;
        float _targetWeight;
        float _nextDecide;
        float _nextGlance, _glanceUntil;
        Transform _hand;
        AgentGaze _nearAgent;
        bool _holding;
        float _holdStart;

        // explicit hooks
        Transform _attention;
        float _attentionUntil;
        float _attentionWeight = 1f;

        // Whatever is serialized on the component, the head never turns further than this.
        const float HardMaxYaw = 60f;
        const float HardMaxPitch = 30f;
        bool _listening, _speaking;

        // smoothed output
        float _yaw, _pitch, _weight, _roll, _bobAmp;
        float _phase, _tiltSign;

        /// <summary>True while <see cref="SetListening"/> is on.</summary>
        public bool IsListening => _listening;
        /// <summary>True while <see cref="SetSpeaking"/> is on.</summary>
        public bool IsSpeaking => _speaking;
        /// <summary>True while the decision paper is parented under this agent.</summary>
        public bool IsHoldingPaper => _holding;
        /// <summary>True while another agent stands within <see cref="agentRadius"/> of this one.</summary>
        public bool HasNearbyAgent => _nearAgent != null;

        // ───────────────────────── public hooks ─────────────────────────

        /// <summary>Look at <paramref name="target"/> for <paramref name="seconds"/>, then go back to normal.</summary>
        public void SetAttention(Transform target, float seconds = 2f)
        {
            SetAttention(target, seconds, 1f);
        }

        /// <summary>Look at <paramref name="target"/> for <paramref name="seconds"/> with a reduced
        /// <paramref name="weight"/> (0..1: a glance instead of a full turn), then go back to normal.
        /// An explicit attention like this one also carries through a reaction clip (at a reduced
        /// weight), so an agent can nod toward a colleague.</summary>
        public void SetAttention(Transform target, float seconds, float weight)
        {
            _attention = target;
            _attentionWeight = Mathf.Clamp01(weight);
            _attentionUntil = Time.time + Mathf.Max(0f, seconds);
            _nextDecide = 0f;
        }

        /// <summary>Look at the user's head for <paramref name="seconds"/> with a reduced weight.</summary>
        public void SetAttentionOnUser(float seconds, float weight)
        {
            ResolveUser();
            SetAttention(_user, seconds, weight);
        }

        /// <summary>Look at the user's head for <paramref name="seconds"/>.</summary>
        public void SetAttentionOnUser(float seconds = 2f)
        {
            ResolveUser();
            SetAttention(_user, seconds);
        }

        public void ClearAttention()
        {
            _attention = null;
            _nextDecide = 0f;
        }

        /// <summary>Voice: the user is talking to this agent. A still, attentive head tilt toward the user.</summary>
        public void SetListening(bool on)
        {
            _listening = on;
            _nextDecide = 0f;
        }

        /// <summary>Voice: this agent is talking. Looks at the user with a small nod.</summary>
        public void SetSpeaking(bool on)
        {
            _speaking = on;
            _nextDecide = 0f;
        }

        /// <summary>The gaze of the agent with this backend id ("researcher", "developer", "tester"), or null.</summary>
        public static AgentGaze ForAgent(string agentId)
        {
            for (int i = 0; i < All.Count; i++)
            {
                AgentGaze g = All[i];
                if (g != null && g.view != null && g.view.agentId == agentId) return g;
            }
            return null;
        }

        /// <summary>Voice: every agent listens (the user is speaking and no agent was addressed).</summary>
        public static void SetListeningAll(bool on)
        {
            for (int i = 0; i < All.Count; i++)
                if (All[i] != null) All[i].SetListening(on);
        }

        /// <summary>Voice: stop every agent's speaking visual.</summary>
        public static void StopSpeakingAll()
        {
            for (int i = 0; i < All.Count; i++)
                if (All[i] != null) All[i].SetSpeaking(false);
        }

        // ───────────────────────── lifecycle ─────────────────────────

        void Awake()
        {
            if (view == null) view = GetComponent<AgentView>();
            if (movement == null) movement = GetComponent<AgentMovement>();
            if (bridge == null) bridge = GetComponent<AgentAnimatorBridge>();
            if (head == null || neck == null)
            {
                Transform[] all = GetComponentsInChildren<Transform>(true);
                for (int i = 0; i < all.Length; i++)
                {
                    if (head == null && all[i].name == "Head") head = all[i];
                    else if (neck == null && all[i].name == "Neck") neck = all[i];
                }
            }
            if (head == null) { enabled = false; return; }   // nothing to aim: stay quiet

            _phase = Random.value;
            _tiltSign = Random.value < 0.5f ? -1f : 1f;
            // First glance at a different moment for each agent.
            _nextGlance = Time.time + Random.Range(glanceIntervalMin * 0.5f, glanceIntervalMax);
            _nextDecide = Time.time + _phase * 0.1f;
        }

        void OnEnable()
        {
            if (!All.Contains(this)) All.Add(this);
        }

        void OnDisable()
        {
            All.Remove(this);
            _neckLayer.Release(neck);
            _headLayer.Release(head);
            _yaw = _pitch = _weight = _roll = _bobAmp = 0f;
            _target = null; _targetWeight = 0f; _nearAgent = null; _hand = null;
        }

        void ResolveUser()
        {
            if (_user != null || Time.unscaledTime < _nextUserTry) return;
            _nextUserTry = Time.unscaledTime + 0.5f;
            Camera cam = Camera.main;
            if (cam != null) _user = cam.transform;
        }

        // ───────────────────────── choosing a target ─────────────────────────

        void Decide(float now)
        {
            ResolveUser();

            bool focus = MuseDesk.Workspace.FocusModeController.Active;
            bool moving = movement != null && movement.IsMoving;
            bool away = movement != null && !movement.AtHome;
            string state = view != null ? view.State : AgentStates.Idle;

            // Is this agent holding the decision paper? (It is parented to a hold point on the agent.)
            bool holding = paper != null && paper.gameObject.activeInHierarchy && paper.IsChildOf(transform);
            if (holding && !_holding) _holdStart = now;
            if (!holding && _holding && bridge != null) bridge.EndOffer();   // the user took it
            _holding = holding;

            _nearAgent = Nearest(away);
            _hand = NearestFingertip();

            Transform t = null;
            float w = 0f;
            bool explicitAttention = false;

            if (_listening || _speaking)
            {
                t = _user; w = 1f;
            }
            else if (_attention != null && now < _attentionUntil)
            {
                t = _attention; w = _attentionWeight;
                explicitAttention = true;
            }
            else if (moving)
            {
                // Look where it is going: the body is already turned along the path.
            }
            else if (away)
            {
                if (AgentHandoffController.AnyBusy && _nearAgent != null)
                {
                    t = _nearAgent.head; w = 0.9f;               // the colleague it is handing work to
                }
                else if (holding && Mathf.Repeat(now - _holdStart, 4.2f) < 0.9f)
                {
                    t = paper; w = 0.8f;                         // a look down at the paper...
                }
                else
                {
                    t = _user; w = 1f;                           // ...then at the user
                }
            }
            else if (_hand != null)
            {
                t = _hand; w = 0.9f;
            }
            else if (AgentHandoffController.AnyBusy && _nearAgent != null)
            {
                t = _nearAgent.head; w = 0.8f;                   // a colleague walked up with a handoff
            }
            else if (state == AgentStates.Waiting || state == AgentStates.Error)
            {
                t = _user; w = focus ? 0.35f : 0.75f;            // it needs the user
            }
            else
            {
                if (now >= _nextGlance)
                {
                    _glanceUntil = now + Random.Range(glanceLengthMin, glanceLengthMax);
                    float slow = focus ? Mathf.Max(1f, focusGlanceSlowdown) : 1f;
                    _nextGlance = _glanceUntil + Random.Range(glanceIntervalMin, glanceIntervalMax) * slow;
                }
                if (now < _glanceUntil)
                {
                    t = _user; w = focus ? 0.5f : 0.85f;
                }
                else if (workTarget != null && bridge != null && bridge.IsWorkingState)
                {
                    t = workTarget; w = 0.5f;
                }
            }

            // A reaction clip (hop, startle, give, receive) owns the head while it plays.
            // An explicit attention (a caller asked for this look) carries through at a reduced
            // weight, layered on the clip, so the agent can nod toward what it was told to look at.
            if (bridge != null && bridge.ReactionPlaying && !_listening && !_speaking)
                w = explicitAttention ? w * 0.6f : 0f;
            if (t == null) w = 0f;

            _target = t;
            _targetWeight = w;
        }

        // Nearest other agent within agentRadius. When this agent is at its desk only agents that
        // have left theirs count (someone walked up); when it is away, any agent counts.
        AgentGaze Nearest(bool anyAgent)
        {
            AgentGaze best = null;
            float bestSqr = agentRadius * agentRadius;
            Vector3 p = transform.position;
            for (int i = 0; i < All.Count; i++)
            {
                AgentGaze o = All[i];
                if (o == null || o == this || o.head == null) continue;
                if (!anyAgent && (o.movement == null || o.movement.AtHome)) continue;
                Vector3 d = o.transform.position - p;
                d.y = 0f;
                float sqr = d.sqrMagnitude;
                if (sqr < bestSqr) { bestSqr = sqr; best = o; }
            }
            return best;
        }

        // Nearest tracked fingertip within handRadius of the head (a little further to let go, so
        // the gaze does not flicker at the edge). The list is resolved and cached by HandFingertips.
        Transform NearestFingertip()
        {
            IReadOnlyList<Transform> tips = HandFingertips.Get();
            if (tips == null || tips.Count == 0) return null;
            Vector3 hp = head.position;
            float keep = handRadius * 1.25f;
            if (_hand != null && (_hand.position - hp).sqrMagnitude < keep * keep) return _hand;
            Transform best = null;
            float bestSqr = handRadius * handRadius;
            for (int i = 0; i < tips.Count; i++)
            {
                Transform tip = tips[i];
                if (tip == null) continue;
                float sqr = (tip.position - hp).sqrMagnitude;
                if (sqr < bestSqr) { bestSqr = sqr; best = tip; }
            }
            return best;
        }

        // ───────────────────────── applying it ─────────────────────────

        void LateUpdate()
        {
            if (head == null) { enabled = false; return; }
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            float now = Time.time;

            if (now >= _nextDecide)
            {
                _nextDecide = now + 0.1f;
                Decide(now);
            }

            // Where the target is, as yaw / pitch from the agent's own forward (+Z is its front).
            float wantYaw = 0f, wantPitch = 0f, wantWeight = 0f;
            if (_target != null && _targetWeight > 0f)
            {
                Vector3 local = transform.InverseTransformDirection(_target.position - head.position);
                float flat = Mathf.Sqrt(local.x * local.x + local.z * local.z);
                if (flat > 1e-5f || Mathf.Abs(local.y) > 1e-5f)
                {
                    float yaw = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
                    float pitch = -Mathf.Atan2(local.y, flat) * Mathf.Rad2Deg;   // up is negative pitch
                    // Something behind the agent: do not crane the neck, just let go.
                    if (Mathf.Abs(yaw) < 120f)
                    {
                        float yawLimit = Mathf.Min(Mathf.Abs(maxYaw), HardMaxYaw);
                        float pitchLimit = Mathf.Min(Mathf.Abs(maxPitch), HardMaxPitch);
                        wantYaw = Mathf.Clamp(yaw, -yawLimit, yawLimit);
                        wantPitch = Mathf.Clamp(pitch, -pitchLimit, pitchLimit);
                        wantWeight = _targetWeight;
                    }
                }
            }

            float wantRoll = _listening ? listenTilt * _tiltSign : (_speaking ? listenTilt * 0.4f * _tiltSign : 0f);
            float wantBob = _speaking ? speakBob : 0f;

            float k = 1f - Mathf.Exp(-dt / Mathf.Max(0.01f, smoothTime));
            _weight = Mathf.Lerp(_weight, wantWeight, k);
            if (wantWeight > 0f)
            {
                _yaw = Mathf.Lerp(_yaw, wantYaw, k);
                _pitch = Mathf.Lerp(_pitch, wantPitch, k);
            }
            // (With no target the last direction is kept and only the weight fades, so the head
            // eases straight back instead of sweeping through an unrelated angle.)
            _roll = Mathf.Lerp(_roll, wantRoll, k);
            _bobAmp = Mathf.Lerp(_bobAmp, wantBob, k);

            float yawOut = _yaw * _weight;
            float pitchOut = _pitch * _weight;
            float bob = 0f;
            if (_bobAmp > 0.01f)
            {
                // A nod with a slowly varying depth so it does not tick like a metronome.
                float t = now + _phase * 10f;
                bob = Mathf.Sin(t * speakBobRate * 6.2831853f) * _bobAmp * (0.65f + 0.35f * Mathf.Sin(t * 1.7f));
            }

            if (Mathf.Abs(yawOut) < 0.02f && Mathf.Abs(pitchOut) < 0.02f && Mathf.Abs(_roll) < 0.02f && bob == 0f)
            {
                _neckLayer.Release(neck);
                _headLayer.Release(head);
                return;
            }

            // The offsets are expressed in the agent's frame and applied as world rotations, so they
            // do not depend on how the bones' own axes are oriented.
            Quaternion rootRot = transform.rotation;
            Quaternion rootInv = Quaternion.Inverse(rootRot);
            float ns = neck != null ? Mathf.Clamp01(neckShare) : 0f;
            if (neck != null)
            {
                Quaternion dn = rootRot * Quaternion.Euler(pitchOut * ns, yawOut * ns, _roll * ns) * rootInv;
                _neckLayer.Apply(neck, dn);
            }
            float hs = 1f - ns;
            Quaternion dh = rootRot * Quaternion.Euler(pitchOut * hs + bob, yawOut * hs, _roll * hs) * rootInv;
            _headLayer.Apply(head, dh);
        }
    }
}
