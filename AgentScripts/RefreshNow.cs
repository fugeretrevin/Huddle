using UnityEditor;
using UnityEditor.Compilation;

public static class RefreshNow
{
    public static string Run()
    {
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        CompilationPipeline.RequestScriptCompilation();
        return "refresh + compile requested";
    }
}
