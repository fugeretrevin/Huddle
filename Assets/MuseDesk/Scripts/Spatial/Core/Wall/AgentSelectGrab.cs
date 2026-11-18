using System;
using System.Collections.Generic;
using UnityEngine;

namespace MuseDesk.Spatial.Wall
{
    // Select Agents "force grab" (AgentSelectGrabV1), pure logic. No scene access, no allocation per tick, so the
    // EditMode tests reach it. The runtime (WallHabitatController.Interaction) feeds it hand rays and positions.
    //
    //   AgentRayTargeting ... which habitat agent a hand ray points at: a physical radius OR a small angular cone,
    //                         whichever is larger, so a 17 cm agent 2 m away is a target a real hand ray can hold.
    //   AgentTargetLock ..... per-hand hover lock with a short grace (the ray dips when the fingers close: the
    //                         target the pinch BEGAN on is the one it takes, not wherever the ray is 150 ms later).
    //   VisitDropRule ....... where a released projection goes: the perch near the user, or back to the wall.
    //   SelectBlockWords .... what the user is told when a pinch on an agent was refused (never silent).
    //   EnableLedger ........ mute things and give back EXACTLY what was muted (the hidden office's interactables).

    [Serializable]
    public sealed class AgentSelectGrabSettings
    {
        [Tooltip("Smallest angular half-size of an agent target (deg). Hand rays jitter by 1-2 deg and dip 2-4 deg as the pinch closes.")]
        public float minTargetDegrees = 4.5f;
        [Tooltip("A locked target survives this long after the ray leaves it (s).")]
        public float lockGraceSeconds = 0.35f;
        [Tooltip("Ignore anything closer than this along the ray (m).")]
        public float minRayDistance = 0.2f;

        [Header("Carry (rc22: keep hold)")]
        [Tooltip("While carrying, the pinch must read open this long on a tracked hand to let go (s). The arbiter's own is 0.12 s; " +
                 "hand tracking drops a held pinch for 0.05-0.15 s now and then.")]
        public float carryReleaseSeconds = 0.20f;
        [Tooltip("While carrying, a lost hand is held (frozen) this long before the carry is cancelled (s). The arbiter's own is 0.2 s.")]
        public float carryLostSeconds = 1.0f;
        [Tooltip("The carried projection sits this far along the hand ray (m).")]
        public float carryDistance = 0.14f;
        [Tooltip("The carried projection follows the hand with this critically damped lag (s).")]
        public float carryFollowSeconds = 0.07f;
        [Tooltip("The ray direction used for the carry point is smoothed with this time constant (s): angular jitter of the pointer pose.")]
        public float carryDirSmoothSeconds = 0.08f;
    }

    [Serializable]
    public sealed class VisitDropSettings
    {
        [Tooltip("Released this close to the perch: it lands on the perch (m). The old rule was the 0.13 m pad radius.")]
        public float perchSnapRadius = 0.30f;
        [Tooltip("Released this close to the user's eye (and nearer the user than its wall home): it lands on the perch (m). " +
                 "The carry point is ~0.14 m past the hand, so an arm stretched toward the wall puts it ~0.7 m out (a put-back) " +
                 "and a hand pulled in to the chest ~0.3-0.45 m (a landing). The perch itself is ~0.43 m from the eye.")]
        public float nearUserRadius = 0.55f;
    }

    public enum VisitDrop { Home = 0, Perch = 1 }

    public static class AgentRayTargeting
    {
        /// <summary>
        /// The eligible target the ray points at, or -1. Accepted when the target centre is within
        /// max(physicalRadius, t * tan(minDegrees)) of the ray at distance t >= minT; among several, the one with the
        /// smallest normalised miss wins (ties: the nearer).
        /// </summary>
        public static int Pick(Vector3 origin, Vector3 dir, IList<Vector3> centers, IList<bool> eligible, float physicalRadius, float minDegrees, float minT)
        {
            if (centers == null) return -1;
            float m = dir.magnitude;
            if (m < 1e-6f) return -1;
            dir /= m;
            float tan = Mathf.Tan(Mathf.Clamp(minDegrees, 0f, 30f) * Mathf.Deg2Rad);
            int pick = -1;
            float best = float.MaxValue, bestT = float.MaxValue;
            for (int i = 0; i < centers.Count; i++)
            {
                if (eligible != null && (i >= eligible.Count || !eligible[i])) continue;
                Vector3 oc = centers[i] - origin;
                float t = Vector3.Dot(oc, dir);
                if (t < minT) continue;
                float accept = Mathf.Max(physicalRadius, t * tan);
                if (accept <= 1e-6f) continue;
                float miss = (oc - dir * t).magnitude / accept;
                if (miss > 1f) continue;
                if (miss < best - 1e-4f || (Mathf.Abs(miss - best) <= 1e-4f && t < bestT)) { best = miss; bestT = t; pick = i; }
            }
            return pick;
        }

        /// <summary>Does the ray pass within radius of a point at distance >= minT?</summary>
        public static bool Hits(Vector3 origin, Vector3 dir, Vector3 point, float radius, float minT)
        {
            float m = dir.magnitude;
            if (m < 1e-6f) return false;
            dir /= m;
            Vector3 oc = point - origin;
            float t = Vector3.Dot(oc, dir);
            return t >= minT && (oc - dir * t).sqrMagnitude <= radius * radius;
        }
    }

