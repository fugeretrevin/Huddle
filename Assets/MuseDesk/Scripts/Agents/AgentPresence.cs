using UnityEngine;
using MuseDesk.Feedback;

namespace MuseDesk.Agents
{
    /// <summary>
    /// One character's reactions to the person: a glance with its own personality, the poke
    /// bounce, a wave back, a duck under a fast hand, a reach up to a held-out fingertip, the
    /// acknowledging nod when it is addressed, and the small idle gestures. One per agent, next to
    /// AgentView. AgentAttention (one in the scene) senses the user and calls the methods here.
    ///
    /// Everything is a small procedural layer applied in LateUpdate after the Animator. It never
    /// changes the Animator's state, the agent's state or its position, so the work clips and the
    /// pipeline choreography play exactly as before:
    ///
    ///   Armature (the skeleton's top transform, 100x scale): a multiplicative local scale for the
    ///       squash and stretch, through BoneScaleLayer (it multiplies whatever scale is there).
    ///   Spine, Chest: a turn of the upper body toward the user (at most 26 degrees in total),
    ///       a forward duck / backward stretch (at most 12 degrees) and a wobble (4 degrees). Handed
    ///       to AgentIdleLife.SetReaction when that component runs (one writer per bone), else
    ///       written with this component's own layers.
    ///   Head: a tilt, nod or wobble (at most 10 degrees), handed to AgentGaze.SetExpression, which
    ///       clamps it together with the look-at inside its hard limits (60 yaw / 30 pitch).
    ///   UpperArm_L/R, Forearm_L/R: the wave, the reach and the point. Each is a rotation from
    ///       where the clip put the arm toward a wanted direction, by world positions, so it does
    ///       not depend on bone axes or the armature scale.
    ///
    /// The pipeline always wins. While the agent walks, is away from its desk, holds or offers the
    /// paper, plays a reaction clip, stands in a handoff, or while the completion sequence or an
    /// office move runs, no reaction starts and one in flight is dropped at once. A poke or an
    /// address that arrives then is kept for a short while and played when the agent is free.
    /// The only exception is <see cref="PresentPaper"/>, for the agent that stands waiting on the
    /// user's decision.
    /// </summary>
    [DefaultExecutionOrder(420)]
    [DisallowMultipleComponent]
    public class AgentPresence : MonoBehaviour
    {
        public enum Personality { Researcher, Developer, Tester }

        [Header("Wiring (found on this object if left empty)")]
        public AgentView view;
        public AgentMovement movement;
        public AgentAnimatorBridge bridge;
        public AgentGaze gaze;
        public AgentIdleLife idle;

        [Header("Rig (assigned by AgentScripts/BuildAgentInteractionV1; found by name if left empty)")]
        public Transform armature;
        public Transform head;
        public Transform headTop;
        public Transform chest;
        public Transform spine;
        public Transform upperArmL, forearmL, handL;
        public Transform upperArmR, forearmR, handR;

        [Header("Touch volume and signs")]
        [Tooltip("Trigger collider around the head and body. A fingertip inside it is a poke.")]
        public Collider pokeVolume;
        public AgentSpeechBubble bubble;
        public AgentStatusChip chip;

        [Header("Feel")]
        public Personality personality = Personality.Researcher;
        [Tooltip("Largest turn of the upper body toward the user, degrees (spine and chest together).")]
        public float maxBodyYaw = 26f;
        [Tooltip("Seconds between two awareness glances of this agent.")]
        public float glanceCooldown = 5f;

        // ── reaction channels ──
        enum Body { None, Bounce, Duck, Laugh, Perk, Stretch, Dizzy, Present }
        enum Head { None, Curious, Nod, Dizzy, Laugh }
        enum Arm { None, Wave, Reach, Point }
        enum Queued { None, Poke, Acknowledge }

        Body _body; float _bodyStart, _bodyDur;
        Head _headKind; float _headStart, _headDur;
        Arm _arm; float _armStart, _armDur; bool _armRight; Vector3 _armPoint; Transform _armTarget;
        float _faceUntil, _faceW, _faceYaw;
        bool _awayOk;                 // the running reaction is one that may play away from the desk
        Queued _queued; float _queuedUntil;
        bool _dizzyNext;
        bool _wasBlocked;
        bool _exprSet, _reactSet, _scaleSet;
        Cooldown _glanceCd;

