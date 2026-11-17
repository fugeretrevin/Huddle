using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using TMPro;
using Oculus.Interaction;
using Oculus.Interaction.Input;
using MuseDesk.Workspace;

namespace MuseDesk.Spatial
{
    /// <summary>
    /// Placing the floor office from the seat, by pointing (deliberate placement, rc6). While the
    /// floor preview is open either hand can aim at the floor: a thin arc runs from the hand to a
    /// point on the floor plane, the office footprint is highlighted there, a pinch on the footprint
    /// grabs it, moving the hand slides it across the floor, and letting go LEAVES IT THERE: nothing
    /// moves. The Place here key (PlacementGuide card, or the console) moves the office, through
    /// Confirm and its swept route. A short plate in front of the footprint says what is going on in
    /// words (<see cref="SpatialDirectorCore.FootprintWords"/>), and the colours follow
    /// <see cref="SpatialDirectorCore.PlaceStage"/>:
    ///   coral ...... the office may not stand there;
    ///   amber ...... it may stand there but no clear way there was found (and while going back after Stop);
    ///   teal ....... a clear spot with a clear way under the hand (dragging);
    ///   teal, solid  at rest on a clear spot: waiting for Place here;
    ///   blue ....... the office is on its way there;
    ///   green ...... placed (shown for placedShowSeconds).
    ///
    /// This component only READS the hands (the pointer pose and the index pinch of the Interaction
    /// SDK hands that are already in the rig, and through <see cref="HandInteractionProbe"/> whether
    /// their interactors hold or hover anything; it adds no rig, no interactor and no collider) and
    /// DRAWS. Every decision is in <see cref="FloorAimSession"/>, <see cref="HandOwnershipArbiter"/> and
    /// <see cref="SpatialDirectorCore"/>, which edit the preview only. It never writes the office.
    ///
    /// With no floor preview open and nothing moving it does nothing per frame beyond two property
    /// reads, and its visuals are inactive. The meshes are generated once at run time.
    /// </summary>
    public class FloorAimController : MonoBehaviour
    {
        [Header("Wiring")]
        public SpatialWorkspaceDirector director;
        [Tooltip("The rig's hand visuals (their IHand gives the pointer pose and the pinch). Found by handedness at run time when empty.")]
        public HandVisual leftHandVisual;
        public HandVisual rightHandVisual;

        [Header("Behaviour")]
        public FloorAimSettings settings = new FloorAimSettings();
        [Tooltip("Pinch timing and hand ownership (fresh pinch, debounce, near-object clear window, tracking grace).")]
        public HandOwnershipSettings ownership = new HandOwnershipSettings();
        [Tooltip("The arc is drawn for a hand whose aim point is on the footprint or within this distance of it (and always for the hand that is dragging).")]
        public float rayShowDistance = 0.9f;
        [Tooltip("How long the footprint stays on show, green, after the office arrived (seconds).")]
        public float placedShowSeconds = 1.5f;

        [Header("Visuals (built by BuildFloorAimV1; no interactables inside)")]
        [Tooltip("Parent of everything drawn. Inactive unless a floor preview is open.")]
        public GameObject visuals;
        [Tooltip("Follows the footprint on show (root pose of the office-to-be). Fill, rim and hold ring are its children.")]
        public Transform footprintRoot;
        public MeshFilter footprintFill;
        public MeshFilter footprintRim;
        public MeshFilter holdTrack;
        public MeshFilter holdFill;
        public MeshFilter leftCursor;
        public MeshFilter rightCursor;
        public LineRenderer leftRay;
        public LineRenderer rightRay;

        [Header("Words on the footprint (built by BuildFloorPlaceFixV1; optional)")]
        [Tooltip("Holds the plate and the text. Placed every frame just in front of the footprint's near edge, turned to the eye.")]
        public Transform wordsRoot;
        public TextMeshPro wordsText;
        [Tooltip("A quad behind the text, sized to it. Tinted Plate.")]
        public MeshRenderer wordsPlate;
        [Tooltip("Gap between the footprint's near edge and the plate, metres (toward the user).")]
        public float wordsGap = 0.05f;
        [Tooltip("Height of the plate's centre above the floor, metres.")]
        public float wordsHeight = 0.08f;
        [Tooltip("Plate padding around the text (x, y), metres.")]
        public Vector2 wordsPadding = new Vector2(0.016f, 0.010f);

