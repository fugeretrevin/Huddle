using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using MuseDesk.Agents;

var sb = new StringBuilder();
foreach (var v in Object.FindObjectsByType<AgentView>(FindObjectsInactive.Include))
{
    var anim = v.GetComponentInChildren<Animator>();
    var br = v.GetComponent<AgentAnimatorBridge>();
    var smr = v.GetComponentInChildren<SkinnedMeshRenderer>();
    sb.AppendLine($"[{v.agentId}] anim.ctrl={(anim&&anim.runtimeAnimatorController?anim.runtimeAnimatorController.name:"NULL")} " +
                  $"avatar={(anim&&anim.avatar?"ok":"NULL")} bridge={(br?"ok":"NULL")} " +
                  $"bridge.anim={(br&&br.animator?"ok":"NULL")} bridge.view={(br&&br.view?"ok":"NULL")} bridge.move={(br&&br.movement?"ok":"NULL")} " +
                  $"SMR={(smr?smr.sharedMesh.name:"NULL")} worldPos={v.transform.position}");
}
// Aim SceneView at the workspace (agents ~ y=1.0, z=0.5)
var sv = SceneView.lastActiveSceneView;
if (sv != null)
{
    sv.LookAt(new Vector3(0f, 1.0f, 0.5f), Quaternion.Euler(10f, 180f, 0f), 1.1f);
    sv.Repaint();
    sb.AppendLine("SceneView aimed.");
}
return sb.ToString();
