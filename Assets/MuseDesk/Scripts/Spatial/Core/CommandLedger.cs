using System;
using System.Collections.Generic;

namespace MuseDesk.Spatial
{
    /// <summary>
    /// Pure bookkeeping for "one consequential command in flight per target, and one stable idempotency
    /// key per logical action" (Polish S2). Used by the task board presenter (one lock per task card) and
    /// the session's decision responder (one lock per decision id). Lives in Core only so the EditMode
    /// tests can reach it; it has no engine or network references and the placement code never uses it.
    /// Main thread only.
    ///
    ///   lockId   what may only have one command in flight (a task id, a decision id).
    ///   actionId the logical action (task|assign|agent, decision|approve). The SAME actionId reuses the
    ///            SAME key until a definitive answer (accepted OR refused by the server) resolves it; a
    ///            transport failure or cancel is not an answer, so a retry reuses the key and the server
    ///            can deduplicate it. Only after a definitive answer does the next action mint a new key.
    /// </summary>
    public sealed class CommandLedger
    {
        readonly HashSet<string> _busy = new HashSet<string>();
        readonly Dictionary<string, string> _keys = new Dictionary<string, string>();

        public bool IsBusy(string lockId) { return lockId != null && _busy.Contains(lockId); }

        /// <summary>The stable key for a logical action: the one already minted, else a new one from
        /// <paramref name="mint"/>. Does not lock.</summary>
        public string KeyFor(string actionId, Func<string> mint)
        {
            if (actionId == null) return mint();
            string key;
            if (!_keys.TryGetValue(actionId, out key)) { key = mint(); _keys[actionId] = key; }
            return key;
        }

        /// <summary>Take the in-flight lock. False when a command for this target is already in flight
        /// (the caller ignores the second one).</summary>
        public bool TryLock(string lockId)
        {
            if (lockId == null) return true;
            return _busy.Add(lockId);
        }

        /// <summary>Give the lock back. <paramref name="definitive"/> true (the server answered, ok or
        /// refused) forgets the key so the next action gets a fresh one; false (transport error, cancel)
        /// keeps it for a retry.</summary>
        public void Unlock(string lockId, string key, bool definitive)
        {
            if (lockId != null) _busy.Remove(lockId);
            if (!definitive || key == null) return;
            List<string> drop = null;
            foreach (var kv in _keys)
                if (kv.Value == key) (drop ?? (drop = new List<string>())).Add(kv.Key);
            if (drop != null) for (int i = 0; i < drop.Count; i++) _keys.Remove(drop[i]);
        }

        public void Clear() { _busy.Clear(); _keys.Clear(); }
    }
}
