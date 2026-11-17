using System.Collections.Generic;
using System.Linq;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using MuseDesk.Spatial;
using MuseDesk.Workspace;

/// <summary>
/// Owner feedback on rc6-1 (seated at a desk):
///  (a) "the cards for the scanning thing could be cleaner and look nicer"  -> the LookGuide card
///  (b) "the whole thing is just slightly too high up ... so I don't have to bring my hand up so much"
///
/// (a) Visual only; the guide's logic, timings and key wiring are untouched. The card (scene-root "LookGuide")
///     becomes a smaller rounded plate (rounded-rect mesh assets, ink border, paper face): a small teal arrow
///     in a thin ring on the left, one title line and one short hint line to its right (one line each, no
///     wrap, measured against the plate), then ONE row of keys with even 60 mm spacing (Cancel / Other spot /
///     Place here-or-Start here). The plate is sized from the measured key bounds so keys never poke out.
///     Key labels are measured against their cap and shrunk when the margin is under 2 mm. The hint words
///     (serialized strings on PlacementGuide) are shortened so each fits one line. Pitches of the card are
///     lowered a little (see Pitches below). Keys are NOT re-created or re-wired; only their position moves.
/// (b) The desk-mode office is HEAD-RELATIVE, not table-snapped: WorkspacePlacement.PlaceOffDeskRoutine puts
///     WorkspaceRoot at head + right * offDeskSide + forward * offDeskDepth + down * offDeskDrop when the
///     headset is worn and steady (offDeskMode is on in the scene; no MRUK table is used). The scene value of
///     offDeskDrop was 0.30 m; this sets 0.39 m, so everything under WorkspaceRoot (console, board, stamps,
///     panels, agents) sits 9 cm lower relative to the head. MovablePanel's height band is relative to
///     WorkspaceRoot's y (frame.position.y), so it moves with the root and needs no change.
///
/// Run AFTER BuildPlaceHereV1 (that builder resets key y to -0.050; this one then sets the row):
///   powershell scripts\run_guarded.ps1 -File AgentScripts\BuildGuideCardPolishV1.cs -Entry BuildGuideCardPolishV1.Apply
/// Idempotent. Revert() puts the V1 numbers back. Saves the scene. Run only in Edit Mode.
/// </summary>
public static class BuildGuideCardPolishV1
{
    const string GuideName = "LookGuide";
    const string MeshDir = "Assets/MuseDesk/Art/Meshes";
    const string PlateMeshPath = MeshDir + "/GuideCard_Plate.asset";
    const string BorderMeshPath = MeshDir + "/GuideCard_Border.asset";

    // New values
    public const float OffDeskDropNew = 0.39f, OffDeskDropOld = 0.30f;
    const float Pitch_Ceiling = 5f, Pitch_Floor = -20f, Pitch_Preview = -20f;     // was 14 / -16 / -12
    const float PlateZ = 0.006f, ProudZ = 0.0055f;
    const float MinW = 0.205f, CornerR = 0.012f, BorderMm = 2.0f;
    const float TitleTop = 0.040f, KeyTop = -0.006f, BottomPad = 0.007f;
    const float TextX = 0.0275f, TextW = 0.138f;
    const float ArrowX = -0.073f, ArrowY = 0.0145f;
    const float MarginMm = 2f;

    static Color Hex(string h) { ColorUtility.TryParseHtmlString(h, out var c); return c; }

    static void FillRounded(Mesh m, float w, float h, float r, int seg)
    {
        r = Mathf.Min(r, Mathf.Min(w, h) * 0.5f);
        var v = new List<Vector3> { Vector3.zero };
        float hx = w * 0.5f - r, hy = h * 0.5f - r;
        Vector2[] centres = { new Vector2(hx, hy), new Vector2(-hx, hy), new Vector2(-hx, -hy), new Vector2(hx, -hy) };
        for (int c = 0; c < 4; c++)
            for (int i = 0; i <= seg; i++)
            {
                float a = (c * 90f + 90f * i / seg) * Mathf.Deg2Rad;
                v.Add(new Vector3(centres[c].x + Mathf.Cos(a) * r, centres[c].y + Mathf.Sin(a) * r, 0f));
            }
        var t = new List<int>();
        int n = v.Count - 1;
        for (int i = 1; i <= n; i++) { t.Add(0); t.Add(i); t.Add(i == n ? 1 : i + 1); }
        m.Clear();
        m.SetVertices(v);
        m.SetTriangles(t, 0);
        m.SetNormals(Enumerable.Repeat(new Vector3(0, 0, -1), v.Count).ToList());
        m.SetUVs(0, Enumerable.Repeat(Vector2.zero, v.Count).ToList());
        m.RecalculateBounds();
    }

