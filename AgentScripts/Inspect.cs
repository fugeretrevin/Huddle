using System.Linq;
using System.Text;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class Inspect
{
    public static string Scene()
    {
        var scene = EditorSceneManager.GetActiveScene();
        var sb = new StringBuilder();
        sb.AppendLine($"scene={scene.name} path={scene.path}");
        foreach (var root in scene.GetRootGameObjects())
            sb.AppendLine($"ROOT: {root.name}  active={root.activeSelf}");

        var cams = Object.FindObjectsByType<Camera>(FindObjectsInactive.Include);
        sb.AppendLine($"Cameras: {cams.Length}");
        foreach (var c in cams) sb.AppendLine($"  cam: {c.gameObject.name} tag={c.tag}");

        var mrukType = System.AppDomain.CurrentDomain.GetAssemblies()
            .Select(a => a.GetType("Meta.XR.MRUtilityKit.MRUK")).FirstOrDefault(t => t != null);
        var mruk = mrukType != null ? Object.FindObjectsByType(mrukType, FindObjectsInactive.Include) : new Object[0];
        sb.AppendLine($"MRUK components: {mruk.Length}");

        var ovrMgrType = System.AppDomain.CurrentDomain.GetAssemblies()
            .Select(a => a.GetType("OVRManager")).FirstOrDefault(t => t != null);
        var ovr = ovrMgrType != null ? Object.FindObjectsByType(ovrMgrType, FindObjectsInactive.Include) : new Object[0];
        sb.AppendLine($"OVRManager: {ovr.Length}");
        return sb.ToString();
    }
}
