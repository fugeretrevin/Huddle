using System;
using System.Text;
using TMPro;
using UnityEngine;
using Oculus.Interaction;
using MuseDesk.Workspace;

namespace MuseDesk.Spatial
{
    /// <summary>
    /// The one console for where the office is: a low wedge with
    ///
    ///   a segmented selector ..... Desk | Floor | Ceiling, always shown; the current place is lit
    ///                              teal with a bar under it; a place whose feature flag is off, and
    ///                              every place while the office is moving, looks disabled and
    ///                              cannot be poked;
    ///   a context row ............ only what applies: Next / Confirm / Cancel while a placement is
    ///                              previewed, one wide amber Stop while the office moves, Lower or
    ///                              Raise in Ceiling mode, Bring and Status while the office is away;
    ///   a status line ............ the director's words on the upper lip, one line.
    ///
    /// It replaces the loose mode keys of <see cref="SpatialModeStrip"/>, which now forwards its
    /// calls here (the director still talks to the strip), and the two summon keys of
    /// <see cref="SummonController"/>. The state table is <see cref="SpacesDockLogic.Compute"/>.
    ///
    /// UnifiedModeDockV1 (rc8 feedback) makes it the ONE Spaces dock: a fourth place key, Wall, and the
    /// Wall's context keys (Full / Compact, Move, Select, Manager) in the same row and language; the
    /// shared Next spot / Place / Cancel keys act on whichever preview is open (Floor, Ceiling or Wall).
    /// The wall host then builds no floating dock of its own (<see cref="IWallModeHost.ExternalDock"/>).
    /// With no Wall key in the scene everything works exactly as before.
    /// Keys are shown and hidden by scale with a short ease; a key that is hidden or disabled also
    /// has its PokeInteractable switched off. Presentation only: every press is a request to the
    /// director or the summon controller, which may refuse it.
    /// </summary>
    [DisallowMultipleComponent]
    public class ModeConsole : MonoBehaviour
    {
        [Serializable]
        public class ConsoleKey
        {
            public Transform root;
            [Tooltip("Uniform scale of the key when shown.")]
            public Vector3 shownScale = Vector3.one;
            public TMP_Text label;
            public KeyStateVisual visual;
            public PokeInteractable poke;
            public Renderer cap;

            [NonSerialized] public ScaleEase ease;
            [NonSerialized] public bool shown, disabled, wasAnimating;
            [NonSerialized] public float enableAt;
        }

        [Header("Wiring (AgentScripts/BuildModeConsoleV1)")]
        public SpatialWorkspaceDirector director;
        public SummonController summon;
        [Tooltip("Guided placement step (AgentScripts/BuildPlacementGuideV1). Floor and Ceiling presses go through it; empty = request at once.")]
        public PlacementGuide guide;
        [Tooltip("The Wall Habitat host (implements IWallModeHost; AgentScripts/UnifiedModeDockV1). Empty: the director's wallHost.")]
        public MonoBehaviour wallHostBehaviour;

        [Header("Place selector")]
        public ConsoleKey deskKey = new ConsoleKey();
        public ConsoleKey floorKey = new ConsoleKey();
        public ConsoleKey ceilingKey = new ConsoleKey();
        [Tooltip("The fourth place (UnifiedModeDockV1). Empty: the console has three places, as before.")]
        public ConsoleKey wallKey = new ConsoleKey();
        [Tooltip("Cap material of a place key that is not the current place.")]
        public Material segmentIdle;
        [Tooltip("Cap material of the current place.")]
        public Material segmentActive;
        [Tooltip("The bars under the place keys (Desk, Floor, Ceiling, and Wall when present).")]
        public Transform[] indicators;
        public Vector3 indicatorScale = Vector3.one;
        public string floorWords = "Floor";
        public string sendBackWords = "Send back";

