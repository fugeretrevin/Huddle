using UnityEngine;
using MuseDesk.Models;
using MuseDesk.Networking;

namespace MuseDesk.Documents
{
    /// <summary>
    /// The stamp leaves once it has done its job and comes back when it is needed.
    ///
    /// About two seconds after the backend (or the labelled demo) confirms a decision, by stamp or
    /// by key, the stamp squashes, pops and is gone in a small puff. If a hand is holding it at
    /// that moment it waits for the release and goes from where it was let go. It pops back in on
    /// its rest pose when the next decision is requested (or one is found waiting after a resume).
    ///
    /// Presentation only: it listens to the reducer's decision.resolved and decision.requested and
    /// never sends or implies a decision.
    ///
    /// How it composes with <c>NearUserKit</c>, which hides the stamps by writing the ROOT's
    /// localScale while the office is away: this component never writes the root's scale. It
    /// scales the root's CHILDREN (the model, the hover ring), shrinks the root's own grab collider
    /// so an invisible stamp cannot be taken, and keeps the body kinematic while gone. The two
    /// multiply through the hierarchy, in either order, with no shared state.
    /// </summary>
    [DefaultExecutionOrder(310)]   // before StampTool (320), so a release is seen here first
    public class StampPoof : MonoBehaviour
    {
        [Header("Wiring (set by BuildStampPoofV1)")]
        public SessionController controller;
        public StampTool tool;
        [Tooltip("The stamp's rigidbody. Kinematic while the stamp is gone; its setting is restored after.")]
        public Rigidbody body;
        [Tooltip("Children of this stamp that are scaled away (the model, the hover ring). Empty: every direct child except InkHead and the effect.")]
        public Transform[] visuals;
        [Tooltip("Point the stamp squashes toward, in this stamp's local space (its base).")]
        public Vector3 pivotLocal = Vector3.zero;

        [Header("Effect (built by BuildStampPoofV1; optional)")]
        [Tooltip("A few soft unlit puffs, played once at the pop.")]
        public ParticleSystem puff;
        [Tooltip("One flat expanding ring, played at the pop and, alone, when the stamp comes back.")]
        public ParticleSystem ring;

        [Header("Timing")]
        [Tooltip("Seconds the stamp stays after the decision is confirmed.")]
        public float lingerSeconds = 2f;
        [Tooltip("Seconds of the squash-and-pop out.")]
        public float leaveSeconds = 0.25f;
        [Tooltip("Seconds of the pop back in.")]
        public float arriveSeconds = 0.3f;
        [Tooltip("A stamp held when its time is up leaves this long after it is let go.")]
        public float releaseGraceSeconds = 0.15f;
        [Tooltip("Seconds the gone stamp stays where it vanished (so the puff is not dragged) before it is put back on its rest pose.")]
        public float settleSeconds = 0.5f;
        [Tooltip("Start with the stamp gone until the first decision is requested. Off: the stamp is on the pad at launch, as before.")]
        public bool startHidden = false;

        StampPoofMachine _m;
        bool _bound;
        bool _driverHeld;
        bool _snapPending;
        float _snapTimer;
        bool _frozeBody;
        bool _bodyWasKinematic;
        float _rootScaleY0 = 1f;

        Vector3[] _basePos, _baseScale, _wx, _wy, _wz;
        Collider[] _colliders;
        Vector3[] _boxSize;
        bool[] _colliderWasEnabled;
        bool _grabbable = true;

        /// <summary>True while the stamp is gone or on its way out or in (not grabbable).</summary>
        public bool Away => _m != null && !_m.Grabbable;

        /// <summary>Shown, Waiting, Leaving, Hidden or Arriving.</summary>
        public string PhaseName
        {
            get
            {
                if (_m == null) return "Shown";
                switch (_m.Phase)
                {
                    case StampPoofMachine.Waiting: return "Waiting";
                    case StampPoofMachine.Leaving: return "Leaving";
                    case StampPoofMachine.Hidden: return "Hidden";
                    case StampPoofMachine.Arriving: return "Arriving";
                    default: return "Shown";
                }
            }
        }

