using UnityEditor;

public static class RefreshDb
{
    public static string Go()
    {
        AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
        return "refreshed";
    }
}
