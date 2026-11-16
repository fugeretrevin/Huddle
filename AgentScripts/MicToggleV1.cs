using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using TMPro;
using MuseDesk.Voice;
using MuseDesk.Workspace;

/// <summary>
/// Hands-free voice: adds the "MicToggleKey" poke key to the settings panel (the console that holds the Focus key and the
/// Calm motion key). It is a clone of the Focus key (FocusToggle), same size as the Calm motion key, seated in the slot
/// BELOW the Calm motion key's slot (the same parent, clear of the console plate). It carries MicToggleKey, which flips
/// VoiceCommandCenter's hands-free mic. The key is never hidden: it is the privacy control.
///
///   unity command script run --file AgentScripts/MicToggleV1.cs --entry MicToggleV1.Apply
///   (Revert deactivates and renames it "MicToggleKey_old"; Apply brings it back. Idempotent; saves the scene.)
///
/// Copying an ISDK interactable: after Instantiate, any _pointableElement / _rigidbody reference that still points outside the
/// new key is repointed to the key's own components, and the select event is rewired to MicToggleKey.Press.
/// Nothing else in the scene is touched.
/// </summary>
public static class MicToggleV1
{
    const string KeyName = "MicToggleKey", OldName = "MicToggleKey_old";
    const string MatDir = "Assets/MuseDesk/Art/Materials/UI/";
    const float Gap = 0.006f;
    const float SizeFactor = 0.62f;

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

    static bool Inside(Transform key, Object o)
    {
        var c = o as Component;
        return c != null && (c.transform == key || c.transform.IsChildOf(key));
    }

    static void RepointInternals(Transform key, StringBuilder sb)
    {
        var rb = key.GetComponentInChildren<Rigidbody>(true);
        var pointable = key.GetComponentsInChildren<MonoBehaviour>(true)
            .FirstOrDefault(m => m != null && m.GetType().GetInterfaces().Any(i => i.Name == "IPointableElement"));
        foreach (var mb in key.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (mb == null) continue;
            var so = new SerializedObject(mb);
            bool changed = false;
            var p = so.FindProperty("_pointableElement");
            if (p != null && p.propertyType == SerializedPropertyType.ObjectReference && pointable != null && !Inside(key, p.objectReferenceValue))
            { p.objectReferenceValue = pointable; changed = true; sb.AppendLine("  repointed _pointableElement on " + mb.GetType().Name); }
            var r = so.FindProperty("_rigidbody");
            if (r != null && r.propertyType == SerializedPropertyType.ObjectReference && rb != null && !Inside(key, r.objectReferenceValue))
            { r.objectReferenceValue = rb; changed = true; sb.AppendLine("  repointed _rigidbody on " + mb.GetType().Name); }
            if (changed) so.ApplyModifiedProperties();
        }
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
        if (EditorApplication.isPlayingOrWillChangePlaymode) return "MicToggleV1: stop Play Mode first.\n";
        var scene = EditorSceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded) return "MicToggleV1: no open scene.\n";
        var sb = new StringBuilder();
        sb.AppendLine("== MicToggleV1 ==");

        var focus = FindAny("FocusToggle");
        if (focus == null || focus.parent == null) return sb.Append("FocusToggle not found (nothing to clone); nothing changed.\n").ToString();

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
        key.localScale = focus.localScale * SizeFactor;

        // The Calm motion key's slot (just under the console plate), then one slot lower for this key.
        float dy = 0.034f;
        Transform fh = Under(focus, "Housing3D");
        if (fh != null && OwnMeshBounds(fh, focus.parent, out var hmn, out var hmx)) dy = (hmx.y - hmn.y) * 0.5f * (1f + SizeFactor) + Gap;
        Vector3 slot = focus.localPosition + new Vector3(0f, -dy, 0f);
        float keyH = 0.030f;
        Transform body = Under(focus.parent, "Body");
        if (body != null && fh != null && OwnMeshBounds(body, focus.parent, out var bmn, out _) && OwnMeshBounds(fh, focus.parent, out var h2, out var h3))
        {
            float keyHalf = (h3.y - h2.y) * 0.5f * SizeFactor;
            keyH = keyHalf * 2f;
            slot = new Vector3(focus.localPosition.x, bmn.y - 0.005f - keyHalf, focus.localPosition.z);
        }
        key.localPosition = slot + new Vector3(0f, -(keyH + Gap), 0f);

        sb.AppendLine("  components on the clone root: " + string.Join(", ", key.GetComponents<Component>().Where(c => c != null).Select(c => c.GetType().Name)));
        foreach (var mb in key.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (mb == null) continue;
            string tn = mb.GetType().Name;
            if (mb is MicToggleKey) continue;
            if (tn.Contains("Focus") || tn == "MotionToggleKey") { sb.AppendLine("  removed copied component " + tn + " on " + mb.name); Object.DestroyImmediate(mb); }
        }

        var cap = Under(key, "Cap3D");
        var capR = cap != null ? cap.GetComponent<MeshRenderer>() : null;
        var label = key.GetComponentInChildren<TMP_Text>(true);
        var visual = key.GetComponent<KeyStateVisual>();
        var matOn = AssetDatabase.LoadAssetAtPath<Material>(MatDir + "Key_ConsoleActive.mat");
        var matOff = AssetDatabase.LoadAssetAtPath<Material>(MatDir + "Key_ConsoleIdle.mat");
        if (capR == null || label == null || matOn == null || matOff == null)
            sb.AppendLine($"  WARNING: cap={(capR != null)} label={(label != null)} matOn={(matOn != null)} matOff={(matOff != null)}");

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
                label.text = MicPreference.KeyText(MicPhase.Muted);
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
                sb.AppendLine($"  label 5.5 mm bold, two lines, in a cap {capW * 1000f:F1} x {capH * 1000f:F1} mm (longest word 'hearing'/'offline' must fit {0.94f * capW * 1000f:F1} mm)");
            }
        }
        if (capR != null && matOff != null) { capR.sharedMaterial = matOff; EditorUtility.SetDirty(capR); }
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

        var tk = key.GetComponent<MicToggleKey>();
        if (tk == null) tk = Undo.AddComponent<MicToggleKey>(key.gameObject);
        tk.center = Object.FindFirstObjectByType<VoiceCommandCenter>(FindObjectsInactive.Include);
        tk.label = label; tk.visual = visual; tk.cap = capR; tk.capOn = matOn; tk.capOff = matOff;
        EditorUtility.SetDirty(tk);
        sb.AppendLine("  VoiceCommandCenter found=" + (tk.center != null) + (tk.center == null ? " (the key will look for it at run time and read 'Voice offline' until it exists)" : ""));

        RepointInternals(key, sb);
        Rewire(key, tk, nameof(MicToggleKey.Press));

        EditorUtility.SetDirty(key.gameObject);
        sb.AppendLine($"  {(created ? "created" : "updated")} {KeyName} under {focus.parent.name}, local {key.localPosition.x:F3},{key.localPosition.y:F3},{key.localPosition.z:F3}; poke -> MicToggleKey.Press; always shown");
        EditorSceneManager.MarkSceneDirty(scene);
        bool saved = EditorSceneManager.SaveOpenScenes();
        sb.AppendLine("  scene saved=" + saved);
        return sb.ToString();
    }

    public static string Revert()
    {
        var key = FindAny(KeyName);
        if (key == null) return "MicToggleV1.Revert: no " + KeyName;
        key.name = OldName;
        key.gameObject.SetActive(false);
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        EditorSceneManager.SaveOpenScenes();
        return "MicToggleV1.Revert: " + KeyName + " deactivated and renamed " + OldName;
    }
}
