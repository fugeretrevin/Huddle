using System.Text;
using UnityEditor;
using UnityEditor.Compilation;

public static class ForceImport
{
    public static string Go()
    {
        var sb = new StringBuilder();
        foreach (var p in new[] {
            "Assets/MuseDesk/Scripts/Voice/WitVoiceSource.cs" })
        {
            AssetDatabase.ImportAsset(p, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            var guid = AssetDatabase.AssetPathToGUID(p);
            var mono = AssetImporter.GetAtPath(p) as MonoImporter;
            sb.Append(p).Append("  guid=").Append(guid)
              .Append("  importer=").Append(mono != null ? "MonoImporter" : (AssetImporter.GetAtPath(p)?.GetType().Name ?? "NULL"))
              .Append('\n');
        }
        CompilationPipeline.RequestScriptCompilation();
        sb.Append("requested compilation\n");
        return sb.ToString();
    }
}
