using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using MuseDesk.Documents;
using MuseDesk.Models;

public static class ShowPaper
{
    public static string Go()
    {
        var doc = SceneManager.GetActiveScene().GetRootGameObjects()
            .FirstOrDefault(r => r.name == "DecisionDocument");
        if (doc == null) return "no doc";
        var panel = doc.GetComponentInChildren<DecisionPanel>(true);
        var content = new DecisionContent
        {
            Title = "Deploy auth service to staging?",
            Summary = "OAuth login flow passed 12/12 tests. Ready to push to staging.",
            Priority = "blocking",
            Recommendation = "approve",
            Rationale = "All tests green; config matches prod. Low blast radius on staging.",
            RiskLevel = "low",
            Impact = "Staging only; reversible via rollback.",
            SourceMode = "demo",
        };
        if (panel != null) panel.Bind(content, "demo");
        doc.SetActive(true);
        return $"paper shown (panel={panel != null}, active={doc.activeInHierarchy})";
    }
}
