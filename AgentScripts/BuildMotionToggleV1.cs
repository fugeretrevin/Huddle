using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using TMPro;
using MuseDesk.Workspace;

/// <summary>
/// Polish S9c: adds the in-app "Calm motion" (Reduced Motion) poke key "MotionToggleKey", a clone of the Focus key
/// (FocusToggle) seated beside it in the same parent, same size and rotation, one housing-height below it.
/// It carries MotionToggleKey, which hides it by scale unless the ReleaseConfig flag reducedMotionToggle is on
/// (the committed config has it off, so the release layout is unchanged), and wires the poke to MotionToggleKey.Press.
///
///   unity command script run --file AgentScripts/BuildMotionToggleV1.cs --entry BuildMotionToggleV1.Apply
///   (Revert deactivates and renames it "MotionToggleKey_old"; Apply brings it back. Idempotent; saves the scene.)
///
/// It touches nothing else: the Focus key, the console and every other object are left as they are.
/// </summary>
public static class BuildMotionToggleV1
{
    const string KeyName = "MotionToggleKey", OldName = "MotionToggleKey_old";
    const string MatDir = "Assets/MuseDesk/Art/Materials/UI/";
    const float Gap = 0.006f;           // metres between the Focus housing and this key's housing
    const float SizeFactor = 0.62f;     // of the Focus key's scale (the Focus cap is 49 mm; this one comes out near 30 mm)

    static Transform FindAny(string name) =>
        EditorSceneManager.GetActiveScene().GetRootGameObjects()
            .SelectMany(r => r.GetComponentsInChildren<Transform>(true)).FirstOrDefault(t => t != null && t.name == name);

