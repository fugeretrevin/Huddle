using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using MuseDesk.Spatial;
using MuseDesk.Spatial.Wall;

namespace MuseDesk.Wall
{
    /// <summary>
    /// Phase 3 Wall Habitat: the fourth workspace mode, additive and feature-flagged (ALL flags off by default).
    ///
    ///  - Placement: <see cref="WallModeFlow"/> on the room's walls (MRUK through RoomPlacementService). A ghost
    ///    shows the spot (teal fits / amber check it / coral refused, with words); pinch-and-drag on a wall moves
    ///    it; only the Place here key places it. Saved relative to the wall id and room anchor; re-checked on
    ///    restore, recenter and room updates (a failed check pauses, never jumps).
    ///  - The habitat: <see cref="WallHabitatView"/> drawn from the validated layout; characters moved only by
    ///    <see cref="HabitatStage"/> walkers in response to REAL reducer events (<see cref="IWallEventSource"/>).
    ///  - The desk office is hidden visually only; its agents, decisions and the remote paper delivery keep
    ///    running (Wall is published as a remote mode by the director, so papers come to the user as in Floor).
    ///  - Select Agents, desk visits, /btw, the portal and the Manager live in the partial files.
    ///
    /// Nothing here can start, stop, reassign or change a task, except the Manager's explicit, user-approved
    /// plan commit (which goes through the backend's validated route).
    /// </summary>
    public partial class WallHabitatController : MonoBehaviour, IWallModeHost
    {
        [Header("Feature flags (ReleaseConfigApplier sets these; all off by default)")]
        public WallFeatureFlags flags = new WallFeatureFlags();

        [Header("Wiring (AgentScripts/BuildWallHabitatV1)")]
        public SpatialWorkspaceDirector director;
        public RoomPlacementService roomService;
        [Tooltip("The desk office root (WorkspaceRoot). Hidden visually while Wall is active.")]
        public Transform officeRoot;
        [Tooltip("Spatial/NearUserRig: stands at the desk pose; the Wall dock, perch and cards are placed in its space.")]
        public Transform nearUserRig;
        [Tooltip("Never hidden: the decision paper (DecisionDocument) and anything under it.")]
        public Transform[] neverHide;
        public Transform head;
        [Tooltip("A ModeConsole key to clone for every Wall key (the proven ISDK poke setup).")]
        public Transform keyTemplate;
        public TMP_FontAsset font;
        public Material baseMaterial;
        [Tooltip("Desk agents (Researcher, Developer, Tester) whose MESH is cloned for habitat avatars. Never moved.")]
        public GameObject[] agentSources = new GameObject[3];
        [Tooltip("The dedicated Manager character (Agent_Manager prefab, same rig and clip names as the desk agents). Empty = the Developer mesh with a lavender cap.")]
        public GameObject managerSource;
        public HabitatArtManifest artManifest;
        [Tooltip("IWallBackend (WallBackendGateway).")]
        public MonoBehaviour backendBehaviour;
        [Tooltip("IWallEventSource + IWallTaskContext (WallEventBridge).")]
        public MonoBehaviour eventsBehaviour;

        [Header("Placement")]
        public WallPlacementSettings placement = new WallPlacementSettings();
        public WalkerSettings walker = new WalkerSettings();
        [Tooltip("Living Office: the A7 ElevatorDispatcher runs the cabin (wait spot, face the door, first come first served). Off = the original one-rider machine.")]
        public bool useElevatorDispatcher = true;

        [Header("Near-user layout (NearUserRig space: +Z toward the user, user's right is -X, desk top y = 0)")]
        public Vector3 dockLocal = new Vector3(-0.19f, 0.045f, 0.50f);
        public float dockTiltBack = 35f;
        public float keyScale = 0.023f;
        public Vector3 perchLocal = new Vector3(0.22f, 0.004f, 0.40f);
        public float perchRadius = 0.13f;
        public Vector3 visitCardLocal = new Vector3(0.26f, 0.13f, 0.40f);
        public Vector3 planCardLocal = new Vector3(0.04f, 0.17f, 0.32f);

        [Header("Comfort")]
        [Tooltip("Shorter, calmer portal and visit animations; no beacon pulsing.")]
        public bool comfortMode;
        /// <summary>Comfort is on when set here OR when the app-wide Reduced Motion setting is on.</summary>
        public bool Comfort => comfortMode || MuseDesk.Motion.MotionSettings.ReducedMotion;

        const string SaveKey = "MuseDesk.WallHabitat.v1";
        const string ActiveKey = "MuseDesk.WallHabitat.active";

        WallModeFlow _flow;
        HabitatStage _stage;
        WallHabitatView _view;
        WallLayoutKind _viewKind;
        Transform _habitatRoot;
        readonly WorkspaceVisualSuppressor _suppressor = new WorkspaceVisualSuppressor();
        IWallBackend _backend;
        IWallEventSource _events;
        IWallTaskContext _tasks;
        bool _flagsApplied, _restoreTried, _subscribed, _hooks, _mountHook;
        int _recenterFrames;
        float _statusRefresh;
        readonly Color[] _statusColors = { WallHabitatView.Grey, WallHabitatView.Grey, WallHabitatView.Grey, WallHabitatView.Grey };
        readonly bool[] _away = new bool[4];
        string _note = "";
        float _noteUntil;

        // ── IWallModeHost ──
        public bool WallEnabled => flags.wallHabitatEnabled;
        public bool WallActive => _flow != null && (_flow.State == WallModeState.Active || _flow.State == WallModeState.Relocating);
        public bool WallPreviewing => _flow != null && (_flow.State == WallModeState.Previewing || _flow.State == WallModeState.Relocating);

        public WallModeFlow Flow => _flow;
        public HabitatStage Stage => _stage;
        public WallHabitatView View => _view;
        public string StatusLine => Time.unscaledTime < _noteUntil ? _note : (_flow != null ? _flow.Status : "");

        void Awake()
        {
            _flow = new WallModeFlow(() => roomService != null ? roomService.Room : null, () => roomService != null ? roomService.Walls : null,
                                     ReadUser, placement, flags);
            _flow.PlacedChanged += OnPlacedChanged;
            _flow.Changed += OnFlowChanged;
            _registry = new AgentPresentationRegistry();
            _visit = new AgentVisitFlow(_registry);
            _portal = new PortalFlow(_registry);
            _manager = new ManagerPlanFlow();
            _manager.Committed += OnPlanCommitted;
            _backend = backendBehaviour as IWallBackend;
            _events = eventsBehaviour as IWallEventSource;
            _tasks = eventsBehaviour as IWallTaskContext;
            if (director != null && director.wallHost == null) director.wallHost = this;
        }

