using System;
using UnityEngine;

namespace MuseDesk.Art
{
    /// <summary>
    /// Controls the retractable ceiling workspace assembly (CeilingMount + SuspendedOfficePod + InspectionShelf).
    ///
    /// Design Contract:
    /// - Vertical deployment travel between DockedPose and room-geometry InspectionPose is driven
    ///   strictly at runtime in Unity (never baked into a fixed 2-meter Blender trajectory).
    /// - All 13 pod mechanical clips and 4 ceiling mount clips control local mechanical motion
    ///   (unlock latches, wing fold/unfold, shelf extend/retract, spring settle).
    /// - Every phase supports animation events AND deterministic fallback timers so skipped events
    ///   or mid-flight interrupts recover cleanly without ever locking the pod.
    /// </summary>
    [DisallowMultipleComponent]
    public class CeilingWorkspacePodController : MonoBehaviour
    {
        public enum PodState
        {
            DockedIdle,
            Acknowledging,
            Unlocking,
            BeginningDescent,
            Descending,
            Stabilizing,
            OpeningShelf,
            DeployedIdle,
            ClosingShelf,
            BeginningAscent,
            ReturningToDock,
            DockLocking,
            Settling
        }

        // Exact 13 Pod Animation State Names
        public const string ClipDockedIdle       = "Pod_Docked_Idle";
        public const string ClipAttentionAck     = "Pod_Attention_Ack";
        public const string ClipUnlock           = "Pod_Unlock";
        public const string ClipBeginDescent     = "Pod_Begin_Descent";
        public const string ClipDescending       = "Pod_Descending";
        public const string ClipStabilize        = "Pod_Stabilize";
        public const string ClipShelfOpen        = "Pod_Shelf_Open";
        public const string ClipDeployedIdle     = "Pod_Deployed_Idle";
        public const string ClipShelfClose       = "Pod_Shelf_Close";
        public const string ClipBeginAscent      = "Pod_Begin_Ascent";
        public const string ClipReturningDock    = "Pod_Returning_Dock";
        public const string ClipDockLock         = "Pod_Dock_Lock";
        public const string ClipMechanicalSettle = "Pod_Mechanical_Settle";

        // Matching CeilingMount Animation State Names
        public const string MountIdle  = "Mount_Docked_Idle";
        public const string MountUnlock = "Mount_Unlock";
        public const string MountSpool = "Mount_Spool_Spin";
        public const string MountLock  = "Mount_Lock";

        [Header("Animators")]
        [SerializeField] private Animator podAnimator;
        [SerializeField] private Animator mountAnimator;

        [Header("Required Ceiling & Pod Transforms")]
        [SerializeField] private Transform ceilingAnchor;
        [SerializeField] private Transform ceilingMountRoot;
        [SerializeField] private Transform suspensionTop;
        [SerializeField] private Transform suspensionBottom;
        [SerializeField] private Transform podRoot;
        [SerializeField] private Transform dockedPose;
        [SerializeField] private Transform inspectionPose;
        [SerializeField] private Transform inspectionShelf;
        [SerializeField] private Transform agentInteractionSocket;
        [SerializeField] private Transform documentPresentationSocket;
        [SerializeField] private Transform attentionBeaconSocket;

        [Header("Additional Pod Station Sockets")]
        [SerializeField] private Transform dockingConnector;
        [SerializeField] private Transform researchStationSocket;
        [SerializeField] private Transform developerStationSocket;
        [SerializeField] private Transform testerStationSocket;
        [SerializeField] private Transform factorySocket;
        [SerializeField] private Transform infoDisplaySocket;
        [SerializeField] private Transform mailboxSocket;

        [Header("Dynamic Suspension Tether")]
        [Tooltip("Visual cylinder/rod scaled dynamically between SuspensionTop and Pod DockingConnector.")]
        [SerializeField] private Transform dynamicTetherRod;
        [SerializeField] private float tetherBaseLength = 1f;

