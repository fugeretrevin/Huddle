using System;

namespace MuseDesk.Spatial.Wall
{
    /// <summary>
    /// Press detection for a held approval stamp on the Manager plan paper. Pure: the caller measures the distance from the
    /// stamp's ink head to the paper's stamp spot and says whether the stamp is held and whether a press would count now
    /// (the plan awaits the user, the paper is open). One gate per stamp.
    ///
    /// Same feel as the desk stamp: contact within a radius, held for a short dwell (a brush-past does not fire), then a
    /// cooldown. It only decides that a deliberate press happened; what the press does (the commit or the reject) goes through
    /// the Manager's own guarded methods, and the APPROVED / REJECTED mark is drawn by the paper only after the backend
    /// confirmed.
    /// </summary>
    public sealed class PlanStampGate
    {
        public float contactRadius = 0.06f;
        public float dwellSeconds = 0.12f;
        public float cooldownSeconds = 1.0f;

        float _dwell, _cooldownLeft;

        /// <summary>True on the one tick a press fires.</summary>
        public bool Tick(float dt, bool held, bool armed, float distance)
        {
            if (dt < 0f) dt = 0f;
            if (_cooldownLeft > 0f) _cooldownLeft -= dt;
            if (!held || !armed || _cooldownLeft > 0f || distance > contactRadius) { _dwell = 0f; return false; }
            _dwell += dt;
            if (_dwell < dwellSeconds) return false;
            _dwell = 0f;
            _cooldownLeft = cooldownSeconds;
            return true;
        }

        public void Reset() { _dwell = 0f; _cooldownLeft = 0f; }
    }

    /// <summary>
    /// Confirmed completion nods that could not play yet because the agent was away from its desk (on a pick-up trip, in the
    /// lift). Instead of dropping them as stale after a couple of seconds, one nod per agent waits here until the agent is home
    /// and idle, for at most <see cref="MaxWaitSeconds"/>. Only a confirmed task.completed is ever enqueued by the caller.
    /// Pure, no allocation after construction.
    /// </summary>
    public sealed class DeferredNods
    {
        public const float MaxWaitSeconds = 20f;
        readonly bool[] _pending;
        readonly float[] _since;

        public int Deferred { get; private set; }
        public int Released { get; private set; }
        public int Expired { get; private set; }

        public DeferredNods(int agents) { _pending = new bool[Math.Max(1, agents)]; _since = new float[_pending.Length]; }

        public bool IsPending(int agent) { return agent >= 0 && agent < _pending.Length && _pending[agent]; }

        /// <summary>Wait for the agent to be home. A second completion while one waits adds nothing (one nod is enough).</summary>
        public bool Enqueue(int agent, float now)
        {
            if (agent < 0 || agent >= _pending.Length || _pending[agent]) return false;
            _pending[agent] = true; _since[agent] = now; Deferred++;
            return true;
        }

        /// <summary>True once, when the agent has waited and is now home and free; false while it should keep waiting. A nod past
        /// the wait limit is dropped (counted in <see cref="Expired"/>).</summary>
        public bool TryRelease(int agent, float now, bool homeAndFree)
        {
            if (!IsPending(agent)) return false;
            if (now - _since[agent] > MaxWaitSeconds) { _pending[agent] = false; Expired++; return false; }
            if (!homeAndFree) return false;
            _pending[agent] = false; Released++;
            return true;
        }

        /// <summary>Forget a waiting nod without playing it (Focus came on).</summary>
        public void Drop(int agent) { if (IsPending(agent)) { _pending[agent] = false; Expired++; } }

        public void Clear() { for (int i = 0; i < _pending.Length; i++) _pending[i] = false; }
    }
}