        void OnEnable()
        {
            if (_events != null && !_subscribed)
            {
                _events.HabitatEventRaised += OnHabitatEvent;
                _events.BoardTaskCreated += OnBoardTask;
                _events.SessionReset += OnSessionReset;
                _events.PlanCardReported += OnPlanCardReported;
                _events.CompletionArtifactCreated += OnCompletionArtifact;
                _events.PlanCommittedAnnounced += OnPlanCommittedAnnounced;
                _events.SystemWarning += OnSystemWarning;
                _subscribed = true;
            }
            MuseDesk.Voice.VoiceAddressing.Shared.ListeningStarted += OnVoiceListeningStarted;
            MuseDesk.Voice.VoiceAddressing.Shared.ListeningEnded += OnVoiceListeningEnded;
            SyncVoiceManagerHook();
        }

        void OnDisable()
        {
            if (_subscribed && _events != null)
            {
                _events.HabitatEventRaised -= OnHabitatEvent;
                _events.BoardTaskCreated -= OnBoardTask;
                _events.SessionReset -= OnSessionReset;
                _events.PlanCardReported -= OnPlanCardReported;
                _events.CompletionArtifactCreated -= OnCompletionArtifact;
                _events.PlanCommittedAnnounced -= OnPlanCommittedAnnounced;
                _events.SystemWarning -= OnSystemWarning;
            }
            _subscribed = false;
            MuseDesk.Voice.VoiceAddressing.Shared.ListeningStarted -= OnVoiceListeningStarted;
            MuseDesk.Voice.VoiceAddressing.Shared.ListeningEnded -= OnVoiceListeningEnded;
            if (MuseDesk.Voice.VoiceCommandCenter.ManagerRequestHandler == RequestPlanByVoice) MuseDesk.Voice.VoiceCommandCenter.ManagerRequestHandler = null;
            Unhook();
            ResetPlanStamp();
            if (WallActive || WallPreviewing) ExitWall("disabled");
        }

        /// <summary>Set by ReleaseConfigApplier. Turning the wall flag off leaves Wall mode safely.</summary>
        public void SetFlags(WallFeatureFlags f)
        {
            if (f == null) return;
            bool wasOn = flags.wallHabitatEnabled;
            flags.wallHabitatEnabled = f.wallHabitatEnabled;
            flags.wallManagerEnabled = f.wallManagerEnabled;
            flags.agentSelectEnabled = f.agentSelectEnabled;
            flags.agentBtwEnabled = f.agentBtwEnabled;
            flags.largeAgentPortalEnabled = f.largeAgentPortalEnabled;
            flags.wallElevatorVfxEnabled = f.wallElevatorVfxEnabled;
            if (_flow != null) _flow.flags = flags;
            if (roomService != null) roomService.SetWallDemand(flags.wallHabitatEnabled);
            if (wasOn && !flags.wallHabitatEnabled && (WallActive || WallPreviewing)) ExitWall("flag off");
            if (!flags.agentSelectEnabled && _visit != null && _visit.Busy) _visit.Return(VisitEndReason.ModeChanged);
            if (!flags.largeAgentPortalEnabled && _portal != null) _portal.Interrupt("Portal switched off");
            _flagsApplied = true;
            if (!flags.wallManagerEnabled) ResetPlanStamp();
            SyncVoiceManagerHook();
            RefreshDock();
        }

        /// <summary>The voice layer hands "Manager, ..." to this controller ONLY while the Manager flag is on. With the flag off
        /// nothing is registered, so the voice path is exactly what it was before this feature existed.</summary>
        void SyncVoiceManagerHook()
        {
            bool want = isActiveAndEnabled && flags.wallManagerEnabled && flags.wallHabitatEnabled;
            if (want) MuseDesk.Voice.VoiceCommandCenter.ManagerRequestHandler = RequestPlanByVoice;
            else if (MuseDesk.Voice.VoiceCommandCenter.ManagerRequestHandler == RequestPlanByVoice) MuseDesk.Voice.VoiceCommandCenter.ManagerRequestHandler = null;
        }

        // ───────────────────────── mode host ─────────────────────────

        public bool RequestWall(bool deskIdle)
        {
            if (!flags.wallHabitatEnabled) return false;
            EnsureDock();
            bool held = HandInteractionProbe.AnyHeld || SpatialWorkspaceState.TransitionHeld;
            if (!_flow.RequestEnter(deskIdle || WallActive, held)) { Note(_flow.Status); RefreshDock(); return false; }
            // A remembered spot that still fits is offered first (one press to come back).
            SavedWallPlacement saved;
            if (!WallActive && WallPlacementCodec.TryDecode(PlayerPrefs.GetString(SaveKey, ""), out saved))
            {
                WallCandidate c;
                string why;
                if (WallPlacementCodec.TryRestore(saved, roomService != null ? roomService.Room : null, roomService != null ? roomService.Walls : null, ReadUser(), placement, out c, out why))
                    _flow.Aim(c.position + c.rotation * new Vector3(0, 0, 0.5f), -(c.rotation * Vector3.forward));
            }
            ShowGhost();
            RefreshDock();
            BeginGuideCard();          // rc9: the guided card, "Look at a wall" (WallHabitatController.Guide.cs)
            return true;
        }

        public void ExitWall(string why)
        {
            if (_flow == null) return;
            bool wasActive = WallActive;
            if (_visit != null && _visit.Busy) _visit.Return(VisitEndReason.ModeChanged);
            if (_portal != null) _portal.Interrupt("Mode changed");
            ReleaseHands();
            if (_planCardOpen && _planCard != null) { _planCardOpen = false; _planCard.Show(false); }   // the Manager lives in the wall
            if (_stage != null) _stage.ResetAll();
            _flow.Exit();
            HideGhost();
            EndGuideCard();
            ShowHabitat(false);
            _suppressor.Restore();
            if (wasActive) PlayerPrefs.SetInt(ActiveKey, 0);
            if (director != null) director.PublishWallChanged();
            Debug.Log("[MuseDesk.Wall] Left Wall mode (" + why + ").");
            RefreshDock();
        }