    static Mesh RoundedAsset(string path, float w, float h, float r)
    {
        var m = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (m == null)
        {
            if (!AssetDatabase.IsValidFolder(MeshDir)) AssetDatabase.CreateFolder("Assets/MuseDesk/Art", "Meshes");
            m = new Mesh { name = System.IO.Path.GetFileNameWithoutExtension(path) };
            FillRounded(m, w, h, r, 6);
            AssetDatabase.CreateAsset(m, path);
        }
        else FillRounded(m, w, h, r, 6);
        EditorUtility.SetDirty(m);
        return m;
    }

    static void SetMesh(Transform t, Mesh mesh, Vector3 pos, int z)
    {
        var mf = t.GetComponent<MeshFilter>();
        mf.sharedMesh = mesh;
        t.localPosition = pos; t.localRotation = Quaternion.identity; t.localScale = Vector3.one;
        EditorUtility.SetDirty(mf); EditorUtility.SetDirty(t);
    }

    /// <summary>Key bounds in key-local space scaled by the key's shown scale: min/max in card metres (relative to the key pivot).</summary>
    static bool KeyBounds(PlacementGuide.GuideKey k, out Vector3 min, out Vector3 max, out float capHalfW)
    {
        min = new Vector3(1e9f, 1e9f, 1e9f); max = -min; capHalfW = 0f;
        if (k == null || k.root == null) return false;
        Vector3 s = k.shownScale.sqrMagnitude > 1e-8f ? k.shownScale : Vector3.one;
        bool any = false; float widest = 0f;
        foreach (var r in k.root.GetComponentsInChildren<MeshRenderer>(true))
        {
            if (r.GetComponent<TMP_Text>() != null) continue;
            var mf = r.GetComponent<MeshFilter>(); if (mf == null || mf.sharedMesh == null) continue;
            Matrix4x4 m = k.root.worldToLocalMatrix * r.transform.localToWorldMatrix;
            Bounds lb = mf.sharedMesh.bounds;
            float rmin = 1e9f, rmax = -1e9f;
            for (int i = 0; i < 8; i++)
            {
                Vector3 c = lb.center + Vector3.Scale(lb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                Vector3 p = Vector3.Scale(m.MultiplyPoint3x4(c), s);
                min = Vector3.Min(min, p); max = Vector3.Max(max, p);
                rmin = Mathf.Min(rmin, p.x); rmax = Mathf.Max(rmax, p.x);
                any = true;
            }
            float w = rmax - rmin;
            bool cap = r.name.ToLowerInvariant().Contains("cap");
            if (cap || (w > widest && capHalfW == 0f)) { if (cap || w > widest) { widest = w; capHalfW = w * 0.5f; } }
        }
        return any;
    }

    static void StyleText(TMP_Text t, Vector3 lp, Vector2 sizeM, float size, float min, Color c, TextAlignmentOptions al)
    {
        t.transform.localPosition = lp;
        t.transform.localScale = Vector3.one * 0.01f;
        t.rectTransform.sizeDelta = new Vector2(sizeM.x / 0.01f, sizeM.y / 0.01f);
        t.alignment = al;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.overflowMode = TextOverflowModes.Ellipsis;
        t.enableAutoSizing = true;
        t.fontSize = size; t.fontSizeMax = size; t.fontSizeMin = min;
        t.color = c;
        EditorUtility.SetDirty(t);
    }

    /// <summary>Margin (mm) of the text's rendered bounds inside the plate rect, content space.</summary>
    static float PlateMargin(TMP_Text t, float plateW, float plateH, float plateCy)
    {
        t.ForceMeshUpdate();
        Bounds b = t.textBounds;
        Vector3 p = t.transform.localPosition;
        float x0 = p.x + b.min.x * 0.01f, x1 = p.x + b.max.x * 0.01f;
        float y0 = p.y + b.min.y * 0.01f, y1 = p.y + b.max.y * 0.01f;
        float m = Mathf.Min(x0 + plateW * 0.5f, plateW * 0.5f - x1, y0 - (plateCy - plateH * 0.5f), plateCy + plateH * 0.5f - y1);
        return m * 1000f;
    }

    public static string Apply()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return "BuildGuideCardPolishV1: stop Play Mode first.\n";
        var scene = EditorSceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded) return "BuildGuideCardPolishV1: no open scene.\n";
        var sb = new StringBuilder("== BuildGuideCardPolishV1 ==\n");

