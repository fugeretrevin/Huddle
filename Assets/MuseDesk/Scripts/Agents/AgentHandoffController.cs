using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using MuseDesk.Models;
using MuseDesk.Networking;

namespace MuseDesk.Agents
{
    /// <summary>
    /// Agent-to-agent handoff choreography driven by REAL backend events (never fake progress).
    /// On an approved decision the researcher carries the work to the developer; on a new artifact
    /// the developer carries it to the tester. Movement uses AgentMovement.DeliverTo/ReturnHome and
    /// the reactions use the validated AgentAnimatorBridge give/receive triggers. If the backend
    /// workflow fires without a visual (e.g. an interrupted animation), state is unaffected — this
    /// is presentation only. One handoff runs at a time.
    /// </summary>
    public class AgentHandoffController : MonoBehaviour
    {
        public SessionController controller;
        public AgentRegistry registry;

        [Tooltip("Optional carried token; reparented from source hand to target hand on give. May be null.")]
        public Transform artifactProp;

        [Tooltip("How close (m) the courier stops to the receiver before the give/receive beat.")]
        public float stopDistance = 0.18f;
        [Tooltip("Seconds the give/receive pose holds before the courier walks home.")]
        public float exchangeHold = 1.0f;

        readonly Dictionary<string, AgentView> _byId = new Dictionary<string, AgentView>();
        bool _bound;
        bool _busy;

        void Update()
        {
            if (!_bound && controller != null) Bind();
        }

        void Bind()
        {
            // Reducer is always constructed on the controller, so it is safe to read here.
            _byId.Clear();
            if (registry != null && registry.agents != null)
                foreach (var a in registry.agents)
                    if (a != null && !string.IsNullOrEmpty(a.agentId)) _byId[a.agentId] = a;

            controller.Reducer.DecisionResolved += OnDecisionResolved;
            controller.Reducer.ArtifactCreated += OnArtifactCreated;
            _bound = true;
        }

        void OnDisable()
        {
            if (_bound && controller != null)
            {
                controller.Reducer.DecisionResolved -= OnDecisionResolved;
                controller.Reducer.ArtifactCreated -= OnArtifactCreated;
            }
            _bound = false;
        }

        void OnDecisionResolved(string decisionId, string state)
        {
            string s = (state ?? "").ToLowerInvariant();
            if (!s.Contains("approv")) return;                 // only a greenlit decision hands work onward
            TryHandoff("researcher", "developer");
        }

        void OnArtifactCreated(EventEnvelope e)
        {
            // A produced artifact moves from the developer to the tester for verification.
            TryHandoff("developer", "tester");
        }

        void TryHandoff(string sourceId, string targetId)
        {
            if (_busy) return;
            // Focus mode suppresses routine handoff choreography (the backend transfer already
            // happened; this is presentation only, so skipping it changes no task state).
            if (MuseDesk.Workspace.FocusModeController.Active) return;
            if (!_byId.TryGetValue(sourceId, out var source) || source == null) return;
            if (!_byId.TryGetValue(targetId, out var target) || target == null) return;
            StartCoroutine(HandoffRoutine(source, target));
        }

        IEnumerator HandoffRoutine(AgentView source, AgentView target)
        {
            _busy = true;

            var srcMove = source.GetComponent<AgentMovement>();
            var srcBridge = source.GetComponent<AgentAnimatorBridge>();
            var dstBridge = target.GetComponent<AgentAnimatorBridge>();

            if (srcMove == null) { _busy = false; yield break; }

            // Destination a short stop-distance in front of the receiver (kept alive for the whole move).
            var destGo = new GameObject("HandoffDest");
            var dest = destGo.transform;
            Vector3 toward = source.transform.position - target.transform.position;
            toward.y = 0f;
            if (toward.sqrMagnitude < 1e-4f) toward = Vector3.back;
            dest.position = target.transform.position + toward.normalized * stopDistance;
            dest.position = new Vector3(dest.position.x, source.transform.position.y, dest.position.z);

            srcMove.DeliverTo(dest, null);

            // Wait for arrival (IsMoving falls to false), with a safety guard.
            float guard = 0f;
            yield return null; // let DeliverTo set IsMoving=true first
            while (srcMove.IsMoving && guard < 6f) { guard += Time.deltaTime; yield return null; }

            // Give / receive beat.
            if (srcBridge != null) srcBridge.PlayHandoffGive();
            if (dstBridge != null) dstBridge.PlayHandoffReceive();

            if (artifactProp != null)
            {
                var targetHand = FindChild(target.transform, "RightHand_Attach");
                if (targetHand == null) targetHand = target.transform;
                artifactProp.gameObject.SetActive(true);
                artifactProp.SetParent(targetHand, false);
                artifactProp.localPosition = Vector3.zero;
                artifactProp.localRotation = Quaternion.identity;
            }

            yield return new WaitForSeconds(exchangeHold);

            srcMove.ReturnHome();
            guard = 0f;
            yield return null;
            while (srcMove.IsMoving && guard < 6f) { guard += Time.deltaTime; yield return null; }

            if (destGo != null) Destroy(destGo);
            _busy = false;
        }

        static Transform FindChild(Transform root, string childName)
        {
            var all = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
                if (all[i].name == childName) return all[i];
            return null;
        }
    }
}
