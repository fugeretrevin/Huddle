using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using MuseDesk.Workspace;

namespace MuseDesk.Spatial
{
    /// <summary>
    /// Carries the office's loose physics props rigidly while the office travels.
    ///
    /// WorkspaceRoot is moved by transform, every frame, for one to four seconds (to the floor, to
    /// the ceiling dock, down beside the user, back up, back to the desk, and back along the way
    /// when Stop is pressed). A Rigidbody child does not simply follow that:
    ///
    ///  - a dynamic body is simulated in world space, so gravity and the collider it rests on
    ///    (which jumps with the transform) leave it behind, tip it or drop it;
    ///  - a body with interpolation switched on (PushableProp sets Interpolate) is drawn from its
    ///    last two PHYSICS poses, so even a kinematic one is drawn a physics step behind a parent
    ///    that moved this frame: the props visibly trail the platform;
    ///  - PushableProp only goes home and freezes when its parent jumps more than 2 cm in a frame,
    ///    and it wakes its body again 3.5 s later, which is in the middle of a ceiling descent.
    ///
    /// So for the whole duration of a move this component, for every loose body under the office:
    /// remembers its state, switches its drivers (PushableProp, DelayedPhysics) off, zeroes its
    /// velocities, makes it kinematic, switches interpolation off, and writes its captured local
    /// pose again in LateUpdate after everything else has run. A few frames after the office has
    /// come to rest it puts everything back: PushableProp re-enabled (it starts its own settle
    /// window at its home pose and wakes the body itself), any other body back to its previous
    /// kinematic state, velocities zero, asleep.
    ///
    /// Left alone: anything a hand is holding, anything that is no longer under the office (the
    /// stamps, dock and board that NearUserKit parks at the desk), inactive objects. Bodies that
    /// are already kinematic and have no driver (agents, stamps, cards) only have interpolation
    /// switched off for the move; their own scripts animate them in local space.
    ///
    /// A move is detected three ways, so no path is missed: WorkspaceTransitionController calls
    /// <see cref="NotifyRootWrite"/> just before it writes the root (every spatial move, including
    /// the ceiling pod's, goes through it); SpatialWorkspaceState.IsTransitioning (covers the wait
    /// before the first write and a halted Stop); and the root's own pose changing between frames
    /// (covers the desk placement and anything else that moves the root).
    ///
    /// Presentation only: it never touches session, task or decision state.
    /// </summary>
    [DefaultExecutionOrder(9000)]
    public class WorkspacePropCarrier : MonoBehaviour
    {
        [Tooltip("The office root. Empty: SpatialWorkspaceState.WorkspaceRoot.")]
        public Transform workspaceRoot;
        [Tooltip("Optional. Its IsRunning also counts as moving.")]
        public WorkspaceTransitionController transition;

        [Header("Settle")]
        [Tooltip("Frames the office must be at rest before physics is given back.")]
        public int settleFrames = 6;
        [Tooltip("Seconds the office must be at rest before physics is given back.")]
        public float settleSeconds = 0.2f;

        [Header("Behaviour")]
        [Tooltip("Put a PushableProp back on its home pose when the move starts (it would go there anyway when it is re-enabled at the end; doing it first means nothing pops after the office has arrived).")]
        public bool tidyBeforeMove = true;
        [Tooltip("Also carry when the root moves in Desk mode (desk placement, recenter).")]
        public bool carryInDeskMode = true;
        [Tooltip("Root movement per frame that counts as moving (metres / degrees).")]
        public float moveEpsilon = 0.0002f;
        public float turnEpsilon = 0.02f;

        /// <summary>The scene's carrier, or null.</summary>
        public static WorkspacePropCarrier Instance { get; private set; }

        /// <summary>Raised when a carry starts / after physics was given back. For other smoothed
        /// followers that want to snap during an office move.</summary>
        public static event Action MoveBegan;
        public static event Action MoveEnded;

        /// <summary>True from the start of an office move until physics was given back.</summary>
        public bool Carrying => _carrying;
        /// <summary>Bodies pinned to their local pose right now.</summary>
        public int PinnedCount { get; private set; }
        /// <summary>Bodies whose interpolation is switched off for the move (pinned ones included).</summary>
        public int TouchedCount => _entries.Count;
        /// <summary>Moves carried since start-up.</summary>
        public int MovesCarried { get; private set; }
        /// <summary>Largest distance (m) a pinned body was found away from its pinned pose at
        /// LateUpdate during the current or last move, before it was put back: how hard something
        /// else pulled on it. Zero is the expected value.</summary>
        public float MaxPullDuringMove { get; private set; }

