using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Is the build's Wit.ai voice path alive? Uses the client token of the WitConfiguration the scene's AppVoiceExperience
/// uses, IN MEMORY ONLY (never logged, printed or written), to send (1) a text message and (2) real speech audio files
/// (16 kHz mono WAV, e.g. Windows TTS) to Wit's /speech endpoint, and reports HTTP status, latency and the transcript.
///   unity command script run --file AgentScripts/WitProbe.cs --entry WitProbe.Run --args '["<dir with wav files>"]' --timeout 120
/// </summary>
public static class WitProbe
{
    const string Api = "https://api.wit.ai";

    public static async Task<string> Run(string wavDir)
    {
        var sb = new StringBuilder();
        string token = Token(out string where);
        if (string.IsNullOrEmpty(token)) return "no client token found (" + where + ")";
        sb.AppendLine("token: present (" + token.Length + " chars, from " + where + ")");

        var t0 = DateTime.UtcNow;
        using (var req = UnityWebRequest.Get(Api + "/message?v=20240304&q=" + UnityWebRequest.EscapeURL("Researcher, why?")))
        {
            req.SetRequestHeader("Authorization", "Bearer " + token);
            req.timeout = 20;
            var op = req.SendWebRequest();
            while (!op.isDone) await Task.Delay(50);
            sb.AppendLine("message: HTTP " + req.responseCode + " " + req.result + " in " + (DateTime.UtcNow - t0).TotalMilliseconds.ToString("F0") + " ms" + (req.responseCode != 200 ? " body=" + Clip(req.downloadHandler.text) : ""));
        }

        foreach (var f in Directory.GetFiles(wavDir, "*.wav").OrderBy(x => x))
        {
            byte[] audio = File.ReadAllBytes(f);
            t0 = DateTime.UtcNow;
            using (var req = new UnityWebRequest(Api + "/speech?v=20240304", "POST"))
            {
                req.uploadHandler = new UploadHandlerRaw(audio);
                req.downloadHandler = new DownloadHandlerBuffer();
                req.SetRequestHeader("Authorization", "Bearer " + token);
                req.SetRequestHeader("Content-Type", "audio/wav");
                req.timeout = 30;
                var op = req.SendWebRequest();
                while (!op.isDone) await Task.Delay(50);
                string body = req.downloadHandler.text ?? "";
                // The streaming endpoint returns several JSON chunks; keep the final "text".
                string text = "";
                foreach (var key in new[] { "\"text\": \"", "\"text\":\"" })
                {
                    int i = body.LastIndexOf(key, StringComparison.Ordinal);
                    if (i >= 0) { int s = i + key.Length; int e = body.IndexOf('"', s); if (e > s) text = body.Substring(s, e - s); break; }
                }
                sb.AppendLine(Path.GetFileName(f) + ": HTTP " + req.responseCode + " in " + (DateTime.UtcNow - t0).TotalMilliseconds.ToString("F0")
                              + " ms, transcript='" + text + "'" + (req.responseCode != 200 ? " body=" + Clip(body) : ""));
            }
        }
        return sb.ToString();
    }

    static string Clip(string s) { s = (s ?? "").Replace("\n", " "); return s.Length > 300 ? s.Substring(0, 300) : s; }

    static string Token(out string where)
    {
        where = "nothing";
        Type avType = AppDomain.CurrentDomain.GetAssemblies().SelectMany(a => { try { return a.GetTypes(); } catch { return new Type[0]; } })
            .FirstOrDefault(t => t.Name == "AppVoiceExperience");
        if (avType == null) return null;
        foreach (var av in Resources.FindObjectsOfTypeAll(avType).Cast<Component>().Where(c => c.gameObject.scene.IsValid()))
        {
            object cfg = avType.GetProperty("Configuration")?.GetValue(av);
            if (cfg == null) continue;
            var m = cfg.GetType().GetMethod("GetClientAccessToken");
            string tok = m != null ? m.Invoke(cfg, null) as string : null;
            if (!string.IsNullOrEmpty(tok)) { where = av.gameObject.name + "'s AppVoiceExperience configuration"; return tok; }
        }
        return null;
    }
}