        BoneScaleLayer _scaleLayer;
        BoneRotationLayer _spineLayer, _chestLayer;
        BoneRotationLayer _upperL, _foreL, _upperR, _foreR;
        int _upAxis = 1;

        // highlight
        SkinnedMeshRenderer[] _renderers;
        Color[][] _baseColors;
        MaterialPropertyBlock _mpb;
        float _hl, _hlGoal;
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        // squash and stretch curves: time (0..1) and vertical factor
        static readonly float[] BounceT = { 0f, 0.18f, 0.46f, 0.72f, 1f };
        static readonly float[] BounceV = { 1f, 0.80f, 1.14f, 0.96f, 1f };
        static readonly float[] DuckT = { 0f, 0.22f, 0.62f, 1f };
        static readonly float[] DuckV = { 1f, 0.85f, 0.88f, 1f };
        static readonly float[] PerkT = { 0f, 0.3f, 1f };
        static readonly float[] PerkV = { 1f, 1.06f, 1f };
        static readonly float[] StretchT = { 0f, 0.35f, 0.7f, 1f };
        static readonly float[] StretchV = { 1f, 1.05f, 1.05f, 1f };

        /// <summary>Set every frame by AgentAttention: the completion sequence or an office move is
        /// running, so nobody reacts.</summary>
        public bool GlobalBlock { get; set; }

        /// <summary>Time of the next idle gesture (owned by AgentAttention's idle director).</summary>
        public float NextIdleAt { get; set; }

        /// <summary>"Researcher" / "Developer" / "Tester", from the agent id.</summary>
        public string Role { get; private set; } = AgentStatusSentence.Researcher;

        /// <summary>The pipeline is using the body in a way nothing may layer on: walking, a
        /// reaction clip, a handoff next to a colleague, the completion sequence, an office move.</summary>
        public bool HardBusy
        {
            get
            {
                if (GlobalBlock) return true;
                if (movement != null && movement.IsMoving) return true;
                if (bridge != null && bridge.ReactionPlaying) return true;
                if (AgentHandoffController.AnyBusy && (gaze == null || gaze.HasNearbyAgent
                    || (movement != null && !movement.AtHome))) return true;
                return false;
            }
        }

        /// <summary>True while the pipeline owns this character (see the class comment).</summary>
        public bool PipelineBusy
        {
            get
            {
                if (HardBusy) return true;
                if (movement != null && !movement.AtHome) return true;
                if (bridge != null && bridge.IsOffering) return true;
                if (gaze != null && gaze.IsHoldingPaper) return true;
                return false;
            }
        }

        public bool CanReact => isActiveAndEnabled && !PipelineBusy;
        public bool IsWorking => bridge != null && bridge.IsWorkingState;
        public bool Reacting => _body != Body.None || _headKind != Head.None || _arm != Arm.None || Time.time < _faceUntil;

        /// <summary>A point above the head (world): where a chip or bubble may start.</summary>
        public Vector3 TopPoint
        {
            get
            {
                if (headTop != null) return headTop.position;
                if (head != null)
                {
                    Vector3 h = head.position;
                    float up = Mathf.Max(0.01f, h.y - transform.position.y);
                    return h + Vector3.up * (up * 0.65f);
                }
                return transform.position + Vector3.up * 0.1f;
            }
        }

        public Vector3 HeadPoint => head != null ? head.position : TopPoint;
        public Vector3 CenterPoint => chest != null ? chest.position : (transform.position + TopPoint) * 0.5f;
        public float Height => Mathf.Max(0.03f, TopPoint.y - transform.position.y);

        // ───────────────────────── lifecycle ─────────────────────────

