using System;

namespace MuseDesk.Spatial.Onboarding
{
    /// <summary>Detects the moment a boolean turns true (for example a run starting). Pure.</summary>
    public sealed class RiseEdge
    {
        bool _last;
        /// <summary>Feed the current value. True only on the false-to-true step.</summary>
        public bool Update(bool now)
        {
            bool rose = now && !_last;
            _last = now;
            return rose;
        }
        public void Reset() { _last = false; }
    }

    /// <summary>
    /// Detects a change of an integer (a workspace mode, say). The first value seen is only the baseline,
    /// so a mode the app starts in never counts as the user moving the office. Pure.
    /// </summary>
    public sealed class ChangeEdge
    {
        bool _have;
        int _last;
        /// <summary>True when the value differs from the previous one (never on the first call).</summary>
        public bool Update(int now)
        {
            if (!_have) { _have = true; _last = now; return false; }
            bool changed = now != _last;
            _last = now;
            return changed;
        }
        public int Last { get { return _last; } }
        public void Reset() { _have = false; }
    }

    /// <summary>
    /// Knows when the work really moved to another agent: after the user's decision was approved (acknowledged
    /// by the run, not merely pressed), the first status change of an agent other than the one that asked.
    /// Never claims a handoff it did not see. Pure.
    /// </summary>
    public sealed class HandoffTracker
    {
        string _requester;
        bool _armed;

        /// <summary>A decision was requested by this agent.</summary>
        public void DecisionRequested(string agentId) { _requester = agentId; _armed = false; }

        /// <summary>The decision was resolved. Only an approval arms the tracker.</summary>
        public void DecisionResolved(string state)
        {
            _armed = string.Equals(state, "approved", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>An agent's status changed. True once, when the handoff is observed.</summary>
        public bool AgentChanged(string agentId)
        {
            if (!_armed || string.IsNullOrEmpty(agentId)) return false;
            if (string.Equals(agentId, _requester, StringComparison.Ordinal)) return false;
            _armed = false;
            return true;
        }

        public bool Armed { get { return _armed; } }
        public void Reset() { _armed = false; _requester = null; }
    }

    /// <summary>Maps the release flags onto the coach's feature flags. Pure.</summary>
    public static class OnboardingFlagMap
    {
        public static OnboardingFlags Map(bool floor, bool ceiling, bool calmToggle, bool wall, bool manager, bool agentSelect, bool portal)
        {
            var f = OnboardingFlags.None;
            if (floor || ceiling) f |= OnboardingFlags.Spatial;
            if (calmToggle) f |= OnboardingFlags.CalmToggle;
            if (wall)
            {
                f |= OnboardingFlags.Wall;
                if (manager) f |= OnboardingFlags.Manager;
                if (agentSelect && portal) f |= OnboardingFlags.Portal;
            }
            return f;
        }
    }

    /// <summary>Unscaled-time clock for the coach. The presenter pushes the time in; no Unity types here.</summary>
    public sealed class ManualOnboardingClock : IOnboardingClock
    {
        public double Time;
        public double Now { get { return Time; } }
    }
}
