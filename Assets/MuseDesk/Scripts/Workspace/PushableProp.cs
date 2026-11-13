using UnityEngine;

namespace MuseDesk.Workspace
{
    /// <summary>
    /// A desk prop the user's hands can push, knock over and roll: a real dynamic Rigidbody that
    /// collides with the hand physics capsules and the desk surface, so a hand no longer passes
    /// straight through it. To keep the office tidy it puts itself back: once it has been left
    /// alone for a moment (or has fallen off the desk) it glides home to where it was authored.
    ///
    /// Cosmetic only. It never touches task, agent or decision state.
    ///
    /// Safety against "flings": depenetration and linear speed are capped, so a hand that ends up
    /// inside the collider pushes the prop out gently instead of launching it.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    [DisallowMultipleComponent]
    public class PushableProp : MonoBehaviour
    {
        [Tooltip("Seconds after start (and after the workspace is re-placed) before physics turns on.")]
        public float settleDelay = 3.5f;
        [Tooltip("Seconds the prop must lie still, out of place, before it tidies itself back.")]
        public float returnAfter = 4f;
        [Tooltip("Seconds the glide home takes.")]
        public float returnSeconds = 0.6f;
        [Tooltip("Further than this from home (m) and it goes back immediately (fell off the desk).")]
        public float maxStray = 0.45f;

        enum Mode { Settling, Dynamic, Returning }

        Rigidbody _rb;
        Transform _parent;
        Vector3 _homePos;
        Quaternion _homeRot;
        Vector3 _lastParentPos;
        Mode _mode = Mode.Settling;
        float _timer;
        float _stillFor;
        Vector3 _fromPos;
        Quaternion _fromRot;
        System.Func<int> _sel;
        bool _selResolved;

        bool IsHeld()
        {
            if (!_selResolved)
            {
                _selResolved = true;
                Component g = null;
                foreach (var mb in GetComponents<MonoBehaviour>())
                    if (mb != null && mb.GetType().Name == "Grabbable") { g = mb; break; }
                if (g != null) _sel = GrabState.BindSelectingPoints(g);
            }
            return _sel != null && _sel() > 0;
        }

        void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            _parent = transform.parent;
            _homePos = transform.localPosition;
            _homeRot = transform.localRotation;
            _rb.isKinematic = true;
            _rb.interpolation = RigidbodyInterpolation.Interpolate;
            _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            _rb.maxDepenetrationVelocity = 0.6f;
            _rb.maxLinearVelocity = 1.6f;
            _rb.maxAngularVelocity = 14f;
        }

        void OnEnable()
        {
            EnterSettling();
            if (_parent != null) _lastParentPos = _parent.position;
        }

        void EnterSettling()
        {
            _mode = Mode.Settling;
            _timer = 0f;
            _rb.isKinematic = true;
            transform.localPosition = _homePos;
            transform.localRotation = _homeRot;
        }

        void Update()
        {
            // The whole workspace is moved into place in front of the user (and again on recenter).
            // A dynamic body must not be simulated through that jump: go home and settle again.
            if (_parent != null)
            {
                float moved = (_parent.position - _lastParentPos).sqrMagnitude;
                _lastParentPos = _parent.position;
                if (moved > 0.02f * 0.02f) { EnterSettling(); return; }
            }

            if (IsHeld())
            {
                if (_mode == Mode.Returning) _mode = Mode.Dynamic;
                _stillFor = 0f;
                return;
            }

            switch (_mode)
            {
                case Mode.Settling:
                    _timer += Time.deltaTime;
                    if (_timer >= settleDelay)
                    {
                        _rb.isKinematic = false;
                        _rb.WakeUp();
                        _stillFor = 0f;
                        _mode = Mode.Dynamic;
                    }
                    break;

                case Mode.Dynamic:
                    float stray = (transform.localPosition - _homePos).magnitude;
                    bool outOfPlace = stray > 0.02f ||
                                      Quaternion.Angle(transform.localRotation, _homeRot) > 12f;
                    if (stray > maxStray) { BeginReturn(); break; }
                    if (!outOfPlace) { _stillFor = 0f; break; }
                    bool still = _rb.IsSleeping() || _rb.linearVelocity.sqrMagnitude < 0.03f * 0.03f;
                    _stillFor = still ? _stillFor + Time.deltaTime : 0f;
                    if (_stillFor >= returnAfter) BeginReturn();
                    break;

                case Mode.Returning:
                    _timer += Time.deltaTime;
                    float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(_timer / Mathf.Max(0.05f, returnSeconds)));
                    // A small hop so it reads as "putting itself back", not sliding through the desk.
                    Vector3 p = Vector3.Lerp(_fromPos, _homePos, k);
                    p.y += Mathf.Sin(k * Mathf.PI) * 0.03f;
                    transform.localPosition = p;
                    transform.localRotation = Quaternion.Slerp(_fromRot, _homeRot, k);
                    if (k >= 1f)
                    {
                        transform.localPosition = _homePos;
                        transform.localRotation = _homeRot;
                        _rb.isKinematic = false;
                        _rb.linearVelocity = Vector3.zero;
                        _rb.angularVelocity = Vector3.zero;
                        _stillFor = 0f;
                        _mode = Mode.Dynamic;
                    }
                    break;
            }
        }

        void BeginReturn()
        {
            _rb.isKinematic = true;
            _fromPos = transform.localPosition;
            _fromRot = transform.localRotation;
            _timer = 0f;
            _mode = Mode.Returning;
        }
    }
}