        void Awake()
        {
            if (view == null) view = GetComponent<AgentView>();
            if (movement == null) movement = GetComponent<AgentMovement>();
            if (bridge == null) bridge = GetComponent<AgentAnimatorBridge>();
            if (gaze == null) gaze = GetComponent<AgentGaze>();
            if (idle == null) idle = GetComponent<AgentIdleLife>();
            ResolveRig();

            if (view != null)
            {
                string role = AgentStatusSentence.NormalizeRole(view.agentId);
                if (role != null) Role = role;
                personality = Role == AgentStatusSentence.Developer ? Personality.Developer
                            : Role == AgentStatusSentence.Tester ? Personality.Tester
                            : Personality.Researcher;
            }

            if (armature != null)
            {
                // Which local axis of the armature is "up" for the character (an imported armature
                // is often rotated, so it is not always Y).
                Vector3 up = armature.InverseTransformDirection(transform.up);
                float ax = Mathf.Abs(up.x), ay = Mathf.Abs(up.y), az = Mathf.Abs(up.z);
                _upAxis = ax > ay && ax > az ? 0 : (az > ay ? 2 : 1);
            }

            _renderers = GetComponentsInChildren<SkinnedMeshRenderer>(true);
            _baseColors = new Color[_renderers.Length][];
            for (int i = 0; i < _renderers.Length; i++)
            {
                Material[] mats = _renderers[i].sharedMaterials;
                _baseColors[i] = new Color[mats.Length];
                for (int m = 0; m < mats.Length; m++)
                    _baseColors[i][m] = mats[m] != null && mats[m].HasProperty(BaseColorId)
                        ? mats[m].GetColor(BaseColorId) : Color.white;
            }
            _mpb = new MaterialPropertyBlock();
            NextIdleAt = Time.time + Random.Range(5f, 14f);
        }

        /// <summary>Find the bones by name (docs/Art/RIG_SPECIFICATION.md). Safe to call again.</summary>
        public void ResolveRig()
        {
            Transform[] all = GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                Transform t = all[i];
                switch (t.name)
                {
                    case "Armature": if (armature == null) armature = t; break;
                    case "Head": if (head == null) head = t; break;
                    case "Accessory_Tip": if (headTop == null) headTop = t; break;
                    case "Chest": if (chest == null) chest = t; break;
                    case "Spine": if (spine == null) spine = t; break;
                    case "UpperArm_L": if (upperArmL == null) upperArmL = t; break;
                    case "Forearm_L": if (forearmL == null) forearmL = t; break;
                    case "Hand_L": if (handL == null) handL = t; break;
                    case "UpperArm_R": if (upperArmR == null) upperArmR = t; break;
                    case "Forearm_R": if (forearmR == null) forearmR = t; break;
                    case "Hand_R": if (handR == null) handR = t; break;
                }
            }
        }

        void OnDisable()
        {
            CancelReactions();
            ReleaseAll();
            _hl = _hlGoal = 0f;
            ApplyHighlight(0f);
        }

        // ───────────────────────── reactions (called by AgentAttention) ─────────────────────────

        /// <summary>A glance at <paramref name="target"/> in this character's own manner:
        /// the Researcher looks with a curious tilt, the Developer glances briefly and goes back to
        /// its work, the Tester perks up. Rate-limited per agent. Returns true if it played.</summary>
        public bool Glance(Transform target)
        {
            float now = Time.time;
            if (!CanReact || target == null || gaze == null || !gaze.isActiveAndEnabled) return false;
            if (Reacting || !_glanceCd.TryFire(now, glanceCooldown)) return false;
            switch (personality)
            {
                case Personality.Developer:
                    gaze.SetAttention(target, 0.7f, 0.75f);
                    break;
                case Personality.Tester:
                    gaze.SetAttention(target, 1.4f, 1f);
                    StartBody(Body.Perk, 0.5f, now);
                    break;
                default:
                    gaze.SetAttention(target, 1.7f, 0.9f);
                    StartHead(Head.Curious, 1.7f, now);
                    break;
            }
            return true;
        }

        /// <summary>The user poked this character. <paramref name="streak"/> 0 = first poke: bounce,
        /// turn to the user, wave (or nod, when its hands are busy working). 1 and more = a second
        /// poke within two seconds: a dizzy wobble or a laugh, alternating. Kept for 1.5 s if the
        /// pipeline owns the character right now. Returns true if it played at once.</summary>
        public bool Poke(int streak)
        {
            float now = Time.time;
            if (!CanReact)
            {
                if (streak == 0) { _queued = Queued.Poke; _queuedUntil = now + 1.5f; }
                return false;
            }
            _awayOk = false;
            if (streak <= 0)
            {
                StartBody(Body.Bounce, 0.5f, now);
                FaceUser(2.6f, now);
                if (IsWorking) StartHead(Head.Nod, 0.8f, now + 0.3f);
                else StartArm(Arm.Wave, 1.5f, now + 0.25f, true);
                FeedbackController.PlayCue(FeedbackCue.KeyPress);
            }
            else
            {
                _arm = Arm.None;
                if (_dizzyNext) { StartBody(Body.Dizzy, 1.3f, now); StartHead(Head.Dizzy, 1.3f, now); }
                else { StartBody(Body.Laugh, 1.0f, now); StartHead(Head.Laugh, 1.0f, now); }
                _dizzyNext = !_dizzyNext;
                FaceUser(1.6f, now);
            }
            return true;
        }