    static Transform Under(Transform root, string name) =>
        root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t != null && t != root && t.name == name);

    static bool OwnMeshBounds(Transform part, Transform space, out Vector3 mn, out Vector3 mx)
    {
        mn = Vector3.positiveInfinity; mx = Vector3.negativeInfinity;
        var f = part != null ? part.GetComponent<MeshFilter>() : null;
        if (f == null || f.sharedMesh == null) return false;
        var b = f.sharedMesh.bounds;
        for (int i = 0; i < 8; i++)
        {
            var c = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
            var l = space.InverseTransformPoint(part.TransformPoint(c));
            mn = Vector3.Min(mn, l); mx = Vector3.Max(mx, l);
        }
        return true;
    }

    static void Rewire(Transform key, Object target, string method)
    {
        foreach (var mb in key.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (mb == null || mb.GetType().Name != "InteractableUnityEventWrapper") continue;
            var so = new SerializedObject(mb);
            var calls = so.FindProperty("_whenSelect.m_PersistentCalls.m_Calls");
            if (calls == null) continue;
            if (calls.arraySize == 0) calls.InsertArrayElementAtIndex(0);
            for (int i = 0; i < calls.arraySize; i++)
            {
                var c = calls.GetArrayElementAtIndex(i);
                c.FindPropertyRelative("m_Target").objectReferenceValue = target;
                c.FindPropertyRelative("m_TargetAssemblyTypeName").stringValue = target.GetType().AssemblyQualifiedName;
                c.FindPropertyRelative("m_MethodName").stringValue = method;
                c.FindPropertyRelative("m_Mode").intValue = 1;
                c.FindPropertyRelative("m_CallState").intValue = 2;
            }
            so.ApplyModifiedProperties();
        }
    }

    public static string Apply()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return "BuildMotionToggleV1: stop Play Mode first.\n";
        var scene = EditorSceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded) return "BuildMotionToggleV1: no open scene.\n";
        var sb = new StringBuilder();
        sb.AppendLine("== BuildMotionToggleV1 ==");

        var focus = FindAny("FocusToggle");
        if (focus == null || focus.parent == null) return sb.Append("FocusToggle not found (nothing to clone); nothing changed.\n").ToString();

        // Existing (or switched-off) key?
        Transform key = focus.parent.Find(KeyName);
        bool created = false;
        if (key == null)
        {
            var old = focus.parent.Find(OldName);
            if (old != null) { key = old; key.name = KeyName; key.gameObject.SetActive(true); sb.AppendLine("  revived " + OldName); }
        }
        if (key == null)
        {
            var go = Object.Instantiate(focus.gameObject, focus.parent);
            Undo.RegisterCreatedObjectUndo(go, KeyName);
            go.name = KeyName;
            key = go.transform;
            created = true;
            var face = go.GetComponent<FaceUser>();
            if (face != null) Object.DestroyImmediate(face);
        }
        key.gameObject.layer = focus.gameObject.layer;
        key.gameObject.SetActive(true);
        key.localRotation = focus.localRotation;
        key.localScale = focus.localScale * SizeFactor;            // a compact secondary key: about the size of the console's keys

        // Just below the Focus key (centres half a housing of each apart plus a gap), in the parent's local frame.
        float dy = 0.034f;
        Transform fh = Under(focus, "Housing3D");
        if (fh != null && OwnMeshBounds(fh, focus.parent, out var hmn, out var hmx)) dy = (hmx.y - hmn.y) * 0.5f * (1f + SizeFactor) + Gap;
        key.localPosition = focus.localPosition + new Vector3(0f, -dy, 0f);
        // Fully clear of the console plate, a few millimetres under its near edge (not half over it).
        Transform body = Under(focus.parent, "Body");
        if (body != null && fh != null && OwnMeshBounds(body, focus.parent, out var bmn, out _) && OwnMeshBounds(fh, focus.parent, out var h2, out var h3))
        {
            float keyHalf = (h3.y - h2.y) * 0.5f * SizeFactor;
            key.localPosition = new Vector3(focus.localPosition.x, bmn.y - 0.005f - keyHalf, focus.localPosition.z);
        }

        // Remove any component copied from the Focus key that is not part of the key's look (report them first).
        sb.AppendLine("  components on the clone root: " + string.Join(", ", key.GetComponents<Component>().Where(c => c != null).Select(c => c.GetType().Name)));
        foreach (var mb in key.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (mb == null) continue;
            string tn = mb.GetType().Name;
            if (mb is MotionToggleKey) continue;
            if (tn.Contains("Focus")) { sb.AppendLine("  removed copied component " + tn + " on " + mb.name); Object.DestroyImmediate(mb); }
        }

        var cap = Under(key, "Cap3D");
        var capR = cap != null ? cap.GetComponent<MeshRenderer>() : null;
        var label = key.GetComponentInChildren<TMP_Text>(true);
        var visual = key.GetComponent<KeyStateVisual>();
        var matOn = AssetDatabase.LoadAssetAtPath<Material>(MatDir + "Key_ConsoleActive.mat");
        var matOff = AssetDatabase.LoadAssetAtPath<Material>(MatDir + "Key_ConsoleIdle.mat");
        if (capR == null || label == null || matOn == null || matOff == null)
            sb.AppendLine($"  WARNING: cap={(capR != null)} label={(label != null)} matOn={(matOn != null)} matOff={(matOff != null)}");

        // Label: three lines at 5.5 mm (the type floor), Ink bold, centred on the cap, no autosize (it must never shrink below 5.5).
        if (label != null && cap != null && OwnMeshBounds(cap, key, out var cmn, out var cmx))
        {
            float s = Mathf.Abs(label.transform.lossyScale.x);
            float ks = Mathf.Abs(key.lossyScale.x);
            float capW = (cmx.x - cmn.x) * ks, capH = (cmx.y - cmn.y) * ks;
            if (s > 1e-7f)
            {
                Vector3 centre = key.TransformPoint((cmn + cmx) * 0.5f);
                Vector3 d = label.transform.position - centre;
                label.transform.position -= key.right * Vector3.Dot(d, key.right) + key.up * Vector3.Dot(d, key.up);
                label.text = MuseDesk.Motion.ReducedMotionPreference.KeyText(false);
                label.enableAutoSizing = false;
                label.fontSize = 5.5f * 0.01f / s;
                label.rectTransform.pivot = new Vector2(0.5f, 0.5f);
                label.rectTransform.sizeDelta = new Vector2(0.94f * capW / s, 0.92f * capH / s);
                label.textWrappingMode = TextWrappingModes.NoWrap;
                label.overflowMode = TextOverflowModes.Overflow;
                label.alignment = TextAlignmentOptions.Center;
                label.fontStyle = FontStyles.Bold;
                label.color = new Color32(0x1F, 0x25, 0x30, 0xFF);
                label.lineSpacing = -8f;
                sb.AppendLine($"  label 5.5 mm bold, three lines, in a cap {capW * 1000f:F1} x {capH * 1000f:F1} mm");
            }
        }
        if (capR != null && matOff != null) { capR.sharedMaterial = matOff; EditorUtility.SetDirty(capR); }
        // The SDK's colour editors would erase the key's own tint (as the console builder does).
        foreach (var mb in key.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (mb == null) continue;
            string tn = mb.GetType().Name;
            if ((tn == "InteractableColorVisual" || (tn == "MaterialPropertyBlockEditor" && capR != null && mb.transform == capR.transform)) && mb.enabled)
            { mb.enabled = false; EditorUtility.SetDirty(mb); }
        }
        if (visual == null) visual = Undo.AddComponent<KeyStateVisual>(key.gameObject);
        visual.capRenderer = capR; visual.label = label; visual.rimRenderer = null;
        EditorUtility.SetDirty(visual);

        var tk = key.GetComponent<MotionToggleKey>();
        if (tk == null) tk = Undo.AddComponent<MotionToggleKey>(key.gameObject);
        tk.keyRoot = key; tk.shownScale = key.localScale; tk.label = label; tk.visual = visual; tk.cap = capR;
        tk.capOn = matOn; tk.capOff = matOff;
        EditorUtility.SetDirty(tk);
        Rewire(key, tk, nameof(MotionToggleKey.Press));

        // Saved hidden by scale: the committed ReleaseConfig has reducedMotionToggle off.
        key.localScale = tk.shownScale * MuseDesk.Workspace.ScaleEase.HiddenFactor;
        EditorUtility.SetDirty(key.gameObject);

        sb.AppendLine($"  {(created ? "created" : "updated")} {KeyName} under {focus.parent.name}, local {key.localPosition.x:F3},{key.localPosition.y:F3},{key.localPosition.z:F3} " +
                      $"(Focus key at {focus.localPosition.x:F3},{focus.localPosition.y:F3},{focus.localPosition.z:F3}); shown scale {tk.shownScale.x:F4}; saved hidden; poke -> MotionToggleKey.Press");
        EditorSceneManager.MarkSceneDirty(scene);
        bool saved = EditorSceneManager.SaveOpenScenes();
        sb.AppendLine("  scene saved=" + saved);
        return sb.ToString();
    }

    public static string Revert()
    {
        var key = FindAny(KeyName);
        if (key == null) return "BuildMotionToggleV1.Revert: no " + KeyName;
        key.name = OldName;
        key.gameObject.SetActive(false);
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        EditorSceneManager.SaveOpenScenes();
        return "BuildMotionToggleV1.Revert: " + KeyName + " deactivated and renamed " + OldName;
    }
}
