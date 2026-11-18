#if UNITY_EDITOR
using UnityEditor;

public static class ReimportResearcher
{
    public static string Run()
    {
        AssetDatabase.ImportAsset("Assets/MuseDesk/Art/Models/Characters/Agent_Researcher.fbx", ImportAssetOptions.ForceUpdate);
        return "reimported";
    }
}
#endif
