using System;
using System.Collections.Generic;

namespace MuseDesk.Motion
{
    // Movement ownership protocol (Sprint A1). One writer per transform, enforced by a lease.
    //
    // Why: docs/Polish/audit/A_MOTION_OWNERSHIP.md found several places where two components write the same
    // transform in the same frame (RefaceUser vs the mode transition, RubberBandReturn vs AgentMovement,
    // PushableProp vs the prop carrier). S3/S4 fixed those with ad-hoc guards. This registry makes the rule
    // explicit and testable, in the style of HandClaimRegistry: pure logic, no Unity objects, keyed by an int
    // (callers pass GameObject.GetInstanceID() or Transform.GetInstanceID()).
    //
    // Priority (lower number wins), documented preemption:
    //   1 UserHeld ... a hand holds it (grab, carry). Beats everything. Nothing else may take it while held.
    //   2 Carrier .... a carrier moves it as a passenger: the workspace prop carrier during a mode transition,
    //                  the habitat elevator carrying an agent. Beats navigation and ambient motion.
    //   3 Navigation . deliberate self-motion: AgentMovement walking, HabitatWalker.
    //   4 Ambient .... springs and polish: RubberBandReturn, RefaceUser, idle settle, PushableProp relax.
    // A STRICTLY higher priority preempts the current owner (which gets its callback exactly once). Equal or
    // lower priority is refused, so a later Navigation request never steals from an earlier Navigation owner:
    // it must wait for Release/Cancel. The same owner acquiring again is idempotent and returns its lease.
    //
    // Leases are versioned. Every grant takes a fresh version from one counter, so a lease that was released,
    // cancelled, preempted or recovered can never match again, even if the same owner re-acquires the same
    // target. A mover must call IsOwner (or OwnedMover.Decide) before EVERY write.
    //
    // Re-entrancy: the new state is installed BEFORE a preempted owner's callback runs, so a callback may call
    // TryAcquire/Release/Cancel on this or any other target. Callback exceptions are swallowed and counted in
    // CallbackErrors so one bad owner cannot leave the registry half-updated.

    /// <summary>Who may write a transform, by rank. Lower value wins.</summary>
    public enum MovePriority { None = 0, UserHeld = 1, Carrier = 2, Navigation = 3, Ambient = 4 }

    /// <summary>Why an owner lost a transform.</summary>
    public enum OwnershipLossReason { Preempted = 1, Cancelled = 2 }

    /// <summary>
    /// A grant to write one transform. A value (no allocation). Default is "no lease"; compare with
    /// TransformOwnership.IsOwner for validity, never by the fields alone.
    /// </summary>
    public readonly struct OwnershipLease : IEquatable<OwnershipLease>
    {
        public readonly int Target;
        public readonly int Owner;
        public readonly MovePriority Priority;
        public readonly int Version;

        public OwnershipLease(int target, int owner, MovePriority priority, int version)
        { Target = target; Owner = owner; Priority = priority; Version = version; }

        /// <summary>False for the default lease (a refused acquire). True does NOT mean still current.</summary>
        public bool IsGranted { get { return Version != 0; } }

        public bool Equals(OwnershipLease o) { return Target == o.Target && Owner == o.Owner && Version == o.Version; }
        public override bool Equals(object obj) { return obj is OwnershipLease o && Equals(o); }
        public override int GetHashCode() { return Version * 397 ^ Target; }
    }

    /// <summary>Callback for an owner that lost its lease. Called at most once per lease.</summary>
    public delegate void OwnershipLostHandler(OwnershipLease lost, OwnershipLossReason reason);

    public sealed class TransformOwnership
    {
        /// <summary>The one registry of the running app. Tests make their own.</summary>
        public static readonly TransformOwnership Shared = new TransformOwnership();

        /// <summary>Returned by CurrentOwner when nothing owns the target.</summary>
        public const int NoOwner = int.MinValue;

        sealed class Entry
        {
            public OwnershipLease lease;
            public OwnershipLostHandler onLost;
        }

        readonly Dictionary<int, Entry> _entries = new Dictionary<int, Entry>();
        readonly List<int> _scratch = new List<int>();
        int _nextVersion;

        /// <summary>True when the owner (an instance id) still exists and is enabled. Used by Recover.</summary>
        public Func<int, bool> IsOwnerAlive;

        /// <summary>Count of exceptions thrown by loss callbacks (swallowed). Tests and diagnostics read it.</summary>
        public int CallbackErrors { get; private set; }

        /// <summary>Number of targets that currently have an owner.</summary>
        public int Count { get { return _entries.Count; }  }

        /// <summary>
        /// Ask to write a target. Granted when free, when this owner already holds it (returns the existing
        /// lease, or a fresh one if the priority changed), or when priority is strictly higher than the
        /// current owner's (which is then preempted: callback once). Refused otherwise; lease is default.
        /// </summary>
        public bool TryAcquire(int target, int owner, MovePriority priority, out OwnershipLease lease,
                               OwnershipLostHandler onLost = null)
        {
            lease = default(OwnershipLease);
            if (priority == MovePriority.None) return false;

            Entry cur;
            if (_entries.TryGetValue(target, out cur))
            {
                if (cur.lease.Owner == owner)
                {
                    if (cur.lease.Priority == priority)
                    {
                        if (onLost != null) cur.onLost = onLost;
                        lease = cur.lease;
                        return true;
                    }
                    // Same owner changing rank: new version, old lease goes stale, no callback (it is the same owner).
                    cur.lease = new OwnershipLease(target, owner, priority, ++_nextVersion);
                    if (onLost != null) cur.onLost = onLost;
                    lease = cur.lease;
                    return true;
                }
                if ((int)priority >= (int)cur.lease.Priority) return false;

                // Preempt. Install the new owner first, then tell the old one (re-entrancy safe).
                OwnershipLease old = cur.lease;
                OwnershipLostHandler oldHandler = cur.onLost;
                cur.lease = new OwnershipLease(target, owner, priority, ++_nextVersion);
                cur.onLost = onLost;
                lease = cur.lease;
                Notify(oldHandler, old, OwnershipLossReason.Preempted);
                return true;
            }

            Entry e = new Entry();
            e.lease = new OwnershipLease(target, owner, priority, ++_nextVersion);
            e.onLost = onLost;
            _entries[target] = e;
            lease = e.lease;
            return true;
        }

