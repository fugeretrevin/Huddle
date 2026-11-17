using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class AuditCardGrabDetail
{
    public static string Run()
    {
        var sb = new StringBuilder();
        var all = SceneManager.GetActiveScene().GetRootGameObjects()
            .SelectMany(r => r.GetComponentsInChildren<Transform>(true)).ToArray();

        var bp = all.FirstOrDefault(x => x.name == "TaskBoard")?.GetComponents<MonoBehaviour>().FirstOrDefault(m => m.GetType().Name == "BoardPresenter");
        if (bp != null)
        {
            var so = new SerializedObject(bp);
            foreach (var p in new[] { "cardPrefab", "backlogRegion", "workingRegion", "needsMeRegion", "completedRegion", "cardSpacing", "columns" })
            {
                var prop = so.FindProperty(p);
                sb.AppendLine($"BP.{p} = {(prop.propertyType == SerializedPropertyType.ObjectReference ? prop.objectReferenceValue?.name : prop.propertyType == SerializedPropertyType.Vector2 ? prop.vector2Value.ToString("F3") : prop.intValue.ToString())}");
            }
            foreach (var name in new[] { "Region_Backlog", "Region_Working", "Region_NeedsMe", "Region_Completed", "Backlog", "Working", "NeedsMe", "Completed" })
            {
                var r = all.FirstOrDefault(x => x.name == name && x.IsChildOf(bp.transform));
                if (r != null) sb.AppendLine($"  Region {name}: world={r.position:F3} lp={r.localPosition:F3}");
            }
        }

        var ct = all.FirstOrDefault(x => x.name == "CardTemplate");
        if (ct != null)
        {
            foreach (var c in ct.GetComponents<MonoBehaviour>())
            {
                if (c != null && c.GetType().Name == "ReRegisterInteractable")
                {
                    var so = new SerializedObject(c);
                    var arr = so.FindProperty("interactables");
                    sb.AppendLine($"ReRegisterInteractable len={arr.arraySize}");
                    for (int i = 0; i < arr.arraySize; i++)
                        sb.AppendLine($"  [{i}] = {arr.GetArrayElementAtIndex(i).objectReferenceValue?.GetType().Name ?? "null"}");
                }
            }
            var sh1 = ct.Find("SH_PokePauseResume");
            var sh2 = ct.Find("SH_PokePriority");
            sb.AppendLine($"Card children: SH_PokePauseResume={(sh1 != null ? sh1.localPosition.ToString("F3") : "none")}, SH_PokePriority={(sh2 != null ? sh2.localPosition.ToString("F3") : "none")}");
        }

        var dd = all.FirstOrDefault(x => x.name == "DecisionDocument");
        if (dd != null)
        {
            var bc = dd.GetComponent<BoxCollider>();
            sb.AppendLine($"DecisionDocument BoxCollider center={bc?.center:F3} size={bc?.size:F3} trig={bc?.isTrigger}");
            foreach (var c in dd.GetComponents<MonoBehaviour>())
            {
                if (c != null && c.GetType().Name == "ReRegisterInteractable")
                {
                    var so = new SerializedObject(c);
                    var arr = so.FindProperty("interactables");
                    sb.AppendLine($"DD ReRegisterInteractable len={arr.arraySize}");
                    for (int i = 0; i < arr.arraySize; i++)
                        sb.AppendLine($"  [{i}] = {arr.GetArrayElementAtIndex(i).objectReferenceValue?.GetType().Name ?? "null"}");
                }
            }
        }

        return sb.ToString();
    }
}

