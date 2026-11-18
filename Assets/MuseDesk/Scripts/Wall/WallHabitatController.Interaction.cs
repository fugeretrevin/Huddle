using System;
using System.Collections.Generic;
using UnityEngine;
using Oculus.Interaction;
using Oculus.Interaction.Input;
using MuseDesk.Spatial;
using MuseDesk.Spatial.Wall;

namespace MuseDesk.Wall
{
    /// <summary>
    /// Hands, Select Agents, the small desk visit with read-only /btw questions, and the large-agent portal.
    ///
    /// One pinch arbiter (the app's rc6 <see cref="HandOwnershipArbiter"/>, sharing <see cref="HandClaimRegistry.Shared"/>):
    /// anything held, or a grab/poke/UI hover, wins over these gestures; a lost hand cancels, never confirms.
    /// The carried agent is a PROJECTION (a visual clone): the home avatar stays at its desk with an "on visit"
    /// marker and the backend task is never touched. Questions go to the read-only side-question route only.
    /// </summary>
    public partial class WallHabitatController
    {
        AgentPresentationRegistry _registry;
        AgentVisitFlow _visit;
        PortalFlow _portal;
        SideConversationSession _side;
        readonly HandOwnershipArbiter _arb = new HandOwnershipArbiter(HandClaimRegistry.Shared);
        readonly AimHandSample[] _samples = new AimHandSample[2];
        readonly AimHandSample[] _sim = new AimHandSample[2];
        readonly bool[] _simOn = new bool[2];
        readonly List<IHand> _handsL = new List<IHand>(4), _handsR = new List<IHand>(4);
        float _nextHandSearch;
        int _hoverRole = -1;

        // ── Select Agents force grab (AgentSelectGrabV1) ──
        [Header("Select Agents grab (AgentSelectGrabV1)")]
        [Tooltip("V2 grab: forgiving ray target with a hover lock, the pinch takes what it BEGAN on, refusals are said out loud, " +
                 "a forgiving drop (pull it toward you), pick it up off the perch and let go toward the wall to put it back, " +
                 "and the hidden desk office's interactables are switched off while Wall is active. False = the previous path.")]
        public bool useSelectGrabV2 = true;
        public AgentSelectGrabSettings selectGrab = new AgentSelectGrabSettings();
        public VisitDropSettings visitDrop = new VisitDropSettings();
        [Tooltip("Switch off the hidden desk office's ISDK interactables while Wall is active (they are invisible and ate far pinches).")]
        public bool muteHiddenOfficeInteractables = true;
        [Tooltip("While Select Agents is armed or carrying, switch off distance-grab interactables so a far pinch means 'agent'. Near grabs and pokes stay on.")]
        public bool muteDistanceGrabWhileSelecting = true;
        [Tooltip("A pinch this close to the perched projection (m) picks it up again.")]
        public float regrabReach = 0.16f;
        [Tooltip("The rig's hand-tracking visuals (OVRHandVisualLeft/Right). Wired by AgentSelectGrabV1; when empty or untracked, " +
                 "every HandVisual of that hand is tried and the TRACKED one is used (the rig also has controller-driven hand visuals).")]
        public HandVisual leftHandVisual, rightHandVisual;

        readonly AgentTargetLock _lock = new AgentTargetLock();
        readonly List<Vector3> _targetCenters = new List<Vector3>(4) { Vector3.zero, Vector3.zero, Vector3.zero, Vector3.zero };
        readonly List<bool> _targetEligible = new List<bool>(4) { false, false, false, false };
        readonly bool[] _pinchPending = new bool[2];
        AgentSelectInteractableMute _mute;
        float _returnFromScale;
        int _grabs, _regrabs, _refusals;

        /// <summary>The last thing the user was told about a refused agent pinch (drivers and the log read it).</summary>
        public string LastSelectRefusal { get; private set; }
        public int HoverRole => _hoverRole;
        public int SelectGrabs => _grabs;
        public int SelectRegrabs => _regrabs;
        public int SelectRefusals => _refusals;
        public AgentSelectInteractableMute InteractableMute => _mute;
        public HandOwnershipArbiter HandArbiter => _arb;
        int _carryHand = -1;
        int _aimHand = -1;
        Transform _proxy;
        Vector3 _proxyFrom;
        float _proxyT;
        float _proxyScaleCarry;
        Transform _perchPad;
        Renderer _perchPadR;
        WallCard _visitCard;
        Transform _portalRoot, _portalAvatar;
        float _portalAvatarScale;
        int _visitCounter;
        string _lastQuestionLabel;

        public AgentVisitFlow Visit => _visit;
        public PortalFlow Portal => _portal;
        public AgentPresentationRegistry Presentations => _registry;
        public SideConversationSession Side => _side;
        public WallCard VisitCard => _visitCard;

        // ── rc22 (owner, worn rc21): the agent lands where it is let go (DeskDropRule), and the grab keeps hold (CarryGrip) ──
        [Tooltip("rc22: where a released agent lands on the desk (under the release point, inside the usable desk, clear of the body and the dock).")]
        public DeskDropSettings deskDrop = new DeskDropSettings();
        readonly CarryGrip _grip = new CarryGrip();
        Vector3 _perchSpotLocal;
        bool _perchSpotSet;
        Vector3 _carryDir;
        Vector3 _padLift = new Vector3(0f, 0.002f, 0f);
        bool _otherBusyPrev;

        /// <summary>Where the perched agent stands (or will land): the spot it was let go on, else the fixed teal pad.
        /// The landing, the pad, the visit card, the talk bubble, the regrab and the return all use this.</summary>
        public Vector3 PerchWorld => nearUserRig != null ? nearUserRig.TransformPoint(_perchSpotSet ? _perchSpotLocal : perchLocal) : transform.position;
        /// <summary>The fixed teal pad (the default landing and the fallback for a release out of range).</summary>
        public Vector3 PerchPadWorld => nearUserRig != null ? nearUserRig.TransformPoint(perchLocal) : transform.position;
        /// <summary>True while the perched agent stands on a spot of the user's choosing (not the pad).</summary>
        public bool PerchOnUserSpot => _perchSpotSet;
        /// <summary>The last release's decision (for drivers and the log).</summary>
        public DeskDropResult LastDrop { get; private set; }
        /// <summary>How often armed Select switched itself off because the user did something else, and what it was.</summary>
        public int SelectAutoOffs { get; private set; }
        public string LastSelectAutoOff { get; private set; }
        /// <summary>For drivers: the carry's longer release / lost-hand grace is on the hand arbiter right now.</summary>
        public bool CarryGripApplied => _grip.Applied;

        // ───────────────────────── keys ─────────────────────────

        public void PressSelectAgents()
        {
            if (!flags.agentSelectEnabled || !WallActive) return;
            if (_visit.IsArmed) { _visit.Disarm(); Note("Select Agents off"); return; }
            if (_portal.State != PortalState.Hidden) { Note("Close the portal first"); return; }
            if (!_visit.Arm()) Note(_visit.Message); else Note(_visit.Message);
        }

        /// <summary>rc22: a key other than Select was pressed (raised before it acts): armed Select switches off.</summary>
        void OnOtherAction(string what)
        {
            if (_visit == null || !SelectModeExit.ShouldDisarm(_visit.IsArmed, what)) return;
            DisarmSelect(what);
        }

        /// <summary>Armed Select off because the user did something else. Only Armed: a pick in progress becomes a carry first
        /// (never disarmed), and a carry is never touched. No hand is claimed while armed; the hover lock and any pinch still
        /// debouncing toward an agent are dropped (silently: it was not refused, the mode ended).</summary>
        void DisarmSelect(string cause)
        {
            if (_visit == null || !_visit.IsArmed) return;
            _visit.Disarm();
            _lock.Reset();
            _pinchPending[0] = _pinchPending[1] = false;
            _hoverRole = -1;
            SelectAutoOffs++;
            LastSelectAutoOff = cause;
            Note(SelectModeExit.OffNote);
            Debug.Log("[MuseDesk.Wall] Select Agents off: the user did something else (" + cause + ")");
        }

        /// <summary>Something other than Select Agents is going on now (rising edges switch armed Select off).</summary>
        string OtherBusyNow()
        {
            if (HandInteractionProbe.AnyHeld) return "grab";
            if (_planCardOpen) return "plan paper";
            if (MeetChoosing) return "Meet chooser";
            if (ScreenOpen) return "screen panel";
            if (DraggedCard != null) return "card drag";
            return null;
        }

        void ReleaseHands()
        {
            if (_carryHand >= 0) { _arb.Relinquish(_carryHand); _carryHand = -1; }
            if (_aimHand >= 0) { _arb.Relinquish(_aimHand); _aimHand = -1; }
            _lock.Reset();
            _pinchPending[0] = _pinchPending[1] = false;
        }

        void RefuseSelect(string why)
        {
            if (string.IsNullOrEmpty(why)) return;
            _refusals++;
            LastSelectRefusal = why;
            Note(why);
            Debug.Log("[MuseDesk.Wall] Select Agents pinch refused: " + why);
        }

        // ───────────────────────── per frame ─────────────────────────

