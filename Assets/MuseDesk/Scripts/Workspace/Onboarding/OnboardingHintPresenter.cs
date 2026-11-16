using System.Collections.Generic;
using TMPro;
using UnityEngine;
using MuseDesk.Agents;
using MuseDesk.Config;
using MuseDesk.Documents;
using MuseDesk.Motion;
using MuseDesk.Networking;
using MuseDesk.Spatial;
using MuseDesk.Spatial.Onboarding;
using MuseDesk.Spatial.Ui;

namespace MuseDesk.Workspace
{
    /// <summary>
    /// A15 first-use hints. Feeds the pure <see cref="OnboardingCoach"/> with events that really happened (snapshot, decision
    /// requested/sent/resolved, work delivered, run started, paper grabbed, details opened, agent touched, Focus, Calm
    /// motion, the office moving mode) and shows at most ONE small world-space hint card, anchored to the right object,
    /// with a dismiss dot (a fingertip rests on it). It never shows a hint on a timer of its own, never claims a result, and
    /// its copy is the catalogue's (nine words or fewer, no "AI").
    ///
    /// Inert unless the release flag <c>onboardingHints</c> is on (0 in the committed release): no subscriptions, no card,
    /// no PlayerPrefs reads. The card is a plate, two TMP labels and a dot made at runtime from two shared materials; it is
    /// hidden by scale (never exactly zero) with the panelAppear / panelHide motion tokens, or snapped under Reduced
    /// Motion. Per-frame work allocates nothing.
    /// </summary>
    [DisallowMultipleComponent]
    public class OnboardingHintPresenter : MonoBehaviour
    {
        [Header("Wiring (builder fills these; auto-found when empty)")]
        public SessionController controller;
        public TMP_FontAsset font;
        [Tooltip("Unlit Paper plate material (shared, never instanced).")]
        public Material plateMaterial;
        [Tooltip("Unlit Teal material for the dismiss dot.")]
        public Material dotMaterial;

        [Header("Card")]
        public float cardWidth = 0.13f;
        [Tooltip("Gap between the anchor's top and the card's bottom edge (m).")]
        public float anchorGap = 0.03f;
        public float dotSize = 0.014f;
        [Tooltip("Seconds a fingertip must rest on the dot to dismiss.")]
        public float dismissDwell = 0.12f;
        [Tooltip("Reach window from the eye (m): the card is never nearer or farther than this.")]
        public Vector2 reach = new Vector2(0.30f, 0.75f);

        public OnboardingCoach Coach { get { return _coach; } }
        public bool Active { get { return _on; } }

        static readonly List<OnboardingHintPresenter> All = new List<OnboardingHintPresenter>();
        static readonly List<Renderer> RendererScratch = new List<Renderer>(32);

        OnboardingCoach _coach;
        readonly ManualOnboardingClock _clock = new ManualOnboardingClock();
        readonly RiseEdge _runEdge = new RiseEdge();
        readonly ChangeEdge _modeEdge = new ChangeEdge();
        readonly HandoffTracker _handoff = new HandoffTracker();
        readonly PresenceFader _fader = new PresenceFader(false);

        SessionReducer _reducer;
        bool _on, _boundSession;
        OnboardingFlags _flags;
        SpatialWorkspaceDirector _director;
        float _nextDirectorLookup;

        // Card objects.
        Transform _card, _dot;
        Renderer _plateR, _dotR, _closeR;
        TMP_Text _text, _close;
        float _w, _h;
        bool _built;

        // Anchor.
        Transform _anchor;
        HintAnchorKind _anchorKind;
        HintId _anchorHint = HintId.None;
        float _nextAnchorTry;
        Vector3 _target, _pos;
        bool _havePos;
        float _dwell;
        bool _dotArmed;

        void OnEnable()
        {
            All.Add(this);
            Evaluate();
        }

        void OnDisable()
        {
            All.Remove(this);
            Deactivate();
        }

        /// <summary>Re-reads the release flag on every presenter (ReleaseConfigApplier.Apply calls this).</summary>
        public static void RefreshAll()
        {
            for (int i = 0; i < All.Count; i++) if (All[i] != null) All[i].Evaluate();
        }

        static bool FlagOn()
        {
            var c = ReleaseConfigApplier.Active;
            return c != null && c.onboardingHints;
        }

        void Evaluate()
        {
            if (FlagOn() && isActiveAndEnabled) Activate();
            else Deactivate();
        }

        // ---------------------------------------------------------------- activation