        [Header("Runtime Motion Tuning")]
        [SerializeField] private float verticalSpeedMetersPerSec = 0.65f;
        [SerializeField] private float minTravelSeconds = 0.75f;
        [SerializeField] private float maxTravelSeconds = 3.5f;

        private PodState _state = PodState.DockedIdle;
        private float _phaseTimer;
        private float _phaseTimeout;
        private bool _eventReceived;
        private bool _wantsDeployed;
        private Vector3 _travelStartWorld;
        private Vector3 _travelTargetWorld;
        private float _travelElapsed;
        private float _travelDuration;

        public PodState CurrentState => _state;
        public bool IsDeployed => _state == PodState.DeployedIdle;
        public bool IsDocked => _state == PodState.DockedIdle;
        public bool IsTransitioning => _state != PodState.DockedIdle && _state != PodState.DeployedIdle;

        public Transform CeilingAnchor => ceilingAnchor;
        public Transform CeilingMountRoot => ceilingMountRoot;
        public Transform SuspensionTop => suspensionTop;
        public Transform SuspensionBottom => suspensionBottom;
        public Transform PodRoot => podRoot;
        public Transform DockedPose => dockedPose;
        public Transform InspectionPose => inspectionPose;
        public Transform InspectionShelf => inspectionShelf;
        public Transform AgentInteractionSocket => agentInteractionSocket;
        public Transform DocumentPresentationSocket => documentPresentationSocket;
        public Transform AttentionBeaconSocket => attentionBeaconSocket;
        public Transform DockingConnector => dockingConnector;
        public Transform ResearchStationSocket => researchStationSocket;
        public Transform DeveloperStationSocket => developerStationSocket;
        public Transform TesterStationSocket => testerStationSocket;
        public Transform FactorySocket => factorySocket;
        public Transform InfoDisplaySocket => infoDisplaySocket;
        public Transform MailboxSocket => mailboxSocket;

        public event Action<PodState> StateChanged;

        private void Awake()
        {
            ResolveTransforms();
            UpdateTetherVisual();
        }

        public void ResolveTransforms()
        {
            Transform[] all = GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                Transform t = all[i];
                switch (t.name)
                {
                    case "CeilingAnchor":              ceilingAnchor = t; break;
                    case "CeilingMountRoot":           ceilingMountRoot = t; break;
                    case "SuspensionTop":              suspensionTop = t; break;
                    case "SuspensionBottom":           suspensionBottom = t; break;
                    case "PodRoot":                    podRoot = t; break;
                    case "DockedPose":                 dockedPose = t; break;
                    case "InspectionPose":             inspectionPose = t; break;
                    case "InspectionShelf":            inspectionShelf = t; break;
                    case "AgentInteractionSocket":     agentInteractionSocket = t; break;
                    case "DocumentPresentationSocket": documentPresentationSocket = t; break;
                    case "AttentionBeaconSocket":      attentionBeaconSocket = t; break;
                    case "DockingConnector":           dockingConnector = t; break;
                    case "ResearchStationSocket":      researchStationSocket = t; break;
                    case "DeveloperStationSocket":     developerStationSocket = t; break;
                    case "TesterStationSocket":        testerStationSocket = t; break;
                    case "FactorySocket":              factorySocket = t; break;
                    case "InfoDisplaySocket":          infoDisplaySocket = t; break;
                    case "MailboxSocket":              mailboxSocket = t; break;
                    case "DynamicTetherRod":           dynamicTetherRod = t; break;
                }
            }

            if (podAnimator == null && podRoot != null)
                podAnimator = podRoot.GetComponentInChildren<Animator>(true);
            if (mountAnimator == null && ceilingMountRoot != null)
                mountAnimator = ceilingMountRoot.GetComponentInChildren<Animator>(true);
        }