        [Header("Shape (metres, office scale 1)")]
        [Tooltip("Office footprint (width, depth). Taken from the director when it has one.")]
        public Vector2 footprint = new Vector2(1.0f, 0.7f);
        public Vector2 footprintCenterOffset = new Vector2(0f, 0.12f);
        public float cornerRadius = 0.09f;
        public float rimWidth = 0.018f;
        [Tooltip("Height of the highlight above the floor plane.")]
        public float lift = 0.0025f;
        public float holdRingRadius = 0.13f;
        public float holdRingWidth = 0.032f;
        public float holdRingLift = 0.02f;
        public float cursorRadius = 0.03f;
        public float rayWidth = 0.004f;
        [Range(0f, 1f)] public float fillAlpha = 0.22f;
        [Range(0f, 1f)] public float fillAlphaHover = 0.40f;
        [Tooltip("Off: only the rim is drawn (if a translucent surface composites badly over passthrough).")]
        public bool showFill = true;

        const int RingSegments = 48;
        const int RayPoints = 14;

        FloorAimSession _session;
        MaterialPropertyBlock _block;
        Mesh _fillMesh, _rimMesh, _trackMesh, _ringMesh, _dotMesh;
        bool _built, _visible;
        int _colorKey = -1;
        int _ringShown = -1;
        string _wordsShown;
        MaterialPropertyBlock _wordsBlock;
        float _nextHandSearch;
        readonly AimHandSample[] _sim = new AimHandSample[2];
        readonly bool[] _simOn = new bool[2];
        readonly bool[] _simHolding = new bool[2], _simNear = new bool[2], _simUi = new bool[2];
        readonly Vector3[] _rayBuffer = new Vector3[RayPoints];
        FloorPlaceStage _stageShown = (FloorPlaceStage)(-1);
        float _placedAt = -100f;

        /// <summary>The interaction logic (null until the first step with a director).</summary>
        public FloorAimSession Session => _session;
        /// <summary>True while the highlight is on show.</summary>
        public bool Shown => _visible;

        void Awake()
        {
            if (visuals != null) visuals.SetActive(false);
        }

        void OnDisable()
        {
            if (_session != null) _session.Reset();     // a drag in progress is called off; the preview is as it was
            Show(false);
        }

        void OnDestroy()
        {
            DestroyMesh(_fillMesh); DestroyMesh(_rimMesh); DestroyMesh(_trackMesh); DestroyMesh(_ringMesh); DestroyMesh(_dotMesh);
        }

        static void DestroyMesh(Mesh m) { if (m != null) Destroy(m); }

        void Update()
        {
            if (director == null) return;
            // No floor preview, no floor placement moving, nothing on show: nothing to do.
            if (!_visible && !director.CanAim)
            {
                SpatialDirectorCore core = director.Core;
                FloorPlaceStage stage = core != null ? core.PlaceStage : FloorPlaceStage.None;
                if (stage != FloorPlaceStage.Moving && stage != FloorPlaceStage.Returning) return;
            }
            Step(Time.unscaledDeltaTime);
        }

        /// <summary>One frame of aiming. Update calls it; a Play Mode driver may call it with its own time step.</summary>
        public void Step(float dt)
        {
            if (director == null) return;
            SpatialDirectorCore core = director.Core;
            if (core == null) return;
            if (_session == null) _session = new FloorAimSession(core, HandClaimRegistry.Shared);
            _session.settings = settings;
            _session.Arbiter.settings = ownership;
            // A paper in hand is held even when no grab interactor reports it (its own transition hold).
            _session.Arbiter.externallyHeld = SpatialWorkspaceState.TransitionHeld;

            AimHandSample left = Interaction(0, _simOn[0] ? _sim[0] : ReadHand(0));
            AimHandSample right = Interaction(1, _simOn[1] ? _sim[1] : ReadHand(1));
            _session.Tick(dt, left, right);
            Render(core);
        }

        /// <summary>What the hand's interactors are doing (the probe), plus any simulated hold or hover.</summary>
        AimHandSample Interaction(int hand, AimHandSample s)
        {
            s = s.With(_simHolding[hand], _simNear[hand], _simUi[hand]);
            return HandInteractionProbe.Apply(s, hand);
        }