        void Activate()
        {
            var c = ReleaseConfigApplier.Active;
            if (c != null)
                _flags = OnboardingFlagMap.Map(c.floorMode, c.ceilingMode, c.reducedMotionToggle,
                    c.wallHabitat, c.wallManager, c.agentSelect, c.largeAgentPortal);
            if (_on) return;
            _on = true;
            if (_coach == null)
            {
                _coach = new OnboardingCoach(_clock, new PlayerPrefsOnboardingStore());
                _coach.HintShown += OnHintShown;
                _coach.HintEnded += OnHintEnded;
            }
            HeldPaperController.PaperGrabbed += OnPaperGrabbed;
            DecisionPanel.DetailsOpened += OnDetailsOpened;
            AgentTouchSummary.Touched += OnAgentTouched;
            FocusModeController.FocusChanged += OnFocusChanged;
            MotionToggleKey.Toggled += OnCalmToggled;
            MuseDesk.Wall.WallEvents.ManagerPlanShown += OnPlanShown;
            MuseDesk.Wall.WallEvents.ManagerPlanApproved += OnPlanApproved;
            MuseDesk.Wall.WallEvents.AgentSelected += OnAgentPicked;
            MuseDesk.Wall.WallEvents.PortalOpened += OnPortalOpened;
            _runEdge.Reset(); _modeEdge.Reset(); _handoff.Reset();
        }

        void Deactivate()
        {
            if (!_on) return;
            _on = false;
            HeldPaperController.PaperGrabbed -= OnPaperGrabbed;
            DecisionPanel.DetailsOpened -= OnDetailsOpened;
            AgentTouchSummary.Touched -= OnAgentTouched;
            FocusModeController.FocusChanged -= OnFocusChanged;
            MotionToggleKey.Toggled -= OnCalmToggled;
            MuseDesk.Wall.WallEvents.ManagerPlanShown -= OnPlanShown;
            MuseDesk.Wall.WallEvents.ManagerPlanApproved -= OnPlanApproved;
            MuseDesk.Wall.WallEvents.AgentSelected -= OnAgentPicked;
            MuseDesk.Wall.WallEvents.PortalOpened -= OnPortalOpened;
            UnbindSession();
            // Hide any visible card without touching the persisted progress.
            if (_built) { _fader.Snap(false); ApplyFactor(); }
            if (_coach != null) _coach.SetContext(new OnboardingContext());
            _coach = null;   // a fresh coach next time; it reads the same store
        }

        // ---------------------------------------------------------------- session binding

        void TryBindSession()
        {
            if (_boundSession) return;
            if (controller == null)
            {
                controller = FindAnyObjectByType<SessionController>();
                if (controller == null) return;
            }
            _reducer = controller.Reducer;
            if (_reducer == null) return;
            _reducer.SnapshotLoaded += OnSnapshotLoaded;
            _reducer.DecisionRequested += OnDecisionRequested;
            _reducer.DecisionResolved += OnDecisionResolved;
            _reducer.WorkDelivered += OnWorkDelivered;
            _reducer.AgentChanged += OnAgentChanged;
            controller.RunStateChanged += OnRunStateChanged;
            controller.DecisionSent += OnDecisionSent;
            _boundSession = true;

            // Already running when the flag came on: report what is true now.
            if (controller.Initialized)
            {
                Obs(OnboardingEvent.SessionReady);
                var pending = _reducer.FirstPendingDecision();
                if (pending != null) { _handoff.DecisionRequested(pending.AgentId); Obs(OnboardingEvent.DecisionRequested); }
                OnRunStateChanged();
            }
        }

        void UnbindSession()
        {
            if (!_boundSession) return;
            if (_reducer != null)
            {
                _reducer.SnapshotLoaded -= OnSnapshotLoaded;
                _reducer.DecisionRequested -= OnDecisionRequested;
                _reducer.DecisionResolved -= OnDecisionResolved;
                _reducer.WorkDelivered -= OnWorkDelivered;
                _reducer.AgentChanged -= OnAgentChanged;
            }
            if (controller != null)
            {
                controller.RunStateChanged -= OnRunStateChanged;
                controller.DecisionSent -= OnDecisionSent;
            }
            _reducer = null;
            _boundSession = false;
        }

        // ---------------------------------------------------------------- event adapters (each one is a real event)

        void Obs(OnboardingEvent e)
        {
            if (_coach == null) return;
            _clock.Time = Time.unscaledTimeAsDouble;
            _coach.Observe(e);
        }

        void OnSnapshotLoaded() { Obs(OnboardingEvent.SessionReady); }

        void OnDecisionRequested(string decisionId, MuseDesk.Models.EventEnvelope e)
        {
            _handoff.DecisionRequested(e != null ? e.AgentId : null);
            Obs(OnboardingEvent.DecisionRequested);
        }

