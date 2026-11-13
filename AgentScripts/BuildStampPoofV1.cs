using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEditor;
using UnityEditor.SceneManagement;
using MuseDesk.Documents;
using MuseDesk.Networking;

/// <summary>
/// The stamps leave after a confirmed decision and come back for the next one (StampPoof).
/// Adds a StampPoof to StampApprove and StampReject, wires it, and builds each stamp's small
/// effect: a "PoofFx" child with one burst of 8 soft unlit puffs and a "Ring" child with a single
/// flat expanding ring. Two tiny generated textures and two unlit particle materials are created
/// under Assets/MuseDesk/Art/Materials/UI (no soft particles, no lighting, no shadows).
/// Idempotent: the effect child is rebuilt, the component and assets are reused. Saves the scene.
/// Does not touch NearUserKit, the rest pad, the stamps' grab components or their poses.
/// </summary>
public static class BuildStampPoofV1
{
    const string ScenePath = "Assets/MuseDesk/Scenes/MainMRScene.unity";
    const string Dir = "Assets/MuseDesk/Art/Materials/UI";
    const string PuffTexPath = Dir + "/StampPoof_Puff.png";
    const string RingTexPath = Dir + "/StampPoof_Ring.png";
    const string PuffMatPath = Dir + "/StampPoof_Puff.mat";
    const string RingMatPath = Dir + "/StampPoof_Ring.mat";

    // Design system tokens (OfficeTokens): Paper, Success, Coral.
    static readonly Color Paper = new Color32(0xF7, 0xF2, 0xE3, 0xFF);
    static readonly Color Success = new Color32(0x2E, 0x9B, 0x5F, 0xFF);
    static readonly Color Coral = new Color32(0xD9, 0x53, 0x4A, 0xFF);

    static Transform Find(string n) => EditorSceneManager.GetActiveScene().GetRootGameObjects()
        .SelectMany(r => r.GetComponentsInChildren<Transform>(true)).FirstOrDefault(t => t.name == n);

