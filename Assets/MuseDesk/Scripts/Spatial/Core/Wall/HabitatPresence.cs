namespace MuseDesk.Spatial.Wall
{
    /// <summary>What a habitat character visibly does at its desk. Derived ONLY from authoritative task state.</summary>
    public enum DeskActivity
    {
        /// <summary>No task, queued, or finished: calm idle (never fake typing).</summary>
        Idle = 0,
        /// <summary>The task is running: the role's work pose.</summary>
        Working = 1,
        /// <summary>Needs the user (approval, input, paused): attentive waiting.</summary>
        Waiting = 2,
        /// <summary>Blocked on other tasks: thinking, not working.</summary>
        Blocked = 3,
        /// <summary>Failed / error: a subtle worried pose, never a celebration.</summary>
        Problem = 4,
    }

    /// <summary>
    /// Presentation mapping for habitat characters (polish spec 6: "animations must reflect real task or interaction
    /// state rather than fabricate activity"). Pure and testable; the view chooses clips from the result.
    /// </summary>
    public static class HabitatPresence
    {
        /// <summary>Map a v1/v2 task state string (or null for "no task") to the desk activity.</summary>
        public static DeskActivity FromTaskState(string state)
        {
            if (string.IsNullOrEmpty(state)) return DeskActivity.Idle;
            string s = state.ToLowerInvariant();
            if (s.Contains("fail") || s.Contains("error")) return DeskActivity.Problem;
            if (s.Contains("approval") || s.Contains("input") || s.Contains("pause") || s == "awaiting") return DeskActivity.Waiting;
            if (s.Contains("block")) return DeskActivity.Blocked;
            if (s.Contains("run") || s.Contains("progress") || s.Contains("working") || s.Contains("started") || s.Contains("active")) return DeskActivity.Working;
            return DeskActivity.Idle;   // queued, completed, cancelled, unknown
        }
    }
}