        void OnDecisionResolved(string decisionId, string state)
        {
            _handoff.DecisionResolved(state);
            if (string.Equals(state, "approved", System.StringComparison.OrdinalIgnoreCase))
                Obs(OnboardingEvent.DecisionResolvedApproved);
        }

        void OnAgentChanged(MuseDesk.Models.AgentView a)
        {
            if (a != null && _handoff.AgentChanged(a.AgentId)) Obs(OnboardingEvent.HandoffObserved);
        }

        // Work that was really delivered (it references an artifact). Bookkeeping completions do not start the receipt hint.
        void OnWorkDelivered(MuseDesk.Models.EventEnvelope e) { Obs(OnboardingEvent.TaskCompleted); }

        void OnRunStateChanged()
        {
            if (controller != null && _runEdge.Update(controller.RunInProgress)) Obs(OnboardingEvent.TaskStarted);
        }

        void OnDecisionSent(string decisionId, string action) { Obs(OnboardingEvent.DecisionSent); }
        void OnPaperGrabbed(HeldPaperController p) { Obs(OnboardingEvent.PaperGrabbed); }
        void OnDetailsOpened() { Obs(OnboardingEvent.PaperDetailsOpened); }
        void OnAgentTouched(AgentTouchSummary a) { Obs(OnboardingEvent.AgentTouched); }
        void OnFocusChanged(bool on) { if (on) Obs(OnboardingEvent.FocusEntered); }
        void OnCalmToggled(bool on) { Obs(OnboardingEvent.CalmMotionToggled); }
        void OnPlanShown() { Obs(OnboardingEvent.ManagerPlanShown); }
        void OnPlanApproved() { Obs(OnboardingEvent.ManagerPlanApproved); }
        void OnAgentPicked() { Obs(OnboardingEvent.AgentSelected); }
        void OnPortalOpened() { Obs(OnboardingEvent.PortalOpened); }

        // The office changing mode (Desk/Floor/Ceiling/Wall) is polled: the director has no event, and the first value is
        // only a baseline so the starting mode never counts as the user moving it.
        void PollMode()
        {
            if (_director == null)
            {
                if (Time.unscaledTime < _nextDirectorLookup) return;
                _nextDirectorLookup = Time.unscaledTime + 1f;
                _director = FindAnyObjectByType<SpatialWorkspaceDirector>(FindObjectsInactive.Include);
                if (_director == null) return;
            }
            var mode = _director.Mode;
            if (_modeEdge.Update((int)mode))
            {
                Obs(OnboardingEvent.WorkspaceMoved);
                if (mode == WorkspaceMode.Wall) Obs(OnboardingEvent.WallPlaced);
            }
        }

        // ---------------------------------------------------------------- per frame

        void Update()
        {
            if (!_on) return;
            if (!_boundSession) TryBindSession();
            _clock.Time = Time.unscaledTimeAsDouble;
            PollMode();

            var reg = HandClaimRegistry.Shared;
            var ctx = new OnboardingContext
            {
                FocusActive = FocusModeController.Active,
                HandsBusy = reg.Owner(0) != HandClaim.None || reg.Owner(1) != HandClaim.None,
                ReducedMotion = MotionSettings.ReducedMotion,
                Flags = _flags,
            };
            _coach.SetContext(ctx);
            _coach.Tick();

            if (!_built) return;
            if (_fader.Moving) { _fader.Step(Time.unscaledDeltaTime); ApplyFactor(); }
            if (_coach.IsShowing || !_fader.IsLeaving) FollowAnchor();
            if (_coach.IsShowing && _fader.Interactable) PollDismiss();
        }

        // ---------------------------------------------------------------- show / hide

        void OnHintShown(HintDef d)
        {
            EnsureBuilt();
            if (!_built) return;
            _text.text = d.Text;
            _anchorKind = d.Anchor;
            _anchorHint = d.Id;
            _anchor = null; _nextAnchorTry = 0f;
            ResolveAnchor();
            _target = AnchorTop(out Vector3 firstTop) ? firstTop : transform.position;   // for the viewing distance only
            Layout();
            _havePos = false;
            _dwell = 0f; _dotArmed = false;
            ComputeTarget();
            _pos = _target; _havePos = true;
            PlaceCard(_pos);
            _fader.Show(MuseMotionProfile.Active.panelAppear, _coach.Animate);
            ApplyFactor();
        }

        void OnHintEnded(HintDef d, HintEndReason reason)
        {
            if (!_built) return;
            _fader.Hide(MuseMotionProfile.Active.panelHide, _coach != null && _coach.Animate);
            ApplyFactor();
        }

