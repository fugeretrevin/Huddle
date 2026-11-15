using System;
using System.Reflection;
using TMPro;
using UnityEngine;
using MuseDesk.Phase2.Contracts;

namespace MuseDesk.Phase2.Board
{
    /// <summary>
    /// A single physical task card on the board (P2-01). Pure view + data holder: it renders a
    /// TaskCardV2 as a title, one state line in plain words, a role chip and the LIVE / SCRIPTED DEMO
    /// mode words (always shown: honesty invariant), and exposes grab state. Priority appears only
    /// when it is not the default, as a word. The id and revision are not on the card face. The drag
    /// state machine lives in TaskDragController; the authoritative task truth always comes from the
    /// Phase2Reducer, never from this view.
    ///
    /// Grab state is read by REFLECTION off the project's Oculus.Interaction.Grabbable
    /// (SelectingPointsCount), mirroring the existing MuseDesk.Workspace.GrabbableRigidbody /
    /// GrabReadout pattern, so this runtime assembly keeps NO hard ISDK dependency and cannot break
    /// if the package version shifts. The Grabbable + HandGrabInteractable components are added to
    /// the card prefab by the integrator (Editor builder / ISDK Quick Actions) and assigned here.
    /// </summary>
    public class TaskCardView : MonoBehaviour
    {
        [Header("Text (TMP)")]
        public TMP_Text titleLabel;
        public TMP_Text idRevisionLabel;   // no longer on the card face: kept empty (reference detail)
        public TMP_Text roleLabel;         // assignee role or "Unassigned"
        public TMP_Text priorityLabel;     // a word ("High"), only when the priority is not the default
        public TMP_Text stateLabel;        // one state line in plain words
        public TMP_Text sourceLabel;       // "LIVE" / "SCRIPTED DEMO": always visible (invariant #1)

        [Header("Optional visuals")]
        [Tooltip("Shown while the card is inspected/expanded (pinch to expand). Optional.")]
        public GameObject detailsRoot;
        [Tooltip("Status marker renderer tinted by task state. Optional.")]
        public Renderer statusMarker;
        [Tooltip("Colour the role and priority words to suit the role strip under them (light text, or " +
                 "dark text on the Tester's amber). Set by the builder only when the strip is role-tinted.")]
        public bool tintHeaderText;

        [Header("Interaction (set by the Editor builder / ISDK Quick Actions)")]
        [Tooltip("The Oculus.Interaction.Grabbable on this card. Read by reflection only.")]
        public Component grabbable;

        [Header("Physical edge and states (wired by AgentScripts/BuildBoardV3)")]
        [Tooltip("The darker backing plate behind the card face. Its colour and size show the card's " +
                 "state: idle, hover, selected / held, pending, refused. Optional.")]
        public Renderer edgePlate;
        [Tooltip("The card's grab hover highlight: read for the hover state. Optional.")]
        public MuseDesk.Workspace.GrabHoverHighlight hover;
        [Tooltip("How much the edge plate grows (x, y, relative to its resting scale) for the thicker hover border.")]
        public Vector2 edgeHoverGrow = new Vector2(1.02f, 1.03f);
        [Tooltip("How much the edge plate grows (x, y) for the thick selected / held / pending / refused border.")]
        public Vector2 edgeBoldGrow = new Vector2(1.04f, 1.06f);
        public Color edgeIdleColor = new Color(0.165f, 0.184f, 0.227f);      // housing #2A2F3A
        public Color edgeActiveColor = new Color(0.184f, 0.639f, 0.604f);    // teal    #2FA39A
        public Color edgePendingColor = new Color(0.878f, 0.627f, 0.188f);   // amber   #E0A030
        public Color edgeRefusedColor = new Color(0.851f, 0.325f, 0.290f);   // coral   #D9534A
        [Tooltip("How far a selected card comes toward the reader, in metres (world).")]
        public float selectedLift = 0.004f;
        [Tooltip("Scale of a selected card relative to its resting scale.")]
        public float selectedScale = 1.04f;
        [Tooltip("Seconds a let-go card takes to ease to its lane slot (0 = jump).")]
        public float settleSeconds = 0.2f;
        [Tooltip("Seconds the refused (coral) border shows before the card goes back to its state.")]
        public float refusedSeconds = 1.2f;

        /// <summary>What the card's edge is showing.</summary>
        public enum EdgeState { Idle, Hover, Selected, Held, Pending, Refused }
        /// <summary>The state the card's edge shows right now.</summary>
        public EdgeState Edge => _edge;
        /// <summary>True while the card is the one the card toolbar acts on.</summary>
        public bool IsSelected { get; private set; }

