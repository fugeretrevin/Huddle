using UnityEngine;

namespace MuseDesk.Workspace
{
    /// <summary>
    /// Marks a slot under WorkspaceRoot where a station + agent prefab mounts.
    /// TASK-03 (characters) consumes these via WorkspacePlacement.GetStationAnchor(role).
    /// </summary>
    public class WorkstationAnchor : MonoBehaviour
    {
        [Tooltip("Logical role this slot hosts: researcher | developer | tester | delivery.")]
        public string role = "researcher";
    }
}