        void ApplyFactor()
        {
            float k = _fader.Factor;
            _card.localScale = new Vector3(k, k, k);
            bool vis = _fader.State != PresenceFader.Phase.Hidden;
            if (_plateR.enabled != vis) { _plateR.enabled = vis; _dotR.enabled = vis; _text.enabled = vis; _close.enabled = vis; }
        }

        // ---------------------------------------------------------------- dismiss poke (fingertip rests on the dot)

        void PollDismiss()
        {
            IReadOnlyList<Transform> tips = HandFingertips.Get();
            bool touching = false;
            Vector3 c = _dot.position;
            float r = dotSize * 0.75f;
            float r2 = r * r;
            for (int i = 0; i < tips.Count; i++)
            {
                var t = tips[i];
                if (t != null && (t.position - c).sqrMagnitude <= r2) { touching = true; break; }
            }
            // A finger already resting there when the card appeared must lift first.
            if (!touching) { _dotArmed = true; _dwell = 0f; return; }
            if (!_dotArmed) return;
            _dwell += Time.unscaledDeltaTime;
            if (_dwell >= dismissDwell) { _dwell = 0f; _coach.Dismiss(); }
        }

        // ---------------------------------------------------------------- card construction

        void EnsureBuilt()
        {
            if (_built) return;
            if (plateMaterial == null || dotMaterial == null)
            {
                Debug.LogWarning("[MuseDesk.Onboarding] presenter has no plate/dot material; run AgentScripts/BuildOnboardingHintsV1. Hints stay off.");
                return;
            }
            var root = new GameObject("OnboardingHintCard");
            root.transform.SetParent(transform, false);
            _card = root.transform;
            _plateR = MakeQuad("Plate", _card, plateMaterial);
            _dot = new GameObject("Dismiss").transform;
            _dot.SetParent(_card, false);
            _dotR = MakeQuad("Dot", _dot, dotMaterial);
            _text = MakeText("Text", _card, TextAlignmentOptions.MidlineLeft, TypeScale.BodyMm, OfficeTokens.Ink);
            _close = MakeText("Close", _dot, TextAlignmentOptions.Center, TypeScale.CaptionMm, OfficeTokens.Paper);
            _close.text = "x";
            _close.transform.localPosition = new Vector3(0f, 0f, -0.0005f);
            _close.rectTransform.sizeDelta = new Vector2(dotSize * 100f, dotSize * 100f);
            _built = true;
            _fader.Snap(false);
            ApplyFactor();
        }

        Renderer MakeQuad(string name, Transform parent, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = name;
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);          // a hint is never a physical thing
            go.transform.SetParent(parent, false);
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return r;
        }

        TMP_Text MakeText(string name, Transform parent, TextAlignmentOptions align, float mm, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localScale = Vector3.one * 0.01f;
            var t = go.AddComponent<TextMeshPro>();
            if (font != null) t.font = font;
            t.fontSize = mm;
            t.color = color;
            t.alignment = align;
            t.enableAutoSizing = false;
            t.textWrappingMode = TextWrappingModes.Normal;
            t.overflowMode = TextOverflowModes.Overflow;
            t.text = "";
            return t;
        }

        // Sizes the card for the viewing distance (TypeScale), wraps the text, places the dot at the top right.
        void Layout()
        {
            Transform eye = Eye();
            float dist = eye != null ? Vector3.Distance(eye.position, _target) : 0.5f;
            if (dist < 0.2f) dist = 0.5f;
            float mm = Mathf.Clamp(TypeScale.ComfortMmAt(dist), TypeScale.BodyMm, 9f);
            _text.fontSize = mm;

            const float pad = 0.007f;
            float gutter = dotSize + 0.008f;
            _w = mm > 7f ? cardWidth * 1.25f : cardWidth;
            float textW = _w - pad - gutter;                          // metres
            _text.rectTransform.sizeDelta = new Vector2(textW * 100f, 50f);
            float prefH = _text.GetPreferredValues(_text.text, textW * 100f, 0f).y * 0.01f;   // cm -> m
            _h = Mathf.Max(0.028f, prefH + pad * 1.6f);
            _text.rectTransform.sizeDelta = new Vector2(textW * 100f, prefH * 100f);

            _plateR.transform.localScale = new Vector3(_w, _h, 1f);
            _text.transform.localPosition = new Vector3(-_w * 0.5f + pad + textW * 0.5f, 0f, -0.0005f);   // 0.5 mm proud
            _dot.localPosition = new Vector3(_w * 0.5f - gutter * 0.5f - 0.002f, _h * 0.5f - dotSize * 0.5f - 0.004f, -0.0005f);
            _dotR.transform.localScale = new Vector3(dotSize, dotSize, 1f);
        }

