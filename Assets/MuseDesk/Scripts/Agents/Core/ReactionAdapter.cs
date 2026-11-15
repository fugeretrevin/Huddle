namespace MuseDesk.Agents
{
    // Sprint A3 adapter logic, pure (no engine types) so EditMode tests reach it. AgentAttention feeds real
    // scene state in; everything here only maps state to ReactionArbiter calls / idle choices. No allocation
    // after construction.

    public enum IdleAction { None = 0, LookAtWorker = 1, Stretch = 2, LookAtColleague = 3 }

    public static class ReactionAdapter
    {
        /// <summary>Which Critical kind the agent's confirmed state maps to, highest first:
        /// completion paper transfer, office move (elevator boarding), handoff, walking. False = none.</summary>
        public static bool CriticalKindFor(bool paperTransfer, bool boarding, bool handoff, bool walking, out ReactionKind kind)
        {
            if (paperTransfer) { kind = ReactionKind.PaperTransfer; return true; }
            if (boarding) { kind = ReactionKind.ElevatorBoarding; return true; }
            if (handoff) { kind = ReactionKind.Handoff; return true; }
            if (walking) { kind = ReactionKind.Walking; return true; }
            kind = ReactionKind.Walking;
            return false;
        }

        /// <summary>Map an idle variant (0..2, from AmbientVariation) to what the agent does. Only gestures that can
        /// be honest for the current situation; Focus never reaches here for ambient (callers skip it) but the
        /// stretch is also withheld under Focus as before.</summary>
        public static IdleAction ChooseIdleAction(int variant, bool hasWorkingColleague, bool hasAnyColleague, bool focus)
        {
            switch (variant)
            {
                case 0:
                    if (hasWorkingColleague) return IdleAction.LookAtWorker;
                    return hasAnyColleague ? IdleAction.LookAtColleague : IdleAction.None;
                case 1:
                    if (!focus) return IdleAction.Stretch;
                    return hasAnyColleague ? IdleAction.LookAtColleague : IdleAction.None;
                default:
                    if (hasAnyColleague) return IdleAction.LookAtColleague;
                    return focus ? IdleAction.None : IdleAction.Stretch;
            }
        }

        /// <summary>Request a reaction; true only when it plays now. Queued counts as false (the queue's Poll plays it).</summary>
        public static bool Allow(ReactionArbiter arb, float now, int agent, ReactionKind kind, bool statusBacked)
        {
            if (arb == null) return true;
            return arb.Request(now, new ReactionRequest(agent, kind, now, statusBacked)) == ReactionVerdict.Play;
        }

        /// <summary>The body refused after the arbiter said yes: free the slot again.</summary>
        public static void Release(ReactionArbiter arb, int agent, ReactionKind kind)
        {
            if (arb != null) arb.End(agent, kind);
        }

        /// <summary>Per-agent seed for AmbientVariation: stable, distinct per agent index and salt.</summary>
        public static int SeedFor(int agentIndex, int salt) { return 7919 * (agentIndex + 1) + 104729 * salt + 13; }
    }

    /// <summary>Keeps the arbiter's Critical slot per agent in step with confirmed state (read-only: it never moves anything).</summary>
    public sealed class CriticalSync
    {
        readonly bool[] _has;
        readonly ReactionKind[] _kind;

        public CriticalSync(int agentCount)
        {
            int n = agentCount < 1 ? 1 : agentCount;
            _has = new bool[n];
            _kind = new ReactionKind[n];
        }

        /// <summary>Returns the Critical kind now held for the agent, or false if none.</summary>
        public bool Sync(ReactionArbiter arb, int agent, float now, bool paperTransfer, bool boarding, bool handoff, bool walking)
        {
            if (arb == null || agent < 0 || agent >= _has.Length) return false;
            ReactionKind want;
            bool wanted = ReactionAdapter.CriticalKindFor(paperTransfer, boarding, handoff, walking, out want);
            if (_has[agent] && (!wanted || _kind[agent] != want))
            {
                arb.End(agent, _kind[agent]);
                _has[agent] = false;
            }
            if (!wanted) return false;
            // Re-assert when new, or when the arbiter lost the slot (60 s safety expiry).
            if (!_has[agent] || !arb.IsBusy(agent, now))
            {
                if (arb.Request(now, new ReactionRequest(agent, want, now, true)) == ReactionVerdict.Play)
                {
                    _has[agent] = true;
                    _kind[agent] = want;
                }
            }
            return _has[agent];
        }

        public bool Holds(int agent) { return agent >= 0 && agent < _has.Length && _has[agent]; }
    }
}