        void TickInteraction(float dt)
        {
            _visit.Tick(dt);
            TickCardDrag(dt);
            _portal.comfortMode = Comfort;
            _portal.Tick(dt);
            TickPresentations();
            TickMeet(dt);

            // rc22: armed Select switches off when the user starts something else (a grab, the plan paper, the Meet chooser,
            // the screen panel, a card drag). A rising edge: whatever was already going on when Select was armed does not count.
            string other = OtherBusyNow();
            bool otherBusy = other != null;
            if (SelectModeExit.EdgeTrips(_visit.IsArmed, _otherBusyPrev, otherBusy)) DisarmSelect(other);
            _otherBusyPrev = otherBusy;

            bool selectMode = WallActive && flags.agentSelectEnabled && (_visit.IsArmed || _visit.State == VisitState.Carrying);
            // V2: while an agent sits on the perch, a pinch ON it picks it up again (to move it, or to put it back on the wall).
            bool regrabMode = useSelectGrabV2 && WallActive && flags.agentSelectEnabled && _visit.State == VisitState.Perched
                              && _portal.State == PortalState.Hidden && _proxy != null;
            bool aimMode = WallPreviewing;
            TickInteractableMute(selectMode);
            // Asking the Manager by hand: point a free hand at the Manager in the habitat and pinch (same arbiter as Select
            // Agents, so a held paper, a grab or a poke always wins). It only opens the Manager paper.
            bool managerMode = managerRayPoke && WallActive && !aimMode && !selectMode && flags.wallManagerEnabled && !_planCardOpen
                               && !_visit.Busy && _portal.State == PortalState.Hidden && !MeetChoosing && _view != null;
            if (!managerMode && _view != null) _view.SetManagerHint(false);
            // A Return key (or any interruption) ended the carry while the pinch is still closed: give the hand back now.
            if (_carryHand >= 0 && _visit.State != VisitState.Carrying) { _arb.Relinquish(_carryHand); _carryHand = -1; }
            if (!selectMode && !regrabMode && !aimMode && !managerMode && _carryHand < 0 && _aimHand < 0)
            {
                _hoverRole = -1;
                _lock.Reset();
                _pinchPending[0] = _pinchPending[1] = false;
                TickWallAim(false, dt);
                TickSelectAim(false);
                TickProxy(dt);
                TickPortalVisual(dt);
                RefreshVisitCard();
                return;
            }

            _arb.externallyHeld = SpatialWorkspaceState.TransitionHeld;
            bool carrying = _carryHand >= 0 && _visit.State == VisitState.Carrying;
            for (int i = 0; i < 2; i++)
            {
                AimHandSample s = _simOn[i] ? _sim[i] : ReadHand(i);
                // Select agents points with the hand ray: a ray merely sweeping over a key/card on its way is not "over a key"
                // (rc17 worn false refusals). A fingertip poke hover or a real ray selection still blocks the pinch.
                if (!_simOn[i]) s = selectMode ? HandInteractionProbe.ApplyPointing(s, i) : HandInteractionProbe.Apply(s, i);
                // rc22 keep hold: while carrying, the OTHER hand picking something up does not take the agent away (the
                // arbiter cancels a claim on any hold). The carrying hand's own grab, and a paper's transition hold, still do.
                if (useSelectGrabV2 && carrying && i != _carryHand) s.holding = false;
                _samples[i] = s;
            }
            // rc22 keep hold: a longer seen-open release and a longer frozen lost hand while carrying (given back after).
            _grip.Apply(_arb.settings, useSelectGrabV2 && carrying, selectGrab);
            _arb.Tick(dt, _samples[0], _samples[1]);

            // Hover: which agent does a free hand's ray point at?
            _hoverRole = -1;
            int hoverHand = -1;
            bool picking = selectMode && _visit.IsArmed && _view != null;
            if (picking && useSelectGrabV2)
            {
                // Forgiving target + per-hand lock: the pinch takes what it BEGAN on (the ray dips as the fingers close).
                BuildAgentTargets();
                _lock.graceSeconds = selectGrab.lockGraceSeconds;
                float scale = _habitatRoot != null ? _habitatRoot.lossyScale.y : 1f;
                float radius = Mathf.Max(0.045f, _view.AvatarHeight * 0.45f) * scale + 0.02f;
                for (int i = 0; i < 2; i++)
                {
                    int raw = _samples[i].tracked && _carryHand < 0
                        ? AgentRayTargeting.Pick(_samples[i].origin, _samples[i].direction, _targetCenters, _targetEligible, radius, selectGrab.minTargetDegrees, selectGrab.minRayDistance)
                        : -1;
                    _lock.Update(i, raw, Time.unscaledTime);
                    if (_arb.Began(i)) { _lock.NoteBegan(i); _pinchPending[i] = _lock.PinchTarget(i) >= 0; }
                    if (_hoverRole < 0 && _lock.Locked(i) >= 0) { _hoverRole = _lock.Locked(i); hoverHand = i; }
                }
            }
            else if (picking)
            {
                for (int i = 0; i < 2 && _hoverRole < 0; i++)
                    if (_samples[i].tracked) { _hoverRole = RayPickAgent(_samples[i].origin, _samples[i].direction); if (_hoverRole >= 0) hoverHand = i; }
            }
            else _lock.Reset();
            if (regrabMode)
                for (int i = 0; i < 2; i++)
                    if (_arb.Began(i) && i != _carryHand) _pinchPending[i] = NearProxy(_samples[i]);
            if (regrabMode && _perchPadR != null)
            {
                bool near = (_samples[0].tracked && NearProxy(_samples[0])) || (_samples[1].tracked && NearProxy(_samples[1]));
                ArtHelper().Tint(_perchPadR, near ? Color.Lerp(WallHabitatView.Teal, Color.white, 0.45f) : WallHabitatView.Teal * 0.55f);
            }

            // A pinch that began on an agent (or on the perched projection) and was refused is never silent.
            for (int i = 0; i < 2; i++)
            {
                if (!_pinchPending[i] || _arb.Signal(i) == HandGateSignal.Ready) continue;
                if (_arb.IsSpent(i)) { RefuseSelect(SelectBlockWords.For(_arb.LastBlock(i))); _pinchPending[i] = false; _lock.ClearPinch(i); }
                else if (!_arb.IsPending(i)) { _pinchPending[i] = false; _lock.ClearPinch(i); }   // a short tap: it opened inside the debounce
            }

            if (managerMode)
            {
                bool hint = false;
                for (int i = 0; i < 2; i++)
                {
                    if (!_samples[i].tracked || !RayHitsRole(_samples[i].origin, _samples[i].direction, HabitatRole.Manager)) continue;
                    hint = true;
                    if (_arb.Signal(i) == HandGateSignal.Ready && Time.unscaledTime >= _managerPokeAt)
                    {
                        _managerPokeAt = Time.unscaledTime + 1.0f;
                        PressManager();
                        Note("Manager");
                        break;
                    }
                }
                _view.SetManagerHint(hint && !_planCardOpen);
            }

            for (int i = 0; i < 2; i++)
            {
                HandGateSignal sig = _arb.Signal(i);
                if (sig == HandGateSignal.Ready)
                {
                    int target = useSelectGrabV2 ? (picking ? _lock.PinchTarget(i) : -1) : (hoverHand == i ? _hoverRole : -1);
                    if (useSelectGrabV2) { _pinchPending[i] = false; _lock.ClearPinch(i); }
                    if (useSelectGrabV2 && target >= 0 && !_targetEligible[target]) target = -1;   // it left home since the pinch began
                    if (selectMode && _visit.IsArmed && target >= 0 && _carryHand < 0)
                    {
                        string agent = AgentId((HabitatRole)target);
                        if (useSelectGrabV2)
                        {
                            // Claim the hand first (one owner per hand, held until a seen-open release), then start the carry.
                            if (!_arb.TryClaim(i, HandClaim.AgentSelect)) RefuseSelect(SelectBlockWords.For(HandBlock.Preempted));
                            else if (!_visit.Grab(agent)) { _arb.Relinquish(i); RefuseSelect(_visit.Message); }
                            else { _carryHand = i; _grabs++; _lock.Forget(target); BeginCarry((HabitatRole)target); }
                        }
                        else if (_visit.Grab(agent) && _arb.TryClaim(i, HandClaim.AgentSelect)) { _carryHand = i; BeginCarry((HabitatRole)target); }
                        else { if (_visit.State == VisitState.Carrying) _visit.CancelCarry(VisitEndReason.Preempted); Note(_visit.Message); }
                    }
                    else if (regrabMode && _carryHand < 0 && NearProxy(_samples[i]))
                    {
                        if (!_arb.TryClaim(i, HandClaim.AgentSelect)) RefuseSelect(SelectBlockWords.For(HandBlock.Preempted));
                        else if (!_visit.Regrab()) _arb.Relinquish(i);
                        else { _carryHand = i; _regrabs++; BeginRegrab(); }
                    }
                    else if (aimMode && _aimHand < 0)
                    {
                        // Pinch while pointing at a wall: drag the ghost (placement still needs Place here).
                        int wi; float u, y, dist;
                        if (WallMath.Raycast(roomService != null ? roomService.Walls : null, _samples[i].origin, _samples[i].direction, 8f, out wi, out u, out y, out dist)
                            && _arb.TryClaim(i, HandClaim.Placement)) _aimHand = i;
                    }
                }
                if (i == _carryHand)
                {
                    if (sig == HandGateSignal.Released) { _carryHand = -1; EndCarry(true, i); }
                    else if (sig == HandGateSignal.Cancelled) { _carryHand = -1; EndCarry(false, i); }
                }
                if (i == _aimHand)
                {
                    if (sig == HandGateSignal.Holding && _samples[i].tracked) { _flow.Aim(_samples[i].origin, _samples[i].direction); ShowGhost(); }
                    else if (sig == HandGateSignal.Released || sig == HandGateSignal.Cancelled) { _aimHand = -1; if (_flow.ReleaseAim()) ShowGhost(); }
                }
            }
            if (!aimMode && _aimHand >= 0) { _arb.Relinquish(_aimHand); _aimHand = -1; }
            TickWallAim(aimMode, dt);
            TickSelectAim(picking && !aimMode && _carryHand < 0);

            TickProxy(dt);
            TickPortalVisual(dt);
            RefreshVisitCard();
        }