        sealed class Entry
        {
            public Rigidbody body;
            public Transform t;
            public Transform parent;
            public Vector3 localPos;
            public Quaternion localRot;
            public bool pinned;                  // false: interpolation only
            public bool free;                    // held, or no longer under the office: hands off
            public bool wasKinematic;
            public RigidbodyInterpolation interpolation;
            public PushableProp pushable;
            public bool pushableWasEnabled;
            public DelayedPhysics delayed;
            public bool delayedWasEnabled;
            public Func<int> held;
        }

        readonly List<Entry> _entries = new List<Entry>(32);
        readonly List<Rigidbody> _scratch = new List<Rigidbody>(32);
        bool _carrying;
        int _stillFrames;
        float _stillSeconds;
        Vector3 _lastRootPos;
        Quaternion _lastRootRot = Quaternion.identity;
        bool _hasLast;
        bool _notified;

        Transform Root => workspaceRoot != null ? workspaceRoot : SpatialWorkspaceState.WorkspaceRoot;

        void Awake() { Instance = this; }

        void OnDestroy() { if (Instance == this) Instance = null; }

        void OnDisable()
        {
            // Never leave a prop frozen because the carrier went away mid-move.
            if (_carrying) End();
        }

        /// <summary>Called by WorkspaceTransitionController just before it writes the root.</summary>
        public static void NotifyRootWrite()
        {
            WorkspacePropCarrier c = Instance;
            if (c == null || !c.isActiveAndEnabled) return;
            c._notified = true;
            if (!c._carrying) c.Begin();
        }

        void LateUpdate()
        {
            Transform root = Root;
            if (root == null) return;

            bool moved = false;
            if (_hasLast)
            {
                moved = (root.position - _lastRootPos).sqrMagnitude > moveEpsilon * moveEpsilon
                        || Quaternion.Angle(root.rotation, _lastRootRot) > turnEpsilon;
            }
            _lastRootPos = root.position; _lastRootRot = root.rotation; _hasLast = true;

            bool spatial = SpatialWorkspaceState.Mode != WorkspaceMode.Desk || SpatialWorkspaceState.IsTransitioning
                           || (transition != null && transition.IsRunning);
            bool moving = _notified
                          || SpatialWorkspaceState.IsTransitioning
                          || (transition != null && transition.IsRunning)
                          || (moved && (spatial || carryInDeskMode));
            _notified = false;

            if (moving)
            {
                _stillFrames = 0; _stillSeconds = 0f;
                if (!_carrying) Begin();
            }
            if (!_carrying) return;

            Pin(root);

            if (!moving)
            {
                _stillFrames++;
                _stillSeconds += Time.unscaledDeltaTime;
                if (_stillFrames >= settleFrames && _stillSeconds >= settleSeconds) End();
            }
        }

        void Begin()
        {
            Transform root = Root;
            if (root == null) return;
            _entries.Clear();
            _scratch.Clear();
            root.GetComponentsInChildren(true, _scratch);
            for (int i = 0; i < _scratch.Count; i++)
            {
                Rigidbody rb = _scratch[i];
                if (rb == null || !rb.gameObject.activeInHierarchy) continue;
                var e = new Entry { body = rb, t = rb.transform };
                e.parent = e.t.parent;
                e.wasKinematic = rb.isKinematic;
                e.interpolation = rb.interpolation;
                e.pushable = rb.GetComponent<PushableProp>();
                e.delayed = rb.GetComponent<DelayedPhysics>();
                e.held = BindHeld(rb);
                bool heldNow = e.held != null && e.held() > 0;
                // Loose = simulated now, or about to be woken by its own driver.
                e.pinned = !rb.isKinematic || e.pushable != null || e.delayed != null;
                if (e.pinned && heldNow) { e.free = true; }

                if (e.pinned)
                {
                    if (e.pushable != null)
                    {
                        e.pushableWasEnabled = e.pushable.enabled;
                        if (e.pushableWasEnabled && !heldNow)
                        {
                            if (tidyBeforeMove)
                            {
                                // Off and on again: its OnEnable puts the prop on its home pose,
                                // kinematic. Then off for the move.
                                e.pushable.enabled = false;
                                e.pushable.enabled = true;
                            }
                            e.pushable.enabled = false;
                        }
                    }
                    if (e.delayed != null)
                    {
                        e.delayedWasEnabled = e.delayed.enabled;
                        if (!heldNow) e.delayed.enabled = false;
                    }
                    if (!e.free) Freeze(e);
                    e.localPos = e.t.localPosition;
                    e.localRot = e.t.localRotation;
                }
                else if (rb.interpolation != RigidbodyInterpolation.None)
                {
                    rb.interpolation = RigidbodyInterpolation.None;
                }
                else continue;          // kinematic, no driver, no interpolation: nothing to do or undo
                _entries.Add(e);
            }
            _scratch.Clear();
            _carrying = true;
            _stillFrames = 0; _stillSeconds = 0f;
            MaxPullDuringMove = 0f;
            MovesCarried++;
            Raise(MoveBegan);
        }