        /// <summary>The owner is done. No callback (the owner chose to stop). False for a stale lease.</summary>
        public bool Release(OwnershipLease lease)
        {
            if (!IsOwner(lease)) return false;
            _entries.Remove(lease.Target);
            return true;
        }

        /// <summary>
        /// Take the target away from its owner (a transition starts, the user grabs, a scene unloads).
        /// The owner's callback fires exactly once. False when nobody owned it.
        /// </summary>
        public bool Cancel(int target, OwnershipLossReason reason = OwnershipLossReason.Cancelled)
        {
            Entry e;
            if (!_entries.TryGetValue(target, out e)) return false;
            _entries.Remove(target);
            Notify(e.onLost, e.lease, reason);
            return true;
        }

        /// <summary>
        /// Clear a stale lease whose owner is dead or disabled (IsOwnerAlive says false). No callback: the
        /// owner is gone. Does nothing when no predicate is set or the owner is alive. True if cleared.
        /// </summary>
        public bool Recover(int target)
        {
            Entry e;
            if (IsOwnerAlive == null || !_entries.TryGetValue(target, out e)) return false;
            if (IsOwnerAlive(e.lease.Owner)) return false;
            _entries.Remove(target);
            return true;
        }

        /// <summary>Recover every target. Returns how many leases were cleared. Reuses a scratch list.</summary>
        public int RecoverAll()
        {
            if (IsOwnerAlive == null || _entries.Count == 0) return 0;
            _scratch.Clear();
            foreach (KeyValuePair<int, Entry> kv in _entries)
                if (!IsOwnerAlive(kv.Value.lease.Owner)) _scratch.Add(kv.Key);
            for (int i = 0; i < _scratch.Count; i++) _entries.Remove(_scratch[i]);
            int n = _scratch.Count;
            _scratch.Clear();
            return n;
        }

        /// <summary>The owner id of a target, or NoOwner.</summary>
        public int CurrentOwner(int target)
        {
            Entry e;
            return _entries.TryGetValue(target, out e) ? e.lease.Owner : NoOwner;
        }

        /// <summary>The rank of a target's owner, or None.</summary>
        public MovePriority CurrentPriority(int target)
        {
            Entry e;
            return _entries.TryGetValue(target, out e) ? e.lease.Priority : MovePriority.None;
        }

        /// <summary>True when this exact lease is still the current grant. Call before every write.</summary>
        public bool IsOwner(OwnershipLease lease)
        {
            if (!lease.IsGranted) return false;
            Entry e;
            return _entries.TryGetValue(lease.Target, out e) && e.lease.Version == lease.Version;
        }

        /// <summary>Drop everything with no callbacks (scene reset, tests).</summary>
        public void Clear() { _entries.Clear(); }

        void Notify(OwnershipLostHandler handler, OwnershipLease lost, OwnershipLossReason reason)
        {
            if (handler == null) return;
            try { handler(lost, reason); }
            catch (Exception) { CallbackErrors++; }
        }
    }

    /// <summary>
    /// How a mover uses a lease. Pure and allocation-free; no MonoBehaviour needed. Pattern per tick:
    ///   switch (OwnedMover.Decide(reg, ref lease, target, me, MovePriority.Navigation)) {
    ///     case WriteDecision.Write:   transform.position = next; break;
    ///     case WriteDecision.Blocked: break;                       // someone better owns it: do not write
    ///     case WriteDecision.Lost:    StopOrRestart(); break;      // we had a lease and it was taken
    ///   }
    /// The mover keeps its lease in a field. Decide never writes; it only says whether writing is allowed.
    /// </summary>
    public enum WriteDecision { Blocked = 0, Write = 1, Lost = 2 }

    public static class OwnedMover
    {
        /// <summary>
        /// Validate or (re)acquire the lease for this tick. If the held lease is still current: Write.
        /// If it was held and is now stale: Lost, the lease is cleared once so the next tick may retry as a
        /// fresh request. If no lease is held: try to acquire; Write on success, Blocked on refusal.
        /// </summary>
        public static WriteDecision Decide(TransformOwnership reg, ref OwnershipLease lease, int target, int owner,
                                           MovePriority priority, OwnershipLostHandler onLost = null)
        {
            if (lease.IsGranted)
            {
                if (reg.IsOwner(lease)) return WriteDecision.Write;
                lease = default(OwnershipLease);
                return WriteDecision.Lost;
            }
            return reg.TryAcquire(target, owner, priority, out lease, onLost) ? WriteDecision.Write : WriteDecision.Blocked;
        }

        /// <summary>Drop a held lease (the move finished or the component is disabled). Safe on a stale or default lease.</summary>
        public static void Finish(TransformOwnership reg, ref OwnershipLease lease)
        {
            if (lease.IsGranted) reg.Release(lease);
            lease = default(OwnershipLease);
        }
    }
}