        float _managerPokeAt;

        /// <summary>Does this ray point at one habitat character (same forgiving target as Select Agents)?</summary>
        bool RayHitsRole(Vector3 origin, Vector3 dir, HabitatRole role)
        {
            Transform a = _view != null ? _view.Avatar(role) : null;
            if (a == null || _habitatRoot == null) return false;
            float scale = _habitatRoot.lossyScale.y;
            float r = Mathf.Max(0.045f, _view.AvatarHeight * 0.45f) * scale + 0.02f;
            Vector3 oc = a.position + _habitatRoot.up * (_view.AvatarHeight * 0.5f * scale) - origin;
            float t = Vector3.Dot(oc, dir);
            return t >= 0.2f && (oc - dir * t).sqrMagnitude <= r * r;
        }

        /// <summary>Play Mode driver: the same path a pinch on the Manager takes.</summary>
        public bool PokeManagerByRay(Vector3 origin, Vector3 direction)
        {
            if (!managerRayPoke || !WallActive || !flags.wallManagerEnabled || _planCardOpen) return false;
            if (!RayHitsRole(origin, direction.normalized, HabitatRole.Manager)) return false;
            PressManager();
            return _planCardOpen;
        }

        int RayPickAgent(Vector3 origin, Vector3 dir)
        {
            float best = float.MaxValue; int pick = -1;
            float scale = _habitatRoot != null ? _habitatRoot.lossyScale.y : 1f;
            float r = Mathf.Max(0.045f, _view.AvatarHeight * 0.45f) * scale + 0.02f;      // a forgiving target at 1-2 m
            for (int i = 0; i < 4; i++)
            {
                Transform a = _view.Avatar((HabitatRole)i);
                if (a == null || _registry.Of(AgentId((HabitatRole)i)) != AgentPresentation.Home) continue;
                Vector3 c = a.position + _habitatRoot.up * (_view.AvatarHeight * 0.5f * scale);
                Vector3 oc = c - origin;
                float t = Vector3.Dot(oc, dir);
                if (t < 0.2f) continue;
                float d2 = (oc - dir * t).sqrMagnitude;
                if (d2 > r * r) continue;
                if (t < best) { best = t; pick = i; }
            }
            return pick;
        }

        /// <summary>V2 targets: the chest of every habitat agent that is at home (one presentation per agent).</summary>
        void BuildAgentTargets()
        {
            float scale = _habitatRoot != null ? _habitatRoot.lossyScale.y : 1f;
            Vector3 up = _habitatRoot != null ? _habitatRoot.up : Vector3.up;
            for (int i = 0; i < 4; i++)
            {
                Transform a = _view != null ? _view.Avatar((HabitatRole)i) : null;
                bool ok = a != null && a.gameObject.activeInHierarchy && _registry.Of(AgentId((HabitatRole)i)) == AgentPresentation.Home;
                _targetEligible[i] = ok;
                _targetCenters[i] = ok ? a.position + up * (_view.AvatarHeight * 0.5f * scale) : Vector3.zero;
                if (!ok) _lock.Forget(i);
            }
        }

        /// <summary>The middle of the perched projection (its pivot is at its feet).</summary>
        Vector3 ProxyCenter => _proxy != null ? _proxy.position + _proxy.up * (0.5f * AvatarWorldHeight * (_proxy.localScale.x / Mathf.Max(1e-5f, _proxyScaleCarry))) : Vector3.zero;
        float AvatarWorldHeight => _view != null ? _view.AvatarHeight * (_habitatRoot != null ? _habitatRoot.lossyScale.y : 1f) : 0.17f;

        /// <summary>Is this hand on the perched projection: its pinch point within reach, or its ray on it from under 1 m?</summary>
        bool NearProxy(AimHandSample s)
        {
            if (!s.tracked || _proxy == null) return false;
            Vector3 c = ProxyCenter;
            if ((s.origin - c).sqrMagnitude <= regrabReach * regrabReach) return true;
            float r = Mathf.Max(0.06f, AvatarWorldHeight * 0.6f);
            return (c - s.origin).sqrMagnitude <= 1f && AgentRayTargeting.Hits(s.origin, s.direction, c, r, 0.02f);
        }

        void EnsureMute()
        {
            if (_mute != null) return;
            _mute = GetComponent<AgentSelectInteractableMute>();
            if (_mute == null) _mute = gameObject.AddComponent<AgentSelectInteractableMute>();
            _mute.owner = this;
        }

        /// <summary>Hidden office interactables off while Wall is active; distance grabs off while selecting/carrying.</summary>
        void TickInteractableMute(bool selecting)
        {
            bool office = useSelectGrabV2 && muteHiddenOfficeInteractables && WallActive && _suppressor.Active;
            bool distance = useSelectGrabV2 && muteDistanceGrabWhileSelecting && selecting;
            if (!office && !distance && (_mute == null || (!_mute.OfficeActive && !_mute.DistanceActive))) return;
            EnsureMute();
            _mute.SetOffice(office, officeRoot != null ? officeRoot : SpatialWorkspaceState.WorkspaceRoot, neverHide);
            _mute.SetDistance(distance);
        }

        // ───────────────────────── the carried projection ─────────────────────────

        Vector3 _proxyVel, _landFromPos;
        float _landFromScale;
        bool _landStarted;
        readonly MuseDesk.Motion.MotionClock _landClock = new MuseDesk.Motion.MotionClock();

        void BeginCarry(HabitatRole role)
        {
            DestroyProxy();
            Transform home = _view.Avatar(role);
            float h = _view.AvatarHeight * (_habitatRoot != null ? _habitatRoot.lossyScale.y : 1f);
            GameObject src = SourceFor(role);
            _proxy = src != null ? WallHabitatView.CloneVisual(src, "VisitProjection_" + role, transform, h)
                                 : WallHabitatView.CloneVisual(home != null ? home.gameObject : null, "VisitProjection_" + role, transform, h);
            if (_proxy == null) { _visit.CancelCarry(VisitEndReason.Cancelled); return; }
            AttachProxyLife(h);                                   // rc12: dangles while carried, lively on the perch
            _proxy.position = home != null ? home.position : PerchWorld;
            _proxy.rotation = home != null ? home.rotation : Quaternion.identity;
            _proxyScaleCarry = _proxy.localScale.x;
            _proxyFrom = _proxy.position;
            _proxyT = 0f;
            _proxyVel = Vector3.zero;
            _carryDir = Vector3.zero;
            _perchSpotSet = false;                                // a fresh visit: the pad until it is let go somewhere
            _landStarted = false;
            // A soft ring marks the projection as a projection.
            float ringK = Mathf.Max(1e-5f, _proxy.lossyScale.y / Mathf.Max(1e-5f, transform.lossyScale.y));
            if (WallUi.AccentMaterial != null)
            {
                // rc12: a round marker (it was a square box), flat under the feet.
                float d = 0.09f / ringK;
                WallUi.Plate(_proxy, "ProjectionRing", Vector3.up * (0.002f / ringK), d, d, d * 0.5f, WallHabitatView.Teal, WallUi.AccentMaterial, ArtHelper())
                    .transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            }
            else ArtHelper().Box(_proxy, "ProjectionRing", Vector3.zero, Vector3.one, WallHabitatView.Teal).transform.localScale = new Vector3(0.09f, 0.002f, 0.09f) / ringK;
            EnsurePerch();
            _side = null;
            Note(!useSelectGrabV2 ? "Let go over the teal pad on your desk"
                 : deskDrop != null && deskDrop.enabled ? "Bring it over your desk and let go: it stands where you put it"
                 : "Pull it toward you and let go: it lands on the teal pad");
        }

        GameObject SourceFor(HabitatRole role)
        {
            // Prefer the habitat avatar: a clean visual clone whose renderers are on. (The desk agents' renderers
            // are switched off while Wall mode hides the desk office, and a clone would copy that.)
            if (_view != null && _view.Avatar(role) != null) return _view.Avatar(role).gameObject;
            if (role == HabitatRole.Manager && managerSource != null) return managerSource;
            if (agentSources == null || agentSources.Length == 0) return null;
            int i = role == HabitatRole.Manager ? 1 : Mathf.Min((int)role, agentSources.Length - 1);
            return agentSources[i];
        }

        void TintProxy(Transform t, Color c)
        {
            var mpb = new MaterialPropertyBlock();
            foreach (var r in t.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                r.GetPropertyBlock(mpb);
                mpb.SetColor("_BaseColor", Color.Lerp(Color.white, c, 0.45f));
                r.SetPropertyBlock(mpb);
            }
        }

