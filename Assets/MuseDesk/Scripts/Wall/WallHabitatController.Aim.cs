using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using MuseDesk.Spatial;
using MuseDesk.Spatial.Wall;

namespace MuseDesk.Wall
{
    /// <summary>
    /// rc12 (owner: "use the same system as the floor placement, the beam, dragging it around clearer, reasons you can or
    /// can't place it"). While a wall preview is open, each tracked hand that points at a scanned wall gets the Floor
    /// placement's arc (same material and width as <see cref="FloorAimController"/>), a ring cursor lying on the wall, and a
    /// small chip by the cursor saying whether the habitat would fit there and why not. The hand that is dragging the ghost
    /// draws its arc to the ghost's spot in the ghost's colour. Presentation only: the spot under the cursor is evaluated
    /// (<see cref="WallPlacementEvaluator.FromRay"/>, at most ten times a second per hand) but nothing changes until a pinch
    /// drags the ghost, and nothing is placed until Place.
    /// </summary>
    public partial class WallHabitatController
    {
        [Header("Wall aim (rc12: Floor-style beam, cursor and reason chip)")]
        public float aimRayWidth = 0.004f;
        public float aimCursorRadius = 0.035f;
        public float aimHoverInterval = 0.1f;

        const int AimRayPoints = 14;
        readonly LineRenderer[] _aimRays = new LineRenderer[2];
        readonly Transform[] _aimCursors = new Transform[2];
        readonly Renderer[] _aimCursorR = new Renderer[2];
        readonly Transform[] _aimChips = new Transform[2];
        readonly TMP_Text[] _aimChipText = new TMP_Text[2];
        readonly Renderer[] _aimChipPlate = new Renderer[2];
        readonly float[] _aimNextEval = new float[2];
        readonly WallCandidate[] _aimHover = new WallCandidate[2];
        readonly bool[] _aimHoverValid = new bool[2];
        readonly Vector3[] _aimBuf = new Vector3[AimRayPoints];
        Transform _aimRoot;
        bool _aimShown;
        MaterialPropertyBlock _aimBlock;
        static readonly int AimBaseColor = Shader.PropertyToID("_BaseColor"), AimColor = Shader.PropertyToID("_Color");

        /// <summary>For drivers: is a hand's beam on show, and what does its chip say.</summary>
        public bool AimBeamShown(int hand) { return hand >= 0 && hand < 2 && _aimRays[hand] != null && _aimRays[hand].enabled; }
        public string AimChipText(int hand) { return hand >= 0 && hand < 2 && _aimChipText[hand] != null && _aimChips[hand].gameObject.activeSelf ? _aimChipText[hand].text : null; }

        void TickWallAim(bool aimMode, float dt)
        {
            IWallGeometry walls = roomService != null ? roomService.Walls : null;
            if (!aimMode || walls == null || walls.WallAvailability != RoomAvailability.Available)
            {
                if (_aimShown) { HideAim(0); HideAim(1); _aimShown = false; }
                return;
            }
            if (_aimRoot == null) BuildAim();
            _aimShown = true;
            SeatedUser user = ReadUser();
            for (int i = 0; i < 2; i++)
            {
                AimHandSample s = _samples[i];
                int wi; float u, y, dist;
                if (!s.tracked || !WallMath.Raycast(walls, s.origin, s.direction, 8f, out wi, out u, out y, out dist)) { HideAim(i); continue; }
                RoomWall w = walls.GetWall(wi);
                Vector3 hit = s.origin + s.direction * dist;
                bool dragging = i == _aimHand && _flow.HasShown;
                Color col;
                string words;
                if (dragging)
                {
                    WallCandidate c = _flow.Shown;
                    col = !c.valid ? WallHabitatView.Coral : c.needsReview ? WallHabitatView.Amber : WallHabitatView.Teal;
                    words = c.valid ? "Let go, then press Place" : WallPlacementEvaluator.Words(c.problem) + " - let go to snap to a fit";
                }
                else
                {
                    if (Time.unscaledTime >= _aimNextEval[i])
                    {
                        _aimNextEval[i] = Time.unscaledTime + aimHoverInterval;
                        _aimHoverValid[i] = WallPlacementEvaluator.FromRay(roomService.Room, walls, s.origin, s.direction, _flow.Layout, user, _flow.settings, out _aimHover[i]);
                    }
                    WallCandidate c = _aimHover[i];
                    bool ok = _aimHoverValid[i] && c.valid;
                    col = !_aimHoverValid[i] ? WallHabitatView.Grey : !c.valid ? WallHabitatView.Coral : c.needsReview ? WallHabitatView.Amber : WallHabitatView.Teal;
                    words = !_aimHoverValid[i] ? null : ok ? "Pinch to move it here" : "Can't: " + WallPlacementEvaluator.Words(c.problem).ToLowerInvariant();
                }
                DrawAim(i, s.origin, s.direction, hit, w.normal, col, words, user.head);
            }
        }