        static void Freeze(Entry e)
        {
            Rigidbody rb = e.body;
            if (!rb.isKinematic)
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
                rb.isKinematic = true;
            }
            if (rb.interpolation != RigidbodyInterpolation.None) rb.interpolation = RigidbodyInterpolation.None;
        }

        void Pin(Transform root)
        {
            int pinned = 0;
            for (int i = 0; i < _entries.Count; i++)
            {
                Entry e = _entries[i];
                if (!e.pinned || e.body == null) continue;
                bool held = e.held != null && e.held() > 0;
                bool inOffice = e.t.IsChildOf(root);
                if (held || !inOffice)
                {
                    e.free = true;       // the hand, or the near-user rig, owns it now
                    continue;
                }
                if (e.free)
                {
                    // Let go (or brought back) during the move: carry it from where it is now.
                    e.free = false;
                    e.parent = e.t.parent;
                    e.localPos = e.t.localPosition;
                    e.localRot = e.t.localRotation;
                    if (e.pushable != null && e.pushable.enabled) e.pushable.enabled = false;
                    if (e.delayed != null && e.delayed.enabled) e.delayed.enabled = false;
                }
                Freeze(e);
                if (e.t.parent != e.parent)
                {
                    // Re-parented inside the office (same local values by convention): follow it.
                    e.parent = e.t.parent;
                    e.localPos = e.t.localPosition;
                    e.localRot = e.t.localRotation;
                }
                else
                {
                    float pull = (e.t.localPosition - e.localPos).magnitude;
                    if (pull > MaxPullDuringMove) MaxPullDuringMove = pull;
                    e.t.localPosition = e.localPos;
                    e.t.localRotation = e.localRot;
                }
                pinned++;
            }
            PinnedCount = pinned;
        }

        void End()
        {
            Transform root = Root;
            for (int i = 0; i < _entries.Count; i++)
            {
                Entry e = _entries[i];
                if (e.body == null) continue;
                Rigidbody rb = e.body;
                if (!e.pinned)
                {
                    rb.interpolation = e.interpolation;
                    continue;
                }
                bool mine = !e.free && root != null && e.t.IsChildOf(root);
                if (mine && e.t.parent == e.parent)
                {
                    e.t.localPosition = e.localPos;
                    e.t.localRotation = e.localRot;
                }
                rb.interpolation = e.interpolation;
                if (e.pushable != null)
                {
                    // Its OnEnable starts a settle window at its home pose and wakes the body itself.
                    if (e.pushableWasEnabled && !e.pushable.enabled) e.pushable.enabled = true;
                }
                else if (mine)
                {
                    rb.isKinematic = e.wasKinematic;
                    if (!rb.isKinematic)
                    {
                        rb.linearVelocity = Vector3.zero;
                        rb.angularVelocity = Vector3.zero;
                        rb.Sleep();
                    }
                }
                if (e.delayed != null && e.delayedWasEnabled && !e.delayed.enabled) e.delayed.enabled = true;
            }
            _entries.Clear();
            PinnedCount = 0;
            _carrying = false;
            Raise(MoveEnded);
        }

        /// <summary>"Is a hand holding this?" for a body with an Interaction SDK Grabbable on it
        /// (read through GrabState, like the props themselves do). Null when it has none.</summary>
        static Func<int> BindHeld(Rigidbody rb)
        {
            var behaviours = rb.GetComponents<MonoBehaviour>();
            for (int i = 0; i < behaviours.Length; i++)
            {
                MonoBehaviour mb = behaviours[i];
                if (mb != null && mb.GetType().Name == "Grabbable") return GrabState.BindSelectingPoints(mb);
            }
            return null;
        }

        static void Raise(Action a)
        {
            if (a == null) return;
            try { a(); } catch (Exception ex) { Debug.LogException(ex); }
        }

        /// <summary>One line per body the carrier is handling right now (Play Mode drivers).</summary>
        public string Describe()
        {
            var sb = new StringBuilder();
            sb.Append("carrying=").Append(_carrying).Append(" pinned=").Append(PinnedCount)
              .Append(" touched=").Append(_entries.Count).Append(" moves=").Append(MovesCarried)
              .Append(" maxPull=").Append((MaxPullDuringMove * 1000f).ToString("F2")).Append("mm");
            for (int i = 0; i < _entries.Count; i++)
            {
                Entry e = _entries[i];
                if (e.body == null) continue;
                sb.Append("\n  ").Append(e.t.name).Append(e.pinned ? (e.free ? " FREE(held/not in office)" : " pinned") : " interpolation-only")
                  .Append(" wasKinematic=").Append(e.wasKinematic).Append(" interp=").Append(e.interpolation)
                  .Append(e.pushable != null ? " PushableProp" : "").Append(e.delayed != null ? " DelayedPhysics" : "");
            }
            return sb.ToString();
        }
    }
}