        void Awake()
        {
            _m = new StampPoofMachine(lingerSeconds, leaveSeconds, arriveSeconds, releaseGraceSeconds);
            if (tool == null) tool = GetComponent<StampTool>();
            if (body == null) body = GetComponent<Rigidbody>();
            _rootScaleY0 = Mathf.Max(1e-5f, Mathf.Abs(transform.localScale.y));

            if (visuals == null || visuals.Length == 0) visuals = DefaultVisuals();
            int n = visuals.Length;
            _basePos = new Vector3[n]; _baseScale = new Vector3[n];
            _wx = new Vector3[n]; _wy = new Vector3[n]; _wz = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                Transform v = visuals[i];
                if (v == null) continue;
                _basePos[i] = v.localPosition;
                _baseScale[i] = v.localScale;
                // How much each of the child's own axes lies along the stamp's X, Y and Z, so a
                // squash in the stamp's space lands on the right axes of a rotated model.
                Quaternion r = v.localRotation;
                _wx[i] = Sq(r * Vector3.right);
                _wy[i] = Sq(r * Vector3.up);
                _wz[i] = Sq(r * Vector3.forward);
            }

            // Only the root's own colliders: they are the grab volume. Children shrink with the model.
            _colliders = GetComponents<Collider>();
            _boxSize = new Vector3[_colliders.Length];
            _colliderWasEnabled = new bool[_colliders.Length];
            for (int i = 0; i < _colliders.Length; i++)
            {
                var box = _colliders[i] as BoxCollider;
                if (box != null) _boxSize[i] = box.size;
                _colliderWasEnabled[i] = _colliders[i].enabled;
            }
        }

        static Vector3 Sq(Vector3 v) { return new Vector3(v.x * v.x, v.y * v.y, v.z * v.z); }

        Transform[] DefaultVisuals()
        {
            int count = 0;
            for (int i = 0; i < transform.childCount; i++) if (IsVisualChild(transform.GetChild(i))) count++;
            var list = new Transform[count];
            int k = 0;
            for (int i = 0; i < transform.childCount; i++)
            {
                Transform c = transform.GetChild(i);
                if (IsVisualChild(c)) list[k++] = c;
            }
            return list;
        }

        bool IsVisualChild(Transform c)
        {
            if (c.name == "InkHead") return false;
            if (puff != null && (c == puff.transform || puff.transform.IsChildOf(c))) return false;
            if (ring != null && (c == ring.transform || ring.transform.IsChildOf(c))) return false;
            return true;
        }

        void Start()
        {
            if (startHidden && (controller == null || string.IsNullOrEmpty(controller.PendingDecisionId)))
            {
                _m.ForceHidden();
                EnterAway();
                ApplyScale(StampPoofMachine.HiddenScale, StampPoofMachine.HiddenScale);
            }
        }

        void OnDisable()
        {
            if (_bound && controller != null)
            {
                controller.Reducer.DecisionResolved -= OnResolved;
                controller.Reducer.DecisionRequested -= OnRequested;
            }
            _bound = false;
            // Never leave an invisible, ungrabbable stamp behind a disabled component.
            if (_m != null && _m.Phase != StampPoofMachine.Shown)
            {
                _m.ForceShown();
                SnapNow();
                ApplyScale(1f, 1f);
                LeaveAway();
            }
        }

        void Bind()
        {
            controller.Reducer.DecisionResolved += OnResolved;
            controller.Reducer.DecisionRequested += OnRequested;
            _bound = true;
        }

        // Confirmed by the backend (or the labelled demo), whichever way the user answered.
        void OnResolved(string decisionId, string state) { _m.Dismiss(); }

        void OnRequested(string decisionId, EventEnvelope e) { Summon(); }

        /// <summary>Bring the stamp back to its rest pose (no effect if it is there).</summary>
        public void Summon() { SummonInternal(true); }