        // Resting slot on the board (set by BoardPresenter during layout). Stored as a parent + local
        // pose rather than a Transform so ReturnHome is stable even while the card itself is moved by
        // the hand. Never self-reference the card's own transform here.
        Transform _homeParent;
        Vector3 _homeLocalPos;
        Quaternion _homeLocalRot = Quaternion.identity;
        bool _hasHome;

        // Motion: a let-go card eases to where it belongs; a selected card lifts a little.
        Vector3 _baseScale = Vector3.one;
        bool _baseScaleCaptured;
        bool _displaced;                 // moved by a hand (or to a pad) since it last rested in its slot
        bool _parked;                    // sent to a pad / station to wait for the answer to a drop
        bool _settling;
        bool _settleToHome;
        float _settleT;
        Vector3 _settleFromPos, _settleToPos;
        Quaternion _settleFromRot = Quaternion.identity, _settleToRot = Quaternion.identity;
        float _lift;                     // 0 resting .. 1 selected

        // Edge state.
        EdgeState _edge = EdgeState.Idle;
        bool _edgeApplied;
        Vector3 _edgeRest = Vector3.one;
        bool _edgeRestRead;
        bool _pending;
        float _pendingUntil;
        float _refusedUntil;
        bool _simulatedHold;
        MaterialPropertyBlock _edgeBlock;
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int ColorId = Shader.PropertyToID("_Color");

        /// <summary>Raised by the accessible no-drag fallback (poke 'Assign to X'). The presenter
        /// turns this into an idempotent assign command exactly like a physical drop would.</summary>
        public event Action<TaskCardView, string> AssignRequested;   // (card, targetAgentId)

        /// <summary>Poke the pause/resume control: the presenter pauses a running task or resumes a
        /// paused one (idempotent, canonical-reconciled, exactly like assign).</summary>
        public event Action<TaskCardView> PauseResumeRequested;
        /// <summary>Poke the priority chip: the presenter cycles the task priority 0→1→2→3→0.</summary>
        public event Action<TaskCardView> PriorityCycleRequested;

        public TaskCardV2 Data { get; private set; }
        public string TaskId => Data?.TaskId;
        public bool IsExpanded { get; private set; }

        Func<int> _selCount;
        bool _selResolved;

        // Transient command feedback shown in the state line ("Sending…", "Assigned to Developer",
        // "Running: pause it first"), then the canonical state text returns.
        float _noticeUntil;
        bool _noticeActive;
        Color _stateBaseColor;
        bool _stateColorCaptured;
        // Dark enough to read on the paper-coloured card face (nothing lighter than "ink soft" there):
        // ink soft while waiting, a dark green for confirmed, a dark coral for refused. The words
        // carry the meaning; the colour only reinforces it.
        static readonly Color NoticePending = new Color(0.294f, 0.333f, 0.388f);   // #4B5563
        static readonly Color NoticeOk = new Color(0.10f, 0.42f, 0.24f);
        static readonly Color NoticeError = new Color(0.66f, 0.18f, 0.15f);
        static readonly Color RoleTextOnDark = new Color(0.953f, 0.957f, 0.965f);  // #F3F4F6
        static readonly Color RoleTextOnLight = new Color(0.122f, 0.145f, 0.188f); // #1F2530

        /// <summary>Raised whenever any card shows command feedback: (card, words, outcome).
        /// outcome is null while the command is pending, true once the gateway confirmed it, false
        /// when it was refused. Lets the card toolbar, the assignment pads and the task dock repeat
        /// the same words the card shows. Subscribers must unsubscribe in OnDisable.</summary>
        public static event Action<TaskCardView, string, bool?> NoticeShown;

        /// <summary>The command was sent and the backend has not answered yet.</summary>
        public void ShowPending(string text = "Sending")
        {
            Notice(text, NoticePending, 8f);
            _pending = true;                                           // amber edge until the answer
            _pendingUntil = Time.unscaledTime + 8f;
            _refusedUntil = 0f;
            RaiseNotice(text, null);
        }

        /// <summary>The backend answered: confirmed (ok) or rejected with a reason.</summary>
        public void ShowResult(bool ok, string text)
        {
            Notice(text, ok ? NoticeOk : NoticeError, ok ? 2f : 4f);
            _pending = false;
            _refusedUntil = ok ? 0f : Time.unscaledTime + refusedSeconds;   // brief coral edge, then back
            // A card that waited on a pad for this answer now eases to its slot (its new lane after
            // a confirmation, its old one after a refusal).
            if (_parked) ReturnHome();
            RaiseNotice(text, ok);
        }

        void RaiseNotice(string text, bool? outcome)
        {
            var handler = NoticeShown;
            if (handler == null || string.IsNullOrEmpty(text)) return;
            try { handler(this, text, outcome); }
            catch (Exception ex) { Debug.LogError($"[MuseDesk.P2] card notice listener error: {ex.Message}"); }
        }