        // ───────────────────────── hands ─────────────────────────

        AimHandSample ReadHand(int index)
        {
            HandVisual visual = index == 0 ? leftHandVisual : rightHandVisual;
            if (visual == null)
            {
                if (Time.unscaledTime < _nextHandSearch) return AimHandSample.None;
                _nextHandSearch = Time.unscaledTime + 1f;       // at most once a second, and only while a preview is open
                ResolveHands();
                visual = index == 0 ? leftHandVisual : rightHandVisual;
                if (visual == null) return AimHandSample.None;
            }
            IHand hand = visual.Hand;
            if (hand == null || !hand.IsTrackedDataValid || !hand.IsPointerPoseValid) return AimHandSample.None;
            Pose pointer;
            if (!hand.GetPointerPose(out pointer)) return AimHandSample.None;
            return AimHandSample.Ray(pointer.position, pointer.rotation * Vector3.forward, hand.GetIndexFingerIsPinching());
        }

        /// <summary>Find the rig's two hand visuals by handedness. Uses what is in the scene; adds nothing.</summary>
        public void ResolveHands()
        {
            HandVisual[] all = FindObjectsByType<HandVisual>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < all.Length; i++)
            {
                HandVisual v = all[i];
                if (v == null || v.Hand == null) continue;
                bool isLeft = v.Hand.Handedness == Handedness.Left;
                if (isLeft && leftHandVisual == null) leftHandVisual = v;
                else if (!isLeft && rightHandVisual == null) rightHandVisual = v;
            }
        }

        // ───────────────────────── simulation (Play Mode driver, no hands needed) ─────────────────────────

        /// <summary>Stand in for a hand (0 = left, 1 = right) until <see cref="ClearSimulation"/>.</summary>
        public void SimulateHand(int hand, Vector3 origin, Vector3 direction, bool pinching)
        {
            if (hand < 0 || hand > 1) return;
            _simOn[hand] = true;
            _sim[hand] = AimHandSample.Ray(origin, direction, pinching);
        }

        /// <summary>Stand in for a hand that is not tracked.</summary>
        public void SimulateHandLost(int hand)
        {
            if (hand < 0 || hand > 1) return;
            _simOn[hand] = true;
            _sim[hand] = AimHandSample.None;
        }

        public bool Simulating(int hand) { return hand >= 0 && hand <= 1 && _simOn[hand]; }
        public AimHandSample SimulatedSample(int hand) { return _sim[hand]; }

        /// <summary>Stand in for what a hand's interactors are doing (holding a paper / stamp / card,
        /// hovering a near object, over a UI control), on top of what the real interactors report.</summary>
        public void SimulateInteraction(int hand, bool holding, bool nearHover, bool uiHover)
        {
            if (hand < 0 || hand > 1) return;
            _simHolding[hand] = holding; _simNear[hand] = nearHover; _simUi[hand] = uiHover;
        }

        public void ClearSimulation()
        {
            _simOn[0] = _simOn[1] = false;
            for (int i = 0; i < 2; i++) _simHolding[i] = _simNear[i] = _simUi[i] = false;
        }

        // ───────────────────────── drawing ─────────────────────────

        void Show(bool on)
        {
            if (on == _visible) return;
            _visible = on;
            _colorKey = -1;
            _ringShown = -1;
            _wordsShown = null;
            if (visuals != null) visuals.SetActive(on);
        }

