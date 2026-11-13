using UnityEngine;

namespace MuseDesk.Spatial
{
    /// <summary>
    /// The floor twin of <see cref="CeilingScanReveal"/>: a cheap scan-reveal grid on the floor while
    /// the Floor preview is starting. Same shader (MuseDesk/Spatial/CeilingScanGrid), same timing logic
    /// (<see cref="CeilingScanRevealLogic"/>). When the guided step handed over a gaze, the radial wipe
    /// starts where the user was looking and the grid slides to the candidate spot; otherwise it wipes
    /// at the candidate. It fades out after <see cref="showSeconds"/> (the ghost outline and the
    /// pointing take over) and at once on confirm, cancel or leaving the preview. Read-only on the
    /// director; lives at the scene root (world space).
    /// The floor height is the previewed root pose's height (the office stands on the floor it was
    /// validated against, from room data or the tracked floor).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FloorScanReveal : MonoBehaviour
    {
        [Header("Wiring")]
        public SpatialWorkspaceDirector director;
        public Material material;

        [Header("Look")]
        public float gridWidth = 2.4f;
        public float gridCell = 0.15f;
        public float lineWidth = 0.005f;
        [Tooltip("Radius of the footprint ring: about the size of the floor office.")]
        public float ringRadius = 0.45f;
        public float ringWidth = 0.014f;
        [Tooltip("Gap above the floor, metres.")]
        public float aboveFloor = 0.003f;
        [Tooltip("Seconds the grid stays on after the preview opens.")]
        public float showSeconds = 3.5f;
        public Color lineColour = new Color(0.184f, 0.639f, 0.604f, 0.6f);
        public Color invalidColour = new Color(0.86f, 0.27f, 0.24f, 0.6f);

        static readonly int IdColor = Shader.PropertyToID("_BaseColor");
        static readonly int IdReveal = Shader.PropertyToID("_RevealRadius");
        static readonly int IdSize = Shader.PropertyToID("_Size");
        static readonly int IdCell = Shader.PropertyToID("_Cell");
        static readonly int IdLine = Shader.PropertyToID("_LineWidth");
        static readonly int IdFalloff = Shader.PropertyToID("_FalloffRadius");
        static readonly int IdRing = Shader.PropertyToID("_RingRadius");
        static readonly int IdRingW = Shader.PropertyToID("_RingWidth");
        static readonly int IdFade = Shader.PropertyToID("_Fade");

        public const float SweepSeconds = 0.8f;

        MeshRenderer _renderer;
        Transform _quad;
        Mesh _mesh;
        MaterialPropertyBlock _mpb;
        bool _wasWanted, _haveCentre, _red, _sweeping;
        Vector3 _centre, _cand, _sweepFrom;
        float _yaw, _wipeT, _fade, _sweepT, _shownFor;

        public float Fade => _fade;
        public bool Shown => _renderer != null && _renderer.enabled;
        public Vector3 Centre => _centre;
        public bool Sweeping => _sweeping;
        public Vector3 SweepFrom => _sweepFrom;
        public bool Red => _red;
        public Transform Quad => _quad;

        void Awake()
        {
            var go = new GameObject("FloorScanQuad");
            go.hideFlags = HideFlags.DontSave;
            _quad = go.transform;
            _quad.SetParent(transform, false);
            _mesh = new Mesh { name = "FloorScanQuad" };
            // A 1 x 1 m quad in XZ, UV 0..1, normal +Y (visible from above; the shader is double sided anyway).
            _mesh.vertices = new[]
            {
                new Vector3(-0.5f, 0f, -0.5f), new Vector3(-0.5f, 0f, 0.5f),
                new Vector3(0.5f, 0f, 0.5f), new Vector3(0.5f, 0f, -0.5f),
            };
            _mesh.uv = new[] { new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0) };
            _mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            _mesh.RecalculateBounds();
            go.AddComponent<MeshFilter>().sharedMesh = _mesh;
            _renderer = go.AddComponent<MeshRenderer>();
            _renderer.sharedMaterial = material;
            _renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _renderer.receiveShadows = false;
            _renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            _renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            _renderer.enabled = false;
            _mpb = new MaterialPropertyBlock();
        }

