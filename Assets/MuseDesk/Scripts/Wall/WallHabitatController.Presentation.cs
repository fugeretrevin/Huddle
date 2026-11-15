using System.Collections.Generic;
using UnityEngine;
using MuseDesk.Spatial;
using MuseDesk.Spatial.Wall;

namespace MuseDesk.Wall
{
    /// <summary>
    /// A9 adapter: desk visit and large portal presented through <see cref="PresentationLifecycle"/> (one presentation per
    /// logical agent, Cancel at any time, backend-lost cleanup, exactly-once ReturnHome/Closed), portal spots through
    /// <see cref="PortalSpotPlanner"/>/<see cref="PortalSpotTracker"/>. The legacy flows keep the states and animation; this
    /// file only mirrors them and adds guarantees. <see cref="usePresentationLifecycle"/> = false restores the old path exactly.
    /// </summary>
    public partial class WallHabitatController
    {
        [Tooltip("A9: route visit/portal through PresentationLifecycle and plan portal spots with PortalSpotPlanner. False = the previous behaviour.")]
        public bool usePresentationLifecycle = true;

        PresentationMirror _mirror;
        BackendLossDetector _lossDetector;
        string _lastVisitAgent, _lastPortalAgent;
        readonly PortalSpotSettings _spotSettings = new PortalSpotSettings();
        readonly PortalRoomBounds _bounds = new PortalRoomBounds();
        PortalSpotTracker _spotTracker;
        float _nextBoundsAt;
        bool _portalSpotManual;
        bool _portalForcedHome;

        /// <summary>True when the last portal plan found no clear large spot (the card says "Not enough space here").</summary>
        public bool PortalSpotIsFallback { get; private set; }
        public PresentationMirror Mirror => _mirror;

        void EnsureMirror()
        {
            if (_mirror != null) return;
            _mirror = new PresentationMirror();
            _mirror.visit.Opened += (pid, agent) => _lastVisitAgent = agent;
            _mirror.portal.Opened += (pid, agent) => _lastPortalAgent = agent;
            _mirror.visit.ReturnHome += pid => ReturnVisitHome();
            _mirror.portal.ReturnHome += pid => ReturnPortalHome();
            _mirror.visit.Closed += OnPresentationClosed;
            _mirror.portal.Closed += OnPresentationClosed;
        }

        /// <summary>Per frame, from TickInteraction. No allocation in steady state.</summary>
        void TickPresentations()
        {
            if (!usePresentationLifecycle) return;
            EnsureMirror();
            _mirror.visit.comfortMode = _mirror.portal.comfortMode = Comfort;

            if (_backend != null && _lossDetector.Update(_backend.IsLive)) { _mirror.BackendLost(); return; }

            bool vAct = _visit.Agent != null && (_visit.State == VisitState.Carrying || _visit.State == VisitState.Perched || _visit.State == VisitState.Returning);
            bool pAct = _portal.Agent != null && _portal.State != PortalState.Hidden;
            _mirror.Sync(vAct, vAct ? _visit.Agent : null, vAct ? HomeAnchorOf(_visit.Agent) : Vector3.zero,
                         pAct, pAct ? _portal.Agent : null, pAct ? HomeAnchorOf(_portal.Agent) : Vector3.zero);
            _mirror.visit.Tick(Time.unscaledDeltaTime);
            _mirror.portal.Tick(Time.unscaledDeltaTime);
        }

        Vector3 HomeAnchorOf(string agent)
        {
            Transform a = _view != null && agent != null ? _view.Avatar(RoleOf(agent)) : null;
            return a != null ? a.position : PerchWorld;
        }

        /// <summary>Cancel any visit/portal presentation now (also callable by drivers). The agents go home exactly once.</summary>
        public void CancelPresentations()
        {
            if (usePresentationLifecycle) { EnsureMirror(); _mirror.CancelAll(); }
            else
            {
                if (_visit != null && _visit.Busy) _visit.Return(VisitEndReason.Cancelled);
                if (_portal != null) _portal.Interrupt("Cancelled");
            }
        }

        /// <summary>Backend disconnect handler: clean up immediately (no animation), nothing is sent to the backend.</summary>
        public void NotifyBackendLost()
        {
            if (!usePresentationLifecycle) return;
            EnsureMirror();
            _mirror.BackendLost();
        }

        // Idempotent: runs once per started session; a stale close (flow already ended) finds nothing to do.
        void ReturnVisitHome()
        {
            if (_visit != null && _visit.Agent != null && _visit.Agent == _lastVisitAgent
                && (_visit.State == VisitState.Carrying || _visit.State == VisitState.Perched)) _visit.Return(VisitEndReason.Cancelled);
        }

        void ReturnPortalHome()
        {
            if (_portal != null && _portal.Agent != null && _portal.Agent == _lastPortalAgent && _portal.State != PortalState.Hidden) _portal.Interrupt("Cancelled");
        }

        void OnPresentationClosed(PresentationClosed c)
        {
            if (c.reason == PresentationCloseReason.BackendLost) ForcePresentationsHome();
        }

        /// <summary>Immediate visual cleanup (backend lost): the flows are returned/interrupted and the clones removed now.</summary>
        void ForcePresentationsHome()
        {
            if (_visit != null && _visit.Busy) _visit.Return(VisitEndReason.Cancelled);
            if (_portal != null) _portal.Interrupt("Connection lost");
            ReleaseHands();
            DestroyProxy();
            _portalForcedHome = _portal != null && _portal.State != PortalState.Hidden;
            if (_portalAvatar != null) { Destroy(_portalAvatar.gameObject); _portalAvatar = null; }
            if (_portalRoot != null) _portalRoot.gameObject.SetActive(false);
            EndDeskVisit();
        }

