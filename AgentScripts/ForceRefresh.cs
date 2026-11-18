#if UNITY_EDITOR
using UnityEditor;

public static class ForceRefresh
{
    public static string Run()
    {
        AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
        UnityEditor.Compilation.CompilationPipeline.RequestScriptCompilation();
        return "refreshed + compile requested";
    }
}
#endif