        void Render(SpatialDirectorCore core)
        {
            FloorPlaceStage stage = core.PlaceStage;
            if (stage == FloorPlaceStage.Placed && _stageShown != FloorPlaceStage.Placed) _placedAt = Time.unscaledTime;
            bool travelling = stage == FloorPlaceStage.Moving || stage == FloorPlaceStage.Returning
                              || (stage == FloorPlaceStage.Placed && Time.unscaledTime - _placedAt < placedShowSeconds);
            bool previewing = _session.State != FloorAimState.Off;
            bool on = previewing || travelling;
            if (on && !_built) BuildMeshes(core);
            Show(on);
            _stageShown = stage;
            if (!on) return;

            // While the office travels the footprint marks where it is going.
            RootPose shown = previewing ? core.ShownPose : core.PlaceTarget;
            if (footprintRoot != null)
            {
                footprintRoot.SetPositionAndRotation(shown.position + Vector3.up * lift, shown.rotation);
                footprintRoot.localScale = new Vector3(shown.scale, shown.scale, shown.scale);
            }

            bool dragging = _session.State == FloorAimState.Dragging;
            bool hover = dragging || _session.State == FloorAimState.Pressing || _session.AnyOverFootprint;
            int key = (int)stage | (hover ? 16 : 0);
            if (key != _colorKey)
            {
                _colorKey = key;
                if (_block == null) _block = new MaterialPropertyBlock();
                Color c; float fill; bool solid = false;
                switch (stage)
                {
                    case FloorPlaceStage.SpotBlocked: c = OfficeTokens.Coral; fill = fillAlpha; break;
                    case FloorPlaceStage.RouteBlocked: c = OfficeTokens.Amber; fill = fillAlpha; break;
                    case FloorPlaceStage.RouteValid: c = OfficeTokens.Teal; fill = fillAlpha; break;
                    case FloorPlaceStage.NeedsConfirmation: c = OfficeTokens.Teal; fill = fillAlphaHover; solid = true; break;
                    case FloorPlaceStage.Moving: c = OfficeTokens.InfoBlue; fill = fillAlpha; break;
                    case FloorPlaceStage.Returning: c = OfficeTokens.Amber; fill = fillAlpha * 0.5f; break;
                    case FloorPlaceStage.Placed: c = OfficeTokens.Success; fill = fillAlphaHover; solid = true; break;
                    default: c = OfficeTokens.Coral; fill = fillAlpha; break;
                }
                Color rim = hover ? Color.Lerp(c, OfficeTokens.OnPlate, 0.35f) : solid ? Color.Lerp(c, OfficeTokens.OnPlate, 0.15f) : c;
                Tint(footprintRim, rim, 1f);
                Tint(footprintFill, c, hover ? Mathf.Max(fill, fillAlphaHover) : fill);
                Tint(holdFill, OfficeTokens.OnPlate, 1f);
                Tint(holdTrack, OfficeTokens.Plate, 1f);
                Tint(leftCursor, rim, 1f);
                Tint(rightCursor, rim, 1f);
                if (leftRay != null) OfficeTokens.Tint(leftRay, _block, rim);
                if (rightRay != null) OfficeTokens.Tint(rightRay, _block, rim);
            }
            if (footprintFill != null && footprintFill.gameObject.activeSelf != showFill) footprintFill.gameObject.SetActive(showFill);

            DrawHoldRing(0f);                       // no pinch-and-hold confirm since rc6
            DrawHand(0, core, leftRay, leftCursor);
            DrawHand(1, core, rightRay, rightCursor);
            DrawWords(core, shown);
        }

        /// <summary>The stage the footprint showed on the last frame it was drawn (for a driver).</summary>
        public FloorPlaceStage StageShown => _stageShown;

        /// <summary>The footprint's words on a small plate in front of its near edge, turned to the eye.</summary>
        void DrawWords(SpatialDirectorCore core, RootPose shown)
        {
            if (wordsRoot == null || wordsText == null) return;
            string words = core.FootprintWords;
            bool show = !string.IsNullOrEmpty(words);
            if (wordsRoot.gameObject.activeSelf != show) wordsRoot.gameObject.SetActive(show);   // no interactables inside
            if (!show) return;

            float s = shown.scale;
            Vector3 centre = shown.position + shown.rotation * new Vector3(footprintCenterOffset.x * s, 0f, footprintCenterOffset.y * s);
            Vector3 toUser = shown.rotation * Vector3.forward;             // the root's +Z points at the user
            Vector3 anchor = centre + toUser * (footprint.y * 0.5f * s + wordsGap) + Vector3.up * (wordsHeight + lift);
            Vector3 look = anchor - director.CurrentUser.head;
            Quaternion facing = look.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(look.normalized, Vector3.up) : shown.rotation;
            wordsRoot.SetPositionAndRotation(anchor, facing);

            if (words == _wordsShown) return;
            _wordsShown = words;
            wordsText.text = words;
            wordsText.color = OfficeTokens.OnPlate;
            if (wordsPlate == null) return;
            wordsText.ForceMeshUpdate();
            Bounds b = wordsText.textBounds;                                // in the text's local units
            Vector3 k = wordsText.transform.localScale;
            float w = Mathf.Max(0.04f, b.size.x * k.x) + 2f * wordsPadding.x;
            float h = Mathf.Max(0.02f, b.size.y * k.y) + 2f * wordsPadding.y;
            Transform plate = wordsPlate.transform;
            Vector3 tp = wordsText.transform.localPosition;
            plate.localPosition = new Vector3(tp.x + b.center.x * k.x, tp.y + b.center.y * k.y, tp.z + 0.0015f);   // 1.5 mm behind the text
            plate.localRotation = Quaternion.identity;
            plate.localScale = new Vector3(w, h, 1f);
            if (_wordsBlock == null) _wordsBlock = new MaterialPropertyBlock();
            OfficeTokens.Tint(wordsPlate, _wordsBlock, OfficeTokens.Plate);
        }

