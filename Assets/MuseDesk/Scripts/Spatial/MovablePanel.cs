using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using Oculus.Interaction;
using Oculus.Interaction.HandGrab;
using MuseDesk.Workspace;

namespace MuseDesk.Spatial
{
    /// <summary>
    /// A group of the user's controls (the task dock with its board, the mode plaque, the mode
    /// console, the completion receipt) that can be picked up and put somewhere else, the way a
    /// Horizon OS window is moved: by pinching the small bar under its near edge.
    ///
    /// WHERE PANELS LIVE. Every panel is a child of the near-user rig (Spatial/NearUserRig), which
    /// <see cref="NearUserKit"/> keeps glued to the office's desk pose while the office is at the
    /// desk and leaves where it is when the office goes to the floor or the ceiling. A panel
    /// therefore keeps the pose the user chose across mode changes without being re-parented, and
    /// its saved pose (its local pose under the rig) is relative to the desk placement, not to the
    /// room: after a recenter or a new desk placement the layout comes back around the desk.
    /// The panel object itself is an empty holder at the rig's origin with identity rotation, so
    /// until the user moves it everything under it is exactly where it was under WorkspaceRoot.
    ///
    /// THE GRAB. The visible bar is a child of the panel. The thing the hand actually grabs is a
    /// separate "grab proxy" (collider, kinematic Rigidbody, Grabbable, HandGrabInteractable: the
    /// same stack as the stamps) that is NOT a child of the panel. While it is held the Interaction
    /// SDK moves the proxy with the hand and this component moves the panel by the same amount
    /// (position only; lightly smoothed), turning it about the vertical axis so it keeps the angle
    /// to the user's head that it had when it was picked up. It never rolls and its tilt never
    /// changes. Between grabs the proxy is parked on the bar.
    ///
    /// ON RELEASE the pose is constrained: within arm's reach of the head and not behind it, not
    /// overlapping another panel, not sunk into the desk. It is then saved (PlayerPrefs) and
    /// restored on the next launch. <see cref="ResetAll"/> puts every panel back.
    ///
    /// Poke keys under the panel are switched off (their <c>enabled</c> flag) while it is dragged
    /// and for a moment after, so nothing fires from a key being carried through a finger.
    /// Presentation only: it never touches session or task state.
    /// </summary>
    [DefaultExecutionOrder(50)]
    [DisallowMultipleComponent]
    public class MovablePanel : MonoBehaviour
    {
        [Tooltip("Stable id: the PlayerPrefs key of the saved pose is derived from it.")]
        public string panelId = "panel";

        [Header("Shape (panel-local; written by AgentScripts/BuildMovablePanelsV1)")]
        [Tooltip("Centre of the grab bar: the point the panel turns about and that is kept within reach.")]
        public Vector3 pivotLocal;
        [Tooltip("Centre of the panel's footprint.")]
        public Vector3 centreLocal;
        [Tooltip("Footprint (width, depth) in metres, used to keep panels from overlapping.")]
        public Vector2 footprint = new Vector2(0.20f, 0.10f);
        [Tooltip("Yaw of the footprint rectangle in the panel's own space, degrees.")]
        public float footprintYaw;
        [Tooltip("How far the panel's lowest point is below the pivot, metres.")]
        public float baseBelowPivot = 0.004f;
        [Tooltip("Height of the panel, metres: two panels only collide when their heights overlap.")]
        public float height = 0.05f;

        [Header("Grab bar")]
        public Transform bar;
        public Renderer barRenderer;
        public Vector3 barShownScale = Vector3.one;
        [Tooltip("The grabbed object. Not a child of this panel.")]
        public Transform grabProxy;
        public Grabbable grabbable;
        public HandGrabInteractable handGrab;

        [Header("When the bar shows")]
        [Tooltip("The bar is shown while any of these is shown (not scaled away). Empty: always.")]
        public Transform[] visibilitySources;
        [Tooltip("World x scale of each source when it is shown.")]
        public float[] visibilityShownScale;

