using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using MuseDesk.Agents;
using MuseDesk.Documents;

var scene = EditorSceneManager.OpenScene("Assets/MuseDesk/Scenes/MainMRScene.unity", OpenSceneMode.Single);
var sb = new StringBuilder();

static string Path(Transform t)
{
    var s = t.name;
    while (t.parent != null) { t = t.parent; s = t.name + "/" + s; }
    return s;
}

var roots = scene.GetRootGameObjects();
sb.AppendLine("ROOTS: " + string.Join(", ", roots.Select(r => r.name)));

sb.AppendLine("\n== AgentView instances ==");
foreach (var v in Object.FindObjectsByType<AgentView>(FindObjectsInactive.Include, FindObjectsSortMode.None))
{
    var comps = v.GetComponents<Component>().Select(c => c.GetType().Name);
    sb.AppendLine($"[{v.agentId}] path={Path(v.transform)} active={v.gameObject.activeInHierarchy}");
    sb.AppendLine($"   localPos={v.transform.localPosition} worldPos={v.transform.position} rot={v.transform.eulerAngles}");
    sb.AppendLine($"   parent={(v.transform.parent? Path(v.transform.parent):"<root>")}");
    sb.AppendLine($"   components: {string.Join(", ", comps)}");
    sb.AppendLine($"   bob={(v.bob?v.bob.name:"null")} bodyRenderer={(v.bodyRenderer?v.bodyRenderer.name:"null")} label={(v.label?v.label.name:"null")} alert={(v.alert?v.alert.name:"null")} movement={(v.movement?"set":"null")}");
}

sb.AppendLine("\n== AgentRegistry ==");
foreach (var r in Object.FindObjectsByType<AgentRegistry>(FindObjectsInactive.Include, FindObjectsSortMode.None))
{
    sb.AppendLine($"path={Path(r.transform)} controller={(r.controller?"set":"null")} deliveryAnchor={(r.deliveryAnchor?Path(r.deliveryAnchor):"null")}");
    sb.AppendLine($"   agents=[{string.Join(", ", r.agents.Select(a => a? a.agentId : "NULL"))}]");
}

sb.AppendLine("\n== HeldPaperController ==");
foreach (var h in Object.FindObjectsByType<HeldPaperController>(FindObjectsInactive.Include, FindObjectsSortMode.None))
    sb.AppendLine($"path={Path(h.transform)} agentHoldPoint={(h.agentHoldPoint?Path(h.agentHoldPoint):"null")}");

return sb.ToString();
