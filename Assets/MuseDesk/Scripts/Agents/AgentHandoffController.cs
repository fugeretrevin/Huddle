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
        readonly Queue<(string src, string dst)> _queue = new Queue<(string, string)>();
        bool _bound;
        bool _busy;
        GameObject _destGo;
        Vector3 _propWorldScale = Vector3.one;
        bool _propScaleCaptured;

        /// <summary>True while a handoff owns an agent's movement. AgentRegistry reads this so its
        /// own "react, then walk home" does not cut a handoff walk short.</summary>
        public static bool AnyBusy { get; private set; }

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
            // A disabled component's coroutines stop; don't stay latched busy or leak the marker.
            _queue.Clear();
            if (_busy) AnyBusy = false;
            _busy = false;
            if (_destGo != null) { Destroy(_destGo); _destGo = null; }
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
            // Focus mode suppresses routine handoff choreography (the backend transfer already
            // happened; this is presentation only, so skipping it changes no task state).
            if (MuseDesk.Workspace.FocusModeController.Active) return;
            if (!_byId.ContainsKey(sourceId) || !_byId.ContainsKey(targetId)) return;
            // Queue rather than drop: the developer→tester handoff routinely arrives while the
            // researcher→developer one is still walking. Bounded so a burst can't back up forever.
            if (_queue.Count >= 4) return;
            _queue.Enqueue((sourceId, targetId));
            if (!_busy) StartCoroutine(Drain());
        }

        IEnumerator Drain()
        {
            _busy = AnyBusy = true;
            while (_queue.Count > 0)
            {
                var (s, t) = _queue.Dequeue();
                if (_byId.TryGetValue(s, out var a) && a != null && _byId.TryGetValue(t, out var b) && b != null)
                    yield return HandoffRoutine(a, b);
            }
            _busy = AnyBusy = false;
        }

        IEnumerator HandoffRoutine(AgentView source, AgentView target)
        {
            var srcMove = source.GetComponent<AgentMovement>();
            var srcBridge = source.GetComponent<AgentAnimatorBridge>();
            var dstBridge = target.GetComponent<AgentAnimatorBridge>();

            if (srcMove == null) yield break;

            // The courier carries the token for the whole walk (not just at the give beat).
            if (artifactProp != null)
            {
                var srcHand = FindChild(source.transform, "RightHand_Attach");
                AttachProp(srcHand != null ? srcHand : source.transform);
            }

            // Destination a short stop-distance in front of the receiver (kept alive for the whole move).
            if (_destGo != null) Destroy(_destGo);
            var destGo = _destGo = new GameObject("HandoffDest");
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
                AttachProp(targetHand != null ? targetHand : target.transform);
            }

            yield return new WaitForSeconds(exchangeHold);

            // The receiver has the work now; put the token away so it doesn't hang in its hand forever.
            if (artifactProp != null) artifactProp.gameObject.SetActive(false);

            srcMove.ReturnHome();
            guard = 0f;
            yield return null;
            while (srcMove.IsMoving && guard < 6f) { guard += Time.deltaTime; yield return null; }

            if (destGo != null) Destroy(destGo);
            if (_destGo == destGo) _destGo = null;
        }

        // Parent the token to a hand while keeping its authored WORLD size (rig bones are not unit scale).
        void AttachProp(Transform hand)
        {
            if (!_propScaleCaptured) { _propWorldScale = artifactProp.lossyScale; _propScaleCaptured = true; }
            artifactProp.gameObject.SetActive(true);
            artifactProp.SetParent(hand, false);
            artifactProp.localPosition = Vector3.zero;
            artifactProp.localRotation = Quaternion.identity;
            Vector3 p = hand.lossyScale;
            artifactProp.localScale = new Vector3(
                _propWorldScale.x / Mathf.Max(1e-5f, Mathf.Abs(p.x)),
                _propWorldScale.y / Mathf.Max(1e-5f, Mathf.Abs(p.y)),
                _propWorldScale.z / Mathf.Max(1e-5f, Mathf.Abs(p.z)));
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
