using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Unified dock tidy (rc9 Editor review + audit #5):
///  - The old push-to-talk key (MicSlot/VoiceHud/MicKey, reads "Mic"/"Stop") duplicated the hands-free Mic toggle and hung
///    over the dock's right edge: hidden by scale (never SetActive; its readout and answer keys stay).
///  - Calm motion and the Mic toggle hung BELOW the dock face under Focus: they move into the free right column, aligned with
///    the two key rows (Mic over Calm motion), inside the face (x 0.115..0.155, rows centred y 0.0135 / -0.027).
///  - The mic level halo moves behind the Mic toggle so the hands-free mic still swells with speech.
/// Idempotent; the first run records every touched transform in Library/DockTidyV1.json for Revert.
///   scripts\run_guarded.ps1 -File AgentScripts\DockTidyV1.cs -Entry DockTidyV1.Apply
/// </summary>
public static class DockTidyV1
{
    const string StateFile = "Library/DockTidyV1.json";
    const float ColX = 0.135f, Row1Y = 0.0135f, Row2Y = -0.027f, KeyScale = 0.029f;

    [System.Serializable] class Rec { public string path; public Vector3 lp; public Vector3 ls; }
    [System.Serializable] class State { public List<Rec> recs = new List<Rec>(); }

    static Transform Console() => Resources.FindObjectsOfTypeAll<Transform>().FirstOrDefault(t => t.gameObject.scene.IsValid() && t.name == "ModeConsole");

    static string PathOf(Transform t, Transform root) { var s = t.name; for (var p = t.parent; p != null && p != root; p = p.parent) s = p.name + "/" + s; return s; }

    public static string Apply()
    {
        var sb = new StringBuilder("DockTidyV1.Apply\n");
        var c = Console(); if (c == null) return "no ModeConsole";
        Transform mic = c.Find("MicToggleKey"), motion = c.Find("MotionToggleKey"), slot = c.Find("MicSlot");
        Transform oldKey = slot != null ? slot.Find("VoiceHud/MicKey") : null, halo = slot != null ? slot.Find("VoiceHud/MicHalo") : null;
        if (mic == null || motion == null || slot == null || oldKey == null) return sb.Append("  missing MicToggleKey / MotionToggleKey / MicSlot / MicKey").ToString();

        if (!File.Exists(StateFile))
        {
            var st = new State();
            foreach (var t in new[] { mic, motion, oldKey, halo }.Where(x => x != null)) st.recs.Add(new Rec { path = PathOf(t, c), lp = t.localPosition, ls = t.localScale });
            File.WriteAllText(StateFile, JsonUtility.ToJson(st, true));
            sb.AppendLine("  recorded " + st.recs.Count + " transforms");
        }

        Undo.RecordObjects(new Object[] { mic, motion, oldKey }, "Dock tidy");
        oldKey.localScale = Vector3.one * 0.0001f;
        sb.AppendLine("  old push-to-talk MicKey hidden by scale");

        mic.localPosition = new Vector3(ColX, Row1Y, mic.localPosition.z);
        mic.localScale = Vector3.one * KeyScale;
        motion.localPosition = new Vector3(ColX, Row2Y, motion.localPosition.z);
        motion.localScale = Vector3.one * KeyScale;
        sb.AppendLine($"  Mic toggle -> ({ColX:F3}, {Row1Y:F4}), Calm motion -> ({ColX:F3}, {Row2Y:F3}), scale {KeyScale}");

        if (halo != null)
        {
            Undo.RecordObject(halo, "Dock tidy");
            // halo is under MicSlot/VoiceHud (MicSlot at console-local slot.localPosition): put it behind the Mic toggle.
            // Keys stand at console z -0.001 (toward the user); the face is at z +0.002. The halo goes between them,
            // just in front of the face, so it shows as a rim around the key and never covers the cap.
            // The halo is a unit Cylinder rotated 90 deg about X (its height axis points at the user): uniform scale made it
            // 73 mm deep, swallowing the key. Flat disc: 38 mm across (the key housing is 35 mm), 2 mm thick.
            Vector3 want = c.TransformPoint(new Vector3(ColX, Row1Y, 0.0012f));
            halo.position = want;
            var hud = slot.GetComponentInChildren<MuseDesk.Voice.VoiceHud>(true);
            if (hud != null) { Undo.RecordObject(hud, "Dock tidy"); hud.haloRestScale = new Vector3(0.038f, 0.001f, 0.038f); halo.localScale = hud.haloRestScale; EditorUtility.SetDirty(hud); }
            sb.AppendLine("  mic level halo moved behind the Mic toggle");
        }

        // Every key's press visual re-reads its rest offset at the new scale.
        foreach (var k in new[] { mic, motion }) k.BroadcastMessage("SyncToCurrentScale", SendMessageOptions.DontRequireReceiver);
        EditorSceneManager.MarkSceneDirty(c.gameObject.scene);
        sb.AppendLine("  scene saved=" + EditorSceneManager.SaveScene(c.gameObject.scene));
        return sb.ToString();
    }

    public static string Revert()
    {
        if (!File.Exists(StateFile)) return "DockTidyV1.Revert: no saved state";
        var c = Console(); if (c == null) return "no ModeConsole";
        var st = JsonUtility.FromJson<State>(File.ReadAllText(StateFile));
        int n = 0;
        foreach (var r in st.recs)
        {
            var t = c.Find(r.path); if (t == null) continue;
            Undo.RecordObject(t, "Dock tidy revert"); t.localPosition = r.lp; t.localScale = r.ls; n++;
        }
        EditorSceneManager.MarkSceneDirty(c.gameObject.scene);
        return "DockTidyV1.Revert: restored " + n + "; saved=" + EditorSceneManager.SaveScene(c.gameObject.scene);
    }
}