        /// <summary>
        /// Sets the runtime DockedPose and InspectionPose based on detected ceiling height and
        /// seated user comfort pose. Does NOT modify local workstation positions inside PodRoot.
        /// </summary>
        public void ConfigureRoomPoses(Vector3 dockedWorldPos, Vector3 inspectionWorldPos, Quaternion yawRotation)
        {
            if (dockedPose != null)
            {
                dockedPose.SetPositionAndRotation(dockedWorldPos, yawRotation);
            }
            if (inspectionPose != null)
            {
                inspectionPose.SetPositionAndRotation(inspectionWorldPos, yawRotation);
            }
            if (!IsTransitioning && podRoot != null)
            {
                Transform target = _state == PodState.DeployedIdle ? inspectionPose : dockedPose;
                if (target != null)
                {
                    podRoot.SetPositionAndRotation(target.position, target.rotation);
                    UpdateTetherVisual();
                }
            }
        }

        /// <summary>Plays the calm attention-acknowledgment gesture without leaving docked state.</summary>
        public void TriggerAttentionAcknowledgment()
        {
            if (_state != PodState.DockedIdle && _state != PodState.DeployedIdle) return;
            EnterPhase(PodState.Acknowledging, ClipAttentionAck, 0.78f, MountIdle);
        }

        /// <summary>Begins smooth deployment from ceiling dock to room-configured InspectionPose.</summary>
        public void Deploy()
        {
            _wantsDeployed = true;
            if (_state == PodState.DeployedIdle || _state == PodState.OpeningShelf || _state == PodState.Stabilizing)
                return;

            // Clean mid-flight recovery if interrupted during ascent/return
            if (_state == PodState.ClosingShelf || _state == PodState.BeginningAscent || _state == PodState.ReturningToDock)
            {
                StartVerticalTravel(GetInspectionWorldPos());
                EnterPhase(PodState.Descending, ClipDescending, _travelDuration + 0.1f, MountSpool);
                return;
            }

            EnterPhase(PodState.Unlocking, ClipUnlock, 0.80f, MountUnlock);
        }

        /// <summary>Begins smooth retraction from InspectionPose back to ceiling DockedPose.</summary>
        public void Retract()
        {
            _wantsDeployed = false;
            if (_state == PodState.DockedIdle || _state == PodState.DockLocking || _state == PodState.Settling)
                return;

            // Clean mid-flight recovery if interrupted during descent or shelf opening
            if (_state == PodState.Unlocking || _state == PodState.BeginningDescent)
            {
                StartVerticalTravel(GetDockedWorldPos());
                EnterPhase(PodState.ReturningToDock, ClipReturningDock, _travelDuration + 0.1f, MountSpool);
                return;
            }
            if (_state == PodState.Descending || _state == PodState.Stabilizing)
            {
                EnterPhase(PodState.BeginningAscent, ClipBeginAscent, 0.65f, MountSpool);
                return;
            }

            EnterPhase(PodState.ClosingShelf, ClipShelfClose, 0.78f, MountIdle);
        }

        /// <summary>Immediately snaps pod into Docked or Deployed pose (for tests, restore, or validation scenes).</summary>
        public void SnapToPoseImmediate(bool deployed)
        {
            _wantsDeployed = deployed;
            _state = deployed ? PodState.DeployedIdle : PodState.DockedIdle;
            if (podRoot != null)
            {
                Vector3 pos = deployed ? GetInspectionWorldPos() : GetDockedWorldPos();
                Quaternion rot = deployed && inspectionPose != null ? inspectionPose.rotation :
                                 (!deployed && dockedPose != null ? dockedPose.rotation : podRoot.rotation);
                podRoot.SetPositionAndRotation(pos, rot);
            }
            PlayPodClip(deployed ? ClipDeployedIdle : ClipDockedIdle, 0f);
            PlayMountClip(MountIdle, 0f);
            UpdateTetherVisual();
            StateChanged?.Invoke(_state);
        }

        /// <summary>Directly plays any of the 13 clips for art validation / inspection.</summary>
        public void PlayClipDirect(string clipName, float crossfade = 0.15f)
        {
            PlayPodClip(clipName, crossfade);
        }