        void EndCarry(bool released, int hand = -1)
        {
            if (_visit.State != VisitState.Carrying) return;
            if (!useSelectGrabV2)
            {
                if (!released) { _visit.CancelCarry(VisitEndReason.TrackingLost); Note("Hand lost: the agent went home. Nothing changed."); return; }
                bool over = _proxy != null && (_proxy.position - PerchWorld).magnitude <= perchRadius;
                _visit.Release(over);
                if (over) BeginDeskVisit();
                else Note("Not over the desk pad: going home");
                return;
            }

            if (!released)
            {
                // Taken away (tracking lost, something picked up, preempted): never a placement. A fresh carry goes home;
                // one picked up off the perch goes back onto the perch it came from.
                HandBlock why = hand >= 0 ? _arb.LastBlock(hand) : HandBlock.TrackingLost;
                _visit.CancelCarry(why == HandBlock.TrackingLost ? VisitEndReason.TrackingLost : VisitEndReason.Preempted);
                if (_visit.State == VisitState.Perched) _landStarted = false;
                Note(_visit.Message + ". Nothing changed.");
                return;
            }
            // rc22: it lands where it was let go (DeskDropRule); out of range = the previous rule (the pad, or home).
            DeskDropResult r = _proxy != null ? DecideDrop(_proxy.position) : new DeskDropResult { kind = DeskDropKind.Home, spot = perchLocal, why = "no projection" };
            LastDrop = r;
            if (r.kind != DeskDropKind.Home)
            {
                _perchSpotSet = r.kind == DeskDropKind.Spot;
                _perchSpotLocal = r.spot;
            }
            Debug.Log("[MuseDesk.Wall] Select Agents release: " + r.kind + " at rig " + r.spot.ToString("F3") + (r.adjusted ? " (pulled onto the desk)" : "")
                      + (r.why != null ? " because " + r.why : ""));
            _visit.Release(r.kind != DeskDropKind.Home ? VisitDrop.Perch : VisitDrop.Home);
            if (_visit.State == VisitState.Perched)
            {
                _landStarted = false;                       // land (again) onto the perch from where it was let go
                if (_side == null) BeginDeskVisit();        // a re-perched projection keeps its conversation
            }
            else Note(_visit.Message);
        }

        Vector3 EyeWorld()
        {
            Transform h = ResolveHead();
            return h != null ? h.position : (nearUserRig != null ? nearUserRig.TransformPoint(new Vector3(0f, 0.30f, 0.62f)) : PerchPadWorld);
        }

        /// <summary>
        /// rc22: where a projection let go at <paramref name="releaseWorld"/> lands. The previous rule (<see cref="VisitDropRule"/>,
        /// world space, around the fixed pad) is the fallback; <see cref="DeskDropRule"/> places it on the desk under the release
        /// point when that is a desk placement (rig space: within reach, nearer the user than its wall home, over the usable desk,
        /// clear of the body keep-out and the dock). Without the rig: the previous rule only.
        /// </summary>
        DeskDropResult DecideDrop(Vector3 releaseWorld)
        {
            Vector3 eye = EyeWorld();
            Vector3 home = HomeAnchorOf(_visit.Agent);
            VisitDrop legacy = VisitDropRule.Decide(releaseWorld, PerchPadWorld, eye, home, visitDrop);
            if (nearUserRig == null)
                return new DeskDropResult { kind = legacy == VisitDrop.Perch ? DeskDropKind.Pad : DeskDropKind.Home, spot = perchLocal, why = "no desk rig" };
            return DeskDropRule.Decide(nearUserRig.InverseTransformPoint(releaseWorld), perchLocal, nearUserRig.InverseTransformPoint(eye),
                                       nearUserRig.InverseTransformPoint(home), true, dockLocal, legacy, deskDrop);
        }

        /// <summary>The visit card's usual spot (rig space) for the agent where it stands.</summary>
        Vector3 VisitCardUsualLocal => _perchSpotSet ? DeskDropRule.CardFor(_perchSpotLocal, perchLocal, visitCardLocal) : visitCardLocal;

        /// <summary>The pad marks where the agent stands, or (while carrying) where letting go now would land it.</summary>
        void PlacePad(Vector3 spotLocal)
        {
            if (_perchPad == null) return;
            Vector3 want = spotLocal + _padLift;
            if ((_perchPad.localPosition - want).sqrMagnitude > 1e-8f) _perchPad.localPosition = want;
        }

        /// <summary>Picked up off the perch: follow the hand from where it stands (no fly-in), keep its size.</summary>
        void BeginRegrab()
        {
            _proxyT = 1f;
            _proxyVel = Vector3.zero;
            _carryDir = Vector3.zero;
            _landStarted = false;
            if (_audio != null) _audio.Play(WallCue.Land, true);
            Note(_visit.Message);
        }

        void TickProxy(float dt)
        {
            bool showPad = _visit.State == VisitState.Carrying || _visit.State == VisitState.Perched;
            if (_perchPad != null && _perchPad.gameObject.activeSelf != showPad) _perchPad.gameObject.SetActive(showPad);
            if (_visit.State == VisitState.Off) _perchSpotSet = false;      // rc22: no visit, no chosen spot (the next starts at the pad)
            if (_visit.State == VisitState.Perched) PlacePad(_perchSpotSet ? _perchSpotLocal : perchLocal);
            if (_proxy == null) return;
            if (_proxyLife != null)
            {
                bool perched = _visit.State == VisitState.Perched && _landStarted && !_landClock.Running;
                _proxyLife.SetCarried(!perched);
                if (perched) TickLifeConversation(_proxyLife, ref _proxyHeard, ref _proxyAsking);
            }
            switch (_visit.State)
            {
                case VisitState.Carrying:
                {
                    if (_carryHand < 0) return;
                    AimHandSample s = _samples[_carryHand];
                    // Frozen (hand lost, inside the grace) or opening the fingers (inside the release grace): hold still, and
                    // forget the follow velocity so a pinch that comes back resumes smoothly instead of flinging.
                    if (!s.tracked || !s.pinching) { _proxyVel = Vector3.zero; return; }
                    _proxyT = Mathf.Min(1f, _proxyT + dt / 0.45f);
                    // rc22: a smoothed ray direction and a fixed distance along it: pointer-pose jitter does not shake the agent.
                    Vector3 rawDir = s.direction.sqrMagnitude > 1e-8f ? s.direction.normalized : Vector3.forward;
                    if (_carryDir.sqrMagnitude < 1e-8f) _carryDir = rawDir;
                    else _carryDir = Vector3.Slerp(_carryDir, rawDir, 1f - Mathf.Exp(-dt / Mathf.Max(1e-3f, selectGrab.carryDirSmoothSeconds))).normalized;
                    Vector3 target = s.origin + _carryDir * selectGrab.carryDistance + Vector3.down * 0.02f;
                    float follow = Mathf.Max(0.01f, selectGrab.carryFollowSeconds);
                    if (_proxyT < 1f)
                    {
                        // Fly from the habitat to the hand on a fixed-endpoint ease (starts where the agent stands).
                        float e = MuseDesk.Motion.Ease.Evaluate(MuseDesk.Motion.EaseKind.InOutCubic, _proxyT);
                        _proxy.position = Vector3.Lerp(_proxyFrom, target, e);
                        _proxyVel = Vector3.zero;
                    }
                    else
                    {
                        // Then follow the hand with a short critically damped lag: hand-tracking jitter is absorbed,
                        // deliberate motion is not delayed noticeably (~70 ms).
                        Vector3 p = _proxy.position;
                        p.x = MuseDesk.Motion.Damped.Follow(p.x, target.x, ref _proxyVel.x, follow, dt, 1e-4f);
                        p.y = MuseDesk.Motion.Damped.Follow(p.y, target.y, ref _proxyVel.y, follow, dt, 1e-4f);
                        p.z = MuseDesk.Motion.Damped.Follow(p.z, target.z, ref _proxyVel.z, follow, dt, 1e-4f);
                        _proxy.position = p;
                    }
                    Vector3 eye = ResolveHead() != null ? ResolveHead().position : _proxy.position + Vector3.back;
                    _proxy.rotation = WallUi.FacingEye(eye, _proxy.position) ;   // +Z toward the user
                    // The pad shows where letting go now would land it, lit when it would stay (V2: the same rule the release uses).
                    bool over;
                    if (useSelectGrabV2)
                    {
                        DeskDropResult pr = DecideDrop(_proxy.position);
                        over = pr.kind != DeskDropKind.Home;
                        PlacePad(over ? pr.spot : (_visit.IsRegrab && _perchSpotSet ? _perchSpotLocal : perchLocal));
                    }
                    else over = (_proxy.position - PerchWorld).magnitude <= perchRadius;
                    if (_perchPadR != null) ArtHelper().Tint(_perchPadR, over ? WallHabitatView.Teal : WallHabitatView.Teal * 0.55f);
                    return;
                }
                case VisitState.Perched:
                {
                    Vector3 eye = ResolveHead() != null ? ResolveHead().position : PerchWorld + Vector3.back;
                    float s = _proxyScaleCarry * 1.35f;
                    if (!_landStarted)
                    {
                        // Land: a timed ease from where it was let go onto the pad (visitLand), ending EXACTLY on it.
                        _landStarted = true;
                        _landFromPos = _proxy.position;
                        _landFromScale = _proxy.localScale.x;
                        _landClock.Start(MuseDesk.Motion.MuseMotionProfile.Active.visitLand);
                        if (_audio != null) _audio.Play(WallCue.Land, true);
                    }
                    _landClock.Step(dt);
                    float u = _landClock.Value;
                    _proxy.position = Vector3.LerpUnclamped(_landFromPos, PerchWorld, u);
                    _proxy.localScale = Vector3.one * Mathf.LerpUnclamped(_landFromScale, s, u);
                    _proxy.rotation = WallUi.FacingEye(eye, PerchWorld);
                    if (_proxyLife != null)
                    {
                        _proxyLife.height = _view.AvatarHeight * (_habitatRoot != null ? _habitatRoot.lossyScale.y : 1f) * 1.35f;
                        // Landed: a hello wave, then it perches and listens (SetCarried(false) above starts its loop).
                        if (!_landClock.Running && !_proxyGreeted) { _proxyGreeted = true; _proxyLife.SetCarried(false); _proxyLife.Arrive(); }
                    }
                    return;
                }
                case VisitState.Returning:
                {
                    Transform home = _view != null && _visit.Agent != null ? _view.Avatar(RoleOf(_visit.Agent)) : null;
                    if (_proxyT >= 0f) { _proxyFrom = _proxy.position; _returnFromScale = _proxy.localScale.x; _proxyT = -1f; }
                    float k = _visit.ReturnProgress;
                    Vector3 to = home != null ? home.position : _proxyFrom;
                    _proxy.position = Vector3.Lerp(_proxyFrom, to, k * k * (3f - 2f * k));
                    // From the size it had when it left (a perched projection is 1.35x), so it never pops.
                    float from = useSelectGrabV2 && _returnFromScale > 0f ? _returnFromScale : _proxyScaleCarry;
                    _proxy.localScale = Vector3.one * Mathf.Lerp(from, _proxyScaleCarry * 0.2f, k);
                    return;
                }
                default:
                    DestroyProxy();
                    EndDeskVisit();
                    return;
            }
        }

