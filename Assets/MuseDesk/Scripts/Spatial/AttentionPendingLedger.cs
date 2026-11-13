using System;
using System.Collections.Generic;

namespace MuseDesk.Spatial
{
    /// <summary>
    /// Keeps "Needs you" honest. <see cref="AttentionModel"/> holds a decision as pending until it
    /// is told that exact id was resolved, or until a reset. If the session's own record of that
    /// decision stops being "requested" by any other route (a resolution whose event carried a
    /// different or empty id, a decision dropped by a reload the model was not told about, a
    /// replay), the model would keep asking for the user with nothing to decide.
    ///
    /// This ledger remembers the ids the model was given and, when asked, drops from the model
    /// every one of them that the session no longer reports as waiting. It never adds a decision
    /// and never answers one: it only withdraws a cue whose cause is gone.
    /// Plain class, no Unity types, so it is tested without a scene.
    /// </summary>
    public sealed class AttentionPendingLedger
    {
        readonly List<string> _ids = new List<string>(2);

        /// <summary>Ids the model is believed to hold as pending.</summary>
        public int Count => _ids.Count;

        public void Add(string decisionId)
        {
            if (string.IsNullOrEmpty(decisionId) || _ids.Contains(decisionId)) return;
            _ids.Add(decisionId);
        }

        public void Remove(string decisionId)
        {
            if (!string.IsNullOrEmpty(decisionId)) _ids.Remove(decisionId);
        }

        public void Clear() { _ids.Clear(); }

        /// <summary>
        /// Withdraw from <paramref name="model"/> every remembered decision for which
        /// <paramref name="stillPending"/> is false. Returns how many were withdrawn.
        /// </summary>
        public int Reconcile(AttentionModel model, Func<string, bool> stillPending)
        {
            if (model == null || stillPending == null) return 0;
            int dropped = 0;
            for (int i = _ids.Count - 1; i >= 0; i--)
            {
                string id = _ids[i];
                if (stillPending(id)) continue;
                _ids.RemoveAt(i);
                model.DecisionResolved(id);
                dropped++;
            }
            return dropped;
        }
    }
}
