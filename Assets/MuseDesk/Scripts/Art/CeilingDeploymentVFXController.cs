using UnityEngine;

namespace MuseDesk.Art
{
    /// <summary>Reversible ceiling deployment VFX budget presets for Quest 3 profiling.</summary>
    public enum CeilingVfxQualityPreset
    {
        Full = 0,
        LowCostNoParticles = 1,
        MinimalIrisHalo = 2
    }

    /// <summary>
    /// Synchronizes ceiling portal projection glow, iris bay particles, directional tether flow motes,
    /// and air-cushion landing/docking ripples with <see cref="CeilingWorkspacePodController"/> transitions.
    /// Zero dynamic lights or per-frame material instances; all shader modulation uses MaterialPropertyBlock.
    /// </summary>
    [DisallowMultipleComponent]
    public class CeilingDeploymentVFXController : MonoBehaviour
    {
        private static readonly int BaseColorId     = Shader.PropertyToID("_BaseColor");
        private static readonly int IntensityId     = Shader.PropertyToID("_Intensity");
        private static readonly int ScanDirectionId = Shader.PropertyToID("_ScanDirection");
        private static readonly int ScanSpeedId     = Shader.PropertyToID("_ScanSpeed");
        private static readonly int RingModeId      = Shader.PropertyToID("_RingMode");

        private static readonly Color ColTealCalm = new Color(0.184f, 0.720f, 0.700f, 1f);
        private static readonly Color ColCyanGlow = new Color(0.320f, 0.880f, 0.940f, 1f);
        private static readonly Color ColWarmGold = new Color(0.950f, 0.760f, 0.340f, 1f);
        private static readonly Color ColLockMint = new Color(0.240f, 0.840f, 0.560f, 1f);

        [Header("Core Binding")]
        [SerializeField] private CeilingWorkspacePodController podController;
        [SerializeField] private Transform ceilingAnchor;
        [SerializeField] private Transform suspensionTop;
        [SerializeField] private Transform dockingConnector;
        [SerializeField] private Transform podRoot;

        [Header("Holographic Portal & Ripple Renderers")]
        [SerializeField] private Transform ceilingPortalCone;
        [SerializeField] private Renderer ceilingPortalConeRenderer;
        [SerializeField] private Transform ceilingProjectionHalo;
        [SerializeField] private Renderer ceilingProjectionHaloRenderer;
        [SerializeField] private Transform ceilingRippleRing;
        [SerializeField] private Renderer ceilingRippleRingRenderer;
        [SerializeField] private Transform airCushionRing;
        [SerializeField] private Renderer airCushionRingRenderer;

        [Header("Quest-Safe Particle Systems")]
        [SerializeField] private ParticleSystem ceilingApertureParticles;
        [SerializeField] private ParticleSystem tetherFlowParticles;
        [SerializeField] private ParticleSystem arrivalBurstParticles;

        [Header("Release switches (set by ReleaseConfigApplier)")]
        public bool particlesEnabled = true;
        public bool portalConeEnabled = true;

        [Header("Reversible Quest 3 Optimization Variant")]
        [SerializeField] private Material portalConeFullMaterial;
        [SerializeField] private Material portalConeLiteMaterial;
        [SerializeField] private CeilingVfxQualityPreset qualityPreset = CeilingVfxQualityPreset.Full;

        private MaterialPropertyBlock _mpb;
        private float _coneAlpha;
        private float _haloAlpha;
        private float _rippleProgress = 1f;
        private float _airCushionAlpha;
        private Color _rippleColor = ColTealCalm;
        private CeilingWorkspacePodController.PodState _lastState = CeilingWorkspacePodController.PodState.DockedIdle;

        public CeilingVfxQualityPreset ActiveQualityPreset => qualityPreset;

