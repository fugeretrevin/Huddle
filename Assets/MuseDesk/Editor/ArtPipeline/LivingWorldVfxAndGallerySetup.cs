#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using MuseDesk.Art;
using TMPro;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace MuseDesk.Editor.ArtPipeline
{
    /// <summary>
    /// Quest-3-safe VFX prefab library builder, Wall Habitat VFX wiring,
    /// 4 isolated Art Validation Gallery scenes, screenshot capture, and
    /// cross-category asset audit writer.
    /// </summary>
    public static class LivingWorldVfxAndGallerySetup
    {
        private const string MatsDir = "Assets/MuseDesk/Art/Materials";
        private const string TexturesDir = "Assets/MuseDesk/Art/Textures";
        private const string VfxPrefabsDir = "Assets/MuseDesk/Art/Prefabs/VFX";
        private const string WallPrefabsDir = "Assets/MuseDesk/Art/Prefabs/WallHabitat";
        private const string CharactersPrefabsDir = "Assets/MuseDesk/Art/Prefabs/Characters";
        private const string DocumentsPrefabsDir = "Assets/MuseDesk/Art/Prefabs/Documents";
        private const string SpatialPrefabsDir = "Assets/MuseDesk/Art/Prefabs/Spatial";
        private const string GalleryScenesDir = "Assets/MuseDesk/Scenes/ArtGalleries";
        private const string ScreenshotDir = "Screenshots/UltimatePolish";
        private const string AuditJsonPath = "docs/Art/ULTIMATE_ASSET_AUDIT.json";
        private const string InterFontPath = "Assets/MuseDesk/Art/Fonts/Inter-Medium SDF.asset";

        private const string SoftParticleMatPath = MatsDir + "/Mat_Spatial_SoftParticle.mat";
        private const string FallbackVfxMatPath = MatsDir + "/Mat_VFX_SoftGlow.mat";
        private const string SoftGlowTexPath = TexturesDir + "/Tex_SoftGlowDot.png";

        private const int ShotWidth = 1440;
        private const int ShotHeight = 960;

        private struct VfxSpec
        {
            public string Name;
            public bool Loop;
            public int MaxParticles;
            public int BurstCount;
            public float RateOverTime;
            public float Duration;
            public float StartLifetime;
            public float StartSpeedMin;
            public float StartSpeedMax;
            public float StartSizeMin;
            public float StartSizeMax;
            public float GravityModifier;
            public ParticleSystemShapeType ShapeType;
            public Vector3 ShapeRotation;
            public Vector3 ShapeScale;
            public float ShapeRadius;
            public float ShapeRadiusThickness;
            public float ShapeAngle;
            public float DonutRadius;
            public Color StartColorA;
            public Color StartColorB;
            public Color MidColor;
            public Color EndColor;
        }

        [Serializable]
        private class PrefabAuditEntry
        {
            public string category;
            public string prefabName;
            public string prefabPath;
            public int meshCount;
            public int triangleCount;
            public int materialCount;
            public string[] materialNames;
            public int rendererCount;
            public int animatorCount;
            public int particleSystemCount;
            public int maxParticles;
            public int missingScripts;
            public int missingMaterials;
        }

        [Serializable]
        private class CategoryAuditSummary
        {
            public string category;
            public int prefabCount;
            public int meshCount;
            public int triangleCount;
            public int materialCount;
            public int rendererCount;
            public int animatorCount;
            public int particleSystemCount;
            public int maxParticles;
        }

        [Serializable]
        private class UltimateAssetAudit
        {
            public string generatedAtUtc;
            public string unityVersion;
            public int totalPrefabsAudited;
            public int totalMissingScripts;
            public int totalMissingMaterials;
            public int totalMeshes;
            public int totalTriangles;
            public int totalRenderers;
            public int totalAnimators;
            public int totalParticleSystems;
            public int maxParticlesAcrossAllPrefabs;
            public List<string> questVfxBudgetViolations = new List<string>();
            public List<string> screenshots = new List<string>();
            public List<CategoryAuditSummary> categorySummaries = new List<CategoryAuditSummary>();
            public List<PrefabAuditEntry> prefabs = new List<PrefabAuditEntry>();
        }

        private static readonly VfxSpec[] VfxLibrarySpecs =
        {
            new VfxSpec
            {
                Name = "VFX_Interaction_Sparkle",
                Loop = false, MaxParticles = 12, BurstCount = 10, RateOverTime = 0f,
                Duration = 0.55f, StartLifetime = 0.42f, StartSpeedMin = 0.12f, StartSpeedMax = 0.22f,
                StartSizeMin = 0.011f, StartSizeMax = 0.020f, GravityModifier = 0f,
                ShapeType = ParticleSystemShapeType.Sphere, ShapeRotation = Vector3.zero, ShapeScale = Vector3.one,
                ShapeRadius = 0.032f, ShapeRadiusThickness = 1f, ShapeAngle = 0f, DonutRadius = 0f,
                StartColorA = new Color(1.00f, 0.94f, 0.72f, 0.95f),
                StartColorB = new Color(0.45f, 0.92f, 0.88f, 0.95f),
                MidColor = Color.white,
                EndColor = new Color(0.45f, 0.92f, 0.88f, 1f)
            },
            new VfxSpec
            {
                Name = "VFX_Approval_Seal",
                Loop = false, MaxParticles = 16, BurstCount = 14, RateOverTime = 0f,
                Duration = 0.75f, StartLifetime = 0.60f, StartSpeedMin = 0.14f, StartSpeedMax = 0.24f,
                StartSizeMin = 0.013f, StartSizeMax = 0.024f, GravityModifier = -0.04f,
                ShapeType = ParticleSystemShapeType.Circle, ShapeRotation = new Vector3(-90f, 0f, 0f), ShapeScale = Vector3.one,
                ShapeRadius = 0.045f, ShapeRadiusThickness = 0.35f, ShapeAngle = 0f, DonutRadius = 0f,
                StartColorA = new Color(0.18f, 0.88f, 0.52f, 0.95f),
                StartColorB = new Color(1.00f, 0.85f, 0.40f, 0.95f),
                MidColor = new Color(0.65f, 1.00f, 0.80f, 1f),
                EndColor = new Color(0.18f, 0.88f, 0.52f, 1f)
            },
            new VfxSpec
            {
                Name = "VFX_Rejection_Pulse",
                Loop = false, MaxParticles = 14, BurstCount = 12, RateOverTime = 0f,
                Duration = 0.65f, StartLifetime = 0.50f, StartSpeedMin = 0.16f, StartSpeedMax = 0.26f,
                StartSizeMin = 0.013f, StartSizeMax = 0.024f, GravityModifier = 0f,
                ShapeType = ParticleSystemShapeType.Circle, ShapeRotation = new Vector3(-90f, 0f, 0f), ShapeScale = Vector3.one,
                ShapeRadius = 0.040f, ShapeRadiusThickness = 0.25f, ShapeAngle = 0f, DonutRadius = 0f,
                StartColorA = new Color(0.98f, 0.34f, 0.30f, 0.95f),
                StartColorB = new Color(1.00f, 0.64f, 0.32f, 0.95f),
                MidColor = new Color(1.00f, 0.52f, 0.42f, 1f),
                EndColor = new Color(0.92f, 0.25f, 0.24f, 1f)
            },
            new VfxSpec
            {
                Name = "VFX_Workstation_TaskStart",
                Loop = false, MaxParticles = 12, BurstCount = 10, RateOverTime = 0f,
                Duration = 0.65f, StartLifetime = 0.54f, StartSpeedMin = 0.12f, StartSpeedMax = 0.20f,
                StartSizeMin = 0.011f, StartSizeMax = 0.019f, GravityModifier = -0.03f,
                ShapeType = ParticleSystemShapeType.Cone, ShapeRotation = new Vector3(-90f, 0f, 0f), ShapeScale = Vector3.one,
                ShapeRadius = 0.035f, ShapeRadiusThickness = 0.4f, ShapeAngle = 18f, DonutRadius = 0f,
                StartColorA = new Color(0.24f, 0.88f, 0.92f, 0.92f),
                StartColorB = new Color(0.68f, 0.96f, 1.00f, 0.92f),
                MidColor = Color.white,
                EndColor = new Color(0.24f, 0.88f, 0.92f, 1f)
            },
            new VfxSpec
            {
                Name = "VFX_Blueprint_Print",
                Loop = false, MaxParticles = 14, BurstCount = 12, RateOverTime = 0f,
                Duration = 0.70f, StartLifetime = 0.56f, StartSpeedMin = 0.09f, StartSpeedMax = 0.17f,
                StartSizeMin = 0.010f, StartSizeMax = 0.018f, GravityModifier = -0.02f,
                ShapeType = ParticleSystemShapeType.Box, ShapeRotation = new Vector3(-90f, 0f, 0f), ShapeScale = new Vector3(0.065f, 0.035f, 0.015f),
                ShapeRadius = 0.03f, ShapeRadiusThickness = 1f, ShapeAngle = 0f, DonutRadius = 0f,
                StartColorA = new Color(0.30f, 0.66f, 1.00f, 0.95f),
                StartColorB = new Color(0.46f, 0.94f, 0.98f, 0.95f),
                MidColor = new Color(0.75f, 0.95f, 1.00f, 1f),
                EndColor = new Color(0.25f, 0.60f, 0.96f, 1f)
            },
            new VfxSpec
            {
                Name = "VFX_Build_Complete",
                Loop = false, MaxParticles = 18, BurstCount = 16, RateOverTime = 0f,
                Duration = 0.80f, StartLifetime = 0.66f, StartSpeedMin = 0.16f, StartSpeedMax = 0.28f,
                StartSizeMin = 0.012f, StartSizeMax = 0.023f, GravityModifier = 0.05f,
                ShapeType = ParticleSystemShapeType.Hemisphere, ShapeRotation = new Vector3(-90f, 0f, 0f), ShapeScale = Vector3.one,
                ShapeRadius = 0.045f, ShapeRadiusThickness = 0.5f, ShapeAngle = 0f, DonutRadius = 0f,
                StartColorA = new Color(1.00f, 0.80f, 0.34f, 0.95f),
                StartColorB = new Color(0.34f, 0.90f, 0.62f, 0.95f),
                MidColor = Color.white,
                EndColor = new Color(1.00f, 0.78f, 0.34f, 1f)
            },
            new VfxSpec
            {
                Name = "VFX_Test_Pass",
                Loop = false, MaxParticles = 16, BurstCount = 14, RateOverTime = 0f,
                Duration = 0.75f, StartLifetime = 0.60f, StartSpeedMin = 0.14f, StartSpeedMax = 0.23f,
                StartSizeMin = 0.012f, StartSizeMax = 0.021f, GravityModifier = -0.04f,
                ShapeType = ParticleSystemShapeType.Cone, ShapeRotation = new Vector3(-90f, 0f, 0f), ShapeScale = Vector3.one,
                ShapeRadius = 0.038f, ShapeRadiusThickness = 0.45f, ShapeAngle = 16f, DonutRadius = 0f,
                StartColorA = new Color(0.22f, 0.88f, 0.54f, 0.95f),
                StartColorB = new Color(0.68f, 1.00f, 0.82f, 0.95f),
                MidColor = Color.white,
                EndColor = new Color(0.22f, 0.88f, 0.54f, 1f)
            },
            new VfxSpec
            {
                Name = "VFX_Test_Fail",
                Loop = false, MaxParticles = 16, BurstCount = 14, RateOverTime = 0f,
                Duration = 0.65f, StartLifetime = 0.52f, StartSpeedMin = 0.18f, StartSpeedMax = 0.30f,
                StartSizeMin = 0.012f, StartSizeMax = 0.022f, GravityModifier = 0.06f,
                ShapeType = ParticleSystemShapeType.Sphere, ShapeRotation = Vector3.zero, ShapeScale = Vector3.one,
                ShapeRadius = 0.028f, ShapeRadiusThickness = 1f, ShapeAngle = 0f, DonutRadius = 0f,
                StartColorA = new Color(0.98f, 0.30f, 0.28f, 0.95f),
                StartColorB = new Color(1.00f, 0.60f, 0.32f, 0.95f),
                MidColor = new Color(1.00f, 0.55f, 0.45f, 1f),
                EndColor = new Color(0.92f, 0.24f, 0.24f, 1f)
            },
            new VfxSpec
            {
                Name = "VFX_Elevator_Arrival",
                Loop = false, MaxParticles = 14, BurstCount = 12, RateOverTime = 0f,
                Duration = 0.70f, StartLifetime = 0.56f, StartSpeedMin = 0.10f, StartSpeedMax = 0.18f,
                StartSizeMin = 0.012f, StartSizeMax = 0.022f, GravityModifier = 0f,
                ShapeType = ParticleSystemShapeType.Donut, ShapeRotation = new Vector3(-90f, 0f, 0f), ShapeScale = Vector3.one,
                ShapeRadius = 0.065f, ShapeRadiusThickness = 1f, ShapeAngle = 0f, DonutRadius = 0.014f,
                StartColorA = new Color(0.36f, 0.88f, 0.84f, 0.92f),
                StartColorB = new Color(0.98f, 0.86f, 0.54f, 0.92f),
                MidColor = Color.white,
                EndColor = new Color(0.36f, 0.88f, 0.84f, 1f)
            },
            new VfxSpec
            {
                Name = "VFX_Portal_Large_Full",
                Loop = true, MaxParticles = 14, BurstCount = 0, RateOverTime = 5f,
                Duration = 2.00f, StartLifetime = 1.60f, StartSpeedMin = 0.10f, StartSpeedMax = 0.14f,
                StartSizeMin = 0.014f, StartSizeMax = 0.024f, GravityModifier = 0f,
                ShapeType = ParticleSystemShapeType.Circle, ShapeRotation = new Vector3(-90f, 0f, 0f), ShapeScale = Vector3.one,
                ShapeRadius = 0.22f, ShapeRadiusThickness = 0.15f, ShapeAngle = 0f, DonutRadius = 0f,
                StartColorA = new Color(0.32f, 0.90f, 0.85f, 0.58f),
                StartColorB = new Color(0.56f, 0.96f, 0.92f, 0.55f),
                MidColor = new Color(0.75f, 1.00f, 0.98f, 1f),
                EndColor = new Color(0.28f, 0.85f, 0.80f, 1f)
            },
            new VfxSpec
            {
                Name = "VFX_Portal_Large_LowFx",
                Loop = false, MaxParticles = 8, BurstCount = 8, RateOverTime = 0f,
                Duration = 0.90f, StartLifetime = 0.78f, StartSpeedMin = 0.08f, StartSpeedMax = 0.12f,
                StartSizeMin = 0.014f, StartSizeMax = 0.021f, GravityModifier = 0f,
                ShapeType = ParticleSystemShapeType.Circle, ShapeRotation = new Vector3(-90f, 0f, 0f), ShapeScale = Vector3.one,
                ShapeRadius = 0.22f, ShapeRadiusThickness = 0.15f, ShapeAngle = 0f, DonutRadius = 0f,
                StartColorA = new Color(0.32f, 0.90f, 0.85f, 0.48f),
                StartColorB = new Color(0.52f, 0.94f, 0.90f, 0.48f),
                MidColor = new Color(0.70f, 0.98f, 0.96f, 1f),
                EndColor = new Color(0.28f, 0.85f, 0.80f, 1f)
            },
            new VfxSpec
            {
                Name = "VFX_Courier_Trail_High",
                Loop = false, MaxParticles = 16, BurstCount = 14, RateOverTime = 0f,
                Duration = 0.75f, StartLifetime = 0.58f, StartSpeedMin = 0.08f, StartSpeedMax = 0.16f,
                StartSizeMin = 0.010f, StartSizeMax = 0.019f, GravityModifier = 0f,
                ShapeType = ParticleSystemShapeType.Cone, ShapeRotation = new Vector3(0f, 180f, 0f), ShapeScale = Vector3.one,
                ShapeRadius = 0.026f, ShapeRadiusThickness = 0.5f, ShapeAngle = 14f, DonutRadius = 0f,
                StartColorA = new Color(0.26f, 0.90f, 0.88f, 0.90f),
                StartColorB = new Color(0.95f, 0.84f, 0.52f, 0.88f),
                MidColor = Color.white,
                EndColor = new Color(0.26f, 0.90f, 0.88f, 1f)
            },
            new VfxSpec
            {
                Name = "VFX_Project_Complete_Celebration",
                Loop = false, MaxParticles = 20, BurstCount = 20, RateOverTime = 0f,
                Duration = 1.05f, StartLifetime = 0.90f, StartSpeedMin = 0.26f, StartSpeedMax = 0.42f,
                StartSizeMin = 0.014f, StartSizeMax = 0.026f, GravityModifier = 0.12f,
                ShapeType = ParticleSystemShapeType.Cone, ShapeRotation = new Vector3(-90f, 0f, 0f), ShapeScale = Vector3.one,
                ShapeRadius = 0.055f, ShapeRadiusThickness = 0.6f, ShapeAngle = 36f, DonutRadius = 0f,
                StartColorA = new Color(1.00f, 0.83f, 0.35f, 0.98f),
                StartColorB = new Color(0.28f, 0.90f, 0.78f, 0.98f),
                MidColor = new Color(0.55f, 0.86f, 1.00f, 1f),
                EndColor = new Color(1.00f, 0.83f, 0.35f, 1f)
            }
        };

        // =====================================================================
        // PUBLIC ENTRY POINTS
        // =====================================================================

        [MenuItem("MuseDesk/Art Pipeline/Living World/Build All (VFX + Galleries + Audit)")]
        public static void BuildAll()
        {
            BuildVfxLibrary();
            BuildGalleryScenesAndAudit();
        }

        [MenuItem("MuseDesk/Art Pipeline/Living World/Build VFX Prefab Library")]
        public static void BuildVfxLibrary()
        {
            EnsureFolders();

            Material particleMat = ResolveOrCreateSoftParticleMaterial();
            var vfxPrefabs = new Dictionary<string, GameObject>(StringComparer.OrdinalIgnoreCase);

            foreach (VfxSpec spec in VfxLibrarySpecs)
            {
                GameObject prefab = BuildOrUpdateVfxPrefab(spec, particleMat);
                if (prefab != null) vfxPrefabs[spec.Name] = prefab;
            }

            WireWallHabitatPrefabs(vfxPrefabs);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            Debug.Log($"[LivingWorldVfxAndGallerySetup] Built {vfxPrefabs.Count} Quest-3-safe VFX prefabs and wired Wall Habitat VFX slots.");
        }

        [MenuItem("MuseDesk/Art Pipeline/Living World/Build Gallery Scenes & Asset Audit")]
        public static void BuildGalleryScenesAndAudit()
        {
            EnsureFolders();

            Dictionary<string, GameObject> vfxPrefabs = LoadOrBuildVfxPrefabs();

            // Remember active scene if dirty-free, or build isolated gallery scenes cleanly
            var capturedScreenshots = new List<string>
            {
                BuildGallery01CharactersAndPersonalities(),
                BuildGallery02VfxAndPhysicalInteractions(vfxPrefabs),
                BuildGallery03WallHabitatElevatorDelegation(vfxPrefabs),
                BuildGallery04SpatialModesCeilingFloor(vfxPrefabs),
            };

            UltimateAssetAudit audit = RunUltimateAssetAudit(capturedScreenshots);
            string json = JsonUtility.ToJson(audit, true);
            File.WriteAllText(AuditJsonPath, json, Encoding.UTF8);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            Debug.Log($"[LivingWorldVfxAndGallerySetup] Gallery scenes & audit complete! " +
                      $"Prefabs audited={audit.totalPrefabsAudited}, meshes={audit.totalMeshes}, tris={audit.totalTriangles}, " +
                      $"renderers={audit.totalRenderers}, animators={audit.totalAnimators}, particleSystems={audit.totalParticleSystems}, " +
                      $"maxParticles={audit.maxParticlesAcrossAllPrefabs}, violations={audit.questVfxBudgetViolations.Count}. " +
                      $"Audit -> {AuditJsonPath}");
        }

        // =====================================================================
        // 1. MATERIAL & VFX PREFAB LIBRARY
        // =====================================================================

        private static void EnsureFolders()
        {
            EnsureFolderPath(MatsDir);
            EnsureFolderPath(TexturesDir);
            EnsureFolderPath(VfxPrefabsDir);
            EnsureFolderPath(WallPrefabsDir);
            EnsureFolderPath(GalleryScenesDir);
            Directory.CreateDirectory(ScreenshotDir);
            Directory.CreateDirectory(Path.GetDirectoryName(AuditJsonPath) ?? "docs/Art");
        }

        private static void EnsureFolderPath(string path)
        {
            string norm = path.Replace("\\", "/");
            if (AssetDatabase.IsValidFolder(norm)) return;
            string[] parts = norm.Split('/');
            string cur = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = cur + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(cur, parts[i]);
                cur = next;
            }
        }

        private static Material ResolveOrCreateSoftParticleMaterial()
        {
            Material existing = AssetDatabase.LoadAssetAtPath<Material>(SoftParticleMatPath);
            if (existing != null && existing.shader != null && existing.shader.name != "Hidden/InternalErrorShader")
            {
                return existing;
            }

            Material mat = AssetDatabase.LoadAssetAtPath<Material>(FallbackVfxMatPath);
            Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit")
                            ?? Shader.Find("Universal Render Pipeline/Unlit")
                            ?? Shader.Find("Particles/Standard Unlit");

            if (shader == null)
            {
                Debug.LogWarning("[LivingWorldVfxAndGallerySetup] Could not find URP Particle/Unlit shader; falling back to Sprites/Default.");
                shader = Shader.Find("Sprites/Default");
            }

            if (mat == null)
            {
                mat = new Material(shader) { name = "Mat_VFX_SoftGlow" };
                AssetDatabase.CreateAsset(mat, FallbackVfxMatPath);
            }
            else if (mat.shader != shader)
            {
                mat.shader = shader;
            }

            Texture2D glowTex = AssetDatabase.LoadAssetAtPath<Texture2D>(SoftGlowTexPath)
                                ?? EnsureSoftGlowTexture(SoftGlowTexPath);

            if (glowTex != null)
            {
                if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", glowTex);
                if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", glowTex);
            }

            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", Color.white);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", Color.white);
            if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1f);
            if (mat.HasProperty("_Blend")) mat.SetFloat("_Blend", 2f); // Additive
            if (mat.HasProperty("_SrcBlend")) mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            if (mat.HasProperty("_DstBlend")) mat.SetFloat("_DstBlend", (float)BlendMode.One);
            if (mat.HasProperty("_ZWrite")) mat.SetFloat("_ZWrite", 0f);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.renderQueue = (int)RenderQueue.Transparent;
            EditorUtility.SetDirty(mat);
            return mat;
        }

        private static Texture2D EnsureSoftGlowTexture(string assetPath)
        {
            const int size = 64;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false, false);
            float center = (size - 1) * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x - center) / center;
                    float dy = (y - center) / center;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float falloff = Mathf.Clamp01(1f - r);
                    float a = Mathf.Pow(falloff, 2.2f);
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            }
            tex.Apply();
            File.WriteAllBytes(assetPath, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);

            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
            if (AssetImporter.GetAtPath(assetPath) is TextureImporter ti)
            {
                ti.textureType = TextureImporterType.Default;
                ti.alphaIsTransparency = true;
                ti.wrapMode = TextureWrapMode.Clamp;
                ti.filterMode = FilterMode.Bilinear;
                ti.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
        }

        private static GameObject BuildOrUpdateVfxPrefab(VfxSpec spec, Material particleMat)
        {
            var go = new GameObject(spec.Name);
            try
            {
                var ps = go.AddComponent<ParticleSystem>();
                var main = ps.main;
                main.playOnAwake = false;
                main.loop = spec.Loop;
                main.duration = spec.Duration;
                main.maxParticles = spec.MaxParticles;
                main.startLifetime = spec.StartLifetime;
                main.startSpeed = new ParticleSystem.MinMaxCurve(spec.StartSpeedMin, spec.StartSpeedMax);
                main.startSize = new ParticleSystem.MinMaxCurve(spec.StartSizeMin, spec.StartSizeMax);
                main.startColor = new ParticleSystem.MinMaxGradient(spec.StartColorA, spec.StartColorB);
                main.gravityModifier = spec.GravityModifier;
                main.simulationSpace = ParticleSystemSimulationSpace.Local;
                main.stopAction = ParticleSystemStopAction.Disable;

                var em = ps.emission;
                em.enabled = true;
                em.rateOverTime = spec.RateOverTime;
                if (spec.BurstCount > 0)
                {
                    em.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)Mathf.Min(spec.BurstCount, 20)) });
                }
                else
                {
                    em.SetBursts(Array.Empty<ParticleSystem.Burst>());
                }

                var shape = ps.shape;
                shape.enabled = true;
                shape.shapeType = spec.ShapeType;
                shape.rotation = spec.ShapeRotation;
                shape.scale = spec.ShapeScale == Vector3.zero ? Vector3.one : spec.ShapeScale;
                shape.radius = spec.ShapeRadius;
                shape.radiusThickness = spec.ShapeRadiusThickness;
                if (spec.ShapeType == ParticleSystemShapeType.Cone)
                {
                    shape.angle = spec.ShapeAngle;
                }
                if (spec.ShapeType == ParticleSystemShapeType.Donut)
                {
                    shape.donutRadius = spec.DonutRadius;
                }

                var colLife = ps.colorOverLifetime;
                colLife.enabled = true;
                var grad = new Gradient();
                grad.SetKeys(
                    new[]
                    {
                        new GradientColorKey(spec.StartColorA, 0f),
                        new GradientColorKey(spec.MidColor, 0.45f),
                        new GradientColorKey(spec.EndColor, 1f)
                    },
                    new[]
                    {
                        new GradientAlphaKey(0f, 0f),
                        new GradientAlphaKey(0.95f, 0.16f),
                        new GradientAlphaKey(0.80f, 0.70f),
                        new GradientAlphaKey(0f, 1f)
                    });
                colLife.color = grad;

                var sizeLife = ps.sizeOverLifetime;
                sizeLife.enabled = true;
                sizeLife.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                    new Keyframe(0f, 0.25f),
                    new Keyframe(0.28f, 1f),
                    new Keyframe(1f, 0f)));

                var rend = go.GetComponent<ParticleSystemRenderer>();
                if (rend != null)
                {
                    rend.sharedMaterial = particleMat;
                    rend.renderMode = ParticleSystemRenderMode.Billboard;
                    rend.shadowCastingMode = ShadowCastingMode.Off;
                    rend.receiveShadows = false;
                    rend.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
                    rend.allowOcclusionWhenDynamic = false;
                }

                string prefabPath = $"{VfxPrefabsDir}/{spec.Name}.prefab";
                return PrefabUtility.SaveAsPrefabAsset(go, prefabPath);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        private static Dictionary<string, GameObject> LoadOrBuildVfxPrefabs()
        {
            var map = new Dictionary<string, GameObject>(StringComparer.OrdinalIgnoreCase);
            bool anyMissing = false;
            foreach (VfxSpec spec in VfxLibrarySpecs)
            {
                string path = $"{VfxPrefabsDir}/{spec.Name}.prefab";
                GameObject p = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (p == null)
                {
                    anyMissing = true;
                    break;
                }
                map[spec.Name] = p;
            }

            if (anyMissing)
            {
                BuildVfxLibrary();
                map.Clear();
                foreach (VfxSpec spec in VfxLibrarySpecs)
                {
                    GameObject p = AssetDatabase.LoadAssetAtPath<GameObject>($"{VfxPrefabsDir}/{spec.Name}.prefab");
                    if (p != null) map[spec.Name] = p;
                }
            }

            return map;
        }

        // =====================================================================
        // 2. IDEMPOTENT WALL HABITAT PREFAB WIRING & LOW-FX VARIANT
        // =====================================================================

        private static void WireWallHabitatPrefabs(Dictionary<string, GameObject> vfxPrefabs)
        {
            if (vfxPrefabs.TryGetValue("VFX_Portal_Large_Full", out GameObject fullPortalVfx))
            {
                WirePortalPrefab(
                    $"{WallPrefabsDir}/WallHabitat_LargeAgentPortal.prefab",
                    $"{WallPrefabsDir}/WallHabitat_LargeAgentPortal.prefab",
                    fullPortalVfx,
                    "VFX_Portal_Large_Full");
            }

            if (vfxPrefabs.TryGetValue("VFX_Portal_Large_LowFx", out GameObject lowPortalVfx))
            {
                WirePortalPrefab(
                    $"{WallPrefabsDir}/WallHabitat_LargeAgentPortal.prefab",
                    $"{WallPrefabsDir}/WallHabitat_LargePortal_LowFx.prefab",
                    lowPortalVfx,
                    "VFX_Portal_Large_LowFx");
            }

            if (vfxPrefabs.TryGetValue("VFX_Blueprint_Print", out GameObject printVfx))
            {
                WireTaskPrinterPrefab($"{WallPrefabsDir}/WallHabitat_TaskPrinter.prefab", printVfx);
            }

            if (vfxPrefabs.TryGetValue("VFX_Elevator_Arrival", out GameObject elevatorVfx))
            {
                WireElevatorCarPrefab($"{WallPrefabsDir}/WallHabitat_ElevatorCar.prefab", elevatorVfx);
            }
        }

        private static void WirePortalPrefab(
            string sourcePrefabPath,
            string targetPrefabPath,
            GameObject vfxPrefab,
            string childName)
        {
            if (!File.Exists(sourcePrefabPath) || vfxPrefab == null)
            {
                Debug.LogWarning($"[LivingWorldVfxAndGallerySetup] Missing portal prefab or VFX at {sourcePrefabPath}");
                return;
            }

            GameObject root = PrefabUtility.LoadPrefabContents(sourcePrefabPath);
            try
            {
                RemoveChildrenByName(root.transform, "VFX_PortalSparseMotes", "VFX_Portal_Large_Full", "VFX_Portal_Large_LowFx");

                var vfxInst = (GameObject)PrefabUtility.InstantiatePrefab(vfxPrefab, root.transform);
                vfxInst.name = childName;
                vfxInst.transform.localPosition = new Vector3(0f, 0.03f, 0f);
                vfxInst.transform.localRotation = Quaternion.identity;
                vfxInst.transform.localScale = Vector3.one;

                var ps = vfxInst.GetComponent<ParticleSystem>();
                var portalCtrl = root.GetComponent<WallHabitatLargePortalVisualController>()
                                 ?? root.AddComponent<WallHabitatLargePortalVisualController>();

                var so = new SerializedObject(portalCtrl);
                SerializedProperty motesProp = so.FindProperty("optionalSparseMotes");
                if (motesProp != null)
                {
                    motesProp.objectReferenceValue = ps;
                    so.ApplyModifiedPropertiesWithoutUndo();
                }

                portalCtrl.ResolveReferences();
                EditorUtility.SetDirty(root);
                PrefabUtility.SaveAsPrefabAsset(root, targetPrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static void WireTaskPrinterPrefab(string printerPath, GameObject printVfxPrefab)
        {
            if (!File.Exists(printerPath) || printVfxPrefab == null)
            {
                Debug.LogWarning($"[LivingWorldVfxAndGallerySetup] Missing TaskPrinter prefab at {printerPath}");
                return;
            }

            GameObject root = PrefabUtility.LoadPrefabContents(printerPath);
            try
            {
                RemoveChildrenByName(root.transform, "VFX_Blueprint_Print");

                Transform mountParent = FindChildRecursive(root.transform, "TaskCardSpawnPose")
                                        ?? FindChildRecursive(root.transform, "CardOutput_Tray")
                                        ?? root.transform;

                var vfxInst = (GameObject)PrefabUtility.InstantiatePrefab(printVfxPrefab, mountParent);
                vfxInst.name = "VFX_Blueprint_Print";
                vfxInst.transform.localPosition = mountParent == root.transform
                    ? new Vector3(0f, 0.03f, 0.048f)
                    : Vector3.zero;
                vfxInst.transform.localRotation = Quaternion.identity;
                vfxInst.transform.localScale = Vector3.one;

                Type delegationType = FindTypeInLoadedAssemblies("MuseDesk.Art.WallHabitatManagerDelegationVisualController");
                if (delegationType != null)
                {
                    Component ctrl = root.GetComponent(delegationType) ?? root.AddComponent(delegationType);
                    WireVfxReferenceBySerializedObject(ctrl, vfxInst.GetComponent<ParticleSystem>(), vfxInst,
                        "optionalPrintParticles", "blueprintPrintVfx", "printVfx", "printParticles", "vfxBlueprintPrint", "blueprintVfx");

                    MethodInfo resolveMethod = delegationType.GetMethod("ResolveReferences", BindingFlags.Instance | BindingFlags.Public);
                    resolveMethod?.Invoke(ctrl, null);
                }
                else
                {
                    Debug.LogWarning("[LivingWorldVfxAndGallerySetup] MuseDesk.Art.WallHabitatManagerDelegationVisualController type not found yet; TaskPrinter VFX mounted and ready for controller attach.");
                }

                EditorUtility.SetDirty(root);
                PrefabUtility.SaveAsPrefabAsset(root, printerPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static void WireElevatorCarPrefab(string carPath, GameObject elevatorVfxPrefab)
        {
            if (!File.Exists(carPath) || elevatorVfxPrefab == null)
            {
                Debug.LogWarning($"[LivingWorldVfxAndGallerySetup] Missing ElevatorCar prefab at {carPath}");
                return;
            }

            GameObject root = PrefabUtility.LoadPrefabContents(carPath);
            try
            {
                RemoveChildrenByName(root.transform, "VFX_Elevator_Arrival");

                Transform mountParent = FindChildRecursive(root.transform, "ElevatorCabinPose")
                                        ?? root.transform;

                var vfxInst = (GameObject)PrefabUtility.InstantiatePrefab(elevatorVfxPrefab, mountParent);
                vfxInst.name = "VFX_Elevator_Arrival";
                vfxInst.transform.localPosition = mountParent == root.transform
                    ? new Vector3(0f, 0.01f, 0f)
                    : Vector3.zero;
                vfxInst.transform.localRotation = Quaternion.identity;
                vfxInst.transform.localScale = Vector3.one;

                EditorUtility.SetDirty(root);
                PrefabUtility.SaveAsPrefabAsset(root, carPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static void WireVfxReferenceBySerializedObject(
            Component ctrl,
            ParticleSystem ps,
            GameObject vfxGo,
            params string[] candidateFieldNames)
        {
            if (ctrl == null) return;
            var so = new SerializedObject(ctrl);
            bool modified = false;

            foreach (string fieldName in candidateFieldNames)
            {
                SerializedProperty prop = so.FindProperty(fieldName);
                if (prop == null || prop.propertyType != SerializedPropertyType.ObjectReference) continue;
                string pType = prop.type;
                if (pType.IndexOf("ParticleSystem", StringComparison.OrdinalIgnoreCase) >= 0 && ps != null)
                {
                    prop.objectReferenceValue = ps;
                    modified = true;
                    break;
                }
                if (pType.IndexOf("GameObject", StringComparison.OrdinalIgnoreCase) >= 0 && vfxGo != null)
                {
                    prop.objectReferenceValue = vfxGo;
                    modified = true;
                    break;
                }
            }

            if (modified) so.ApplyModifiedPropertiesWithoutUndo();
        }

        // =====================================================================
        // 3. FOUR ISOLATED ART VALIDATION GALLERY SCENES
        // =====================================================================

        private static string BuildGallery01CharactersAndPersonalities()
        {
            string scenePath = $"{GalleryScenesDir}/Gallery_01_Characters_And_Personalities.unity";
            Scene scene = CreateStudioScene(out Camera cam, new Color(0.18f, 0.21f, 0.26f, 1f), 40f);

            Material floorMat = LoadMat("Mat_FeltSlate");
            Material wallMat = LoadMat("Mat_WarmBeige");
            Material deskMat = LoadMat("Mat_WarmWood");

            CreateStudioBox("Studio_Floor", new Vector3(0f, -0.025f, 0f), new Vector3(6.0f, 0.05f, 4.5f), floorMat);
            CreateStudioBox("Studio_BackWall", new Vector3(0f, 1.35f, -0.55f), new Vector3(6.0f, 2.70f, 0.05f), wallMat);
            CreateStudioBox("Studio_ShowcasePlinth", new Vector3(0f, 0.37f, 0.15f), new Vector3(2.20f, 0.74f, 0.85f), deskMat);

            var specs = new (string Path, string Label, string PoseClip, float Scale, string PairStationPath)[]
            {
                ($"{CharactersPrefabsDir}/Agent_Researcher.prefab", "Researcher\n(Curious / Analytical)", "Thinking", 1.35f,
                    "Assets/MuseDesk/Art/Prefabs/VisualPolish/Workstation_Research_Optimized.prefab"),
                ($"{CharactersPrefabsDir}/Agent_Developer.prefab", "Developer\n(Eager / Builder)", "Excited", 1.35f,
                    "Assets/MuseDesk/Art/Prefabs/VisualPolish/Workstation_Developer_Optimized.prefab"),
                ($"{CharactersPrefabsDir}/Agent_Tester.prefab", "Tester\n(Alert / Inspector)", "Inspecting", 1.35f,
                    "Assets/MuseDesk/Art/Prefabs/VisualPolish/Workstation_Testing_Optimized.prefab"),
                ($"{WallPrefabsDir}/Agent_Manager.prefab", "Manager\n(Planner / Lookout)", "Manager_Lookout_Idle", 1.35f,
                    $"{WallPrefabsDir}/WallHabitat_TaskPrinter.prefab"),
            };

            float startX = -0.66f;
            float spacingX = 0.44f;

            for (int i = 0; i < specs.Length; i++)
            {
                float x = startX + i * spacingX;
                var item = specs[i];

                GameObject agent = SpawnPrefabAt(item.Path, new Vector3(x, 0.74f, 0.28f), Quaternion.identity, Vector3.one * item.Scale);
                if (agent != null)
                {
                    SampleControllerClip(agent, item.PoseClip, 0.25f);
                }

                SpawnPrefabAt(item.PairStationPath, new Vector3(x, 0.74f, -0.04f), Quaternion.Euler(0f, 180f, 0f), Vector3.one * 0.85f);

                AddWorldTextLabel(null, $"Label_{i}_{item.Label}", item.Label,
                    new Vector3(x, 0.76f, 0.50f), new Vector3(22f, 0f, 0f), 0.0018f, new Color(0.95f, 0.96f, 0.98f), new Vector2(85f, 22f));
            }

            AddWorldTextLabel(null, "Title_Gallery01", "GALLERY 01 — CHARACTERS & PERSONALITIES",
                new Vector3(0f, 1.32f, -0.50f), Vector3.zero, 0.0030f, new Color(0.22f, 0.25f, 0.30f), new Vector2(180f, 20f));

            SaveSceneWithCameraPose(scene, scenePath, cam, new Vector3(0f, 1.18f, 1.72f), new Vector3(0f, 0.90f, 0.14f));
            return WarmupAndCapture(cam, "01_Gallery_Characters_Personalities.png",
                new Vector3(0f, 1.18f, 1.72f), new Vector3(0f, 0.90f, 0.14f));
        }

        private static string BuildGallery02VfxAndPhysicalInteractions(Dictionary<string, GameObject> vfxPrefabs)
        {
            string scenePath = $"{GalleryScenesDir}/Gallery_02_VFX_And_PhysicalInteractions.unity";
            Scene scene = CreateStudioScene(out Camera cam, new Color(0.14f, 0.17f, 0.22f, 1f), 42f);

            Material floorMat = LoadMat("Mat_FeltSlate");
            Material deskMat = LoadMat("Mat_DarkWood");
            Material padMat = LoadMat("Mat_DS_FeltSlate") ?? floorMat;

            CreateStudioBox("Studio_Floor", new Vector3(0f, -0.025f, 0f), new Vector3(7.5f, 0.05f, 5.0f), floorMat);
            CreateStudioBox("Studio_InteractionTable", new Vector3(0f, 0.37f, 0.15f), new Vector3(2.90f, 0.74f, 1.35f), deskMat);
            CreateStudioBox("Studio_VfxBackdrop", new Vector3(0f, 1.25f, -0.60f), new Vector3(3.20f, 1.40f, 0.05f), padMat);

            // Front row: Physical Documents & Stamps paired with trigger VFX
            SpawnPrefabAt("Assets/MuseDesk/Art/Prefabs/VisualPolish/DecisionPaper_Expanded_Polished.prefab",
                new Vector3(-0.78f, 0.742f, 0.42f), Quaternion.identity, Vector3.one);
            SpawnPrefabAt($"{DocumentsPrefabsDir}/Doc_StampInkMark_Approve.prefab",
                new Vector3(-0.85f, 0.752f, 0.43f), Quaternion.Euler(0f, -8f, 0f), Vector3.one);
            SpawnPrefabAt($"{DocumentsPrefabsDir}/Doc_StampInkMark_Reject.prefab",
                new Vector3(-0.71f, 0.752f, 0.43f), Quaternion.Euler(0f, 8f, 0f), Vector3.one);
            SpawnPrefabAt("Assets/MuseDesk/Art/Prefabs/VisualPolish/Doc_Stamp_Approve_Polished.prefab",
                new Vector3(-0.96f, 0.76f, 0.34f), Quaternion.identity, Vector3.one);
            SpawnPrefabAt("Assets/MuseDesk/Art/Prefabs/VisualPolish/Doc_Stamp_Reject_Polished.prefab",
                new Vector3(-0.60f, 0.76f, 0.34f), Quaternion.identity, Vector3.one);

            SpawnSimulatedVfx(vfxPrefabs, "VFX_Approval_Seal", new Vector3(-0.96f, 0.80f, 0.34f), 0.20f);
            SpawnSimulatedVfx(vfxPrefabs, "VFX_Rejection_Pulse", new Vector3(-0.60f, 0.80f, 0.34f), 0.20f);

            SpawnPrefabAt($"{DocumentsPrefabsDir}/Doc_BlueprintSheet.prefab",
                new Vector3(-0.12f, 0.745f, 0.42f), Quaternion.Euler(0f, 6f, 0f), Vector3.one);
            SpawnSimulatedVfx(vfxPrefabs, "VFX_Blueprint_Print", new Vector3(-0.12f, 0.79f, 0.42f), 0.20f);

            SpawnPrefabAt("Assets/MuseDesk/Art/Prefabs/VisualPolish/TaskCard_Grabbable_Polished.prefab",
                new Vector3(0.28f, 0.755f, 0.42f), Quaternion.Euler(0f, -10f, 0f), Vector3.one);
            SpawnSimulatedVfx(vfxPrefabs, "VFX_Interaction_Sparkle", new Vector3(0.28f, 0.80f, 0.42f), 0.18f);

            SpawnPrefabAt("Assets/MuseDesk/Art/Prefabs/VisualPolish/Workstation_Testing_Optimized.prefab",
                new Vector3(0.76f, 0.74f, 0.38f), Quaternion.Euler(0f, 180f, 0f), Vector3.one);
            SpawnSimulatedVfx(vfxPrefabs, "VFX_Test_Pass", new Vector3(0.68f, 0.84f, 0.38f), 0.20f);
            SpawnSimulatedVfx(vfxPrefabs, "VFX_Test_Fail", new Vector3(0.86f, 0.84f, 0.38f), 0.20f);

            // Back display: All 13 Quest-Safe VFX Prefabs in an ordered labeled showcase
            for (int i = 0; i < VfxLibrarySpecs.Length; i++)
            {
                int col = i % 7;
                int row = i / 7;
                float x = -1.08f + col * 0.36f;
                float y = 0.86f + (1 - row) * 0.34f;
                float z = -0.22f + row * 0.20f;

                string vfxName = VfxLibrarySpecs[i].Name;
                CreateStudioBox($"Pedestal_{vfxName}", new Vector3(x, y - 0.08f, z), new Vector3(0.08f, 0.03f, 0.08f), floorMat);
                float simTime = VfxLibrarySpecs[i].Loop ? 0.85f : 0.20f;
                SpawnSimulatedVfx(vfxPrefabs, vfxName, new Vector3(x, y, z), simTime);

                string shortName = vfxName.Replace("VFX_", "").Replace("_", " ");
                AddWorldTextLabel(null, $"Label_{vfxName}", $"{i + 1}. {shortName}",
                    new Vector3(x, y - 0.11f, z + 0.06f), new Vector3(12f, 0f, 0f), 0.0011f, new Color(0.90f, 0.94f, 0.98f), new Vector2(110f, 16f));
            }

            AddWorldTextLabel(null, "Title_Gallery02", "GALLERY 02 — QUEST-SAFE VFX & PHYSICAL INTERACTIONS",
                new Vector3(0f, 1.54f, -0.56f), Vector3.zero, 0.0028f, new Color(0.92f, 0.95f, 0.99f), new Vector2(220f, 20f));

            SaveSceneWithCameraPose(scene, scenePath, cam, new Vector3(0f, 1.38f, 1.88f), new Vector3(0f, 0.96f, 0.10f));
            return WarmupAndCapture(cam, "02_Gallery_VFX_And_Documents.png",
                new Vector3(0f, 1.38f, 1.88f), new Vector3(0f, 0.96f, 0.10f));
        }

        private static string BuildGallery03WallHabitatElevatorDelegation(Dictionary<string, GameObject> vfxPrefabs)
        {
            string scenePath = $"{GalleryScenesDir}/Gallery_03_WallHabitat_Elevator_Delegation.unity";
            Scene scene = CreateStudioScene(out Camera cam, new Color(0.18f, 0.21f, 0.26f, 1f), 44f);

            Material floorMat = LoadMat("Mat_FeltSlate");
            Material wallMat = LoadMat("Mat_WarmBeige");
            Material deskMat = LoadMat("Mat_WarmWood");

            CreateStudioBox("Studio_Floor", new Vector3(0.4f, -0.025f, 0.5f), new Vector3(8.0f, 0.05f, 5.0f), floorMat);
            CreateStudioBox("Studio_MockWall", new Vector3(0.4f, 1.45f, -0.02f), new Vector3(6.5f, 2.90f, 0.04f), wallMat);
            CreateStudioBox("Studio_DeskProxy", new Vector3(-0.68f, 0.37f, 0.85f), new Vector3(0.95f, 0.74f, 0.48f), deskMat);

            // 1. 3-Tier Wall Habitat with wired TaskPrinter & ElevatorCar VFX
            GameObject habitat3 = SpawnPrefabAt($"{WallPrefabsDir}/WallHabitat_Complete_3Tier.prefab",
                new Vector3(-0.68f, 1.48f, 0f), Quaternion.identity, Vector3.one);
            if (habitat3 != null)
            {
                SimulateAllParticlesInHierarchy(habitat3, 0.20f);
                var elevCtrl = habitat3.GetComponentInChildren<WallHabitatElevatorVisualController>(true);
                if (elevCtrl != null)
                {
                    elevCtrl.ResolveReferences();
                    elevCtrl.MoveCabToStop("ElevatorStop_Mid", instant: true);
                }
            }

            SpawnPrefabAt($"{WallPrefabsDir}/WallHabitat_DeskVisit_Complete.prefab",
                new Vector3(-0.68f, 0.74f, 0.85f), Quaternion.identity, Vector3.one);

            // 2. Full-VFX Large Agent Portal vs Low-FX Variant side-by-side
            GameObject fullPortal = SpawnPrefabAt($"{WallPrefabsDir}/WallHabitat_LargeAgentPortal.prefab",
                new Vector3(0.36f, 0.0f, 0.55f), Quaternion.identity, Vector3.one);
            if (fullPortal != null)
            {
                Transform pose = FindChildRecursive(fullPortal.transform, "AgentHomePose");
                if (pose != null)
                {
                    GameObject proxy = SpawnPrefabAt($"{CharactersPrefabsDir}/Agent_Developer.prefab",
                        pose.position, pose.rotation, Vector3.one * 3.2f);
                    if (proxy != null) proxy.transform.SetParent(pose, true);
                }
                SimulateAllParticlesInHierarchy(fullPortal, 0.85f);
            }
            AddWorldTextLabel(null, "Label_PortalFull", "Portal: Full VFX (<=16 motes)",
                new Vector3(0.36f, 1.02f, 0.55f), Vector3.zero, 0.0018f, new Color(0.24f, 0.92f, 0.86f), new Vector2(120f, 16f));

            GameObject lowPortal = SpawnPrefabAt($"{WallPrefabsDir}/WallHabitat_LargePortal_LowFx.prefab",
                new Vector3(1.16f, 0.0f, 0.55f), Quaternion.identity, Vector3.one);
            if (lowPortal != null)
            {
                Transform pose = FindChildRecursive(lowPortal.transform, "AgentHomePose");
                if (pose != null)
                {
                    GameObject proxy = SpawnPrefabAt($"{CharactersPrefabsDir}/Agent_Researcher.prefab",
                        pose.position, pose.rotation, Vector3.one * 3.2f);
                    if (proxy != null) proxy.transform.SetParent(pose, true);
                }
                SimulateAllParticlesInHierarchy(lowPortal, 0.20f);
            }
            AddWorldTextLabel(null, "Label_PortalLowFx", "Portal: LowFx Quest Variant",
                new Vector3(1.16f, 1.02f, 0.55f), Vector3.zero, 0.0018f, new Color(0.95f, 0.82f, 0.45f), new Vector2(120f, 16f));

            // Project Complete Celebration burst between portals/desk for visual completeness
            SpawnSimulatedVfx(vfxPrefabs, "VFX_Project_Complete_Celebration", new Vector3(-0.16f, 0.92f, 0.55f), 0.26f);

            AddWorldTextLabel(null, "Title_Gallery03", "GALLERY 03 — WALL HABITAT, ELEVATOR, DELEGATION & PORTAL FULL VS LOWFX",
                new Vector3(0.24f, 2.04f, 0.02f), Vector3.zero, 0.0026f, new Color(0.20f, 0.24f, 0.30f), new Vector2(280f, 20f));

            SaveSceneWithCameraPose(scene, scenePath, cam, new Vector3(0.24f, 1.24f, 2.55f), new Vector3(0.24f, 1.02f, 0.25f));
            return WarmupAndCapture(cam, "03_Gallery_WallHabitat_Portal_FullVsLow.png",
                new Vector3(0.24f, 1.24f, 2.55f), new Vector3(0.24f, 1.02f, 0.25f));
        }

        private static string BuildGallery04SpatialModesCeilingFloor(Dictionary<string, GameObject> vfxPrefabs)
        {
            string scenePath = $"{GalleryScenesDir}/Gallery_04_SpatialModes_Ceiling_Floor.unity";
            Scene scene = CreateStudioScene(out Camera cam, new Color(0.18f, 0.21f, 0.26f, 1f), 45f);

            Material floorMat = LoadMat("Mat_FeltSlate");
            Material ceilMat = LoadMat("Mat_WarmBeige");
            Material deskMat = LoadMat("Mat_WarmWood");

            CreateStudioBox("Studio_Floor", new Vector3(0.1f, -0.025f, 0.2f), new Vector3(8.0f, 0.05f, 5.5f), floorMat);
            CreateStudioBox("Studio_CeilingBeam", new Vector3(0.55f, 2.43f, 0.0f), new Vector3(3.2f, 0.06f, 0.9f), ceilMat);
            CreateStudioBox("Studio_DeskProxy", new Vector3(0.05f, 0.37f, 0.72f), new Vector3(1.10f, 0.74f, 0.52f), deskMat);

            // 1. Floor Office Complete (includes Courier System) + Compact Floor Office Platform
            SpawnPrefabAt($"{SpatialPrefabsDir}/Spatial_FloorOffice_Complete.prefab",
                new Vector3(-0.95f, 0f, 0.05f), Quaternion.identity, Vector3.one);
            SpawnPrefabAt($"{SpatialPrefabsDir}/FloorOfficePlatform_Compact.prefab",
                new Vector3(-1.82f, 0f, 0.15f), Quaternion.identity, Vector3.one);

            // 2. Ceiling Pod Full VFX (Descending)
            GameObject ceilFull = SpawnPrefabAt($"{SpatialPrefabsDir}/Spatial_CeilingPod_Complete.prefab",
                new Vector3(0.20f, 2.40f, 0.0f), Quaternion.identity, Vector3.one);
            if (ceilFull != null)
            {
                var ctrl = ceilFull.GetComponent<CeilingWorkspacePodController>();
                if (ctrl != null)
                {
                    ctrl.ConfigureRoomPoses(new Vector3(0.20f, 2.012f, 0.0f), new Vector3(0.20f, 1.48f, 0.0f), Quaternion.identity);
                    ctrl.SnapToPoseImmediate(true);
                }
                var vfxCtrl = ceilFull.GetComponent<CeilingDeploymentVFXController>();
                if (vfxCtrl != null)
                {
                    vfxCtrl.SetQualityPreset(CeilingVfxQualityPreset.Full);
                    vfxCtrl.ApplySnapshotImmediate(CeilingWorkspacePodController.PodState.Descending, 0.35f);
                }
                SimulateAllParticlesInHierarchy(ceilFull, 0.45f);
            }

            // 3. Ceiling Pod LowCostNoParticles (Deployed)
            GameObject ceilLow = SpawnPrefabAt($"{SpatialPrefabsDir}/Spatial_CeilingPod_Complete.prefab",
                new Vector3(1.05f, 2.40f, 0.0f), Quaternion.identity, Vector3.one);
            if (ceilLow != null)
            {
                var ctrl = ceilLow.GetComponent<CeilingWorkspacePodController>();
                if (ctrl != null)
                {
                    ctrl.ConfigureRoomPoses(new Vector3(1.05f, 2.012f, 0.0f), new Vector3(1.05f, 1.06f, 0.0f), Quaternion.identity);
                    ctrl.SnapToPoseImmediate(true);
                }
                var vfxCtrl = ceilLow.GetComponent<CeilingDeploymentVFXController>();
                if (vfxCtrl != null)
                {
                    vfxCtrl.SetQualityPreset(CeilingVfxQualityPreset.LowCostNoParticles);
                    vfxCtrl.ApplySnapshotImmediate(CeilingWorkspacePodController.PodState.DeployedIdle, 0f);
                }
            }

            // 4. DocumentCourier in flight with VFX_Courier_Trail_High + Mailbox & Beacon on desk
            GameObject courier = SpawnPrefabAt($"{SpatialPrefabsDir}/DocumentCourier.prefab",
                new Vector3(0.05f, 1.02f, 0.68f), Quaternion.Euler(0f, 15f, 0f), Vector3.one);
            if (courier != null)
            {
                Transform docSocket = FindChildRecursive(courier.transform, "DocumentAttachSocket");
                if (docSocket != null)
                {
                    GameObject carriedDoc = SpawnPrefabAt($"{DocumentsPrefabsDir}/Doc_ResearchDocument.prefab",
                        docSocket.position, docSocket.rotation, Vector3.one * 0.75f);
                    if (carriedDoc != null)
                    {
                        SpatialScaleUtility.MountPreservingWorldScale(carriedDoc.transform, docSocket, Vector3.one * 0.75f, true);
                    }
                }
                if (vfxPrefabs.TryGetValue("VFX_Courier_Trail_High", out GameObject trailPrefab))
                {
                    var trail = (GameObject)PrefabUtility.InstantiatePrefab(trailPrefab, courier.transform);
                    trail.name = "VFX_Courier_Trail_High";
                    trail.transform.localPosition = new Vector3(0f, 0.02f, -0.08f);
                    trail.transform.localRotation = Quaternion.identity;
                    SimulateParticleSystem(trail.GetComponent<ParticleSystem>(), 0.22f);
                }
            }

            GameObject mailbox = SpawnPrefabAt($"{SpatialPrefabsDir}/SpatialMailbox.prefab",
                new Vector3(0.42f, 0.74f, 0.72f), Quaternion.identity, Vector3.one);
            mailbox?.GetComponent<SpatialMailboxController>()?.UpdateCounts(2, 1, 1);

            GameObject beacon = SpawnPrefabAt($"{SpatialPrefabsDir}/WorkspaceAttentionBeacon.prefab",
                new Vector3(-0.34f, 0.74f, 0.72f), Quaternion.identity, Vector3.one);
            beacon?.GetComponent<SpatialBeaconVisualController>()?.SetState(SpatialBeaconVisualController.BeaconState.BlockingDecision, true);

            AddWorldTextLabel(null, "Title_Gallery04", "GALLERY 04 — SPATIAL MODES: FLOOR, CEILING POD & COURIER",
                new Vector3(0.05f, 2.26f, 0.05f), Vector3.zero, 0.0026f, new Color(0.92f, 0.95f, 0.99f), new Vector2(240f, 20f));

            SaveSceneWithCameraPose(scene, scenePath, cam, new Vector3(0.05f, 1.44f, 2.85f), new Vector3(0.05f, 1.05f, 0.20f));
            return WarmupAndCapture(cam, "04_Gallery_Spatial_Ceiling_Floor_Courier.png",
                new Vector3(0.05f, 1.44f, 2.85f), new Vector3(0.05f, 1.05f, 0.20f));
        }

        // =====================================================================
        // 4. ULTIMATE ASSET AUDIT (JSON)
        // =====================================================================

        private static UltimateAssetAudit RunUltimateAssetAudit(List<string> screenshots)
        {
            var audit = new UltimateAssetAudit
            {
                generatedAtUtc = DateTime.UtcNow.ToString("O"),
                unityVersion = Application.unityVersion,
                screenshots = screenshots ?? new List<string>()
            };

            var categories = new (string Name, string Dir)[]
            {
                ("Characters", CharactersPrefabsDir),
                ("WallHabitat", WallPrefabsDir),
                ("VFX", VfxPrefabsDir),
                ("Documents", DocumentsPrefabsDir),
                ("Spatial", SpatialPrefabsDir),
            };

            foreach (var cat in categories)
            {
                var summary = new CategoryAuditSummary { category = cat.Name };
                var categoryMaterials = new HashSet<string>(StringComparer.Ordinal);

                if (AssetDatabase.IsValidFolder(cat.Dir))
                {
                    string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { cat.Dir });
                    var paths = guids.Select(AssetDatabase.GUIDToAssetPath)
                                     .OrderBy(p => p, StringComparer.Ordinal)
                                     .ToArray();

                    foreach (string prefabPath in paths)
                    {
                        GameObject prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                        if (prefabAsset == null) continue;

                        PrefabAuditEntry entry = AuditSinglePrefab(cat.Name, prefabAsset, prefabPath, audit.questVfxBudgetViolations);
                        audit.prefabs.Add(entry);

                        audit.totalPrefabsAudited++;
                        audit.totalMissingScripts += entry.missingScripts;
                        audit.totalMissingMaterials += entry.missingMaterials;
                        audit.totalMeshes += entry.meshCount;
                        audit.totalTriangles += entry.triangleCount;
                        audit.totalRenderers += entry.rendererCount;
                        audit.totalAnimators += entry.animatorCount;
                        audit.totalParticleSystems += entry.particleSystemCount;
                        if (entry.maxParticles > audit.maxParticlesAcrossAllPrefabs)
                            audit.maxParticlesAcrossAllPrefabs = entry.maxParticles;

                        summary.prefabCount++;
                        summary.meshCount += entry.meshCount;
                        summary.triangleCount += entry.triangleCount;
                        summary.rendererCount += entry.rendererCount;
                        summary.animatorCount += entry.animatorCount;
                        summary.particleSystemCount += entry.particleSystemCount;
                        if (entry.maxParticles > summary.maxParticles)
                            summary.maxParticles = entry.maxParticles;

                        foreach (string mn in entry.materialNames)
                            categoryMaterials.Add(mn);
                    }
                }

                summary.materialCount = categoryMaterials.Count;
                audit.categorySummaries.Add(summary);
            }

            return audit;
        }

        private static PrefabAuditEntry AuditSinglePrefab(
            string category,
            GameObject prefabAsset,
            string prefabPath,
            List<string> violations)
        {
            var go = UnityEngine.Object.Instantiate(prefabAsset);
            try
            {
                var entry = new PrefabAuditEntry
                {
                    category = category,
                    prefabName = prefabAsset.name,
                    prefabPath = prefabPath
                };

                foreach (Component c in go.GetComponentsInChildren<Component>(true))
                {
                    if (c == null) entry.missingScripts++;
                }

                var matNames = new HashSet<string>(StringComparer.Ordinal);
                Renderer[] renderers = go.GetComponentsInChildren<Renderer>(true);
                entry.rendererCount = renderers.Length;

                foreach (Renderer r in renderers)
                {
                    Mesh shared = null;
                    if (r is SkinnedMeshRenderer smr) shared = smr.sharedMesh;
                    else if (r is MeshRenderer)
                    {
                        var mf = r.GetComponent<MeshFilter>();
                        if (mf != null) shared = mf.sharedMesh;
                    }

                    if (shared != null)
                    {
                        entry.meshCount++;
                        for (int s = 0; s < shared.subMeshCount; s++)
                            entry.triangleCount += (int)(shared.GetIndexCount(s) / 3);
                    }

                    foreach (Material mat in r.sharedMaterials)
                    {
                        if (mat == null) entry.missingMaterials++;
                        else matNames.Add(mat.name);
                    }
                }

                entry.materialNames = matNames.OrderBy(n => n, StringComparer.Ordinal).ToArray();
                entry.materialCount = entry.materialNames.Length;
                entry.animatorCount = go.GetComponentsInChildren<Animator>(true).Length;

                ParticleSystem[] particleSystems = go.GetComponentsInChildren<ParticleSystem>(true);
                entry.particleSystemCount = particleSystems.Length;
                int totalMaxParticles = 0;
                foreach (ParticleSystem ps in particleSystems)
                {
                    totalMaxParticles += ps.main.maxParticles;

                    if (category == "VFX")
                    {
                        CheckQuestVfxConstraints(prefabAsset.name, ps, violations);
                    }
                }
                entry.maxParticles = totalMaxParticles;

                return entry;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        private static void CheckQuestVfxConstraints(string prefabName, ParticleSystem ps, List<string> violations)
        {
            var main = ps.main;
            if (main.playOnAwake)
                violations.Add($"{prefabName}: playOnAwake must be false.");

            bool isPortalFull = string.Equals(prefabName, "VFX_Portal_Large_Full", StringComparison.OrdinalIgnoreCase);
            if (!isPortalFull && main.loop)
                violations.Add($"{prefabName}: loop must be false.");
            if (isPortalFull && main.maxParticles > 16)
                violations.Add($"{prefabName}: maxParticles ({main.maxParticles}) exceeds 16.");
            if (!isPortalFull && main.maxParticles > 20)
                violations.Add($"{prefabName}: maxParticles ({main.maxParticles}) exceeds 20.");

            if (main.stopAction != ParticleSystemStopAction.Disable)
                violations.Add($"{prefabName}: stopAction must be Disable.");

            var rend = ps.GetComponent<ParticleSystemRenderer>();
            if (rend != null)
            {
                if (rend.shadowCastingMode != ShadowCastingMode.Off)
                    violations.Add($"{prefabName}: shadowCastingMode must be Off.");
                if (rend.receiveShadows)
                    violations.Add($"{prefabName}: receiveShadows must be false.");
            }
        }

        // =====================================================================
        // 5. STUDIO SCENE, PARTICLE SIMULATION & SCREENSHOT HELPERS
        // =====================================================================

        private static Scene CreateStudioScene(out Camera cam, Color bgColor, float fov)
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.76f, 0.80f, 0.86f, 1f);
            RenderSettings.ambientEquatorColor = new Color(0.58f, 0.60f, 0.64f, 1f);
            RenderSettings.ambientGroundColor = new Color(0.35f, 0.33f, 0.30f, 1f);

            var keyGo = new GameObject("Studio_KeyLight");
            var key = keyGo.AddComponent<Light>();
            key.type = LightType.Directional;
            key.intensity = 1.25f;
            key.color = new Color(1.0f, 0.97f, 0.92f);
            keyGo.transform.rotation = Quaternion.Euler(40f, 145f, 0f);

            var fillGo = new GameObject("Studio_FillLight");
            var fill = fillGo.AddComponent<Light>();
            fill.type = LightType.Directional;
            fill.intensity = 0.45f;
            fill.color = new Color(0.84f, 0.91f, 1.0f);
            fillGo.transform.rotation = Quaternion.Euler(28f, -45f, 0f);

            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = bgColor;
            cam.fieldOfView = fov;
            cam.nearClipPlane = 0.03f;
            cam.farClipPlane = 30f;

            return scene;
        }

        private static void SaveSceneWithCameraPose(Scene scene, string scenePath, Camera cam, Vector3 camPos, Vector3 lookAt)
        {
            cam.transform.position = camPos;
            cam.transform.LookAt(lookAt);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, scenePath);
        }

        private static string WarmupAndCapture(Camera cam, string fileName, Vector3 pos, Vector3 lookAt)
        {
            CaptureCameraShot(cam, "_warmup.png", pos, lookAt, 256, 256);
            string warmupPath = Path.Combine(ScreenshotDir, "_warmup.png");
            if (File.Exists(warmupPath)) File.Delete(warmupPath);

            return CaptureCameraShot(cam, fileName, pos, lookAt, ShotWidth, ShotHeight);
        }

        private static string CaptureCameraShot(
            Camera cam,
            string fileName,
            Vector3 pos,
            Vector3 lookAt,
            int width,
            int height)
        {
            cam.transform.position = pos;
            cam.transform.LookAt(lookAt);

            var rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt;
            cam.Render();

            RenderTexture prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            tex.Apply();

            cam.targetTexture = null;
            RenderTexture.active = prev;
            UnityEngine.Object.DestroyImmediate(rt);

            string fullPath = Path.Combine(ScreenshotDir, fileName).Replace("\\", "/");
            File.WriteAllBytes(fullPath, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
            return fullPath;
        }

        private static GameObject SpawnPrefabAt(string assetPath, Vector3 pos, Quaternion rot, Vector3 scale)
        {
            var src = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (src == null)
            {
                Debug.LogWarning($"[LivingWorldVfxAndGallerySetup] Missing prefab: {assetPath}");
                return null;
            }
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(src);
            inst.transform.SetPositionAndRotation(pos, rot);
            inst.transform.localScale = scale;
            return inst;
        }

        private static void SpawnSimulatedVfx(
            Dictionary<string, GameObject> vfxPrefabs,
            string vfxName,
            Vector3 worldPos,
            float simTime)
        {
            if (!vfxPrefabs.TryGetValue(vfxName, out GameObject prefab) || prefab == null) return;
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            inst.name = vfxName;
            inst.transform.SetPositionAndRotation(worldPos, Quaternion.identity);
            SimulateParticleSystem(inst.GetComponent<ParticleSystem>(), simTime);
        }

        private static void SimulateAllParticlesInHierarchy(GameObject root, float simTime)
        {
            if (root == null) return;
            foreach (ParticleSystem ps in root.GetComponentsInChildren<ParticleSystem>(true))
            {
                SimulateParticleSystem(ps, simTime);
            }
        }

        private static void SimulateParticleSystem(ParticleSystem ps, float simTime)
        {
            if (ps == null) return;
            if (!ps.gameObject.activeSelf) ps.gameObject.SetActive(true);
            ps.Simulate(simTime, withChildren: true, restart: true, fixedTimeStep: false);
        }

        private static void SampleControllerClip(GameObject root, string stateName, float timeSeconds)
        {
            if (root == null || string.IsNullOrEmpty(stateName)) return;
            Animator anim = root.GetComponentInChildren<Animator>(true);
            if (anim == null || !(anim.runtimeAnimatorController is AnimatorController ac)) return;

            foreach (ChildAnimatorState cs in ac.layers[0].stateMachine.states)
            {
                if (string.Equals(cs.state.name, stateName, StringComparison.Ordinal) && cs.state.motion is AnimationClip clip)
                {
                    clip.SampleAnimation(anim.gameObject, timeSeconds);
                    return;
                }
            }
        }

        private static void CreateStudioBox(string name, Vector3 pos, Vector3 scale, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.position = pos;
            go.transform.localScale = scale;
            if (mat != null) go.GetComponent<Renderer>().sharedMaterial = mat;
        }

        private static void AddWorldTextLabel(
            Transform parent,
            string name,
            string text,
            Vector3 localPos,
            Vector3 localEuler,
            float scale,
            Color col,
            Vector2 rectSize)
        {
            var go = new GameObject(name);
            if (parent != null) go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.Euler(0f, 180f, 0f) * Quaternion.Euler(localEuler);
            go.transform.localScale = Vector3.one * scale;

            var tmp = go.AddComponent<TextMeshPro>();
            tmp.text = text;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.fontSize = 36f;
            tmp.color = col;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;

            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(InterFontPath);
            if (font != null) tmp.font = font;

            var rt = go.GetComponent<RectTransform>();
            if (rt != null) rt.sizeDelta = rectSize;
        }

        private static Material LoadMat(string matName)
        {
            return AssetDatabase.LoadAssetAtPath<Material>($"{MatsDir}/{matName}.mat");
        }

        private static void RemoveChildrenByName(Transform root, params string[] names)
        {
            if (root == null) return;
            var toDestroy = new List<GameObject>();
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t == root) continue;
                foreach (string targetName in names)
                {
                    if (string.Equals(t.name, targetName, StringComparison.Ordinal))
                    {
                        toDestroy.Add(t.gameObject);
                        break;
                    }
                }
            }

            foreach (GameObject go in toDestroy)
            {
                if (go != null) UnityEngine.Object.DestroyImmediate(go);
            }
        }

        private static Transform FindChildRecursive(Transform root, string targetName)
        {
            if (root == null) return null;
            if (root.name == targetName) return root;
            for (int i = 0; i < root.childCount; i++)
            {
                Transform found = FindChildRecursive(root.GetChild(i), targetName);
                if (found != null) return found;
            }
            return null;
        }

        private static Type FindTypeInLoadedAssemblies(string fullTypeName)
        {
            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type t = asm.GetType(fullTypeName, throwOnError: false);
                if (t != null) return t;
            }
            return null;
        }
    }
}
#endif