        var guide = Object.FindObjectsByType<PlacementGuide>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                          .FirstOrDefault(g => g != null && g.transform.parent == null && g.name == GuideName);
        if (guide == null || guide.card == null || guide.content == null) return sb.Append("LookGuide / card / content missing (run BuildPlacementGuideV1, BuildPlaceHereV1); nothing changed.\n").ToString();
        Transform content = guide.content;
        Transform plate = content.Find("Plate"), border = content.Find("Border");
        if (plate == null || border == null || guide.title == null || guide.body == null) return sb.Append("Plate/Border/Title/Body missing; nothing changed.\n").ToString();

        Undo.RecordObject(guide, "Guide card polish");

        // ---- words: one short line each ----
        guide.ceilingTitle = "Look up at the ceiling";
        guide.ceilingBody = "Hold your gaze there";
        guide.floorTitle = "Look at the floor";
        guide.floorBody = "Where the office should go";
        guide.offerBody = "Or press Start here";
        guide.requestedTitle = "Scanning";
        guide.requestedBody = "Keep looking at the spot";
        guide.floorPreviewTitle = "Place the office";
        guide.floorPreviewBody = "Press Place here, or drag it";
        guide.floorDraggingBody = "Let go on a clear spot";
        guide.floorReadyBody = "Press Place here to move";
        guide.ceilingPreviewTitle = "Mount it here?";
        guide.ceilingPreviewBody = "Confirm, or try Next spot";

        // ---- pitches (head-relative card height), slightly below the eye line ----
        sb.AppendLine($"  card pitch ceiling {guide.ceilingCardPitch:F0}->{Pitch_Ceiling:F0}, floor {guide.floorCardPitch:F0}->{Pitch_Floor:F0}, preview {guide.previewCardPitch:F0}->{Pitch_Preview:F0} degrees");
        guide.ceilingCardPitch = Pitch_Ceiling; guide.floorCardPitch = Pitch_Floor; guide.previewCardPitch = Pitch_Preview;

        // ---- measure the keys ----
        var keys = new[] { guide.cancelKey, guide.anotherKey, guide.placeKey, guide.startKey };
        string[] names = { "Cancel", "Other spot", "Place here", "Start here" };
        float[] xs = { -0.060f, 0f, 0.060f, 0.060f };
        float minY = 1e9f, maxY = -1e9f, maxAbsX = 0f;
        for (int i = 0; i < keys.Length; i++)
        {
            if (!KeyBounds(keys[i], out var kmin, out var kmax, out _)) { sb.AppendLine($"  WARNING: key {names[i]} missing or has no mesh; skipped"); continue; }
            minY = Mathf.Min(minY, kmin.y); maxY = Mathf.Max(maxY, kmax.y);
            maxAbsX = Mathf.Max(maxAbsX, Mathf.Abs(xs[i]) + Mathf.Max(-kmin.x, kmax.x));
        }
        if (minY > maxY) { minY = -0.011f; maxY = 0.011f; maxAbsX = 0.060f + 0.021f; sb.AppendLine("  WARNING: no key measured; assumed 42 x 22 mm"); }

        // Pivot y so that the key tops sit at KeyTop; plate sized around the row.
        float keyPivotY = KeyTop - maxY;
        float keyBottom = keyPivotY + minY;
        float plateBottom = keyBottom - BottomPad;
        float plateH = TitleTop - plateBottom, plateCy = (TitleTop + plateBottom) * 0.5f;
        float plateW = Mathf.Max(MinW, 2f * (maxAbsX + 0.010f));

        Mesh plateMesh = RoundedAsset(PlateMeshPath, plateW, plateH, CornerR);
        Mesh borderMesh = RoundedAsset(BorderMeshPath, plateW + 2f * BorderMm * 0.001f, plateH + 2f * BorderMm * 0.001f, CornerR + BorderMm * 0.001f);
        SetMesh(border, borderMesh, new Vector3(0f, plateCy, PlateZ + 0.0005f), 0);
        SetMesh(plate, plateMesh, new Vector3(0f, plateCy, PlateZ), 0);