        void Notice(string text, Color color, float seconds)
        {
            if (stateLabel == null || string.IsNullOrEmpty(text)) return;
            if (!_stateColorCaptured) { _stateBaseColor = stateLabel.color; _stateColorCaptured = true; }
            stateLabel.text = Truncate(text, 40);
            stateLabel.color = color;
            _noticeUntil = Time.unscaledTime + seconds;
            _noticeActive = true;
        }

        void Awake() => CaptureBaseScale();

        void CaptureBaseScale()
        {
            if (_baseScaleCaptured) return;
            _baseScaleCaptured = true;
            _baseScale = transform.localScale;
        }

        void Update()
        {
            float now = Time.unscaledTime;
            bool held = IsHeld;
            // Polish S9b: a hide that waited for the user to let go starts on the first frame the card is free.
            if (_deferredHide.Tick(held)) StartHide();
            UpdateEdge(held, now);
            StepPresence();
            UpdateMotion(held);

            if (!_noticeActive || now < _noticeUntil) return;
            _noticeActive = false;
            if (stateLabel != null)
            {
                if (_stateColorCaptured) stateLabel.color = _stateBaseColor;
                stateLabel.text = Data != null ? StateLine(Data) : "";
            }
        }

        void OnDisable()
        {
            // Pooled cards must not come back showing another task's notice, edge or pose.
            _noticeActive = false;
            if (stateLabel != null && _stateColorCaptured) stateLabel.color = _stateBaseColor;
            bool hideWasWaiting = _deferredHide.Pending;
            ResetPresentation();
            // A card switched off while its hide still waited for a release would never finish leaving: finish it now.
            if (hideWasWaiting && Application.isPlaying) HideFinishedNow();
        }

        // ─── edge: the card's visible physical border and its states ───

        void UpdateEdge(bool held, float now)
        {
            if (_pending && now >= _pendingUntil) _pending = false;    // an answer that never came
            EdgeState want =
                now < _refusedUntil ? EdgeState.Refused
                : _pending ? EdgeState.Pending
                : held ? EdgeState.Held
                : IsSelected ? EdgeState.Selected
                : (hover != null && hover.Highlighted) ? EdgeState.Hover
                : EdgeState.Idle;
            if (_edgeApplied && want == _edge) return;
            ApplyEdge(want);
        }

        void ApplyEdge(EdgeState state)
        {
            _edge = state;
            _edgeApplied = true;
            if (edgePlate == null) return;
            Transform t = edgePlate.transform;
            // The resting size is whatever the scene gives the plate (a plain or a rounded mesh).
            if (!_edgeRestRead) { _edgeRestRead = true; _edgeRest = t.localScale; }
            Color c;
            Vector2 grow;
            switch (state)
            {
                case EdgeState.Hover: c = edgeActiveColor; grow = edgeHoverGrow; break;
                case EdgeState.Selected:
                case EdgeState.Held: c = edgeActiveColor; grow = edgeBoldGrow; break;
                case EdgeState.Pending: c = edgePendingColor; grow = edgeBoldGrow; break;
                case EdgeState.Refused: c = edgeRefusedColor; grow = edgeBoldGrow; break;
                default: c = edgeIdleColor; grow = Vector2.one; break;
            }
            // Property block, never renderer.material: pooled cards share one material.
            if (_edgeBlock == null) _edgeBlock = new MaterialPropertyBlock();
            edgePlate.GetPropertyBlock(_edgeBlock);
            _edgeBlock.SetColor(BaseColorId, c);
            _edgeBlock.SetColor(ColorId, c);
            edgePlate.SetPropertyBlock(_edgeBlock);
            t.localScale = new Vector3(_edgeRest.x * grow.x, _edgeRest.y * grow.y, _edgeRest.z);
        }

        /// <summary>Mark this card as the one the card toolbar acts on (thicker teal edge, a small
        /// lift toward the reader). Presentation only; called by CardActionConsole.</summary>
        public void SetSelected(bool selected) => IsSelected = selected;

        // ─── motion: ease to the lane slot after a drop; lift while selected ───

