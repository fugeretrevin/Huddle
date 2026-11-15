namespace MuseDesk.Agents
{
    /// <summary>
    /// Pure scheduling for "the completion celebration waits for the handoffs the event stream reported".
    /// A handoff that was reported (e.g. Developer -> Tester on artifact.created) may be queued behind another
    /// one that is still walking. At 1x the delivery arrives while it is still queued; it is shown first, within
    /// a bound, and only what is still unfinished after the bound is retired. Presentation only.
    /// </summary>
    public static class HandoffWaitPolicy
    {
        /// <summary>True while the completion sequence should keep waiting for the handoff chain.</summary>
        public static bool KeepWaiting(bool exchangePending, float waited, float maxSeconds)
            => exchangePending && waited < maxSeconds;

        /// <summary>True when the wait ran out with handoffs still pending: they are retired, never shown late.</summary>
        public static bool ShouldAbandon(bool supersedes, bool exchangePending, float waited, float maxSeconds)
            => supersedes && exchangePending && waited >= maxSeconds;
    }
}
