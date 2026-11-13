using UnityEngine;
using MuseDesk.Models;
using MuseDesk.Networking;

namespace MuseDesk.Art
{
    /// <summary>
    /// Drives the miniature Software Factory visuals from REAL backend events only — never fake
    /// progress. Subscribes to the SessionReducer and maps canonical agent/task/decision events to
    /// the SoftwareFactoryVisualController's 10 production-stage hooks. Visual-only: it never advances
    /// task state (AGENTS invariants #1/#2 — presentation reflects acknowledged backend state).
    /// </summary>
    [DisallowMultipleComponent]
    public class FactoryEventController : MonoBehaviour
    {
        public SessionController controller;
        public SoftwareFactoryVisualController factory;

        /// <summary>Set (at run time) by a component that sequences the delivery visuals itself, the
        /// completion sequence. While true, this controller does not start its own "test gate, then
        /// delivery" on WorkDelivered; the owner calls <see cref="PlayDeliveryGate"/> and
        /// <see cref="PlayDeliveryTransport"/> at its own times. False (the default) keeps the
        /// original behaviour.</summary>
        public bool SequencedDelivery { get; set; }

        /// <summary>First delivery beat: light the green test gate, but only if real passing test
        /// evidence arrived with the artifact (or <paramref name="force"/>, for an Editor preview).
        /// Consumes that evidence. Returns true if the gate was lit.</summary>
        public bool PlayDeliveryGate(bool force = false)
        {
            bool passed = _testsPassedSeen || force;
            _testsPassedSeen = false;
            if (factory == null || !passed) return false;
            factory.PlayTestSuccess();
            return true;
        }

        /// <summary>Second delivery beat: the completed artifact travels to the output.</summary>
        public void PlayDeliveryTransport()
        {
            if (factory != null) factory.PlayCompletedDelivery();
        }

        /// <summary>True while passing test evidence from the last artifact is waiting to be shown.</summary>
        public bool HasPassingTestEvidence => _testsPassedSeen;

        bool _bound;
        bool _testsPassedSeen;
        Coroutine _delivery;
        readonly System.Collections.Generic.Dictionary<string, string> _lastState =
            new System.Collections.Generic.Dictionary<string, string>();

        void Update()
        {
            // SessionController builds its gateway/reducer asynchronously; bind once it is live.
            if (!_bound && controller != null) Bind();
        }

        void Bind()
        {
            controller.Reducer.AgentChanged += OnAgentChanged;
            controller.Reducer.Applied += OnApplied;
            controller.Reducer.WorkDelivered += OnTaskCompleted;
            controller.Reducer.ArtifactCreated += OnArtifactCreated;
            controller.Reducer.DecisionResolved += OnDecisionResolved;
            controller.Reducer.SessionReset += OnSessionReset;
            _bound = true;
        }

        void OnDisable()
        {
            if (!_bound || controller == null) return;
            controller.Reducer.AgentChanged -= OnAgentChanged;
            controller.Reducer.Applied -= OnApplied;
            controller.Reducer.WorkDelivered -= OnTaskCompleted;
            controller.Reducer.ArtifactCreated -= OnArtifactCreated;
            controller.Reducer.DecisionResolved -= OnDecisionResolved;
            controller.Reducer.SessionReset -= OnSessionReset;
            _bound = false;
        }

        // A real agent begins a work phase → the matching factory stage activates:
        //   researching → blueprint printer sweeps;  coding → build chamber assembles the artifact;
        //   testing → QA inspection chamber activates. One visual stage per real agent-state change.
        void OnAgentChanged(AgentView a)
        {
            if (factory == null || a == null || string.IsNullOrEmpty(a.AgentId)) return;
            // Act on state TRANSITIONS only: progress-only status events repeat the same state and
            // must not restart the stage clip.
            _lastState.TryGetValue(a.AgentId, out var prev);
            if (prev == a.State) return;
            _lastState[a.AgentId] = a.State;

            switch (a.State)
            {
                case AgentStates.Researching: factory.PlayBlueprintPrint(); break;
                case AgentStates.Coding:      factory.PlayArtifactAssemble(); break;
                case AgentStates.Testing:     factory.PlayTestingActivate(); break;
                case AgentStates.Complete:
                    if (a.AgentId == "developer" && prev == AgentStates.Coding) factory.PlayDevelopmentComplete();
                    break;
            }
        }

        // A produced artifact glides along the conveyor between stations. Its real test evidence
        // (tests_passed / tests_failed in the payload) is remembered for the completion stage.
        void OnArtifactCreated(EventEnvelope e)
        {
            if (factory == null) return;
            if (e != null && e.Payload != null)
            {
                int passed = e.Payload.Value<int?>("tests_passed") ?? 0;
                int failed = e.Payload.Value<int?>("tests_failed") ?? 0;
                _testsPassedSeen = passed > 0 && failed == 0;
            }
            factory.PlayArtifactTransport();
        }

        // Backend confirms the task is done → green test gate (only if real passing test evidence
        // arrived with the artifact), then the completed artifact is delivered to the shelf.
        void OnTaskCompleted(EventEnvelope e)
        {
            if (factory == null) return;
            if (SequencedDelivery) return;   // the completion sequence plays the two beats itself
            if (_delivery != null) StopCoroutine(_delivery);
            _delivery = StartCoroutine(SuccessThenDeliver(_testsPassedSeen));
            _testsPassedSeen = false;
        }

        System.Collections.IEnumerator SuccessThenDeliver(bool testsPassed)
        {
            if (testsPassed)
            {
                factory.PlayTestSuccess();
                yield return new WaitForSeconds(1.2f);
            }
            factory.PlayCompletedDelivery();
            _delivery = null;
        }

        // Replay: forget per-agent state and pending test evidence so stages fire again next run.
        void OnSessionReset()
        {
            _lastState.Clear();
            _testsPassedSeen = false;
            if (_delivery != null) { StopCoroutine(_delivery); _delivery = null; }
        }

        // task.failed → the red reject lamp/chute fires (routed off the generic Applied stream).
        void OnApplied(EventEnvelope e)
        {
            if (factory == null || e == null) return;
            if (e.Type == EventTypes.TaskFailed) factory.PlayTestFailed();
        }

        // The human's real decision: approved → the blueprint moves on to development (an approval is
        // NOT a test result, so it never lights the test-pass gate); rejected/changes → reject return.
        void OnDecisionResolved(string decisionId, string state)
        {
            if (factory == null) return;
            string s = (state ?? "").ToLowerInvariant();
            if (s.Contains("approv")) factory.PlayBlueprintTransfer();
            else factory.PlayRejectedReturn();
        }
    }
}