        [Header("Behaviour")]
        [Tooltip("Time constant of the smoothing that removes hand jitter, seconds.")]
        public float smoothTime = 0.05f;
        public bool faceHead = true;
        public float minReach = PanelLayoutMath.MinReach;
        public float maxReach = PanelLayoutMath.MaxReach;
        [Tooltip("Gap kept between two panels, metres.")]
        public float overlapGap = 0.008f;
        [Tooltip("Within this level distance of the desk frame's origin the panel is kept above the desk plane.")]
        public float deskNearRadius = 0.9f;
        [Tooltip("Lowest the panel's base may be dragged, metres below the desk plane (never below the desk when near it).")]
        public float lowestBelowDesk = PanelLayoutMath.DefaultBelowDesk;
        [Tooltip("Highest the panel's base may be dragged, metres above the desk plane.")]
        public float highestAboveDesk = PanelLayoutMath.DefaultAboveDesk;
        [Tooltip("Near the desk, how far below the detected desk plane the base may still go. The detected table can sit a little above the real desk, which makes panels look too high; this lets the user lower them that much.")]
        public float deskSinkAllowance = 0.03f;
        public bool restoreOnStart = true;
        [Tooltip("Holding the bar still this long offers the Reset layout chip.")]
        public float longPinchSeconds = 1.5f;
        public float stillRadius = 0.02f;
        public PanelResetChip resetChip;

        [Header("Builder bookkeeping (for Revert)")]
        public Transform[] movedChildren;
        public Transform[] originalParents;
        public int[] originalSiblings;

        /// <summary>Every enabled panel.</summary>
        public static readonly List<MovablePanel> All = new List<MovablePanel>(8);
        /// <summary>Raised after <see cref="ResetAll"/>.</summary>
        public static event Action LayoutReset;

        /// <summary>Raised when a drag starts (true) or ends (false).</summary>
        public event Action<bool> DragChanged;
        /// <summary>Raised when the poke keys under the panel were switched back on after a drag.</summary>
        public event Action PokesResumed;

        public bool IsDragging => _dragging;
        public bool PokesSuspended => _pokesSuspended;
        public bool BarShown => _barShown;
        public Vector3 PivotWorld => transform.TransformPoint(pivotLocal);

        static readonly Color BarIdle = Color.Lerp(OfficeTokens.Housing, OfficeTokens.OnPlate, 0.60f);
        static readonly Color BarHover = OfficeTokens.OnPlate;
        static readonly Color BarHeld = OfficeTokens.Teal;
        const float SettleSeconds = 0.18f;
        const float ResumePokesAfter = 0.25f;

        static readonly Vector2[] OtherCentres = new Vector2[16];
        static readonly Vector2[] OtherHalves = new Vector2[16];

        readonly ScaleEase _barEase = new ScaleEase();
        readonly List<PokeInteractable> _pokes = new List<PokeInteractable>(32);
        readonly List<PokeInteractable> _suspended = new List<PokeInteractable>(32);
        MaterialPropertyBlock _block;
        Color _color = BarIdle;
        bool _inited, _barShown, _dragging, _pokesSuspended, _settling, _movedFar, _chipOffered;
        float _resumePokesAt = float.MaxValue, _settleT, _holdStart, _yaw0, _yawDelta;
        Vector3 _defaultPos, _hand0, _smoothed, _pos0, _pivot0, _settleFromPos, _settleToPos;
        Quaternion _defaultRot = Quaternion.identity, _rot0 = Quaternion.identity,
                   _settleFromRot = Quaternion.identity, _settleToRot = Quaternion.identity;
        Transform _head;

        // ───────────────────────── lifecycle ─────────────────────────

        void Awake() { EnsureInit(); }