        [Header("Context row")]
        public ConsoleKey nextKey = new ConsoleKey();
        public ConsoleKey confirmKey = new ConsoleKey();
        public ConsoleKey cancelKey = new ConsoleKey();
        public ConsoleKey stopKey = new ConsoleKey();
        public ConsoleKey podKey = new ConsoleKey();
        public ConsoleKey bringKey = new ConsoleKey();
        public ConsoleKey statusKey = new ConsoleKey();
        [Tooltip("Wall preview: Full / Compact layout (UnifiedModeDockV1).")]
        public ConsoleKey layoutKey = new ConsoleKey();
        [Tooltip("Wall at rest: move the habitat, Select agents, the Manager (UnifiedModeDockV1).")]
        public ConsoleKey moveKey = new ConsoleKey();
        public ConsoleKey selectKey = new ConsoleKey();
        public ConsoleKey managerKey = new ConsoleKey();
        [Tooltip("Seconds a key takes to grow in or shrink away.")]
        public float easeSeconds = 0.2f;
        [Tooltip("The Stop key cannot be poked for this long after it appears (the finger that pressed Confirm is still there).")]
        public float armDelay = 0.35f;
        [Tooltip("Words of the confirm and next keys outside a floor preview, and during one (rc6 deliberate placement).")]
        public string confirmWords = "Confirm";
        public string placeHereWords = "Place here";
        public string nextWords = "Next";
        public string otherSpotWords = "Other spot";

        [Header("Status line")]
        public TMP_Text statusLabel;
        public string deskWords = "Office at your desk";
        public string floorModeWords = "Office on the floor";
        public string ceilingModeWords = "Office at the ceiling";
        public string wallModeWords = "Office on the wall";

        [Header("Builder bookkeeping (for Revert)")]
        public Transform focusKey;
        public Transform focusOriginalParent;
        public Vector3 focusOriginalLocalPosition;
        public Quaternion focusOriginalLocalRotation = Quaternion.identity;
        public Vector3 focusOriginalLocalScale = Vector3.one;
        public int focusOriginalSibling;
        public Behaviour focusBillboardDisabled;
        [Tooltip("UnifiedModeDockV1 bookkeeping: how far (console-local x) the existing parts were shifted when the fourth column was added.")]
        public float unifiedShift;
        public bool unifiedApplied;
        public Transform[] unifiedShifted;
        public float[] unifiedDeltas;

        ConsoleKey[] _all;
        ConsoleKey[] _segments;
        readonly ScaleEase[] _indicatorEase = { new ScaleEase(), new ScaleEase(), new ScaleEase(), new ScaleEase() };
        bool _inited, _applied, _animating, _pokeDirty, _sendsBack, _podDown;
        bool _floorPreview, _placeReady = true;
        int _active = -1, _armFrame;
        string _status, _podWords, _floorShown;
        SpacesDockKeys _keys;
        PlacementMode _mode;
        MovablePanel _panel;
        IWallModeHost _host;
        bool _hostBound;

        // The director's last call, so a change on the wall side can lay the console out again.
        bool _aFloor, _aCeiling, _aPreviewing, _aCanCycle, _aTransitioning, _aPodWaiting;
        CeilingPodState _aPod = CeilingPodState.Docked;
        string _aStatus;

        /// <summary>True while the Stop key is on show.</summary>
        public bool StopShown => _applied && _keys.stop;
        /// <summary>True while the Bring or Status key is on show.</summary>
        public bool SummonKeysShown => _applied && (_keys.bring || _keys.status);
        /// <summary>0 Desk, 1 Floor, 2 Ceiling, 3 Wall: the lit place key.</summary>
        public int ActiveSegment => _active;
        /// <summary>The scene has the Wall place key (UnifiedModeDockV1): the console is the one Spaces dock.</summary>
        public bool HasWallKey => wallKey != null && wallKey.root != null;
        /// <summary>The wall host the Wall keys talk to (null without one).</summary>
        public IWallModeHost WallHost => ResolveHost();
        /// <summary>The words on the status line now.</summary>
        public string StatusShown => _status;

        // ───────────────────────── lifecycle ─────────────────────────

        void Awake() { EnsureInit(); }

