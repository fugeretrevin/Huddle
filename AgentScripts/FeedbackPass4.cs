using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using TMPro;
using MuseDesk.Documents;

/// <summary>
/// Fixes from the fourth on-head pass (2026-11-12, late). Idempotent; saves the scene.
///   1. Keys: a fully pressed cap ended up about 1 cm INSIDE its housing (the housing sat in front of
///      the poke surface), so its face vanished. Every housing now sits behind the surface with its
///      rim just below where the cap face stops, so the cap is still proud at full press.
///   2. Paper: rides in the Researcher's right hand (position from the hand socket every frame).
///   3. Task cards overlapped: rows were 6 cm apart for 9 cm cards, and cards were wider than their
///      lanes. Cards are scaled to fit a lane, rows are spaced to the card height, lanes are centred
///      on the frame's own columns, and the board keys / dock move below the frame.
///   4. Column labels and the board title are centred on their header blocks / title rail and sit
///      flush on the surface (they floated about 2 cm in front, 7 mm low).
///   unity command script run --file AgentScripts/FeedbackPass4.cs --entry FeedbackPass4.Apply
/// </summary>
public static class FeedbackPass4
{
    static List<Transform> All() => EditorSceneManager.GetActiveScene().GetRootGameObjects()
        .SelectMany(r => r.GetComponentsInChildren<Transform>(true)).ToList();