        void OnPlacedChanged()
        {
            // Place here (or a restore): the habitat appears at the placed spot; the desk office hides.
            WallCandidate p = _flow.Placed;
            EnsureView(p.layout);
            _habitatRoot.SetPositionAndRotation(p.position, p.rotation);
            ShowHabitat(true);
            HideGhost();
            EndGuideCard();            // placed: the guided card is done
            Transform rootToHide = officeRoot != null ? officeRoot : SpatialWorkspaceState.WorkspaceRoot;
            if (!_suppressor.Active) _suppressor.Suppress(rootToHide, neverHide);
            Pose reference = Pose.identity;
            bool hasRef = roomService != null && roomService.Room != null && roomService.Room.TryGetReferencePose(out reference);
            string roomId = roomService != null && roomService.Room != null ? roomService.Room.RoomId : "";
            if (p.roomChecked)
            {
                PlayerPrefs.SetString(SaveKey, WallPlacementCodec.Encode(WallPlacementCodec.Build(p, roomId, hasRef, reference, Application.buildGUID)));
                PlayerPrefs.SetInt(ActiveKey, 1);
            }
            else PlayerPrefs.SetInt(ActiveKey, 0);          // a hand-placed spot without a scan is not restored at start-up
            PlayerPrefs.Save();
            if (director != null) director.PublishWallChanged();
            Debug.Log("[MuseDesk.Wall] Habitat placed: wall=" + p.wallId + " layout=" + p.layout + " checked=" + p.roomChecked + " review=" + p.needsReview);
            RefreshDock();
        }

        void OnFlowChanged()
        {
            if (WallPreviewing) ShowGhost();
            RefreshDock();
        }

        // ───────────────────────── keys (also the driver API) ─────────────────────────

        public void PressWall() { if (director != null) director.RequestMode(WorkspaceMode.Wall); else RequestWall(true); }
        public void PressDesk() { if (director != null) director.RequestMode(WorkspaceMode.Desk); else ExitWall("desk key"); }
        public void PressPlaceHere()
        {
            bool held = HandInteractionProbe.AnyHeld || SpatialWorkspaceState.TransitionHeld || HeldByVisit();
            if (!_flow.PlaceHere(held)) Note(_flow.Status);
        }
        public void PressTryAnother() { _flow.TryAnother(); ShowGhost(); }
        public void PressLayout() { _flow.ToggleLayout(); ShowGhost(); }
        public void PressCancel()
        {
            bool entering = _flow.State == WallModeState.Previewing;
            _flow.Cancel();
            HideGhost();
            EndGuideCard();
            if (entering) { if (director != null) director.PublishWallChanged(); }
            RefreshDock();
        }
        public void PressMove()
        {
            if (_visit.Busy || _portal.State != PortalState.Hidden) { Note("Send the visiting agent home first"); return; }
            if (_stage != null && _stage.TravelLocked) { Note("Wait until the elevator trip ends"); return; }
            if (!_flow.Relocate(HandInteractionProbe.AnyHeld)) Note(_flow.Status);
            ShowGhost();
        }

        bool HeldByVisit() { return _visit != null && _visit.State == VisitState.Carrying; }

        void Note(string s) { _note = s ?? ""; _noteUntil = Time.unscaledTime + 4f; }

        // ───────────────────────── frame ─────────────────────────

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            WallUi.Tick(dt);           // transitions finish even after the flags go off
            TickReveal(dt);
            if (!flags.wallHabitatEnabled && !WallActive && !WallPreviewing) { HideDockIfAny(); return; }
            if (!_hooks) Hook();
            EnsureDock();

            if (!_restoreTried) TryRestoreOnStart();

            if (_recenterFrames > 0 && --_recenterFrames == 0) OnRecenterSettled();

            if (_stage != null && WallActive)
            {
                _stage.Tick(dt, Time.unscaledTime);
                LogSoftTransfers();
                TickLiftAudio();
                TickReactions(Time.unscaledTime);
                if (Time.unscaledTime >= _statusRefresh) { _statusRefresh = Time.unscaledTime + 0.5f; RefreshStatusColors(); }
                for (int r = 0; r < 4; r++) _away[r] = _registry.Of(AgentId((HabitatRole)r)) != AgentPresentation.Home;
                Color beacon; bool pulse;
                BeaconState(out beacon, out pulse);
                _view.Apply(_stage, dt, _visit.IsArmed, _hoverRole, _away, _statusColors, beacon, pulse && !Comfort);
                _suppressor.Tick();
            }

            TickInteraction(dt);
            TickManager(dt);
            if (WallPreviewing) UpdateGhost();
            RefreshDockLabels();
        }

        [Tooltip("Re-enter Wall mode at launch when a placement was saved. OFF (owner rc9): every launch starts at the desk so the " +
                 "user chooses Desk / Floor / Ceiling / Wall and sees the placement flow; the saved wall spot is offered first when Wall is pressed.")]
        public bool restoreOnStart = false;

        void TryRestoreOnStart()
        {
            if (!restoreOnStart) { _restoreTried = true; return; }
            if (director != null && !director.RestoreSettled) return;
            if (roomService == null || roomService.Walls == null || roomService.Walls.WallAvailability != RoomAvailability.Available)
            {
                if (Time.unscaledTime < 20f) return;               // give the scan time to load, then give up quietly
                _restoreTried = true;
                return;
            }
            _restoreTried = true;
            if (PlayerPrefs.GetInt(ActiveKey, 0) != 1) return;
            if (director != null && director.Mode != WorkspaceMode.Desk) return;     // a Floor/Ceiling placement was restored: keep it
            SavedWallPlacement saved;
            string why;
            if (!WallPlacementCodec.TryDecode(PlayerPrefs.GetString(SaveKey, ""), out saved)) return;
            if (!_flow.TryRestore(saved, out why))
            {
                Debug.Log("[MuseDesk.Wall] Saved wall placement not used: " + why);
                Note("Saved wall spot not used: " + why);
                PlayerPrefs.SetInt(ActiveKey, 0);
            }
        }

        readonly int[] _softSeen = new int[4];