        void Tint(MeshFilter filter, Color c, float alpha)
        {
            if (filter == null) return;
            Renderer r = filter.GetComponent<Renderer>();
            if (r == null) return;
            c.a = alpha;
            OfficeTokens.Tint(r, _block, c);
        }

        void DrawHoldRing(float progress)
        {
            int segments = progress <= 0f ? 0 : Mathf.Clamp(Mathf.CeilToInt(progress * RingSegments), 1, RingSegments);
            if (segments == _ringShown) return;
            _ringShown = segments;
            bool show = segments > 0;
            if (holdTrack != null && holdTrack.gameObject.activeSelf != show) holdTrack.gameObject.SetActive(show);
            if (holdFill != null && holdFill.gameObject.activeSelf != show) holdFill.gameObject.SetActive(show);
            if (show && _ringMesh != null)
                _ringMesh.SetSubMesh(0, new SubMeshDescriptor(0, segments * 6, MeshTopology.Triangles));
        }

        void DrawHand(int hand, SpatialDirectorCore core, LineRenderer ray, MeshFilter cursor)
        {
            bool has = _session.HasCursor(hand);
            bool busy = _session.State == FloorAimState.Pressing || _session.State == FloorAimState.Dragging;
            bool show = has && (busy ? _session.ActiveHand == hand : core.FootprintContains(_session.Cursor(hand), rayShowDistance));

            if (cursor != null)
            {
                if (cursor.gameObject.activeSelf != show) cursor.gameObject.SetActive(show);
                if (show) cursor.transform.SetPositionAndRotation(_session.Cursor(hand) + Vector3.up * (lift + 0.002f), Quaternion.identity);
            }
            if (ray == null) return;
            if (ray.enabled != show) ray.enabled = show;
            if (!show) return;

            // A gentle arc: it leaves the hand along the pointer direction and lands on the
            // (smoothed, clamped) aim point, so smoothing never shows as a ray that misses its end.
            Vector3 p0 = _session.RayOrigin(hand);
            Vector3 p2 = _session.Cursor(hand) + Vector3.up * lift;
            Vector3 dir = _session.RayDirection(hand);
            float reach = (p2 - p0).magnitude;
            Vector3 p1 = dir.sqrMagnitude > 1e-8f ? p0 + dir.normalized * (reach * 0.5f) : (p0 + p2) * 0.5f;
            for (int i = 0; i < RayPoints; i++)
            {
                float t = i / (float)(RayPoints - 1), u = 1f - t;
                _rayBuffer[i] = p0 * (u * u) + p1 * (2f * u * t) + p2 * (t * t);
            }
            if (ray.positionCount != RayPoints) ray.positionCount = RayPoints;
            ray.SetPositions(_rayBuffer);
        }

        // ───────────────────────── meshes (generated once) ─────────────────────────

