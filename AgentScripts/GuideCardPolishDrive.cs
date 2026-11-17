using System.Linq;
using System.Text;
using TMPro;
using UnityEngine;
using Oculus.Interaction;
using MuseDesk.Spatial;
using MuseDesk.Workspace;
using MuseDesk.Documents;

/// <summary>
/// Play Mode checks for BuildGuideCardPolishV1 (run: scripts\drv.ps1 GuideCardPolishDrive Heights / Card).
///   Heights : heights (mm, relative to the camera = the eye) of every poke key under the mode console, the
///             movable panels (board, dock...) and the stamp tool, plus WorkspaceRoot. Run before and after
///             the builder (before = scene value offDeskDrop 0.30; after = 0.39: all should drop ~90 mm).
///   Card    : the guide's card: plate size, each label's text bounds margin inside the plate, key row y/x.
///             Show the card first (PlacementGuideDrive) so the keys are scaled up.
/// </summary>
public static class GuideCardPolishDrive
{
    static string Mm(float m) => (m * 1000f).ToString("F0");

    public static string Heights()
    {
        var cam = Camera.main;
        if (cam == null) return "GuideCardPolishDrive.Heights: no Camera.main";
        float eye = cam.transform.position.y;
        var sb = new StringBuilder($"eye y {eye:F3} m; values are mm relative to the eye (negative = below)\n");
        var place = Object.FindAnyObjectByType<WorkspacePlacement>(FindObjectsInactive.Include);
        if (place != null && place.workspaceRoot != null)
            sb.AppendLine($"WorkspaceRoot y {Mm(place.workspaceRoot.position.y - eye)} (offDeskDrop {place.offDeskDrop:F2})");
        var console = Object.FindAnyObjectByType<ModeConsole>(FindObjectsInactive.Include);
        if (console != null)
            foreach (var p in console.GetComponentsInChildren<PokeInteractable>(true))
                sb.AppendLine($"console key {p.transform.root == p.transform ? p.name : p.transform.parent != null ? p.transform.parent.name + "/" + p.name : p.name}: {Mm(p.transform.position.y - eye)}");
        foreach (var mp in MovablePanel.All)
            if (mp != null) sb.AppendLine($"panel {mp.name}: pivot {Mm(mp.transform.position.y - eye)}");
        var stamp = Object.FindAnyObjectByType<StampTool>(FindObjectsInactive.Include);
        if (stamp != null) sb.AppendLine($"stamp tool {stamp.name}: {Mm(stamp.transform.position.y - eye)}");
        return sb.ToString();
    }

    public static string Card()
    {
        var guide = PlacementGuide.Instance;
        if (guide == null || guide.card == null || guide.content == null) return "GuideCardPolishDrive.Card: no PlacementGuide";
        var sb = new StringBuilder($"card shown={guide.CardShown} state={guide.State} title='{guide.TitleText}' body='{guide.BodyText}'\n");
        var plate = guide.content.Find("Plate");
        var mf = plate != null ? plate.GetComponent<MeshFilter>() : null;
        if (mf == null || mf.sharedMesh == null) return sb.Append("no plate mesh").ToString();
        Bounds pb = mf.sharedMesh.bounds;
        Vector3 pc = plate.localPosition;
        sb.AppendLine($"plate {pb.size.x * 1000f:F0} x {pb.size.y * 1000f:F0} mm, centre y {Mm(pc.y)}");
        foreach (TMP_Text t in new[] { guide.title, guide.body })
        {
            if (t == null) continue;
            t.ForceMeshUpdate();
            Bounds b = t.textBounds; Vector3 p = t.transform.localPosition;
            float m = Mathf.Min(p.x + b.min.x * 0.01f - (pc.x - pb.extents.x), pc.x + pb.extents.x - (p.x + b.max.x * 0.01f),
                                p.y + b.min.y * 0.01f - (pc.y - pb.extents.y), pc.y + pb.extents.y - (p.y + b.max.y * 0.01f));
            sb.AppendLine($"label '{t.text}': margin to plate edge {m * 1000f:F1} mm");
        }
        foreach (var k in new[] { guide.cancelKey, guide.anotherKey, guide.placeKey, guide.startKey })
            if (k != null && k.root != null)
                sb.AppendLine($"key {k.root.name}: card x {Mm(k.root.localPosition.x)} y {Mm(k.root.localPosition.y)} scale {k.root.localScale.x:F3} shown={k.shown}");
        return sb.ToString();
    }
}
