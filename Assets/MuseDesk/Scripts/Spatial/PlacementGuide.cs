using System;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using Oculus.Interaction;
using MuseDesk.Workspace;

namespace MuseDesk.Spatial
{
    /// <summary>
    /// The guided step between pressing Floor or Ceiling and the preview starting. A small paper card
    /// appears in front of the user ("Look up at the ceiling", an animated arrow, a Cancel key). It
    /// tracks the head's gaze (main camera forward); when the user looks past the pitch threshold at
    /// the target surface and holds for about 0.6 s (a fill ring on the card), THEN the mode is
    /// requested, so the room scan runs while the user is looking at the surface. The scan visuals
    /// (<see cref="CeilingScanReveal"/>, <see cref="FloorScanReveal"/>) start their wipe from the gaze
    /// point handed over through <see cref="TryConsumeGaze"/>. After 8 s without looking, a "Start here"
    /// key requests the mode anyway. The card then carries the next instruction through the preview
    /// and hides on confirm, cancel or desk.
    ///
    /// Floor preview (deliberate placement, rc6): the card moves closer (previewCardDistance) and carries
    /// the contextual keys: "Place here" (only while <see cref="SpatialDirectorCore.CanPlaceHere"/>: a
    /// clear spot with a clear way, the outline not being dragged), "Other spot" (choose another spot) and
    /// "Cancel" (ends the preview; nothing moved). A floor preview opened without the guide is adopted, so
    /// the keys are always there. The look-at step's "where you looked" (CentrePreviewOn) only previews.
    ///
    /// Nothing here moves the office except Place here, which is the director's Confirm (route planned
    /// and swept again at that moment). All decisions are in <see cref="PlacementGuideLogic"/> and
    /// <see cref="SpatialDirectorCore"/> (Core, EditMode tested). The card is a scene-root object (world
    /// space, never under WorkspaceRoot), shown and hidden by scale.
    /// Layout: the "Card" transform has +Z toward the user (as the mode console); its "Content" child
    /// is turned 180 degrees about Y so TMP (readable from its local -Z) reads correctly and x is the
    /// user's right.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlacementGuide : MonoBehaviour
    {
        [Serializable]
        public class GuideKey
        {
            public Transform root;
            public Vector3 shownScale = Vector3.one;
            public PokeInteractable poke;
            public KeyStateVisual visual;
            [NonSerialized] public ScaleEase ease;
            [NonSerialized] public bool shown;
            [NonSerialized] public float enableAt;
        }

        [Header("Wiring (AgentScripts/BuildPlacementGuideV1)")]
        public SpatialWorkspaceDirector director;
        [Tooltip("The user's head. Camera.main when empty.")]
        public Transform head;
        public Transform card;
        public Transform content;
        public TMP_Text title;
        public TMP_Text body;
        public MeshFilter ringFill;
        public MeshFilter ringTrack;
        public MeshFilter arrow;
        public GuideKey cancelKey = new GuideKey();
        public GuideKey startKey = new GuideKey();
        [Tooltip("Floor preview: moves the office to the previewed spot (AgentScripts/BuildPlaceHereV1). Shown only while the spot and its way are clear.")]
        public GuideKey placeKey = new GuideKey();
        [Tooltip("Floor preview: the next detected spot, or how to choose one by pointing (AgentScripts/BuildPlaceHereV1).")]
        public GuideKey anotherKey = new GuideKey();

        [Header("Gaze (degrees past the horizon)")]
        public float ceilingPitchDeg = 35f;
        public float floorPitchDeg = 30f;
        public float dwellSeconds = 0.6f;
        public float timeoutSeconds = 8f;

        [Header("Card")]
        public float cardDistance = 0.75f;
        [Tooltip("Where the card sits relative to the horizon while guiding, so it stays in view when the user looks at the surface.")]
        public float ceilingCardPitch = 14f;
        public float floorCardPitch = -16f;
        public float previewCardPitch = -12f;
        [Tooltip("Distance of the card while a floor preview is open: its keys are poked (arm's reach is about 0.6 m).")]
        public float previewCardDistance = 0.55f;
        public float followRate = 3f;
        public float easeSeconds = 0.2f;
        public float armDelay = 0.35f;
        public float ringInner = 0.027f;
        public float ringOuter = 0.036f;

        [Header("Words")]
        public string ceilingTitle = "Look up at the ceiling";
        public string ceilingBody = "Hold your gaze there for a moment";
        public string floorTitle = "Look at the floor";
        public string floorBody = "Where you'd like the office";
        public string offerBody = "Or press Start here";
        public string requestedTitle = "Scanning";
        public string requestedBody = "Keep looking at the spot";
        public string floorPreviewTitle = "Place the office";
        public string floorPreviewBody = "Press Place here, or pinch and drag the outline";
        public string floorDraggingBody = "Let go on a clear spot, then press Place here";
        public string floorReadyBody = "Press Place here to move it, or drag it again";
        public string ceilingPreviewTitle = "Mount it here?";
        public string ceilingPreviewBody = "Confirm to mount it here, or Next spot";

        const int RingSegments = 48;
        static readonly int IdColor = Shader.PropertyToID("_BaseColor");

        // Gaze handed to the scan visuals at the moment of the request.
        static Vector3 _gazeOrigin, _gazeDir;
        static float _gazeStamp = -100f;
        /// <summary>The running guide (for the voice path and drivers). Null before Awake.</summary>
        public static PlacementGuide Instance { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { _gazeStamp = -100f; Instance = null; }

        /// <summary>
        /// The gaze ray at the last request, once, if it is under 3 s old. The scan reveals call this
        /// when their grid becomes wanted and intersect the ray with the surface height they know.
        /// </summary>
        public static bool TryConsumeGaze(out Vector3 origin, out Vector3 dir)
        {
            origin = _gazeOrigin; dir = _gazeDir;
            if (Time.unscaledTime - _gazeStamp > 3f) return false;
            _gazeStamp = -100f;
            return true;
        }

        public static void PublishGaze(Vector3 origin, Vector3 dir)
        {
            _gazeOrigin = origin; _gazeDir = dir; _gazeStamp = Time.unscaledTime;
        }

        readonly PlacementGuideLogic _logic = new PlacementGuideLogic();
        ScaleEase _cardEase;
        Mesh _fillMesh, _trackMesh, _arrowMesh;
        int _fillIndexCount = -1;
        Vector3 _anchorFlat = Vector3.forward;
        Vector3 _cardTarget;
        bool _poseValid, _inited;
        string _title, _body;
        Vector3 _injected; bool _hasInjected;
        Vector3 _lastGaze = Vector3.forward;
        float _bob;

        // Read by the driver and the tests.
        public GuideState State => _logic.State;
        public GuideTarget Target => _logic.Target;
        public float Fill => _logic.Fill;
        public float Elapsed => _logic.Elapsed;
        public bool StartOffered => _logic.StartOffered;
        public bool Active => _logic.Active;
        public bool CardShown => _cardEase != null && _cardEase.Shown;
        public string TitleText => _title;
        public string BodyText => _body;
        public Vector3 LastGazeForward => _lastGaze;

        // ───────────────────────── lifecycle ─────────────────────────

        void Awake() { Instance = this; EnsureInit(); }
        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (_fillMesh != null) Destroy(_fillMesh);
            if (_trackMesh != null) Destroy(_trackMesh);
            if (_arrowMesh != null) Destroy(_arrowMesh);
        }

        void EnsureInit()
        {
            if (_inited) return;
            _inited = true;
            _cardEase = new ScaleEase();
            if (card != null) _cardEase.Init(card, Vector3.one, easeSeconds, false);
            InitKey(cancelKey);
            InitKey(startKey);
            InitKey(placeKey);
            InitKey(anotherKey);
            _fillMesh = BuildRing(RingSegments, ringInner, ringOuter, "GuideRingFill");
            _trackMesh = BuildRing(RingSegments, ringInner, ringOuter, "GuideRingTrack");
            _arrowMesh = BuildArrow();
            if (ringFill != null) ringFill.sharedMesh = _fillMesh;
            if (ringTrack != null) ringTrack.sharedMesh = _trackMesh;
            if (arrow != null) { arrow.sharedMesh = _arrowMesh; _arrowRestY = arrow.transform.localPosition.y; }
            _fillIndexCount = -1;
            SetFill(0f);
            ApplySettings();
            SetKeyPoke(cancelKey, false);
            SetKeyPoke(startKey, false);
            SetKeyPoke(placeKey, false);
            SetKeyPoke(anotherKey, false);
        }

        void InitKey(GuideKey k)
        {
            k.ease = new ScaleEase();
            if (k.root != null) k.ease.Init(k.root, k.shownScale, easeSeconds, false);
            k.shown = false;
        }

        void ApplySettings()
        {
            _logic.ceilingPitchDeg = ceilingPitchDeg;
            _logic.floorPitchDeg = floorPitchDeg;
            _logic.dwellSeconds = dwellSeconds;
            _logic.timeoutSeconds = timeoutSeconds;
        }

        // ───────────────────────── entry points ─────────────────────────

        /// <summary>
        /// A Floor or Ceiling press. True when the guide took it (the card is up and the request is
        /// delayed); false when the caller should request the mode directly (no guide possible).
        /// </summary>
        public bool TryBegin(WorkspaceMode mode)
        {
            EnsureInit();
            GuideTarget target = mode == WorkspaceMode.Floor ? GuideTarget.Floor
                               : mode == WorkspaceMode.Ceiling ? GuideTarget.Ceiling : GuideTarget.None;
            if (target == GuideTarget.None || !isActiveAndEnabled || card == null) return false;
            if (director == null) director = FindAnyObjectByType<SpatialWorkspaceDirector>();
            SpatialDirectorCore core = director != null ? director.Core : null;
            if (core == null || EnsureHead() == null) return false;
            if (director.flags != null && !(target == GuideTarget.Floor ? director.flags.floorModeEnabled : director.flags.ceilingModeEnabled)) return false;
            if (SpatialWorkspaceState.IsTransitioning) return false;
            PlacementMode pm = target == GuideTarget.Floor ? PlacementMode.Floor : PlacementMode.Ceiling;
            if (core.Phase == DirectorPhase.Previewing && core.PendingMode == pm) return false;   // already open: the director ignores a repeat
            if (target == GuideTarget.Floor && director.HasParkedFloorSpot && core.Mode == PlacementMode.Desk && core.Phase == DirectorPhase.Idle)
                return false;                                                                      // "Send back": no look-around needed
            if (_logic.State == GuideState.Guiding && _logic.Target == target) return true;       // a repeat press while guiding

            ApplySettings();
            _logic.Begin(target);
            SnapCard();
            _cardEase.Show();
            SetTexts();
            SetKeyShown(cancelKey, true);
            SetKeyShown(startKey, false);
            SetFill(0f);
            return true;
        }

        /// <summary>Cancel the guide (Cancel key, Desk key, mode change). Nothing was moved. No-op when idle.</summary>
        public void CancelGuide()
        {
            if (!_logic.Active) return;
            _logic.Cancel();
            Hide();
        }

        /// <summary>The card's Cancel key. While a preview is open it also ends the preview (nothing had moved).</summary>
        public void PressCancel()
        {
            bool previewing = _logic.State == GuideState.Previewing;
            CancelGuide();
            if (previewing && director != null && director.IsPreviewing) director.CancelPlacement();
        }

        public void PressStart()
        {
            if (_logic.PressStart()) DoRequest();
        }

        /// <summary>Floor preview: move the office to the previewed spot (the director's Confirm: the route is
        /// planned and swept again now). Refused, nothing moves, the footprint and status line say why.</summary>
        public void PressPlace()
        {
            if (director == null || !placeKey.shown) return;
            if (!director.PlaceHere() && placeKey.visual != null) placeKey.visual.FlashRefused();
        }

        /// <summary>Floor preview: the next detected spot, or how to choose one by pointing. Never moves the office.</summary>
        public void PressAnother()
        {
            if (director == null || !anotherKey.shown) return;
            director.ChooseAnotherSpot();
        }

        // ───────────────────────── frame loop ─────────────────────────

        void Update()
        {
            if (!_inited) return;
            float dt = Time.unscaledDeltaTime;
            bool animating = _cardEase.Animating || cancelKey.ease.Animating || startKey.ease.Animating
                             || placeKey.ease.Animating || anotherKey.ease.Animating;
            if (!_logic.Active && !animating)
            {
                // A floor preview opened without the guide: carry the card (and its keys) through it.
                if (director == null || !director.CanAim || !TryAdopt()) return;
            }

            if (animating)
            {
                _cardEase.Tick(dt);
                TickKey(cancelKey, dt);
                TickKey(startKey, dt);
                TickKey(placeKey, dt);
                TickKey(anotherKey, dt);
                if (!_cardEase.Animating && _cardEase.Shown) card.BroadcastMessage("SyncToCurrentScale", SendMessageOptions.DontRequireReceiver);
            }
            if (!_logic.Active) return;
            LateTickPokes();

            Transform h = EnsureHead();
            if (h == null) return;
            if (director == null) director = FindAnyObjectByType<SpatialWorkspaceDirector>();
            SpatialDirectorCore core = director != null ? director.Core : null;

            Vector3 gaze = _hasInjected ? _injected : h.forward;
            _lastGaze = gaze;

            switch (_logic.State)
            {
                case GuideState.Guiding:
                    if (_logic.Tick(dt, gaze)) DoRequest();
                    else
                    {
                        SetFill(_logic.Fill);
                        if (_logic.StartOffered && !startKey.shown) { SetKeyShown(startKey, true); SetTexts(); }
                    }
                    break;
                case GuideState.Previewing:
                    {
                        bool on = core != null && core.Phase == DirectorPhase.Previewing
                                  && core.PendingMode == (_logic.Target == GuideTarget.Floor ? PlacementMode.Floor : PlacementMode.Ceiling);
                        _logic.ReportPreviewing(on);
                        if (_logic.State == GuideState.Done) Hide();
                        else if (_logic.Target == GuideTarget.Floor) UpdateFloorKeys(core);
                        break;
                    }
            }

            if (_logic.Active) FollowHead(h, dt);
            AnimateArrow(dt);
        }

        void DoRequest()
        {
            if (director == null || director.Core == null) { _logic.ReportRequestResult(false); Hide(); return; }
            Transform h = EnsureHead();
            Vector3 dir = _hasInjected ? _injected : (h != null ? h.forward : Vector3.forward);
            Vector3 origin = h != null ? h.position : Vector3.zero;
            // Stamp the gaze BEFORE asking, so the scan reveal sees it on the frame the preview opens.
            PublishGaze(origin, dir);
            SetKeyShown(startKey, false);
            SetKeyShown(cancelKey, false);
            SetFill(1f);
            if (_logic.Target == GuideTarget.Floor) director.RequestFloor(); else director.RequestCeiling();

            SpatialDirectorCore core = director.Core;
            PlacementMode pm = _logic.Target == GuideTarget.Floor ? PlacementMode.Floor : PlacementMode.Ceiling;
            bool opened = core.Phase == DirectorPhase.Previewing && core.PendingMode == pm;
            _logic.ReportRequestResult(opened);
            if (opened && pm == PlacementMode.Floor && dir.y < -0.05f)
            {
                // Open the footprint where the user was looking (same rules as a drag; if that spot
                // cannot be used the preview stays on the automatic spot and the footprint says why).
                float floorY = core.PreviewPose.position.y;
                float t = (floorY - origin.y) / dir.y;
                if (t > 0f && t < 4f) core.CentrePreviewOn(origin + dir * t, quietIfBlocked: true);
            }
            if (opened) { SetTexts(); }
            else { _gazeStamp = -100f; Hide(); }             // refused or moved at once: the console's status line says why
        }

        void Hide()
        {
            _cardEase.Hide();
            SetKeyShown(cancelKey, false);
            SetKeyShown(startKey, false);
            SetKeyShown(placeKey, false);
            SetKeyShown(anotherKey, false);
            SetFill(0f);
        }

        /// <summary>Carry the card through a floor preview this guide did not open.</summary>
        bool TryAdopt()
        {
            if (card == null || !isActiveAndEnabled || EnsureHead() == null) return false;
            if (!_logic.Adopt(GuideTarget.Floor)) return false;
            SnapCard();
            _cardEase.Show();
            SetFill(0f);
            SetTexts();
            return true;
        }

        /// <summary>Floor preview: Place here only while the spot and its way are clear and nothing is being
        /// dragged; Other spot and Cancel always; the body says what to do next.</summary>
        void UpdateFloorKeys(SpatialDirectorCore core)
        {
            if (core == null) return;
            bool ready = core.CanPlaceHere;
            SetKeyShown(placeKey, ready);
            SetKeyShown(anotherKey, true);
            SetKeyShown(cancelKey, true);
            SetKeyShown(startKey, false);
            string b;
            switch (core.PlaceStage)
            {
                case FloorPlaceStage.RouteValid: b = floorDraggingBody; break;
                case FloorPlaceStage.SpotBlocked:
                case FloorPlaceStage.RouteBlocked:
                    b = core.FootprintWords;
                    if (string.IsNullOrEmpty(b)) b = floorPreviewBody;
                    break;
                case FloorPlaceStage.NeedsConfirmation: b = core.PlacedByAim ? floorReadyBody : floorPreviewBody; break;
                default: b = floorPreviewBody; break;
            }
            if (b != _body && body != null) { _body = b; body.text = b; }
        }

        // ───────────────────────── card placement ─────────────────────────

        Transform EnsureHead()
        {
            if (head == null) { Camera c = Camera.main; if (c != null) head = c.transform; }
            return head;
        }

        float CardPitch()
        {
            if (_logic.State == GuideState.Previewing) return previewCardPitch;
            return _logic.Target == GuideTarget.Ceiling ? ceilingCardPitch : floorCardPitch;
        }

        static Vector3 Flat(Vector3 v, Vector3 fallback)
        {
            v.y = 0f;
            float m = v.magnitude;
            return m < 0.2f ? fallback : v / m;
        }

        void SnapCard()
        {
            Transform h = EnsureHead();
            if (h == null) return;
            _anchorFlat = Flat(h.forward, Flat(h.up, Vector3.forward));
            PlaceCard(h, true, 0f);
        }

        void FollowHead(Transform h, float dt)
        {
            Vector3 flat = Flat(_hasInjected ? _injected : h.forward, _anchorFlat);
            if (_logic.State == GuideState.Previewing)
            {
                // Hold still while the user works on the preview; re-seat only when they turn well away.
                if (Vector3.Angle(flat, _anchorFlat) > 55f)
                    _anchorFlat = Vector3.Slerp(_anchorFlat, flat, 1f - Mathf.Exp(-followRate * dt));
            }
            else
            {
                _anchorFlat = Vector3.Slerp(_anchorFlat, flat, 1f - Mathf.Exp(-followRate * dt));
            }
            PlaceCard(h, false, dt);
        }

        void PlaceCard(Transform h, bool snap, float dt)
        {
            float p = CardPitch() * Mathf.Deg2Rad;
            float cp = Mathf.Cos(p), sp = Mathf.Sin(p);
            float dist = _logic.State == GuideState.Previewing && _logic.Target == GuideTarget.Floor ? previewCardDistance : cardDistance;
            Vector3 target = h.position + new Vector3(_anchorFlat.x * cp, sp, _anchorFlat.z * cp) * dist;
            if (snap || !_poseValid) _cardTarget = target;
            else _cardTarget = Vector3.Lerp(_cardTarget, target, 1f - Mathf.Exp(-followRate * 1.5f * dt));
            _poseValid = true;
            Vector3 toUser = h.position - _cardTarget;
            if (toUser.sqrMagnitude < 1e-4f) return;
            // +Z of the card toward the user, kept upright.
            transform.SetPositionAndRotation(_cardTarget, Quaternion.LookRotation(toUser, Vector3.up));
        }

        // ───────────────────────── presentation ─────────────────────────

        void SetTexts()
        {
            string t, b;
            bool floor = _logic.Target == GuideTarget.Floor;
            switch (_logic.State)
            {
                case GuideState.Requested:
                    t = requestedTitle; b = requestedBody; break;
                case GuideState.Previewing:
                    t = floor ? floorPreviewTitle : ceilingPreviewTitle;
                    b = floor ? floorPreviewBody : ceilingPreviewBody; break;
                default:
                    t = floor ? floorTitle : ceilingTitle;
                    b = _logic.StartOffered ? offerBody : (floor ? floorBody : ceilingBody); break;
            }
            if (t != _title && title != null) { _title = t; title.text = t; }
            if (b != _body && body != null) { _body = b; body.text = b; }
            // The arrow and ring belong to the looking step only.
            bool looking = _logic.State == GuideState.Guiding || _logic.State == GuideState.Requested;
            if (arrow != null && arrow.gameObject.activeSelf != looking) arrow.gameObject.SetActive(looking);
            if (ringFill != null && ringFill.gameObject.activeSelf != looking) ringFill.gameObject.SetActive(looking);
            if (ringTrack != null && ringTrack.gameObject.activeSelf != looking) ringTrack.gameObject.SetActive(looking);
            if (arrow != null) arrow.transform.localRotation = Quaternion.Euler(0f, 0f, floor ? 180f : 0f);
        }

        void AnimateArrow(float dt)
        {
            if (arrow == null || !arrow.gameObject.activeSelf) return;
            _bob += dt * 5f;
            float dir = _logic.Target == GuideTarget.Floor ? -1f : 1f;
            Vector3 lp = arrow.transform.localPosition;
            lp.y = _arrowRestY + dir * Mathf.Abs(Mathf.Sin(_bob)) * 0.008f;
            arrow.transform.localPosition = lp;
        }

        float _arrowRestY;

        void SetFill(float f)
        {
            if (ringFill == null || _fillMesh == null) return;
            int count = Mathf.Clamp(Mathf.RoundToInt(f * RingSegments), 0, RingSegments) * 6;
            if (count == _fillIndexCount) return;
            _fillIndexCount = count;
            _fillMesh.SetSubMesh(0, new SubMeshDescriptor(0, count),
                MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices | MeshUpdateFlags.DontNotifyMeshUsers);
            ringFill.GetComponent<MeshRenderer>().enabled = count > 0;
        }

        void SetKeyShown(GuideKey k, bool shown)
        {
            if (k.root == null || k.shown == shown) { k.shown = shown; if (!shown) SetKeyPoke(k, false); return; }
            k.shown = shown;
            if (shown) { k.ease.Show(); k.enableAt = Time.unscaledTime + armDelay; }
            else { k.ease.Hide(); SetKeyPoke(k, false); }
        }

        void TickKey(GuideKey k, float dt)
        {
            if (k.root == null) return;
            bool was = k.ease.Animating;
            k.ease.Tick(dt);
            if (was && !k.ease.Animating && k.ease.Shown) k.root.BroadcastMessage("SyncToCurrentScale", SendMessageOptions.DontRequireReceiver);
        }

        void SetKeyPoke(GuideKey k, bool on)
        {
            if (k.poke != null && k.poke.enabled != on) k.poke.enabled = on;
        }

        void LateTickPokes()
        {
            float now = Time.unscaledTime;
            if (cancelKey.shown) SetKeyPoke(cancelKey, now >= cancelKey.enableAt);
            if (startKey.shown) SetKeyPoke(startKey, now >= startKey.enableAt);
            if (placeKey.shown) SetKeyPoke(placeKey, now >= placeKey.enableAt);
            if (anotherKey.shown) SetKeyPoke(anotherKey, now >= anotherKey.enableAt);
        }

        // ───────────────────────── mesh builders ─────────────────────────

        static Mesh BuildRing(int segments, float inner, float outer, string name)
        {
            var verts = new Vector3[(segments + 1) * 2];
            var uvs = new Vector2[verts.Length];
            var tris = new int[segments * 6];
            for (int i = 0; i <= segments; i++)
            {
                float a = (float)i / segments * Mathf.PI * 2f;     // from the top, clockwise as the user sees it
                float sx = Mathf.Sin(a), cy = Mathf.Cos(a);
                verts[i * 2] = new Vector3(sx * inner, cy * inner, 0f);
                verts[i * 2 + 1] = new Vector3(sx * outer, cy * outer, 0f);
            }
            for (int i = 0; i < segments; i++)
            {
                int a = i * 2, b = a + 1, c = a + 2, d = a + 3, t = i * 6;
                tris[t] = a; tris[t + 1] = b; tris[t + 2] = c;
                tris[t + 3] = c; tris[t + 4] = b; tris[t + 5] = d;
            }
            var m = new Mesh { name = name, hideFlags = HideFlags.DontSave };
            m.vertices = verts; m.uv = uvs; m.triangles = tris;
            m.bounds = new Bounds(Vector3.zero, Vector3.one * outer * 2.2f);
            return m;
        }

        /// <summary>An up arrow, about 36 mm tall, centred on its middle, drawn in the content plane.</summary>
        static Mesh BuildArrow()
        {
            var v = new[]
            {
                new Vector3(0f, 0.018f, 0f), new Vector3(-0.016f, -0.002f, 0f), new Vector3(0.016f, -0.002f, 0f),   // head
                new Vector3(-0.0065f, -0.002f, 0f), new Vector3(0.0065f, -0.002f, 0f),                              // shaft top
                new Vector3(-0.0065f, -0.018f, 0f), new Vector3(0.0065f, -0.018f, 0f),                              // shaft bottom
            };
            var t = new[] { 0, 2, 1, 3, 4, 5, 4, 6, 5 };
            var m = new Mesh { name = "GuideArrow", hideFlags = HideFlags.DontSave };
            m.vertices = v; m.uv = new Vector2[v.Length]; m.triangles = t;
            m.RecalculateBounds();
            return m;
        }

        // ───────────────────────── drivers ─────────────────────────

        /// <summary>Play Mode driver: inject a gaze direction instead of moving the camera. Null clears it.</summary>
        public void InjectGaze(Vector3? forward)
        {
            if (forward.HasValue) { _injected = forward.Value.normalized; _hasInjected = true; }
            else _hasInjected = false;
        }

        public string Describe()
        {
            var sb = new StringBuilder(300);
            sb.Append("PlacementGuide state=").Append(_logic.State).Append(" target=").Append(_logic.Target)
              .Append(" fill=").Append(_logic.Fill.ToString("F2")).Append(" elapsed=").Append(_logic.Elapsed.ToString("F1"))
              .Append(" gazePitch=").Append(PlacementGuideLogic.PitchDeg(_lastGaze).ToString("F0"))
              .Append(" injected=").Append(_hasInjected).Append(" startOffered=").Append(_logic.StartOffered)
              .Append("\n  card ").Append(card != null ? (card.localScale.x.ToString("F3")) : "MISSING")
              .Append(" shown=").Append(CardShown)
              .Append(" at ").Append(transform.position.ToString("F2"))
              .Append("\n  title '").Append(_title).Append("' body '").Append(_body).Append("'")
              .Append("\n  cancelKey ").Append(cancelKey.shown ? "shown" : "hidden").Append(cancelKey.poke != null && cancelKey.poke.enabled ? " poke-on" : " poke-off")
              .Append("  startKey ").Append(startKey.shown ? "shown" : "hidden").Append(startKey.poke != null && startKey.poke.enabled ? " poke-on" : " poke-off")
              .Append("\n  placeKey ").Append(placeKey.root == null ? "MISSING" : placeKey.shown ? "shown" : "hidden").Append(placeKey.poke != null && placeKey.poke.enabled ? " poke-on" : " poke-off")
              .Append("  anotherKey ").Append(anotherKey.root == null ? "MISSING" : anotherKey.shown ? "shown" : "hidden").Append(anotherKey.poke != null && anotherKey.poke.enabled ? " poke-on" : " poke-off");
            return sb.ToString();
        }

        void OnEnable() { EnsureInit(); }
    }
}
