using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using TMPro;
using MuseDesk.Art;
using MuseDesk.Spatial;

/// <summary>
/// Ceiling deployment effects, integration pass 1 (Antigravity's commit cbd606b).
///
///   1. The new 4-leaf-iris CeilingMount: the prefab asset was replaced in place, so the scene's
///      Spatial/CeilingPod/Visuals/CeilingMount instance already shows it. This puts it where
///      BuildSpatialArtV1 put the old one (local 0, 2.4, 0; the pod controller moves it to the
///      ceiling), re-reads its sockets (mastTop = SuspensionBottom), re-points the pod controller and
///      CeilingPodArt at it, and checks the mount animator has the four clips CeilingPodArt asks for.
///   2. Spatial/CeilingPod/CeilingVfx: the five VFX_ mesh and particle objects taken from
///      Spatial_CeilingWorkspaceAssembly.prefab (unpacked copy, the rest discarded), a
///      CeilingDeploymentVFXController (no CeilingWorkspacePodController) and a CeilingVfxBridge that
///      follows CeilingPodMachine.StateChanged and hides everything outside Ceiling mode.
///   3. Re-measures CeilingPodController.mountOffset and mastAttachHeight the way BuildCeilingPolishV1
///      does (the pod's DockingConnector, root-local) and, as BuildSpatialArtV1 did, makes sure
///      ceilingSettings.dockDrop leaves 3 cm of mast under the (deeper) mount. Old and new values
///      are in the report. Nothing in Spatial/Core is touched; its hard-coded mount numbers are
///      compared against the measured mount in the report only.
///
/// Runs AFTER BuildVoiceV2. Does not edit or re-run BuildSpatialArtV1 / BuildCeilingPolishV1.
/// Nothing is deleted from the scene (the unused rest of the assembly copy is destroyed at once,
/// it never was in the scene). Idempotent. Saves the scene. Refuses in Play Mode and outside MainMRScene.
///   BuildCeilingVfxV1.Apply   BuildCeilingVfxV1.Revert
/// </summary>
public static class BuildCeilingVfxV1
{
    const string PrefabDir = "Assets/MuseDesk/Art/Prefabs/Spatial";
    const string HolderName = "CeilingVfx";
    const string BackupFile = "Temp/BuildCeilingVfxV1.revert.txt";
    const float MastShowingMin = 0.03f;

