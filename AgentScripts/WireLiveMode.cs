using UnityEditor;
using UnityEngine;
using MuseDesk.Config;

public static class WireLiveMode
{
    const string Path = "Assets/MuseDesk/Resources/RuntimeSettings.asset";

    public static string Apply()
    {
        var s = AssetDatabase.LoadAssetAtPath<RuntimeSettings>(Path);
        if (s == null) return "RuntimeSettings not found";
        s.preferOfflineDemo = false;     // try the PC backend first, fall back to offline demo
        s.connectTimeoutSeconds = 5f;
        s.apiBaseUrl = "http://127.0.0.1:8765";
        s.webSocketBaseUrl = "ws://127.0.0.1:8765";
        EditorUtility.SetDirty(s);
        AssetDatabase.SaveAssets();
        return $"preferOfflineDemo={s.preferOfflineDemo} timeout={s.connectTimeoutSeconds} api={s.apiBaseUrl}";
    }
}