        void EnsureInit()
        {
            if (_inited) return;
            _inited = true;
            if (wallKey == null) wallKey = new ConsoleKey();
            if (layoutKey == null) layoutKey = new ConsoleKey();
            if (moveKey == null) moveKey = new ConsoleKey();
            if (selectKey == null) selectKey = new ConsoleKey();
            if (managerKey == null) managerKey = new ConsoleKey();
            _segments = new[] { deskKey, floorKey, ceilingKey, wallKey };
            _all = new[] { deskKey, floorKey, ceilingKey, wallKey, nextKey, confirmKey, cancelKey, stopKey, podKey, bringKey, statusKey,
                           layoutKey, moveKey, selectKey, managerKey };
            for (int i = 0; i < _all.Length; i++)
            {
                ConsoleKey k = _all[i];
                if (k == null) { _all[i] = k = new ConsoleKey(); }
                k.ease = new ScaleEase();
                k.shown = i < 4;                                   // the selector is always on show
                if (k.root != null) k.ease.Init(k.root, k.shownScale, easeSeconds, k.shown);
            }
            _segments = new[] { _all[0], _all[1], _all[2], _all[3] };
            if (indicators != null)
                for (int i = 0; i < indicators.Length && i < _indicatorEase.Length; i++)
                    if (indicators[i] != null) _indicatorEase[i].Init(indicators[i], indicatorScale, easeSeconds, false);
            _floorShown = floorWords;
            // Interactables are left alone for the first frames so each one runs its own Start.
            _armFrame = Time.frameCount + 3;
            _pokeDirty = true;
        }

        void OnEnable()
        {
            EnsureInit();
            _armFrame = Time.frameCount + 3;
            _pokeDirty = true;
            _panel = GetComponentInParent<MovablePanel>(true);
            if (_panel != null) _panel.PokesResumed += OnPokesResumed;
            if (!_applied) ApplyIdle();
        }

        void OnDisable()
        {
            if (_panel != null) _panel.PokesResumed -= OnPokesResumed;
            _panel = null;
        }

        void OnDestroy()
        {
            if (_hostBound && _host != null) _host.DockChanged -= OnWallDockChanged;
            _hostBound = false;
        }

        void OnPokesResumed() { _pokeDirty = true; }

        // ───────────────────────── the wall host ─────────────────────────

        IWallModeHost ResolveHost()
        {
            if (_host == null)
            {
                _host = wallHostBehaviour as IWallModeHost;
                if (_host == null && director != null) _host = director.wallHost as IWallModeHost;
            }
            return _host;
        }

        /// <summary>Listen to the wall host once (also while this console is inactive: its state must be right when it shows).</summary>
        void BindHost()
        {
            if (_hostBound || !HasWallKey) return;
            IWallModeHost h = ResolveHost();
            if (h == null) return;
            h.DockChanged += OnWallDockChanged;
            _hostBound = true;
        }

        void OnWallDockChanged()
        {
            if (!_applied) return;
            Apply(_aFloor, _aCeiling, _mode, _aPreviewing, _aCanCycle, _aTransitioning, _aPod, _aPodWaiting, _aStatus);
        }

        // ───────────────────────── state (called by SpatialModeStrip) ─────────────────────────

        /// <summary>Nothing spatial is on offer: Desk lit, the other places disabled, no context keys.</summary>
        public void ApplyIdle()
        {
            Apply(false, false, PlacementMode.Desk, false, false, false, CeilingPodState.Docked, false, null);
        }

        /// <summary>The Floor key reads "Send back" while a Floor office is parked at the desk.</summary>
        public void SetFloorSendsBack(bool sendsBack)
        {
            EnsureInit();
            _sendsBack = sendsBack;
            string words = sendsBack ? sendBackWords : floorWords;
            if (words == _floorShown) return;
            _floorShown = words;
            if (floorKey.label != null) floorKey.label.text = words;
        }

        /// <summary>
        /// Floor preview (rc6): the confirm key reads "Place here" and is shown only while
        /// <paramref name="placeReady"/> (SpatialDirectorCore.CanPlaceHere); Next reads "Other spot".
        /// Called by the director before <see cref="Apply"/>.
        /// </summary>
        public void SetFloorPlacement(bool floorPreview, bool placeReady)
        {
            EnsureInit();
            _floorPreview = floorPreview;
            _placeReady = placeReady;
            if (HasWallKey) return;                     // the Spaces dock's words come from its table (Apply)
            string c = floorPreview ? placeHereWords : confirmWords;
            if (confirmKey.label != null && confirmKey.label.text != c) confirmKey.label.text = c;
            string n = floorPreview ? otherSpotWords : nextWords;
            if (nextKey.label != null && nextKey.label.text != n) nextKey.label.text = n;
        }

