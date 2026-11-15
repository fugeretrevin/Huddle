using System;

namespace MuseDesk.Spatial
{
    /// <summary>
    /// Pure bookkeeping for a two-state backend toggle (focus on/off) sent with stable idempotency keys
    /// (Sprint A4b). Built on <see cref="CommandLedger"/>: one request in flight, one key per intended
    /// value, a retry of the same value reuses the key until the server answers, and the newest wish wins
    /// (a late answer for an older value never decides the final state; the pump just sends the newest
    /// wish next). No engine or network references. Main thread only.
    /// </summary>
    public sealed class LatestWinsToggle
    {
        const string Lock = "toggle";
        readonly CommandLedger _ledger = new CommandLedger();
        bool _hasDesired, _desired;
        bool _hasAcked, _acked;
        bool _inFlight;

        public bool InFlight { get { return _inFlight; } }
        public bool HasDesired { get { return _hasDesired; } }
        public bool Desired { get { return _desired; } }

        /// <summary>The user wants this value now. Overwrites any older, unsent wish.</summary>
        public void Want(bool value) { _hasDesired = true; _desired = value; }

        /// <summary>The server told us its state (a backend echo). Never clobbers a wish that is being
        /// sent; the pump compares again when the in-flight request completes.</summary>
        public void Observe(bool serverValue)
        {
            _hasAcked = true; _acked = serverValue;
            if (!_inFlight) { _hasDesired = true; _desired = serverValue; }
        }

        /// <summary>Take the next request to send, or false when one is already in flight or the server
        /// already has the wanted value. The key is stable per value until a definitive answer.</summary>
        public bool TryBegin(Func<bool, string> mint, out bool value, out string key)
        {
            value = _desired; key = null;
            if (!_hasDesired || _inFlight) return false;
            if (_hasAcked && _acked == _desired) return false;
            if (!_ledger.TryLock(Lock)) return false;
            _inFlight = true;
            bool v = _desired;
            key = _ledger.KeyFor("toggle|" + (v ? "on" : "off"), () => mint(v));
            return true;
        }

        /// <summary>The request finished. <paramref name="answered"/> true: the server answered, so the key
        /// is spent and the value is acknowledged. False (transport error, cancel): the key is kept so a
        /// retry of the same value deduplicates, and nothing is acknowledged. Returns true when a newer
        /// wish is waiting and the pump should send again.</summary>
        public bool Complete(bool sentValue, string key, bool answered)
        {
            _inFlight = false;
            _ledger.Unlock(Lock, key, answered);
            if (answered) { _hasAcked = true; _acked = sentValue; }
            return answered && _hasDesired && _desired != _acked;
        }

        public void Reset()
        {
            _ledger.Clear();
            _hasDesired = _hasAcked = _inFlight = false;
        }
    }
}
