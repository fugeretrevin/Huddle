using System.Text;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using MuseDesk.Agents;

public static class StationInspect
{
    public static string Run()
    {
        EditorSceneManager.OpenScene("Assets/MuseDesk/Scenes/MainMRScene.unity", OpenSceneMode.Single);
        var sb = new StringBuilder();
        var ws = GameObject.Find("WorkspaceRoot");
        foreach (var sName in new[]{"ResearchStation","DevStation","QAStation"})
        {
            var st = ws.transform.Find(sName);
            if (st == null) { sb.AppendLine(sName+" MISSING"); continue; }
            sb.AppendLine($"{sName} localPos={st.localPosition} localRot={st.localEulerAngles} scale={st.localScale}");
            foreach (Transform c in st)
            {
                sb.AppendLine($"   {c.name} p={F(c.localPosition)} r={F(c.localEulerAngles)} s={F(c.localScale)}");
                var av = c.GetComponent<AgentView>();
                if (av != null) sb.AppendLine($"      ^AgentView id={av.agentId}");
            }
        }
        var app = GameObject.Find("AppServices");
        sb.AppendLine("AppServices present: " + (app!=null));
        return sb.ToString();
    }
    static string F(Vector3 v)=>$"({v.x:0.###},{v.y:0.###},{v.z:0.###})";
}