        public void SetQualityPreset(CeilingVfxQualityPreset preset)
        {
            qualityPreset = preset;
            ApplyQualityMaterial();
            if (preset != CeilingVfxQualityPreset.Full)
            {
                SetParticlesActive(ceilingApertureParticles, false);
                SetParticlesActive(tetherFlowParticles, false);
                if (arrivalBurstParticles != null)
                    arrivalBurstParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
        }

        private void ApplyQualityMaterial()
        {
            if (ceilingPortalConeRenderer == null) return;
            if (portalConeFullMaterial == null) portalConeFullMaterial = ceilingPortalConeRenderer.sharedMaterial;
            Material targetMat = (qualityPreset == CeilingVfxQualityPreset.LowCostNoParticles && portalConeLiteMaterial != null)
                ? portalConeLiteMaterial
                : portalConeFullMaterial;
            if (targetMat != null && ceilingPortalConeRenderer.sharedMaterial != targetMat)
                ceilingPortalConeRenderer.sharedMaterial = targetMat;
        }

        private void Awake()
        {
            ResolveReferences();
            _mpb = new MaterialPropertyBlock();
            if (podController != null)
            {
                podController.StateChanged += HandlePodStateChanged;
                _lastState = podController.CurrentState;
                ApplySnapshotImmediate(_lastState, 0f);
            }
        }

        private void OnDestroy()
        {
            if (podController != null)
                podController.StateChanged -= HandlePodStateChanged;
        }

        public void ResolveReferences()
        {
            if (podController == null)
                podController = GetComponent<CeilingWorkspacePodController>();

            Transform[] all = GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                Transform t = all[i];
                switch (t.name)
                {
                    case "CeilingAnchor":            ceilingAnchor = t; break;
                    case "SuspensionTop":            suspensionTop = t; break;
                    case "DockingConnector":         dockingConnector = t; break;
                    case "PodRoot":                  podRoot = t; break;
                    case "VFX_CeilingPortalCone":
                        ceilingPortalCone = t;
                        ceilingPortalConeRenderer = t.GetComponent<Renderer>();
                        break;
                    case "VFX_CeilingProjectionHalo":
                        ceilingProjectionHalo = t;
                        ceilingProjectionHaloRenderer = t.GetComponent<Renderer>();
                        break;
                    case "VFX_CeilingRippleRing":
                        ceilingRippleRing = t;
                        ceilingRippleRingRenderer = t.GetComponent<Renderer>();
                        break;
                    case "VFX_AirCushionRing":
                        airCushionRing = t;
                        airCushionRingRenderer = t.GetComponent<Renderer>();
                        break;
                    case "VFX_CeilingApertureParticles":
                        ceilingApertureParticles = t.GetComponent<ParticleSystem>();
                        break;
                    case "VFX_TetherFlowParticles":
                        tetherFlowParticles = t.GetComponent<ParticleSystem>();
                        break;
                    case "VFX_ArrivalBurstParticles":
                        arrivalBurstParticles = t.GetComponent<ParticleSystem>();
                        break;
                }
            }
        }

        /// <summary>Public entry for a driver other than CeilingWorkspacePodController (the project's
        /// CeilingVfxBridge, which follows CeilingPodMachine). Works with podController == null.</summary>
        public void OnPodState(CeilingWorkspacePodController.PodState state)
        {
            if (_mpb == null) _mpb = new MaterialPropertyBlock();
            HandlePodStateChanged(state);
        }

        /// <summary>The state the effects are currently showing.</summary>
        public CeilingWorkspacePodController.PodState CurrentVfxState => _lastState;

