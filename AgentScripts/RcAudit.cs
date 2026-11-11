using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

/// <summary>Read the Quest-critical build settings authoritatively (not by parsing YAML maps).</summary>
public static class RcAudit
{
    public static string Dump()
    {
        var sb = new StringBuilder();
        var android = NamedBuildTarget.Android;

        sb.Append($"productName         : {PlayerSettings.productName}\n");
        sb.Append($"companyName         : {PlayerSettings.companyName}\n");
        sb.Append($"applicationId(Droid): {PlayerSettings.GetApplicationIdentifier(android)}\n");
        sb.Append($"bundleVersion       : {PlayerSettings.bundleVersion}\n");
        sb.Append($"bundleVersionCode   : {PlayerSettings.Android.bundleVersionCode}\n");
        sb.Append($"scriptingBackend    : {PlayerSettings.GetScriptingBackend(android)}\n");
        sb.Append($"targetArchitectures : {PlayerSettings.Android.targetArchitectures}\n");
        sb.Append($"minSdk / targetSdk  : {PlayerSettings.Android.minSdkVersion} / {PlayerSettings.Android.targetSdkVersion}\n");
        sb.Append($"graphicsAPIs(Droid) : {string.Join(",", PlayerSettings.GetGraphicsAPIs(BuildTarget.Android))}\n");
        sb.Append($"colorSpace          : {PlayerSettings.colorSpace}\n");
        return sb.ToString();
    }
}
