using UnityEngine;

namespace MuseDesk.Spatial
{
    /// <summary>
    /// A scan-reveal grid on the real ceiling while the ceiling pod's dock spot is being chosen.
    /// Reads the director (read-only): previewing a Ceiling placement, the candidate's mount point
    /// and the real ceiling height it was validated against. One flat quad 2 mm under the ceiling,
    /// a radial wipe from the dock point, teal grid lines that fade with distance, and a footprint
    /// ring where the mount will be. Red when the shown candidate is not valid. Fades out over
    /// 0.3 s on confirm, cancel or leaving Ceiling mode. Lives at the scene root (world space),
    /// not under WorkspaceRoot, so the office moving never drags it.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CeilingScanReveal : MonoBehaviour
    {
        [Header("Wiring")]
        public SpatialWorkspaceDirector director;
        public Material material;

        [Header("Look")]
        public float gridWidth = 1.6f;
        public float gridCell = 0.1f;
        public float lineWidth = 0.004f;
        [Tooltip("Radius of the footprint ring: about the size of the ceiling mount.")]
        public float ringRadius = 0.18f;
        public float ringWidth = 0.012f;
        [Tooltip("Gap under the real ceiling, metres.")]
        public float belowCeiling = 0.002f;
        public Color lineColour = new Color(0.184f, 0.639f, 0.604f, 0.6f);   // Teal #2FA39A
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

        MeshRenderer _renderer;
        Transform _quad;
        Mesh _mesh;
        MaterialPropertyBlock _mpb;
        bool _wasWanted;
        int _lastIndex = -1;
        Vector3 _centre;
        float _yaw;
        bool _red;
        float _wipeT;
        float _fade;
        bool _haveCentre;

        // Read by the driver.
        public float Fade => _fade;
        public float WipeSeconds => _wipeT;
        public float RevealRadiusNow => CeilingScanRevealLogic.RevealRadius(_wipeT, CeilingScanRevealLogic.WipeSeconds, gridWidth * 0.5f * 1.1f);
        public Vector3 Centre => _centre;
        public bool Red => _red;
        public bool Shown => _renderer != null && _renderer.enabled;
        public Transform Quad => _quad;

        void Awake()
        {
            var go = new GameObject("CeilingScanQuad");
            go.hideFlags = HideFlags.DontSave;
            _quad = go.transform;
            _quad.SetParent(transform, false);
            _mesh = new Mesh { name = "CeilingScanQuad" };
            // A 1 x 1 m quad in XZ facing down (-Y), UV 0..1. Scaled by gridWidth at show time.
            _mesh.vertices = new[]
            {
                new Vector3(-0.5f, 0f, -0.5f), new Vector3(-0.5f, 0f, 0.5f),
                new Vector3(0.5f, 0f, 0.5f), new Vector3(0.5f, 0f, -0.5f),
            };
            _mesh.uv = new[] { new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0) };
            _mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };   // normal -Y
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

        void OnDestroy()
        {
            if (_mesh != null) Destroy(_mesh);
        }

        void Update()
        {
            if (director == null) director = FindAnyObjectByType<SpatialWorkspaceDirector>();
            SpatialDirectorCore core = director != null ? director.Core : null;

            bool previewing = core != null && core.Phase == DirectorPhase.Previewing;
            bool pendingCeiling = previewing && core.PendingMode == PlacementMode.Ceiling;
            CeilingPlacement p = pendingCeiling ? core.LastCeiling : default(CeilingPlacement);
            bool wanted = CeilingScanRevealLogic.WantShown(previewing, pendingCeiling, p.ceilingHeight);

            if (wanted)
            {
                Vector3 c = p.valid ? p.mastTop : new Vector3(p.dockPosition.x, p.ceilingHeight, p.dockPosition.z);
                c.y = p.ceilingHeight - Mathf.Max(0f, belowCeiling);
                int index = core.CandidateIndex;
                float moved = _haveCentre ? new Vector2(c.x - _centre.x, c.z - _centre.z).magnitude : 0f;
                bool restart = CeilingScanRevealLogic.ShouldRestart(_wasWanted, true, index != _lastIndex && _lastIndex >= 0, moved);
                if (restart) _wipeT = 0f;
                _lastIndex = index;
                _centre = c;
                _haveCentre = true;
                _yaw = p.dockRotation.eulerAngles.y;
                _red = !p.valid || !core.ShownValid;
            }
            else if (!_wasWanted && _fade <= 0f)
            {
                _lastIndex = -1;
            }
            _wasWanted = wanted;

            float dt = Time.unscaledDeltaTime;
            _fade = CeilingScanRevealLogic.StepFade(_fade, wanted, dt, CeilingScanRevealLogic.FadeInSeconds, CeilingScanRevealLogic.FadeOutSeconds);
            _wipeT = CeilingScanRevealLogic.StepWipe(_wipeT, dt, CeilingScanRevealLogic.WipeSeconds);

            bool visible = CeilingScanRevealLogic.Visible(_fade) && _haveCentre && material != null;
            if (_renderer.enabled != visible) _renderer.enabled = visible;
            if (!visible) return;

            _quad.SetPositionAndRotation(_centre, Quaternion.Euler(0f, _yaw, 0f));
            _quad.localScale = new Vector3(gridWidth, 1f, gridWidth);

            _renderer.GetPropertyBlock(_mpb);
            _mpb.SetColor(IdColor, _red ? invalidColour : lineColour);
            _mpb.SetFloat(IdReveal, RevealRadiusNow);
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