        /// <summary>The user addressed this character: it stops fidgeting, turns head and upper body
        /// to the user, nods once and holds for about four seconds. Its base animation is never
        /// changed, so afterwards it is simply doing what the run state says. Kept for 6 s if the
        /// pipeline owns the character right now.</summary>
        public bool Acknowledge(float holdSeconds = 4f)
        {
            float now = Time.time;
            if (!CanReact)
            {
                _queued = Queued.Acknowledge; _queuedUntil = now + 6f;
                return false;
            }
            _awayOk = false;
            _arm = Arm.None;
            _body = Body.None;
            if (idle != null) idle.SuppressFidgets(holdSeconds + 1f);
            FaceUser(holdSeconds, now);
            StartHead(Head.Nod, 0.8f, now + 0.35f);
            StartBody(Body.Perk, 0.5f, now);
            NextIdleAt = Mathf.Max(NextIdleAt, now + holdSeconds + 2f);
            return true;
        }

        /// <summary>Wave back at the user after <paramref name="delay"/> seconds.</summary>
        public bool WaveBack(float delay = 0f)
        {
            float now = Time.time;
            if (!CanReact || _arm != Arm.None) return false;
            _awayOk = false;
            FaceUser(1.9f + delay, now);
            StartArm(Arm.Wave, 1.5f, now + delay, true);
            return true;
        }

        /// <summary>A hand swept fast right over this character: it ducks a little and looks at it.</summary>
        public bool Duck(Transform hand)
        {
            float now = Time.time;
            if (!CanReact || _body == Body.Duck) return false;
            _awayOk = false;
            _arm = Arm.None;
            StartBody(Body.Duck, 0.6f, now);
            if (hand != null && gaze != null && gaze.isActiveAndEnabled) gaze.SetAttention(hand, 0.8f, 0.9f);
            return true;
        }

        /// <summary>A fingertip is held out in front of this (idle) character: it reaches up and taps it.</summary>
        public bool HighFive(Vector3 worldPoint)
        {
            float now = Time.time;
            if (!CanReact || IsWorking || _arm != Arm.None) return false;
            _awayOk = false;
            _armPoint = worldPoint;
            _armTarget = null;
            StartArm(Arm.Reach, 0.9f, now, NearerArmIsRight(worldPoint));
            StartBody(Body.Perk, 0.5f, now + 0.25f);
            FaceUser(1.2f, now);
            return true;
        }

        /// <summary>Idle only: a small stretch.</summary>
        public bool Stretch()
        {
            float now = Time.time;
            if (!CanReact || IsWorking || Reacting) return false;
            _awayOk = false;
            StartBody(Body.Stretch, 1.6f, now);
            return true;
        }

        /// <summary>Look at something (a colleague) for a moment. Head only.</summary>
        public bool LookAt(Transform target, float seconds, float weight)
        {
            if (!CanReact || Reacting || target == null || gaze == null || !gaze.isActiveAndEnabled) return false;
            if (gaze.HasExplicitAttention) return false;
            gaze.SetAttention(target, seconds, weight);
            return true;
        }

        /// <summary>
        /// For the agent that is waiting on the user's decision. While it still holds the paper: a
        /// small forward "here it is" with a nod (its arms stay on the paper). Once the user has
        /// taken the paper: it points at the paper with its nearer arm and looks at it, then back at
        /// the user. Allowed away from the desk, never while walking or during a reaction clip.
        /// </summary>
        public bool PresentPaper(Transform paper)
        {
            float now = Time.time;
            if (!isActiveAndEnabled || HardBusy || Reacting) return false;
            _awayOk = true;
            bool holding = gaze != null && gaze.IsHoldingPaper;
            if (holding || paper == null)
            {
                StartBody(Body.Present, 1.1f, now);
                StartHead(Head.Nod, 0.8f, now + 0.15f);
            }
            else
            {
                _armTarget = paper;
                StartArm(Arm.Point, 1.4f, now, NearerArmIsRight(paper.position));
                if (gaze != null && gaze.isActiveAndEnabled) gaze.SetAttention(paper, 0.9f, 0.8f);
            }
            return true;
        }

        /// <summary>Gaze dwell highlight: the character brightens slightly. Eased; no material instance.</summary>
        public void SetHighlight(bool on) { _hlGoal = on ? 1f : 0f; }

