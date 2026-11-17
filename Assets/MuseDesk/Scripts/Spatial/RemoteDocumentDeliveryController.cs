using System;
using UnityEngine;
using MuseDesk.Agents;
using MuseDesk.Documents;
using MuseDesk.Networking;

namespace MuseDesk.Spatial
{
    /// <summary>
    /// Brings the decision paper to the user when the office is out of reach (Floor mode, or
    /// Ceiling mode while docked). PRESENTATION ONLY: the decision id, the paper object and the
    /// whole decision flow stay with <see cref="DocumentDeliveryController"/> and
    /// <see cref="HeldPaperController"/>; this component never answers, creates or hides a paper.
    ///
    /// How it moves the paper without editing those classes. While the paper has not been taken,
    /// HeldPaperController.LateUpdate places it at <c>followHand.position</c> plus a fixed lift,
    /// turned to the head. This component re-points the public <c>followHand</c> field at a
    /// transform it owns (<see cref="courierAnchor"/>), animates that transform from the agent's
    /// hand to a platform at the office edge and then along an eased arc to the near-user pose,
    /// and puts the original value back when the paper is taken, the delivery is abandoned, the
    /// session resets or this component is disabled. The paper stays parented to the agent's
    /// hold point throughout (so its carried size is the one HeldPaperController computed against
    /// the 100x rig bone), and taking it still goes through HeldPaperController.OnGrabbed, which
    /// detaches it to the world and unfolds it exactly as at the desk.
    ///
    /// In Desk mode (<see cref="SpatialWorkspaceState.IsRemote"/> false) nothing is re-pointed
    /// and nothing moves. The one thing that runs in every mode is the transition hold: while a
    /// hand holds the paper the placement side is asked not to move the office.
    ///
    /// Fallback: if the courier path takes longer than the timeout, or computing it fails, the
    /// paper is put at the near-user pose at once. The backend is never waited on or delayed.
    /// </summary>
    public class RemoteDocumentDeliveryController : MonoBehaviour
    {
        [Header("Wiring")]
        public SessionController controller;
        [Tooltip("The owner of the decision flow. Only its public fileSeconds is adjusted while remote.")]
        public DocumentDeliveryController delivery;
        [Tooltip("The decision paper's HeldPaperController.")]
        public HeldPaperController paper;
        [Tooltip("The Researcher's movement, to wait for the walk-up before the courier leaves. Optional.")]
        public AgentMovement researcherMovement;
        [Tooltip("The Researcher's animator bridge, to lower the empty hand when the courier leaves. Optional.")]
        public MuseDesk.Agents.AgentAnimatorBridge researcherBridge;
        [Tooltip("A transform this component owns and animates (NOT under WorkspaceRoot).")]
        public Transform courierAnchor;
        [Tooltip("The courier (the DocumentCourier model, or the primitive tray). Optional: without it the paper travels by itself. This component alone moves it; the courier only plays its own clips.")]
        public CourierVisual courier;
        [Tooltip("Used when the placement side has not published the office root.")]
        public Transform workspaceRootFallback;

        [Header("Path")]
        [Tooltip("The delivery platform, in the office root's local space (the office edge on the user's side).")]
        public Vector3 platformLocal = new Vector3(-0.20f, 0.06f, 0.33f);
        [Tooltip("How far the arc rises above the straight line (m).")]
        public float riseHeight = 0.12f;
        [Range(0.9f, 1.4f)] public float minFlightSeconds = 0.9f;
        [Range(0.9f, 1.4f)] public float maxFlightSeconds = 1.4f;
        [Tooltip("Seconds the agent's offer is left to read before the courier takes the paper.")]
        public float offerDwellSeconds = 0.7f;
        [Tooltip("Seconds from the agent's hand to the platform.")]
        public float platformSeconds = 0.45f;
        [Tooltip("The courier path may take at most this long; then the paper is put at the user.")]
        public float courierTimeoutSeconds = 3f;
        [Tooltip("How far below the paper's lower edge the tray rides (m).")]
        public float trayDrop = 0.012f;
        [Tooltip("Seconds for the empty courier to return to the office.")]
        public float returnSeconds = 0.9f;
        [Tooltip("The hovering paper follows the near-user pose only after it moved this far (m).")]
        public float reLatchDistance = 0.25f;
        [Tooltip("Speed at which the hovering paper moves to a new near-user pose (m/s).")]
        public float hoverFollowSpeed = 0.6f;