        void UpdateMotion(bool held)
        {
            if (held)
            {
                // The hand owns the transform. Remember that the card has left its slot.
                _displaced = true;
                _settling = false;
                if (_revealing) { _revealing = false; _revealScale = 1f; }
                if (_baseScaleCaptured) transform.localScale = _baseScale * _hide.Factor;
                _presenceDirty = false;
                return;
            }
            float goal = IsSelected ? 1f : 0f;
            bool liftMoving = _lift != goal;
            if (!_settling && !liftMoving && !_revealing && !_presenceDirty) return;
            _presenceDirty = false;
            float dt = MuseDesk.Motion.MotionSettings.SafeStep(Time.unscaledDeltaTime);
            if (liftMoving) _lift = Mathf.MoveTowards(_lift, goal, dt / 0.12f);
            if (_revealing)
            {
                var appear = MuseDesk.Motion.MuseMotionProfile.Active.panelAppear;
                float rd = Mathf.Max(0.01f, appear.Duration);
                _revealT = Mathf.MoveTowards(_revealT, 1f, dt / rd);
                _revealScale = Mathf.Max(0.001f, appear.Evaluate(_revealT));
                if (_revealT >= 1f) { _revealing = false; _revealScale = 1f; }
            }

            if (_settling)
            {
                // Glide with the cardRelease token (InOutCubic, exact at both ends) for drops, lane changes and reflow.
                var release = MuseDesk.Motion.MuseMotionProfile.Active.cardRelease;
                float sd = Mathf.Max(0.01f, release.Duration);
                _settleT = settleSeconds > 0f ? Mathf.MoveTowards(_settleT, 1f, dt / sd) : 1f;
                float eased = release.Evaluate(_settleT);
                Vector3 to = _settleToHome ? HomeWithLift() : _settleToPos;
                Quaternion toRot = _settleToHome ? _homeLocalRot : _settleToRot;
                transform.localPosition = Vector3.LerpUnclamped(_settleFromPos, to, eased);
                transform.localRotation = Quaternion.SlerpUnclamped(_settleFromRot, toRot, eased);
                if (_settleT >= 1f)
                {
                    _settling = false;
                    if (_settleToHome) { _displaced = false; NoteLanded(); }   // back in its slot
                }
            }
            else if (_hasHome && !_displaced)
            {
                transform.localPosition = HomeWithLift();
                transform.localRotation = _homeLocalRot;
            }
            if (_baseScaleCaptured) transform.localScale = _baseScale * ((1f + (selectedScale - 1f) * _lift) * _revealScale * _hide.Factor);
        }

        // ─── Polish S9a: leaving (recycle) and the details sheet ease on panelHide / panelAppear, never pop ───

        readonly MuseDesk.Motion.PresenceFader _hide = new MuseDesk.Motion.PresenceFader(true);
        readonly MuseDesk.Motion.PresenceFader _details = new MuseDesk.Motion.PresenceFader(false);
        readonly System.Collections.Generic.List<Collider> _hideOff = new System.Collections.Generic.List<Collider>();
        readonly System.Collections.Generic.List<Collider> _detailsOff = new System.Collections.Generic.List<Collider>();
        Vector3 _detailsBase = Vector3.one;
        bool _detailsInit;
        bool _presenceDirty;

        /// <summary>True from BeginHide until the card is revived (CancelHide) or recycled. Layout gives a leaving card
        /// no slot, and its colliders are off.</summary>
        public bool IsLeaving => _hide.IsLeaving;

        /// <summary>Raised once, on the step that finishes a hide that was not cancelled. The presenter then deactivates
        /// the card and pools it. Never raised for a hide that was cancelled.</summary>
        public event Action<TaskCardView> HideFinished;

        /// <summary>Ease the card out on panelHide (minimum scale ScaleEase.HiddenFactor) instead of deactivating it at
        /// once. Colliders (pokes, grab) are off while it leaves. Safe to call repeatedly. If the card cannot animate
        /// (inactive, edit time) it finishes at once and HideFinished is raised before this returns.</summary>
        public void BeginHide()
        {
            CaptureBaseScale();
            if (_hide.IsLeaving) return;
            // Polish S9b: switching the colliders off drops a card the user is holding. Wait for the release, then hide.
            // The drag controller sees HidePending and cancels the drag (no assign for a task that is gone).
            if (Application.isPlaying && isActiveAndEnabled && IsHeld)
            {
                _deferredHide.Request();
                return;
            }
            StartHide();
        }

        MuseDesk.Agents.DeferredHide _deferredHide;

        /// <summary>True while the card's task is gone but a hand still holds the card, so the hide waits for the release.</summary>
        public bool HidePending => _deferredHide.Pending;

        /// <summary>True when the task behind this card has left (hiding or about to): nothing may be sent for it.</summary>
        public bool TaskGone => _deferredHide.Pending || _hide.IsLeaving;

        void StartHide()
        {
            SetCollidersOff(_hideOff, transform);
            bool animate = Application.isPlaying && isActiveAndEnabled;
            _hide.Hide(MuseDesk.Motion.MuseMotionProfile.Active.panelHide, animate);
            _presenceDirty = true;
            if (!animate) HideFinishedNow();
        }

        /// <summary>The task came back while the card was leaving: grow back from the CURRENT scale (no half-scaled card,
        /// no jump) and take colliders back. A card that already finished hiding cannot be revived (it is pooled).</summary>
        public bool CancelHide()
        {
            // The hide was only waiting for the user to let go: the task is back, so it simply does not happen.
            if (_deferredHide.Pending) { _deferredHide.Cancel(); return true; }
            if (_hide.State != MuseDesk.Motion.PresenceFader.Phase.Hiding) return false;
            _hide.Show(MuseDesk.Motion.MuseMotionProfile.Active.panelAppear, true);
            RestoreColliders(_hideOff);
            _presenceDirty = true;
            return true;
        }

