using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
public static class GameCapSetup {
  public static string Run(){
    // fresh temp scene so only the desk + a framing camera exist (true culling test)
    var sc = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
    var camGo = new GameObject("TestCam");
    var cam = camGo.AddComponent<Camera>();
    camGo.tag = "MainCamera";
    cam.clearFlags = CameraClearFlags.SolidColor;
    cam.backgroundColor = new Color(0.2f,0.25f,0.3f);
    camGo.transform.position = new Vector3(0.25f, 0.18f, -0.35f);
    camGo.transform.LookAt(new Vector3(0,0.08f,0));
    var light = new GameObject("L").AddComponent<Light>(); light.type=LightType.Directional; light.transform.rotation=Quaternion.Euler(50,-30,0);
    var fbx = AssetDatabase.LoadMainAssetAtPath("Assets/MuseDesk/Art/Models/Workstations/Workstation_Developer.fbx") as GameObject;
    var d = Object.Instantiate(fbx); d.transform.position=Vector3.zero;
    var plant = AssetDatabase.LoadMainAssetAtPath("Assets/MuseDesk/Art/Models/Decorations/Decor_MiniPlant.fbx") as GameObject;
    var pl = Object.Instantiate(plant); pl.transform.position=new Vector3(0.18f,0,0.05f);
    return "temp scene ready";
  }
}