        void EnsureInit()
        {
            if (_inited) return;
            _inited = true;
            _block = new MaterialPropertyBlock();
            _defaultPos = transform.localPosition;
            _defaultRot = transform.localRotation;
            _barShown = ContentShown();
            if (bar != null) _barEase.Init(bar, ScaleEase.ResolveFull(bar, barShownScale), 0.2f, _barShown);
            if (grabProxy != null) grabProxy.localScale = _barShown ? Vector3.one : Vector3.one * ScaleEase.HiddenFactor;
            if (restoreOnStart) LoadSaved();
        }

        void OnEnable()
        {
            EnsureInit();
            if (!All.Contains(this)) All.Add(this);
        }

        void OnDisable()
        {
            All.Remove(this);
            if (_dragging) { _dragging = false; DragChanged?.Invoke(false); }
            if (_pokesSuspended) ResumePokes();
            if (_settling) { _settling = false; transform.localPosition = _settleToPos; transform.localRotation = _settleToRot; }
        }

        void LateUpdate()
        {
            float dt = Time.unscaledDeltaTime;

            bool shown = ContentShown();
            if (shown != _barShown)
            {
                _barShown = shown;
                if (shown) _barEase.Show(); else _barEase.Hide();
                if (grabProxy != null) grabProxy.localScale = shown ? Vector3.one : Vector3.one * ScaleEase.HiddenFactor;
            }
            _barEase.Tick(dt);

            bool grabbed = shown && grabbable != null && grabProxy != null && grabbable.SelectingPointsCount > 0;
            if (grabbed && !_dragging) BeginDrag(grabProxy.position);
            else if (!grabbed && _dragging) EndDrag(false);

            if (_dragging)
            {
                Vector3 hand = grabProxy.position;
                DragTo(hand, PanelLayoutMath.Smooth(dt, smoothTime));
                TickLongPinch(hand);
            }
            else
            {
                if (_settling) TickSettle(dt);
                RestProxy();
                if (_pokesSuspended && Time.unscaledTime >= _resumePokesAt) ResumePokes();
            }
            UpdateBarColour(dt);
        }

        bool ContentShown()
        {
            if (visibilitySources == null || visibilitySources.Length == 0) return true;
            for (int i = 0; i < visibilitySources.Length; i++)
            {
                Transform t = visibilitySources[i];
                if (t == null || !t.gameObject.activeInHierarchy) continue;
                float full = visibilityShownScale != null && i < visibilityShownScale.Length && visibilityShownScale[i] > 0f
                    ? visibilityShownScale[i] : 1f;
                if (Mathf.Abs(t.lossyScale.x) > full * 0.25f) return true;
            }
            return false;
        }

        // ───────────────────────── dragging ─────────────────────────

        void BeginDrag(Vector3 hand)
        {
            _dragging = true;
            _settling = false;
            _hand0 = hand; _smoothed = hand;
            _pos0 = transform.position; _rot0 = transform.rotation;
            _pivot0 = transform.TransformPoint(pivotLocal);
            _head = RemoteNearUser.Head();
            _yaw0 = _head != null ? PanelLayoutMath.YawToward(_pivot0, _head.position, 0f) : 0f;
            _yawDelta = 0f;
            _holdStart = Time.unscaledTime;
            _movedFar = false; _chipOffered = false;
            SuspendPokes();
            DragChanged?.Invoke(true);
        }

        void DragTo(Vector3 hand, float k)
        {
            _smoothed = Vector3.Lerp(_smoothed, hand, k);
            Vector3 pivot = PanelLayoutMath.FollowHand(_pivot0, _hand0, _smoothed);   // 1:1 including height
            pivot.y = ClampPivotHeight(pivot.y);
            float target = 0f;
            if (faceHead && _head != null)
                target = Mathf.DeltaAngle(_yaw0, PanelLayoutMath.YawToward(pivot, _head.position, _yaw0));
            _yawDelta = Mathf.LerpAngle(_yawDelta, target, k);
            // Yaw only, about the world's up: no roll, and the authored tilt is kept.
            Quaternion turn = Quaternion.AngleAxis(_yawDelta, Vector3.up);
            transform.SetPositionAndRotation(pivot + turn * (_pos0 - _pivot0), PanelLayoutMath.UprightTurn(_rot0, _yawDelta));
        }