        // ---- keys: one row, even spacing; only their position moves ----
        for (int i = 0; i < keys.Length; i++)
        {
            if (keys[i] == null || keys[i].root == null) continue;
            keys[i].root.localPosition = new Vector3(-xs[i], keyPivotY, 0f);     // card faces the user: its x is the user's left
            EditorUtility.SetDirty(keys[i].root);
        }

        // ---- key label fit (>= 2 mm each side of the label inside its cap) ----
        for (int i = 0; i < keys.Length; i++)
        {
            var k = keys[i];
            if (k == null || k.root == null) continue;
            var label = k.root.GetComponentInChildren<TMP_Text>(true);
            if (label == null || !KeyBounds(k, out _, out _, out float capHalf) || capHalf <= 0f) continue;
            Vector3 s = k.shownScale.sqrMagnitude > 1e-8f ? k.shownScale : Vector3.one;
            for (int pass = 0; pass < 2; pass++)
            {
                label.ForceMeshUpdate();
                Bounds b = label.textBounds;
                Matrix4x4 m = k.root.worldToLocalMatrix * label.transform.localToWorldMatrix;
                float x0 = Vector3.Scale(m.MultiplyPoint3x4(new Vector3(b.min.x, b.center.y, 0f)), s).x;
                float x1 = Vector3.Scale(m.MultiplyPoint3x4(new Vector3(b.max.x, b.center.y, 0f)), s).x;
                float textW = Mathf.Abs(x1 - x0), margin = (capHalf * 2f - textW) * 0.5f * 1000f;
                if (pass == 0)
                {
                    sb.AppendLine($"  key '{label.text}': cap {capHalf * 2000f:F1} mm, text {textW * 1000f:F1} mm, margin {margin:F1} mm/side");
                    if (margin >= MarginMm) break;
                    float want = Mathf.Max(3f, label.fontSize * (capHalf * 2f - 2f * MarginMm * 0.001f) / Mathf.Max(textW, 1e-5f));
                    label.enableAutoSizing = true; label.fontSizeMax = want; label.fontSize = want; label.fontSizeMin = Mathf.Min(3f, want);
                    EditorUtility.SetDirty(label);
                }
                else sb.AppendLine($"     shrunk to {label.fontSize:F1}; margin now {margin:F1} mm/side");
            }
        }

        // ---- text and icon ----
        Color ink = Hex("#1F2530"), soft = Hex("#3A4250");
        StyleText(guide.title, new Vector3(TextX, 0.0275f, ProudZ), new Vector2(TextW, 0.014f), 8f, 6f, ink, TextAlignmentOptions.MidlineLeft);
        StyleText(guide.body, new Vector3(TextX, 0.0105f, ProudZ), new Vector2(TextW, 0.012f), 6f, 5f, soft, TextAlignmentOptions.MidlineLeft);
        guide.title.fontStyle = FontStyles.Bold;
        if (guide.arrow != null) { guide.arrow.transform.localPosition = new Vector3(ArrowX, ArrowY, ProudZ); guide.arrow.transform.localScale = Vector3.one * 0.45f; EditorUtility.SetDirty(guide.arrow.transform); }
        if (guide.ringTrack != null) { guide.ringTrack.transform.localPosition = new Vector3(ArrowX, ArrowY, ProudZ + 0.0001f); EditorUtility.SetDirty(guide.ringTrack.transform); }
        if (guide.ringFill != null) { guide.ringFill.transform.localPosition = new Vector3(ArrowX, ArrowY, ProudZ); EditorUtility.SetDirty(guide.ringFill.transform); }
        guide.ringInner = 0.016f; guide.ringOuter = 0.0195f;

