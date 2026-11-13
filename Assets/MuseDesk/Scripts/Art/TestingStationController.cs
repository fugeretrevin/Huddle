using System.Collections;
using UnityEngine;
using MuseDesk.Models;
using MuseDesk.Networking;

namespace MuseDesk.Art
{
    /// <summary>
    /// Drives the testing workstation's pass/fail lamps + scanner ring from REAL backend events,
    /// never a simulated timer. Tester enters a working state -> Testing; its run completing -> Passed;
    /// TaskFailed -> Failed; a rejected/changes decision -> Blocked. After a pass/fail pulse the
    /// lamp settles back to Idle. The backend remains authoritative: if a visual is missed, task
    /// state is unaffected (presentation only).
    /// </summary>
    public class TestingStationController : MonoBehaviour
    {
        public SessionController controller;
        public WorkstationVisualController testingStation;

        [Tooltip("Agent id whose working state means the test machine is active.")]
        public string testerAgentId = "tester";
        [Range(0.5f, 6f)] public float resultHold = 2.5f;

        bool _bound;
        Coroutine _settle;
        string _lastTesterState = "";

        /// <summary>True once the tester's own run went from testing to complete (a real pass) and
        /// until it tests again, fails, or the session is reset. The completion sequence reads it so
        /// the pass lamp is only ever shown for a pass the backend reported.</summary>
        public bool TesterPassed { get; private set; }

        /// <summary>Show a result on the lamps for <paramref name="holdSeconds"/>, then settle to
        /// idle. For callers that sequence a result the reducer has already confirmed (the completion
        /// sequence); it changes no state.</summary>
        public void ShowResult(WorkstationVisualController.TestingVisualState state, float holdSeconds)
        {
            if (testingStation == null || !isActiveAndEnabled) return;
            if (_settle != null) StopCoroutine(_settle);
            _settle = StartCoroutine(PulseRoutine(state, Mathf.Max(0.2f, holdSeconds)));
        }

        void Update() { if (!_bound && controller != null) Bind(); }

        void Bind()
        {
            controller.Reducer.AgentChanged += OnAgentChanged;
            controller.Reducer.DecisionResolved += OnDecisionResolved;
            controller.Reducer.Applied += OnApplied;
            controller.Reducer.SessionReset += OnSessionReset;
            _bound = true;
            if (testingStation != null) testingStation.SetTestingVisualState(WorkstationVisualController.TestingVisualState.Idle);
        }

        void OnDisable()
        {
            if (!_bound || controller == null) return;
            controller.Reducer.AgentChanged -= OnAgentChanged;
            controller.Reducer.DecisionResolved -= OnDecisionResolved;
            controller.Reducer.Applied -= OnApplied;
            controller.Reducer.SessionReset -= OnSessionReset;
            _bound = false;
        }

        void OnAgentChanged(AgentView a)
        {
            if (a == null || a.AgentId != testerAgentId || testingStation == null) return;
            string s = (a.State ?? "").ToLowerInvariant();
            bool wasTesting = _lastTesterState.Contains("test");
            if (s == _lastTesterState) return;          // progress-only update, same state
            _lastTesterState = s;
            if (wasTesting && s.Contains("complete")) TesterPassed = true;
            else if (s.Contains("test") || s.Contains("error") || s.Contains("fail")) TesterPassed = false;

            if (s.Contains("test")) Set(WorkstationVisualController.TestingVisualState.Testing);
            else if (s.Contains("block") || s.Contains("wait")) Set(WorkstationVisualController.TestingVisualState.Blocked);
            // The tester's OWN run finishing is the test result: testing → complete is a real pass,
            // testing → error a real failure. (task.completed is not used: it also fires for
            // non-test work and for a cancelled branch.)
            else if (s.Contains("complete"))
            {
                if (wasTesting) PulseThenSettle(WorkstationVisualController.TestingVisualState.Passed);
                else if (_settle == null) Set(WorkstationVisualController.TestingVisualState.Idle);
            }
            else if (s.Contains("error") || s.Contains("fail"))
                PulseThenSettle(WorkstationVisualController.TestingVisualState.Failed);
            else if (s.Contains("idle") && _settle == null)
                Set(WorkstationVisualController.TestingVisualState.Idle);
        }

        // Replay: lamps back to idle, and forget the last tester state so the next run reads fresh.
        void OnSessionReset()
        {
            _lastTesterState = "";
            TesterPassed = false;
            Set(WorkstationVisualController.TestingVisualState.Idle);
        }

        void OnApplied(EventEnvelope e)
        {
            // TaskFailed has no dedicated reducer event; catch it from the raw applied stream.
            if (e != null && e.Type == EventTypes.TaskFailed)
                PulseThenSettle(WorkstationVisualController.TestingVisualState.Failed);
        }


        void OnDecisionResolved(string decisionId, string state)
        {
            string s = (state ?? "").ToLowerInvariant();
            if (s.Contains("reject") || s.Contains("change"))
                PulseThenSettle(WorkstationVisualController.TestingVisualState.Blocked);
        }

        void Set(WorkstationVisualController.TestingVisualState st)
        {
            if (_settle != null) { StopCoroutine(_settle); _settle = null; }
            if (testingStation != null) testingStation.SetTestingVisualState(st);
        }

        void PulseThenSettle(WorkstationVisualController.TestingVisualState st)
        {
            if (testingStation == null) return;
            if (_settle != null) StopCoroutine(_settle);
            _settle = StartCoroutine(PulseRoutine(st, resultHold));
        }

        IEnumerator PulseRoutine(WorkstationVisualController.TestingVisualState st, float hold)
        {
            testingStation.SetTestingVisualState(st);
            yield return new WaitForSeconds(hold);
            testingStation.SetTestingVisualState(WorkstationVisualController.TestingVisualState.Idle);
            _settle = null;
        }
    }
}
