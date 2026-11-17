using System.Linq;
using System.Text;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using TMPro;

/// <summary>Read-only facts for the release-candidate pass: stamp poses, artifact panel pose and size,
/// paper text colours, archive readout.</summary>
public static class RcProbe
{
    public static string Run()
    {
        var all = EditorSceneManager.GetActiveScene().GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true)).ToList();
        var ws = all.First(t => t.name == "WorkspaceRoot");
        var sb = new StringBuilder();
        System.Func<Transform, string> path = t => { string s = t.name; while (t.parent != null) { t = t.parent; s = t.name + "/" + s; } return s; };
        foreach (var n in new[] { "StampApprove", "StampReject", "Doc_StampRestPad", "ArtifactPanel", "ArchiveReadout", "CompletionReceipt", "DecisionDocument", "TaskDock", "ModePlaque", "FocusToggle", "SoftwareFactory", "ProjectShelf", "DeliveryZone" })
        {
            foreach (var t in all.Where(x => x.name == n))
            {
                var rs = t.GetComponentsInChildren<Renderer>(true).Where(r => r.GetComponent<TMP_Text>() == null).ToList();
                Bounds b = new Bounds(t.position, Vector3.zero); foreach (var r in rs) b.Encapsulate(r.bounds);
                sb.AppendLine($"{path(t)} active={t.gameObject.activeInHierarchy} rootLocal={ws.InverseTransformPoint(t.position):F3} euler={t.eulerAngles:F0} lossy={t.lossyScale:F3} boundsSize={b.size:F3} boundsCentreRL={ws.InverseTransformPoint(b.center):F3} comps={string.Join(",", t.GetComponents<MonoBehaviour>().Where(m => m != null).Select(m => m.GetType().Name))}");
            }
        }
        sb.AppendLine("--- ArtifactPanel children ---");
        var ap = all.FirstOrDefault(t => t.name == "ArtifactPanel");
        if (ap != null) foreach (var t in ap.GetComponentsInChildren<Transform>(true))
            {
                var tmp = t.GetComponent<TMP_Text>();
                sb.AppendLine($"  {path(t).Substring(path(ap).Length)} active={t.gameObject.activeSelf} lp={t.localPosition:F3} ls={t.localScale:F3}" + (tmp != null ? $" font={tmp.fontSize} rect={tmp.rectTransform.sizeDelta} col={ColorUtility.ToHtmlStringRGBA(tmp.color)}" : ""));
            }
        sb.AppendLine("--- paper text colours ---");
        var d = all.First(t => t.name == "DecisionDocument");
        foreach (var tmp in d.GetComponentsInChildren<TMP_Text>(true))
            sb.AppendLine($"  {tmp.name}: #{ColorUtility.ToHtmlStringRGBA(tmp.color)} font={tmp.fontSize} style={tmp.fontStyle} faceCol=#{(tmp.fontSharedMaterial != null && tmp.fontSharedMaterial.HasProperty("_FaceColor") ? ColorUtility.ToHtmlStringRGBA(tmp.fontSharedMaterial.GetColor("_FaceColor")) : "?")} mat={tmp.fontSharedMaterial?.name}");
        sb.AppendLine("--- card template text ---");
        var card = all.First(t => t.name == "CardTemplate");
        foreach (var tmp in card.GetComponentsInChildren<TMP_Text>(true))
            sb.AppendLine($"  {tmp.name} active={tmp.gameObject.activeSelf}: #{ColorUtility.ToHtmlStringRGBA(tmp.color)} font={tmp.fontSize} lossy={tmp.transform.lossyScale.x:F4} rect={tmp.rectTransform.sizeDelta}");
        var sv = SceneView.lastActiveSceneView;
        sb.AppendLine($"sceneview: lighting={sv.sceneLighting} fx fog={sv.sceneViewState.fogEnabled} skybox={sv.sceneViewState.skyboxEnabled} pp={sv.sceneViewState.showImageEffects} drawMode={sv.cameraMode.drawMode}");
        return sb.ToString();
    }
}