        void HideFinishedNow() { var h = HideFinished; if (h != null) h(this); }

        void StepPresence()
        {
            if (!_hide.Moving && !_details.Moving) return;
            float dt = Time.unscaledDeltaTime;
            if (_hide.Moving)
            {
                _presenceDirty = true;
                if (_hide.Step(dt)) { HideFinishedNow(); return; }
            }
            if (_details.Moving)
            {
                _details.Step(dt);
                ApplyDetailsScale();
                if (_details.Interactable && !_hide.IsLeaving) RestoreColliders(_detailsOff);
            }
        }

        void ApplyDetailsScale()
        {
            if (detailsRoot != null) detailsRoot.transform.localScale = _detailsBase * _details.Factor;
        }

        void EnsureDetails()
        {
            if (_detailsInit || detailsRoot == null) return;
            _detailsInit = true;
            _detailsBase = detailsRoot.transform.localScale;
            // Authored shown (active) stays shown until told otherwise; authored inactive is hidden.
            _details.Snap(detailsRoot.activeSelf);
        }

        static void SetCollidersOff(System.Collections.Generic.List<Collider> remembered, Transform root)
        {
            remembered.Clear();
            var all = root.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < all.Length; i++)
                if (all[i] != null && all[i].enabled) { remembered.Add(all[i]); all[i].enabled = false; }
        }

        static void RestoreColliders(System.Collections.Generic.List<Collider> remembered)
        {
            for (int i = 0; i < remembered.Count; i++) if (remembered[i] != null) remembered[i].enabled = true;
            remembered.Clear();
        }

        /// <summary>The resting slot, moved toward the reader (the region's -Z) by the selection lift.</summary>
        Vector3 HomeWithLift()
        {
            if (_lift <= 0f || _homeParent == null) return _homeLocalPos;
            float z = Mathf.Abs(_homeParent.lossyScale.z);
            if (z < 1e-5f) return _homeLocalPos;
            return _homeLocalPos + new Vector3(0f, 0f, -selectedLift * _lift / z);
        }

        void BeginSettle(Vector3 toLocalPos, Quaternion toLocalRot, bool toHome)
        {
            _settleFromPos = transform.localPosition;
            _settleFromRot = transform.localRotation;
            _settleToPos = toLocalPos;
            _settleToRot = toLocalRot;
            _settleToHome = toHome;
            _settleT = 0f;
            _settling = true;
        }

        bool CanAnimate => settleSeconds > 0f && Application.isPlaying && isActiveAndEnabled;

        /// <summary>Forget any hold, selection, edge state and half-finished motion. Called when a
        /// pooled card is recycled, so it does not come back lifted, tinted or mid-flight.</summary>
        public void ResetPresentation()
        {
            _settling = false;
            _displaced = false;
            _placed = false;
            _revealing = false;
            _revealScale = 1f;
            _parked = false;
            _simulatedHold = false;
            _pending = false;
            _refusedUntil = 0f;
            IsSelected = false;
            _lift = 0f;
            // A pooled card comes back whole: full size, its colliders back, the details sheet hidden (by scale).
            _hide.Snap(true);
            _deferredHide.Cancel();
            _laneChanged = false;
            _presenceDirty = false;
            RestoreColliders(_hideOff);
            if (_detailsInit && detailsRoot != null && detailsRoot.activeSelf)
            {
                IsExpanded = false;
                _details.Snap(false);
                ApplyDetailsScale();
                SetCollidersOff(_detailsOff, detailsRoot.transform);
            }
            if (_baseScaleCaptured) transform.localScale = _baseScale;
            ApplyEdge(EdgeState.Idle);
        }

        /// <summary>Capture hook for a Play Mode driver with no hands: makes the card report itself
        /// as held (the assignment pads appear, the toolbar selects it, layout leaves it alone) until
        /// it is switched off again, which the drag controller treats as letting go. Not used by the
        /// app itself.</summary>
        public void SetSimulatedHold(bool held) => _simulatedHold = held;

