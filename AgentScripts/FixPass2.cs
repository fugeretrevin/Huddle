using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// On-device feedback pass 2. Applies the three verified fixes:
///  1. Paper grab: BoxCollider.isTrigger = true (matches the proven-working cube; the ONLY diff).
///  2. Mirror: any FaceUser that sits on a TMP text and is still forward=true -> forward=false
///     (TMP readable face is -Z; confirmed by capture). Leaves eyes/characters alone.
///  3. Details button: rewire its poke _whenSelect from SessionController.ApprovePending()
///     (wrong!) to DocumentDeliveryController.OnToggleDetails().
/// </summary>
public static class FixPass2
{
    public static string Run()
    {
        var scene = EditorSceneManager.GetActiveScene();
        var all = scene.GetRootGameObjects()
            .SelectMany(r => r.GetComponentsInChildren<Transform>(true))
            .Select(t => t.gameObject).ToList();
        var sb = new StringBuilder();

        // ---- 1. Paper grab collider -> trigger ----
        var paper = all.FirstOrDefault(g => g.name == "DecisionDocument");
        if (paper != null)
        {
            foreach (var bc in paper.GetComponents<BoxCollider>())
            {
                if (!bc.isTrigger) { bc.isTrigger = true; sb.AppendLine($"GRAB: DecisionDocument BoxCollider.isTrigger -> true"); }
                else sb.AppendLine("GRAB: already trigger");
            }
        }
        else sb.AppendLine("GRAB: DecisionDocument not found");

        // ---- 2. Mirror: flip FaceUser on text billboards ----
        int flipped = 0;
        foreach (var go in all)
        {
            var fu = go.GetComponents<Component>().FirstOrDefault(c => c != null && c.GetType().Name == "FaceUser");
            if (fu == null) continue;
            bool hasText = go.GetComponents<Component>().Any(c => c != null &&
                (c.GetType().Name == "TextMeshPro" || c.GetType().Name == "TextMeshProUGUI" || c.GetType().Name == "TMP_Text"));
            if (!hasText) continue;
            var f = fu.GetType().GetField("forward", BindingFlags.Public | BindingFlags.Instance);
            if (f != null && (bool)f.GetValue(fu))
            {
                f.SetValue(fu, false);
                EditorUtility.SetDirty(fu);
                flipped++;
                sb.AppendLine($"MIRROR: {go.name} FaceUser.forward -> false");
            }
        }
        sb.AppendLine($"MIRROR: flipped {flipped} text billboard(s)");

        // ---- 3. Rewire Details button ----
        var delivery = all.SelectMany(g => g.GetComponents<Component>())
            .FirstOrDefault(c => c != null && c.GetType().Name == "DocumentDeliveryController");
        if (delivery == null) sb.AppendLine("DETAILS: DocumentDeliveryController not found");
        else
        {
            var btnWrap = all.FirstOrDefault(g => g.transform.parent != null &&
                            g.transform.parent.name == "Btn_Details")
                        ?? all.FirstOrDefault(g => g.name.Contains("Btn_Details"));
            // find the InteractableUnityEventWrapper under Btn_Details
            Component wrap = null;
            var detailsBtn = all.FirstOrDefault(g => g.name == "Btn_Details");
            if (detailsBtn != null)
            {
                wrap = detailsBtn.GetComponentsInChildren<Transform>(true)
                    .SelectMany(t => t.GetComponents<Component>())
                    .FirstOrDefault(c => c != null && c.GetType().Name == "InteractableUnityEventWrapper");
            }
            if (wrap == null) sb.AppendLine("DETAILS: Btn_Details event wrapper not found");
            else
            {
                var so = new SerializedObject(wrap);
                var calls = so.FindProperty("_whenSelect.m_PersistentCalls.m_Calls");
                if (calls == null || calls.arraySize == 0)
                {
                    if (calls != null) calls.arraySize = 1;
                }
                var call = calls.GetArrayElementAtIndex(0);
                call.FindPropertyRelative("m_Target").objectReferenceValue = delivery;
                var tm = call.FindPropertyRelative("m_TargetAssemblyTypeName");
                if (tm != null) tm.stringValue = "MuseDesk.Documents.DocumentDeliveryController, Assembly-CSharp";
                call.FindPropertyRelative("m_MethodName").stringValue = "OnToggleDetails";
                call.FindPropertyRelative("m_Mode").enumValueIndex = 1; // Void
                call.FindPropertyRelative("m_CallState").enumValueIndex = 2; // RuntimeOnly
                var args = call.FindPropertyRelative("m_Arguments");
                if (args != null)
                {
                    var objArg = args.FindPropertyRelative("m_ObjectArgument");
                    if (objArg != null) objArg.objectReferenceValue = null;
                }
                so.ApplyModifiedPropertiesWithoutUndo();
                sb.AppendLine("DETAILS: Btn_Details -> DocumentDeliveryController.OnToggleDetails()");
            }
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        sb.AppendLine("SAVED scene.");
        return sb.ToString();
    }
}
