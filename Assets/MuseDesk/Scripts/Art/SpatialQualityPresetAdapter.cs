using UnityEngine;

namespace MuseDesk.Art
{
    /// <summary>
    /// Unified Quest-safe visual effect quality tier across spatial and wall-habitat art controllers.
    /// </summary>
    public enum ArtEffectQualityTier
    {
        High = 0,
        Medium = 1,
        Low = 2
    }

    /// <summary>
    /// Unified helper bridging <see cref="CeilingDeploymentVFXController"/>,
    /// <see cref="WallHabitatLargePortalVisualController"/>, <see cref="DocumentCourierController"/>,
    /// <see cref="MuseVfxPoolPlayer"/>, and standalone <see cref="ParticleSystem"/> toggles
    /// under a single <see cref="ArtEffectQualityTier"/> setting.
    /// </summary>
    [DisallowMultipleComponent]
    public class SpatialQualityPresetAdapter : MonoBehaviour
    {
        [Header("Active Quality Tier")]
        [SerializeField] private ArtEffectQualityTier qualityTier = ArtEffectQualityTier.High;
        [SerializeField] private bool autoResolveOnAwake = true;
        [SerializeField] private bool applyOnStart = true;

        [Header("Bridged Art Controllers")]
        [SerializeField] private CeilingDeploymentVFXController[] ceilingVfxControllers;
        [SerializeField] private WallHabitatLargePortalVisualController[] largePortalControllers;
        [SerializeField] private DocumentCourierController[] courierControllers;
        [SerializeField] private MuseVfxPoolPlayer[] vfxPoolPlayers;
        [SerializeField] private ParticleSystem[] managedParticleSystems;

        public ArtEffectQualityTier CurrentTier => qualityTier;

        private void Awake()
        {
            if (autoResolveOnAwake)
            {
                ResolveReferences(includeScene: false);
            }
        }

        private void Start()
        {
            if (applyOnStart)
            {
                ApplyQualityTier(qualityTier);
            }
        }

        /// <summary>
        /// Resolves unassigned controller arrays from hierarchy (or the whole scene if requested).
        /// </summary>
        public void ResolveReferences(bool includeScene = false)
        {
            if (ceilingVfxControllers == null || ceilingVfxControllers.Length == 0)
            {
                ceilingVfxControllers = includeScene
                    ? FindObjectsByType<CeilingDeploymentVFXController>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                    : GetComponentsInChildren<CeilingDeploymentVFXController>(true);
            }

            if (largePortalControllers == null || largePortalControllers.Length == 0)
            {
                largePortalControllers = includeScene
                    ? FindObjectsByType<WallHabitatLargePortalVisualController>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                    : GetComponentsInChildren<WallHabitatLargePortalVisualController>(true);
            }

            if (courierControllers == null || courierControllers.Length == 0)
            {
                courierControllers = includeScene
                    ? FindObjectsByType<DocumentCourierController>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                    : GetComponentsInChildren<DocumentCourierController>(true);
            }

            if (vfxPoolPlayers == null || vfxPoolPlayers.Length == 0)
            {
                vfxPoolPlayers = includeScene
                    ? FindObjectsByType<MuseVfxPoolPlayer>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                    : GetComponentsInChildren<MuseVfxPoolPlayer>(true);
            }

            if (managedParticleSystems == null || managedParticleSystems.Length == 0)
            {
                managedParticleSystems = includeScene
                    ? FindObjectsByType<ParticleSystem>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                    : GetComponentsInChildren<ParticleSystem>(true);
            }
        }

        /// <summary>
        /// Applies the requested quality tier across all bridged controllers and ParticleSystems.
        /// </summary>
        public void ApplyQualityTier(ArtEffectQualityTier tier)
        {
            qualityTier = tier;

            bool allowParticles = tier == ArtEffectQualityTier.High;
            bool allowPortalCone = tier != ArtEffectQualityTier.Low;
            bool reducedPortalMotion = tier != ArtEffectQualityTier.High;
            CeilingVfxQualityPreset ceilingPreset = tier switch
            {
                ArtEffectQualityTier.High => CeilingVfxQualityPreset.Full,
                ArtEffectQualityTier.Medium => CeilingVfxQualityPreset.LowCostNoParticles,
                _ => CeilingVfxQualityPreset.MinimalIrisHalo,
            };

            if (ceilingVfxControllers != null)
            {
                for (int i = 0; i < ceilingVfxControllers.Length; i++)
                {
                    CeilingDeploymentVFXController c = ceilingVfxControllers[i];
                    if (c == null) continue;
                    c.particlesEnabled = allowParticles;
                    c.portalConeEnabled = allowPortalCone;
                    c.SetQualityPreset(ceilingPreset);
                }
            }

            if (largePortalControllers != null)
            {
                for (int i = 0; i < largePortalControllers.Length; i++)
                {
                    WallHabitatLargePortalVisualController p = largePortalControllers[i];
                    if (p == null) continue;
                    p.SetComfortReducedMotion(reducedPortalMotion);
                }
            }

            if (courierControllers != null)
            {
                for (int i = 0; i < courierControllers.Length; i++)
                {
                    DocumentCourierController dc = courierControllers[i];
                    if (dc == null) continue;
                    ToggleHierarchyParticles(dc.transform, allowParticles);
                }
            }

            if (vfxPoolPlayers != null)
            {
                for (int i = 0; i < vfxPoolPlayers.Length; i++)
                {
                    MuseVfxPoolPlayer pool = vfxPoolPlayers[i];
                    if (pool == null) continue;
                    pool.SetReducedMotion(!allowParticles);
                }
            }

            SetParticlesEnabled(allowParticles);
        }

        /// <summary>Alias for <see cref="ApplyQualityTier"/>.</summary>
        public void SetQualityTier(ArtEffectQualityTier tier)
        {
            ApplyQualityTier(tier);
        }

        /// <summary>
        /// Enables or disables emission on all managed standalone ParticleSystems cleanly.
        /// </summary>
        public void SetParticlesEnabled(bool enabled)
        {
            if (managedParticleSystems == null) return;
            for (int i = 0; i < managedParticleSystems.Length; i++)
            {
                ApplyParticleToggle(managedParticleSystems[i], enabled);
            }
        }

        private static void ToggleHierarchyParticles(Transform root, bool enabled)
        {
            if (root == null) return;
            ParticleSystem[] systems = root.GetComponentsInChildren<ParticleSystem>(true);
            for (int i = 0; i < systems.Length; i++)
            {
                ApplyParticleToggle(systems[i], enabled);
            }
        }

        private static void ApplyParticleToggle(ParticleSystem ps, bool enabled)
        {
            if (ps == null) return;
            var emission = ps.emission;
            emission.enabled = enabled;
            if (!enabled)
            {
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
        }
    }
}