        [Header("Behaviour")]
        [Tooltip("When the courier leaves with the paper, the agent stops holding its hand out.")]
        public bool lowerOfferWhenCourierLeaves = true;
        [Tooltip("Filing glide time used while the office is remote (the out-tray is far away).")]
        public float remoteFileSeconds = 1.1f;

        readonly RemoteDeliveryMachine _machine = new RemoteDeliveryMachine();
        readonly RemoteHoldLedger _ledger = new RemoteHoldLedger(
            SpatialWorkspaceState.AcquireTransitionHold, SpatialWorkspaceState.ReleaseTransitionHold);

        SessionReducer _reducer;
        bool _bound;
        bool _carrying, _swapped, _returning, _hoverLatched;
        Transform _origFollow;
        Vector3 _legFrom, _hover, _returnFrom, _lastTarget;
        float _lift, _returnStart;
        bool _fileSecondsSaved;
        float _origFileSeconds;
        bool _loggedFailure;

        /// <summary>Current phase of the remote delivery (Idle in Desk mode).</summary>
        public RemoteDeliveryState State => _machine.State;
        /// <summary>True while this component is positioning the paper.</summary>
        public bool IsCarrying => _carrying;
        /// <summary>True while a transition hold is outstanding (a hand is holding the paper).</summary>
        public bool HoldingTransition => _ledger.Holding;
        /// <summary>True when the current delivery reached the user by the fallback snap.</summary>
        public bool SnappedByTimeout => _machine.SnappedByTimeout;

        void Awake()
        {
            _machine.StateChanged += OnStateChanged;
        }

        void OnEnable()
        {
            SpatialWorkspaceState.Changed += OnSpatialChanged;
        }

        void Bind()
        {
            _reducer = controller.Reducer;
            _reducer.SessionReset += OnSessionReset;
            _bound = true;
        }

        void OnDisable()
        {
            SpatialWorkspaceState.Changed -= OnSpatialChanged;
            if (_bound && _reducer != null) _reducer.SessionReset -= OnSessionReset;
            _reducer = null;
            _bound = false;
            Abandon();
        }

        // Session reset (replay): whatever was in flight is over; the paper is hidden by its owner.
        void OnSessionReset() { Abandon(); }

        /// <summary>Stop any delivery, give the paper's follow target back, release the hold
        /// (the safety release) and put the courier away.</summary>
        void Abandon()
        {
            _machine.Reset();          // raises StateChanged -> EndCarry + RestoreFileSeconds
            EndCarry();
            RestoreFileSeconds();
            _ledger.Release();
            _returning = false;
            if (courier != null) courier.HideNow();
        }

        void Update()
        {
            if (!_bound && controller != null) Bind();
            if (paper == null) return;

            // What the paper is doing, read from its public state only.
            bool active = paper.isActiveAndEnabled && paper.agentHoldPoint != null;
            bool taken = active && paper.IsTaken;
            bool offered = active && !taken;

            // The office must not be moved while a hand holds the paper (any mode).
            _ledger.Set(active && paper.IsHeld);

            float now = Time.unscaledTime;
            Vector3 target = Vector3.zero;
            bool canDeliver = courierAnchor != null && SpatialWorkspaceState.IsRemote
                              && RemoteNearUser.TryGetTarget(out target);
            _lastTarget = target;
            bool settled = researcherMovement == null || !researcherMovement.IsMoving;

            _machine.offerDwellSeconds = offerDwellSeconds;
            _machine.platformSeconds = platformSeconds;
            _machine.courierTimeoutSeconds = courierTimeoutSeconds;
            _machine.Tick(now, canDeliver, offered, taken, settled);

            if (_carrying)
            {
                try { DriveCarry(now, target); }
                catch (Exception ex)
                {
                    // Anything wrong on the way: the paper goes to the user at once.
                    if (!_loggedFailure) { _loggedFailure = true; Debug.LogException(ex); }
                    _machine.SnapNow(now);
                    if (courierAnchor != null) courierAnchor.position = target - Vector3.up * _lift;
                }
            }
            if (_returning) DriveReturn(now);
        }