        void BuildMeshes(SpatialDirectorCore core)
        {
            _built = true;
            if (core != null) { footprint = core.footprint; footprintCenterOffset = core.floorSettings.footprintCenterOffset; }
            Vector3 centre = new Vector3(footprintCenterOffset.x, 0f, footprintCenterOffset.y);
            float hw = footprint.x * 0.5f, hd = footprint.y * 0.5f;
            float r = Mathf.Clamp(cornerRadius, 0.005f, Mathf.Min(hw, hd) - 0.005f);
            float w = Mathf.Clamp(rimWidth, 0.002f, Mathf.Min(hw, hd) * 0.5f);

            var outer = new List<Vector3>(40);
            var inner = new List<Vector3>(40);
            RoundedRect(outer, hw, hd, r, 8, centre);
            RoundedRect(inner, hw - w, hd - w, Mathf.Max(0.003f, r - w), 8, centre);

            _fillMesh = Fan("FloorAimFill", centre, inner);
            _rimMesh = Strip("FloorAimRim", outer, inner);

            var ringOuter = new List<Vector3>(RingSegments + 1);
            var ringInner = new List<Vector3>(RingSegments + 1);
            float ro = Mathf.Max(0.02f, holdRingRadius), ri = Mathf.Max(0.005f, holdRingRadius - holdRingWidth);
            for (int i = 0; i <= RingSegments; i++)
            {
                // Starts on the side nearest the user (+Z of the root) and fills clockwise seen from above.
                float a = i / (float)RingSegments * Mathf.PI * 2f;
                Vector3 d = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                ringOuter.Add(d * ro);
                ringInner.Add(d * ri);
            }
            _ringMesh = Strip("FloorAimHoldFill", ringOuter, ringInner);
            _ringMesh.MarkDynamic();
            _trackMesh = Strip("FloorAimHoldTrack", ringOuter, ringInner);

            var dot = new List<Vector3>(20);
            for (int i = 0; i < 20; i++)
            {
                float a = i / 20f * Mathf.PI * 2f;
                dot.Add(new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * Mathf.Max(0.005f, cursorRadius));
            }
            dot.Add(dot[0]);
            _dotMesh = Fan("FloorAimCursor", Vector3.zero, dot);

            if (footprintFill != null) footprintFill.sharedMesh = _fillMesh;
            if (footprintRim != null) footprintRim.sharedMesh = _rimMesh;
            if (holdFill != null)
            {
                holdFill.sharedMesh = _ringMesh;
                holdFill.transform.localPosition = centre + Vector3.up * (holdRingLift + 0.001f);
                holdFill.transform.localRotation = Quaternion.identity;
            }
            if (holdTrack != null)
            {
                holdTrack.sharedMesh = _trackMesh;
                holdTrack.transform.localPosition = centre + Vector3.up * holdRingLift;
                holdTrack.transform.localRotation = Quaternion.identity;
            }
            if (leftCursor != null) leftCursor.sharedMesh = _dotMesh;
            if (rightCursor != null) rightCursor.sharedMesh = _dotMesh;
            SetupRay(leftRay);
            SetupRay(rightRay);
        }

        void SetupRay(LineRenderer ray)
        {
            if (ray == null) return;
            ray.useWorldSpace = true;
            ray.positionCount = RayPoints;
            ray.startWidth = rayWidth;
            ray.endWidth = rayWidth * 0.6f;
            ray.numCapVertices = 2;
            ray.shadowCastingMode = ShadowCastingMode.Off;
            ray.receiveShadows = false;
            ray.enabled = false;
        }

        /// <summary>Outline of a rounded rectangle in the XZ plane, clockwise seen from above, closed
        /// (the first point is repeated at the end).</summary>
        static void RoundedRect(List<Vector3> pts, float hw, float hd, float r, int segmentsPerCorner, Vector3 centre)
        {
            pts.Clear();
            r = Mathf.Min(r, Mathf.Min(hw, hd));
            for (int corner = 0; corner < 4; corner++)
            {
                float cx = (corner == 0 || corner == 1) ? hw - r : -(hw - r);
                float cz = (corner == 0 || corner == 3) ? hd - r : -(hd - r);
                for (int i = 0; i <= segmentsPerCorner; i++)
                {
                    float a = (corner + i / (float)segmentsPerCorner) * Mathf.PI * 0.5f;
                    pts.Add(centre + new Vector3(cx + Mathf.Sin(a) * r, 0f, cz + Mathf.Cos(a) * r));
                }
            }
            pts.Add(pts[0]);
        }