        /// <summary>Drop every reaction in flight (the pipeline took the character).</summary>
        public void CancelReactions()
        {
            _body = Body.None; _headKind = Head.None; _arm = Arm.None;
            _faceUntil = 0f;
            _awayOk = false;
        }

        // ───────────────────────── internals ─────────────────────────

        void StartBody(Body kind, float seconds, float at) { _body = kind; _bodyStart = at; _bodyDur = seconds; }
        void StartHead(Head kind, float seconds, float at) { _headKind = kind; _headStart = at; _headDur = seconds; }
        void StartArm(Arm kind, float seconds, float at, bool right)
        {
            _arm = kind; _armStart = at; _armDur = seconds; _armRight = right;
        }

        void FaceUser(float seconds, float now)
        {
            _faceUntil = Mathf.Max(_faceUntil, now + seconds);
            if (gaze != null && gaze.isActiveAndEnabled) gaze.SetAttentionOnUser(seconds, 1f);
        }

        bool NearerArmIsRight(Vector3 worldPoint)
        {
            if (upperArmR == null) return false;
            if (upperArmL == null) return true;
            return (upperArmR.position - worldPoint).sqrMagnitude <= (upperArmL.position - worldPoint).sqrMagnitude;
        }

        // 0..1 progress of a channel, or -1 before its (delayed) start.
        static float Progress(float now, float start, float dur)
        {
            if (now < start) return -1f;
            return dur <= 1e-4f ? 1f : (now - start) / dur;
        }

        void LateUpdate()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            float now = Time.time;

            bool blocked = _awayOk ? HardBusy : PipelineBusy;
            if (blocked)
            {
                if (!_wasBlocked || Reacting) { CancelReactions(); ReleaseAll(); }
                _wasBlocked = true;
                _faceW = 0f;
                TickHighlight(dt);
                return;
            }
            _wasBlocked = false;

            if (_queued != Queued.None)
            {
                Queued q = _queued;
                _queued = Queued.None;
                if (now <= _queuedUntil)
                {
                    if (q == Queued.Poke) Poke(0); else Acknowledge();
                }
            }

            // ── body: squash and stretch, torso pitch and roll ──
            float vertical = 1f, pitch = 0f, roll = 0f;
            if (_body != Body.None)
            {
                float t = Progress(now, _bodyStart, _bodyDur);
                if (t >= 1f) _body = Body.None;
                else if (t >= 0f)
                {
                    float env = Mathf.Sin(t * Mathf.PI);
                    switch (_body)
                    {
                        case Body.Bounce: vertical = AgentViewingMath.Keys(BounceT, BounceV, t); break;
                        case Body.Duck:
                            vertical = AgentViewingMath.Keys(DuckT, DuckV, t);
                            pitch = 12f * Mathf.Clamp01((1f - vertical) / 0.15f);
                            break;
                        case Body.Perk:
                            vertical = AgentViewingMath.Keys(PerkT, PerkV, t);
                            pitch = -4f * env;
                            break;
                        case Body.Stretch:
                            vertical = AgentViewingMath.Keys(StretchT, StretchV, t);
                            pitch = -8f * env;
                            break;
                        case Body.Laugh:
                            vertical = 1f + 0.05f * Mathf.Sin(t * 6f * 6.2831853f) * env;
                            pitch = -3f * env;
                            break;
                        case Body.Dizzy:
                            roll = 4f * Mathf.Sin(t * 2.2f * 6.2831853f) * env;
                            break;
                        case Body.Present:
                            pitch = 5f * env;
                            break;
                    }
                }
            }
            pitch = Mathf.Clamp(pitch, -12f, 12f);

            if (armature != null)
            {
                if (Mathf.Abs(vertical - 1f) < 0.0005f)
                {
                    if (_scaleSet) { _scaleLayer.Release(armature); _scaleSet = false; }
                }
                else
                {
                    float side = AgentViewingMath.SideForVertical(vertical);
                    Vector3 m = new Vector3(side, side, side);
                    m[_upAxis] = vertical;
                    _scaleLayer.Apply(armature, m);
                    _scaleSet = true;
                }
            }