        /// <summary>Device evidence (rc9: "never left the elevator, teleported back to the seat"): every soft transfer
        /// (fade out, reappear at the desk) is logged once with its reason and where the walker was.</summary>
        void LogSoftTransfers()
        {
            for (int r = 0; r < 4; r++)
            {
                HabitatWalker w = _stage.Walker((HabitatRole)r);
                if (w == null || w.SoftTransfers == _softSeen[r]) continue;
                _softSeen[r] = w.SoftTransfers;
                Debug.Log("[MuseDesk.Wall] soft transfer " + (HabitatRole)r + ": " + w.LastBlock + " at " + w.Position.ToString("F3")
                          + " lift floor=" + _stage.lift.CabinFloor + " door=" + _stage.lift.DoorOpenEased.ToString("F2"));
            }
        }

        void RefreshStatusColors()
        {
            for (int r = 0; r < 4; r++)
            {
                string taskId, title, state = null; int rev;
                Color c = WallHabitatView.Grey;
                bool have = _tasks != null && _tasks.TryGetAgentTask(AgentId((HabitatRole)r), out taskId, out title, out state, out rev);
                if (have) c = StateColor(state);
                _statusColors[r] = c;
                if (_view != null && (HabitatRole)r != HabitatRole.Manager)
                    _view.SetActivity((HabitatRole)r, HabitatPresence.FromTaskState(have ? state : null));
            }
            if (_view != null) _view.SetActivity(HabitatRole.Manager, ManagerActivity());
        }

        /// <summary>The Manager has no task of its own: it works while a plan is being drafted or committed, waits while one
        /// awaits the user, and is otherwise calm.</summary>
        DeskActivity ManagerActivity()
        {
            if (_manager == null) return DeskActivity.Idle;
            switch (_manager.State)
            {
                case ManagerFlowState.Proposing: case ManagerFlowState.Committing: case ManagerFlowState.Delegating: case ManagerFlowState.Revising: return DeskActivity.Working;
                case ManagerFlowState.AwaitingApproval: return DeskActivity.Waiting;
                case ManagerFlowState.Failed: return DeskActivity.Problem;
                default: return DeskActivity.Idle;
            }
        }

        /// <summary>The app-wide state palette (blocked is neutral, not amber: amber means "needs you").</summary>
        public static Color StateColor(string state) { return Workspace.OfficeTokens.StateColor(state); }

        void BeaconState(out Color c, out bool pulse)
        {
            // Approval needed (amber) outranks a Manager plan waiting (lavender, a different signal); otherwise a calm teal.
            AttentionController a = AttentionController.Instance;
            if (a != null && a.PendingDecisionCount > 0) { c = WallHabitatView.Amber; pulse = true; return; }
            if (_manager != null && _manager.State == ManagerFlowState.AwaitingApproval) { c = WallHabitatView.Lavender; pulse = true; return; }
            c = WallHabitatView.Teal * 0.8f; pulse = false;
        }

        public static string AgentId(HabitatRole r) { return r.ToString().ToLowerInvariant(); }

        // ───────────────────────── habitat view ─────────────────────────

        WallAudio _audio;

        void EnsureView(WallLayoutKind kind)
        {
            if (_view != null && _viewKind == kind) return;
            if (_stage != null) _stage.ResetAll();
            if (_view != null) _view.Destroy();
            if (_habitatRoot == null)
            {
                _habitatRoot = new GameObject("WallHabitatRoot").transform;
                _habitatRoot.SetParent(transform, false);
            }
            HabitatLayout L = HabitatLayoutBuilder.Build(kind);
            _stage = new HabitatStage(L, useElevatorDispatcher);
            _stage.ActStarted += OnActStarted;
            _stage.ActArrived += OnActArrived;
            _stage.ActEnded += OnActEnded;
            _stage.ActDropped += OnActDropped;
            _doorsWereMoving = _cabinWasMoving = false;
            for (int r = 0; r < 4; r++) _stage.Walker((HabitatRole)r).settings = walker;
            _view = new WallHabitatView(_habitatRoot, L, ResolveMaterial(), agentSources, artManifest, managerSource);
            _viewKind = kind;
            if (_audio == null) _audio = new WallAudio(_habitatRoot, nearUserRig != null ? nearUserRig : transform);
            // (Elevator sounds follow the cabin's REAL transitions: see TickLiftAudio.)
            var issues = HabitatLayoutValidator.Validate(L);
            if (issues.Count > 0) Debug.LogError("[MuseDesk.Wall] Layout " + kind + " failed validation: " + issues[0].what);
        }

        bool _doorsWereMoving, _cabinWasMoving;

        /// <summary>Doors slide and a small ding on arrival, from whichever authority runs the cabin (edges of its real state).</summary>
        void TickLiftAudio()
        {
            IHabitatElevator lift = _stage.lift;
            bool doors = lift.DoorsMoving, moving = lift.CabinMoving;
            if (_audio != null && WallActive)
            {
                if (doors && !_doorsWereMoving) _audio.Play(WallCue.DoorSlide);
                if (!moving && _cabinWasMoving) _audio.Play(WallCue.Ding);
            }
            _doorsWereMoving = doors; _cabinWasMoving = moving;
        }

        // ── reactions (sprint A3 arbiter) and truthful Listening (sprint A12) ──

        // One arbiter for the four habitat characters: a walking or riding character's body is taken (Critical), so a
        // completion nod or a worried beat never plays over a walk; it waits (briefly) or is dropped as stale. Requests
        // are made only for confirmed events (task.completed / task.failed from the reducers).
        readonly MuseDesk.Agents.ReactionArbiter _reactions = new MuseDesk.Agents.ReactionArbiter(new MuseDesk.Agents.ReactionArbiterConfig { AgentCount = 4 });
        readonly bool[] _bodyBusy = new bool[4];
        public int ReactionsPlayed { get; private set; }
        public int ReactionsHeld { get; private set; }

        // A confirmed completion nod whose agent is away (a pick-up trip, the lift) waits here, up to DeferredNods.MaxWaitSeconds,
        // until the agent is home and free, instead of being dropped as stale. Only task.completed ever gets in.
        readonly DeferredNods _nods = new DeferredNods(4);
        public int NodsDeferred => _nods.Deferred;
        public int NodsReleased => _nods.Released;
        public int NodsExpired => _nods.Expired;