        static void SetWords(ConsoleKey k, string words)
        {
            if (k == null || k.label == null || string.IsNullOrEmpty(words) || k.label.text == words) return;
            k.label.text = words;
        }

        /// <summary>Lay the console out for the current state. Called on state changes only.</summary>
        public void Apply(bool floorEnabled, bool ceilingEnabled, PlacementMode mode, bool previewing, bool canCycle,
                          bool transitioning, CeilingPodState podState, bool podWaiting, string status)
        {
            EnsureInit();
            BindHost();
            _applied = true;
            _mode = mode;
            _aFloor = floorEnabled; _aCeiling = ceilingEnabled; _aPreviewing = previewing; _aCanCycle = canCycle;
            _aTransitioning = transitioning; _aPod = podState; _aPodWaiting = podWaiting; _aStatus = status;
            _podDown = podState == CeilingPodState.Engaged;

            IWallModeHost h = HasWallKey ? ResolveHost() : null;
            SpacesDockInput input = new SpacesDockInput
            {
                floorEnabled = floorEnabled, ceilingEnabled = ceilingEnabled, mode = mode, previewing = previewing,
                canCycle = canCycle, transitioning = transitioning, podState = podState, podWaiting = podWaiting,
                floorPreview = _floorPreview && previewing, placeReady = _placeReady,
            };
            if (h != null)
            {
                input.wallEnabled = h.WallEnabled;
                input.wallActive = h.WallActive;
                input.wallPreviewing = h.WallPreviewing;
                input.wallCanPlace = h.WallCanPlace;
                input.wallLayoutFull = h.WallLayoutFull;
                input.wallSelectEnabled = h.WallSelectEnabled;
                input.wallManagerEnabled = h.WallManagerEnabled;
                input.wallSelecting = h.WallSelecting;
            }
            _keys = SpacesDockLogic.Compute(input);

            SetActiveSegment(_keys.active);
            SetDisabled(deskKey, _keys.deskDisabled);
            SetDisabled(floorKey, _keys.floorDisabled);
            SetDisabled(ceilingKey, _keys.ceilingDisabled);
            SetDisabled(wallKey, _keys.wallDisabled || h == null);

            SetShown(nextKey, _keys.next);
            SetShown(confirmKey, _keys.confirm);
            SetDisabled(confirmKey, _keys.confirmDisabled);
            SetShown(cancelKey, _keys.cancel);
            SetShown(stopKey, _keys.stop);
            SetShown(podKey, _keys.pod);
            SetShown(bringKey, _keys.bring);
            SetShown(statusKey, _keys.status);
            SetShown(layoutKey, _keys.layout);
            SetShown(moveKey, _keys.move);
            SetShown(selectKey, _keys.select);
            SetShown(managerKey, _keys.manager);

            if (podKey.label != null && _keys.podWords != _podWords)
            {
                _podWords = _keys.podWords;
                podKey.label.text = _podWords;
            }
            if (HasWallKey)
            {
                // One short word per key (rc8: long labels overflowed with an ellipsis).
                SetWords(confirmKey, _keys.confirmWords);
                SetWords(nextKey, _keys.nextWords);
                SetWords(layoutKey, _keys.layoutWords);
                SetWords(selectKey, _keys.selectWords);
            }

            string line = status;
            if (h != null && (h.WallActive || h.WallPreviewing)) line = h.WallStatus;
            if (string.IsNullOrEmpty(line) && h != null && !string.IsNullOrEmpty(_keys.wallNote)) line = _keys.wallNote;
            if (string.IsNullOrEmpty(line))
                line = h != null && h.WallActive ? wallModeWords
                     : mode == PlacementMode.Floor ? floorModeWords : mode == PlacementMode.Ceiling ? ceilingModeWords : deskWords;
            if (statusLabel != null && line != _status)
            {
                _status = line;
                statusLabel.text = line;
            }
            _pokeDirty = true;
        }