        void OnStateChanged(RemoteDeliveryState prev, RemoteDeliveryState next)
        {
            switch (next)
            {
                case RemoteDeliveryState.ToPlatform:
                    BeginCarry();
                    if (courier != null) courier.PlayLoad();
                    break;

                case RemoteDeliveryState.Flying:
                    BeginCarry();
                    if (courier != null) courier.PlayFly();
                    if (courierAnchor != null)
                    {
                        _legFrom = courierAnchor.position;
                        float dist = Vector3.Distance(_legFrom, _lastTarget);
                        _machine.flightSeconds = Mathf.Clamp(0.55f + 0.4f * dist,
                            Mathf.Min(minFlightSeconds, maxFlightSeconds), Mathf.Max(minFlightSeconds, maxFlightSeconds));
                    }
                    // The paper has left the agent's hand: stop holding the empty hand out.
                    if (lowerOfferWhenCourierLeaves && researcherBridge != null) researcherBridge.EndOffer();
                    break;

                case RemoteDeliveryState.Hovering:
                    BeginCarry();
                    _hoverLatched = false;
                    NormalizeHeldScale();
                    if (courier != null) courier.PlayPresent();
                    break;

                case RemoteDeliveryState.Taken:
                    // HeldPaperController has detached the paper to the world and is unfolding it.
                    EndCarry();
                    StartReturn();
                    if (SpatialWorkspaceState.IsRemote) ApplyFileSeconds();
                    break;

                case RemoteDeliveryState.AtOffice:
                    EndCarry();
                    StartReturn();
                    break;

                default:    // Idle
                    EndCarry();
                    StartReturn();
                    RestoreFileSeconds();
                    break;
            }
        }

        void OnSpatialChanged()
        {
            // The office came within reach (or went away) while the user has the paper: the filing
            // glide length follows.
            if (_machine.State != RemoteDeliveryState.Taken) return;
            if (SpatialWorkspaceState.IsRemote) ApplyFileSeconds(); else RestoreFileSeconds();
        }

        // ── carrying ──

        void BeginCarry()
        {
            if (_carrying || paper == null || courierAnchor == null) return;
            _carrying = true;
            _returning = false;
            // The same lift HeldPaperController.LateUpdate adds above followHand, from its public fields.
            _lift = paper.FullScale.y * paper.heldScale * 0.5f * paper.sheetHeight * 0.85f;
            _legFrom = paper.transform.position - Vector3.up * _lift;     // no jump when the target is swapped
            courierAnchor.position = _legFrom;
            _origFollow = paper.followHand;
            paper.followHand = courierAnchor;
            _swapped = true;
            NormalizeHeldScale();
            if (courier != null)
            {
                courier.SetPose(PlatformWorld() + Vector3.down * trayDrop, YawToHead(PlatformWorld()));
                courier.Show();
            }
        }

        void EndCarry()
        {
            _carrying = false;
            if (!_swapped) return;
            _swapped = false;
            // Only put back what this component set; if someone else re-pointed it meanwhile, leave it.
            if (paper != null && paper.followHand == courierAnchor) paper.followHand = _origFollow;
            _origFollow = null;
        }

        /// <summary>
        /// Re-apply the carried size exactly as HeldPaperController.OnEnable computed it
        /// (full size x heldScale, divided by the hold point's lossy scale). The paper is never
        /// re-parented here, so this only matters if the office root was rescaled after the paper
        /// was shown; it keeps the cover the same size at the user as in the agent's hand.
        /// </summary>
        void NormalizeHeldScale()
        {
            if (paper == null || paper.IsTaken) return;
            Transform t = paper.transform;
            Transform parent = t.parent;
            if (parent == null || parent != paper.agentHoldPoint) return;
            Vector3 p = parent.lossyScale;
            Vector3 w = paper.FullScale * paper.heldScale;
            t.localScale = new Vector3(
                w.x / Mathf.Max(1e-5f, Mathf.Abs(p.x)),
                w.y / Mathf.Max(1e-5f, Mathf.Abs(p.y)),
                w.z / Mathf.Max(1e-5f, Mathf.Abs(p.z)));
        }