        // ---- Optional Animation Event Hooks (safe if skipped) ----
        public void OnAnimUnlockComplete()      { if (_state == PodState.Unlocking) _eventReceived = true; }
        public void OnAnimBeginDescentDone()    { if (_state == PodState.BeginningDescent) _eventReceived = true; }
        public void OnAnimStabilizeComplete()   { if (_state == PodState.Stabilizing) _eventReceived = true; }
        public void OnAnimShelfOpened()         { if (_state == PodState.OpeningShelf) _eventReceived = true; }
        public void OnAnimShelfClosed()         { if (_state == PodState.ClosingShelf) _eventReceived = true; }
        public void OnAnimBeginAscentDone()     { if (_state == PodState.BeginningAscent) _eventReceived = true; }
        public void OnAnimDockLocked()          { if (_state == PodState.DockLocking) _eventReceived = true; }
        public void OnAnimSettleComplete()      { if (_state == PodState.Settling) _eventReceived = true; }

        private void Update()
        {
            if (_state == PodState.DockedIdle || _state == PodState.DeployedIdle)
            {
                UpdateTetherVisual();
                return;
            }

            _phaseTimer += Time.deltaTime;

            if (_state == PodState.Descending || _state == PodState.ReturningToDock)
            {
                UpdateVerticalTravel();
                UpdateTetherVisual();
                return;
            }

            if (_eventReceived || _phaseTimer >= _phaseTimeout)
            {
                AdvancePhase();
            }
        }

        private void AdvancePhase()
        {
            _eventReceived = false;
            switch (_state)
            {
                case PodState.Acknowledging:
                    EnterPhase(_wantsDeployed ? PodState.DeployedIdle : PodState.DockedIdle,
                               _wantsDeployed ? ClipDeployedIdle : ClipDockedIdle, 0f, MountIdle);
                    break;
                case PodState.Unlocking:
                    EnterPhase(PodState.BeginningDescent, ClipBeginDescent, 0.65f, MountSpool);
                    break;
                case PodState.BeginningDescent:
                    StartVerticalTravel(GetInspectionWorldPos());
                    EnterPhase(PodState.Descending, ClipDescending, _travelDuration + 0.25f, MountSpool);
                    break;
                case PodState.Stabilizing:
                    EnterPhase(PodState.OpeningShelf, ClipShelfOpen, 0.85f, MountIdle);
                    break;
                case PodState.OpeningShelf:
                    EnterPhase(PodState.DeployedIdle, ClipDeployedIdle, 0f, MountIdle);
                    break;
                case PodState.ClosingShelf:
                    EnterPhase(PodState.BeginningAscent, ClipBeginAscent, 0.65f, MountSpool);
                    break;
                case PodState.BeginningAscent:
                    StartVerticalTravel(GetDockedWorldPos());
                    EnterPhase(PodState.ReturningToDock, ClipReturningDock, _travelDuration + 0.25f, MountSpool);
                    break;
                case PodState.DockLocking:
                    EnterPhase(PodState.Settling, ClipMechanicalSettle, 0.70f, MountIdle);
                    break;
                case PodState.Settling:
                    EnterPhase(PodState.DockedIdle, ClipDockedIdle, 0f, MountIdle);
                    break;
            }
        }

        private void StartVerticalTravel(Vector3 targetWorldPos)
        {
            _travelStartWorld = podRoot != null ? podRoot.position : transform.position;
            _travelTargetWorld = targetWorldPos;
            float dist = Vector3.Distance(_travelStartWorld, _travelTargetWorld);
            float rawDur = dist / Mathf.Max(0.15f, verticalSpeedMetersPerSec);
            _travelDuration = Mathf.Clamp(rawDur, minTravelSeconds, maxTravelSeconds);
            _travelElapsed = 0f;
        }