        bool _proxyGreeted;

        /// <summary>
        /// Owner rc13: no second copy. While an agent's projection is out (carried / on the desk perch / flying home, or
        /// the portal visitor), its habitat avatar is not drawn; it reappears the moment the projection is gone.
        /// </summary>
        void TickAwayHiding()
        {
            if (_view == null) return;
            int visitRole = _proxy != null && _visit.Agent != null ? (int)RoleOf(_visit.Agent) : -1;
            int portalRole = _portalAvatar != null && _portal.Agent != null ? (int)RoleOf(_portal.Agent) : -1;
            for (int r = 0; r < 4; r++)
            {
                bool hide = r == visitRole || r == portalRole;
                // Owner (worn rc17): after Send back the agent "just pops back in to their spot": it now teleports in at its
                // desk (a short light column, and it grows out of it).
                if (!hide && _view.IsHidden((HabitatRole)r)) { _view.PlayArrive((HabitatRole)r); StartDeskBeam((HabitatRole)r); }
                _view.SetHidden((HabitatRole)r, hide);
            }
            TickDeskBeams(Time.deltaTime);
        }

        // ── Teleport-in beam at an agent's wall desk (reuses the portal column's mesh and glow material) ──
        readonly Transform[] _deskBeam = new Transform[4];
        readonly float[] _deskBeamT = { 99f, 99f, 99f, 99f };
        Material _deskBeamMat;
        /// <summary>For drivers: how many teleport-in beams have started at the wall desks.</summary>
        public int DeskBeamsStarted { get; private set; }
        /// <summary>For drivers: a teleport-in beam is showing right now.</summary>
        public bool DeskBeamShowing { get { for (int i = 0; i < 4; i++) if (_deskBeam[i] != null && _deskBeam[i].gameObject.activeSelf) return true; return false; } }
        static readonly int DeskBeamColorId = Shader.PropertyToID("_BaseColor");
        static readonly int DeskBeamColorId2 = Shader.PropertyToID("_Color");

        void StartDeskBeam(HabitatRole role)
        {
            int r = (int)role;
            Transform a = _view != null ? _view.Avatar(role) : null;
            if (a == null) return;
            EnsurePortalFx();
            if (_deskBeamMat == null) _deskBeamMat = _portalColumnMat != null ? new Material(_portalColumnMat) { name = "DeskBeam_Glow" } : null;
            if (_deskBeamMat == null) return;
            if (_deskBeam[r] == null)
            {
                var go = new GameObject("DeskBeam_" + role);
                go.AddComponent<MeshFilter>().sharedMesh = ColumnMesh(20);
                var mr = go.AddComponent<MeshRenderer>();
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mr.receiveShadows = false;
                mr.sharedMaterial = _deskBeamMat;
                _deskBeam[r] = go.transform;
            }
            _deskBeam[r].SetParent(a.parent, false);
            _deskBeamT[r] = 0f;
            DeskBeamsStarted++;
            _deskBeam[r].gameObject.SetActive(true);
            if (_audio != null) _audio.Play(WallCue.PrintTick);
        }

        void TickDeskBeams(float dt)
        {
            for (int r = 0; r < 4; r++)
            {
                Transform b = _deskBeam[r];
                if (b == null || !b.gameObject.activeSelf) continue;
                _deskBeamT[r] += dt;
                float k = _deskBeamT[r] / (WallHabitatView.ArriveSeconds + 0.35f);
                Transform a = _view != null ? _view.Avatar((HabitatRole)r) : null;
                if (k >= 1f || a == null) { b.gameObject.SetActive(false); continue; }
                float h = _view.AvatarHeight;
                float rad = h * 0.36f;
                b.localPosition = a.localPosition + Vector3.up * 0.002f;
                b.localRotation = Quaternion.identity;
                // Up fast, hold while the agent forms, then fade.
                float rise = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(k / 0.22f));
                b.localScale = new Vector3(rad, h * 1.35f * rise, rad);
                float alpha = k < 0.55f ? 1f : Mathf.SmoothStep(1f, 0f, (k - 0.55f) / 0.45f);
                Color c = Color.Lerp(WallHabitatView.Teal, Color.white, 0.35f); c.a = alpha * 0.85f;
                if (_deskBeamMat.HasProperty(DeskBeamColorId)) _deskBeamMat.SetColor(DeskBeamColorId, c);
                if (_deskBeamMat.HasProperty(DeskBeamColorId2)) _deskBeamMat.SetColor(DeskBeamColorId2, c);
            }
        }

        /// <summary>For drivers: whether the habitat avatar of this role is hidden while its projection is out.</summary>
        public bool AvatarHidden(HabitatRole r) { return _view != null && _view.IsHidden(r); }

        void DestroyProxy()
        {
            if (_proxy != null) Destroy(_proxy.gameObject);
            _proxy = null;
            _proxyLife = null;
            _proxyGreeted = false;
        }

        void EnsurePerch()
        {
            if (_perchPad != null || nearUserRig == null) return;
            if (WallUi.AccentMaterial != null)
            {
                // rc12: a round unlit pad (the cards' teal), lying flat: the plate faces -Z, turned to face up.
                _perchPadR = WallUi.Plate(nearUserRig, "WallVisitPerch", perchLocal + Vector3.up * 0.002f, 0.11f, 0.11f, 0.055f, WallHabitatView.Teal * 0.55f, WallUi.AccentMaterial, ArtHelper());
                _perchPadR.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            }
            else _perchPadR = ArtHelper().Box(nearUserRig, "WallVisitPerch", perchLocal, new Vector3(0.11f, 0.004f, 0.11f), WallHabitatView.Teal * 0.55f);
            _perchPad = _perchPadR.transform;
            _padLift = _perchPad.localPosition - perchLocal;      // rc22: the pad moves with the landing spot, same lift
            _perchPad.gameObject.SetActive(false);
        }

        static HabitatRole RoleOf(string agent)
        {
            HabitatRole r;
            return HabitatStage.TryRole(agent, out r) ? r : HabitatRole.Developer;
        }

        // ───────────────────────── desk visit + /btw ─────────────────────────

        void BeginDeskVisit()
        {
            _visitCounter++;
            _side = new SideConversationSession(_visit.Agent, "visit-" + DateTime.UtcNow.Ticks.ToString("x") + "-" + _visitCounter);
            EnsureVisitCard();
            PlaceCards();
            _visitCard.Show(true);
            Note("");
        }

        void EndDeskVisit()
        {
            if (_side != null && _backend != null) _backend.ClearVisit(_side.agentId, _side.visitSessionId);
            _side = null;
            if (_visitCard != null && _portal.State == PortalState.Hidden) _visitCard.Show(false);
        }

        void EnsureVisitCard()
        {
            if (_visitCard != null || nearUserRig == null) return;
            _visitCard = new WallCard(nearUserRig, "WallVisitCard", keyTemplate, font, ArtHelper(), new Vector2(0.16f, 0.13f), keyScale);
        }