        /// <summary>True while at least one hand is selecting (grabbing) this card. Reflection read of
        /// Grabbable.SelectingPointsCount &gt; 0 — the same property the existing workspace scripts use.</summary>
        public bool IsHeld
        {
            get
            {
                if (_simulatedHold) return true;
                if (grabbable == null) return false;
                if (!_selResolved)
                {
                    // VERIFY: property name 'SelectingPointsCount' matches the installed
                    // Oculus.Interaction.Grabbable (confirmed in use by GrabbableRigidbody.cs / GrabReadout.cs).
                    // Bound once to a typed delegate: PropertyInfo.GetValue boxed an int per card per frame.
                    var prop = grabbable.GetType().GetProperty("SelectingPointsCount",
                        BindingFlags.Instance | BindingFlags.Public);
                    var getter = prop != null ? prop.GetGetMethod() : null;
                    if (getter != null && prop.PropertyType == typeof(int))
                        _selCount = (Func<int>)Delegate.CreateDelegate(typeof(Func<int>), grabbable, getter);
                    _selResolved = true;
                }
                if (_selCount == null) return false;
                try { return _selCount() > 0; }
                catch { return false; }
            }
        }

        public void SetData(TaskCardV2 data)
        {
            Data = data;
            Render();
        }

        void Render()
        {
            if (Data == null) return;
            // The card face: title, one state line, role chip, mode words. Nothing else by default.
            SetText(titleLabel, Truncate(Data.Title, 48));
            SetText(idRevisionLabel, "");                        // id / revision are off the card face
            SetText(roleLabel, RoleWord(Data.AgentId));
            // Priority only when it is not the default, and as a word.
            SetText(priorityLabel, Data.Priority == DefaultPriority ? "" : PriorityWord(Data.Priority));
            if (tintHeaderText)
            {
                // Both sit on the role strip. The Tester's strip is amber, where dark text reads;
                // the other strips are dark enough for light text.
                Color want = Data.AgentId == Phase2AgentIds.Tester ? RoleTextOnLight : RoleTextOnDark;
                if (roleLabel && roleLabel.color != want) roleLabel.color = want;
                if (priorityLabel && priorityLabel.color != want) priorityLabel.color = want;
            }
            if (!_noticeActive) SetText(stateLabel, StateLine(Data));
            SetText(sourceLabel, ModeWords(Data.SourceMode));
            if (statusMarker) ApplyStateTint();
        }

        static void SetText(TMP_Text label, string text)
        {
            if (label == null) return;
            if (text == null) text = "";
            if (label.text != text) label.text = text;
        }

        MaterialPropertyBlock _tintBlock;

        void ApplyStateTint()
        {
            // Non-authoritative color hint; never a substitute for the text marker.
            // The shared state palette (polish: one colour = one meaning on the board, agents and the Wall).
            Color c = MuseDesk.Workspace.OfficeTokens.StateColor(Data.TaskState);
            // Property block, not renderer.material: that created a material instance per card.
            if (_tintBlock == null) _tintBlock = new MaterialPropertyBlock();
            statusMarker.GetPropertyBlock(_tintBlock);
            _tintBlock.SetColor("_BaseColor", c);
            _tintBlock.SetColor("_Color", c);
            statusMarker.SetPropertyBlock(_tintBlock);
        }

        public void SetExpanded(bool expanded)
        {
            IsExpanded = expanded;
            if (!detailsRoot) return;
            EnsureDetails();
            var prof = MuseDesk.Motion.MuseMotionProfile.Active;
            bool animate = Application.isPlaying && isActiveAndEnabled;
            if (expanded)
            {
                // Kept active once shown (hiding is by scale, never SetActive: poke keys under it stay registered).
                // The first time, it is activated while still at the hidden scale, then grows.
                if (!detailsRoot.activeSelf)
                {
                    _details.Snap(false);
                    ApplyDetailsScale();
                    detailsRoot.SetActive(true);
                    SetCollidersOff(_detailsOff, detailsRoot.transform);
                }
                _details.Show(prof.panelAppear, animate);
                ApplyDetailsScale();
                if (_details.Interactable && !_hide.IsLeaving) RestoreColliders(_detailsOff);
            }
            else
            {
                if (!detailsRoot.activeSelf) return;                 // never shown: nothing to hide
                if (_detailsOff.Count == 0) SetCollidersOff(_detailsOff, detailsRoot.transform);   // pokes off at once
                _details.Hide(prof.panelHide, animate);
                ApplyDetailsScale();
            }
        }

        public void ToggleExpanded() => SetExpanded(!IsExpanded);

        /// <summary>Record the card's resting slot in its region (parent + local pose).</summary>
        public void SetHome(Transform parent, Vector3 localPos, Quaternion localRot)
        {
            _homeParent = parent;
            _homeLocalPos = localPos;
            _homeLocalRot = localRot;
            _hasHome = true;
        }

