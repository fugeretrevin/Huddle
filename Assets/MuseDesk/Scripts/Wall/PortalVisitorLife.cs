using System.Collections.Generic;
using UnityEngine;
using MuseDesk.Agents;

namespace MuseDesk.Wall
{
    /// <summary>
    /// rc12 (owner: "that agent when it's there should still have the physics interactions and the reactions, looking at
    /// you and your hand, so you can talk to them like they're a lively thing"). Life for the full-size portal visitor (and
    /// any other stripped visual clone of an agent). The clone keeps its Animator; this component plays Antigravity's
    /// portal clips by name and layers small procedural motion on the bones in LateUpdate, after the Animator:
    ///
    ///   arrive ........ Portal_Emerge_Step (step out and wave), then the base loop;
    ///   base loop ..... Listening; Thinking while a question is being answered; Portal_Converse_Loop while it answers;
    ///   look .......... head and neck turn toward the user's head (yaw 65, pitch 35 at most), or toward a hand that comes
    ///                   near the agent, or toward the finger that just touched it;
    ///   turn .......... the body turns to keep facing the user if they lean or move;
    ///   breathe ....... a slow chest swell, its own phase;
    ///   touch ......... a fingertip that reaches the body pushes it: the upper body leans away and the agent slides a
    ///                   little (kinematic, springs back; never real physics, never leaves its cleared column by more
    ///                   than <see cref="maxPush"/>); a quick poke plays React_Poke_Surprise;
    ///   wave .......... a raised hand moving side to side near the user's face: React_Wave_Greet back;
    ///   leave ......... Portal_Return_Step (turn and wave goodbye); look and touch fade out.
    ///
    /// Presentation only: it never changes the agent's task, the portal state or anything outside the clone.
    /// Allocation-free per frame. Every size is relative to <see cref="height"/> so a small visitor works too.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PortalVisitorLife : MonoBehaviour
    {
        [Tooltip("World height of the avatar (m). Radii and pushes scale with it.")]
        public float height = 1.05f;
        [Tooltip("The user's head (Camera.main when empty).")]
        public Transform eye;
        public float maxYaw = 65f, maxPitch = 35f;
        [Tooltip("Largest slide away from a pushing hand, as a share of the height.")]
        public float maxPush = 0.07f;
        public float maxLean = 12f;
        [Tooltip("A hand closer than this share of the height to the agent's head draws its gaze.")]
        public float handNotice = 0.55f;

        [Header("Clips (portal defaults; UsePerchClips() for the small desk visitor)")]
        public string Base = "Listening", Think = "Thinking", Converse = "Portal_Converse_Loop",
                      Emerge = "Portal_Emerge_Step", Leave = "Portal_Return_Step", Poke = "React_Poke_Surprise", Wave = "React_Wave_Greet",
                      Dangle = "Carry_Suspended_Idle";
        [Tooltip("True: this component may turn the root to face the user and slide it away from a push (the portal visitor). " +
                 "False: someone else places the root every frame (the carried / perched desk visitor); only the bones move.")]
        public bool moveRoot = true;

        /// <summary>The small Select Agents visitor on the desk perch: its own perch clips.</summary>
        public void UsePerchClips()
        {
            Base = "DeskVisit_Perch_Idle"; Think = "DeskVisit_Listening"; Converse = "DeskVisit_Explain";
            Emerge = "React_Wave_Greet"; Leave = "Waving";
            moveRoot = false;
        }

        bool _carried;
        /// <summary>Held in a hand (or flying home): it dangles, does not react, and looks at the user only lightly.</summary>
        public void SetCarried(bool carried)
        {
            if (_carried == carried) return;
            _carried = carried;
            _oneShotUntil = -1f;
            _playing = null;
            if (carried) { _leaving = false; Cross(Has(Dangle) ? Dangle : Base, 0.2f); }
            else RefreshLoop();
        }

        Animator _an;
        Transform _head, _neck, _chest, _spine;
        readonly Dictionary<string, float> _len = new Dictionary<string, float>();
        string _loop = "Listening", _playing;
        float _oneShotUntil = -1f, _speakUntil = -1f;
        bool _thinking, _leaving;

        Quaternion _look = Quaternion.identity;
        float _lookWeight;
        Vector3 _lookAt;
        float _attendTipUntil;
        Vector3 _attendTip;

        Vector3 _baseLocal, _push, _pushVel;
        float _lean, _leanVel;
        Vector3 _leanAxis = Vector3.right;
        bool _touching;
        float _pokeReady, _waveReady;
        float _breathPhase;

        // Wave sensing: per-hand lateral centroid motion.
        readonly float[] _lastX = new float[2], _lastDir = new float[2], _flipT0 = new float[2];
        readonly int[] _flips = new int[2];
        readonly Vector3[] _lastTip = new Vector3[10];
        bool _tipsInit;

        /// <summary>For drivers: the clip on show, what it is looking at, whether a finger is touching it.</summary>
        public string Describe()
        {
            return "clip=" + (_playing ?? "-") + " loop=" + _loop + " look=" + (_attendTipUntil > Time.unscaledTime ? "finger" : _lookTargetIsHand ? "hand" : "user")
                 + " touching=" + _touching + " push=" + _push.magnitude.ToString("F3") + " lean=" + _lean.ToString("F1");
        }
        bool _lookTargetIsHand;
        public int Pokes { get; private set; }
        public int Waves { get; private set; }

        void Awake()
        {
            _an = GetComponentInChildren<Animator>();
            if (_an != null)
            {
                _an.applyRootMotion = false;
                // rc14: the bones must be rewritten every frame (the additive look/lean below is applied on top of the
                // clip); an Animator culled on the headset would leave last frame's pose and the additions would pile up.
                _an.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                if (_an.runtimeAnimatorController != null)
                    foreach (var c in _an.runtimeAnimatorController.animationClips) if (c != null && !_len.ContainsKey(c.name)) _len[c.name] = c.length;
            }
            foreach (var t in GetComponentsInChildren<Transform>(true))
            {
                switch (t.name)
                {
                    case "Head": if (_head == null) _head = t; break;
                    case "Neck": if (_neck == null) _neck = t; break;
                    case "Chest": if (_chest == null) _chest = t; break;
                    case "Spine": if (_spine == null) _spine = t; break;
                }
            }
            // rc22 polish (owner: "arms clip through themselves when idle full size"): hanging arms are rolled clear of the body.
            if (GetComponent<AgentArmClearance>() == null) gameObject.AddComponent<AgentArmClearance>();
            _baseLocal = transform.localPosition;
            _breathPhase = Random.value * 10f;
            _pokeReady = _waveReady = Time.unscaledTime + 1.5f;
        }

        // ───────────── what the portal and the conversation tell it ─────────────

        public void Arrive() { _leaving = false; PlayOnce(Emerge, 1.29f); }
        public void Depart() { _leaving = true; _thinking = false; _speakUntil = -1f; PlayOnce(Leave, 1.13f); }
        /// <summary>The user asked something: it thinks until <see cref="Speak"/>.</summary>
        public void Asked() { if (_leaving) return; _thinking = true; RefreshLoop(); }
        /// <summary>An answer arrived: it gestures for a while, then listens again.</summary>
        public void Speak(float seconds)
        {
            if (_leaving) return;
            _thinking = false;
            _speakUntil = Time.unscaledTime + Mathf.Max(1.5f, seconds);
            RefreshLoop();
        }

        void PlayOnce(string state, float fallbackLen)
        {
            if (!Has(state)) return;
            float len;
            if (!_len.TryGetValue(state, out len)) len = fallbackLen;
            Cross(state, 0.15f);
            _oneShotUntil = Time.unscaledTime + Mathf.Max(0.3f, len - 0.15f);
        }

        bool Has(string state) { return _an != null && _an.HasState(0, Animator.StringToHash(state)); }

        void Cross(string state, float fade)
        {
            if (_an == null || !Has(state)) return;
            _an.CrossFadeInFixedTime(state, fade, 0);
            _playing = state;
        }

        void RefreshLoop()
        {
            if (_carried) return;
            string want = _speakUntil > Time.unscaledTime ? Converse : _thinking ? Think : Base;
            if (!Has(want)) want = Base;
            _loop = want;
            if (Time.unscaledTime >= _oneShotUntil && _playing != want) Cross(want, 0.35f);
        }

        // ───────────── per frame ─────────────

        void Update()
        {
            if (_oneShotUntil > 0f && Time.unscaledTime >= _oneShotUntil) { _oneShotUntil = -1f; if (!_leaving) { _playing = null; RefreshLoop(); } }
            else if (_oneShotUntil < 0f && !_leaving) RefreshLoop();
        }

        // ───────────── bone guard (owner rc13: "the head keeps spinning around in circles", "the body easily rolls") ─────────────
        // Every procedural rotation here is ADDED to the bone's current pose. If the Animator did not rewrite a bone this
        // frame (a clip without curves for it, a culled Animator, a stalled cross-fade), the current pose is still last
        // frame's result and the additions accumulate: the head turns a little more every frame and the lean rolls the
        // body over. So each bone remembers the pose it was given before the additions and the pose it was left in; if it
        // is still exactly as it was left, the Animator did not touch it and the remembered base is put back first.
        struct BoneGuard { public Transform t; public Quaternion baseRot, left; public bool has; }
        BoneGuard[] _guards;

        void GuardBones()
        {
            if (_guards == null)
            {
                var list = new List<Transform>();
                foreach (var b in new[] { _neck, _head, _chest, _spine }) if (b != null && !list.Contains(b)) list.Add(b);
                _guards = new BoneGuard[list.Count];
                for (int i = 0; i < list.Count; i++) _guards[i].t = list[i];
            }
            for (int i = 0; i < _guards.Length; i++)
            {
                Transform t = _guards[i].t;
                if (t == null) continue;
                Quaternion now = t.localRotation;
                if (_guards[i].has && Quaternion.Angle(now, _guards[i].left) < 0.01f) { t.localRotation = _guards[i].baseRot; now = _guards[i].baseRot; Restores++; }
                _guards[i].baseRot = now;
            }
        }

        void SealBones()
        {
            if (_guards == null) return;
            for (int i = 0; i < _guards.Length; i++)
            {
                if (_guards[i].t == null) continue;
                _guards[i].left = _guards[i].t.localRotation;
                _guards[i].has = true;
            }
        }

        /// <summary>Frames on which a bone was found un-animated and put back (driver evidence; 0 when the clip owns it).</summary>
        public int Restores { get; private set; }

        /// <summary>Head bone's angle from the body's forward, in degrees (driver evidence: never above maxYaw + a clip's own turn).</summary>
        public float HeadYawFromBody
        {
            get
            {
                if (_head == null) return 0f;
                Vector3 f = _head.forward; f.y = 0f; Vector3 b = transform.forward; b.y = 0f;
                return f.sqrMagnitude < 1e-6f || b.sqrMagnitude < 1e-6f ? 0f : Vector3.Angle(f, b);
            }
        }

        void LateUpdate()
        {
            GuardBones();
            try { Life(); }
            finally { SealBones(); }
        }

        void Life()
        {
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
            if (eye == null && Camera.main != null) eye = Camera.main.transform;
            float h = Mathf.Max(0.05f, height * Mathf.Clamp01(grow));
            Vector3 root = transform.position;
            Vector3 up = Vector3.up;
            Vector3 headPos = _head != null ? _head.position : root + up * h * 0.9f;

            IReadOnlyList<Transform> tips = HandFingertips.Get();
            SenseHands(tips, root, h, headPos, dt);

            // Look target: a finger that just touched it, else a hand near its head, else the user.
            Vector3 target = eye != null ? eye.position : headPos + transform.forward;
            _lookTargetIsHand = false;
            if (Time.unscaledTime < _attendTipUntil) target = _attendTip;
            else if (_nearHand) { target = _nearHandPos; _lookTargetIsHand = true; }
            _lookAt = Vector3.Lerp(_lookAt == Vector3.zero ? target : _lookAt, target, 1f - Mathf.Exp(-dt * 7f));

            // Body: keep facing the user (yaw only), gently.
            if (eye != null && !_leaving && moveRoot && !_carried)
            {
                Vector3 to = eye.position - root; to.y = 0f;
                Transform parent = transform.parent;
                if (to.sqrMagnitude > 1e-4f && parent != null)
                {
                    Quaternion want = Quaternion.Inverse(parent.rotation) * Quaternion.LookRotation(to.normalized, up);
                    transform.localRotation = Quaternion.Slerp(transform.localRotation, want, 1f - Mathf.Exp(-dt * 2.5f));
                }
            }

            // Head and neck (additive, after the clip).
            float wantWeight = _leaving ? 0f : _carried ? 0.5f : (_playing == Emerge ? 0.35f : 1f);
            _lookWeight = Mathf.MoveTowards(_lookWeight, wantWeight, dt * 2f);
            if (_head != null)
            {
                Vector3 fwd = transform.forward; fwd.y = 0f; if (fwd.sqrMagnitude < 1e-6f) fwd = Vector3.forward; fwd.Normalize();
                Vector3 d = _lookAt - headPos;
                Vector3 flat = new Vector3(d.x, 0f, d.z);
                // A target well behind the shoulder is let go (the head comes back to the front) instead of clamped: a
                // clamp flips from +maxYaw to -maxYaw as the target crosses behind, and the head whips round.
                float rawYaw = flat.sqrMagnitude > 1e-6f ? Vector3.SignedAngle(fwd, flat, up) : 0f;
                float yaw = Mathf.Abs(rawYaw) > maxYaw + 35f ? 0f : Mathf.Clamp(rawYaw, -maxYaw, maxYaw);
                float pitch = Mathf.Clamp(-Mathf.Atan2(d.y, Mathf.Max(0.05f, flat.magnitude)) * Mathf.Rad2Deg, -maxPitch, maxPitch);
                Vector3 right = Vector3.Cross(up, Quaternion.AngleAxis(yaw, up) * fwd);
                Quaternion want = Quaternion.AngleAxis(pitch, right) * Quaternion.AngleAxis(yaw, up);
                // Eased, and never faster than a calm head turn (240 deg/s).
                _look = Quaternion.RotateTowards(_look, Quaternion.Slerp(_look, want, 1f - Mathf.Exp(-dt * 6f)), 240f * dt);
                Quaternion w = Quaternion.Slerp(Quaternion.identity, _look, _lookWeight);
                if (_neck != null) _neck.rotation = Quaternion.Slerp(Quaternion.identity, w, 0.35f) * _neck.rotation;
                _head.rotation = Quaternion.Slerp(Quaternion.identity, w, _neck != null ? 0.65f : 1f) * _head.rotation;
            }

            // Breathing and the lean away from a push.
            _breathPhase += dt;
            Vector3 bodyRight = transform.right;
            Transform torso = _chest != null ? _chest : _spine;
            if (torso != null)
            {
                float breath = Mathf.Sin(_breathPhase * Mathf.PI * 2f / 3.6f) * 1.4f * (_leaving ? 0f : 1f);
                torso.rotation = Quaternion.AngleAxis(breath, bodyRight) * torso.rotation;
            }
            float leanTarget = _touching ? _leanGoal : 0f;
            _lean = Mathf.SmoothDamp(_lean, leanTarget, ref _leanVel, _touching ? 0.08f : 0.25f, Mathf.Infinity, dt);
            _lean = Mathf.Clamp(_lean, -maxLean, maxLean);
            if (_spine != null && Mathf.Abs(_lean) > 0.01f) _spine.rotation = Quaternion.AngleAxis(_lean, _leanAxis) * _spine.rotation;

            // The slide (kinematic rubber band), in the parent's space so the portal's own pose is untouched.
            Vector3 pushGoal = _touching ? _pushGoal : Vector3.zero;
            _push = Vector3.SmoothDamp(_push, pushGoal, ref _pushVel, _touching ? 0.06f : 0.3f, Mathf.Infinity, dt);
            if (moveRoot && transform.parent != null) transform.localPosition = _baseLocal + transform.parent.InverseTransformVector(_push + Vector3.up * liftWorld);
        }

        /// <summary>How far grown the avatar is (0..1; the portal sets it while the agent appears): radii follow it.</summary>
        [System.NonSerialized] public float grow = 1f;
        /// <summary>World metres the feet stand above the root's rest point (the portal's hover platform top).</summary>
        [System.NonSerialized] public float liftWorld;

        bool _nearHand;
        Vector3 _nearHandPos, _pushGoal;
        float _leanGoal;

        void SenseHands(IReadOnlyList<Transform> tips, Vector3 root, float h, Vector3 headPos, float dt)
        {
            _nearHand = false;
            bool touching = false;
            Vector3 push = Vector3.zero;
            float bestNear = handNotice * h;
            Vector3 a = root + Vector3.up * (h * 0.12f), b = root + Vector3.up * (h * 0.92f);
            float bodyR = h * 0.17f;
            int n = tips != null ? Mathf.Min(tips.Count, 10) : 0;
            float worstDepth = 0f;
            Vector3 contactTip = Vector3.zero;
            bool sim = Time.unscaledTime < _simUntil;
            for (int i = 0; i < n + (sim ? 1 : 0); i++)
            {
                Vector3 p;
                float speed;
                if (i < n)
                {
                    Transform t = tips[i];
                    if (t == null) continue;
                    p = t.position;
                    speed = _tipsInit ? (p - _lastTip[i]).magnitude / Mathf.Max(1e-4f, dt) : 0f;
                    _lastTip[i] = p;
                }
                else { p = _simTip; speed = _simFresh ? 1f : 0f; _simFresh = false; }
                float dHead = (p - headPos).magnitude;
                if (dHead < bestNear) { bestNear = dHead; _nearHand = true; _nearHandPos = p; }
                Vector3 c = ClosestOnSegment(a, b, p);
                Vector3 away = c - p; away.y = 0f;
                float dist = away.magnitude;
                float depth = bodyR - dist;
                if (depth > 0f && !_leaving && !_carried)
                {
                    touching = true;
                    Vector3 dir = dist > 1e-4f ? away / dist : -transform.forward;
                    push += dir * depth;
                    if (depth > worstDepth) { worstDepth = depth; contactTip = p; }
                    // A quick jab, once in a while: the startled hop.
                    if (!_touching && speed > 0.25f && Time.unscaledTime >= _pokeReady)
                    {
                        _pokeReady = Time.unscaledTime + 2.5f;
                        Pokes++;
                        WallEvents.RaiseVisitorGreeted();
                        PlayOnce(Poke, 1.0f);
                    }
                }
            }
            _tipsInit = n > 0;
            if (touching)
            {
                float cap = maxPush * h;
                _pushGoal = Vector3.ClampMagnitude(push * 1.6f, cap);
                Vector3 flat = new Vector3(_pushGoal.x, 0f, _pushGoal.z);
                if (flat.sqrMagnitude > 1e-8f) _leanAxis = Vector3.Cross(Vector3.up, flat.normalized);
                _leanGoal = Mathf.Clamp(worstDepth / Mathf.Max(0.01f, bodyR) * maxLean * 1.5f, 3f, maxLean);
                _attendTip = contactTip;
                _attendTipUntil = Time.unscaledTime + 1.5f;
            }
            _touching = touching;
            SenseWave(tips, n, dt);
        }

        void SenseWave(IReadOnlyList<Transform> tips, int n, float dt)
        {
            if (eye == null || n < 5 || _leaving || _carried) return;
            Vector3 right = eye.right;
            for (int hand = 0; hand < 2; hand++)
            {
                int start = hand * 5;
                if (start + 5 > n) break;
                Vector3 c = Vector3.zero;
                for (int i = start; i < start + 5; i++) if (tips[i] != null) c += tips[i].position;
                c /= 5f;
                bool raised = c.y > eye.position.y - 0.30f && (c - eye.position).magnitude < 0.9f;
                float x = Vector3.Dot(c - eye.position, right);
                float v = (x - _lastX[hand]) / Mathf.Max(1e-4f, dt);
                _lastX[hand] = x;
                if (!raised || Mathf.Abs(v) < 0.35f) { if (Time.unscaledTime - _flipT0[hand] > 1.4f) _flips[hand] = 0; continue; }
                float dir = Mathf.Sign(v);
                if (dir != _lastDir[hand])
                {
                    if (_flips[hand] == 0) _flipT0[hand] = Time.unscaledTime;
                    _flips[hand]++;
                    _lastDir[hand] = dir;
                }
                if (_flips[hand] >= 3 && Time.unscaledTime - _flipT0[hand] < 1.6f && Time.unscaledTime >= _waveReady)
                {
                    _flips[hand] = 0;
                    _waveReady = Time.unscaledTime + 6f;
                    Waves++;
                    WallEvents.RaiseVisitorGreeted();
                    PlayOnce(Has(Wave) ? Wave : "Waving", 1.6f);
                }
            }
        }

        static Vector3 ClosestOnSegment(Vector3 a, Vector3 b, Vector3 p)
        {
            Vector3 ab = b - a;
            float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
            return a + ab * t;
        }

        /// <summary>Play Mode driver: a fingertip pushing at a world point (stands in for a tracked hand).</summary>
        public void SimulateTouch(Vector3 tipWorld, float seconds)
        {
            _simTip = tipWorld; _simUntil = Time.unscaledTime + seconds; _simFresh = true;
        }
        Vector3 _simTip; float _simUntil = -1f; bool _simFresh;
        public bool SimulatingTouch => Time.unscaledTime < _simUntil;
        public Vector3 SimulatedTip => _simTip;
    }
}
