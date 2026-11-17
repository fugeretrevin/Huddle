using System.Text;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

public static class InspectPoke
{
    public static string Run()
    {
        var scene = EditorSceneManager.OpenScene("Assets/MuseDesk/Scenes/MainMRScene.unity", OpenSceneMode.Single);
        var sb = new StringBuilder();
        Transform poke=null, btnApprove=null;
        foreach (var root in scene.GetRootGameObjects())
            foreach (var t in root.GetComponentsInChildren<Transform>(true)){
                if (t.name=="[BuildingBlock] ISDK_PokeInteraction" && poke==null && root.name=="Poke Interaction") poke=t;
                if (t.name=="Btn_Approve" && btnApprove==null) btnApprove=t;
            }
        sb.AppendLine("== standalone Poke template components ==");
        if(poke!=null) Dump(poke, sb, 0, 2);
        sb.AppendLine("== Btn_Approve components (event wiring) ==");
        if(btnApprove!=null){
            foreach(var c in btnApprove.GetComponents<Component>())
                sb.AppendLine("  " + (c==null?"<MISSING>":c.GetType().FullName));
        }
        return sb.ToString();
    }
    static void Dump(Transform t, StringBuilder sb, int d, int max){
        if(d>max) return;
        var comps="";
        foreach(var c in t.GetComponents<Component>()) comps += (c==null?"<MISSING>":c.GetType().Name)+",";
        sb.AppendLine(new string(' ',d*2)+t.name+" ["+comps+"]");
        foreach(Transform ch in t) Dump(ch,sb,d+1,max);
    }
}