        /// <summary>Go back to the recorded resting slot (used on an invalid drop / 409 / cancel).
        /// A card that a hand moved eases there in about settleSeconds; a card that never left its
        /// slot (or an inactive / edit-time card) is placed at once.</summary>
        public void ReturnHome()
        {
            _parked = false;
            if (!_hasHome) return;
            // Polish: a card that already rests on the board and whose SLOT changed (a lane neighbour left or
            // arrived, or the card moved lane) glides there with the cardRelease token instead of teleporting.
            bool slotMoved = _placed && CanAnimate && _homeParent != null && transform.parent == _homeParent
                             && (transform.localPosition - HomeWithLift()).sqrMagnitude > 1e-8f;
            // Already gliding to this very slot (layout runs on every task event): leave the running glide alone.
            if (_settling && _settleToHome && transform.parent == _homeParent
                && (_settleToPos - _homeLocalPos).sqrMagnitude < 1e-10f) return;
            bool glide = (_displaced || slotMoved) && CanAnimate;
            if (_homeParent != null && transform.parent != _homeParent)
                transform.SetParent(_homeParent, worldPositionStays: glide);
            if (glide)
            {
                BeginSettle(_homeLocalPos, _homeLocalRot, toHome: true);
                _placed = true;
                return;
            }
            _settling = false;
            _displaced = false;
            _placed = true;
            transform.localPosition = HomeWithLift();
            transform.localRotation = _homeLocalRot;
            NoteLanded();
        }

        bool _laneChanged;

        // The card reached its slot in a different lane than it rested in: one quiet tick (FeedbackController gates it
        // under Focus and rate-limits it, so a burst of cards landing together is one tick).
        void NoteLanded()
        {
            if (!_laneChanged) return;
            _laneChanged = false;
            if (Application.isPlaying) MuseDesk.Feedback.FeedbackController.PlayCue(MuseDesk.Feedback.FeedbackCue.CardLanded);
        }

        // True once the card has been put in a lane slot since it was shown (only then can a slot change glide).
        bool _placed;

        // Reveal of a newly created card: grows from nothing with the panelAppear token (never a full-size pop).
        bool _revealing;
        float _revealT;
        float _revealScale = 1f;

        /// <summary>A newly created card grows into its slot (panelAppear) instead of popping in at full size.
        /// Presentation only; the card's data and slot are already final.</summary>
        public void BeginReveal()
        {
            CaptureBaseScale();
            if (!CanAnimate || IsHeld) { _revealing = false; _revealScale = 1f; return; }
            _revealing = true;
            _revealT = 0f;
            _revealScale = 0.001f;
            transform.localScale = _baseScale * _revealScale;   // before the first rendered frame
        }

        /// <summary>Layout hook for BoardPresenter: put a card that is not in a hand at its recorded
        /// slot. Same rule as ReturnHome: ease if a hand moved it, otherwise place it at once. A card
        /// waiting on a pad for the answer to its drop stays there until the answer (ShowResult).</summary>
        public void PlaceAtHome()
        {
            if (_parked && _pending) return;
            ReturnHome();
        }

        /// <summary>Move the card under another lane. A card that a hand has moved keeps its place
        /// in the world (so it can ease to the new slot); a fresh card takes the lane's frame.</summary>
        public void MoveToRegion(Transform region)
        {
            if (region == null || transform.parent == region) return;
            bool held = IsHeld;
            // Polish S9b: a card that was resting on the board and now belongs to another lane (the board changed it:
            // a confirmed move or a reducer update) sounds one quiet tick when it settles. Never for a new card, never on a drag.
            if (_placed) _laneChanged = true;
            // A card already resting on the board keeps its place in the world too, so it glides to the new
            // lane's slot (cardRelease) rather than teleporting. A held card is never touched: the hand owns it.
            bool keepWorld = (_displaced || _settling || held || _placed) && CanAnimate;
            if (keepWorld && !held && !_settling) _displaced = true;   // so the layout eases it into the new slot
            Transform old = transform.parent;
            // A glide to a pad is expressed in the old lane's frame: carry its target across.
            Vector3 targetWorld = old != null ? old.TransformPoint(_settleToPos) : _settleToPos;
            transform.SetParent(region, worldPositionStays: keepWorld);
            if (_settling) BeginSettle(region.InverseTransformPoint(targetWorld), _settleToRot, _settleToHome);
        }

        /// <summary>Ease the card to a place in the world that is not its slot (a drop pad or a
        /// station) while a command is pending. It stays "displaced", so it eases home afterwards.</summary>
        public void GlideTo(Transform anchor)
        {
            if (anchor == null) return;
            _displaced = true;
            _parked = true;
            if (!CanAnimate)
            {
                _settling = false;
                transform.SetPositionAndRotation(anchor.position, anchor.rotation);
                return;
            }
            Transform parent = transform.parent;
            Vector3 lp = parent != null ? parent.InverseTransformPoint(anchor.position) : anchor.position;
            Quaternion lr = parent != null ? Quaternion.Inverse(parent.rotation) * anchor.rotation : anchor.rotation;
            BeginSettle(lp, lr, toHome: false);
        }