    /// <summary>Per-hand hover lock. Feed it the raw pick every tick; read <see cref="Locked"/> for the highlight and
    /// <see cref="PinchTarget"/> for the grab.</summary>
    public sealed class AgentTargetLock
    {
        readonly int[] _locked = { -1, -1 };
        readonly float[] _lastSeen = new float[2];
        readonly int[] _atBegin = { -1, -1 };
        public float graceSeconds = 0.35f;

        public int Locked(int hand) { return _locked[hand & 1]; }

        /// <summary>One tick for one hand. <paramref name="rawPick"/> = -1 when the ray is on nothing (or untracked).</summary>
        public void Update(int hand, int rawPick, float now)
        {
            int h = hand & 1;
            if (rawPick >= 0) { _locked[h] = rawPick; _lastSeen[h] = now; return; }
            if (_locked[h] >= 0 && now - _lastSeen[h] > graceSeconds) _locked[h] = -1;
        }

        /// <summary>A fresh pinch closed on this tick: remember what it began on.</summary>
        public void NoteBegan(int hand) { _atBegin[hand & 1] = _locked[hand & 1]; }

        /// <summary>The target to grab when the pinch becomes Ready: what it began on, else what is locked now.</summary>
        public int PinchTarget(int hand)
        {
            int h = hand & 1;
            return _atBegin[h] >= 0 ? _atBegin[h] : _locked[h];
        }

        /// <summary>The pinch ended (taken, spent or opened).</summary>
        public void ClearPinch(int hand) { _atBegin[hand & 1] = -1; }

        /// <summary>A target became ineligible (it left home): drop it everywhere.</summary>
        public void Forget(int target)
        {
            for (int h = 0; h < 2; h++)
            {
                if (_locked[h] == target) _locked[h] = -1;
                if (_atBegin[h] == target) _atBegin[h] = -1;
            }
        }

        public void Reset() { _locked[0] = _locked[1] = _atBegin[0] = _atBegin[1] = -1; }
    }

    public static class VisitDropRule
    {
        /// <summary>
        /// Where a released projection goes. Perch when it is within perchSnapRadius of the perch, or brought within
        /// nearUserRadius of the eye AND nearer the eye than its wall home (pulled toward the user). Anything else,
        /// including a release back toward the wall, sends it home. Fail-closed: no settings = the old pad rule.
        /// </summary>
        public static VisitDrop Decide(Vector3 proxy, Vector3 perch, Vector3 eye, Vector3 home, VisitDropSettings s)
        {
            float snap = s != null ? s.perchSnapRadius : 0.13f;
            if ((proxy - perch).sqrMagnitude <= snap * snap) return VisitDrop.Perch;
            if (s == null) return VisitDrop.Home;
            float toEye = (proxy - eye).magnitude;
            if (toEye <= s.nearUserRadius && toEye < (proxy - home).magnitude) return VisitDrop.Perch;
            return VisitDrop.Home;
        }
    }

    public static class SelectBlockWords
    {
        /// <summary>Why a pinch aimed at an agent did not take it. Null when there is nothing to say.</summary>
        public static string For(HandBlock b)
        {
            switch (b)
            {
                case HandBlock.Holding: return "You're holding something: put it down, then pinch the agent";
                case HandBlock.CloseInteraction: return "Your hand was over something grabbable: point at the agent and pinch again";
                case HandBlock.Ui: return "Your hand was over a key: point at the agent and pinch again";
                case HandBlock.NotFresh: return "Open your hand, then pinch the agent";
                case HandBlock.TrackingLost: return "Hand lost: pinch the agent again";
                case HandBlock.Preempted: return "Another gesture has that hand: pinch again";
                default: return null;
            }
        }
    }

    /// <summary>
    /// Switch things off and later back on, touching ONLY what this ledger switched off (something that was already
    /// off stays off; something that is busy is skipped). The accessors keep it engine-free for the tests.
    /// </summary>
    public sealed class EnableLedger<T> where T : class
    {
        readonly List<T> _muted = new List<T>();
        readonly HashSet<T> _set = new HashSet<T>();
        public int Count => _muted.Count;
        public bool Contains(T item) { return item != null && _set.Contains(item); }

        /// <summary>Mute one item when it is on and not busy. True when this call muted it.</summary>
        public bool Mute(T item, Func<T, bool> isOn, Func<T, bool> isBusy, Action<T, bool> setOn)
        {
            if (item == null) return false;
            if (_set.Contains(item))
            {
                // Ours, but something switched it back on (a re-register toggle): off again, still recorded once.
                if (isOn(item) && (isBusy == null || !isBusy(item))) setOn(item, false);
                return false;
            }
            if (!isOn(item) || (isBusy != null && isBusy(item))) return false;
            setOn(item, false);
            _muted.Add(item);
            _set.Add(item);
            return true;
        }

        /// <summary>Give back everything muted (in reverse order). Null/destroyed items are skipped by the accessor.</summary>
        public int RestoreAll(Action<T, bool> setOn)
        {
            int n = 0;
            for (int i = _muted.Count - 1; i >= 0; i--) { T t = _muted[i]; if (t != null) { setOn(t, true); n++; } }
            _muted.Clear();
            _set.Clear();
            return n;
        }

        /// <summary>Give back the items that match (e.g. the distance grabbables once the carry ends).</summary>
        public int Restore(Func<T, bool> which, Action<T, bool> setOn)
        {
            int n = 0;
            for (int i = _muted.Count - 1; i >= 0; i--)
            {
                T t = _muted[i];
                if (t != null && !which(t)) continue;
                if (t != null) { setOn(t, true); n++; }
                _set.Remove(t);
                _muted.RemoveAt(i);
            }
            return n;
        }
    }
}
