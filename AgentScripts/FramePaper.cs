using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class FramePaper
{
    public static string Show()
    {
        var scene = EditorSceneManager.GetActiveScene();
        var all = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true)).ToList();
        var paper = all.FirstOrDefault(t => t.name == "DecisionDocument");
        if (paper == null) return "paper not found";
        paper.gameObject.SetActive(true);
        // reveal content for the capture (OnEnable may have hidden it in handoff mode, and edit mode
        // doesn't run OnEnable reliably) - force all children active for the photo.
        foreach (var t in paper.GetComponentsInChildren<Transform>(true)) t.gameObject.SetActive(true);

        var existing = GameObject.Find("TempPaperCam"); if (existing) Object.DestroyImmediate(existing);
        var go = new GameObject("TempPaperCam");
        var cam = go.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.3f,0.32f,0.35f);
        cam.fieldOfView = 40f; cam.nearClipPlane = 0.01f;
        Vector3 p = paper.transform.position - paper.transform.forward * 0.6f; // readable (-Z) side
        cam.transform.position = p;
        cam.transform.rotation = Quaternion.LookRotation(paper.transform.position - p, Vector3.up);
        return $"paper active + TempPaperCam at {p}";
    }
    public static string Hide()
    {
        var existing = GameObject.Find("TempPaperCam"); if (existing) Object.DestroyImmediate(existing);
        var scene = EditorSceneManager.GetActiveScene();
        var paper = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true)).FirstOrDefault(t => t.name == "DecisionDocument");
        if (paper) paper.gameObject.SetActive(false);
        EditorSceneManager.SaveScene(scene);
        return "cleaned up + paper inactive + saved";
    }
}
