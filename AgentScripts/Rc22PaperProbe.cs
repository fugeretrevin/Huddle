using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;
using MuseDesk.Documents;
using MuseDesk.Voice;

/// <summary>Play Mode probes for rc22: the why-answer is spoken, and a poofed stamp cannot be grabbed.
/// Call with scripts\drv.ps1 Rc22PaperProbe Method. Nothing here sends or implies a decision.</summary>
public static class Rc22PaperProbe
{
    static Transform Find(string n) => Enumerable.Range(0, SceneManager.sceneCount).Select(SceneManager.GetSceneAt)
        .SelectMany(s => s.GetRootGameObjects()).SelectMany(r => r.GetComponentsInChildren<Transform>(true)).FirstOrDefault(t => t.name == n);

    static string Speech() =>
        $"AgentSpeech lastSpeaker='{AgentSpeech.LastSpeaker}' lastSaid='{AgentSpeech.LastSaid}' saidCount={AgentSpeech.SaidCount}";

    /// <summary>Press the paper's Why key (DocumentDeliveryController.OnAskWhy, the key's own handler). The answer
    /// is async: call AskWhyState a second or two later.</summary>
    public static string AskWhy()
    {
        if (!Application.isPlaying) return "not playing";
        var d = Object.FindAnyObjectByType<DocumentDeliveryController>();
        if (d == null) return "no DocumentDeliveryController";
        int before = AgentSpeech.SaidCount;
        d.OnAskWhy();
        return $"asked (saidCount before={before}). Now: " + AskWhyState();
    }

    /// <summary>The paper's answer text and what the agents last said.</summary>
    public static string AskWhyState()
    {
        if (!Application.isPlaying) return "not playing";
        var p = Object.FindAnyObjectByType<DecisionPanel>(FindObjectsInactive.Include);
        string ans = p != null && p.answer != null ? p.answer.text : "(no panel/answer field)";
        return $"answer='{ans}' | {Speech()}";
    }

    static StampPoof[] Stamps() => new[] { Find("StampApprove"), Find("StampReject") }
        .Select(t => t != null ? t.GetComponent<StampPoof>() : null).ToArray();

    /// <summary>Send both stamps away now. Call StampState after about a second. (A pending decision brings them back.)</summary>
    public static string PoofStamps()
    {
        if (!Application.isPlaying) return "not playing";
        foreach (var s in Stamps()) if (s != null) { s.lingerSeconds = 0.01f; s.Dismiss(); }
        return "dismissed; read StampState in ~1 s";
    }

    public static string UnpoofStamps()
    {
        if (!Application.isPlaying) return "not playing";
        foreach (var s in Stamps()) if (s != null) s.Summon();
        return "summoned; read StampState in ~1 s";
    }

    /// <summary>Per stamp: phase, renderers visible, enabled/disabled Interaction SDK interactables, colliders.</summary>
    public static string StampState()
    {
        if (!Application.isPlaying) return "not playing";
        var sb = new StringBuilder();
        foreach (var n in new[] { "StampApprove", "StampReject" })
        {
            var t = Find(n);
            if (t == null) { sb.AppendLine(n + ": MISSING"); continue; }
            var s = t.GetComponent<StampPoof>();
            int rendOn = t.GetComponentsInChildren<Renderer>(false).Count(r => r.enabled && !(r is ParticleSystemRenderer) && r.bounds.size.y > 0.002f);
            var ints = t.GetComponentsInChildren<MonoBehaviour>(true).Where(m => m is Oculus.Interaction.IInteractable).ToArray();
            int intOn = ints.Count(m => m.enabled);
            var cols = t.GetComponents<Collider>();
            string colInfo = string.Join(",", cols.Select(c => c.enabled + (c is BoxCollider b ? ":" + b.size.y.ToString("F4") : "")));
            sb.AppendLine($"{n}: phase={(s != null ? s.PhaseName : "n/a")} away={(s != null && s.Away)} visibleRenderers={rendOn} " +
                          $"interactables={ints.Length} enabled={intOn} mutedByPoof={(s != null ? s.MutedInteractableCount : -1)} rootColliders=[{colInfo}] " +
                          $"interactableNames=[{string.Join(",", ints.Select(m => m.GetType().Name + (m.enabled ? "+" : "-")))}]");
        }
        return sb.ToString();
    }
}