        void TickReactions(float now)
        {
            // Focus mode, the same rule as the desk: a completion nod is silenced; status-backed worry (a confirmed failure) stays.
            _reactions.SetFocus(MuseDesk.Workspace.FocusModeController.Active);
            for (int r = 0; r < 4; r++)
            {
                bool busy = !_stage.Walker((HabitatRole)r).AtHome;
                if (busy == _bodyBusy[r]) continue;
                _bodyBusy[r] = busy;
                if (busy) _reactions.Request(now, new MuseDesk.Agents.ReactionRequest(r, MuseDesk.Agents.ReactionKind.Walking, now, true));
                else _reactions.End(r, MuseDesk.Agents.ReactionKind.Walking);
            }
            MuseDesk.Agents.ReactionRequest played;
            while (_reactions.Poll(now, out played)) PlayReaction(played.Agent, played.Kind);

            for (int r = 0; r < 4; r++)
            {
                if (!_nods.IsPending(r)) continue;
                if (MuseDesk.Workspace.FocusModeController.Active) { _nods.Drop(r); continue; }   // Focus: dropped, not replayed later
                bool home = _stage.Walker((HabitatRole)r).AtHome && !_reactions.IsBusy(r, now);
                if (_nods.TryRelease(r, now, home)) RequestReaction((HabitatRole)r, MuseDesk.Agents.ReactionKind.CompletionNod, true);
            }
        }

        void RequestReaction(HabitatRole role, MuseDesk.Agents.ReactionKind kind, bool fromQueue = false)
        {
            float now = Time.unscaledTime;
            if (kind == MuseDesk.Agents.ReactionKind.CompletionNod && !fromQueue && !MuseDesk.Workspace.FocusModeController.Active
                && (!_stage.Walker(role).AtHome || _reactions.IsBusy((int)role, now)))
            {
                _nods.Enqueue((int)role, now);          // away on a trip: the nod waits for the agent to be home (bounded)
                ReactionsHeld++;
                return;
            }
            var v = _reactions.Request(now, new MuseDesk.Agents.ReactionRequest((int)role, kind, now, true));
            if (v == MuseDesk.Agents.ReactionVerdict.Play) PlayReaction((int)role, kind);
            else ReactionsHeld++;
        }

        void PlayReaction(int role, MuseDesk.Agents.ReactionKind kind)
        {
            if (_view == null) return;
            ReactionsPlayed++;
            if (kind == MuseDesk.Agents.ReactionKind.CompletionNod) _view.Acknowledge((HabitatRole)role);
            else if (kind == MuseDesk.Agents.ReactionKind.ErrorWorry) _view.Worry((HabitatRole)role);
        }

        static bool TryVoiceRole(MuseDesk.Voice.VoiceAgentTarget t, out HabitatRole role)
        {
            switch (t)
            {
                case MuseDesk.Voice.VoiceAgentTarget.Researcher: role = HabitatRole.Researcher; return true;
                case MuseDesk.Voice.VoiceAgentTarget.Developer: role = HabitatRole.Developer; return true;
                case MuseDesk.Voice.VoiceAgentTarget.Tester: role = HabitatRole.Tester; return true;
                case MuseDesk.Voice.VoiceAgentTarget.Manager: role = HabitatRole.Manager; return true;
                default: role = HabitatRole.Researcher; return false;
            }
        }

        // Only the agent the voice layer says is being addressed shows Listening, and only while the mic is open.
        void OnVoiceListeningStarted(MuseDesk.Voice.VoiceAgentTarget t)
        {
            HabitatRole role;
            if (_view != null && WallActive && TryVoiceRole(t, out role)) _view.SetListening(role, true);
        }

        void OnVoiceListeningEnded(MuseDesk.Voice.VoiceAgentTarget t)
        {
            HabitatRole role;
            if (_view != null && TryVoiceRole(t, out role)) _view.SetListening(role, false);
        }

        // Appear: the habitat grows OUT of the wall (depth scale 0.02 -> 1, eased) instead of popping in; leave: folds
        // back into the wall, then deactivates. Purely visual: the stage/placement state changes at once.
        readonly MuseDesk.Motion.MotionClock _revealClock = new MuseDesk.Motion.MotionClock();
        float _revealFrom, _revealTo, _reveal = 1f;
        const float RevealSeconds = 0.45f, FoldedDepth = 0.02f;

        void ShowHabitat(bool on)
        {
            if (_habitatRoot == null) return;
            bool active = _habitatRoot.gameObject.activeSelf;
            if (on && !active) { _habitatRoot.gameObject.SetActive(true); _reveal = FoldedDepth; }
            float goal = on ? 1f : FoldedDepth;
            if (!on && !active) return;
            if (Mathf.Approximately(_revealTo, goal) && (_revealClock.Running || Mathf.Approximately(_reveal, goal))) return;
            _revealFrom = _reveal; _revealTo = goal;
            _revealClock.Start(new MuseDesk.Motion.MotionToken(on ? RevealSeconds : RevealSeconds * 0.7f,
                                                                on ? MuseDesk.Motion.EaseKind.OutCubic : MuseDesk.Motion.EaseKind.InOutCubic));
            ApplyReveal();
        }

        void TickReveal(float dt)
        {
            if (_habitatRoot == null || !_revealClock.Running) return;
            bool done = _revealClock.Step(dt);
            _reveal = Mathf.Lerp(_revealFrom, _revealTo, _revealClock.Value);
            ApplyReveal();
            if (done && _revealTo < 0.5f) _habitatRoot.gameObject.SetActive(false);
        }

        void ApplyReveal()
        {
            Vector3 s = _habitatRoot.localScale;
            s.z = s.x * Mathf.Max(FoldedDepth, _reveal);   // root scale is uniform otherwise; only depth animates
            _habitatRoot.localScale = s;
        }

        Material _fallbackMat;
        Material ResolveMaterial()
        {
            if (baseMaterial != null) return baseMaterial;
            if (_fallbackMat != null) return _fallbackMat;
            Shader s = Shader.Find("Universal Render Pipeline/Simple Lit");
            if (s == null) s = Shader.Find("Universal Render Pipeline/Lit");
            if (s == null) s = Shader.Find("Universal Render Pipeline/Unlit");
            _fallbackMat = new Material(s) { name = "WallHabitat_Fallback" };
            return _fallbackMat;
        }

        // ───────────────────────── ghost preview ─────────────────────────