        /// <summary>Clamp a world pivot height so the panel's base stays in the vertical band around the desk plane.</summary>
        float ClampPivotHeight(float pivotY)
        {
            Transform frame = transform.parent;
            if (frame == null) return pivotY;
            float defaultBaseY = frame.TransformPoint(_defaultPos + _defaultRot * pivotLocal).y - baseBelowPivot;
            float lo, hi;
            PanelLayoutMath.HeightBand(frame.position.y, defaultBaseY, lowestBelowDesk, highestAboveDesk, out lo, out hi);
            return PanelLayoutMath.ClampHeight(pivotY - baseBelowPivot, lo, hi) + baseBelowPivot;
        }

        void EndDrag(bool snap)
        {
            _dragging = false;
            Vector3 world = ConstrainedPosition();
            Transform frame = transform.parent;
            _settleFromPos = transform.localPosition;
            _settleFromRot = transform.localRotation;
            _settleToPos = frame != null ? frame.InverseTransformPoint(world) : world;
            _settleToRot = transform.localRotation;
            if (snap) { transform.localPosition = _settleToPos; _settling = false; }
            else { _settling = true; _settleT = 0f; }
            Save(_settleToPos, _settleToRot);
            _resumePokesAt = Time.unscaledTime + ResumePokesAfter;
            DragChanged?.Invoke(false);
        }

        /// <summary>Where the panel may rest, given where it is now (world position of the panel object).</summary>
        Vector3 ConstrainedPosition()
        {
            Vector3 start = transform.TransformPoint(pivotLocal);
            Vector3 pivot = start;
            Transform head = RemoteNearUser.Head();
            pivot.y = ClampPivotHeight(pivot.y);
            // Reach is judged with the chosen height kept: the panel slides level, it is not pulled up or down.
            if (head != null) pivot = PanelLayoutMath.ClampToReachKeepHeight(pivot, head.position, head.forward, minReach, maxReach);

            pivot += OverlapShift(pivot - start, head);

            Transform frame = transform.parent;
            if (frame != null)
            {
                Vector3 level = pivot - frame.position; level.y = 0f;
                float baseY = pivot.y - baseBelowPivot;
                pivot.y += PanelLayoutMath.LiftAboveDesk(baseY, frame.position.y - Mathf.Max(0f, deskSinkAllowance), level.magnitude, deskNearRadius) - baseY;
            }
            // The nudge may have pushed it out of reach again: reach wins.
            if (head != null && (pivot - head.position).magnitude > maxReach + 0.02f)
                pivot = PanelLayoutMath.ClampToReachKeepHeight(pivot, head.position, head.forward, minReach, maxReach);

            if (!RemoteNearUser.IsFinite(pivot)) pivot = start;
            return transform.position + (pivot - start);
        }

        Vector3 OverlapShift(Vector3 offset, Transform head)
        {
            Transform frame = transform.parent;
            if (frame == null) return Vector3.zero;
            Vector3 c = frame.InverseTransformPoint(transform.TransformPoint(centreLocal) + offset);
            Vector2 half = PanelLayoutMath.RotatedHalfExtents(footprint * 0.5f, footprintYaw + transform.localEulerAngles.y);
            int n = 0;
            for (int i = 0; i < All.Count && n < OtherCentres.Length; i++)
            {
                MovablePanel o = All[i];
                if (o == null || o == this || o.transform.parent != frame) continue;
                Vector3 oc = frame.InverseTransformPoint(o.transform.TransformPoint(o.centreLocal));
                if (Mathf.Abs(oc.y - c.y) > (height + o.height) * 0.5f + 0.02f) continue;
                OtherCentres[n] = new Vector2(oc.x, oc.z);
                OtherHalves[n] = PanelLayoutMath.RotatedHalfExtents(o.footprint * 0.5f, o.footprintYaw + o.transform.localEulerAngles.y);
                n++;
            }
            if (n == 0) return Vector3.zero;
            Vector2 c2 = new Vector2(c.x, c.z);
            Vector2 preferred = new Vector2(1f, 1f);
            if (head != null)
            {
                Vector3 h = frame.InverseTransformPoint(head.position);
                preferred = new Vector2(h.x - c.x, h.z - c.z);       // when in doubt, toward the user
            }
            Vector2 r = PanelLayoutMath.ResolveOverlap(c2, half, OtherCentres, OtherHalves, n, overlapGap, preferred);
            return frame.TransformVector(new Vector3(r.x - c2.x, 0f, r.y - c2.y));
        }