    public static string Apply()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.path != ScenePath) return $"BuildStampPoofV1: open {ScenePath} first (active scene is '{scene.path}')";
        if (Application.isPlaying) return "BuildStampPoofV1: not in Play Mode";

        var sb = new StringBuilder("BuildStampPoofV1: ");
        if (!AssetDatabase.IsValidFolder(Dir)) return sb.Append("folder missing: " + Dir).ToString();

        var puffTex = SoftTexture(PuffTexPath, 32, false);
        var ringTex = SoftTexture(RingTexPath, 64, true);
        var puffMat = ParticleMat(PuffMatPath, puffTex);
        var ringMat = ParticleMat(RingMatPath, ringTex);
        if (puffMat == null || ringMat == null) return sb.Append("shader 'Universal Render Pipeline/Particles/Unlit' not found; nothing changed").ToString();

        var controller = Object.FindAnyObjectByType<SessionController>(FindObjectsInactive.Include);
        int done = 0;
        foreach (var (name, token) in new[] { ("StampApprove", Success), ("StampReject", Coral) })
        {
            var s = Find(name);
            if (s == null) { sb.Append($"[{name} MISSING] "); continue; }
            var tool = s.GetComponent<StampTool>();
            if (tool == null) { sb.Append($"[{name} has no StampTool] "); continue; }

            // Effect child: rebuilt each run.
            var old = s.Find("PoofFx");
            if (old != null) Object.DestroyImmediate(old.gameObject);
            var puff = BuildPuff(s, puffMat, token);
            var ring = BuildRing(puff.transform, ringMat, token);

            var poof = s.GetComponent<StampPoof>();
            if (poof == null) poof = s.gameObject.AddComponent<StampPoof>();
            poof.controller = tool.controller != null ? tool.controller : controller;
            poof.tool = tool;
            poof.body = s.GetComponent<Rigidbody>();
            poof.puff = puff;
            poof.ring = ring;
            poof.pivotLocal = Vector3.zero;
            // Everything the user sees of the stamp, except the ink contact point and the effect.
            poof.visuals = Enumerable.Range(0, s.childCount).Select(s.GetChild)
                .Where(c => c.name != "InkHead" && c.name != "PoofFx").ToArray();
            EditorUtility.SetDirty(poof);
            EditorUtility.SetDirty(s.gameObject);

            var rb = poof.body;
            var box = s.GetComponent<BoxCollider>();
            sb.Append($"[{name}: StampPoof wired, controller={(poof.controller != null)}, visuals={string.Join("+", poof.visuals.Select(v => v.name))}, "
                    + $"body={(rb != null ? (rb.isKinematic ? "kinematic" : "DYNAMIC (made kinematic while gone)") : "none")}, "
                    + $"grab box={(box != null ? box.size.ToString("F3") : "NONE on root: check grab volume")}, fx=8 puffs + 1 ring] ");
            done++;
        }

        EditorSceneManager.MarkSceneDirty(scene);
        bool saved = EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        sb.Append($"stamps={done}/2 saved={saved}");
        return sb.ToString();
    }

    // ── effect ──

    static ParticleSystem BuildPuff(Transform stamp, Material mat, Color token)
    {
        var go = new GameObject("PoofFx");
        go.layer = stamp.gameObject.layer;
        go.transform.SetParent(stamp, false);
        go.transform.localPosition = new Vector3(0f, 0.03f, 0f);     // about the middle of the stamp
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.playOnAwake = false;
        main.loop = false;
        main.duration = 0.5f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.32f, 0.48f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.06f, 0.14f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.012f, 0.022f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startColor = new ParticleSystem.MinMaxGradient(Paper, Color.Lerp(token, Paper, 0.55f));
        main.gravityModifier = new ParticleSystem.MinMaxCurve(-0.03f);     // drifts up a little
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;
        main.maxParticles = 12;
        main.stopAction = ParticleSystemStopAction.None;

        var em = ps.emission;
        em.enabled = true;
        em.rateOverTime = new ParticleSystem.MinMaxCurve(0f);
        em.rateOverDistance = new ParticleSystem.MinMaxCurve(0f);
        em.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)8) });

        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Hemisphere;
        shape.radius = 0.012f;
        shape.rotation = new Vector3(-90f, 0f, 0f);                        // dome up

        var col = ps.colorOverLifetime;
        col.enabled = true;
        col.color = new ParticleSystem.MinMaxGradient(Fade(0.85f, 0.55f));

        var size = ps.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.55f), new Keyframe(0.35f, 1f), new Keyframe(1f, 1.35f)));

        SetupRenderer(go, mat, ParticleSystemRenderMode.Billboard);
        return ps;
    }

    static ParticleSystem BuildRing(Transform parent, Material mat, Color token)
    {
        var go = new GameObject("Ring");
        go.layer = parent.gameObject.layer;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = new Vector3(0f, -0.026f, 0f);    // just above the stamp's base
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.playOnAwake = false;
        main.loop = false;
        main.duration = 0.4f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.36f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.036f);
        main.startColor = new ParticleSystem.MinMaxGradient(token);
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;
        main.maxParticles = 2;
        main.stopAction = ParticleSystemStopAction.None;

        var em = ps.emission;
        em.enabled = true;
        em.rateOverTime = new ParticleSystem.MinMaxCurve(0f);
        em.rateOverDistance = new ParticleSystem.MinMaxCurve(0f);
        em.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)1) });

        var shape = ps.shape;
        shape.enabled = false;

        var col = ps.colorOverLifetime;
        col.enabled = true;
        col.color = new ParticleSystem.MinMaxGradient(Fade(0.75f, 0.45f));

        var size = ps.sizeOverLifetime;
        size.enabled = true;                                           // 3.6 cm to about 11 cm, easing out
        size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.5f, 2.4f), new Keyframe(1f, 3.1f)));

        SetupRenderer(go, mat, ParticleSystemRenderMode.HorizontalBillboard);
        return ps;
    }

    // White throughout; alpha holds, then fades to nothing.
    static Gradient Fade(float startAlpha, float midAlpha)
    {
        var g = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(startAlpha, 0f), new GradientAlphaKey(midAlpha, 0.45f), new GradientAlphaKey(0f, 1f) });
        return g;
    }

    static void SetupRenderer(GameObject go, Material mat, ParticleSystemRenderMode mode)
    {
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.renderMode = mode;
        r.sharedMaterial = mat;
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.receiveShadows = false;
        r.lightProbeUsage = LightProbeUsage.Off;
        r.reflectionProbeUsage = ReflectionProbeUsage.Off;
        r.alignment = ParticleSystemRenderSpace.View;
        r.sortMode = ParticleSystemSortMode.None;
        r.minParticleSize = 0f;
        r.maxParticleSize = 1f;
    }

    // ── assets ──

    /// <summary>A white texture whose alpha is a soft round dot, or a soft ring.</summary>
    static Texture2D SoftTexture(string path, int size, bool ringShape)
    {
        if (!File.Exists(path))
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var px = new Color32[size * size];
            float half = (size - 1) * 0.5f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float r = Mathf.Sqrt((x - half) * (x - half) + (y - half) * (y - half)) / half;   // 0 centre .. 1 edge
                    float a;
                    if (ringShape)
                    {
                        // A band at 0.8 of the radius, 0.07 thick each side, soft on both edges.
                        float d = Mathf.Abs(r - 0.80f) / 0.14f;
                        a = Mathf.Clamp01(1f - d);
                        a = a * a * (3f - 2f * a);
                    }
                    else
                    {
                        a = Mathf.Clamp01((1f - r) / 0.65f);
                        a = a * a * (3f - 2f * a);
                    }
                    px[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
                }
            tex.SetPixels32(px);
            tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        }
        var imp = AssetImporter.GetAtPath(path) as TextureImporter;
        if (imp != null && (!imp.alphaIsTransparency || imp.mipmapEnabled || imp.wrapMode != TextureWrapMode.Clamp))
        {
            imp.textureType = TextureImporterType.Default;
            imp.alphaIsTransparency = true;
            imp.mipmapEnabled = false;
            imp.wrapMode = TextureWrapMode.Clamp;
            imp.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    /// <summary>Unlit, alpha-blended particle material: no soft particles, no depth write, no fading.</summary>
    static Material ParticleMat(string path, Texture2D tex)
    {
        var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (shader == null) return null;
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            m = new Material(shader);
            AssetDatabase.CreateAsset(m, path);
        }
        m.shader = shader;
        if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", Color.white);
        Set(m, "_Surface", 1f);                 // transparent
        Set(m, "_Blend", 0f);                   // alpha
        Set(m, "_ColorMode", 0f);               // multiply by the particle colour
        Set(m, "_Cull", (float)CullMode.Off);
        Set(m, "_AlphaClip", 0f);
        Set(m, "_ZWrite", 0f);
        Set(m, "_SrcBlend", (float)BlendMode.SrcAlpha);
        Set(m, "_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        // Keep the framebuffer's alpha (passthrough compositing) from being thinned by the puff.
        Set(m, "_SrcBlendAlpha", (float)BlendMode.One);
        Set(m, "_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
        Set(m, "_SoftParticlesEnabled", 0f);
        Set(m, "_CameraFadingEnabled", 0f);
        Set(m, "_DistortionEnabled", 0f);
        Set(m, "_FlipbookBlending", 0f);
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        m.DisableKeyword("_ALPHAMODULATE_ON");
        m.DisableKeyword("_ALPHATEST_ON");
        m.DisableKeyword("_SOFTPARTICLES_ON");
        m.DisableKeyword("_FADING_ON");
        m.DisableKeyword("_DISTORTION_ON");
        m.DisableKeyword("_FLIPBOOKBLENDING_ON");
        m.SetOverrideTag("RenderType", "Transparent");
        m.renderQueue = (int)RenderQueue.Transparent;
        m.SetShaderPassEnabled("ShadowCaster", false);
        m.enableInstancing = false;
        EditorUtility.SetDirty(m);
        return m;
    }

    static void Set(Material m, string prop, float value) { if (m.HasProperty(prop)) m.SetFloat(prop, value); }
}