        // ---- text fit report over every state's words ----
        string[] titles = { guide.ceilingTitle, guide.floorTitle, guide.requestedTitle, guide.floorPreviewTitle, guide.ceilingPreviewTitle };
        string[] bodies = { guide.ceilingBody, guide.floorBody, guide.offerBody, guide.requestedBody, guide.floorPreviewBody, guide.floorDraggingBody, guide.floorReadyBody, guide.ceilingPreviewBody };
        float worst = 1e9f; string worstWhere = "";
        string t0 = guide.title.text, b0 = guide.body.text;
        foreach (string s in titles) { guide.title.text = s; float m = PlateMargin(guide.title, plateW, plateH, plateCy); if (m < worst) { worst = m; worstWhere = "title '" + s + "'"; } }
        foreach (string s in bodies) { guide.body.text = s; float m = PlateMargin(guide.body, plateW, plateH, plateCy); if (m < worst) { worst = m; worstWhere = "hint '" + s + "'"; } }
        guide.title.text = guide.ceilingTitle; guide.body.text = guide.ceilingBody;
        EditorUtility.SetDirty(guide.title); EditorUtility.SetDirty(guide.body);
        sb.AppendLine($"  text fit: worst margin to the plate edge {worst:F1} mm ({worstWhere}); wanted >= {MarginMm:F0} mm");
        sb.AppendLine($"  plate {plateW * 1000f:F0} x {plateH * 1000f:F0} mm (was 300 x 155); key row y {keyPivotY * 1000f:F0} mm, pitch 60 mm; keys cover y {(keyPivotY + minY) * 1000f:F0}..{(keyPivotY + maxY) * 1000f:F0} mm");
        EditorUtility.SetDirty(guide);

        // ---- (b) lower the desk-mode office ----
        var place = Object.FindAnyObjectByType<WorkspacePlacement>(FindObjectsInactive.Include);
        if (place == null) sb.AppendLine("  WARNING: WorkspacePlacement not found; workspace height NOT changed");
        else
        {
            Undo.RecordObject(place, "Lower workspace");
            sb.AppendLine($"  WorkspacePlacement.offDeskDrop {place.offDeskDrop:F2} -> {OffDeskDropNew:F2} m (offDeskMode={place.offDeskMode}; head-relative, no table snap)");
            place.offDeskDrop = OffDeskDropNew;
            EditorUtility.SetDirty(place);
        }

        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(scene);
        bool saved = EditorSceneManager.SaveScene(scene);
        sb.AppendLine("  scene saved=" + saved + "; the new desk height applies the next time the office is placed (Play Mode start or headset re-mount)");
        return sb.ToString();
    }

    /// <summary>Back to the V1 card numbers and the old workspace drop. Keys keep their wiring.</summary>
    public static string Revert()
    {
        var scene = EditorSceneManager.GetActiveScene();
        var guide = Object.FindObjectsByType<PlacementGuide>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                          .FirstOrDefault(g => g != null && g.transform.parent == null && g.name == GuideName);
        if (guide != null && guide.content != null)
        {
            Mesh quad = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
            Transform plate = guide.content.Find("Plate"), border = guide.content.Find("Border");
            if (plate != null) { plate.GetComponent<MeshFilter>().sharedMesh = quad; plate.localPosition = new Vector3(0, 0, PlateZ); plate.localScale = new Vector3(0.30f, 0.155f, 1f); }
            if (border != null) { border.GetComponent<MeshFilter>().sharedMesh = quad; border.localPosition = new Vector3(0, 0, PlateZ + 0.0005f); border.localScale = new Vector3(0.306f, 0.161f, 1f); }
            foreach (var k in new[] { guide.cancelKey, guide.anotherKey, guide.placeKey, guide.startKey })
                if (k != null && k.root != null) k.root.localPosition = new Vector3(k.root.localPosition.x, -0.050f, 0f);
            guide.ceilingCardPitch = 14f; guide.floorCardPitch = -16f; guide.previewCardPitch = -12f;
            guide.ringInner = 0.027f; guide.ringOuter = 0.036f;
            if (guide.arrow != null) { guide.arrow.transform.localPosition = new Vector3(-0.105f, 0.008f, ProudZ); guide.arrow.transform.localScale = Vector3.one; }
            if (guide.ringTrack != null) guide.ringTrack.transform.localPosition = new Vector3(-0.105f, 0.008f, ProudZ + 0.0001f);
            if (guide.ringFill != null) guide.ringFill.transform.localPosition = new Vector3(-0.105f, 0.008f, ProudZ);
            guide.title.transform.localPosition = new Vector3(0.040f, 0.045f, ProudZ);
            guide.body.transform.localPosition = new Vector3(0.040f, 0.006f, ProudZ);
            EditorUtility.SetDirty(guide);
        }
        var place = Object.FindAnyObjectByType<WorkspacePlacement>(FindObjectsInactive.Include);
        if (place != null) { place.offDeskDrop = OffDeskDropOld; EditorUtility.SetDirty(place); }
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        return "BuildGuideCardPolishV1.Revert: card layout and pitches and offDeskDrop restored (words, title rect sizes and font sizes keep the polished values; re-run BuildPlacementGuideV1 for those)";
    }
}