        void DriveCarry(float now, Vector3 target)
        {
            Vector3 hover = target - Vector3.up * _lift;     // the sheet's centre ends up at the target
            Vector3 p;
            switch (_machine.State)
            {
                case RemoteDeliveryState.ToPlatform:
                {
                    float k = Ease(_machine.Progress);
                    p = Vector3.Lerp(_legFrom, PlatformWorld(), k);
                    p.y += Mathf.Sin(k * Mathf.PI) * 0.025f;
                    break;
                }
                case RemoteDeliveryState.Flying:
                {
                    float k = Ease(_machine.Progress);
                    p = Vector3.Lerp(_legFrom, hover, k);
                    p.y += Mathf.Sin(k * Mathf.PI) * riseHeight;
                    break;
                }
                case RemoteDeliveryState.Hovering:
                {
                    if (!_hoverLatched)
                    {
                        _hover = hover;
                        _hoverLatched = true;
                        if (_machine.SnappedByTimeout) courierAnchor.position = hover;   // the fallback: no glide
                    }
                    else if ((hover - _hover).sqrMagnitude > reLatchDistance * reLatchDistance) _hover = hover;
                    p = Vector3.MoveTowards(courierAnchor.position, _hover, hoverFollowSpeed * Time.unscaledDeltaTime);
                    break;
                }
                default:
                    return;
            }

            if (!RemoteNearUser.IsFinite(p))
            {
                _machine.SnapNow(now);
                if (!RemoteNearUser.IsFinite(hover)) return;
                p = hover;
            }
            courierAnchor.position = p;
            if (courier != null) courier.SetPose(p + Vector3.down * trayDrop, YawToHead(p));
        }

        // ── courier going home ──

        void StartReturn()
        {
            if (courier == null || !courier.Shown) { _returning = false; return; }
            if (!_returning) courier.PlayReturn();
            _returning = true;
            _returnFrom = courier.CarryPoint;       // SetPose places this point (the attach socket when there is art)
            _returnStart = Time.unscaledTime;
        }

        void DriveReturn(float now)
        {
            if (courier == null) { _returning = false; return; }
            float k = Mathf.Clamp01((now - _returnStart) / Mathf.Max(0.1f, returnSeconds));
            float e = Ease(k);
            Vector3 p = Vector3.Lerp(_returnFrom, PlatformWorld() + Vector3.down * trayDrop, e);
            p.y += Mathf.Sin(e * Mathf.PI) * riseHeight * 0.5f;
            if (RemoteNearUser.IsFinite(p)) courier.SetPose(p, courier.transform.rotation);
            if (k >= 1f)
            {
                _returning = false;
                courier.PlayDocked();
                courier.Hide();
            }
        }

        // ── filing glide ──

        // DocumentDeliveryController glides a decided paper to its fileTarget (the out-tray, at the
        // office) in fileSeconds. With the office 1.5 m away the default 0.55 s is a dart; while
        // remote the public field is lengthened and put back afterwards.
        void ApplyFileSeconds()
        {
            if (delivery == null) return;
            if (!_fileSecondsSaved) { _origFileSeconds = delivery.fileSeconds; _fileSecondsSaved = true; }
            delivery.fileSeconds = remoteFileSeconds;
        }

        void RestoreFileSeconds()
        {
            if (!_fileSecondsSaved) return;
            _fileSecondsSaved = false;
            if (delivery != null && Mathf.Approximately(delivery.fileSeconds, remoteFileSeconds))
                delivery.fileSeconds = _origFileSeconds;
        }

        // ── helpers ──

        Vector3 PlatformWorld()
        {
            Transform ws = SpatialWorkspaceState.WorkspaceRoot;
            if (ws == null) ws = workspaceRootFallback;
            if (ws != null) return ws.TransformPoint(platformLocal);
            return courierAnchor != null ? courierAnchor.position : transform.position;
        }