        void SummonInternal(bool toRest)
        {
            if (_m == null) return;
            bool wasHidden = _m.Phase == StampPoofMachine.Hidden;
            if (!_m.Summon()) return;
            // From gone: appear on the pad. Turned round mid-leave (or in a hand): no jump, the
            // stamp tool's normal glide takes it home once it is free.
            if (wasHidden && toRest) SnapNow();
            else { _snapPending = false; if (tool != null) tool.HoldInPlace = false; }
            ApplyScale(_m.ScaleXZ, _m.ScaleY);
            if (VisibleNow())
            {
                if (ring != null) ring.Play(false);
                if (tool != null) tool.PlayPoof(true);
            }
        }

        /// <summary>Send the stamp away after the usual wait, as a confirmed decision does.</summary>
        public void Dismiss() { if (_m != null) _m.Dismiss(); }

        void Update()
        {
            if (!_bound && controller != null) Bind();

            _m.LingerSeconds = lingerSeconds;
            _m.LeaveSeconds = leaveSeconds;
            _m.ArriveSeconds = arriveSeconds;
            _m.ReleaseGraceSeconds = releaseGraceSeconds;

            bool held = _driverHeld || (tool != null && tool.Held);
            int phase = _m.Phase;

            // A decision that is waiting always has its stamps (also covers a resume, where no
            // decision.requested event comes).
            if (phase != StampPoofMachine.Shown && phase != StampPoofMachine.Arriving
                && controller != null && !string.IsNullOrEmpty(controller.PendingDecisionId))
                Summon();

            // Fail-safe: a stamp that is somehow in a hand while gone shows itself, then leaves
            // again once it is let go.
            if (_m.Phase == StampPoofMachine.Hidden && held)
            {
                SummonInternal(false);
                _m.DismissAfter(0f);
            }

            int ev = _m.Tick(Time.deltaTime, held);
            switch (ev)
            {
                case StampPoofMachine.EventLeaveStarted:
                    if (tool != null) tool.HoldInPlace = true;      // goes from where it is
                    EnterAway();
                    break;
                case StampPoofMachine.EventPop:
                    if (VisibleNow())
                    {
                        if (puff != null) puff.Play(false);
                        if (ring != null) ring.Play(false);
                        if (tool != null) tool.PlayPoof(false);
                    }
                    break;
                case StampPoofMachine.EventHidden:
                    ApplyScale(StampPoofMachine.HiddenScale, StampPoofMachine.HiddenScale);
                    _snapPending = true;
                    _snapTimer = settleSeconds;
                    break;
                case StampPoofMachine.EventShown:
                    ApplyScale(1f, 1f);
                    LeaveAway();
                    break;
            }

            phase = _m.Phase;
            if (phase == StampPoofMachine.Leaving || phase == StampPoofMachine.Arriving)
                ApplyScale(_m.ScaleXZ, _m.ScaleY);

            if (_snapPending)
            {
                _snapTimer -= Time.deltaTime;
                if (_snapTimer <= 0f) SnapNow();
            }
        }

        // The puff and the sound are for a stamp the user can see: not while NearUserKit has the
        // stamps scaled away (office on the floor or ceiling, nothing to decide).
        bool VisibleNow() { return Mathf.Abs(transform.localScale.y) > 0.5f * _rootScaleY0; }

        void SnapNow()
        {
            _snapPending = false;
            if (tool != null) tool.SnapHome();
        }

        // Not grabbable and not subject to physics while leaving, gone or arriving.
        void EnterAway()
        {
            if (!_grabbable) return;
            _grabbable = false;
            MuteInteractables();
            for (int i = 0; i < _colliders.Length; i++)
            {
                Collider c = _colliders[i];
                if (c == null) continue;
                var box = c as BoxCollider;
                // Shrunk, not disabled: the same thing a hidden-by-scale object presents to the
                // Interaction SDK, which keeps its registration untouched.
                if (box != null) box.size = _boxSize[i] * StampPoofMachine.HiddenScale;
                else { _colliderWasEnabled[i] = c.enabled; c.enabled = false; }
            }
            if (body != null)
            {
                _bodyWasKinematic = body.isKinematic;
                if (!_bodyWasKinematic)
                {
                    body.linearVelocity = Vector3.zero;
                    body.angularVelocity = Vector3.zero;
                    body.isKinematic = true;
                }
                _frozeBody = true;
            }
        }