        void PlaceCards()
        {
            Transform h = ResolveHead();
            Vector3 eye = h != null ? h.position : (nearUserRig != null ? nearUserRig.TransformPoint(new Vector3(0, 0.30f, 0.62f)) : Vector3.zero);
            // rc22: the card stands beside the agent where it was put down (unless the user moved the card).
            if (_visitCard != null && nearUserRig != null) _visitCard.Place(CardSpot(_visitCard, nearUserRig.TransformPoint(VisitCardUsualLocal)), eye, 12f);
            // The plan paper is taller than the old plan card: it grows UP from the old card's bottom edge, so it stays clear of
            // the stamps and the desk things the old card was placed above.
            if (_planCard != null && nearUserRig != null)
                _planCard.Place(CardSpot(_planCard, nearUserRig.TransformPoint(planCardLocal + Vector3.up * Mathf.Max(0f, (planPaperSize.y - 0.15f) * 0.5f))), eye, 12f);
        }

        static readonly string[] Questions = { "What are you working on?", "Is anything blocking you?", "What happens next?" };
        static readonly string[] QuestionKeys = { "Status?", "Blockers?", "Next?" };

        /// <summary>Ask the visiting (or portal) agent one of the preset questions. Read-only.</summary>
        public bool Ask(int questionIndex)
        {
            if (!flags.agentBtwEnabled) { Note("Questions are switched off in this build"); return false; }
            if (_side == null || _backend == null) return false;
            string q = Questions[Mathf.Clamp(questionIndex, 0, Questions.Length - 1)];
            return AskText(q);
        }

        /// <summary>Ask a free question (voice or a driver). Read-only: a change request comes back as a suggestion, never applied.</summary>
        public bool AskText(string question)
        {
            if (!flags.agentBtwEnabled || _side == null || _backend == null) return false;
            string taskId = null, title, state; int rev = -1;
            if (_tasks != null) _tasks.TryGetAgentTask(_side.agentId, out taskId, out title, out state, out rev);
            _side.taskId = taskId;
            string rid, key;
            if (!_side.TryBegin(question, Time.unscaledTime, out rid, out key)) return false;
            _lastQuestionLabel = question;
            SideConversationSession session = _side;
            _backend.AskSide(_side.agentId, taskId, _side.visitSessionId, question, _side.contextRevision >= 0 ? _side.contextRevision : rev, rid, key,
                reply =>
                {
                    if (session != _side || !session.Complete(reply)) return;
                    // rc20 (owner, worn rc19): the agent says its answer, not only the card.
                    if (reply.status == "answered" || reply.status == "amendment_suggested")
                        MuseDesk.Voice.AgentSpeech.Say(session.agentId, reply.reply, SpeechSpot());
                });
            return true;
        }

        /// <summary>Where a visiting agent's voice comes from: its card (beside the agent), else in front of the user.</summary>
        Vector3 SpeechSpot()
        {
            if (_visitCard != null && _visitCard.Visible) return _visitCard.root.position;
            Transform h = ResolveHead();
            return h != null ? h.position + h.forward * 0.6f : Vector3.zero;
        }

        public void PressReturn()
        {
            MuseDesk.Voice.AgentSpeech.StopAll();
            if (_portal.State != PortalState.Hidden) { _portal.SendBack(); return; }
            _visit.Return(VisitEndReason.UserReturn);
        }

        void RefreshVisitCard()
        {
            if (_side != null) _side.CheckTimeout(Time.unscaledTime);
            // rc12: the card stays up while the portal opens and the agent steps out (it used to vanish and come back).
            bool visiting = _visit.State == VisitState.Perched || (_portal.State >= PortalState.Preview && _portal.State <= PortalState.Conversing);
            if (_visitCard == null) return;
            if (!visiting) { if (_visitCard.Visible) _visitCard.Show(false); return; }
            if (!_visitCard.Visible) { PlaceCards(); _visitCard.Show(true); }

            string agent = _portal.State != PortalState.Hidden ? _portal.Agent : _visit.Agent;
            string name = AgentPresentationRegistry.Pretty(agent);
            var keys = new List<KeyValuePair<string, Action>>();
            string body;
            if (_portal.State == PortalState.Preview || _portal.State == PortalState.AwaitConfirm)
            {
                body = _portal.Message + (_portal.Spot.roomChecked ? "\nChecked against your room scan." : "");
                if (PortalSpotIsFallback) body += "\n" + PortalSpotAdapter.NotEnoughSpace + " for a large visitor; a small one can still come.";
                keys.Add(new KeyValuePair<string, Action>(_portal.State == PortalState.AwaitConfirm ? "Open here" : null, () => _portal.Confirm()));
                keys.Add(new KeyValuePair<string, Action>("Other spot", NextPortalSpot));
                keys.Add(new KeyValuePair<string, Action>("Cancel", () => _portal.Interrupt("Cancelled")));
                _visitCard.SetText("Meet " + name + " larger?", body, "The agent's work continues either way.");
                _visitCard.SetKeys(keys);
                return;
            }

            string taskId, title, state; int rev;
            string task = _tasks != null && _tasks.TryGetAgentTask(agent, out taskId, out title, out state, out rev)
                ? "Task: " + title + " (" + state + ", rev " + rev + ")" : "No task right now";
            body = task + "\n";
            if (_side != null && _side.State == SideAskState.Asking) body += "\nYou: " + _lastQuestionLabel + "\n…asking";
            else if (_side != null && _side.History.Count > 0)
            {
                SideExchange x = _side.History[_side.History.Count - 1];
                body += "\nYou: " + x.question + "\n" + name + ": " + x.reply;
                if (x.staleContext) body += "\n(Task changed since; this is the current state.)";
                if (!string.IsNullOrEmpty(x.amendment))
                {
                    if (_amendDoneFor == x.requestId) body += "\n" + _amendNote;
                    else if (_amendBusyFor == x.requestId) body += "\nAdding the change as a new step…";
                    else body += CanApplyAmendment() ? "\nSuggested change NOT applied yet. Press Apply change to add it as a new step."
                                                     : "\nSuggested change NOT applied: there is no approved plan to add it to.";
                }
            }
            else body += flags.agentBtwEnabled ? "\nAsk a question. This never changes the task." : "";
            // rc12: one short line (it wrapped into the keys), and the mode only once ("SCRIPTED DEMO · SCRIPTED DEMO").
            string said = _side != null && _side.History.Count > 0 ? (!string.IsNullOrEmpty(_side.History[_side.History.Count - 1].label) ? _side.History[_side.History.Count - 1].label : ProvenanceWords(_side.History[_side.History.Count - 1].provenance)) : "";
            string modeLabel = _tasks != null ? _tasks.SourceModeLabel : "";
            if (modeLabel == "LIVE") modeLabel = "";            // rc20 (owner): no LIVE tag; the demo stays labelled
            string lead = string.IsNullOrEmpty(said) || said == modeLabel ? modeLabel
                        : string.IsNullOrEmpty(modeLabel) ? said : said + " · " + modeLabel;
            string foot = string.IsNullOrEmpty(lead) ? "The real agent keeps working" : lead + " · the real agent keeps working";
            _visitCard.SetText(name + (_portal.State != PortalState.Hidden ? " is here" : " visiting"), body, foot);
            // A suggested change that can be applied takes the third question's place: "Apply change" (the one key that
            // changes anything from a visit, and only after this explicit press).
            bool applyKey = PendingAmendment() != null && CanApplyAmendment();
            if (flags.agentBtwEnabled)
                for (int i = 0; i < 3; i++)
                {
                    int qi = i;
                    if (i == 2 && applyKey) keys.Add(new KeyValuePair<string, Action>("Apply change", () => ApplyAmendment()));
                    else keys.Add(new KeyValuePair<string, Action>(QuestionKeys[i], () => Ask(qi)));
                }
            if (_portal.State == PortalState.Hidden && flags.largeAgentPortalEnabled) keys.Add(new KeyValuePair<string, Action>("Meet larger", PressMeetLarger));
            else keys.Add(new KeyValuePair<string, Action>(null, null));
            keys.Add(new KeyValuePair<string, Action>(_portal.State != PortalState.Hidden ? "Send back" : "Return", PressReturn));
            _visitCard.SetKeys(keys);
        }

        // ───────────────────────── a suggested change → a new step (explicit Apply only) ─────────────────────────

        string _amendBusyFor, _amendDoneFor, _amendNote;
        public int AmendmentsApplied { get; private set; }

        /// <summary>The latest exchange's suggested change, when it has not been applied or sent yet.</summary>
        SideExchange? PendingAmendment()
        {
            if (_side == null || _side.History.Count == 0) return null;
            SideExchange x = _side.History[_side.History.Count - 1];
            if (string.IsNullOrEmpty(x.amendment) || x.requestId == _amendDoneFor || x.requestId == _amendBusyFor) return null;
            return x;
        }

        bool CanApplyAmendment()
        {
            return _backend != null && _manager != null && !string.IsNullOrEmpty(_manager.CommittedPlanId)
                   && (_manager.State == ManagerFlowState.Delegating || _manager.State == ManagerFlowState.Monitoring);
        }