        Transform _ghost;
        Renderer[] _ghostEdges;
        TMP_Text _ghostLabel;
        WallHabitatView _ghostArt;
        Vector3 _ghostSize;

        void ShowGhost()
        {
            if (!WallPreviewing || !_flow.HasShown) { HideGhost(); return; }
            if (_ghost == null) BuildGhost();
            WallCandidate c = _flow.Shown;
            if (_ghostSize != c.size) LayoutGhost(c.size);
            _ghost.SetPositionAndRotation(c.position, c.rotation);
            Color col = !c.valid ? WallHabitatView.Coral : c.needsReview ? WallHabitatView.Amber : WallHabitatView.Teal;
            for (int i = 0; i < _ghostEdges.Length; i++) _ghostArt.Tint(_ghostEdges[i], col);
            string words = c.valid ? (c.needsReview ? "Check this spot: " + c.reason : "Fits: " + c.reason) : "Can't place: " + c.reason;
            words += c.roomChecked ? "\nChecked against the room scan. Things not in the scan were not checked." : "\nNo room scan here: nothing around it was checked.";
            _ghostLabel.text = words;
            _ghostLabel.color = WallHabitatView.Ink;
            if (!_ghost.gameObject.activeSelf) _ghost.gameObject.SetActive(true);
        }

        void UpdateGhost()
        {
            if (_flow.HasShown && _ghost != null && _ghost.gameObject.activeSelf)
            {
                WallCandidate c = _flow.Shown;
                if ((_ghost.position - c.position).sqrMagnitude > 1e-8f || _ghost.rotation != c.rotation || _ghostSize != c.size) ShowGhost();
            }
            else if (_flow.HasShown) ShowGhost();
        }

        void HideGhost() { if (_ghost != null && _ghost.gameObject.activeSelf) _ghost.gameObject.SetActive(false); }

        void BuildGhost()
        {
            _ghost = new GameObject("WallGhost").transform;
            _ghost.SetParent(transform, false);
            _ghostArt = ArtHelper();
            _ghostEdges = new Renderer[16];
            for (int i = 0; i < _ghostEdges.Length; i++) _ghostEdges[i] = _ghostArt.Box(_ghost, "Edge" + i, Vector3.zero, Vector3.one, WallHabitatView.Teal);
            _ghostLabel = WallUi.MakeText(_ghost, "GhostLabel", font, Vector3.zero, new Vector2(40f, 6f), 14f, WallHabitatView.Ink, TextAlignmentOptions.Center);
            _ghostArt.Box(_ghost, "LabelPlate", Vector3.zero, Vector3.one, WallHabitatView.Paper).name = "LabelPlate";
        }

        void LayoutGhost(Vector3 s)
        {
            _ghostSize = s;
            float t = 0.006f, hw = s.x * 0.5f, hh = s.y * 0.5f, d = s.z;
            int i = 0;
            // 12 edges of the habitat volume + 4 wall contact pads at the corners.
            for (int sy = -1; sy <= 1; sy += 2) for (int sz = 0; sz <= 1; sz++) Edge(i++, new Vector3(0, sy * hh, sz * d), new Vector3(s.x, t, t));
            for (int sx = -1; sx <= 1; sx += 2) for (int sz = 0; sz <= 1; sz++) Edge(i++, new Vector3(sx * hw, 0, sz * d), new Vector3(t, s.y, t));
            for (int sx = -1; sx <= 1; sx += 2) for (int sy = -1; sy <= 1; sy += 2) Edge(i++, new Vector3(sx * hw, sy * hh, d * 0.5f), new Vector3(t, t, d));
            for (int sx = -1; sx <= 1; sx += 2) for (int sy = -1; sy <= 1; sy += 2) Edge(i++, new Vector3(sx * (hw - 0.03f), sy * (hh - 0.03f), 0.003f), new Vector3(0.05f, 0.05f, 0.004f));
            // Label in front of the volume, facing the room (TMP reads on -Z: turn it so -Z faces out).
            _ghostLabel.transform.localPosition = new Vector3(0, -hh - 0.05f, d + 0.01f);
            _ghostLabel.transform.localRotation = Quaternion.Euler(0, 180f, 0);
            Transform plate = _ghost.Find("LabelPlate");
            if (plate != null) { plate.localPosition = new Vector3(0, -hh - 0.05f, d + 0.012f); plate.localScale = new Vector3(0.42f, 0.07f, 0.002f); }
        }

        void Edge(int i, Vector3 c, Vector3 size) { _ghostEdges[i].transform.localPosition = c; _ghostEdges[i].transform.localScale = size; }

        WallHabitatView _artHelper;
        /// <summary>A view used only as a box/tint factory for UI (no layout content is drawn).</summary>
        WallHabitatView ArtHelper()
        {
            if (_view != null) return _view;
            if (_artHelper == null)
            {
                var holder = new GameObject("WallArtHelper").transform;
                holder.SetParent(transform, false);
                _artHelper = new WallHabitatView(holder, HabitatLayoutBuilder.Build(WallLayoutKind.Compact), ResolveMaterial(), null, null);
                holder.gameObject.SetActive(false);
            }
            return _artHelper;
        }

        // ───────────────────────── dock (near-user Wall controls) ─────────────────────────

        Transform _dock;
        TMP_Text _dockStatus;
        WallKey _kWall, _kDesk, _kPlace, _kOther, _kLayout, _kCancel, _kMove, _kSelect, _kManager;

        // UnifiedModeDockV1: when the director's Spaces dock (ModeConsole) carries the Wall keys it sets ExternalDock, and
        // this controller builds and shows NO dock of its own; it only reports changes (DockChanged). Without it (an older
        // scene) the fallback dock below is built as before, with the same short one-word labels as the Spaces dock.
        bool _externalDock;
        string _dockSig;

