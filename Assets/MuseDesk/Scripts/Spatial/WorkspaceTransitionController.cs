using UnityEngine;
using MuseDesk.Agents;

namespace MuseDesk.Spatial
{
    /// <summary>
    /// The ONLY writer of WorkspaceRoot's transform while a spatial mode (Floor, Ceiling) is active
    /// or a mode change is in flight. It wraps one <see cref="WorkspaceTransitionMachine"/>: eased
    /// moves of position, rotation and uniform scale; one move at a time; refuses to start while
    /// <see cref="SpatialWorkspaceState.TransitionHeld"/>; waits (bounded) for agents to stop
    /// walking, because AgentMovement lerps from a WORLD-space start point and a hand-over walks
    /// to a world-space marker, so moving the root under a walking agent would make it trail and
    /// snap. Standing agents are children of the root and simply move with it.
    ///
    /// In Desk mode at rest it writes nothing: WorkspacePlacement owns the root there.
    /// </summary>
    public class WorkspaceTransitionController : MonoBehaviour
    {
        [Tooltip("The office root (WorkspaceRoot).")]
        public Transform workspaceRoot;
        [Tooltip("Longest wait for agents to finish walking before a move is given up.")]
        public float maxAgentWaitSeconds = 6f;
        [Tooltip("In Floor / Ceiling mode, put the root back if another script moved it.")]
        public bool enforceOwnership = true;

        /// <summary>Set by the director, which then ticks the machine itself (fixed order).</summary>
        [System.NonSerialized] public bool tickedByDirector;

        WorkspaceTransitionMachine _machine;
        AgentMovement[] _agents;
        RootPose _lastWritten;
        bool _hasWritten;
        bool _warnedOwnership;

        public WorkspaceTransitionMachine Machine
        {
            get
            {
                if (_machine == null)
                {
                    _machine = new WorkspaceTransitionMachine(new UnityUnscaledClock(), ReadRoot, WriteRoot, IsHeld, AgentsBusy);
                    _machine.maxAgentWaitSeconds = maxAgentWaitSeconds;
                }
                return _machine;
            }
        }

        public bool IsRunning => _machine != null && _machine.IsRunning;

        void Start()
        {
            CacheAgents();
        }

        void CacheAgents()
        {
            _agents = workspaceRoot != null ? workspaceRoot.GetComponentsInChildren<AgentMovement>(true) : new AgentMovement[0];
        }

        RootPose ReadRoot()
        {
            if (workspaceRoot == null) return new RootPose(Vector3.zero, Quaternion.identity, 1f);
            return new RootPose(workspaceRoot.position, workspaceRoot.rotation, workspaceRoot.localScale.x);
        }

        void WriteRoot(RootPose pose)
        {
            if (workspaceRoot == null) return;
            // Prop-carrying hook: loose physics props are frozen to the office before it moves.
            WorkspacePropCarrier.NotifyRootWrite();
            workspaceRoot.SetPositionAndRotation(pose.position, pose.rotation);
            workspaceRoot.localScale = new Vector3(pose.scale, pose.scale, pose.scale);
            _lastWritten = pose;
            _hasWritten = true;
        }

        static bool IsHeld() { return SpatialWorkspaceState.TransitionHeld; }

        /// <summary>True while a hand-over runs or any agent is walking.</summary>
        bool AgentsBusy()
        {
            if (AgentHandoffController.AnyBusy) return true;
            if (_agents == null) CacheAgents();
            for (int i = 0; i < _agents.Length; i++)
            {
                AgentMovement a = _agents[i];
                if (a != null && a.IsMoving) return true;
            }
            return false;
        }

        void Update()
        {
            if (!tickedByDirector && _machine != null) _machine.Tick();
        }

        void LateUpdate()
        {
            if (!enforceOwnership || !_hasWritten || _machine == null || _machine.IsRunning || workspaceRoot == null) return;
            if (SpatialWorkspaceState.Mode == WorkspaceMode.Desk) return;      // WorkspacePlacement owns the root in Desk mode
            if (ReadRoot().Approximately(_lastWritten, 0.001f, 0.2f)) return;
            WriteRoot(_lastWritten);
            if (!_warnedOwnership)
            {
                _warnedOwnership = true;
                Debug.LogWarning("[MuseDesk] Another script moved WorkspaceRoot while a spatial mode is active; it was put back. " +
                                 "Only WorkspaceTransitionController may move the root outside Desk mode.");
            }
        }

        void OnApplicationPause(bool paused)
        {
            if (paused && !tickedByDirector && _machine != null) _machine.FinishNow();
        }
    }
}