        /// <summary>The visit card's Apply change key: the agent's suggested change becomes ONE new step for that agent on the
        /// approved plan, after its current card. The backend creates it; it prints and is worked like every other card.</summary>
        public bool ApplyAmendment()
        {
            SideExchange? pending = PendingAmendment();
            if (pending == null || !CanApplyAmendment()) { Note("There is no suggested change to apply"); return false; }
            SideExchange x = pending.Value;
            string agent = _side.agentId;
            string afterTask = null, title, state; int rev;
            if (_tasks != null) _tasks.TryGetAgentTask(agent, out afterTask, out title, out state, out rev);
            string planId = _manager.CommittedPlanId, requestId = x.requestId;
            string key = "amend-" + requestId;
            _amendBusyFor = requestId;
            SendAmend(planId, _manager.Revision, x.amendment, agent, afterTask, key, requestId, true);
            return true;
        }

        void SendAmend(string planId, int revision, string summary, string agent, string afterTask, string key, string requestId, bool mayRetry)
        {
            _backend.AmendPlan(planId, revision, summary, agent, afterTask, key, r =>
            {
                if (r != null && r.ok)
                {
                    _manager.OnAmended(planId, r.revision, r.tasks);
                    _amendBusyFor = null; _amendDoneFor = requestId; AmendmentsApplied++;
                    _amendNote = "Change added as a new step for " + AgentPresentationRegistry.Pretty(agent) + (cardTubes ? ". It prints at the Manager's desk and comes down the tube." : ". It prints on the Manager's tray.");
                    return;
                }
                // The plan moved on (another change, a retry): read its revision once and try again with a new key.
                if (mayRetry && r != null && r.code == "REVISION_CONFLICT")
                {
                    _backend.GetPlanStatus(planId, s =>
                    {
                        if (s != null && s.revision > 0) SendAmend(planId, s.revision, summary, agent, afterTask, key + "-r" + s.revision, requestId, false);
                        else { _amendBusyFor = null; Note("The change was not added: the plan changed. Try again."); }
                    });
                    return;
                }
                _amendBusyFor = null;
                Note("The change was not added: " + (r == null ? "no reply" : (string.IsNullOrEmpty(r.message) ? r.code : r.message)));
            });
        }

        static string ProvenanceWords(string p)
        {
            switch (p)
            {
                case "LIVE_MODEL": return "Answer from the AI model";
                case "TASK_SNAPSHOT": return "From the task record (no AI model)";
                case "SCRIPTED_DEMO": return "SCRIPTED DEMO";
                case "OFFLINE_SNAPSHOT": return "Offline: from local task state";
                default: return string.IsNullOrEmpty(p) ? "" : p;
            }
        }

        // ───────────────────────── large portal ─────────────────────────

        public PortalSettings portalSettings = new PortalSettings();
        List<PortalCandidate> _portalSpots = new List<PortalCandidate>();
        int _portalSpotIndex;

        public void PressMeetLarger()
        {
            if (!flags.largeAgentPortalEnabled) { Note("The portal is switched off in this build"); return; }
            string agent;
            if (!_visit.HandOverToPortal(out agent)) { Note("Bring an agent to your desk first"); return; }
            // The desk projection leaves without flying home: the portal takes the same presentation.
            DestroyProxy();
            BeginPortalPreview(agent, _side);
        }

        /// <summary>
        /// The one way into the portal preview (Meet larger on the visit card, the dock's Meet key and its chooser, voice).
        /// Plans the spots (A9 PortalSpotPlanner; the small-visitor fallback "Not enough space here" when no large spot is
        /// clear), reserves the agent through <see cref="PortalFlow.BeginPreview"/> (one presentation per agent, one away at a
        /// time) and shows the ring. The conversation of a desk visit that handed over is kept; otherwise a fresh read-only
        /// side session starts, so the portal card can ask its questions. Nothing opens until the user presses Open here.
        /// </summary>
        bool BeginPortalPreview(string agent, SideConversationSession keep)
        {
            _portalSpotIndex = 0;
            PortalCandidate spot = PlanPortalSpots(ReadUser());      // A9: side spots via PortalSpotPlanner (legacy evaluator when off / non-rectangular room)
            if (!_portal.BeginPreview(agent, spot)) { Note(_portal.Message); return false; }
            // Meet starts with no desk visit: the portal's card may not exist yet. Place it now (not a frame later), because the
            // keep-out guard keeps the avatar clear of the card and of the line of sight to it.
            EnsureVisitCard();
            if (_visitCard != null && !_visitCard.Visible && nearUserRig != null)
            {
                Transform hd = ResolveHead();
                _visitCard.Place(nearUserRig.TransformPoint(visitCardLocal), hd != null ? hd.position : nearUserRig.TransformPoint(new Vector3(0f, 0.30f, 0.62f)), 12f);
            }
            if (keep != null && keep.agentId == agent) _side = keep;
            else
            {
                _visitCounter++;
                _side = new SideConversationSession(agent, "visit-" + DateTime.UtcNow.Ticks.ToString("x") + "-" + _visitCounter);
            }
            ShowPortalPreview();
            return true;
        }

        void NextPortalSpot()
        {
            _portalSpotManual = true;                                // the user chose: the tracker no longer moves the ring
            if (_portalSpots.Count == 0) { _portal.UpdateSpot(PortalPlacementEvaluator.ManualSmall(ReadUser(), _portalSpotIndex++ % 2 == 0 ? -70f : 70f, portalSettings)); }
            else { _portalSpotIndex = (_portalSpotIndex + 1) % _portalSpots.Count; _portal.UpdateSpot(_portalSpots[_portalSpotIndex]); }
            ShowPortalPreview();
        }

        void ShowPortalPreview()
        {
            if (_portalRoot == null)
            {
                _portalRoot = new GameObject("LargeAgentPortal").transform;
                _portalRoot.SetParent(transform, false);
                var art = ArtHelper();
                for (int i = 0; i < 12; i++)
                {
                    float a = i * Mathf.PI * 2f / 12f;
                    var seg = art.Box(_portalRoot, "Ring" + i, new Vector3(Mathf.Cos(a), 0.003f, Mathf.Sin(a)), new Vector3(0.16f, 0.006f, 0.02f), WallHabitatView.Lavender).transform;
                    seg.localRotation = Quaternion.Euler(0, -a * Mathf.Rad2Deg + 90f, 0);
                }
                // rc12: one smooth flat ring (unlit, the cards' accent material) instead of twelve boxes. Made after the twelve
                // segments (they keep their child indices 0-11, hidden), so the code that walks them is unchanged.
                if (WallUi.AccentMaterial != null)
                {
                    var rg = new GameObject("RingSmooth");
                    rg.transform.SetParent(_portalRoot, false);
                    rg.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                    rg.AddComponent<MeshFilter>().sharedMesh = RingMesh(1f, 0.9f, 64);
                    var rr = rg.AddComponent<MeshRenderer>();
                    rr.sharedMaterial = WallUi.AccentMaterial;
                    rr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; rr.receiveShadows = false;
                    _portalRingSmooth = rr;
                }
            }
            PortalCandidate c = _portal.Spot;
            float r = portalSettings.footprintRadius;
            _portalRoot.SetPositionAndRotation(c.position, c.rotation);
            for (int i = 0; i < 12; i++)
            {
                Transform seg = _portalRoot.GetChild(i);
                float a = i * Mathf.PI * 2f / 12f;
                seg.localPosition = new Vector3(Mathf.Cos(a) * r, 0.003f, Mathf.Sin(a) * r);
                seg.localScale = new Vector3(r * 0.55f, 0.006f, 0.02f);
                ArtHelper().Tint(seg.GetComponent<Renderer>(), c.valid ? (c.roomChecked ? WallHabitatView.Lavender : WallHabitatView.Amber) : WallHabitatView.Coral);
            }
            if (_portalRingSmooth != null)
            {
                _portalRingSmooth.transform.localPosition = Vector3.up * 0.004f;
                _portalRingSmooth.transform.localScale = new Vector3(r, r, 1f);
                WallUi.TintPlate(_portalRingSmooth, c.valid ? (c.roomChecked ? WallHabitatView.Lavender : WallHabitatView.Amber) : WallHabitatView.Coral);
            }
            _portalRoot.gameObject.SetActive(true);
        }

        PortalState _lastPortalSound = PortalState.Hidden;

