using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>Read-only: which hand interactors the rig has, how the grabbables are set up, and how
/// many meshes/materials the big static models have (current vs the "Optimized" variants).</summary>
public static class RigProbe
{
    static string Path(Transform t, int depth = 4)
    {
        var parts = new List<string>();
        while (t != null && parts.Count < depth) { parts.Add(t.name); t = t.parent; }
        parts.Reverse();
        return string.Join("/", parts);
    }

    public static string Run()
    {
        var all = EditorSceneManager.GetActiveScene().GetRootGameObjects()
            .SelectMany(r => r.GetComponentsInChildren<Transform>(true)).ToList();
        var sb = new StringBuilder();

        sb.AppendLine("--- interactors / hand visuals on the rig ---");
        foreach (var t in all)
            foreach (var mb in t.GetComponents<MonoBehaviour>())
            {
                if (mb == null) continue;
                string n = mb.GetType().Name;
                if (n.EndsWith("Interactor") || n == "HandVisual" || n.Contains("PokeLimiter") || n.Contains("SyntheticHand") || n == "HandPhysicsCapsules" || n.Contains("InteractorGroup"))
                    sb.AppendLine($"{n} enabled={mb.enabled} active={t.gameObject.activeInHierarchy} @ {Path(t, 5)}");
            }

        sb.AppendLine("--- interactables ---");
        foreach (var t in all)
            foreach (var mb in t.GetComponents<MonoBehaviour>())
            {
                if (mb == null) continue;
                string n = mb.GetType().Name;
                if (!n.EndsWith("Interactable") || n == "PokeInteractable") continue;
                var so = new SerializedObject(mb);
                string f(string p) { var x = so.FindProperty(p); return x == null ? "?" : x.propertyType == SerializedPropertyType.ObjectReference ? (x.objectReferenceValue != null ? x.objectReferenceValue.name : "null") : x.propertyType == SerializedPropertyType.Enum || x.propertyType == SerializedPropertyType.Integer ? x.intValue.ToString() : "?"; }
                sb.AppendLine($"{n} @ {Path(t, 3)} pointable={f("_pointableElement")} rigidbody={f("_rigidbody")} grabTypes={f("_supportedGrabTypes")}");
            }

        sb.AppendLine("--- big static model instances in the scene ---");
        foreach (var name in new[] { "Workstation", "KanbanFrame_Visual", "SoftwareFactory", "OfficeProps_Collection" })
            foreach (var t in all.Where(x => x.name == name))
            {
                var rs = t.GetComponentsInChildren<MeshRenderer>(true);
                int mats = rs.SelectMany(r => r.sharedMaterials).Where(m => m != null).Distinct().Count();
                int tris = rs.Select(r => r.GetComponent<MeshFilter>()).Where(f => f != null && f.sharedMesh != null).Sum(f => f.sharedMesh.triangles.Length / 3);
                var src = PrefabUtility.GetCorrespondingObjectFromSource(t.gameObject);
                sb.AppendLine($"{Path(t, 2)}: renderers={rs.Length} materials={mats} tris={tris} animators={t.GetComponentsInChildren<Animator>(true).Length} source={(src != null ? AssetDatabase.GetAssetPath(src) : "(none)")}");
            }

        sb.AppendLine("--- model assets: current vs Optimized ---");
        foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { "Assets/MuseDesk/Art/Models" }))
        {
            string p = AssetDatabase.GUIDToAssetPath(guid);
            if (!(p.Contains("/Optimized/") || p.Contains("Workstation_") || p.Contains("KanbanFrame") || p.Contains("Factory") || p.Contains("/UI/") || p.Contains("/Affordances/"))) continue;
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(p);
            if (go == null) continue;
            var rs = go.GetComponentsInChildren<MeshRenderer>(true);
            int mats = rs.SelectMany(r => r.sharedMaterials).Where(m => m != null).Distinct().Count();
            int tris = rs.Select(r => r.GetComponent<MeshFilter>()).Where(f => f != null && f.sharedMesh != null).Sum(f => f.sharedMesh.triangles.Length / 3);
            int nodes = go.GetComponentsInChildren<Transform>(true).Length;
            sb.AppendLine($"{p.Replace("Assets/MuseDesk/Art/Models/", "")}: renderers={rs.Length} materials={mats} tris={tris} nodes={nodes}");
        }
        return sb.ToString();
    }
}
