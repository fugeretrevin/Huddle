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

        bool _bound;

        void Update()
        {
            // SessionController builds its gateway/reducer asynchronously; bind once it is live.
            if (!_bound && controller != null) Bind();
        }

        void Bind()
        {
            controller.Reducer.AgentChanged += OnAgentChanged;
            controller.Reducer.Applied += OnApplied;
            controller.Reducer.TaskCompleted += OnTaskCompleted;
            controller.Reducer.ArtifactCreated += OnArtifactCreated;
            controller.Reducer.DecisionResolved += OnDecisionResolved;
            _bound = true;
        }

        void OnDisable()
        {
            if (!_bound || controller == null) return;
            controller.Reducer.AgentChanged -= OnAgentChanged;
            controller.Reducer.Applied -= OnApplied;
            controller.Reducer.TaskCompleted -= OnTaskCompleted;
            controller.Reducer.ArtifactCreated -= OnArtifactCreated;
            controller.Reducer.DecisionResolved -= OnDecisionResolved;
            _bound = false;
        }

        // A real agent begins a work phase → the matching factory stage activates:
        //   researching → blueprint printer sweeps;  coding → build chamber assembles the artifact;
        //   testing → QA inspection chamber activates. One visual stage per real agent-state change.
        void OnAgentChanged(AgentView a)
        {
            if (factory == null || a == null) return;
            switch (a.State)
            {
                case AgentStates.Researching: factory.PlayBlueprintPrint(); break;
                case AgentStates.Coding:      factory.PlayArtifactAssemble(); break;
                case AgentStates.Testing:     factory.PlayTestingActivate(); break;
            }
        }

        // A produced artifact glides along the conveyor between stations.
        void OnArtifactCreated(EventEnvelope e)
        {
            if (factory == null) return;
            factory.PlayArtifactTransport();
        }

        // Backend confirms the task is done → the completed artifact is delivered to the shelf.
        void OnTaskCompleted(EventEnvelope e)
        {
            if (factory == null) return;
            factory.PlayCompletedDelivery();
        }

        // task.failed → the red reject lamp/chute fires (routed off the generic Applied stream).
        void OnApplied(EventEnvelope e)
        {
            if (factory == null || e == null) return;
            if (e.Type == EventTypes.TaskFailed) factory.PlayTestFailed();
        }

        // The human's real decision: approved → green test-pass gate; rejected/changes → reject return.
        void OnDecisionResolved(string decisionId, string state)
        {
            if (factory == null) return;
            string s = (state ?? "").ToLowerInvariant();
            if (s.Contains("approv")) factory.PlayTestSuccess();
            else factory.PlayRejectedReturn();
        }
    }
}
