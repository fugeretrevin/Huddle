using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>Reload the committed (voice-free) scene into the editor and drop orphaned voice scaffolding.</summary>
public static class CleanupVoice
{
    public static string Go()
    {
        EditorSceneManager.OpenScene("Assets/MuseDesk/Scenes/MainMRScene.unity", OpenSceneMode.Single);
        bool deleted = AssetDatabase.DeleteAsset("Assets/Oculus/Voice");
        AssetDatabase.Refresh();
        return $"scene reloaded (voice-free); Oculus/Voice deleted={deleted}";
    }
}