    static bool OwnMeshBounds(Transform part, Transform space, out Vector3 mn, out Vector3 mx)
    {
        mn = Vector3.positiveInfinity; mx = Vector3.negativeInfinity;
        var f = part.GetComponent<MeshFilter>();
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

    /// <summary>Front-most (smallest z, board-local) surface of the frame meshes inside a rectangle.</summary>
    static bool FrontSurface(Transform frame, Transform board, Rect area, out float z)
    {
        z = float.MaxValue; bool any = false;
        foreach (var r in frame.GetComponentsInChildren<MeshRenderer>(true))
        {
            var f = r.GetComponent<MeshFilter>(); if (f == null || f.sharedMesh == null || !f.sharedMesh.isReadable) continue;
            foreach (var v in f.sharedMesh.vertices)
            {
                var l = board.InverseTransformPoint(r.transform.TransformPoint(v));
                if (area.Contains(new Vector2(l.x, l.y)) && l.z < z) { z = l.z; any = true; }
            }
        }
        return any;
    }

    public static string Apply()
    {
        var all = All();
        var sb = new StringBuilder();

        // ── 1. keys ──
        int keys = 0;
        foreach (var key in all)
        {
            if (key == null || !key.GetComponents<MonoBehaviour>().Any(m => m != null && m.GetType().Name == "PokeInteractable")) continue;
            var housing = key.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "Housing3D");
            var cap = key.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "Cap3D");
            var visual = key.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "ButtonVisual");
            if (housing == null || cap == null || visual == null) continue;
            // Rest offset: the cap body is 0.276 deep but used to travel 0.30, so no housing position
            // could both hide its back at rest and keep its face proud when pressed. With 0.22 of
            // travel the cap's back stays inside the housing at rest (no floating cap seen from the
            // side) and its face is still proud of the rim at full press.
            const float restZ = -0.22f;
            if (Mathf.Abs(visual.localPosition.z - restZ) > 1e-4f || Mathf.Abs(visual.localPosition.x) > 1e-4f || Mathf.Abs(visual.localPosition.y) > 1e-4f)
            {
                Undo.RecordObject(visual, "key rest offset");
                visual.localPosition = new Vector3(0f, 0f, restZ);
                EditorUtility.SetDirty(visual);
            }
            var guard = visual.GetComponent<MuseDesk.Workspace.PokeVisualRestGuard>();
            if (guard != null)
            {
                Undo.RecordObject(guard, "key rest offset");
                guard.restLocalPosition = visual.localPosition;
                guard.hasAuthoredRest = true;
                EditorUtility.SetDirty(guard);
            }
            if (!OwnMeshBounds(cap, key, out var cmn, out _) || !OwnMeshBounds(housing, key, out var hmn, out _)) continue;
            // The press visual travels from its rest offset to the surface plane (key-local z = 0).
            float travel = -key.InverseTransformPoint(visual.position).z;
            float capFacePressed = cmn.z + travel;          // where the cap's front face stops
            const float proud = 0.016f;                     // cap stays this far in front of the rim (0.8 mm on a 5 cm key)
            float shift = (capFacePressed + proud) - hmn.z; // move the rim to just behind the stopped face
            if (Mathf.Abs(shift) > 1e-4f)
            {
                Undo.RecordObject(housing, "housing depth");
                housing.position += key.TransformVector(new Vector3(0f, 0f, shift));
                EditorUtility.SetDirty(housing);
            }
            keys++;
        }
        sb.AppendLine($"keys: {keys} housings set so the cap face stops proud of the rim at full press");

        // ── 2. paper in the hand ──
        var doc = all.FirstOrDefault(t => t.name == "DecisionDocument");
        var researcher = all.FirstOrDefault(t => t.name == "Agent_Researcher");
        var held = doc != null ? doc.GetComponent<HeldPaperController>() : null;
        var hand = researcher != null ? researcher.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "RightHand_Attach") : null;
        if (held != null && hand != null)
        {
            Undo.RecordObject(held, "paper follows hand");
            held.followHand = hand;
            held.heldScale = 0.22f;                    // about 7.5 cm: a sheet a 31 cm tall agent can hold
            var cardQuad = doc.Find("Card");
            held.sheetHeight = cardQuad != null ? cardQuad.localScale.y : 0.34f;
            EditorUtility.SetDirty(held);
            sb.AppendLine($"paper: rides in {researcher.name}/RightHand_Attach (position), printed side kept toward the user; about {held.sheetHeight * held.heldScale * 100f:F1} cm");
        }
        else sb.AppendLine("SKIP paper: controller or RightHand_Attach not found");

        // ── 3 + 4. board ──
        var board = all.FirstOrDefault(t => t.name == "TaskBoard");
        var frame = board != null ? board.Find("KanbanFrame_Visual") : null;
        if (board != null && frame != null)
        {
            // Lanes, from the frame's own header blocks (board-local).
            var headers = frame.GetComponentsInChildren<MeshRenderer>(true).Where(r => r.name.StartsWith("Header_"))
                .Select(r => { OwnMeshBounds(r.transform, board, out var a, out var b); return (r, a, b); })
                .OrderBy(h => (h.a.x + h.b.x) * 0.5f).ToList();
            var labelNames = new[] { "Label_Backlog", "Label_Working", "Label_NeedsMe", "Label_Completed" };
            var regionNames = new[] { "Region_Backlog", "Region_Working", "Region_NeedsMe", "Region_Completed" };

            // Card footprint at unit scale (it is not symmetric about its pivot).
            var card = board.Find("CardTemplate");
            Vector3 cmn = Vector3.positiveInfinity, cmx = Vector3.negativeInfinity;
            foreach (var r in card.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (r.GetComponent<TMP_Text>() != null || !r.enabled) continue;
                bool shown = true; for (var t = r.transform; t != card; t = t.parent) if (!t.gameObject.activeSelf) shown = false;
                if (!shown || r.name == "HoverFrame") continue;
                if (!OwnMeshBounds(r.transform, card, out var a, out var b)) continue;
                cmn = Vector3.Min(cmn, a); cmx = Vector3.Max(cmx, b);
            }
            float unitW = cmx.x - cmn.x, unitH = cmx.y - cmn.y, unitCx = (cmx.x + cmn.x) * 0.5f, unitCy = (cmx.y + cmn.y) * 0.5f;

            if (headers.Count == 4)
            {
                float laneW = headers[0].b.x - headers[0].a.x;
                float headerBottom = headers[0].a.y, headerMidY = (headers[0].a.y + headers[0].b.y) * 0.5f, headerH = headers[0].b.y - headers[0].a.y;
                float scale = Mathf.Min(1.3f, (laneW - 0.006f) / unitW);          // fits the lane; 1.3 lets four rows fit the frame
                Undo.RecordObject(card, "card scale");
                card.localScale = Vector3.one * scale;
                EditorUtility.SetDirty(card);
                float cardH = unitH * scale;
                float pitch = cardH + 0.008f;                                      // 8 mm between cards

                var presenter = board.GetComponent("BoardPresenter");
                var pso = new SerializedObject(presenter);
                pso.FindProperty("cardSpacing").vector2Value = new Vector2(laneW + 0.018f, pitch);
                pso.FindProperty("columns").intValue = 1;
                pso.ApplyModifiedProperties();

                for (int i = 0; i < 4; i++)
                {
                    float laneCx = (headers[i].a.x + headers[i].b.x) * 0.5f;
                    var region = board.Find(regionNames[i]);
                    if (region != null)
                    {
                        Undo.RecordObject(region, "lane");
                        // The card's pivot is not its centre: place the pivot so the FOOTPRINT is centred in the lane.
                        region.localPosition = new Vector3(laneCx - unitCx * scale, headerBottom - 0.008f - cardH * 0.5f - unitCy * scale, region.localPosition.z);
                        EditorUtility.SetDirty(region);
                    }
                    var label = board.Find(labelNames[i]);
                    var tmp = label != null ? label.GetComponent<TMP_Text>() : null;
                    if (tmp != null)
                    {
                        Undo.RecordObject(label, "header label"); Undo.RecordObject(tmp, "header label"); Undo.RecordObject(tmp.rectTransform, "header label");
                        label.localScale = Vector3.one * 0.01f;
                        label.localRotation = Quaternion.identity;
                        // Centred on the header block and 0.5 mm proud of its face: flush, not floating.
                        label.localPosition = new Vector3(laneCx, headerMidY, headers[i].a.z - 0.0005f);
                        tmp.rectTransform.pivot = new Vector2(0.5f, 0.5f);
                        tmp.rectTransform.sizeDelta = new Vector2((laneW - 0.012f) * 100f, (headerH - 0.006f) * 100f);
                        tmp.enableAutoSizing = true; tmp.fontSizeMax = 19f; tmp.fontSizeMin = 10f; tmp.fontSize = 19f;
                        tmp.alignment = TextAlignmentOptions.Center;
                        tmp.textWrappingMode = TextWrappingModes.NoWrap;
                        tmp.overflowMode = TextOverflowModes.Ellipsis;
                        tmp.margin = Vector4.zero;
                        EditorUtility.SetDirty(tmp);
                    }
                }
                int rows = Mathf.FloorToInt((headerBottom - 0.008f - (-0.170f)) / pitch + 0.25f);
                sb.AppendLine($"cards: scale {scale:F2} ({unitW * scale * 100f:F1} x {cardH * 100f:F1} cm) in {laneW * 100f:F1} cm lanes, row pitch {pitch * 100f:F1} cm, {rows} rows fit above the lane bottom");
                sb.AppendLine($"column labels: centred on the header blocks at y={headerMidY:F3}, z={headers[0].a.z - 0.0005f:F4} (were y=0.160, z=-0.016)");
            }
            else sb.AppendLine($"SKIP lanes: expected 4 header blocks, found {headers.Count}");

            // Title: on the top rail, centred, flush with the surface under it.
            var title = board.Find("Title");
            var titleTmp = title != null ? title.GetComponent<TMP_Text>() : null;
            var anchor = frame.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "Board_Title_Anchor");
            if (titleTmp != null)
            {
                Vector3 a = anchor != null ? board.InverseTransformPoint(anchor.position) : new Vector3(0f, 0.240f, 0f);
                float z = a.z;
                bool hit = FrontSurface(frame, board, new Rect(a.x - 0.10f, a.y - 0.012f, 0.20f, 0.024f), out float surf);
                if (hit) z = surf;
                Undo.RecordObject(title, "title"); Undo.RecordObject(titleTmp, "title"); Undo.RecordObject(titleTmp.rectTransform, "title");
                title.localScale = Vector3.one * 0.01f;
                title.localRotation = Quaternion.identity;
                title.localPosition = new Vector3(0f, a.y, z - 0.0005f);
                titleTmp.rectTransform.pivot = new Vector2(0.5f, 0.5f);
                titleTmp.rectTransform.sizeDelta = new Vector2(40f, 3.0f);
                titleTmp.enableAutoSizing = true; titleTmp.fontSizeMax = 20f; titleTmp.fontSizeMin = 12f; titleTmp.fontSize = 20f;
                titleTmp.alignment = TextAlignmentOptions.Center;
                titleTmp.textWrappingMode = TextWrappingModes.NoWrap;
                titleTmp.margin = Vector4.zero;
                EditorUtility.SetDirty(titleTmp);
                sb.AppendLine($"title: centred at y={a.y:F3}, on the surface at z={z:F4} ({(hit ? "measured from the frame mesh" : "frame anchor; mesh not readable")})");
            }

            // Board keys, their caption and the assign dock go BELOW the frame, clear of the lanes.
            float frameBottom = -0.215f;
            var moves = new (string name, Vector3 pos)[]
            {
                ("NewTaskButton",     new Vector3(-0.240f, frameBottom - 0.034f, -0.012f)),
                ("PauseResumeButton", new Vector3(-0.160f, frameBottom - 0.034f, -0.012f)),
                ("PriorityButton",    new Vector3(-0.080f, frameBottom - 0.034f, -0.012f)),
                ("CardConsoleLabel",  new Vector3(-0.160f, frameBottom - 0.074f, -0.012f)),
                ("AssignDock",        new Vector3( 0.120f, frameBottom - 0.060f, -0.012f)),
            };
            foreach (var (name, pos) in moves)
            {
                var t = board.Find(name);
                if (t == null) continue;
                Undo.RecordObject(t, "board controls");
                t.localPosition = pos;
                EditorUtility.SetDirty(t);
            }
            // Raise the board so the controls under it stay above the desk.
            Undo.RecordObject(board, "board height");
            board.localPosition = new Vector3(board.localPosition.x, 0.40f, board.localPosition.z);
            EditorUtility.SetDirty(board);
            sb.AppendLine("board keys, caption and assign dock moved below the frame; board raised to y=0.40");
        }

        var scene = EditorSceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(scene);
        sb.AppendLine("scene saved=" + EditorSceneManager.SaveScene(scene));
        return sb.ToString();
    }
}
