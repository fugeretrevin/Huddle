using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using MuseDesk.Spatial;

/// <summary>
/// Copy fixes in the open scene (Assets/MuseDesk/Scenes/MainMRScene.unity). Idempotent (run twice = same result);
/// old values are kept in Library/CopyFixV1.json (first run wins) so Revert() puts them back. Saves the scene.
///
///  1. Every TMP object named "StampHint" (under DecisionDocument/StampZone): "Use a stamp instead of a key" -> "Stamp to approve or reject".
///  2. PlacementGuide (scene-root "LookGuide"): ONE source of truth for the preview keys. While a Floor or Ceiling preview is open the
///     unified dock (ModeConsole) already shows Place / Next spot / Cancel, so the card's own Place here / Other spot / Cancel keys
///     are hidden: each GuideKey.shownScale is set to ~0.0001 of its old value (the guide still eases them "in", to an invisible
///     size, and its pokes stay off the finger's reach). Nothing is SetActive(false)'d. The card's Start here key is untouched.
///  3. PlacementGuide body words now name the dock key: "Press Place ..." (the key is "Place", not "Place here"), and the ceiling
///     body "Confirm to mount it here, or Next spot" -> "Press Place to mount it here".
///
///   powershell scripts\run_guarded.ps1 -File AgentScripts\CopyFixV1.cs -Entry CopyFixV1.Apply
/// Revert() restores everything from the saved file. The dock's own status line text is code (SpatialDirectorCore "Press Place here...")
/// and is not touched here.
/// </summary>
public static class CopyFixV1
{
    const string SavePath = "Library/CopyFixV1.json";
    const string StampHintOld = "Use a stamp instead of a key", StampHintNew = "Stamp to approve or reject";
    const float Shrink = 0.0001f;

    [Serializable] class TextRec { public string path; public string old; }
    [Serializable] class KeyRec { public string field; public Vector3 shown; }
    [Serializable] class Saved
    {
        public List<TextRec> stampHints = new List<TextRec>();
        public List<KeyRec> keys = new List<KeyRec>();
        public bool haveBodies;
        public string floorPreviewBody, floorDraggingBody, floorReadyBody, ceilingPreviewBody;
    }

    static Saved Load()
    {
        try { if (File.Exists(SavePath)) return JsonUtility.FromJson<Saved>(File.ReadAllText(SavePath)) ?? new Saved(); }
        catch (Exception) { }
        return new Saved();
    }

    static void Save(Saved s) { File.WriteAllText(SavePath, JsonUtility.ToJson(s, true)); }

    static string PathOf(Transform t)
    {
        var sb = new StringBuilder(t.name);
        for (var p = t.parent; p != null; p = p.parent) sb.Insert(0, p.name + "/");
        return sb.ToString();
    }

    static PlacementGuide FindGuide()
    {
        foreach (var g in UnityEngine.Object.FindObjectsByType<PlacementGuide>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (g != null && g.transform.parent == null && g.name == "LookGuide") return g;
        return null;
    }

    static PlacementGuide.GuideKey KeyOf(PlacementGuide g, string field)
    {
        switch (field)
        {
            case "placeKey": return g.placeKey;
            case "anotherKey": return g.anotherKey;
            case "cancelKey": return g.cancelKey;
        }
        return null;
    }

    public static string Apply()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return "CopyFixV1: stop Play Mode first.\n";
        var scene = EditorSceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded) return "CopyFixV1: no open scene.\n";
        var sb = new StringBuilder("== CopyFixV1 ==\n");
        var saved = Load();

        // 1. stamp hint text
        int hints = 0;
        foreach (var t in Resources.FindObjectsOfTypeAll<TMP_Text>())
        {
            if (t == null || t.name != "StampHint" || !t.gameObject.scene.IsValid() || t.gameObject.scene != scene) continue;
            string path = PathOf(t.transform);
            if (saved.stampHints.Find(r => r.path == path) == null) saved.stampHints.Add(new TextRec { path = path, old = t.text });
            if (t.text != StampHintNew)
            {
                Undo.RecordObject(t, "Stamp hint copy");
                t.text = StampHintNew;
                EditorUtility.SetDirty(t);
            }
            hints++;
        }
        sb.AppendLine("  StampHint objects set to \"" + StampHintNew + "\": " + hints + (hints == 0 ? "  (none found: paper not built?)" : ""));

