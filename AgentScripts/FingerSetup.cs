using System;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Wire dynamic finger collision: a dedicated 'FingerCollide' layer, a trigger proxy collider on each
/// grabbable (so finger casts hit grabbables only, never the desk/agents/own hand), the
/// DynamicFingerCollision component on the rig, and a late script execution order so it wins over
/// ISDK's own joint writes.
/// </summary>
public static class FingerSetup
{
    const string LayerName = "FingerCollide";

    public static string Run()
    {
        var sb = new StringBuilder();
        int layer = EnsureLayer(LayerName, sb);
        if (layer < 0) return sb.ToString();

        var scene = EditorSceneManager.GetActiveScene();
        var all = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true))
            .Select(t => t.gameObject).ToList();
        GameObject Find(string n) => all.FirstOrDefault(g => g.name == n);

        // Trigger proxies on each grabbable, copying its primary collider shape.
        foreach (var name in new[] { "[BuildingBlock] Grabbable Item", "Prop_Block", "Prop_Mug", "DecisionDocument" })
        {
            var go = Find(name);
            if (go == null) { sb.AppendLine($"proxy: {name} not found"); continue; }
            var old = go.transform.Find("FingerProxy");
            if (old != null) UnityEngine.Object.DestroyImmediate(old.gameObject);
            var proxy = new GameObject("FingerProxy");
            proxy.transform.SetParent(go.transform, false);
            proxy.layer = layer;

            // Copy the shape of the object's first collider.
            var src = go.GetComponents<Collider>().FirstOrDefault();
            if (src is CapsuleCollider cap)
            {
                var c = proxy.AddComponent<CapsuleCollider>();
                c.isTrigger = true; c.radius = cap.radius; c.height = cap.height; c.direction = cap.direction; c.center = cap.center;
            }
            else if (src is BoxCollider box)
            {
                var c = proxy.AddComponent<BoxCollider>();
                c.isTrigger = true; c.size = box.size; c.center = box.center;
            }
            else
            {
                var c = proxy.AddComponent<BoxCollider>();
                c.isTrigger = true; c.size = Vector3.one;
            }
            sb.AppendLine($"proxy added on {name} (layer {LayerName})");
        }

        // Component on the Camera Rig (always-present), masked to the FingerCollide layer.
        var rig = Find("[BuildingBlock] Camera Rig") ?? all.FirstOrDefault(g => g.GetComponent<Camera>() != null);
        if (rig == null) { sb.AppendLine("camera rig not found"); }
        else
        {
            var dfcType = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(a => { try { return a.GetTypes(); } catch { return Type.EmptyTypes; } })
                .FirstOrDefault(t => t.Name == "DynamicFingerCollision");
            if (dfcType == null) sb.AppendLine("DynamicFingerCollision type not found (compile first)");
            else
            {
                var comp = rig.GetComponent(dfcType) ?? rig.AddComponent(dfcType);
                var maskField = dfcType.GetField("grabbableMask", BindingFlags.Instance | BindingFlags.Public);
                if (maskField != null) maskField.SetValue(comp, (LayerMask)(1 << layer));
                EditorUtility.SetDirty(comp);
                sb.AppendLine("DynamicFingerCollision added to Camera Rig, mask=FingerCollide");
                // Execution order is set via [DefaultExecutionOrder(2000)] on the class (compile-time) —
                // NOT MonoImporter.SetExecutionOrder here, which recompiles mid-script-run and deadlocks.
            }
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        sb.AppendLine("SAVED.");
        return sb.ToString();
    }

    static int EnsureLayer(string name, StringBuilder sb)
    {
        var tagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
        var layers = tagManager.FindProperty("layers");
        for (int i = 8; i < layers.arraySize; i++)
            if (layers.GetArrayElementAtIndex(i).stringValue == name) { sb.AppendLine($"layer {name} exists at {i}"); return i; }
        for (int i = 8; i < layers.arraySize; i++)
        {
            var el = layers.GetArrayElementAtIndex(i);
            if (string.IsNullOrEmpty(el.stringValue))
            {
                el.stringValue = name;
                tagManager.ApplyModifiedProperties();
                sb.AppendLine($"created layer {name} at {i}");
                return i;
            }
        }
        sb.AppendLine("no free user layer");
        return -1;
    }
}