        void SetActiveSegment(int active)
        {
            if (active == _active) return;
            _active = active;
            for (int i = 0; i < _segments.Length; i++)
            {
                ConsoleKey k = _segments[i];
                bool on = i == active;
                Material m = on ? segmentActive : segmentIdle;
                if (k.cap != null && m != null && k.cap.sharedMaterial != m)
                {
                    k.cap.sharedMaterial = m;              // a shared asset, never an instance
                    if (k.visual != null)
                    {
                        k.visual.RefreshBaseColor();
                        k.visual.SimulateState(k.visual.ShownState);   // re-blend from the new base colour
                    }
                }
                if (indicators != null && i < indicators.Length && indicators[i] != null)
                {
                    if (on) _indicatorEase[i].Show(); else _indicatorEase[i].Hide();
                }
            }
            _animating = true;
        }

        void SetDisabled(ConsoleKey k, bool disabled)
        {
            if (k.disabled == disabled) return;
            k.disabled = disabled;
            if (k.visual != null) k.visual.SetDisabled(disabled);
        }

        void SetShown(ConsoleKey k, bool shown)
        {
            if (k.shown == shown || k.root == null) { k.shown = shown; return; }
            k.shown = shown;
            // Only Stop appears under the finger that just pressed something (Confirm, Bring, Lower):
            // it is armed a moment later, and looks disabled until then.
            if (shown) { k.ease.Show(); k.enableAt = k == stopKey ? Time.unscaledTime + armDelay : 0f; }
            else
            {
                k.ease.Hide();
                if (k.poke != null && Time.frameCount >= _armFrame) k.poke.enabled = false;   // at once: a shrinking key must not fire
            }
            _animating = true;
        }

        // ───────────────────────── frame loop (only while something changes) ─────────────────────────

        // Device evidence (rc9: "the panel for choosing the mode was entirely gone"): on every mode change and every 20 s,
        // one log line with where the console is relative to the head and whether it renders. Cheap; no allocation steady state.
        WorkspaceMode _diagMode = (WorkspaceMode)(-1);
        float _diagNext;
        Renderer[] _diagRenderers;

        void LogWhereabouts()
        {
            WorkspaceMode m = SpatialWorkspaceState.Mode;
            float now = Time.unscaledTime;
            if (m == _diagMode && now < _diagNext) return;
            _diagMode = m; _diagNext = now + 20f;
            Camera cam = Camera.main;
            if (cam == null) return;
            if (_diagRenderers == null) _diagRenderers = GetComponentsInChildren<Renderer>(true);
            int on = 0;
            for (int i = 0; i < _diagRenderers.Length; i++) if (_diagRenderers[i] != null && _diagRenderers[i].enabled && _diagRenderers[i].gameObject.activeInHierarchy) on++;
            Vector3 d = transform.position - cam.transform.position;
            float ang = d.sqrMagnitude > 1e-6f ? Vector3.Angle(cam.transform.forward, d) : 0f;
            Debug.Log("[MuseDesk.Dock] mode=" + m + " active=" + gameObject.activeInHierarchy + " scale=" + transform.lossyScale.x.ToString("F2")
                      + " dist=" + d.magnitude.ToString("F2") + "m angleFromGaze=" + ang.ToString("F0") + " renderers=" + on + "/" + _diagRenderers.Length);
        }

        void Update()
        {
            LogWhereabouts();
            if (!_animating && !_pokeDirty) return;
            float dt = Time.unscaledDeltaTime;

            if (_animating)
            {
                bool any = false;
                for (int i = 0; i < _all.Length; i++)
                {
                    ConsoleKey k = _all[i];
                    if (k.root == null) continue;
                    k.ease.Tick(dt);
                    bool a = k.ease.Animating;
                    // When a key has finished growing, let its press visual re-read its resting offsets.
                    if (k.wasAnimating && !a && k.ease.Shown)
                        k.root.BroadcastMessage("SyncToCurrentScale", SendMessageOptions.DontRequireReceiver);
                    k.wasAnimating = a;
                    any |= a;
                }
                for (int i = 0; i < _indicatorEase.Length; i++)
                {
                    _indicatorEase[i].Tick(dt);
                    any |= _indicatorEase[i].Animating;
                }
                _animating = any;
            }

            if (_pokeDirty && Time.frameCount >= _armFrame && (_panel == null || !_panel.PokesSuspended))
                SyncPokes();
        }

