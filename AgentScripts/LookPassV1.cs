using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// rc18 look pass (owner: "it all looks kinda dull"): adds the LookRig root with ViewerKeyLight (one soft warm key light that
/// follows the viewer's heading + a gradient ambient). Idempotent.
///   unity command script run --file AgentScripts/LookPassV1.cs --entry LookPassV1.Apply --args '[true]'
/// </summary>
public static class LookPassV1
{
    public static string Apply(bool save)
    {
        var sb = new StringBuilder();
        var scene = EditorSceneManager.GetActiveScene();
        GameObject rig = scene.GetRootGameObjects().FirstOrDefault(g => g.name == "LookRig");
        if (rig == null) { rig = new GameObject("LookRig"); Undo.RegisterCreatedObjectUndo(rig, "LookRig"); sb.AppendLine("created LookRig"); }
        var key = rig.GetComponent<MuseDesk.Art.ViewerKeyLight>() ?? rig.AddComponent<MuseDesk.Art.ViewerKeyLight>();
        if (key.keyLight == null)
        {
            var lgo = new GameObject("MuseKeyLight");
            lgo.transform.SetParent(rig.transform, false);
            lgo.transform.rotation = Quaternion.Euler(38f, -28f, 0f);
            var l = lgo.AddComponent<Light>();
            l.type = LightType.Directional; l.shadows = LightShadows.None; l.color = key.keyColor; l.intensity = key.keyIntensity;
            l.renderMode = LightRenderMode.ForcePixel;
            key.keyLight = l;
            sb.AppendLine("created MuseKeyLight");
        }
        // Saved pose = what a viewer at the desk (looking toward -Z of the office) gets at run time.
        key.keyLight.transform.rotation = Quaternion.Euler(key.elevation, 180f - key.sideAngle, 0f);
        EditorUtility.SetDirty(key.keyLight.transform);
        EditorUtility.SetDirty(key);
        key.ApplyAmbient();   // also in the saved scene, so Edit-mode captures match the device
        sb.AppendLine("ambient " + RenderSettings.ambientMode + " sky=" + RenderSettings.ambientSkyColor);
        if (save) { EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene); sb.AppendLine("saved"); }
        return sb.ToString();
    }
}
