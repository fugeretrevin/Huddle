using System.Text;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

public static class InspectBtn
{
    public static string Run()
    {
        EditorSceneManager.OpenScene("Assets/MuseDesk/Scenes/MainMRScene.unity", OpenSceneMode.Single);
        var sb = new StringBuilder();
        var doc = GameObject.Find("DecisionDocument");
        var btn = FindDeep(doc.transform, "Btn_Approve");
        if (btn==null){ return "no Btn_Approve"; }
        foreach (var c in btn.GetComponents<Component>())
            sb.AppendLine(c==null ? "<MISSING SCRIPT>" : c.GetType().FullName);
        sb.AppendLine("-- children --");
        foreach (Transform ch in btn)
            foreach (var c in ch.GetComponents<Component>())
                sb.AppendLine($"{ch.name} :: " + (c==null?"<MISSING>":c.GetType().Name));
        return sb.ToString();
    }
    static Transform FindDeep(Transform r, string n){ foreach(var t in r.GetComponentsInChildren<Transform>(true)) if(t.name==n) return t; return null; }
}
