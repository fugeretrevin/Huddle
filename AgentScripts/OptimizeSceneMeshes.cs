using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using MuseDesk.Art;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

public static class OptimizeSceneMeshes
{
    static Dictionary<string, Material> LoadMaterialsByName()
    {
        var map = new Dictionary<string, Material>();
        foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { "Assets/MuseDesk/Art/Materials" }))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat != null && !map.ContainsKey(mat.name))
                map[mat.name] = mat;
        }
        return map;
    }

    static void ApplyMaterials(GameObject root, Dictionary<string, Material> matMap)
    {
        foreach (var r in root.GetComponentsInChildren<Renderer>(true))
        {
            var mats = r.sharedMaterials;
            bool changed = false;
            for (int i = 0; i < mats.Length; i++)
            {
                if (mats[i] == null) continue;
                string key = mats[i].name;
                int paren = key.IndexOf(" (Instance)");
                if (paren > 0) key = key.Substring(0, paren);
                if (matMap.TryGetValue(key, out var replacement) && replacement != mats[i])
                {
                    mats[i] = replacement;
                    changed = true;
                }
            }
            if (changed) r.sharedMaterials = mats;
        }
    }

    static bool IsPrefabInstanceOf(GameObject go, GameObject expectedPrefab)
    {
        if (go == null || expectedPrefab == null) return false;
        return PrefabUtility.GetCorrespondingObjectFromSource(go) == expectedPrefab;
    }

    public static string Run()
    {
        var sb = new StringBuilder();
        var scene = EditorSceneManager.GetActiveScene();
        var wsRoot = GameObject.Find("WorkspaceRoot")?.transform;
        var app = GameObject.Find("AppServices");
        if (wsRoot == null) throw new System.Exception("WorkspaceRoot not found in active scene.");

        var matMap = LoadMaterialsByName();
        int totalBefore = 0;
        int totalAfter = 0;

        // 1. Swap 3 Workstations
        var stations = new (string station, string defaultRole, string fbxPath)[]
        {
            ("ResearchStation", "researcher", "Assets/MuseDesk/Art/Models/Optimized/Workstation_Research_Optimized.fbx"),
            ("DevStation",      "developer",  "Assets/MuseDesk/Art/Models/Optimized/Workstation_Developer_Optimized.fbx"),
            ("QAStation",       "tester",     "Assets/MuseDesk/Art/Models/Optimized/Workstation_Testing_Optimized.fbx"),
        };

        foreach (var (stationName, defaultRole, fbxPath) in stations)
        {
            var st = wsRoot.Find(stationName);
            var oldWs = st != null ? st.Find("Workstation") : null;
            var optFbx = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
            if (st == null || oldWs == null || optFbx == null)
            {
                sb.AppendLine($"SKIP {stationName}: missing station/workstation/fbx");
                continue;
            }

            if (IsPrefabInstanceOf(oldWs.gameObject, optFbx))
            {
                var existingVis = oldWs.GetComponent<WorkstationVisualController>();
                if (existingVis != null) existingVis.ResolveAnchorsAndParts();
                int rCount = oldWs.GetComponentsInChildren<Renderer>(true).Length;
                sb.AppendLine($"{stationName}/Workstation: already optimized ({rCount} renderers)");
                continue;
            }

            int beforeR = oldWs.GetComponentsInChildren<Renderer>(true).Length;
            totalBefore += beforeR;

            var pos = oldWs.localPosition;
            var rot = oldWs.localRotation;
            var scl = oldWs.localScale;
            var oldVis = oldWs.GetComponent<WorkstationVisualController>();
            string role = oldVis != null && !string.IsNullOrEmpty(oldVis.StationRole) ? oldVis.StationRole : defaultRole;

            // Preserve runtime / collider children before deactivating legacy visual tree
            var keep = new List<Transform>();
            foreach (Transform ch in oldWs)
            {
                if (ch.name == "FurnitureCollider" || ch.name == "Monitor" || ch.GetComponent<TMP_Text>() != null)
                    keep.Add(ch);
            }
            foreach (var k in keep) k.SetParent(st, true);

            // Remove old backup if any, keep current legacy tree inactive and without WorkstationVisualController
            var prevBackup = st.Find("_Legacy_Workstation_Backup");
            if (prevBackup != null) Object.DestroyImmediate(prevBackup.gameObject);
            if (oldVis != null) Object.DestroyImmediate(oldVis);
            oldWs.name = "_Legacy_Workstation_Backup";
            oldWs.gameObject.SetActive(false);

            var newWs = (GameObject)PrefabUtility.InstantiatePrefab(optFbx, st);
            newWs.name = "Workstation";
            newWs.transform.localPosition = pos;
            newWs.transform.localRotation = rot;
            newWs.transform.localScale = scl;
            ApplyMaterials(newWs, matMap);

            foreach (var k in keep) k.SetParent(newWs.transform, true);

            var vis = newWs.GetComponent<WorkstationVisualController>() ?? newWs.AddComponent<WorkstationVisualController>();
            var so = new SerializedObject(vis);
            var roleProp = so.FindProperty("stationRole");
            if (roleProp != null) roleProp.stringValue = role;
            so.ApplyModifiedPropertiesWithoutUndo();
            vis.ResolveAnchorsAndParts();
            EditorUtility.SetDirty(vis);

            if (stationName == "QAStation" && app != null)
            {
                var tsc = app.GetComponent<TestingStationController>();
                if (tsc != null)
                {
                    tsc.testingStation = vis;
                    EditorUtility.SetDirty(tsc);
                }
            }

            int afterR = newWs.GetComponentsInChildren<Renderer>(true).Length;
            totalAfter += afterR;
            sb.AppendLine($"{stationName}/Workstation: {Path.GetFileName(fbxPath)} ({beforeR} -> {afterR} renderers)");
        }

        // 2. Swap TaskBoard/KanbanFrame_Visual
        var tb = wsRoot.Find("TaskBoard");
        var oldFrame = tb != null ? tb.Find("KanbanFrame_Visual") : null;
        var optBoardFbx = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/MuseDesk/Art/Models/Optimized/TaskBoard_KanbanFrame_Optimized.fbx");
        if (tb != null && oldFrame != null && optBoardFbx != null)
        {
            if (IsPrefabInstanceOf(oldFrame.gameObject, optBoardFbx))
            {
                int rCount = oldFrame.GetComponentsInChildren<Renderer>(true).Length;
                sb.AppendLine($"TaskBoard/KanbanFrame_Visual: already optimized ({rCount} renderers)");
            }
            else
            {
                int beforeR = oldFrame.GetComponentsInChildren<Renderer>(true).Length;
                totalBefore += beforeR;
                var pos = oldFrame.localPosition;
                var rot = oldFrame.localRotation;
                var scl = oldFrame.localScale;

                var prevBackup = tb.Find("_Legacy_KanbanFrame_Backup");
                if (prevBackup != null) Object.DestroyImmediate(prevBackup.gameObject);
                oldFrame.name = "_Legacy_KanbanFrame_Backup";
                oldFrame.gameObject.SetActive(false);

                var newFrame = (GameObject)PrefabUtility.InstantiatePrefab(optBoardFbx, tb);
                newFrame.name = "KanbanFrame_Visual";
                newFrame.transform.localPosition = pos;
                newFrame.transform.localRotation = rot;
                newFrame.transform.localScale = scl;
                ApplyMaterials(newFrame, matMap);

                int afterR = newFrame.GetComponentsInChildren<Renderer>(true).Length;
                totalAfter += afterR;
                sb.AppendLine($"TaskBoard/KanbanFrame_Visual: TaskBoard_KanbanFrame_Optimized.fbx ({beforeR} -> {afterR} renderers)");
            }
        }

        // 3. Upgrade 5-cube Housing3D on Poke buttons to single-mesh UI_PokeKey_Housing.fbx
        var housingFbx = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/MuseDesk/Art/Models/UI/UI_PokeKey_Housing.fbx");
        var housingMesh = housingFbx != null ? housingFbx.GetComponentInChildren<MeshFilter>(true)?.sharedMesh : null;
        var housingMat = matMap.TryGetValue("Mat_CharcoalVisor", out var hm) ? hm : null;
        int keysUpgraded = 0;
        if (housingMesh != null && housingMat != null)
        {
            var allTransforms = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true)).ToList();
            foreach (var btn in allTransforms)
            {
                if (btn == null) continue;
                var oldHousing = btn.Find("Housing3D");
                if (oldHousing == null) continue;
                var existingMf = oldHousing.GetComponent<MeshFilter>();
                if (existingMf != null && existingMf.sharedMesh == housingMesh && oldHousing.childCount == 0)
                    continue;

                int beforeH = oldHousing.GetComponentsInChildren<Renderer>(true).Length;
                totalBefore += beforeH;
                Object.DestroyImmediate(oldHousing.gameObject);

                var hGo = new GameObject("Housing3D");
                hGo.transform.SetParent(btn, false);
                hGo.transform.localPosition = -housingMesh.bounds.center + new Vector3(0f, 0f, -0.013f);
                hGo.transform.localRotation = Quaternion.identity;
                hGo.transform.localScale = Vector3.one;
                hGo.AddComponent<MeshFilter>().sharedMesh = housingMesh;
                var hr = hGo.AddComponent<MeshRenderer>();
                hr.sharedMaterial = housingMat;
                hr.shadowCastingMode = ShadowCastingMode.Off;
                hr.receiveShadows = false;
                totalAfter += 1;
                keysUpgraded++;
            }
            sb.AppendLine($"PokeKey Housing3D: upgraded {keysUpgraded} housings to UI_PokeKey_Housing (5 -> 1 renderer each)");
        }

        EditorSceneManager.MarkSceneDirty(scene);
        bool saved = EditorSceneManager.SaveScene(scene);
        sb.AppendLine($"Summary: swapped renderers {totalBefore} -> {totalAfter} (saved={saved})");
        return sb.ToString();
    }
}