        // ───────────── Select Agents ray (owner rc13: "no indicator ... a ray from your hand that shows which one you're hovering") ─────────────

        bool _selectAimShown;
        readonly int[] _selectAimTarget = { -1, -1 };

        /// <summary>For drivers: the agent a hand's select ray is on (-1 none), and whether that ray is drawn.</summary>
        public int SelectAimTarget(int hand) { return hand >= 0 && hand < 2 ? _selectAimTarget[hand] : -1; }
        public bool SelectAimShown => _selectAimShown;

        /// <summary>
        /// While Select Agents is armed (and nothing is carried), each tracked free hand gets the placement beam. On an
        /// agent (the same forgiving target and per-hand lock the pinch uses) it bends to that agent's chest in teal, a
        /// ring sits in front of it and a chip says "Pinch to pick up Developer". Pointing at nothing it is a dim grey beam
        /// ending on the habitat's face. Presentation only: the pinch path is unchanged.
        /// </summary>
        void TickSelectAim(bool picking)
        {
            if (!picking || _view == null || _habitatRoot == null)
            {
                if (_selectAimShown) { HideAim(0); HideAim(1); _selectAimShown = false; }
                _selectAimTarget[0] = _selectAimTarget[1] = -1;
                return;
            }
            if (_aimRoot == null) BuildAim();
            _selectAimShown = true;
            Vector3 eye = ResolveHead() != null ? ResolveHead().position : (_samples[0].tracked ? _samples[0].origin : _habitatRoot.position + _habitatRoot.forward);
            float scale = _habitatRoot.lossyScale.y;
            for (int i = 0; i < 2; i++)
            {
                AimHandSample s = _samples[i];
                if (!s.tracked || i == _carryHand) { HideAim(i); _selectAimTarget[i] = -1; continue; }
                int target = useSelectGrabV2 ? _lock.Locked(i) : (RayPickAgent(s.origin, s.direction));
                if (target >= 0 && useSelectGrabV2 && !_targetEligible[target]) target = -1;
                _selectAimTarget[i] = target;
                if (target >= 0)
                {
                    Transform a = _view.Avatar((HabitatRole)target);
                    Vector3 chest = a.position + _habitatRoot.up * (_view.AvatarHeight * 0.5f * scale);
                    Vector3 toEye = (eye - chest); toEye = toEye.sqrMagnitude > 1e-6f ? toEye.normalized : _habitatRoot.forward;
                    // The ring sits just in front of the chest (toward the user), so the body does not hide half of it.
                    Vector3 hit = chest + toEye * (_view.AvatarHeight * 0.22f * scale);
                    DrawAim(i, s.origin, s.direction, hit, toEye, WallHabitatView.Teal, "Pinch to pick up " + (HabitatRole)target, eye);
                }
                else
                {
                    // Nothing under the ray: end it on the habitat's face (or 1 m out), grey, no words.
                    Vector3 n = _habitatRoot.forward;
                    float den = Vector3.Dot(s.direction, -n);
                    float t = den > 0.05f ? Vector3.Dot(s.origin - _habitatRoot.position, n) / den : -1f;
                    if (t < 0.2f || t > 6f) t = 1f;
                    DrawAim(i, s.origin, s.direction, s.origin + s.direction * t, den > 0.05f ? n : -s.direction, WallHabitatView.Grey, null, eye);
                }
            }
        }