        void EnsureDock()
        {
            if (_externalDock) { HideDockIfAny(); return; }
            if (_dock != null || nearUserRig == null || keyTemplate == null) return;
            _dock = new GameObject("WallDock").transform;
            _dock.SetParent(nearUserRig, false);
            var art = ArtHelper();
            // The keys are clones of the console's 36 mm segment key (a 42 mm housing): 46 mm apart so they do not overlap.
            float p = 0.046f;
            art.Box(_dock, "DockPlate", new Vector3(0, 0.004f, 0.006f), new Vector3(p * 4f + 0.012f, 0.085f, 0.004f), WallHabitatView.Paper);
            _kWall = WallUi.MakeKey(keyTemplate, _dock, "Key_Wall", SpacesDockLogic.Wall, new Vector3(-1.5f * p, 0.016f, 0), keyScale, PressWall);
            _kPlace = WallUi.MakeKey(keyTemplate, _dock, "Key_PlaceHere", SpacesDockLogic.Place, new Vector3(-1.5f * p, 0.016f, 0), keyScale, PressPlaceHere);
            _kOther = WallUi.MakeKey(keyTemplate, _dock, "Key_Other", SpacesDockLogic.NextSpot, new Vector3(-0.5f * p, 0.016f, 0), keyScale, PressTryAnother);
            _kLayout = WallUi.MakeKey(keyTemplate, _dock, "Key_Layout", SpacesDockLogic.Compact, new Vector3(0.5f * p, 0.016f, 0), keyScale, PressLayout);
            _kCancel = WallUi.MakeKey(keyTemplate, _dock, "Key_Cancel", SpacesDockLogic.Cancel, new Vector3(1.5f * p, 0.016f, 0), keyScale, PressCancel);
            _kMove = WallUi.MakeKey(keyTemplate, _dock, "Key_Move", SpacesDockLogic.Move, new Vector3(-1.5f * p, 0.016f, 0), keyScale, PressMove);
            _kDesk = WallUi.MakeKey(keyTemplate, _dock, "Key_Desk", SpacesDockLogic.Desk, new Vector3(1.5f * p, 0.016f, 0), keyScale, PressDesk);
            _kSelect = WallUi.MakeKey(keyTemplate, _dock, "Key_Select", SpacesDockLogic.Select, new Vector3(-0.5f * p, -0.016f, 0), keyScale, PressSelectAgents);
            _kManager = WallUi.MakeKey(keyTemplate, _dock, "Key_Manager", SpacesDockLogic.Manager, new Vector3(0.5f * p, -0.016f, 0), keyScale, PressManager);
            _dockStatus = WallUi.MakeText(_dock, "DockStatus", font, new Vector3(0, 0.036f, -0.0005f), new Vector2(p * 400f, 1.2f), 4.2f, WallHabitatView.Ink, TextAlignmentOptions.Center);
            PlaceDock();
            RefreshDock();
        }

        void PlaceDock()
        {
            if (_dock == null) return;
            // Rig space: the eye is about (0, 0.30, 0.62). Face the eye and lean back like a desk console.
            Vector3 eyeLocal = new Vector3(0f, 0.30f, 0.62f);
            Vector3 d = dockLocal - eyeLocal; d.y = 0f;
            _dock.localPosition = dockLocal;
            _dock.localRotation = Quaternion.LookRotation(d.sqrMagnitude < 1e-6f ? Vector3.back : d.normalized, Vector3.up) * Quaternion.Euler(dockTiltBack, 0f, 0f);
        }

        void HideDockIfAny()
        {
            if (_dock != null && _dock.gameObject.activeSelf) _dock.gameObject.SetActive(false);
        }

        void RefreshDock()
        {
            if (_externalDock)
            {
                HideDockIfAny();
                _dockSig = DockSignature();
                RaiseDockChanged();
                return;
            }
            if (_dock == null) return;
            bool on = flags.wallHabitatEnabled || WallActive || WallPreviewing;
            if (_dock.gameObject.activeSelf != on) _dock.gameObject.SetActive(on);
            if (!on) return;
            bool previewing = WallPreviewing;
            bool active = WallActive && !previewing;
            bool busyDirector = director != null && (director.IsPreviewing || (director.Core != null && director.Core.IsTransitioning));
            _kWall.Show(!previewing && !active, !busyDirector);
            _kPlace.Show(previewing, _flow.HasShown && _flow.Shown.valid);
            _kOther.Show(previewing);
            _kLayout.Show(previewing);
            _kCancel.Show(previewing);
            _kMove.Show(active);
            _kDesk.Show(active);
            _kSelect.Show(active && flags.agentSelectEnabled);
            _kManager.Show(active && flags.wallManagerEnabled);
        }

        void RefreshDockLabels()
        {
            if (_externalDock)
            {
                // The Spaces dock shows these: tell it only when something it shows changed (a cheap compare per frame).
                string sig = DockSignature();
                if (sig != _dockSig) { _dockSig = sig; RaiseDockChanged(); }
                return;
            }
            if (_dock == null || !_dock.gameObject.activeSelf) return;
            _kLayout.SetLabel(WallLayoutFull ? SpacesDockLogic.Compact : SpacesDockLogic.Full);
            _kSelect.SetLabel(WallSelecting ? SpacesDockLogic.Done : SpacesDockLogic.Select);
            bool canPlace = WallCanPlace;
            if (_kPlace.shown && _kPlace.enabled != canPlace) _kPlace.Show(true, canPlace);
            string s = WallStatus;
            if (_dockStatus.text != s) _dockStatus.text = s;
        }

        string DockSignature()
        {
            if (_flow == null) return "";
            return (WallEnabled ? "E" : "e") + (WallActive ? "A" : "a") + (WallPreviewing ? "P" : "p") + (WallCanPlace ? "C" : "c")
                 + (WallLayoutFull ? "F" : "f") + (WallSelecting ? "S" : "s") + (WallSelectEnabled ? "1" : "0") + (WallManagerEnabled ? "1" : "0")
                 + "|" + WallStatus;
        }

        void RaiseDockChanged()
        {
            Action h = DockChanged;
            if (h == null) return;
            try { h(); } catch (Exception e) { Debug.LogException(e); }
        }

        // ── IWallModeHost: the unified Spaces dock ──

        public event Action DockChanged;

        public bool ExternalDock
        {
            get { return _externalDock; }
            set
            {
                if (_externalDock == value) return;
                _externalDock = value;
                if (value) HideDockIfAny();
                else { EnsureDock(); }
                RefreshDock();
            }
        }

        public bool WallCanPlace => _flow != null && _flow.HasShown && _flow.Shown.valid;
        public bool WallLayoutFull => _flow != null && _flow.Layout == WallLayoutKind.Full;
        public bool WallSelecting => _visit != null && _visit.IsArmed;
        public bool WallSelectEnabled => flags.agentSelectEnabled;
        public bool WallManagerEnabled => flags.wallManagerEnabled;

