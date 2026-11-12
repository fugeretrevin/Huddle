using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class DiagBoardText
{
    public static string Run()
    {
        var scene = EditorSceneManager.GetActiveScene();
        var all = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true)).ToList();
        var board = all.FirstOrDefault(t => t.name == "TaskBoard");
        if (board == null) return "no board";
        var sb = new StringBuilder();
        sb.AppendLine($"TaskBoard lossyScale={board.lossyScale} localScale={board.localScale} pos={board.position}");
        foreach (var c in board.GetComponentsInChildren<Component>(true).Where(x => x != null && x.GetType().Name.StartsWith("TextMeshPro")))
        {
            var ty = c.GetType();
            var fs = ty.GetProperty("fontSize")?.GetValue(c);
            var au = ty.GetProperty("enableAutoSizing")?.GetValue(c);
            var txtP = ty.GetProperty("text")?.GetValue(c) as string ?? "";
            if (txtP.Length > 14) txtP = txtP.Substring(0, 14);
            var t = (c as Component).transform;
            sb.AppendLine($"  '{t.name}' text='{txtP}' fontSize={fs} autoSize={au} lossyScale={t.lossyScale}");
        }
        return sb.ToString();
    }
}