        void TickSettle(float dt)
        {
            _settleT = Mathf.Min(1f, _settleT + dt / SettleSeconds);
            float inv = 1f - _settleT;
            float e = 1f - inv * inv * inv;
            transform.localPosition = Vector3.LerpUnclamped(_settleFromPos, _settleToPos, e);
            transform.localRotation = Quaternion.SlerpUnclamped(_settleFromRot, _settleToRot, e);
            if (_settleT >= 1f) _settling = false;
        }

        void RestProxy()
        {
            if (grabProxy == null || bar == null) return;
            if ((grabProxy.position - bar.position).sqrMagnitude > 1e-10f
                || Quaternion.Angle(grabProxy.rotation, bar.rotation) > 0.05f)
                grabProxy.SetPositionAndRotation(bar.position, bar.rotation);
        }

        void TickLongPinch(Vector3 hand)
        {
            if (_movedFar) return;
            if ((hand - _hand0).sqrMagnitude > stillRadius * stillRadius)
            {
                _movedFar = true;
                if (_chipOffered && resetChip != null) resetChip.Hide();
                return;
            }
            if (_chipOffered || resetChip == null) return;
            if (Time.unscaledTime - _holdStart < longPinchSeconds) return;
            _chipOffered = true;
            Vector3 at = (bar != null ? bar.position : PivotWorld) + Vector3.up * 0.055f;
            resetChip.ShowAt(at, RemoteNearUser.FacingHead(at, transform.rotation));
        }

        // ───────────────────────── poke keys ─────────────────────────

        void SuspendPokes()
        {
            _resumePokesAt = float.MaxValue;
            if (_pokesSuspended) return;
            _pokesSuspended = true;
            GetComponentsInChildren(true, _pokes);
            for (int i = 0; i < _pokes.Count; i++)
            {
                PokeInteractable p = _pokes[i];
                if (p == null || !p.enabled) continue;
                p.enabled = false;
                _suspended.Add(p);
            }
            _pokes.Clear();
        }

        void ResumePokes()
        {
            for (int i = 0; i < _suspended.Count; i++)
                if (_suspended[i] != null) _suspended[i].enabled = true;
            _suspended.Clear();
            _pokesSuspended = false;
            _resumePokesAt = float.MaxValue;
            PokesResumed?.Invoke();
        }

        // ───────────────────────── bar look ─────────────────────────

        void UpdateBarColour(float dt)
        {
            if (barRenderer == null) return;
            Color target = BarIdle;
            if (_dragging) target = BarHeld;
            else if (handGrab != null && handGrab.State == InteractableState.Hover) target = BarHover;
            Color d = target - _color;
            if (d.r * d.r + d.g * d.g + d.b * d.b < 1e-6f) return;
            _color = Color.Lerp(_color, target, PanelLayoutMath.Smooth(dt, 0.05f));
            d = target - _color;
            if (d.r * d.r + d.g * d.g + d.b * d.b < 1e-5f) _color = target;
            OfficeTokens.Tint(barRenderer, _block, _color);
        }

        // ───────────────────────── persistence ─────────────────────────