        private void HandlePodStateChanged(CeilingWorkspacePodController.PodState nextState)
        {
            CeilingWorkspacePodController.PodState prev = _lastState;
            _lastState = nextState;

            switch (nextState)
            {
                case CeilingWorkspacePodController.PodState.Acknowledging:
                case CeilingWorkspacePodController.PodState.Unlocking:
                    TriggerCeilingRipple(ColCyanGlow);
                    SetParticlesActive(ceilingApertureParticles, true);
                    break;

                case CeilingWorkspacePodController.PodState.BeginningDescent:
                case CeilingWorkspacePodController.PodState.Descending:
                    SetParticlesActive(ceilingApertureParticles, true);
                    ConfigureTetherFlowDirection(downward: true);
                    SetParticlesActive(tetherFlowParticles, true);
                    break;

                case CeilingWorkspacePodController.PodState.Stabilizing:
                case CeilingWorkspacePodController.PodState.OpeningShelf:
                    SetParticlesActive(tetherFlowParticles, false);
                    EmitBurstAt(arrivalBurstParticles, podRoot != null ? podRoot.position : transform.position, 16);
                    break;

                case CeilingWorkspacePodController.PodState.DeployedIdle:
                    SetParticlesActive(ceilingApertureParticles, false);
                    SetParticlesActive(tetherFlowParticles, false);
                    break;

                case CeilingWorkspacePodController.PodState.BeginningAscent:
                case CeilingWorkspacePodController.PodState.ReturningToDock:
                    SetParticlesActive(ceilingApertureParticles, true);
                    ConfigureTetherFlowDirection(downward: false);
                    SetParticlesActive(tetherFlowParticles, true);
                    break;

                case CeilingWorkspacePodController.PodState.DockLocking:
                case CeilingWorkspacePodController.PodState.Settling:
                    SetParticlesActive(tetherFlowParticles, false);
                    TriggerCeilingRipple(ColLockMint);
                    EmitBurstAt(arrivalBurstParticles, ceilingAnchor != null ? ceilingAnchor.position + Vector3.down * 0.04f : transform.position, 18);
                    break;

                case CeilingWorkspacePodController.PodState.DockedIdle:
                    if (prev == CeilingWorkspacePodController.PodState.Settling || prev == CeilingWorkspacePodController.PodState.DockLocking)
                        TriggerCeilingRipple(ColTealCalm);
                    SetParticlesActive(ceilingApertureParticles, false);
                    SetParticlesActive(tetherFlowParticles, false);
                    break;
            }
        }

        private void LateUpdate()
        {
            if (_mpb == null) _mpb = new MaterialPropertyBlock();
            var st = podController != null ? podController.CurrentState : _lastState;

            bool activeFlight = st == CeilingWorkspacePodController.PodState.Descending ||
                                st == CeilingWorkspacePodController.PodState.ReturningToDock;
            bool prepping = st == CeilingWorkspacePodController.PodState.Unlocking ||
                            st == CeilingWorkspacePodController.PodState.BeginningDescent ||
                            st == CeilingWorkspacePodController.PodState.BeginningAscent ||
                            st == CeilingWorkspacePodController.PodState.DockLocking;
            bool stabilizing = st == CeilingWorkspacePodController.PodState.Stabilizing ||
                               st == CeilingWorkspacePodController.PodState.OpeningShelf;
            bool ascending = st == CeilingWorkspacePodController.PodState.BeginningAscent ||
                             st == CeilingWorkspacePodController.PodState.ReturningToDock ||
                             st == CeilingWorkspacePodController.PodState.DockLocking;

            float targetCone = activeFlight ? 0.42f : (prepping ? 0.22f : 0f);
            float targetHalo = (activeFlight || prepping) ? 0.55f : (st == CeilingWorkspacePodController.PodState.DeployedIdle ? 0.22f : 0.10f);
            float targetCushion = stabilizing ? 0.48f : (st == CeilingWorkspacePodController.PodState.Descending ? 0.20f : 0f);

            float dt = Time.deltaTime;
            _coneAlpha = Mathf.MoveTowards(_coneAlpha, targetCone, dt * 1.8f);
            _haloAlpha = Mathf.MoveTowards(_haloAlpha, targetHalo, dt * 1.4f);
            _airCushionAlpha = Mathf.MoveTowards(_airCushionAlpha, targetCushion, dt * 2.0f);

            if (_rippleProgress < 1f)
                _rippleProgress = Mathf.Min(1f, _rippleProgress + dt * 1.25f);

            Color beamCol = ascending ? ColWarmGold : ColCyanGlow;
            bool allowCone = qualityPreset != CeilingVfxQualityPreset.MinimalIrisHalo;
            bool allowSecondaryRings = qualityPreset == CeilingVfxQualityPreset.Full;
            UpdatePortalConeTransformAndColor(beamCol, allowCone ? _coneAlpha : 0f, ascending ? 1f : -1f);
            UpdateRingRenderer(ceilingProjectionHaloRenderer, ascending ? ColWarmGold : ColTealCalm, _haloAlpha, 1.0f);
            if (!allowSecondaryRings)
            {
                if (ceilingRippleRing != null && ceilingRippleRing.gameObject.activeSelf) ceilingRippleRing.gameObject.SetActive(false);
                if (airCushionRing != null && airCushionRing.gameObject.activeSelf) airCushionRing.gameObject.SetActive(false);
            }
            else
            {
                UpdateCeilingRippleTransformAndColor();
                UpdateAirCushionTransformAndColor();
            }
        }