        // ─── Accessible no-drag fallback (hands-only poke). Wire these to poke buttons on the
        //     expanded card. They raise AssignRequested; the presenter issues the same idempotent
        //     assign command a physical drop would (master §4 requirement). ───
        public void PokeAssignResearcher() => AssignRequested?.Invoke(this, Phase2AgentIds.Researcher);
        public void PokeAssignDeveloper() => AssignRequested?.Invoke(this, Phase2AgentIds.Developer);
        public void PokeAssignTester() => AssignRequested?.Invoke(this, Phase2AgentIds.Tester);

        /// <summary>Poke target: pause a running task / resume a paused one (presenter decides which).</summary>
        public void PokePauseResume() => PauseResumeRequested?.Invoke(this);
        /// <summary>Poke target: cycle this task's priority.</summary>
        public void PokeCyclePriority() => PriorityCycleRequested?.Invoke(this);

        // ─── helpers ───
        // ASCII only (the UI font rule): three dots, not an ellipsis character.
        static string Truncate(string s, int max)
            => string.IsNullOrEmpty(s) ? "" : (s.Length <= max ? s : s.Substring(0, max - 3) + "...");
        static string Capitalize(string s)
            => string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);

        // What the card can honestly say about itself, in one short line.
        static string StateLine(TaskCardV2 d) => StateWords(d);

        // ─── words shared by the card, the card toolbar and the task dock ───

        /// <summary>The priority new cards get (BoardPresenter / Phase2Bootstrap create with 1). A card
        /// at this priority shows no priority word.</summary>
        public const int DefaultPriority = 1;

        /// <summary>Priority 0..3 as a word.</summary>
        public static string PriorityWord(int priority)
        {
            switch (Mathf.Clamp(priority, 0, 3))
            {
                case 0: return "Low";
                case 2: return "High";
                case 3: return "Urgent";
                default: return "Normal";
            }
        }

        /// <summary>The assignee as a word: "Researcher", "Developer", "Tester" or "Unassigned".</summary>
        public static string RoleWord(string agentId)
        {
            switch (agentId)
            {
                case Phase2AgentIds.Researcher: return "Researcher";
                case Phase2AgentIds.Developer: return "Developer";
                case Phase2AgentIds.Tester: return "Tester";
                default: return string.IsNullOrEmpty(agentId) ? "Unassigned" : Capitalize(agentId);
            }
        }

        /// <summary>"SCRIPTED DEMO" or "LIVE" for a source_mode value. Anything that is not
        /// explicitly the demo is reported as LIVE only when it says "live"; an unknown or missing
        /// value is shown as scripted, so nothing is ever presented as live by default.</summary>
        public static string ModeWords(string sourceMode)
            => sourceMode == SourceModes.Live ? "LIVE" : "SCRIPTED DEMO";

        /// <summary>True for a queued executable card whose run has not been started: the card that
        /// Start (or a drop on the Researcher) launches.</summary>
        public static bool IsReadyToRun(TaskCardV2 d)
            => d != null && d.Executable && d.TaskState == TaskStates.Queued && string.IsNullOrEmpty(d.RunTaskId);

        /// <summary>Local hint for showing a Pause action (the gateway still decides). Waiting cards
        /// can be paused; a card whose pipeline run is in progress has no safe pause point.</summary>
        public static bool IsPausable(TaskCardV2 d)
            => d != null && (d.TaskState == TaskStates.Queued || d.TaskState == TaskStates.Blocked);

        /// <summary>Local hint for showing a Resume action.</summary>
        public static bool IsResumable(TaskCardV2 d)
            => d != null && (d.TaskState == TaskStates.Paused || d.TaskState == TaskStates.PauseRequested);

        /// <summary>One state line in plain words for a card, from its canonical task state.</summary>
        public static string StateWords(TaskCardV2 d)
        {
            if (d == null) return "";
            switch (d.TaskState)
            {
                case TaskStates.Queued:
                    if (IsReadyToRun(d)) return "Ready for Researcher";
                    if (d.Executable) return "Queued";
                    // A planned card: nothing runs it, and the card says so.
                    switch (d.AgentId)
                    {
                        case Phase2AgentIds.Researcher: return "Planned for Researcher";
                        case Phase2AgentIds.Developer: return "Planned for Developer";
                        case Phase2AgentIds.Tester: return "Planned for Tester";
                        default: return "Planned";
                    }
                case TaskStates.Running: return "In progress";
                case TaskStates.AwaitingApproval: return "Needs your decision";
                case TaskStates.Blocked: return "Waiting on a task";
                case TaskStates.PauseRequested: return "Pausing";
                case TaskStates.Paused: return "Paused";
                case TaskStates.Completed: return "Completed";
                case TaskStates.Failed: return "Failed";
                case TaskStates.Cancelled: return "Cancelled";
                default: return string.IsNullOrEmpty(d.TaskState) ? "" : Capitalize(d.TaskState);
            }
        }
    }
}
