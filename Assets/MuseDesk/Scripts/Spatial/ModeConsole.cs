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
    /// <see cref="SummonController"/>. The state table is <see cref="ModeConsoleLogic.Compute"/>.
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

        [Header("Place selector")]
        public ConsoleKey deskKey = new ConsoleKey();
        public ConsoleKey floorKey = new ConsoleKey();
        public ConsoleKey ceilingKey = new ConsoleKey();
        [Tooltip("Cap material of a place key that is not the current place.")]
        public Material segmentIdle;
        [Tooltip("Cap material of the current place.")]
        public Material segmentActive;
        [Tooltip("The bars under the three place keys (Desk, Floor, Ceiling).")]
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

        [Header("Builder bookkeeping (for Revert)")]
        public Transform focusKey;
        public Transform focusOriginalParent;
        public Vector3 focusOriginalLocalPosition;
        public Quaternion focusOriginalLocalRotation = Quaternion.identity;
        public Vector3 focusOriginalLocalScale = Vector3.one;
        public int focusOriginalSibling;
        public Behaviour focusBillboardDisabled;

        ConsoleKey[] _all;
        ConsoleKey[] _segments;
        readonly ScaleEase[] _indicatorEase = { new ScaleEase(), new ScaleEase(), new ScaleEase() };
        bool _inited, _applied, _animating, _pokeDirty, _sendsBack, _podDown;
        bool _floorPreview, _placeReady = true;
        int _active = -1, _armFrame;
        string _status, _podWords, _floorShown;
        ModeConsoleKeys _keys;
        PlacementMode _mode;
        MovablePanel _panel;

        /// <summary>True while the Stop key is on show.</summary>
        public bool StopShown => _applied && _keys.stop;
        /// <summary>True while the Bring or Status key is on show.</summary>
        public bool SummonKeysShown => _applied && (_keys.bring || _keys.status);
        /// <summary>0 Desk, 1 Floor, 2 Ceiling: the lit place key.</summary>
        public int ActiveSegment => _active;

        // ───────────────────────── lifecycle ─────────────────────────

        void Awake() { EnsureInit(); }

        void EnsureInit()
        {
            if (_inited) return;
            _inited = true;
            _segments = new[] { deskKey, floorKey, ceilingKey };
            _all = new[] { deskKey, floorKey, ceilingKey, nextKey, confirmKey, cancelKey, stopKey, podKey, bringKey, statusKey };
            for (int i = 0; i < _all.Length; i++)
            {
                ConsoleKey k = _all[i];
                if (k == null) { _all[i] = k = new ConsoleKey(); }
                k.ease = new ScaleEase();
                k.shown = i < 3;                                   // the selector is always on show
                if (k.root != null) k.ease.Init(k.root, k.shownScale, easeSeconds, k.shown);
            }
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

        void OnPokesResumed() { _pokeDirty = true; }

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
            string c = floorPreview ? placeHereWords : confirmWords;
            if (confirmKey.label != null && confirmKey.label.text != c) confirmKey.label.text = c;
            string n = floorPreview ? otherSpotWords : nextWords;
            if (nextKey.label != null && nextKey.label.text != n) nextKey.label.text = n;
        }

        /// <summary>Lay the console out for the current state. Called on state changes only.</summary>
        public void Apply(bool floorEnabled, bool ceilingEnabled, PlacementMode mode, bool previewing, bool canCycle,
                          bool transitioning, CeilingPodState podState, bool podWaiting, string status)
        {
            EnsureInit();
            _applied = true;
            _mode = mode;
            _podDown = podState == CeilingPodState.Engaged;
            _keys = ModeConsoleLogic.Compute(floorEnabled, ceilingEnabled, mode, previewing, canCycle, transitioning, podState, podWaiting,
                                             _floorPreview && previewing, _placeReady);

            SetActiveSegment(_keys.active);
            SetDisabled(deskKey, _keys.deskDisabled);
            SetDisabled(floorKey, _keys.floorDisabled);
            SetDisabled(ceilingKey, _keys.ceilingDisabled);

            SetShown(nextKey, _keys.next);
            SetShown(confirmKey, _keys.confirm);
            SetShown(cancelKey, _keys.cancel);
            SetShown(stopKey, _keys.stop);
            SetShown(podKey, _keys.pod);
            SetShown(bringKey, _keys.bring);
            SetShown(statusKey, _keys.status);

            if (podKey.label != null && _keys.podWords != _podWords)
            {
                _podWords = _keys.podWords;
                podKey.label.text = _podWords;
            }

            string line = status;
            if (string.IsNullOrEmpty(line))
                line = mode == PlacementMode.Floor ? floorModeWords : mode == PlacementMode.Ceiling ? ceilingModeWords : deskWords;
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

        void Update()
        {
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

        public void PressNext() { if (!Refused(nextKey)) director.NextCandidate(); }

        public void PressConfirm()
        {
            if (Refused(confirmKey)) return;
            director.ConfirmPlacement();
            // Still previewing and nothing moving: the spot was refused (the status line says why).
            if (director.IsPreviewing && !SpatialWorkspaceState.IsTransitioning && confirmKey.visual != null)
                confirmKey.visual.FlashRefused();
        }

        public void PressCancel() { if (!Refused(cancelKey)) director.CancelPlacement(); }

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
            string[] names = { "Desk", "Floor", "Ceiling", "Next", "Confirm", "Cancel", "Stop", "Pod", "Bring", "Status" };
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

        /// <summary>Play Mode driver: press a key by name as its poke would (Desk, Floor, Ceiling,
        /// Next, Confirm, Cancel, Stop, Pod, Bring, Status). False for an unknown name.</summary>
        public bool PressByName(string key)
        {
            switch (key)
            {
                case "Desk": PressDesk(); return true;
                case "Floor": PressFloor(); return true;
                case "Ceiling": PressCeiling(); return true;
                case "Next": PressNext(); return true;
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
