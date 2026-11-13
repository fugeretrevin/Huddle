using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using MuseDesk.Spatial;

/// <summary>
/// Adds the ceiling scan grid: creates Mat_CeilingScanGrid (if missing) and one scene-root object
/// "CeilingScanReveal" wired to the director. Idempotent; nothing is deleted. Saves the scene.
/// Revert() deactivates the object.
///   BuildCeilingScanV1.Apply
/// </summary>
public static class BuildCeilingScanV1
{
    const string MatPath = "Assets/MuseDesk/Art/Materials/Mat_CeilingScanGrid.mat";
    const string ShaderName = "MuseDesk/Spatial/CeilingScanGrid";
    const string ObjName = "CeilingScanReveal";

    public static string Apply()
    {
        if (EditorApplication.isPlaying) return "refused: in Play Mode";
        var scene = EditorSceneManager.GetActiveScene();
        if (!scene.path.EndsWith("MainMRScene.unity")) return "refused: active scene is " + scene.path;

        var shader = Shader.Find(ShaderName);
        if (shader == null) return "shader " + ShaderName + " not found (compile errors?)";

        var mat = AssetDatabase.LoadAssetAtPath<Material>(MatPath);
        bool madeMat = false;
        if (mat == null) { mat = new Material(shader); AssetDatabase.CreateAsset(mat, MatPath); madeMat = true; }
        mat.shader = shader;
        mat.renderQueue = 3000;
        mat.SetColor("_BaseColor", new Color(0.184f, 0.639f, 0.604f, 0.6f));
        mat.SetFloat("_Fade", 1f);
        EditorUtility.SetDirty(mat);
        AssetDatabase.SaveAssets();

        var all = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true)).ToList();
        var go = all.Select(t => t.gameObject).FirstOrDefault(g => g.name == ObjName);
        bool madeGo = false;
        if (go == null) { go = new GameObject(ObjName); Undo.RegisterCreatedObjectUndo(go, ObjName); madeGo = true; }
        go.SetActive(true);
        if (go.transform.parent != null) go.transform.SetParent(null, true);   // world space: never under WorkspaceRoot
        go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

        var comp = go.GetComponent<CeilingScanReveal>();
        if (comp == null) comp = go.AddComponent<CeilingScanReveal>();
        comp.material = mat;
        comp.director = Object.FindAnyObjectByType<SpatialWorkspaceDirector>(FindObjectsInactive.Include);

        // Footprint ring: the size of the ceiling mount, if a mount renderer can be measured.
        string ringNote = "default " + comp.ringRadius.ToString("F2") + " m";
        var mount = all.FirstOrDefault(t => t.name == "CeilingMount");
        if (mount != null)
        {
            var rs = mount.GetComponentsInChildren<Renderer>(true);
            if (rs.Length > 0)
            {
                Bounds b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
                float rad = Mathf.Clamp(Mathf.Max(b.extents.x, b.extents.z), 0.08f, 0.35f);
                comp.ringRadius = rad;
                ringNote = "measured from CeilingMount " + rad.ToString("F3") + " m";
            }
        }
        EditorUtility.SetDirty(comp);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        return $"ceiling scan grid: material {(madeMat ? "created" : "updated")}, object {(madeGo ? "created" : "found")}, director {(comp.director != null ? "wired" : "MISSING")}, ring {ringNote}";
    }

    public static string Revert()
    {
        var scene = EditorSceneManager.GetActiveScene();
        var go = scene.GetRootGameObjects().FirstOrDefault(g => g.name == ObjName);
        if (go == null) return "nothing to revert";
        go.SetActive(false);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        return "CeilingScanReveal deactivated";
    }
}