        void LoadSaved()
        {
            string text = PlayerPrefs.GetString(PanelLayoutMath.PrefsKey(panelId), "");
            if (string.IsNullOrEmpty(text)) return;
            Vector3 p; Quaternion q;
            if (!PanelLayoutMath.TryDecodePose(text, out p, out q))
            {
                PlayerPrefs.DeleteKey(PanelLayoutMath.PrefsKey(panelId));    // unreadable: forget it
                return;
            }
            transform.localPosition = p;
            transform.localRotation = q;
        }

        void Save(Vector3 localPosition, Quaternion localRotation)
        {
            PlayerPrefs.SetString(PanelLayoutMath.PrefsKey(panelId), PanelLayoutMath.EncodePose(localPosition, localRotation));
            PlayerPrefs.Save();
        }

        /// <summary>The saved pose text, or empty (drivers).</summary>
        public string SavedText() { return PlayerPrefs.GetString(PanelLayoutMath.PrefsKey(panelId), ""); }

        /// <summary>Put this panel back where the scene has it and forget its saved pose.</summary>
        public void ResetToDefault(bool animate)
        {
            EnsureInit();
            PlayerPrefs.DeleteKey(PanelLayoutMath.PrefsKey(panelId));
            _settleFromPos = transform.localPosition; _settleFromRot = transform.localRotation;
            _settleToPos = _defaultPos; _settleToRot = _defaultRot;
            if (animate && isActiveAndEnabled && !_dragging) { _settling = true; _settleT = 0f; }
            else { _settling = false; transform.localPosition = _defaultPos; transform.localRotation = _defaultRot; }
        }

        /// <summary>
        /// Put every panel back where the scene has it and forget the saved layout. The entry point
        /// for the Reset layout chip and for a voice command.
        /// </summary>
        public static void ResetAll()
        {
            for (int i = 0; i < All.Count; i++)
                if (All[i] != null) All[i].ResetToDefault(true);
            PlayerPrefs.Save();
            Action h = LayoutReset;
            if (h != null) h();
        }

        // ───────────────────────── drivers ─────────────────────────

        /// <summary>
        /// Play Mode driver: do what a pinch-drag of the bar by <paramref name="worldDelta"/> would
        /// do, including the release constraints and the save, in one call and without easing.
        /// </summary>
        public void SimulateDrag(Vector3 worldDelta)
        {
            EnsureInit();
            Vector3 p = transform.TransformPoint(pivotLocal);
            BeginDrag(p);
            DragTo(p + worldDelta, 1f);
            EndDrag(true);
            ResumePokes();
        }

        /// <summary>Play Mode driver: offer the Reset layout chip as a long pinch would.</summary>
        public void SimulateLongPinch()
        {
            if (resetChip == null) return;
            Vector3 at = (bar != null ? bar.position : PivotWorld) + Vector3.up * 0.055f;
            resetChip.ShowAt(at, RemoteNearUser.FacingHead(at, transform.rotation));
        }

        public string Describe()
        {
            StringBuilder sb = new StringBuilder(160);
            Transform head = RemoteNearUser.Head();
            Vector3 pivot = PivotWorld;
            sb.Append(panelId).Append(": local ").Append(transform.localPosition.ToString("F3"))
              .Append(" yaw ").Append(transform.localEulerAngles.y.ToString("F0"))
              .Append(" | pivot world ").Append(pivot.ToString("F3"));
            if (head != null) sb.Append(" head ").Append(Vector3.Distance(head.position, pivot).ToString("F2")).Append(" m");
            Transform frame = transform.parent;
            if (frame != null) sb.Append(" base above desk ").Append(((pivot.y - baseBelowPivot - frame.position.y) * 1000f).ToString("F0")).Append(" mm");
            sb.Append(" | bar ").Append(_barShown ? "shown" : "hidden")
              .Append(_dragging ? " DRAGGING" : "").Append(_pokesSuspended ? " pokes-off" : "")
              .Append(" footprint ").Append((footprint * 1000f).ToString("F0")).Append(" mm")
              .Append(" saved=").Append(string.IsNullOrEmpty(SavedText()) ? "no" : "yes");
            return sb.ToString();
        }
    }
}