        // ───────────────────────── portal spot planning ─────────────────────────

        /// <summary>Plan the large-portal spot. Returns the first candidate; the alternatives are in <c>_portalSpots</c>.</summary>
        PortalCandidate PlanPortalSpots(SeatedUser user)
        {
            _portalSpots.Clear();
            PortalSpotIsFallback = false;
            _portalSpotManual = false;
            IRoomGeometry room = roomService != null ? roomService.Room : null;

            if (usePresentationLifecycle && PortalSpotAdapter.Build(room, user, _bounds))
            {
                _spotSettings.bodyRadius = portalSettings.footprintRadius;
                _spotSettings.avatarHeight = portalSettings.maxAvatarHeight;
                _spotTracker = new PortalSpotTracker(_spotSettings);
                _nextBoundsAt = Time.unscaledTime + 0.5f;
                float h = PortalSpotAdapter.FitHeight(portalSettings.maxAvatarHeight, _bounds.floorY, _bounds.ceilingY, portalSettings.headRoom, null);
                List<PortalSpot> spots = PortalSpotAdapter.ListSpots(user, _bounds, _spotSettings);
                for (int i = 0; i < spots.Count; i++) _portalSpots.Add(PortalSpotAdapter.ToCandidate(spots[i], true, h));
                if (_portalSpots.Count > 0)
                {
                    _spotTracker.Update(Time.unscaledTime, user, _bounds);   // seed the tracker so the preview follows with hysteresis
                    return _portalSpots[0];
                }
                PortalSpotIsFallback = true;
                return PortalPlacementEvaluator.ManualSmall(user, 70f, portalSettings);
            }

            _spotTracker = null;
            _portalSpots = PortalPlacementEvaluator.Find(room, user, portalSettings);
            if (_portalSpots.Count > 0) return _portalSpots[0];
            PortalSpotIsFallback = usePresentationLifecycle && room != null;                             // a scanned room with no space; no scan is "unchecked", not "no space"
            return PortalPlacementEvaluator.ManualSmall(user, 70f, portalSettings);
        }

        /// <summary>While the ring is shown, follow the user with hysteresis (never jitters, never in front of the face).</summary>
        void TickPortalSpotTracking()
        {
            if (!usePresentationLifecycle || _spotTracker == null || _portalSpotManual || PortalSpotIsFallback) return;
            if (_portal.State != PortalState.Preview && _portal.State != PortalState.AwaitConfirm) return;
            float now = Time.unscaledTime;
            SeatedUser user = ReadUser();
            if (now >= _nextBoundsAt)
            {
                _nextBoundsAt = now + 0.5f;
                PortalSpotAdapter.Build(roomService != null ? roomService.Room : null, user, _bounds);
            }
            if (!_bounds.hasFloor) return;
            PortalSpot s = _spotTracker.Update(now, user, _bounds);
            if (s.valid && !s.isFallback && (s.position - _portal.Spot.position).sqrMagnitude > 0.0025f)
            {
                float h = PortalSpotAdapter.FitHeight(portalSettings.maxAvatarHeight, _bounds.floorY, _bounds.ceilingY, portalSettings.headRoom, null);
                _portal.UpdateSpot(PortalSpotAdapter.ToCandidate(s, true, h));
            }
        }

        /// <summary>Large-avatar height under a known ceiling (PortalAvatarScale), feet stay on the spot's floor.</summary>
        float PortalFitFactor()
        {
            if (!usePresentationLifecycle || !_bounds.hasFloor) return 1f;
            float want = _portal.Spot.avatarHeight;
            float fit = PortalSpotAdapter.FitHeight(want, _portal.Spot.position.y, _bounds.ceilingY, portalSettings.headRoom, null);
            return want > 1e-4f ? Mathf.Clamp(fit / want, 0.05f, 1f) : 1f;
        }

        // ───────────────────────── tether data for the view ─────────────────────────

        /// <summary>Tether from the visiting agent's home to its desk projection (draw a thin line from/to with alpha; inactive otherwise).</summary>
        public PresentationTether VisitTether
        {
            get
            {
                if (_proxy == null || _visit.Agent == null) return default(PresentationTether);
                PresentationPhase ph; float k;
                switch (_visit.State)
                {
                    case VisitState.Carrying: ph = PresentationPhase.Opening; k = Mathf.Clamp01(_proxyT); break;
                    case VisitState.Perched: ph = PresentationPhase.Presenting; k = 1f; break;
                    case VisitState.Returning: ph = PresentationPhase.Closing; k = _visit.ReturnProgress; break;
                    default: return default(PresentationTether);
                }
                return PresentationMirror.TetherFor(ph, k, HomeAnchorOf(_visit.Agent), _proxy.position);
            }
        }

        /// <summary>Tether from the agent's home to the portal avatar's chest.</summary>
        public PresentationTether PortalTether
        {
            get
            {
                if (_portal.Agent == null || _portal.State == PortalState.Hidden || _portal.State == PortalState.Preview || _portal.State == PortalState.AwaitConfirm)
                    return default(PresentationTether);
                PresentationPhase ph; float k = _portal.Progress;
                switch (_portal.State)
                {
                    case PortalState.Opening: case PortalState.Emerging: ph = PresentationPhase.Opening; break;
                    case PortalState.Conversing: ph = PresentationPhase.Presenting; break;
                    default: ph = PresentationPhase.Closing; break;
                }
                Vector3 chest = _portal.Spot.position + Vector3.up * (_portal.Spot.avatarHeight * 0.55f);
                return PresentationMirror.TetherFor(ph, k, HomeAnchorOf(_portal.Agent), chest);
            }
        }
    }
}
