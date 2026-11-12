using System.Collections.Generic;
using UnityEngine;

namespace MuseDesk.Art
{
    /// <summary>
    /// Modular art-layer controller for a miniature workstation
    /// (Workstation_Research, Workstation_Developer, Workstation_Testing).
    /// Exposes all named interaction anchors and provides visual state hooks
    /// for status lights, dynamic monitor surfaces, and blueprint/report printers.
    /// </summary>
    [DisallowMultipleComponent]
    public class WorkstationVisualController : MonoBehaviour
    {
        public enum TestingVisualState
        {
            Idle,
            Testing,
            Passed,
            Failed,
            Blocked
        }

        [Header("Station Identity")]
        [SerializeField] private string stationRole = "developer";

        [Header("Standard Interaction Anchors")]
        [SerializeField] private Transform agentWorkPosition;
        [SerializeField] private Transform agentSeatPosition;
        [SerializeField] private Transform taskDropZone;
        [SerializeField] private Transform documentPickupPoint;
        [SerializeField] private Transform artifactOutputPoint;
        [SerializeField] private Transform handoffReceivePoint;
        [SerializeField] private Transform monitorScreenLeft;
        [SerializeField] private Transform monitorScreenRight;
        [SerializeField] private Transform blueprintSpawnPoint;
        [SerializeField] private Transform chamberSlotPoint;
        [SerializeField] private Transform acceptedTrayPoint;
        [SerializeField] private Transform rejectedTrayPoint;

        [Header("Controllable Visual Elements")]
        [SerializeField] private Renderer lightIdle;
        [SerializeField] private Renderer lightTesting;
        [SerializeField] private Renderer lightPass;
        [SerializeField] private Renderer lightFail;
        [SerializeField] private Renderer lightBlocked;
        [SerializeField] private Transform scannerRing;
        [SerializeField] private Transform chamberDoor;
        [SerializeField] private Transform printerOutputSheet;

        private readonly Dictionary<string, Transform> _anchorMap = new Dictionary<string, Transform>();
        private TestingVisualState _currentTestingState = TestingVisualState.Idle;

        public string StationRole => stationRole;
        public Transform AgentWorkPosition => agentWorkPosition;
        public Transform AgentSeatPosition => agentSeatPosition;
        public Transform TaskDropZone => taskDropZone;
        public Transform DocumentPickupPoint => documentPickupPoint;
        public Transform ArtifactOutputPoint => artifactOutputPoint;
        public Transform HandoffReceivePoint => handoffReceivePoint;
        public Transform MonitorScreenLeft => monitorScreenLeft;
        public Transform MonitorScreenRight => monitorScreenRight;
        public Transform BlueprintSpawnPoint => blueprintSpawnPoint;
        public Transform ChamberSlotPoint => chamberSlotPoint;
        public Transform AcceptedTrayPoint => acceptedTrayPoint;
        public Transform RejectedTrayPoint => rejectedTrayPoint;

        private void Awake()
        {
            ResolveAnchorsAndParts();
        }

        public void ResolveAnchorsAndParts()
        {
            _anchorMap.Clear();
            Transform[] children = GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < children.Length; i++)
            {
                Transform t = children[i];
                _anchorMap[t.name] = t;
            }

            agentWorkPosition   = GetChildOrExisting("AgentWorkPosition",   agentWorkPosition);
            agentSeatPosition   = GetChildOrExisting("AgentSeatPosition",   agentSeatPosition);
            taskDropZone        = GetChildOrExisting("TaskDropZone",        taskDropZone);
            documentPickupPoint = GetChildOrExisting("DocumentPickupPoint", documentPickupPoint);
            artifactOutputPoint = GetChildOrExisting("ArtifactOutputPoint", artifactOutputPoint);
            handoffReceivePoint = GetChildOrExisting("HandoffReceivePoint", handoffReceivePoint);
            monitorScreenLeft   = GetChildOrExisting("MonitorScreen_Left",  monitorScreenLeft);
            monitorScreenRight  = GetChildOrExisting("MonitorScreen_Right", monitorScreenRight);
            blueprintSpawnPoint = GetChildOrExisting("BlueprintSpawnPoint", blueprintSpawnPoint);
            chamberSlotPoint    = GetChildOrExisting("ChamberSlotPoint",    chamberSlotPoint);
            acceptedTrayPoint   = GetChildOrExisting("AcceptedTrayPoint",   acceptedTrayPoint);
            rejectedTrayPoint   = GetChildOrExisting("RejectedTrayPoint",   rejectedTrayPoint);

            scannerRing        = GetChildOrExisting("Testing_ScannerRing",      scannerRing);
            chamberDoor        = GetChildOrExisting("Testing_ChamberDoor",      chamberDoor);
            printerOutputSheet = GetChildOrExisting("Printer_OutputBlueprint",  printerOutputSheet);
            if (printerOutputSheet == null)
            {
                printerOutputSheet = GetChildOrExisting("Testing_ErrorReportSheet", null);
            }

            lightIdle    = GetRenderer("Light_Idle",    lightIdle);
            lightTesting = GetRenderer("Light_Testing", lightTesting);
            lightPass    = GetRenderer("Light_Pass",    lightPass);
            lightFail    = GetRenderer("Light_Fail",    lightFail);
            lightBlocked = GetRenderer("Light_Blocked", lightBlocked);
        }

        private Transform GetChildOrExisting(string childName, Transform existing)
        {
            if (existing != null) return existing;
            Transform found;
            if (_anchorMap.TryGetValue(childName, out found)) return found;
            return null;
        }

        private Renderer GetRenderer(string childName, Renderer existing)
        {
            if (existing != null) return existing;
            Transform t = GetChildOrExisting(childName, null);
            if (t != null) return t.GetComponent<Renderer>();
            return null;
        }

        public Transform GetAnchor(string anchorName)
        {
            if (_anchorMap.Count == 0) ResolveAnchorsAndParts();
            Transform found;
            return _anchorMap.TryGetValue(anchorName, out found) ? found : null;
        }

        /// <summary>
        /// Sets the visual state of the Testing Workstation status lights and chamber.
        /// </summary>
        public void SetTestingVisualState(TestingVisualState state)
        {
            _currentTestingState = state;
            SetLightActive(lightIdle,    state == TestingVisualState.Idle);
            SetLightActive(lightTesting, state == TestingVisualState.Testing);
            SetLightActive(lightPass,    state == TestingVisualState.Passed);
            SetLightActive(lightFail,    state == TestingVisualState.Failed);
            SetLightActive(lightBlocked, state == TestingVisualState.Blocked);
        }

        private static void SetLightActive(Renderer r, bool active)
        {
            if (r == null) return;
            r.transform.localScale = active ? Vector3.one * 1.25f : Vector3.one * 0.85f;
        }

        private void Update()
        {
            if (_currentTestingState == TestingVisualState.Testing && scannerRing != null)
            {
                scannerRing.Rotate(Vector3.up, 120f * Time.deltaTime, Space.Self);
            }
        }
    }
}