        static Quaternion YawToHead(Vector3 position)
        {
            Transform head = RemoteNearUser.Head();
            if (head == null) return Quaternion.identity;
            Vector3 away = position - head.position; away.y = 0f;
            if (away.sqrMagnitude < 1e-6f) return Quaternion.identity;
            return Quaternion.LookRotation(away.normalized, Vector3.up);
        }

        static float Ease(float k) { return Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(k)); }

        // ── Editor Play Mode driver hooks (no hand, no placement director needed) ──

        /// <summary>Pretend the office is remote (Floor mode) with the near-user pose in front of
        /// the camera. Writes the shared state the placement director normally writes.</summary>
        public void DriverForceRemote()
        {
            Transform ws = SpatialWorkspaceState.WorkspaceRoot != null ? SpatialWorkspaceState.WorkspaceRoot : workspaceRootFallback;
            if (ws != null) SpatialWorkspaceState.PublishRoot(ws);
            Vector3 p;
            Transform head = RemoteNearUser.Head();
            if (head != null)
            {
                Vector3 fwd = head.forward; fwd.y = 0f;
                if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
                p = head.position + fwd.normalized * RemoteNearUser.FallbackForward + Vector3.down * RemoteNearUser.FallbackDrop;
            }
            else p = ws != null ? ws.TransformPoint(new Vector3(0f, 0.15f, 0.45f)) : Vector3.zero;
            DriverForceRemoteAt(p);
        }

        /// <summary>Pretend the office is remote with the near-user pose 1.9 m from the office on
        /// its user side (a full-length flight; the Editor camera will not be near it).</summary>
        public void DriverForceRemoteFar()
        {
            Transform ws = SpatialWorkspaceState.WorkspaceRoot != null ? SpatialWorkspaceState.WorkspaceRoot : workspaceRootFallback;
            if (ws != null) SpatialWorkspaceState.PublishRoot(ws);
            DriverForceRemoteAt(ws != null ? ws.TransformPoint(new Vector3(0f, 0.15f, 1.9f)) : new Vector3(0f, 1f, 1.9f));
        }

        /// <summary>Pretend the office is remote with the near-user pose at a world position.</summary>
        public void DriverForceRemoteAt(Vector3 nearUserWorld)
        {
            SpatialWorkspaceState.PublishNearUserPose(new Pose(nearUserWorld, Quaternion.identity), true);
            SpatialWorkspaceState.Publish(WorkspaceMode.Floor, true, false);
        }

        /// <summary>Back to Desk mode.</summary>
        public void DriverForceDesk()
        {
            SpatialWorkspaceState.Publish(WorkspaceMode.Desk, false, false);
            SpatialWorkspaceState.PublishNearUserPose(Pose.identity, false);
        }

        /// <summary>Take the paper as a grab and release would (HeldPaperController.TakeNow).</summary>
        public void DriverTake() { if (paper != null) paper.TakeNow(); }

        /// <summary>Force the fallback: the paper goes to the near-user pose now.</summary>
        public void DriverSnap() { _machine.SnapNow(Time.unscaledTime); }

        /// <summary>One line describing the delivery, for a driver's log.</summary>
        public string DriverReport()
        {
            string where = paper != null ? paper.transform.position.ToString("F3") : "(no paper)";
            string scale = paper != null ? paper.transform.lossyScale.ToString("F4") : "-";
            return "remote=" + SpatialWorkspaceState.IsRemote + " mode=" + SpatialWorkspaceState.Mode
                 + " state=" + _machine.State + " progress=" + _machine.Progress.ToString("F2")
                 + " carrying=" + _carrying + " snapped=" + _machine.SnappedByTimeout
                 + " hold=" + _ledger.Holding + " (acquired " + _ledger.AcquireCount + ", released " + _ledger.ReleaseCount + ")"
                 + " transitionHeld=" + SpatialWorkspaceState.TransitionHeld
                 + " paperPos=" + where + " paperLossyScale=" + scale
                 + " taken=" + (paper != null && paper.IsTaken) + " ready=" + (paper != null && paper.ReadyForDecision);
        }
    }
}