        /// <summary>A key can be poked only while it is shown, not disabled, and armed.</summary>
        void SyncPokes()
        {
            bool waiting = false;
            float now = Time.unscaledTime;
            for (int i = 0; i < _all.Length; i++)
            {
                ConsoleKey k = _all[i];
                if (k.poke == null) continue;
                bool want = k.shown && !k.disabled;
                if (want && now < k.enableAt) { want = false; waiting = true; }
                if (k.poke.enabled != want) k.poke.enabled = want;
            }
            _pokeDirty = waiting;
        }

        // ───────────────────────── key targets (wired by the builder) ─────────────────────────

        bool Refused(ConsoleKey k)
        {
            if (director != null && k.shown && !k.disabled) return false;
            if (k.visual != null) k.visual.FlashRefused();
            return true;
        }

        /// <summary>After a place request: if nothing came of it, say so on the key (the status line has the words).</summary>
        void AfterPlaceRequest(ConsoleKey k, WorkspaceMode wanted)
        {
            if (director.Mode == wanted || director.IsPreviewing || SpatialWorkspaceState.IsTransitioning) return;
            if (k.visual != null) k.visual.FlashRefused();
        }

        public void PressDesk()
        {
            if (Refused(deskKey)) return;
            if (guide != null) guide.CancelGuide();       // a guided Floor / Ceiling step is dropped; nothing had moved
            director.RequestDesk();
            AfterPlaceRequest(deskKey, WorkspaceMode.Desk);
        }

        public void PressFloor()
        {
            if (Refused(floorKey)) return;
            if (guide != null && guide.TryBegin(WorkspaceMode.Floor)) return;   // guided: the request follows the user's gaze
            director.RequestFloor();                  // also "Send back" while a Floor office is parked at the desk
            AfterPlaceRequest(floorKey, WorkspaceMode.Floor);
        }

        public void PressCeiling()
        {
            if (Refused(ceilingKey)) return;
            if (guide != null && guide.TryBegin(WorkspaceMode.Ceiling)) return;
            director.RequestCeiling();
            AfterPlaceRequest(ceilingKey, WorkspaceMode.Ceiling);
        }

        /// <summary>The shared preview keys act on the wall while a wall preview is open (the table's wallContext).</summary>
        bool WallContext => _applied && _keys.wallContext && ResolveHost() != null;

        public void PressNext()
        {
            if (Refused(nextKey)) return;
            if (WallContext) { _host.WallNextSpot(); return; }
            // Floor preview: the next detected spot, or (after a drag) back to a scored one, as the guide card's key.
            if (_floorPreview && director.IsPreviewing) director.ChooseAnotherSpot();
            else director.NextCandidate();
        }

        public void PressConfirm()
        {
            if (Refused(confirmKey)) return;
            if (WallContext)
            {
                _host.WallPlace();
                if (_host.WallPreviewing && confirmKey.visual != null) confirmKey.visual.FlashRefused();   // still choosing: refused (the status says why)
                return;
            }
            director.ConfirmPlacement();
            // Still previewing and nothing moving: the spot was refused (the status line says why).
            if (director.IsPreviewing && !SpatialWorkspaceState.IsTransitioning && confirmKey.visual != null)
                confirmKey.visual.FlashRefused();
        }

        public void PressCancel()
        {
            if (Refused(cancelKey)) return;
            if (WallContext) { _host.WallCancel(); return; }
            director.CancelPlacement();
        }

