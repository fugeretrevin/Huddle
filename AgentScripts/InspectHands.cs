using System.Linq;
using System.Text;
using UnityEngine;

public static class InspectHands
{
    public static string Interactors()
    {
        var sb = new StringBuilder();
        // Any component whose type name ends in "Interactor" (ISDK: PokeInteractor, RayInteractor...)
        var all = Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include);
        var interactors = all.Where(c => c != null &&
            (c.GetType().Name.Contains("Interactor") || c.GetType().Name.Contains("PointableCanvas")
             || c.GetType().Name == "OVRHand" || c.GetType().Name.Contains("HandVisual")))
            .Select(c => $"{c.GetType().Name} on {c.gameObject.name}")
            .Distinct().OrderBy(x => x).ToList();
        sb.AppendLine($"ISDK/hand components: {interactors.Count}");
        foreach (var i in interactors) sb.AppendLine("  " + i);

        var es = Object.FindObjectsByType<UnityEngine.EventSystems.EventSystem>(FindObjectsInactive.Include);
        sb.AppendLine($"EventSystems: {es.Length}");
        return sb.ToString();
    }
}