        // ---------------------------------------------------------------- anchoring

        static Transform Eye()
        {
            var cam = Camera.main;
            return cam != null ? cam.transform : null;
        }

        void ResolveAnchor()
        {
            _nextAnchorTry = Time.unscaledTime + 0.5f;
            Transform found = null;
            switch (_anchorKind)
            {
                case HintAnchorKind.Agent: found = NearestAgent(); break;
                case HintAnchorKind.Paper:
                {
                    var p = FindAnyObjectByType<HeldPaperController>();
                    if (p != null) found = p.transform;
                    break;
                }
                case HintAnchorKind.Stamp: found = ByName("StampApprove", "Doc_StampRestPad"); break;
                case HintAnchorKind.Board: found = ByName("TaskBoard"); break;
                case HintAnchorKind.Plaque:
                    found = _anchorHint == HintId.ReceiptDelivered ? ByName("CompletionReceipt", "ModePlaque") : ByName("ModePlaque");
                    break;
            }
            _anchor = found;
        }

        static Transform ByName(params string[] names)
        {
            for (int i = 0; i < names.Length; i++)
            {
                var go = GameObject.Find(names[i]);
                if (go != null) return go.transform;
            }
            return null;
        }

        static Transform NearestAgent()
        {
            var eye = Eye();
            Transform best = null; float bd = float.MaxValue;
            var agents = FindObjectsByType<MuseDesk.Agents.AgentView>(FindObjectsSortMode.None);
            for (int i = 0; i < agents.Length; i++)
            {
                if (agents[i] == null) continue;
                float d = eye != null ? (agents[i].transform.position - eye.position).sqrMagnitude : 0f;
                if (d < bd) { bd = d; best = agents[i].transform; }
            }
            return best;
        }

        // The top-centre of what the anchor draws, so the card sits above it and never over the question text or the stamp.
        bool AnchorTop(out Vector3 top)
        {
            top = default;
            if (_anchor == null || !_anchor.gameObject.activeInHierarchy) return false;
            RendererScratch.Clear();
            _anchor.GetComponentsInChildren(false, RendererScratch);
            bool any = false; Bounds b = default;
            for (int i = 0; i < RendererScratch.Count; i++)
            {
                var r = RendererScratch[i];
                if (r == null || !r.enabled || r.transform.IsChildOf(_card)) continue;
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
            RendererScratch.Clear();
            top = any ? new Vector3(b.center.x, b.max.y, b.center.z) : _anchor.position;
            return true;
        }

        void ComputeTarget()
        {
            Transform eye = Eye();
            Vector3 p;
            if (AnchorTop(out Vector3 top)) p = top + Vector3.up * (anchorGap + _h * 0.5f);
            else if (eye != null)
            {
                Vector3 f = eye.forward; f.y = 0f;
                f = f.sqrMagnitude > 1e-4f ? f.normalized : Vector3.forward;
                p = eye.position + f * 0.5f + Vector3.down * 0.12f;      // no anchor found: low in front of the user
            }
            else p = transform.position;
            if (eye != null) p = PanelLayoutMath.ClampToReach(p, eye.position, eye.forward, reach.x, reach.y);
            _target = p;
        }

        void FollowAnchor()
        {
            if ((_anchor == null || !_anchor.gameObject.activeInHierarchy) && Time.unscaledTime >= _nextAnchorTry) ResolveAnchor();
            ComputeTarget();
            if (!_havePos || !_coach.Animate) _pos = _target;
            else _pos = Vector3.Lerp(_pos, _target, PanelLayoutMath.Smooth(Time.unscaledDeltaTime, 0.12f));
            _havePos = true;
            PlaceCard(_pos);
        }

        void PlaceCard(Vector3 pos)
        {
            Transform eye = Eye();
            Quaternion rot = _card.rotation;
            if (eye != null)
            {
                // TMP and quads show on local -Z, so +Z points away from the eye.
                Vector3 away = pos - eye.position; away.y = 0f;
                if (away.sqrMagnitude > 1e-6f) rot = Quaternion.LookRotation(away.normalized, Vector3.up);
            }
            _card.SetPositionAndRotation(pos, rot);
        }

        // ---------------------------------------------------------------- driver / report

        /// <summary>One line for the Play Mode driver.</summary>
        public string Describe()
        {
            string cur = _coach != null && _coach.Current != null ? _coach.Current.Id.ToString() : "none";
            return "on=" + _on + " bound=" + _boundSession + " showing=" + cur + " fader=" + _fader.State + " flags=" + _flags;
        }
    }
}