        void TickPortalVisual(float dt)
        {
            PortalState st = _portal.State;
            if (st != _lastPortalSound)
            {
                if (_audio != null && st == PortalState.Emerging) _audio.Play(WallCue.PortalOpen, true);
                if (_audio != null && st == PortalState.Returning) _audio.Play(WallCue.PortalClose, true);
                _lastPortalSound = st;
            }
            if (st == PortalState.Hidden)
            {
                _portalForcedHome = false;
                if (_portalRoot != null && _portalRoot.gameObject.activeSelf) _portalRoot.gameObject.SetActive(false);
                if (_portalAvatar != null)
                {
                    Destroy(_portalAvatar.gameObject);
                    _portalAvatar = null;
                    if (_visit.State != VisitState.Perched) EndDeskVisit();     // the conversation ends with the portal
                }
                // A preview cancelled before it opened (Meet, voice, Meet larger): its read-only conversation ends with it.
                else if (_side != null && !_visit.Busy) EndDeskVisit();
                _portalFxState = PortalState.Hidden;
                _portalLife = null;
                return;
            }
            if (st == PortalState.Preview || st == PortalState.AwaitConfirm)
            {
                TickPortalSpotTracking();
                if (_portalRoot != null && (_portalRoot.position - _portal.Spot.position).sqrMagnitude > 1e-6f) ShowPortalPreview();
                TickPortalFx(st, 0f);
                return;
            }
            if (_portalAvatar == null && st >= PortalState.Opening && !_portalForcedHome)
            {
                GameObject src = SourceFor(RoleOf(_portal.Agent));
                _portalAvatar = WallHabitatView.CloneVisual(src, "PortalProjection_" + _portal.Agent, _portalRoot, _portal.Spot.avatarHeight);
                if (_portalAvatar != null)
                {
                    _portalAvatarScale = _portalAvatar.localScale.x * PortalFitFactor();   // never taller than the ceiling allows
                    _portalAvatar.localPosition = Vector3.zero;
                    _portalAvatar.localRotation = Quaternion.identity;       // the root already faces the user
                    var an = _portalAvatar.GetComponentInChildren<Animator>();
                    // Attentive listening, not talking: nothing is spoken aloud, and speech must not be simulated (spec 6).
                    if (an != null) { an.applyRootMotion = false; an.speed = 1f; if (an.HasState(0, Animator.StringToHash("Listening"))) an.CrossFadeInFixedTime("Listening", 0.3f, 0); }
                    // rc12: a lively visitor (look at the user and their hands, breathe, lean from a touch, wave back, talk).
                    _portalLife = _portalAvatar.gameObject.AddComponent<PortalVisitorLife>();
                    _portalLife.height = _portal.Spot.avatarHeight * PortalFitFactor();
                    _portalLife.eye = ResolveHead();
                    _portalLife.grow = 0f;
                    // It came out during Emerging (a late spawn): the step-out clip still plays.
                    if (st == PortalState.Emerging) _portalLife.Arrive();
                }
            }
            float k = _portal.Progress;
            TickPortalFx(st, k);
            if (_portalAvatar == null) return;
            // rc12: pops in inside the light column with a small overshoot, then steps out (its clip); on leaving it is drawn
            // up into the column, narrower and taller. It never walks: the cleared column is where it stays.
            float stretch;
            float grow = PortalGrow(st, k, out stretch);
            float s = _portalAvatarScale * grow;
            float xz = s / Mathf.Sqrt(Mathf.Max(0.2f, stretch));
            _portalAvatar.localScale = new Vector3(xz, s * stretch, xz);
            if (_portalLife != null) { _portalLife.grow = grow; _portalLife.liftWorld = _platformLift; }
            else _portalAvatar.localPosition = Vector3.up * _platformLift;     // on the hover platform
        }

        // ───────────────────────── hands ─────────────────────────

        // The comprehensive rig has FOUR HandVisuals (the two hand-tracking ones, OVRHandVisualLeft/Right, and two
        // controller-driven hands). The previous code latched the FIRST HandVisual of each handedness that
        // FindObjectsByType returned (unordered, inactive ones included) and never looked again, so a hand could be read
        // from a controller-driven visual that is never tracked while hands are used: no ray, no pinch, nothing happens.
        // Now: the wired visual if it is tracked, else whichever candidate of that hand IS tracked this frame.
        AimHandSample ReadHand(int index)
        {
            AimHandSample s;
            HandVisual wired = index == 0 ? leftHandVisual : rightHandVisual;
            if (wired != null && TrySample(wired.Hand, out s)) return s;
            List<IHand> list = index == 0 ? _handsL : _handsR;
            for (int i = 0; i < list.Count; i++) if (TrySample(list[i], out s)) return s;
            // None tracked: look again, at most once a second (a hand that was inactive at the last search shows up).
            if (Time.unscaledTime < _nextHandSearch) return AimHandSample.None;
            _nextHandSearch = Time.unscaledTime + 1f;
            FindHands();
            for (int i = 0; i < list.Count; i++) if (TrySample(list[i], out s)) return s;
            return AimHandSample.None;
        }

        static bool TrySample(IHand hand, out AimHandSample s)
        {
            s = AimHandSample.None;
            if (hand == null) return false;
            var uo = hand as UnityEngine.Object;
            if (ReferenceEquals(uo, null) == false && uo == null) return false;          // destroyed
            if (!hand.IsTrackedDataValid || !hand.IsPointerPoseValid) return false;
            Pose pointer;
            if (!hand.GetPointerPose(out pointer)) return false;
            s = AimHandSample.Ray(pointer.position, pointer.rotation * Vector3.forward, hand.GetIndexFingerIsPinching());
            return true;
        }

        void FindHands()
        {
            _handsL.Clear(); _handsR.Clear();
            HandVisual[] all = FindObjectsByType<HandVisual>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            // Active visuals first, then the rest: the order only matters when two are tracked at once.
            for (int pass = 0; pass < 2; pass++)
                for (int i = 0; i < all.Length; i++)
                {
                    HandVisual v = all[i];
                    if (v == null || v.Hand == null || v.gameObject.activeInHierarchy != (pass == 0)) continue;
                    List<IHand> list = v.Hand.Handedness == Handedness.Left ? _handsL : _handsR;
                    if (!list.Contains(v.Hand)) list.Add(v.Hand);
                }
        }

        /// <summary>For a driver's report: how many hand candidates each side has, and which are tracked now.</summary>
        public string DescribeHands()
        {
            if (_handsL.Count == 0 && _handsR.Count == 0) FindHands();
            AimHandSample s;
            int lt = 0, rt = 0;
            for (int i = 0; i < _handsL.Count; i++) if (TrySample(_handsL[i], out s)) lt++;
            for (int i = 0; i < _handsR.Count; i++) if (TrySample(_handsR[i], out s)) rt++;
            return "hands: L candidates=" + _handsL.Count + " tracked=" + lt + " R candidates=" + _handsR.Count + " tracked=" + rt
                 + " wired L=" + (leftHandVisual != null ? leftHandVisual.name : "none") + " R=" + (rightHandVisual != null ? rightHandVisual.name : "none");
        }

        /// <summary>Play Mode driver: stand in for a hand (ray + pinch). holding/nearHover mark it busy like a paper grab.</summary>
        public void SimulateHand(int hand, Vector3 origin, Vector3 direction, bool pinching, bool holding = false, bool nearHover = false)
        {
            if (hand < 0 || hand > 1) return;
            _simOn[hand] = true;
            _sim[hand] = AimHandSample.Ray(origin, direction.normalized, pinching).With(holding, nearHover, false);
        }

        /// <summary>Open the simulated pinch without moving the hand.</summary>
        public void SimulateRelease(int hand) { if (hand >= 0 && hand <= 1 && _simOn[hand]) _sim[hand].pinching = false; }

        public void SimulateHandLost(int hand) { if (hand >= 0 && hand <= 1) { _simOn[hand] = true; _sim[hand] = AimHandSample.None; } }
        public void StopSimulating() { _simOn[0] = _simOn[1] = false; }

        /// <summary>World centre of a habitat avatar (drivers aim at it).</summary>
        public bool TryGetAvatarCenter(HabitatRole r, out Vector3 c)
        {
            c = Vector3.zero;
            if (_view == null || _view.Avatar(r) == null) return false;
            c = _view.Avatar(r).position + _habitatRoot.up * (_view.AvatarHeight * 0.5f * _habitatRoot.lossyScale.y);
            return true;
        }

        public Vector3 ProxyPosition => _proxy != null ? _proxy.position : Vector3.zero;
        public bool HasProxy => _proxy != null;

        /// <summary>How many desk-visit projections exist right now (must be 0 or 1: one presentation per logical agent).</summary>
        public int ProjectionCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < transform.childCount; i++) if (transform.GetChild(i).name.StartsWith("VisitProjection_", StringComparison.Ordinal)) n++;
                return n;
            }
        }

        /// <summary>Select Agents grab state for drivers and logs.</summary>
        public string DescribeSelectGrab()
        {
            return "selectGrab v2=" + useSelectGrabV2 + " visit=" + _visit.State + (_visit.Agent != null ? ":" + _visit.Agent : "") + (_visit.IsRegrab ? "(regrab)" : "")
                 + " hover=" + (_hoverRole >= 0 ? ((HabitatRole)_hoverRole).ToString() : "-") + " carryHand=" + _carryHand
                 + " grabs=" + _grabs + " regrabs=" + _regrabs + " refusals=" + _refusals + (LastSelectRefusal != null ? " lastRefusal='" + LastSelectRefusal + "'" : "")
                 + " away=" + _registry.AwayCount + " projections=" + ProjectionCount
                 + " muted office=" + (_mute != null ? _mute.OfficeMuted : 0) + " distance=" + (_mute != null ? _mute.DistanceMuted : 0)
                 + " gate L=" + _arb.Signal(0) + "/" + _arb.LastBlock(0) + " R=" + _arb.Signal(1) + "/" + _arb.LastBlock(1)
                 + " grip=" + _grip.Applied + " release=" + _arb.settings.releaseDebounce.ToString("F2") + "s lost=" + _arb.settings.lostGraceSeconds.ToString("F2") + "s"
                 + " perch=" + (_perchSpotSet ? "spot" : "pad") + " lastDrop=" + LastDrop.kind + (LastDrop.why != null ? "(" + LastDrop.why + ")" : "")
                 + " autoOffs=" + SelectAutoOffs + (LastSelectAutoOff != null ? "(" + LastSelectAutoOff + ")" : "");
        }
        public bool HasPortalAvatar => _portalAvatar != null;
        public float PortalAvatarHeight => _portal.Spot.avatarHeight;
    }
}