        void LeaveAway()
        {
            if (_grabbable) return;
            _grabbable = true;
            UnmuteInteractables();
            for (int i = 0; i < _colliders.Length; i++)
            {
                Collider c = _colliders[i];
                if (c == null) continue;
                var box = c as BoxCollider;
                if (box != null) box.size = _boxSize[i];
                else c.enabled = _colliderWasEnabled[i];
            }
            if (body != null && _frozeBody) body.isKinematic = _bodyWasKinematic;
            _frozeBody = false;
        }

        // A shrunk collider is not enough: a hand grab scores by pose distance, so a pinch at the spot
        // still took the invisible stamp. While gone, every Interaction SDK interactable on the stamp
        // (grab, hand grab, distance grab, ray, poke) is switched off through its component's enabled
        // flag, the SDK's own unregister path. That also cancels a selection still in progress, so a
        // stamp in a hand at that moment is released cleanly. Only what this switched off is switched
        // back on, so anything another system muted stays muted.
        readonly System.Collections.Generic.List<Behaviour> _muted = new System.Collections.Generic.List<Behaviour>(4);
        readonly System.Collections.Generic.List<MonoBehaviour> _scratch = new System.Collections.Generic.List<MonoBehaviour>(8);

        /// <summary>Number of interactables this stamp currently has switched off (for the probe).</summary>
        public int MutedInteractableCount => _muted.Count;

        void MuteInteractables()
        {
            GetComponentsInChildren(true, _scratch);
            for (int i = 0; i < _scratch.Count; i++)
            {
                MonoBehaviour mb = _scratch[i];
                if (mb == null || !mb.enabled || !(mb is Oculus.Interaction.IInteractable)) continue;
                mb.enabled = false;
                _muted.Add(mb);
            }
            _scratch.Clear();
        }

        void UnmuteInteractables()
        {
            for (int i = 0; i < _muted.Count; i++)
                if (_muted[i] != null) _muted[i].enabled = true;
            _muted.Clear();
        }

        void ApplyScale(float xz, float y)
        {
            if (visuals == null || _basePos == null) return;
            var f = new Vector3(xz, y, xz);
            bool full = xz == 1f && y == 1f;
            for (int i = 0; i < visuals.Length; i++)
            {
                Transform v = visuals[i];
                if (v == null) continue;
                if (full)
                {
                    v.localPosition = _basePos[i];
                    v.localScale = _baseScale[i];
                    continue;
                }
                v.localPosition = pivotLocal + Vector3.Scale(_basePos[i] - pivotLocal, f);
                Vector3 b = _baseScale[i];
                v.localScale = new Vector3(b.x * Vector3.Dot(_wx[i], f), b.y * Vector3.Dot(_wy[i], f), b.z * Vector3.Dot(_wz[i], f));
            }
        }

        // ── Play Mode driver (AgentScripts/StampPoofDrive.cs). Test aids; they send nothing. ──

        /// <summary>Pretend a hand is holding the stamp (for the poof only; the stamp tool is not told).</summary>
        public void DriverHold(bool held) { _driverHeld = held; }

        public string DriverReport()
        {
            float vis = visuals != null && visuals.Length > 0 && visuals[0] != null && _baseScale != null && Mathf.Abs(_baseScale[0].y) > 1e-6f
                ? visuals[0].localScale.y / _baseScale[0].y : 1f;
            var box = _colliders != null && _colliders.Length > 0 ? _colliders[0] as BoxCollider : null;
            return $"{name}: phase={PhaseName} progress={(_m != null ? _m.Progress : 0f):F2} visualScale={vis:F3} rootScale={transform.localScale.y:F3} "
                 + $"grabbable={_grabbable} collider={(box != null ? box.size.y.ToString("F4") : "n/a")} kinematic={(body != null ? body.isKinematic.ToString() : "n/a")} "
                 + $"held={(_driverHeld || (tool != null && tool.Held))} localPos={transform.localPosition.ToString("F3")} holdInPlace={(tool != null && tool.HoldInPlace)} "
                 + $"puff={(puff != null ? puff.particleCount.ToString() : "none")} ring={(ring != null ? ring.particleCount.ToString() : "none")} bound={_bound}";
        }
    }
}