        /// <summary>A filled shape: triangles from a centre point to a closed outline. Faces up.</summary>
        static Mesh Fan(string name, Vector3 centre, List<Vector3> outline)
        {
            int n = outline.Count;
            var verts = new Vector3[n + 1];
            var normals = new Vector3[n + 1];
            verts[0] = centre; normals[0] = Vector3.up;
            for (int i = 0; i < n; i++) { verts[i + 1] = outline[i]; normals[i + 1] = Vector3.up; }
            var tris = new int[(n - 1) * 3];
            for (int i = 0; i < n - 1; i++)
            {
                tris[i * 3] = 0; tris[i * 3 + 1] = i + 1; tris[i * 3 + 2] = i + 2;
            }
            return Make(name, verts, normals, tris);
        }

        /// <summary>A band between two outlines with the same number of points. Faces up. Segment k
        /// uses indices k*6 .. k*6+5, so a prefix of the index buffer is a prefix of the band.</summary>
        static Mesh Strip(string name, List<Vector3> outer, List<Vector3> inner)
        {
            int n = Mathf.Min(outer.Count, inner.Count);
            var verts = new Vector3[n * 2];
            var normals = new Vector3[n * 2];
            for (int i = 0; i < n; i++)
            {
                verts[i * 2] = outer[i]; verts[i * 2 + 1] = inner[i];
                normals[i * 2] = normals[i * 2 + 1] = Vector3.up;
            }
            var tris = new int[(n - 1) * 6];
            for (int i = 0; i < n - 1; i++)
            {
                int o0 = i * 2, i0 = o0 + 1, o1 = o0 + 2, i1 = o0 + 3, t = i * 6;
                tris[t] = o0; tris[t + 1] = o1; tris[t + 2] = i0;
                tris[t + 3] = i0; tris[t + 4] = o1; tris[t + 5] = i1;
            }
            return Make(name, verts, normals, tris);
        }

        static Mesh Make(string name, Vector3[] verts, Vector3[] normals, int[] tris)
        {
            var m = new Mesh { name = name, hideFlags = HideFlags.DontSave };
            m.vertices = verts;
            m.normals = normals;
            m.triangles = tris;
            m.RecalculateBounds();
            return m;
        }

        /// <summary>One line for a Play Mode driver.</summary>
        public string Describe()
        {
            if (_session == null) return "aim: not started";
            SpatialDirectorCore core = director != null ? director.Core : null;
            HandOwnershipArbiter arb = _session.Arbiter;
            string s = "aim: state=" + _session.State + " hand=" + _session.ActiveHand
                       + " grabs=" + _session.Grabs + " drops=" + _session.Drops + " cancels=" + _session.Cancels
                       + " held=" + _session.HeldSeconds.ToString("F2") + "s"
                       + " owner L=" + arb.Owner(0) + "/" + arb.LastBlock(0) + " R=" + arb.Owner(1) + "/" + arb.LastBlock(1)
                       + " anyHeld=" + arb.AnyHeld
                       + " stage=" + (core != null ? core.PlaceStage.ToString() : "?") + " canPlaceHere=" + (core != null && core.CanPlaceHere)
                       + " shown=" + _visible
                       + " L(cursor=" + _session.HasCursor(0) + " over=" + _session.OverFootprint(0) + (_session.HasCursor(0) ? " at " + _session.Cursor(0).ToString("F2") : "") + ")"
                       + " R(cursor=" + _session.HasCursor(1) + " over=" + _session.OverFootprint(1) + (_session.HasCursor(1) ? " at " + _session.Cursor(1).ToString("F2") : "") + ")"
                       + " hands=" + (leftHandVisual != null ? "L" : "-") + (rightHandVisual != null ? "R" : "-");
            if (core != null && core.HasPreview)
                s += " | footprint at " + core.ShownPose.position.ToString("F2") + " yaw=" + core.ShownPose.rotation.eulerAngles.y.ToString("F0")
                     + " valid=" + core.ShownValid + " roomChecked=" + core.ShownRoomChecked + " dragging=" + core.AimDragging
                     + " dragged=" + core.AimDragDisplacement.ToString("F2") + "m"
                     + (string.IsNullOrEmpty(core.AimProblem) ? "" : " problem='" + core.AimProblem + "'")
                     + " words='" + core.FootprintWords + "'"
                     + " wordsDrawn=" + (wordsRoot != null && wordsRoot.gameObject.activeInHierarchy);
            return s;
        }
    }
}
