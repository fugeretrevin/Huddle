using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using MuseDesk.Agents;

public static class TriggerWalk
{
    public static string Go()
    {
        var views = SceneManager.GetActiveScene().GetRootGameObjects()
            .SelectMany(r => r.GetComponentsInChildren<AgentView>(true)).ToList();
        var researcher = views.FirstOrDefault(v => v.agentId == "researcher");
        if (researcher == null) return "no researcher";
        var mv = researcher.GetComponent<AgentMovement>();
        if (mv == null) return "no AgentMovement";
        mv.travelSeconds = 3f;                 // slow it down so we catch mid-stride
        mv.DeliverInFrontOfUser(0.4f);
        researcher.SetAlert(true);
        return $"researcher walking (IsMoving={mv.IsMoving}, hasArms={researcher.armL != null && researcher.armR != null})";
    }
}
