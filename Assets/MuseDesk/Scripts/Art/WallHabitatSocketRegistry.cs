using System;
using System.Collections.Generic;
using UnityEngine;

namespace MuseDesk.Art
{
    /// <summary>
    /// Data-driven socket lookup registry for MuseDesk Phase 3 Wall Habitat modules and composite assemblies.
    /// Guarantees stable named socket resolution without magic child indices and verifies (1, 1, 1) socket scale safety.
    /// </summary>
    [DisallowMultipleComponent]
    public class WallHabitatSocketRegistry : MonoBehaviour
    {
        public static readonly string[] RequiredHabitatSockets =
        {
            "WallContact",
            "ManagerOfficeSocket",
            "ResearchCubicleSocket",
            "DeveloperCubicleSocket",
            "TesterCubicleSocket",
            "ElevatorCarSocket",
            "ElevatorStop_Lower",
            "ElevatorStop_Manager",
            "TaskPrinterSocket",
            "AttentionBeaconSocket",
            "PortalDepartureSocket",
            "CourierDispatchSocket",
        };

        public static readonly string[] OptionalHabitatSockets =
        {
            "ElevatorStop_Mid",
            "SharedMeetingSocket",
            "DeliverySocket",
        };

        public static readonly string[] RequiredAgentSockets =
        {
            "AgentHomePose",
            "WorkPose",
            "SeatPose",
            "EntryPose",
            "HandoffPose",
            "ElevatorQueuePose",
            "DocumentAttach",
            "HeadLookTarget",
        };

        [Serializable]
        public struct NamedSocketEntry
        {
            public string id;
            public Transform target;
        }

        [Header("Resolved Sockets")]
        [SerializeField] private List<NamedSocketEntry> entries = new List<NamedSocketEntry>();
        [SerializeField] private bool isCompactLayout;

        private readonly Dictionary<string, Transform> _lookup = new Dictionary<string, Transform>(StringComparer.Ordinal);

        public bool IsCompactLayout
        {
            get => isCompactLayout;
            set => isCompactLayout = value;
        }

        public IReadOnlyList<NamedSocketEntry> Entries => entries;

        private void Awake()
        {
            RebuildLookup();
        }

        public void RebuildLookup()
        {
            _lookup.Clear();
            for (int i = 0; i < entries.Count; i++)
            {
                if (!string.IsNullOrEmpty(entries[i].id) && entries[i].target != null)
                {
                    _lookup[entries[i].id] = entries[i].target;
                }
            }
        }

        /// <summary>
        /// Scans hierarchy recursively and registers known habitat and agent socket names.
        /// Prefers top-level/root-owned sockets over child module duplicates for shared habitat IDs.
        /// </summary>
        public void ResolveAllSockets()
        {
            entries.Clear();
            _lookup.Clear();

            var knownIds = new HashSet<string>(RequiredHabitatSockets, StringComparer.Ordinal);
            foreach (string s in OptionalHabitatSockets) knownIds.Add(s);
            foreach (string s in RequiredAgentSockets) knownIds.Add(s);
            knownIds.Add("WorkstationSocket");
            knownIds.Add("TaskCardSpawnPose");
            // Priority B navigation, elevator threshold, handoff & interaction sockets
            string[] extendedIds =
            {
                "ManagerDeskSocket", "DoorClearance", "RaySelectCollider",
                "PathNode_Lower_L", "PathNode_Lower_C", "PathNode_Lower_R", "PathNode_Lower_Elev",
                "PathNode_Mid_C", "PathNode_Mid_Elev", "PathNode_Manager_C", "PathNode_Manager_Printer",
                "PathNode_Entry", "PathNode_MidDeck", "PathNode_ShaftFront",
                "ElevatorEntry_Lower", "ElevatorExit_Lower",
                "ElevatorEntry_Mid", "ElevatorExit_Mid",
                "ElevatorEntry_Manager", "ElevatorExit_Manager",
                "AgentStand_Cubicle", "AgentStand_Nook_L", "AgentStand_Nook_R", "AgentStand_Manager",
                "Handoff_In", "Handoff_Out", "Handoff_Mid", "Handoff_Manager", "Handoff_Printer",
                "CardOutput_Manager", "CardOutput_Tray",
                "PortalSource_Wall", "PortalSource_Floor", "DialoguePanelSocket", "PortalCenterPose", "EmergeStartPose",
                "ElevatorStop_Lower_Queue", "ElevatorStop_Mid_Queue", "ElevatorStop_Manager_Queue",
            };
            foreach (string s in extendedIds) knownIds.Add(s);
            knownIds.Add("PortalCenterPose");
            knownIds.Add("EmergeStartPose");
            knownIds.Add("DialoguePanelSocket");
            knownIds.Add("ElevatorSpineSocket");
            knownIds.Add("ElevatorCabinPose");
            knownIds.Add("ElevatorQueuePose_Lower");
            knownIds.Add("ElevatorQueuePose_Mid");
            knownIds.Add("ElevatorQueuePose_Manager");
            knownIds.Add("EntryPose_Lower");
            knownIds.Add("EntryPose_Mid");
            knownIds.Add("EntryPose_Manager");
            knownIds.Add("ConfirmationBadgeSocket");

            Transform[] all = GetComponentsInChildren<Transform>(true);
            // Register shallowest transform per ID first so composite root sockets take precedence over sub-modules
            var sorted = new List<Transform>(all);
            sorted.Sort((a, b) => GetDepth(a, transform).CompareTo(GetDepth(b, transform)));

            for (int i = 0; i < sorted.Count; i++)
            {
                Transform t = sorted[i];
                if (!knownIds.Contains(t.name)) continue;
                if (_lookup.ContainsKey(t.name)) continue;

                _lookup[t.name] = t;
                entries.Add(new NamedSocketEntry { id = t.name, target = t });
            }
        }

        public bool TryGetSocket(string socketId, out Transform socket)
        {
            if (_lookup.Count == 0 && entries.Count > 0) RebuildLookup();
            if (string.IsNullOrEmpty(socketId))
            {
                socket = null;
                return false;
            }
            if (_lookup.TryGetValue(socketId, out socket) && socket != null)
                return true;

            Transform[] all = GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i].name == socketId)
                {
                    _lookup[socketId] = all[i];
                    socket = all[i];
                    return true;
                }
            }
            socket = null;
            return false;
        }

        public bool ValidateHabitatAssembly(out List<string> missingSockets, out List<string> nonUnitScaleSockets)
        {
            if (_lookup.Count == 0) ResolveAllSockets();
            missingSockets = new List<string>();
            nonUnitScaleSockets = new List<string>();

            for (int i = 0; i < RequiredHabitatSockets.Length; i++)
            {
                string req = RequiredHabitatSockets[i];
                if (!TryGetSocket(req, out Transform t))
                {
                    missingSockets.Add(req);
                }
                else if (Vector3.Distance(t.lossyScale, Vector3.one) > 0.02f)
                {
                    nonUnitScaleSockets.Add($"{req}({t.lossyScale})");
                }
            }
            return missingSockets.Count == 0 && nonUnitScaleSockets.Count == 0;
        }

        private static int GetDepth(Transform t, Transform stopAt)
        {
            int depth = 0;
            Transform cur = t;
            while (cur != null && cur != stopAt)
            {
                depth++;
                cur = cur.parent;
            }
            return depth;
        }
    }
}
