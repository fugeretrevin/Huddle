using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using MuseDesk.Documents;
using MuseDesk.Models;

/// <summary>Edit-mode preview: bind demo content to the paper, face it +Z=world, and aim the Scene
/// View at it from the +Z side (where the user stands if the paper's +Z points at them) so a
/// scene_view capture shows exactly what the user would read. Tells us if text is mirrored.</summary>
public static class SeePaper
{
    public static string GoPlus() => Go("+z");
    public static string GoMinus() => Go("-z");

    public static string Go(string side)
    {
        var scene = EditorSceneManager.GetActiveScene();
        var doc = scene.GetRootGameObjects().FirstOrDefault(r => r.name == "DecisionDocument");
        if (doc == null) return "no doc";
        doc.transform.rotation = Quaternion.identity;   // +Z = world +Z
        doc.SetActive(true);
        var panel = doc.GetComponentInChildren<DecisionPanel>(true);
        if (panel) panel.Bind(new DecisionContent {
            Title = "Deploy auth service to staging?",
            Summary = "OAuth login flow passed 12/12 tests.",
            Priority = "blocking", Recommendation = "approve",
            Rationale = "All tests green; config matches prod.",
            RiskLevel = "low", Impact = "Staging only; reversible.",
            SourceMode = "demo" }, "demo");
        if (panel) panel.Expand();

        var sv = SceneView.lastActiveSceneView;
        if (sv == null) return "no sceneview";
        // side "+z": camera on +Z looking -Z (reads the +Z face). side "-z": opposite.
        Vector3 camFwd = side == "-z" ? Vector3.forward : Vector3.back;
        sv.LookAt(doc.transform.position, Quaternion.LookRotation(camFwd, Vector3.up), 0.32f);
        sv.Repaint();
        return $"shown; viewing from {(side == "-z" ? "-Z" : "+Z")} side";
    }
}