        /// <summary>
        /// Deterministic Editor / Validation scene hook to freeze a transition state cleanly without Play mode.
        /// </summary>
        public void ApplySnapshotImmediate(CeilingWorkspacePodController.PodState state, float ripplePhase01 = 0.35f)
        {
            ResolveReferences();
            ApplyQualityMaterial();
            if (_mpb == null) _mpb = new MaterialPropertyBlock();

            bool activeFlight = state == CeilingWorkspacePodController.PodState.Descending ||
                                state == CeilingWorkspacePodController.PodState.ReturningToDock ||
                                state == CeilingWorkspacePodController.PodState.Unlocking;
            bool ascending = state == CeilingWorkspacePodController.PodState.ReturningToDock ||
                             state == CeilingWorkspacePodController.PodState.BeginningAscent ||
                             state == CeilingWorkspacePodController.PodState.DockLocking;

            bool allowCone = portalConeEnabled && qualityPreset != CeilingVfxQualityPreset.MinimalIrisHalo;
            bool allowSecondary = qualityPreset == CeilingVfxQualityPreset.Full;
            bool allowParticles = activeFlight && particlesEnabled && qualityPreset == CeilingVfxQualityPreset.Full;

            _coneAlpha = (activeFlight && allowCone) ? 0.44f : 0f;
            _haloAlpha = activeFlight ? 0.62f : (state == CeilingWorkspacePodController.PodState.DeployedIdle ? 0.24f : 0.12f);
            _airCushionAlpha = allowSecondary && (state == CeilingWorkspacePodController.PodState.Stabilizing ||
                                state == CeilingWorkspacePodController.PodState.Descending) ? 0.38f : 0f;
            _rippleProgress = (activeFlight && allowSecondary) ? Mathf.Clamp01(ripplePhase01) : 1f;
            _rippleColor = ascending ? ColWarmGold : ColCyanGlow;

            Color beamCol = ascending ? ColWarmGold : ColCyanGlow;
            UpdatePortalConeTransformAndColor(beamCol, _coneAlpha, ascending ? 1f : -1f);
            UpdateRingRenderer(ceilingProjectionHaloRenderer, ascending ? ColWarmGold : ColTealCalm, _haloAlpha, 1.15f);
            UpdateCeilingRippleTransformAndColor();
            UpdateAirCushionTransformAndColor();

            if (tetherFlowParticles != null)
            {
                if (allowParticles)
                {
                    ConfigureTetherFlowDirection(!ascending);
                    tetherFlowParticles.Simulate(0.65f, withChildren: true, restart: true);
                }
                else
                {
                    tetherFlowParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                }
            }
            if (ceilingApertureParticles != null)
            {
                if (allowParticles)
                    ceilingApertureParticles.Simulate(0.50f, withChildren: true, restart: true);
                else
                    ceilingApertureParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
        }

        private void UpdatePortalConeTransformAndColor(Color col, float alpha, float scanDir)
        {
            if (ceilingPortalCone == null || ceilingPortalConeRenderer == null) return;
            if (!portalConeEnabled) alpha = 0f;
            if (alpha <= 0.005f)
            {
                if (ceilingPortalCone.gameObject.activeSelf) ceilingPortalCone.gameObject.SetActive(false);
                return;
            }

            Vector3 topPos = suspensionTop != null ? suspensionTop.position :
                (ceilingAnchor != null ? ceilingAnchor.position + Vector3.down * 0.03f : transform.position);
            Vector3 botPos = dockingConnector != null ? dockingConnector.position :
                (podRoot != null ? podRoot.position + Vector3.up * 0.18f : topPos + Vector3.down * 0.4f);

            float len = Mathf.Max(0.08f, topPos.y - botPos.y);
            if (!ceilingPortalCone.gameObject.activeSelf) ceilingPortalCone.gameObject.SetActive(true);

            ceilingPortalCone.position = new Vector3(topPos.x, topPos.y, topPos.z);
            Vector3 s = ceilingPortalCone.localScale;
            s.y = len;
            ceilingPortalCone.localScale = s;

            if (tetherFlowParticles != null)
            {
                var sh = tetherFlowParticles.shape;
                sh.length = Mathf.Max(0.08f, len * 0.48f);
                var pMain = tetherFlowParticles.main;
                pMain.startLifetime = Mathf.Clamp((len * 0.38f) / 0.35f, 0.28f, 0.95f);
                tetherFlowParticles.transform.position = Vector3.Lerp(topPos, botPos, scanDir > 0f ? 0.60f : 0.35f);
            }

            _mpb.Clear();
            Color c = col;
            c.a = alpha;
            _mpb.SetColor(BaseColorId, c);
            _mpb.SetFloat(IntensityId, 1.35f);
            _mpb.SetFloat(ScanSpeedId, 1.45f);
            _mpb.SetFloat(ScanDirectionId, scanDir);
            _mpb.SetFloat(RingModeId, 0f);
            ceilingPortalConeRenderer.SetPropertyBlock(_mpb);
        }

        private void UpdateRingRenderer(Renderer rend, Color col, float alpha, float intensity)
        {
            if (rend == null) return;
            if (alpha <= 0.005f)
            {
                if (rend.gameObject.activeSelf) rend.gameObject.SetActive(false);
                return;
            }
            if (!rend.gameObject.activeSelf) rend.gameObject.SetActive(true);

            _mpb.Clear();
            Color c = col;
            c.a = alpha;
            _mpb.SetColor(BaseColorId, c);
            _mpb.SetFloat(IntensityId, intensity);
            _mpb.SetFloat(ScanSpeedId, 1.2f);
            _mpb.SetFloat(RingModeId, 1f);
            rend.SetPropertyBlock(_mpb);
        }

        private void UpdateCeilingRippleTransformAndColor()
        {
            if (ceilingRippleRing == null || ceilingRippleRingRenderer == null) return;
            if (_rippleProgress >= 0.995f)
            {
                if (ceilingRippleRing.gameObject.activeSelf) ceilingRippleRing.gameObject.SetActive(false);
                return;
            }

            float scale = 0.26f * Mathf.Lerp(0.95f, 1.65f, _rippleProgress);
            float alpha = (1f - _rippleProgress) * 0.65f;
            ceilingRippleRing.localScale = new Vector3(scale, 1f, scale);
            UpdateRingRenderer(ceilingRippleRingRenderer, _rippleColor, alpha, 1.5f);
        }

        private void UpdateAirCushionTransformAndColor()
        {
            if (airCushionRing == null || airCushionRingRenderer == null) return;
            if (_airCushionAlpha <= 0.005f)
            {
                if (airCushionRing.gameObject.activeSelf) airCushionRing.gameObject.SetActive(false);
                return;
            }
            if (podRoot != null)
                airCushionRing.position = podRoot.position + Vector3.down * 0.015f;

            UpdateRingRenderer(airCushionRingRenderer, ColTealCalm, _airCushionAlpha, 1.15f);
        }

        private void TriggerCeilingRipple(Color color)
        {
            _rippleColor = color;
            _rippleProgress = 0f;
        }

        private void ConfigureTetherFlowDirection(bool downward)
        {
            if (tetherFlowParticles == null) return;
            var main = tetherFlowParticles.main;
            main.startSpeed = downward ? 0.35f : -0.35f;
            var colMod = tetherFlowParticles.colorOverLifetime;
            if (colMod.enabled)
            {
                var grad = new Gradient();
                Color c = downward ? ColCyanGlow : ColWarmGold;
                grad.SetKeys(
                    new[] { new GradientColorKey(c, 0f), new GradientColorKey(Color.white, 0.5f), new GradientColorKey(c, 1f) },
                    new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.85f, 0.25f), new GradientAlphaKey(0.75f, 0.75f), new GradientAlphaKey(0f, 1f) });
                colMod.color = grad;
            }
        }

        private void SetParticlesActive(ParticleSystem ps, bool active)
        {
            if (ps == null) return;
            if (!particlesEnabled) active = false;
            if (active)
            {
                if (!ps.isPlaying) ps.Play();
            }
            else if (ps.isPlaying)
            {
                ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            }
        }

        private void EmitBurstAt(ParticleSystem ps, Vector3 worldPos, int count)
        {
            if (ps == null || !particlesEnabled) return;
            ps.transform.position = worldPos;
            ps.Emit(count);
        }
    }
}

