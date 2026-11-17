using System;
using UnityEngine;

namespace MuseDesk.Spatial
{
    /// <summary>
    /// What the user asked for from a distance, as static events, so the reach side (which raises
    /// them from a key, a hand ray or a voice phrase) needs no reference to the placement side
    /// (which subscribes and decides whether and how to move the office). They are REQUESTS: the
    /// placement director may refuse or delay one (for example while
    /// <see cref="SpatialWorkspaceState.TransitionHeld"/> is set). The reach side's UI changes
    /// only when <see cref="SpatialWorkspaceState.Changed"/> reports the new state.
    ///
    /// Subscribers:
    ///   <see cref="BringOfficeHere"/>, <see cref="SendOfficeBack"/>: the placement director.
    ///   <see cref="ShowLatestUpdate"/>, <see cref="AskAgent"/>: <see cref="PerchController"/>.
    /// Nothing here starts, stops or changes backend work.
    /// </summary>
    public static class SummonRequests
    {
        /// <summary>Bring the office within reach (Floor: beside the seat; Ceiling: lower the pod).</summary>
        public static event Action BringOfficeHere;
        /// <summary>Send the office back to its remote place.</summary>
        public static event Action SendOfficeBack;
        /// <summary>Show the most recent update near the user.</summary>
        public static event Action ShowLatestUpdate;
        /// <summary>Show what one agent is doing (agent id: researcher / developer / tester).</summary>
        public static event Action<string> AskAgent;

        /// <summary>How many requests of any kind have been raised (drivers and tests).</summary>
        public static int RaisedCount { get; private set; }
        /// <summary>Name of the last request raised, or null.</summary>
        public static string LastRaised { get; private set; }

        public static void RaiseBringOfficeHere() { Note("BringOfficeHere"); Invoke(BringOfficeHere); }
        public static void RaiseSendOfficeBack() { Note("SendOfficeBack"); Invoke(SendOfficeBack); }
        public static void RaiseShowLatestUpdate() { Note("ShowLatestUpdate"); Invoke(ShowLatestUpdate); }

        public static void RaiseAskAgent(string agentId)
        {
            if (string.IsNullOrEmpty(agentId)) return;
            Note("AskAgent");
            var h = AskAgent;
            if (h == null) return;
            try { h(agentId); } catch (Exception e) { Debug.LogException(e); }
        }

        /// <summary>Drop every subscriber and counter (domain reload off, tests, scene reload).</summary>
        public static void ResetForTests()
        {
            BringOfficeHere = null; SendOfficeBack = null; ShowLatestUpdate = null; AskAgent = null;
            RaisedCount = 0; LastRaised = null;
        }

        static void Note(string name) { RaisedCount++; LastRaised = name; }

        static void Invoke(Action h)
        {
            if (h == null) return;
            try { h(); } catch (Exception e) { Debug.LogException(e); }
        }
    }
}