            // ── torso: turn toward the user ──
            _faceW = Mathf.MoveTowards(_faceW, now < _faceUntil ? 1f : 0f, dt / 0.3f);
            if (_faceW > 0.001f)
            {
                Transform user = gaze != null ? gaze.UserHead : null;
                if (user != null)
                {
                    Vector3 from = chest != null ? chest.position : transform.position;
                    Vector3 local = transform.InverseTransformDirection(user.position - from);
                    float yaw = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
                    // Someone behind the character: do not twist the body round, the head alone lets go too.
                    float want = Mathf.Abs(yaw) < 120f ? Mathf.Clamp(yaw, -maxBodyYaw, maxBodyYaw) : 0f;
                    _faceYaw = Mathf.Lerp(_faceYaw, want, 1f - Mathf.Exp(-dt / 0.15f));
                }
            }
            else _faceYaw = 0f;

            float bodyYaw = _faceYaw * _faceW;
            bool torso = Mathf.Abs(bodyYaw) > 0.02f || Mathf.Abs(pitch) > 0.02f || Mathf.Abs(roll) > 0.02f;
            if (torso)
            {
                Vector3 up = transform.up, right = transform.right, fwd = transform.forward;
                Quaternion spineQ = Quaternion.AngleAxis(bodyYaw * 0.4f, up);
                Quaternion chestQ = Quaternion.AngleAxis(bodyYaw * 0.6f, up)
                                  * Quaternion.AngleAxis(pitch, right)
                                  * Quaternion.AngleAxis(roll, fwd);
                if (idle != null && idle.isActiveAndEnabled)
                {
                    ReleaseOwnTorso();
                    idle.SetReaction(spineQ, chestQ);
                    _reactSet = true;
                }
                else
                {
                    if (spine != null) _spineLayer.Apply(spine, spineQ);
                    if (chest != null) _chestLayer.Apply(chest, chestQ);
                }
            }
            else
            {
                if (_reactSet) { if (idle != null) idle.ClearReaction(); _reactSet = false; }
                ReleaseOwnTorso();
            }

            // ── head: tilt, nod, wobble ──
            float hRoll = 0f, hPitch = 0f, hYaw = 0f;
            if (_headKind != Head.None)
            {
                float t = Progress(now, _headStart, _headDur);
                if (t >= 1f) _headKind = Head.None;
                else if (t >= 0f)
                {
                    float env = Mathf.Sin(t * Mathf.PI);
                    switch (_headKind)
                    {
                        case Head.Curious: hRoll = 9f * Mathf.Clamp01(env * 1.6f); break;
                        case Head.Nod: hPitch = 6f * 0.5f * (1f - Mathf.Cos(t * 2f * 6.2831853f)); break;
                        case Head.Dizzy:
                            hRoll = 10f * Mathf.Sin(t * 2.2f * 6.2831853f) * env;
                            hYaw = 9f * Mathf.Cos(t * 2.2f * 6.2831853f) * env;
                            break;
                        case Head.Laugh:
                            hPitch = (-4f + 2.5f * Mathf.Sin(t * 6f * 6.2831853f)) * env;
                            break;
                    }
                }
            }
            if (gaze != null)
            {
                bool want = hRoll != 0f || hPitch != 0f || hYaw != 0f;
                if (want) { gaze.SetExpression(hRoll, hPitch, hYaw); _exprSet = true; }
                else if (_exprSet) { gaze.SetExpression(0f, 0f, 0f); _exprSet = false; }
            }

            // ── arm: wave, reach, point ──
            TickArm(now);