        void DrawAim(int i, Vector3 origin, Vector3 dir, Vector3 hit, Vector3 normal, Color col, string words, Vector3 eye)
        {
            LineRenderer ray = _aimRays[i];
            Vector3 p2 = hit + normal * 0.003f;
            float reach = (p2 - origin).magnitude;
            Vector3 p1 = origin + dir.normalized * (reach * 0.5f);
            for (int k = 0; k < AimRayPoints; k++)
            {
                float t = k / (float)(AimRayPoints - 1), v = 1f - t;
                _aimBuf[k] = origin * (v * v) + p1 * (2f * v * t) + p2 * (t * t);
            }
            ray.SetPositions(_aimBuf);
            ray.startColor = new Color(col.r, col.g, col.b, 0.25f);
            ray.endColor = new Color(col.r, col.g, col.b, 0.95f);
            if (ray.sharedMaterial != null) { ray.GetPropertyBlock(_aimBlock); _aimBlock.SetColor(AimBaseColor, col); _aimBlock.SetColor(AimColor, col); ray.SetPropertyBlock(_aimBlock); }
            if (!ray.enabled) ray.enabled = true;

            Transform cur = _aimCursors[i];
            cur.SetPositionAndRotation(p2, Quaternion.LookRotation(-normal, Vector3.up));
            // The ring grows a little with distance so it reads the same size from the seat.
            float k2 = Mathf.Clamp(reach / 1.2f, 0.8f, 2.2f);
            cur.localScale = Vector3.one * k2;
            _aimCursorR[i].GetPropertyBlock(_aimBlock); _aimBlock.SetColor(AimBaseColor, col); _aimBlock.SetColor(AimColor, col); _aimCursorR[i].SetPropertyBlock(_aimBlock);
            if (!cur.gameObject.activeSelf) cur.gameObject.SetActive(true);

            Transform chip = _aimChips[i];
            bool showChip = !string.IsNullOrEmpty(words);
            if (showChip)
            {
                Vector3 at = p2 + normal * 0.02f + Vector3.down * (0.065f * k2);
                chip.SetPositionAndRotation(at, WallUi.FacingEye(at, eye));
                chip.localScale = Vector3.one * k2;
                if (_aimChipText[i].text != words)
                {
                    _aimChipText[i].text = words;
                    _aimChipText[i].ForceMeshUpdate();
                    float w = Mathf.Clamp(_aimChipText[i].GetPreferredValues(words, 40f, 3f).x * 0.01f + 0.02f, 0.06f, 0.30f);
                    _aimChipPlate[i].transform.localScale = WallUi.PlateMaterial != null ? new Vector3(w / 0.1f, 1f, 1f) : new Vector3(w, 0.026f, 0.002f);
                }
                WallUi.TintPlate(_aimChipPlate[i], Color.Lerp(WallCard.DarkPlate, col, 0.22f));
            }
            if (chip.gameObject.activeSelf != showChip) chip.gameObject.SetActive(showChip);
        }

        void HideAim(int i)
        {
            if (_aimRays[i] != null && _aimRays[i].enabled) _aimRays[i].enabled = false;
            if (_aimCursors[i] != null && _aimCursors[i].gameObject.activeSelf) _aimCursors[i].gameObject.SetActive(false);
            if (_aimChips[i] != null && _aimChips[i].gameObject.activeSelf) _aimChips[i].gameObject.SetActive(false);
        }