    static List<Transform> All() => EditorSceneManager.GetActiveScene().GetRootGameObjects()
        .SelectMany(r => r.GetComponentsInChildren<Transform>(true)).ToList();
    static Transform Find(List<Transform> all, string name) => all.FirstOrDefault(t => t != null && t.name == name);
    static Transform Under(Transform root, string name) => root == null ? null : root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == name);
    static string P(Vector3 v) => $"({v.x:F3}, {v.y:F3}, {v.z:F3})";
    static T Ensure<T>(GameObject go) where T : Component { var c = go.GetComponent<T>(); return c != null ? c : Undo.AddComponent<T>(go); }

    static readonly Vector3[] Corners =
    {
        new Vector3(-1, -1, -1), new Vector3(1, -1, -1), new Vector3(-1, 1, -1), new Vector3(1, 1, -1),
        new Vector3(-1, -1, 1), new Vector3(1, -1, 1), new Vector3(-1, 1, 1), new Vector3(1, 1, 1),
    };

    // Same measurement as BuildCeilingPolishV1.MeasureIn (skinned meshes by bind-pose bounds).
    static bool MeasureIn(Transform root, Transform space, out Vector3 mn, out Vector3 mx)
    {
        mn = Vector3.positiveInfinity; mx = Vector3.negativeInfinity;
        bool any = false;
        foreach (var r in root.GetComponentsInChildren<Renderer>(true))
        {
            if (r == null || r.GetComponent<TMP_Text>() != null) continue;
            Mesh mesh = null;
            if (r is SkinnedMeshRenderer smr) mesh = smr.sharedMesh;
            else { var f = r.GetComponent<MeshFilter>(); if (f != null) mesh = f.sharedMesh; }
            if (mesh == null) continue;
            var b = mesh.bounds;
            for (int i = 0; i < 8; i++)
            {
                var l = space.InverseTransformPoint(r.transform.TransformPoint(b.center + Vector3.Scale(b.extents, Corners[i])));
                mn = Vector3.Min(mn, l); mx = Vector3.Max(mx, l);
            }
            any = true;
        }
        return any;
    }

    static string F(float v) => v.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
    static float Pf(string s) => float.Parse(s, System.Globalization.CultureInfo.InvariantCulture);

    static bool Guard(out Transform ws, out Transform spatial, out string error)
    {
        ws = null; spatial = null; error = null;
        if (EditorApplication.isPlayingOrWillChangePlaymode) { error = "BuildCeilingVfxV1: stop Play Mode first.\n"; return false; }
        var scene = EditorSceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded) { error = "BuildCeilingVfxV1: no open scene.\n"; return false; }
        if (!scene.path.Replace('\\', '/').EndsWith("Assets/MuseDesk/Scenes/MainMRScene.unity"))
        { error = "BuildCeilingVfxV1: the open scene is '" + scene.path + "', not MainMRScene; nothing changed.\n"; return false; }
        var all = All();
        ws = Find(all, "WorkspaceRoot");
        if (ws == null) { error = "BuildCeilingVfxV1: WorkspaceRoot not found; nothing changed.\n"; return false; }
        Transform wsLocal = ws;
        spatial = all.FirstOrDefault(t => t != null && t.name == "Spatial" && !t.IsChildOf(wsLocal));
        if (spatial == null) { error = "BuildCeilingVfxV1: scene object Spatial not found; nothing changed.\n"; return false; }
        return true;
    }

    public static string Apply()
    {
        if (!Guard(out var ws, out var spatial, out var err)) return err;
        var scene = EditorSceneManager.GetActiveScene();
        var sb = new StringBuilder();
        sb.AppendLine("== BuildCeilingVfxV1 ==");

        var podT = spatial.Find("CeilingPod");
        var pod = podT != null ? podT.GetComponent<CeilingPodController>() : null;
        var podArt = podT != null ? podT.GetComponent<CeilingPodArt>() : null;
        var director = spatial.GetComponent<SpatialWorkspaceDirector>();
        if (pod == null) return "BuildCeilingVfxV1: Spatial/CeilingPod/CeilingPodController not found; nothing changed.\n";
        var visuals = podT.Find("Visuals");
        if (visuals == null) return "BuildCeilingVfxV1: Spatial/CeilingPod/Visuals not found; nothing changed.\n";

        // ── originals, kept once for Revert ──
        float oldAttach = pod.mastAttachHeight; Vector2 oldOffset = pod.mountOffset;
        float oldDrop = director != null ? director.ceilingSettings.dockDrop : float.NaN;
        if (!File.Exists(BackupFile))
        {
            Directory.CreateDirectory("Temp");
            File.WriteAllText(BackupFile, string.Join("\n", F(oldOffset.x), F(oldOffset.y), F(oldAttach), F(oldDrop)));
        }

        // ── 1. the mount ──
        sb.AppendLine("-- 1. CeilingMount (new 4-leaf iris) --");
        bool visualsWasActive = visuals.gameObject.activeSelf;
        visuals.gameObject.SetActive(true);
        Transform mount = visuals.Find("CeilingMount");
        if (mount == null)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabDir}/CeilingMount.prefab");
            if (asset == null) { visuals.gameObject.SetActive(visualsWasActive); return "BuildCeilingVfxV1: CeilingMount.prefab not found; nothing changed.\n"; }
            var go = (GameObject)PrefabUtility.InstantiatePrefab(asset, visuals);
            Undo.RegisterCreatedObjectUndo(go, "CeilingMount");
            go.name = "CeilingMount";
            mount = go.transform;
            sb.AppendLine("  instantiated CeilingMount.prefab");
        }
        else sb.AppendLine("  found Spatial/CeilingPod/Visuals/CeilingMount (a prefab instance: it shows the replaced art)");
        mount.gameObject.SetActive(true);
        mount.localPosition = new Vector3(0f, 2.4f, 0f); mount.localRotation = Quaternion.identity; mount.localScale = Vector3.one;
        foreach (var t in mount.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = visuals.gameObject.layer;
        foreach (var c in mount.GetComponentsInChildren<Collider>(true)) if (c.enabled) { c.enabled = false; EditorUtility.SetDirty(c); }
        foreach (var r in mount.GetComponentsInChildren<Renderer>(true)) { r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false; EditorUtility.SetDirty(r); }

        var bones = new[] { "Bone_Iris_F", "Bone_Iris_B", "Bone_Iris_L", "Bone_Iris_R" };
        sb.AppendLine("  iris bones: " + string.Join(", ", bones.Select(b => b + (Under(mount, b) != null ? " ok" : " MISSING"))));
        Transform bottom = Under(mount, "SuspensionBottom"), top = Under(mount, "SuspensionTop"), anchor = Under(mount, "CeilingAnchor"), docked = Under(mount, "DockedPose");
        float mountDepth = 0.118f;
        if (bottom != null)
        {
            mountDepth = Mathf.Abs(mount.InverseTransformPoint(bottom.position).y);
            pod.mastTop = bottom;
            sb.AppendLine($"  CeilingPodController.mastTop = SuspensionBottom ({mountDepth:F3} m below the mount origin / ceiling)");
        }
        else sb.AppendLine("  WARNING: SuspensionBottom not found in the new mount; mastTop left as it was: " + (pod.mastTop != null ? pod.mastTop.name : "null"));
        pod.ceilingMount = mount;
        if (podArt != null) { podArt.mountAnimator = mount.GetComponent<Animator>(); EditorUtility.SetDirty(podArt); }
        sb.AppendLine($"  SuspensionTop {(top != null ? P(mount.InverseTransformPoint(top.position)) : "MISSING")}, CeilingAnchor {(anchor != null ? P(mount.InverseTransformPoint(anchor.position)) : "MISSING")}, DockedPose {(docked != null ? P(mount.InverseTransformPoint(docked.position)) : "MISSING")} (mount-local; Antigravity reported DockedPose y = -0.388)");

        var animator = mount.GetComponent<Animator>();
        var ctrl = animator != null ? animator.runtimeAnimatorController as AnimatorController : null;
        if (ctrl != null)
        {
            var states = ctrl.layers.Length > 0 ? ctrl.layers[0].stateMachine.states.Select(s => s.state.name).ToList() : new List<string>();
            var need = new[] { CeilingWorkspacePodController.MountIdle, CeilingWorkspacePodController.MountUnlock, CeilingWorkspacePodController.MountSpool, CeilingWorkspacePodController.MountLock };
            sb.AppendLine("  mount animator '" + ctrl.name + "': " + string.Join(", ", need.Select(n => n + (states.Contains(n) ? " ok" : " MISSING"))));
        }
        else sb.AppendLine("  NOTE: mount animator controller not readable (" + (animator == null ? "no Animator" : "runtime controller only") + ")");

        float width = 0f, depthBelow = 0f; Vector3 mmn = default, mmx = default;
        if (MeasureIn(mount, mount, out mmn, out mmx))
        {
            width = Mathf.Max(mmx.x - mmn.x, mmx.z - mmn.z); depthBelow = -mmn.y;
            sb.AppendLine($"  mount bounds (mount-local, bind pose): x {mmn.x:F3}..{mmx.x:F3}, y {mmn.y:F3}..{mmx.y:F3}, z {mmn.z:F3}..{mmx.z:F3}");
        }
        var cs = director != null ? director.ceilingSettings : new CeilingPlacementSettings();
        sb.AppendLine($"  CORE COMPARISON (Core not edited): CeilingPlacementSettings.mountClearance = {cs.mountClearance:F2} m (half-extent of the blocked square around the mount point) vs mount half-width {width * 0.5f:F3} m; mountDepth = {cs.mountDepth:F2} m (band below the ceiling tested for room volumes) vs the mount reaches {depthBelow:F3} m below its origin (SuspensionBottom {mountDepth:F3}); mastRadius = {cs.mastRadius:F3} m. {(width * 0.5f > cs.mountClearance || depthBelow > cs.mountDepth ? "WARNING: the mount exceeds a Core number; the coordinator must decide whether to raise it in SpatialWorkspaceDirector.ceilingSettings." : "The mount is inside these numbers.")}");

        // ── 2. effects holder ──
        sb.AppendLine("-- 2. deployment effects (Spatial/CeilingPod/" + HolderName + ") --");
        Transform vfxT = podT.Find(HolderName);
        if (vfxT == null)
        {
            var go = new GameObject(HolderName);
            Undo.RegisterCreatedObjectUndo(go, HolderName);
            go.transform.SetParent(podT, false);
            go.layer = podT.gameObject.layer;
            vfxT = go.transform;
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabDir}/Spatial_CeilingWorkspaceAssembly.prefab");
            if (asset == null) sb.AppendLine("  WARNING: Spatial_CeilingWorkspaceAssembly.prefab not found; no effect objects");
            else
            {
                var tmp = (GameObject)PrefabUtility.InstantiatePrefab(asset);
                PrefabUtility.UnpackPrefabInstance(tmp, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                var take = tmp.GetComponentsInChildren<Transform>(true).Where(t => t != tmp.transform && t.name.StartsWith("VFX_")).ToList();
                foreach (var t in take) t.SetParent(vfxT, false);
                sb.AppendLine("  copied " + take.Count + " effect object(s) from the assembly prefab: " + string.Join(", ", take.Select(t => t.name)));
                Object.DestroyImmediate(tmp);          // the rest of the old assembly (pod, tether rod, its controller) is not used
            }
        }
        else sb.AppendLine("  found existing " + HolderName);
        vfxT.gameObject.SetActive(true);
        vfxT.localPosition = Vector3.zero; vfxT.localRotation = Quaternion.identity; vfxT.localScale = Vector3.one;
        if (Mathf.Abs(vfxT.lossyScale.x - 1f) > 1e-3f) sb.AppendLine($"  WARNING: effects holder world scale is {vfxT.lossyScale.x:F3}; the cone length is set in local scale and would be wrong");

        foreach (var t in vfxT.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = vfxT.gameObject.layer;
        foreach (var r in vfxT.GetComponentsInChildren<Renderer>(true)) { r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false; EditorUtility.SetDirty(r); }
        foreach (var ps in vfxT.GetComponentsInChildren<ParticleSystem>(true))
        {
            var so = new SerializedObject(ps);
            var p = so.FindProperty("playOnAwake");
            if (p != null) { p.boolValue = false; so.ApplyModifiedPropertiesWithoutUndo(); }
            else { var main = ps.main; main.playOnAwake = false; }
            EditorUtility.SetDirty(ps);
        }
        foreach (var r in vfxT.GetComponentsInChildren<Renderer>(true)) if (!(r is ParticleSystemRenderer)) r.gameObject.SetActive(false);   // the controller switches them on

        var podHolder = ws.Find("SpatialArt_CeilingPod");
        var podInst = podHolder != null ? podHolder.Find("SuspendedOfficePod") : null;
        var connector = podInst != null ? podInst.Find("DockingConnector") : null;

        var vfx = Ensure<CeilingDeploymentVFXController>(vfxT.gameObject);
        vfx.ResolveReferences();                      // the VFX_ children by name
        var vso = new SerializedObject(vfx);
        void Set(string field, Transform value)
        {
            var p = vso.FindProperty(field);
            if (p != null && value != null) p.objectReferenceValue = value;
            else if (p == null) sb.AppendLine("  WARNING: controller field '" + field + "' not found");
        }
        Set("ceilingAnchor", anchor); Set("suspensionTop", top); Set("dockingConnector", connector); Set("podRoot", podInst);
        vso.FindProperty("podController").objectReferenceValue = null;    // never the art-side pod controller
        vso.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(vfx);
        sb.AppendLine($"  controller refs: ceilingAnchor={(anchor != null)}, suspensionTop={(top != null)}, dockingConnector={(connector != null)}, podRoot={(podInst != null)}, podController=null");

        var bridge = Ensure<CeilingVfxBridge>(vfxT.gameObject);
        bridge.pod = pod; bridge.vfx = vfx;
        EditorUtility.SetDirty(bridge);
        sb.AppendLine($"  CeilingVfxBridge: pod = CeilingPodController, vfx = controller; vfxEnabled = {bridge.vfxEnabled}, particlesEnabled = {bridge.particlesEnabled}. Binds to the pod machine's StateChanged in Update; hidden outside Ceiling mode.");

        // ── 3. re-measure the mount and mast numbers ──
        sb.AppendLine("-- 3. mount / mast numbers (measured like BuildCeilingPolishV1) --");
        if (podHolder != null && podInst != null && connector != null)
        {
            bool hActive = podHolder.gameObject.activeSelf; Vector3 hScale = podHolder.localScale; bool iActive = podInst.gameObject.activeSelf;
            podHolder.gameObject.SetActive(true); podHolder.localScale = Vector3.one; podInst.gameObject.SetActive(true);
            Vector3 conn = ws.InverseTransformPoint(connector.position);
            podInst.gameObject.SetActive(iActive); podHolder.localScale = hScale; podHolder.gameObject.SetActive(hActive);

            pod.mountOffset = new Vector2(conn.x, conn.z);
            pod.mastAttachHeight = conn.y;
            if (pod.mastBottom == null) pod.mastBottom = connector;
            sb.AppendLine($"  mountOffset ({oldOffset.x:F3}, {oldOffset.y:F3}) -> ({conn.x:F3}, {conn.z:F3}); mastAttachHeight {oldAttach:F3} -> {conn.y:F3}  (the connector is pod geometry, so a change here only comes from the pod fit; the new mount does not move it)");

            if (director != null)
            {
                float drop = director.ceilingSettings.dockDrop;
                float gap = drop - conn.y - mountDepth;
                if (gap < MastShowingMin)
                {
                    float wanted = conn.y + mountDepth + MastShowingMin;
                    director.ceilingSettings.dockDrop = wanted;
                    EditorUtility.SetDirty(director);
                    sb.AppendLine($"  ceilingSettings.dockDrop {drop:F3} -> {wanted:F3} m (connector {conn.y:F3} m above the root + mount {mountDepth:F3} m + {MastShowingMin * 100f:F0} cm of mast); NOTE: this is scene data fed to the Core placement checks; run the ceiling safety tests and re-validate placement");
                }
                else sb.AppendLine($"  dockDrop {drop:F3} m unchanged: connector {conn.y:F3} + mount {mountDepth:F3} leaves {gap * 100f:F1} cm of mast when docked");
            }
            else sb.AppendLine("  WARNING: SpatialWorkspaceDirector not found; dockDrop not checked");
        }
        else sb.AppendLine("  SKIP: SpatialArt_CeilingPod/SuspendedOfficePod/DockingConnector not found; mount offset and attach height left at " + oldOffset + " / " + oldAttach);
        EditorUtility.SetDirty(pod);

        visuals.gameObject.SetActive(visualsWasActive);
        EditorSceneManager.MarkSceneDirty(scene);
        sb.AppendLine("scene saved=" + EditorSceneManager.SaveScene(scene));
        sb.AppendLine("Next: CeilingVfxDrive.Report in Play Mode; re-run the ceiling safety tests (dockDrop / mount numbers above).");
        return sb.ToString();
    }

    /// <summary>Switches the effects off (holder inactive) and puts back the mount/mast numbers and dockDrop
    /// recorded before the first Apply. The new mount art stays (it is the prefab asset itself).</summary>
    public static string Revert()
    {
        if (!Guard(out var ws, out var spatial, out var err)) return err;
        var scene = EditorSceneManager.GetActiveScene();
        var podT = spatial.Find("CeilingPod");
        var pod = podT != null ? podT.GetComponent<CeilingPodController>() : null;
        var director = spatial.GetComponent<SpatialWorkspaceDirector>();
        var sb = new StringBuilder("== BuildCeilingVfxV1.Revert ==\n");
        var holder = podT != null ? podT.Find(HolderName) : null;
        if (holder != null) { holder.gameObject.SetActive(false); sb.AppendLine("  effects holder switched off (kept)"); }
        if (File.Exists(BackupFile) && pod != null)
        {
            var l = File.ReadAllLines(BackupFile);
            if (l.Length >= 4)
            {
                pod.mountOffset = new Vector2(Pf(l[0]), Pf(l[1])); pod.mastAttachHeight = Pf(l[2]);
                EditorUtility.SetDirty(pod);
                float d = Pf(l[3]);
                if (director != null && !float.IsNaN(d)) { director.ceilingSettings.dockDrop = d; EditorUtility.SetDirty(director); }
                sb.AppendLine($"  mountOffset ({l[0]}, {l[1]}), mastAttachHeight {l[2]}, dockDrop {l[3]} restored");
            }
        }
        else sb.AppendLine("  no recorded numbers; left as they are");
        EditorSceneManager.MarkSceneDirty(scene);
        sb.AppendLine("scene saved=" + EditorSceneManager.SaveScene(scene));
        return sb.ToString();
    }
}