            TickHighlight(dt);
        }

        void TickArm(float now)
        {
            // Back to the clip pose first, so the directions below are read from the clip and not
            // from what this component wrote last frame.
            if (upperArmL != null) _upperL.Release(upperArmL);
            if (forearmL != null) _foreL.Release(forearmL);
            if (upperArmR != null) _upperR.Release(upperArmR);
            if (forearmR != null) _foreR.Release(forearmR);
            if (_arm == Arm.None) return;

            float t = Progress(now, _armStart, _armDur);
            if (t >= 1f) { _arm = Arm.None; return; }
            if (t < 0f) return;

            Transform upper = _armRight ? upperArmR : upperArmL;
            Transform fore = _armRight ? forearmR : forearmL;
            Transform hand = _armRight ? handR : handL;
            if (upper == null || fore == null || hand == null) { _arm = Arm.None; return; }

            // Ease in over the first fifth, out over the last quarter.
            float w = Mathf.Clamp01(t / 0.2f) * Mathf.Clamp01((1f - t) / 0.25f);
            w = w * w * (3f - 2f * w);

            Vector3 up = transform.up, right = transform.right, fwd = transform.forward;
            Vector3 mid = chest != null ? chest.position : transform.position;
            float side = Vector3.Dot(upper.position - mid, right) >= 0f ? 1f : -1f;

            Vector3 upperDir, foreDir;
            switch (_arm)
            {
                case Arm.Wave:
                {
                    float swing = Mathf.Sin((now - _armStart) * 3.2f * 6.2831853f);
                    upperDir = right * (side * 0.75f) + up * 0.6f + fwd * 0.25f;
                    foreDir = up + right * (side * (0.15f + 0.45f * swing)) + fwd * 0.2f;
                    w *= 0.95f;
                    break;
                }
                case Arm.Reach:
                {
                    Vector3 to = _armPoint - upper.position;
                    if (to.sqrMagnitude < 1e-8f) to = up;
                    upperDir = to.normalized + up * 0.25f;
                    foreDir = _armPoint - fore.position;
                    if (foreDir.sqrMagnitude < 1e-8f) foreDir = upperDir;
                    w *= 0.9f;
                    break;
                }
                default:
                {
                    Vector3 p = _armTarget != null ? _armTarget.position : _armPoint;
                    Vector3 to = p - upper.position;
                    if (to.sqrMagnitude < 1e-8f) to = fwd;
                    upperDir = to.normalized * 0.8f + fwd * 0.2f - up * 0.2f;
                    foreDir = to;
                    w *= 0.6f;
                    break;
                }
            }

            Vector3 cur = fore.position - upper.position;
            if (cur.sqrMagnitude > 1e-12f)
            {
                Quaternion d = Quaternion.Slerp(Quaternion.identity, Quaternion.FromToRotation(cur, upperDir), w);
                if (_armRight) _upperR.Apply(upper, d); else _upperL.Apply(upper, d);
            }
            Vector3 cur2 = hand.position - fore.position;
            if (cur2.sqrMagnitude > 1e-12f)
            {
                Quaternion d2 = Quaternion.Slerp(Quaternion.identity, Quaternion.FromToRotation(cur2, foreDir), w);
                if (_armRight) _foreR.Apply(fore, d2); else _foreL.Apply(fore, d2);
            }
        }

        void ReleaseOwnTorso()
        {
            if (spine != null) _spineLayer.Release(spine);
            if (chest != null) _chestLayer.Release(chest);
        }

        void ReleaseAll()
        {
            if (armature != null) _scaleLayer.Release(armature);
            _scaleSet = false;
            ReleaseOwnTorso();
            if (_reactSet) { if (idle != null) idle.ClearReaction(); _reactSet = false; }
            if (_exprSet) { if (gaze != null) gaze.SetExpression(0f, 0f, 0f); _exprSet = false; }
            if (upperArmL != null) _upperL.Release(upperArmL);
            if (forearmL != null) _foreL.Release(forearmL);
            if (upperArmR != null) _upperR.Release(upperArmR);
            if (forearmR != null) _foreR.Release(forearmR);
            _faceW = 0f; _faceYaw = 0f;
        }

        // ── highlight ──

        void TickHighlight(float dt)
        {
            if (_hl == _hlGoal) return;
            _hl = Mathf.MoveTowards(_hl, _hlGoal, dt / 0.2f);
            ApplyHighlight(_hl);
        }

        void ApplyHighlight(float k)
        {
            if (_renderers == null || _mpb == null) return;
            Color teal = MuseDesk.Workspace.OfficeTokens.Teal;
            for (int i = 0; i < _renderers.Length; i++)
            {
                SkinnedMeshRenderer r = _renderers[i];
                if (r == null) continue;
                Color[] bases = _baseColors[i];
                for (int m = 0; m < bases.Length; m++)
                {
                    if (k <= 0.001f) { r.SetPropertyBlock(null, m); continue; }
                    Color b = bases[m];
                    // A little brighter and a touch toward teal: reads as a soft rim of light.
                    Color c = new Color(b.r * (1f + 0.20f * k) + teal.r * 0.05f * k,
                                        b.g * (1f + 0.20f * k) + teal.g * 0.05f * k,
                                        b.b * (1f + 0.20f * k) + teal.b * 0.05f * k, b.a);
                    r.GetPropertyBlock(_mpb, m);
                    _mpb.SetColor(BaseColorId, c);
                    r.SetPropertyBlock(_mpb, m);
                }
            }
        }
    }
}
