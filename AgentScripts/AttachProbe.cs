using UnityEngine;
using UnityEditor.SceneManagement;
using MuseDesk.DebugTools;
public static class AttachProbe {
  public static string Run(){
    var s=EditorSceneManager.OpenScene("Assets/MuseDesk/Scenes/MainMRScene.unity", OpenSceneMode.Single);
    var app=GameObject.Find("AppServices");
    if(app.GetComponent<DeviceRenderProbe>()==null) app.AddComponent<DeviceRenderProbe>();
    EditorSceneManager.MarkSceneDirty(s); EditorSceneManager.SaveScene(s);
    return "probe attached";
  }
}
