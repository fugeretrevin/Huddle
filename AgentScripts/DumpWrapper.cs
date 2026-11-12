using System.Text;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

public static class DumpWrapper
{
    public static string Run()
    {
        var scene = EditorSceneManager.OpenScene("Assets/MuseDesk/Scenes/MainMRScene.unity", OpenSceneMode.Single);
        var sb = new StringBuilder();
        foreach (var root in scene.GetRootGameObjects())
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name=="[BuildingBlock] ISDK_PokeInteraction" && root.name=="Poke Interaction")
                {
                    var comp = t.GetComponents<Component>();
                    foreach(var c in comp){
                        if(c==null) continue;
                        if(c.GetType().Name=="InteractableUnityEventWrapper"){
                            var so=new SerializedObject(c);
                            var it=so.GetIterator();
                            while(it.NextVisible(true))
                                sb.AppendLine($"{it.propertyType}  {it.propertyPath}  ({it.displayName})");
                        }
                    }
                    return sb.ToString();
                }
        return "not found";
    }
}
