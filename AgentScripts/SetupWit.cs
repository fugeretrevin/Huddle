using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Creates + links a WitConfiguration asset from a Server Access Token (passed as an arg, never
/// hardcoded). Uses the SDK's own WitConfigurationUtility.SetServerToken, which validates the token
/// and pulls the runtime client token + app info from Wit over the network. The asset holds a
/// credential, so it lives under Assets/MuseDesk/Voice/ and that path is gitignored by the caller.
/// </summary>
public static class SetupWit
{
    static Type Find(string n) => AppDomain.CurrentDomain.GetAssemblies()
        .SelectMany(a => { try { return a.GetTypes(); } catch { return Array.Empty<Type>(); } })
        .FirstOrDefault(t => t.Name == n);

    // Token is read from a scratchpad file (outside the repo), never hardcoded or committed.
    const string TokenPath = @"C:\Users\stoof\AppData\Local\Temp\claude\c6247a71f8f519f5\f7d56877-3ea7-408b-a48b-21c3b3042f66\scratchpad\wit_token.txt";

    public static async Task<string> Go()
    {
        if (!System.IO.File.Exists(TokenPath)) return "ERROR: token file not found";
        string serverToken = System.IO.File.ReadAllText(TokenPath).Trim();
        if (string.IsNullOrWhiteSpace(serverToken)) return "ERROR: token file empty";

        var cfgType = Find("WitConfiguration");
        var utilType = Find("WitConfigurationUtility");
        if (cfgType == null || utilType == null) return "ERROR: Wit config types not found";

        const string dir = "Assets/MuseDesk/Voice";
        System.IO.Directory.CreateDirectory(dir);
        const string path = dir + "/WitConfig_MuseDesk.asset";

        var cfg = AssetDatabase.LoadAssetAtPath(path, cfgType) as ScriptableObject;
        if (cfg == null)
        {
            cfg = ScriptableObject.CreateInstance(cfgType);
            AssetDatabase.CreateAsset(cfg, path);
        }

        var setServerToken = utilType.GetMethod("SetServerToken",
            BindingFlags.Static | BindingFlags.Public,
            null, new[] { cfgType, typeof(string), typeof(Action<string>) }, null);
        if (setServerToken == null) return "ERROR: SetServerToken(cfg,string,Action<string>) not found";

        var tcs = new TaskCompletionSource<string>();
        Action<string> onComplete = err => tcs.TrySetResult(err);
        setServerToken.Invoke(null, new object[] { cfg, serverToken.Trim(), onComplete });

        // bounded wait for the network round-trip
        var done = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(45)));
        string result = done == tcs.Task ? (tcs.Task.Result ?? "") : "TIMEOUT";

        EditorUtility.SetDirty(cfg);
        AssetDatabase.SaveAssets();

        // read back what linked (mask the client token; never print it in full)
        string appId = Invoke(cfg, "GetApplicationId");
        string client = Invoke(cfg, "GetClientAccessToken");
        string mask = string.IsNullOrEmpty(client) ? "(none)" : $"{client.Substring(0, Math.Min(4, client.Length))}…len{client.Length}";

        bool ok = string.IsNullOrEmpty(result) || result == "";
        return $"setServerToken='{(string.IsNullOrEmpty(result) ? "OK" : result)}' appId='{appId}' clientToken={mask} asset={path}";
    }

    static string Invoke(object o, string method)
    {
        try
        {
            var m = o.GetType().GetMethod(method, BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
            return m?.Invoke(o, null) as string ?? "";
        }
        catch { return ""; }
    }
}