        // 2 + 3. guide card keys and words
        var guide = FindGuide();
        if (guide == null) sb.AppendLine("  LookGuide/PlacementGuide not found: guide card not changed.");
        else
        {
            Undo.RecordObject(guide, "Hide card preview keys");
            foreach (string f in new[] { "placeKey", "anotherKey", "cancelKey" })
            {
                var k = KeyOf(guide, f);
                if (k == null) continue;
                if (saved.keys.Find(r => r.field == f) == null) saved.keys.Add(new KeyRec { field = f, shown = k.shownScale });
                // The first run's value is the truth: shrink from the saved original, so a second run is a no-op.
                k.shownScale = saved.keys.Find(r => r.field == f).shown * Shrink;
                sb.AppendLine("  " + f + " (" + (k.root != null ? k.root.name : "no root") + ") shownScale -> " + k.shownScale.x.ToString("G3") + " (hidden; the dock owns Place / Next spot / Cancel)");
            }
            if (!saved.haveBodies)
            {
                saved.haveBodies = true;
                saved.floorPreviewBody = guide.floorPreviewBody; saved.floorDraggingBody = guide.floorDraggingBody;
                saved.floorReadyBody = guide.floorReadyBody; saved.ceilingPreviewBody = guide.ceilingPreviewBody;
            }
            guide.floorPreviewBody = "Press Place, or drag the outline";
            guide.floorDraggingBody = "Let go on a clear spot, then press Place";
            guide.floorReadyBody = "Press Place to move it here";
            guide.ceilingPreviewBody = "Press Place to mount it here";
            sb.AppendLine("  guide words: '" + guide.floorPreviewBody + "' / '" + guide.ceilingPreviewBody + "'");
            EditorUtility.SetDirty(guide);
        }

        Save(saved);
        EditorSceneManager.MarkSceneDirty(scene);
        sb.AppendLine("  scene saved=" + EditorSceneManager.SaveScene(scene));
        return sb.ToString();
    }

    public static string Revert()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return "CopyFixV1.Revert: stop Play Mode first.\n";
        var scene = EditorSceneManager.GetActiveScene();
        if (!File.Exists(SavePath)) return "CopyFixV1.Revert: " + SavePath + " not found; nothing to restore.\n";
        var saved = Load();
        int n = 0;
        foreach (var t in Resources.FindObjectsOfTypeAll<TMP_Text>())
        {
            if (t == null || t.name != "StampHint" || !t.gameObject.scene.IsValid() || t.gameObject.scene != scene) continue;
            var rec = saved.stampHints.Find(r => r.path == PathOf(t.transform));
            if (rec == null) continue;
            Undo.RecordObject(t, "Revert stamp hint");
            t.text = rec.old; EditorUtility.SetDirty(t); n++;
        }
        var guide = FindGuide();
        if (guide != null)
        {
            Undo.RecordObject(guide, "Revert guide copy");
            foreach (var r in saved.keys) { var k = KeyOf(guide, r.field); if (k != null) k.shownScale = r.shown; }
            if (saved.haveBodies)
            {
                guide.floorPreviewBody = saved.floorPreviewBody; guide.floorDraggingBody = saved.floorDraggingBody;
                guide.floorReadyBody = saved.floorReadyBody; guide.ceilingPreviewBody = saved.ceilingPreviewBody;
            }
            EditorUtility.SetDirty(guide);
        }
        File.Delete(SavePath);
        EditorSceneManager.MarkSceneDirty(scene);
        bool ok = EditorSceneManager.SaveScene(scene);
        return "CopyFixV1.Revert: " + n + " StampHint text(s) restored; guide keys and words restored; scene saved=" + ok + "\n";
    }
}