        /// <summary>The fourth place: open the wall preview (from the desk only; nothing moves until Place).</summary>
        public void PressWall()
        {
            if (Refused(wallKey)) return;
            IWallModeHost h = ResolveHost();
            if (h == null) { if (wallKey.visual != null) wallKey.visual.FlashRefused(); return; }
            if (h.WallPreviewing || h.WallActive) return;                     // already there, or choosing a spot
            if (guide != null) guide.CancelGuide();                           // a guided Floor / Ceiling step is dropped; nothing had moved
            director.RequestMode(WorkspaceMode.Wall);
            if (!h.WallPreviewing && !h.WallActive && wallKey.visual != null) wallKey.visual.FlashRefused();
        }

        /// <summary>Wall preview: Full / Compact.</summary>
        public void PressLayout() { if (!Refused(layoutKey) && ResolveHost() != null) _host.WallToggleLayout(); }
        /// <summary>Wall at rest: choose a new wall spot (a preview; the habitat stays until Place).</summary>
        public void PressMove() { if (!Refused(moveKey) && ResolveHost() != null) _host.WallMove(); }
        public void PressSelect() { if (!Refused(selectKey) && ResolveHost() != null) _host.WallSelect(); }
        public void PressManager() { if (!Refused(managerKey) && ResolveHost() != null) _host.WallManager(); }

        public void PressStop() { if (!Refused(stopKey)) director.StopMove(); }

        /// <summary>Ceiling mode: Lower when the pod is docked, Raise when it is down.</summary>
        public void PressPod()
        {
            if (Refused(podKey)) return;
            if (_podDown) director.SendPodUp(); else director.BringPodDown();
        }

        public void PressBring()
        {
            if (Refused(bringKey)) return;
            if (summon != null) summon.PressBring(); else SummonRequests.RaiseBringOfficeHere();
        }

        public void PressStatus()
        {
            if (Refused(statusKey)) return;
            if (summon != null) summon.PressStatus(); else SummonRequests.RaiseShowLatestUpdate();
        }

        // ───────────────────────── drivers ─────────────────────────

        /// <summary>Play Mode driver: one line per key (shown, disabled, pokeable, words) and the status line.</summary>
        public string Describe()
        {
            EnsureInit();
            string[] names = { "Desk", "Floor", "Ceiling", "Wall", "Next", "Confirm", "Cancel", "Stop", "Pod", "Bring", "Status",
                               "Layout", "Move", "Select", "Manager" };
            StringBuilder sb = new StringBuilder(400);
            sb.Append("ModeConsole mode=").Append(_mode).Append(" active=").Append(_active)
              .Append(" status='").Append(_status).Append("'");
            for (int i = 0; i < _all.Length; i++)
            {
                ConsoleKey k = _all[i];
                sb.Append("\n  ").Append(names[i]).Append(": ");
                if (k.root == null) { sb.Append("MISSING"); continue; }
                sb.Append(k.shown ? "shown" : "hidden").Append(k.disabled ? " disabled" : "")
                  .Append(k.poke != null ? (k.poke.enabled ? " poke-on" : " poke-off") : " no-poke")
                  .Append(" scale ").Append(k.root.localScale.x.ToString("F4"))
                  .Append(" '").Append(k.label != null ? k.label.text.Replace("\n", " / ") : "").Append("'");
            }
            return sb.ToString();
        }

        /// <summary>Play Mode driver: press a key by name as its poke would (Desk, Floor, Ceiling, Wall,
        /// Next, Confirm / Place, Cancel, Stop, Pod, Bring, Status, Layout, Move, Select, Manager). False for an unknown name.</summary>
        public bool PressByName(string key)
        {
            switch (key)
            {
                case "Desk": PressDesk(); return true;
                case "Floor": PressFloor(); return true;
                case "Ceiling": PressCeiling(); return true;
                case "Wall": PressWall(); return true;
                case "Layout": PressLayout(); return true;
                case "Move": PressMove(); return true;
                case "Select": PressSelect(); return true;
                case "Manager": PressManager(); return true;
                case "Next": PressNext(); return true;
                case "Place":
                case "Confirm": PressConfirm(); return true;
                case "Cancel": PressCancel(); return true;
                case "Stop": PressStop(); return true;
                case "Pod": PressPod(); return true;
                case "Bring": PressBring(); return true;
                case "Status": PressStatus(); return true;
                default: return false;
            }
        }
    }
}
