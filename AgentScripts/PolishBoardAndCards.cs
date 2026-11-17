using System.Collections.Generic;
using System.Linq;
using System.Text;
using MuseDesk.Phase2.Board;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class PolishBoardAndCards
{
    static void StyleLabel(Transform card, string name, Vector3 lp, Vector2 box, float minF, float maxF,
                           Color col, FontStyles style, TextAlignmentOptions align, bool wrap)
    {
        var t = card.Find(name);
        if (t == null) return;
        t.localPosition = lp;
        t.localScale = Vector3.one * 0.01f;
        var tmp = t.GetComponent<TMP_Text>();
        if (tmp == null) return;
        tmp.rectTransform.sizeDelta = box;
        tmp.enableAutoSizing = minF < maxF;
        tmp.fontSizeMin = minF;
        tmp.fontSizeMax = maxF;
        tmp.fontSize = maxF;
        tmp.color = col;
        tmp.fontStyle = style;
        tmp.alignment = align;
        tmp.textWrappingMode = wrap ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
        tmp.overflowMode = TextOverflowModes.Truncate;
        EditorUtility.SetDirty(tmp);
    }

    public static string Run()
    {
        var sb = new StringBuilder();
        var scene = EditorSceneManager.GetActiveScene();
        var ws = GameObject.Find("WorkspaceRoot")?.transform;
        var tb = ws != null ? ws.Find("TaskBoard") : null;
        if (tb == null) return "TaskBoard not found";

        // 1. Center "TASK BOARD" inside top pediment and center column labels in their colored pills
        var titleT = tb.Find("Title");
        var titleTmp = titleT != null ? titleT.GetComponent<TMP_Text>() : null;
        if (titleTmp != null)
        {
            titleT.localPosition = new Vector3(0f, 0.240f, -0.014f);
            titleTmp.color = new Color(0.98f, 0.95f, 0.88f, 1f);
            titleTmp.fontSize = 0.24f;
            titleTmp.alignment = TextAlignmentOptions.Center;
            EditorUtility.SetDirty(titleTmp);
        }
        foreach (var col in new (string name, float x)[]
        {
            ("Label_Backlog", -0.237f),
            ("Label_Working", -0.079f),
            ("Label_NeedsMe", 0.079f),
            ("Label_Completed", 0.237f),
        })
        {
            var lt = tb.Find(col.name);
            var ltmp = lt != null ? lt.GetComponent<TMP_Text>() : null;
            if (ltmp != null)
            {
                lt.localPosition = new Vector3(col.x, 0.167f, -0.016f);
                ltmp.fontSize = 0.20f;
                ltmp.alignment = TextAlignmentOptions.Center;
                EditorUtility.SetDirty(ltmp);
            }
        }
        sb.AppendLine("TaskBoard: centered TASK BOARD in pediment and aligned 4 column labels inside pills.");

        // 2. Clean CardTemplate overlapping sub-props and align typography with TaskCard_Visual
        var cardTemplate = tb.Find("CardTemplate");
        if (cardTemplate != null)
        {
            var vis = cardTemplate.Find("TaskCard_Visual");
            if (vis != null)
            {
                vis.localPosition = new Vector3(0f, 0f, 0.003f);
                vis.localRotation = Quaternion.Euler(0f, 180f, 0f);
                vis.localScale = new Vector3(1.12f, 1.12f, 1.0f);
            }

            foreach (var clutterName in new[] { "OwnerIcon_Visual", "PriorityBadge_Visual", "StatusMarker_Visual" })
            {
                var c = cardTemplate.Find(clutterName);
                if (c != null) c.gameObject.SetActive(false);
            }

            foreach (var qName in new[] { "Shadow", "Face", "HeaderStrip" })
            {
                var q = cardTemplate.Find(qName);
                var mr = q != null ? q.GetComponent<Renderer>() : null;
                if (mr != null) mr.enabled = false;
            }

            // Route TaskCardView.statusMarker to the 3D Card_StatusMarker dome on TaskCard_Visual and hide the flat SH_StatusDot quad
            var cardMarker = vis != null ? vis.Find("Card_StatusMarker")?.GetComponent<Renderer>() : null;
            var dot = cardTemplate.Find("SH_StatusDot");
            var tcv = cardTemplate.GetComponent<TaskCardView>();
            if (cardMarker != null && tcv != null)
            {
                tcv.statusMarker = cardMarker;
                EditorUtility.SetDirty(tcv);
                if (dot != null) dot.gameObject.SetActive(false);
            }
            else if (dot != null)
            {
                dot.localPosition = new Vector3(-0.032f, -0.019f, -0.0055f);
            }

            const float z = -0.0055f;
            StyleLabel(cardTemplate, "SH_Role",     new Vector3(-0.009f,  0.0218f, z), new Vector2(4.2f, 0.70f), 2.8f, 3.8f, new Color(0.99f, 0.98f, 0.95f), FontStyles.Bold,   TextAlignmentOptions.Left,   false);
            StyleLabel(cardTemplate, "SH_Priority", new Vector3( 0.018f,  0.0218f, z), new Vector2(1.4f, 0.70f), 2.8f, 3.8f, new Color(1.00f, 0.94f, 0.65f), FontStyles.Bold,   TextAlignmentOptions.Right,  false);
            StyleLabel(cardTemplate, "Title",       new Vector3( 0.000f,  0.0040f, z), new Vector2(6.6f, 1.65f), 4.2f, 6.2f, new Color(0.10f, 0.11f, 0.15f), FontStyles.Bold,   TextAlignmentOptions.Center, true);
            StyleLabel(cardTemplate, "SH_State",    new Vector3( 0.000f, -0.0095f, z), new Vector2(5.0f, 0.80f), 3.0f, 4.2f, new Color(0.18f, 0.22f, 0.28f), FontStyles.Bold,   TextAlignmentOptions.Center, false);
            StyleLabel(cardTemplate, "SH_Source",   new Vector3( 0.000f, -0.0172f, z), new Vector2(4.8f, 0.60f), 2.4f, 3.2f, new Color(0.68f, 0.26f, 0.20f), FontStyles.Bold,   TextAlignmentOptions.Center, false);
            StyleLabel(cardTemplate, "SH_IdRev",    new Vector3( 0.012f, -0.0225f, z), new Vector2(4.2f, 0.45f), 1.8f, 2.2f, new Color(0.42f, 0.44f, 0.48f), FontStyles.Normal, TextAlignmentOptions.Right,  false);

            EditorUtility.SetDirty(cardTemplate.gameObject);
            sb.AppendLine("CardTemplate: deactivated overlapping corner tokens, disabled legacy quads, aligned typography.");
        }

        EditorSceneManager.MarkSceneDirty(scene);
        bool saved = EditorSceneManager.SaveScene(scene);
        sb.AppendLine($"scene saved={saved}");
        return sb.ToString();
    }
}