        void OnDestroy() { if (_mesh != null) Destroy(_mesh); }

        void Update()
        {
            if (director == null) director = FindAnyObjectByType<SpatialWorkspaceDirector>();
            SpatialDirectorCore core = director != null ? director.Core : null;
            float dt = Time.unscaledDeltaTime;

            bool previewing = core != null && core.Phase == DirectorPhase.Previewing && core.PendingMode == PlacementMode.Floor;
            if (previewing && _wasWanted) _shownFor += dt;
            bool wanted = previewing && _shownFor < showSeconds;

            if (wanted)
            {
                RootPose pose = core.PreviewPose;
                Vector3 c = pose.position;
                c.y += Mathf.Max(0f, aboveFloor);
                float moved = _haveCentre ? new Vector2(c.x - _cand.x, c.z - _cand.z).magnitude : 0f;
                bool restart = CeilingScanRevealLogic.ShouldRestart(_wasWanted, true, false, moved);
                if (restart)
                {
                    _wipeT = 0f; _sweepT = 0f; _sweeping = false;
                    if (!_wasWanted)
                    {
                        _shownFor = 0f;
                        if (PlacementGuide.TryConsumeGaze(out Vector3 go, out Vector3 gd)
                            && PlacementGuideLogic.RayPlane(go, gd, c.y, out Vector3 hit))
                        {
                            _sweepFrom = PlacementGuideLogic.ClampOrigin(hit, c, 4f);
                            _sweepFrom.y = c.y;
                            _sweeping = true;
                        }
                    }
                }
                _cand = c;
                _centre = _sweeping ? PlacementGuideLogic.SweepCentre(_sweepFrom, c, _sweepT, SweepSeconds) : c;
                _haveCentre = true;
                _yaw = pose.rotation.eulerAngles.y;
                _red = !core.ShownValid;
            }
            else if (!previewing)
            {
                _shownFor = 0f;
            }
            _wasWanted = wanted;

            _fade = CeilingScanRevealLogic.StepFade(_fade, wanted, dt, CeilingScanRevealLogic.FadeInSeconds, CeilingScanRevealLogic.FadeOutSeconds);
            _wipeT = CeilingScanRevealLogic.StepWipe(_wipeT, dt, CeilingScanRevealLogic.WipeSeconds);
            if (_sweeping)
            {
                _sweepT += dt;
                if (wanted) _centre = PlacementGuideLogic.SweepCentre(_sweepFrom, _cand, _sweepT, SweepSeconds);
                if (_sweepT >= SweepSeconds) _sweeping = false;
            }

            bool visible = CeilingScanRevealLogic.Visible(_fade) && _haveCentre && material != null;
            if (_renderer.enabled != visible) _renderer.enabled = visible;
            if (!visible) return;

            _quad.SetPositionAndRotation(_centre, Quaternion.Euler(0f, _yaw, 0f));
            _quad.localScale = new Vector3(gridWidth, 1f, gridWidth);

            _renderer.GetPropertyBlock(_mpb);
            _mpb.SetColor(IdColor, _red ? invalidColour : lineColour);
            _mpb.SetFloat(IdReveal, CeilingScanRevealLogic.RevealRadius(_wipeT, CeilingScanRevealLogic.WipeSeconds, gridWidth * 0.5f * 1.1f));
            _mpb.SetFloat(IdSize, gridWidth);
            _mpb.SetFloat(IdCell, gridCell);
            _mpb.SetFloat(IdLine, lineWidth);
            _mpb.SetFloat(IdFalloff, gridWidth * 0.5f);
            _mpb.SetFloat(IdRing, ringRadius);
            _mpb.SetFloat(IdRingW, ringWidth);
            _mpb.SetFloat(IdFade, _fade);
            _renderer.SetPropertyBlock(_mpb);
        }
    }
}