        public string WallStatus
        {
            get
            {
                string s = StatusLine;
                if (!string.IsNullOrEmpty(s)) return s;
                if (WallActive && !WallPreviewing) return _flow != null && _flow.Suspended ? "Room changed: habitat paused" : "Office on the wall";
                return WallPreviewing ? "Choose a wall spot" : "Wall mode";
            }
        }

        public void WallPlace() { if (WallPreviewing) PressPlaceHere(); }
        public void WallNextSpot() { if (WallPreviewing) PressTryAnother(); }
        public void WallToggleLayout() { if (WallPreviewing) PressLayout(); }
        public void WallCancel() { if (WallPreviewing) PressCancel(); }
        public void WallMove() { if (WallActive && !WallPreviewing) PressMove(); }
        public void WallSelect() { if (WallActive && !WallPreviewing && flags.agentSelectEnabled) PressSelectAgents(); }
        public void WallManager() { if (WallActive && !WallPreviewing && flags.wallManagerEnabled) PressManager(); }

        // ───────────────────────── events from the reducers ─────────────────────────

        void OnHabitatEvent(HabitatEvent e)
        {
            if (_stage == null || !WallActive) return;
            _stage.Accept(e, Time.unscaledTime);
            // One restrained reaction for a CONFIRMED completion or failure (the event came from the reducer), through the
            // arbiter: never over a walk or a ride, never repeated inside its cooldown.
            if (e.kind == HabitatEventKind.TaskCompleted) RequestReaction(e.agent, MuseDesk.Agents.ReactionKind.CompletionNod);
            else if (e.kind == HabitatEventKind.TaskFailed) RequestReaction(e.agent, MuseDesk.Agents.ReactionKind.ErrorWorry);
        }

        void OnSessionReset()
        {
            if (_stage != null) _stage.ResetAll();
            if (_visit != null && _visit.Busy) _visit.Return(VisitEndReason.Cancelled);
            if (_portal != null) _portal.Interrupt("Session reset");
            if (_manager != null) _manager.Reset();
            ResetLivingOffice();
            _nods.Clear();
            RestorePlanPicture();       // the backend still knows the committed plans: rebuild the wall's picture from it
        }

        // ───────────────────────── recenter, room changes, pause ─────────────────────────

        void Hook()
        {
            if (_hooks) return;
            if (roomService != null) roomService.RoomChanged += OnRoomChanged;
            if (!_mountHook) { OVRManager.HMDMounted += OnMounted; _mountHook = true; }
            if (OVRManager.display != null) { OVRManager.display.RecenteredPose += OnRecentered; _hooks = true; }
        }

        void Unhook()
        {
            if (roomService != null) roomService.RoomChanged -= OnRoomChanged;
            if (_mountHook) { OVRManager.HMDMounted -= OnMounted; _mountHook = false; }
            if (_hooks && OVRManager.display != null) OVRManager.display.RecenteredPose -= OnRecentered;
            _hooks = false;
        }

        void OnRecentered()
        {
            // Lost tracking / recenter cancels anything carried or opened, never commits it.
            if (_visit != null && _visit.Busy) _visit.Return(VisitEndReason.Recentered);
            if (_portal != null) _portal.Interrupt("Recentered");
            ReleaseHands();
            if (_stage != null) _stage.ResetAll();
            _recenterFrames = 2;
        }

        void OnMounted() { PlaceCards(); }

        void OnRecenterSettled()
        {
            // The room anchors have their new poses: put the habitat back on its wall, or pause it.
            if (!WallActive || !_flow.HasPlaced) return;
            SavedWallPlacement saved;
            string why;
            if (WallPlacementCodec.TryDecode(PlayerPrefs.GetString(SaveKey, ""), out saved) && _flow.TryRestore(saved, out why)) return;
            _flow.OnRoomChanged();
        }

        void OnRoomChanged()
        {
            if (WallActive) _flow.OnRoomChanged();
        }

        void OnApplicationPause(bool paused)
        {
            if (!paused) return;
            if (_visit != null && _visit.Busy) _visit.Return(VisitEndReason.Paused);
            if (_portal != null) _portal.Interrupt("Paused");
            ReleaseHands();
            if (_stage != null) _stage.ResetAll();
        }

        // ───────────────────────── inputs ─────────────────────────

        SeatedUser ReadUser()
        {
            Transform h = ResolveHead();
            if (h == null) return new SeatedUser(new Vector3(0f, 1.2f, 0f), Vector3.forward);
            return new SeatedUser(h.position, h.forward);
        }

        Transform ResolveHead()
        {
            if (head != null) return head;
            if (director != null && director.head != null) return head = director.head;
            Camera cam = Camera.main;
            if (cam != null) head = cam.transform;
            return head;
        }

        /// <summary>One line for drivers and logs.</summary>
        public string Describe()
        {
            string s = "flags wall=" + flags.wallHabitatEnabled + " select=" + flags.agentSelectEnabled + " btw=" + flags.agentBtwEnabled + " portal=" + flags.largeAgentPortalEnabled + " manager=" + flags.wallManagerEnabled;
            s += " | mode=" + SpatialWorkspaceState.Mode + " remote=" + SpatialWorkspaceState.IsRemote + " flow=" + (_flow != null ? _flow.State.ToString() : "-");
            if (_flow != null && _flow.HasShown) s += " shown=" + (_flow.Shown.valid ? "valid" : "invalid:" + _flow.Shown.problem) + " wall=" + _flow.Shown.wallId + " layout=" + _flow.Shown.layout + " review=" + _flow.Shown.needsReview;
            if (_flow != null && _flow.HasPlaced) s += " placed=" + _flow.Placed.wallId + "@" + _flow.Placed.position.ToString("F2") + " suspended=" + _flow.Suspended;
            s += " | hidden=" + _suppressor.HiddenCount;
            if (_stage != null) s += " | stage completed=" + _stage.Completed + " dropped=" + _stage.Dropped + " elevator=" + (_stage.elevator != null ? _stage.elevator.State.ToString() : _stage.dispatch.ToString());
            s += " | visit=" + _visit.State + (_visit.Agent != null ? ":" + _visit.Agent : "") + " portal=" + _portal.State + " manager=" + _manager.State;
            s += " | status='" + StatusLine + "'";
            return s;
        }
    }
}
