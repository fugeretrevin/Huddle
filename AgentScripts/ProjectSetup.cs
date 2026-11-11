using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

/// <summary>
/// Applies Meta's auto-fixable Project Setup Tool rules for Android, and sets the
/// Quest-required player settings (IL2CPP + ARM64). Bundle id is a placeholder to be
/// changed before competition submission.
/// </summary>
public static class ProjectSetup
{
    public static async Task<string> FixAndroid()
    {
        var ovrType = System.AppDomain.CurrentDomain.GetAssemblies()
            .Select(a => a.GetType("OVRProjectSetup"))
            .FirstOrDefaultNonNull();

        string ovrResult = "OVRProjectSetup type not found";
        if (ovrType != null)
        {
            var fixAll = ovrType.GetMethod("FixAllAsync",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            if (fixAll != null)
            {
                var task = (Task)fixAll.Invoke(null, new object[] { BuildTargetGroup.Android });
                await task;
                ovrResult = "OVRProjectSetup.FixAllAsync(Android) completed";
            }
            else ovrResult = "FixAllAsync not found";
        }

        // Quest-required player settings.
        PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
        PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "com.musedesk.quest");
        PlayerSettings.productName = "MuseDesk";
        if (PlayerSettings.Android.minSdkVersion < AndroidSdkVersions.AndroidApiLevel32)
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel32;

        AssetDatabase.SaveAssets();

        return $"{ovrResult}; scriptingBackend=IL2CPP arch=ARM64 appId=com.musedesk.quest " +
               $"minSdk={PlayerSettings.Android.minSdkVersion} target={EditorUserBuildSettings.activeBuildTarget}";
    }

    // tiny helper (avoids LINQ null issues over reflected types)
    static System.Type FirstOrDefaultNonNull(this System.Collections.Generic.IEnumerable<System.Type> src)
    {
        foreach (var t in src) if (t != null) return t;
        return null;
    }
}
