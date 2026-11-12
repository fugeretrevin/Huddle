using System.Linq;
using System.Text;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using TMPro;
using MuseDesk.Agents;
using MuseDesk.Art;

public static class VerifyWs
{
    public static string Run()
    {
        EditorSceneManager.OpenScene("Assets/MuseDesk/Scenes/MainMRScene.unity", OpenSceneMode.Single);
        var sb = new StringBuilder();
        var ws = GameObject.Find("WorkspaceRoot");
        foreach (var sName in new[]{"ResearchStation","DevStation","QAStation"})
        {
            var st = ws.transform.Find(sName);
            var agent = st.GetComponentsInChildren<AgentView>(true).FirstOrDefault();
            var desk = st.Find("Workstation");
            var mon = st.Find("Monitor");
            sb.AppendLine($"== {sName} (worldPos {W(st.position)}) ==");
            if (agent!=null) sb.AppendLine($"  agent '{agent.agentId}' world={W(agent.transform.position)}");
            if (desk!=null){
                var rs=desk.GetComponentsInChildren<Renderer>();
                var b=rs[0].bounds; for(int i=1;i<rs.Length;i++) b.Encapsulate(rs[i].bounds);
                sb.AppendLine($"  desk world={W(desk.position)} boundsCenter={W(b.center)} size={W(b.size)}");
            } else sb.AppendLine("  desk MISSING");
            if (mon!=null){
                var tmp=mon.GetComponentInChildren<TMP_Text>();
                sb.AppendLine($"  monitor world={W(mon.position)} text='{(tmp!=null?tmp.text.Replace("\n","|"):"NONE")}' disp={(mon.GetComponent<WorkstationStatusDisplay>()!=null)}");
            } else sb.AppendLine("  monitor MISSING");
        }
        return sb.ToString();
    }
    static string W(Vector3 v)=>$"({v.x:0.##},{v.y:0.##},{v.z:0.##})";
}