        void BuildAim()
        {
            _aimBlock = new MaterialPropertyBlock();
            _aimRoot = new GameObject("WallAim").transform;
            _aimRoot.SetParent(transform, false);
            // The Floor placement's look: its arc material and its cursor material (FloorAimController in the scene).
            Material rayMat = null, dotMat = null;
            var floor = FindAnyObjectByType<FloorAimController>(FindObjectsInactive.Include);
            if (floor != null)
            {
                if (floor.leftRay != null) rayMat = floor.leftRay.sharedMaterial;
                if (floor.leftCursor != null) { var mr = floor.leftCursor.GetComponent<MeshRenderer>(); if (mr != null) dotMat = mr.sharedMaterial; }
            }
            if (rayMat == null) rayMat = ResolveMaterial();
            if (dotMat == null) dotMat = rayMat;
            Mesh ring = RingMesh(aimCursorRadius, aimCursorRadius * 0.62f, 32);
            for (int i = 0; i < 2; i++)
            {
                var rg = new GameObject(i == 0 ? "AimRayL" : "AimRayR");
                rg.transform.SetParent(_aimRoot, false);
                var lr = rg.AddComponent<LineRenderer>();
                lr.sharedMaterial = rayMat;
                lr.useWorldSpace = true;
                lr.positionCount = AimRayPoints;
                lr.startWidth = aimRayWidth; lr.endWidth = aimRayWidth * 0.6f;
                lr.numCapVertices = 2;
                lr.shadowCastingMode = ShadowCastingMode.Off; lr.receiveShadows = false;
                lr.enabled = false;
                _aimRays[i] = lr;

                var cg = new GameObject(i == 0 ? "AimCursorL" : "AimCursorR");
                cg.transform.SetParent(_aimRoot, false);
                cg.AddComponent<MeshFilter>().sharedMesh = ring;
                var cr = cg.AddComponent<MeshRenderer>();
                cr.sharedMaterial = dotMat; cr.shadowCastingMode = ShadowCastingMode.Off; cr.receiveShadows = false;
                _aimCursors[i] = cg.transform; _aimCursorR[i] = cr;
                cg.SetActive(false);

                var chip = new GameObject(i == 0 ? "AimChipL" : "AimChipR").transform;
                chip.SetParent(_aimRoot, false);
                // The cards' look: a dark rounded plate (unlit, the task dock's material) and light words.
                _aimChipPlate[i] = WallUi.Plate(chip, "Plate", new Vector3(0f, 0f, 0.0015f), 0.1f, 0.026f, 0.009f, WallCard.DarkPlate, WallUi.PlateMaterial, ArtHelper());
                _aimChipText[i] = WallUi.MakeText(chip, "Words", font, Vector3.zero, new Vector2(30f, 2.4f), 5.2f, WallCard.DarkTitle, TextAlignmentOptions.Center);
                _aimChipText[i].textWrappingMode = TextWrappingModes.NoWrap;
                _aimChipText[i].overflowMode = TextOverflowModes.Overflow;
                _aimChips[i] = chip;
                chip.gameObject.SetActive(false);
            }
        }

        /// <summary>A flat ring in the XY plane facing -Z (the side a FacingEye rotation shows), both faces.</summary>
        static Mesh RingMesh(float outer, float inner, int segments)
        {
            var v = new Vector3[(segments + 1) * 2];
            var tris = new int[segments * 12];
            for (int i = 0; i <= segments; i++)
            {
                float a = i / (float)segments * Mathf.PI * 2f;
                Vector3 d = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
                v[i * 2] = d * outer; v[i * 2 + 1] = d * inner;
            }
            for (int i = 0; i < segments; i++)
            {
                int o0 = i * 2, i0 = o0 + 1, o1 = o0 + 2, i1 = o0 + 3, t = i * 12;
                tris[t] = o0; tris[t + 1] = o1; tris[t + 2] = i0; tris[t + 3] = i0; tris[t + 4] = o1; tris[t + 5] = i1;
                tris[t + 6] = o0; tris[t + 7] = i0; tris[t + 8] = o1; tris[t + 9] = i0; tris[t + 10] = i1; tris[t + 11] = o1;
            }
            var m = new Mesh { name = "WallAimRing", vertices = v, triangles = tris };
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }
    }
}