        private void UpdateVerticalTravel()
        {
            if (podRoot == null)
            {
                if (_state == PodState.Descending)
                    EnterPhase(PodState.Stabilizing, ClipStabilize, 0.92f, MountIdle);
                else
                    EnterPhase(PodState.DockLocking, ClipDockLock, 0.82f, MountLock);
                return;
            }

            _travelElapsed += Time.deltaTime;
            float u = Mathf.Clamp01(_travelElapsed / Mathf.Max(0.05f, _travelDuration));
            // Smooth, deliberate, restrained easing (quintic-like via double SmoothStep)
            float k = Mathf.SmoothStep(0f, 1f, Mathf.SmoothStep(0f, 1f, u));
            podRoot.position = Vector3.Lerp(_travelStartWorld, _travelTargetWorld, k);

            if (u >= 1f)
            {
                podRoot.position = _travelTargetWorld;
                if (_state == PodState.Descending)
                {
                    EnterPhase(PodState.Stabilizing, ClipStabilize, 0.92f, MountIdle);
                }
                else
                {
                    EnterPhase(PodState.DockLocking, ClipDockLock, 0.82f, MountLock);
                }
            }
        }

        private void UpdateTetherVisual()
        {
            if (dynamicTetherRod == null || suspensionTop == null) return;
            Transform bottom = dockingConnector != null ? dockingConnector : suspensionBottom;
            if (bottom == null) return;

            Vector3 a = suspensionTop.position;
            Vector3 b = bottom.position;
            Vector3 delta = b - a;
            float len = delta.magnitude;

            if (len < 0.005f)
            {
                dynamicTetherRod.gameObject.SetActive(false);
                return;
            }
            if (!dynamicTetherRod.gameObject.activeSelf)
                dynamicTetherRod.gameObject.SetActive(true);

            dynamicTetherRod.position = (a + b) * 0.5f;
            dynamicTetherRod.up = delta / len;
            Vector3 ls = dynamicTetherRod.localScale;
            float invParentY = dynamicTetherRod.parent != null && Mathf.Abs(dynamicTetherRod.parent.lossyScale.y) > 1e-5f
                ? 1f / Mathf.Abs(dynamicTetherRod.parent.lossyScale.y)
                : 1f;
            ls.y = (len / Mathf.Max(0.01f, tetherBaseLength)) * invParentY;
            dynamicTetherRod.localScale = ls;
        }

        private void EnterPhase(PodState nextState, string podClip, float timeoutSeconds, string mountClip)
        {
            _state = nextState;
            _phaseTimer = 0f;
            _phaseTimeout = timeoutSeconds;
            _eventReceived = false;
            PlayPodClip(podClip, 0.16f);
            PlayMountClip(mountClip, 0.16f);
            StateChanged?.Invoke(_state);
        }

        private Vector3 GetDockedWorldPos()
        {
            if (dockedPose != null) return dockedPose.position;
            if (ceilingAnchor != null) return ceilingAnchor.position + Vector3.down * 0.28f;
            return transform.position;
        }

        private Vector3 GetInspectionWorldPos()
        {
            if (inspectionPose != null) return inspectionPose.position;
            if (ceilingAnchor != null) return ceilingAnchor.position + Vector3.down * 1.35f;
            return transform.position + Vector3.down * 1.1f;
        }

        private void PlayPodClip(string clipName, float crossfade)
        {
            if (podAnimator == null || string.IsNullOrEmpty(clipName)) return;
            int hash = Animator.StringToHash(clipName);
            if (podAnimator.HasState(0, hash))
                podAnimator.CrossFadeInFixedTime(hash, crossfade, 0);
        }

        private void PlayMountClip(string clipName, float crossfade)
        {
            if (mountAnimator == null || string.IsNullOrEmpty(clipName)) return;
            int hash = Animator.StringToHash(clipName);
            if (mountAnimator.HasState(0, hash))
                mountAnimator.CrossFadeInFixedTime(hash, crossfade, 0);
        }
    }
}

