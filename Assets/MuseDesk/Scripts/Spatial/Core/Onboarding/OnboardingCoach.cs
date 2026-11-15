using System;
using UnityEngine;

namespace MuseDesk.Spatial.Onboarding
{
    public interface IOnboardingClock { double Now { get; } }

    /// <summary>Persisted progress. Done hints never come back. Implementations must be cheap to call.</summary>
    public interface IOnboardingStore
    {
        bool IsDone(HintId id);
        void MarkDone(HintId id);
        int GetShownCount(HintId id);
        void SetShownCount(HintId id, int count);
        bool IsSkipped();
        void SetSkipped(bool skipped);
        void Clear();
    }

    public enum HintEndReason { Completed, Dismissed, TimedOut, Suppressed, Preempted }

    /// <summary>Snapshot of the user's situation. Pushed by the integration layer, never polled from inside the coach.</summary>
    public struct OnboardingContext
    {
        public bool FocusActive;      // Focus mode suppresses every hint.
        public bool HandsBusy;        // the hands hold or manipulate something other than the hint target
        public bool ReducedMotion;    // display layer should fade instead of animate
        public OnboardingFlags Flags;
    }

    /// <summary>In-memory store for tests and as a fallback when PlayerPrefs is unavailable.</summary>
    public sealed class MemoryOnboardingStore : IOnboardingStore
    {
        readonly bool[] _done = new bool[64];
        readonly int[] _shown = new int[64];
        bool _skipped;
        public bool IsDone(HintId id) { return id >= 0 && _done[(int)id]; }
        public void MarkDone(HintId id) { if (id >= 0) _done[(int)id] = true; }
        public int GetShownCount(HintId id) { return id >= 0 ? _shown[(int)id] : 0; }
        public void SetShownCount(HintId id, int count) { if (id >= 0) _shown[(int)id] = count; }
        public bool IsSkipped() { return _skipped; }
        public void SetSkipped(bool skipped) { _skipped = skipped; }
        public void Clear() { Array.Clear(_done, 0, _done.Length); Array.Clear(_shown, 0, _shown.Length); _skipped = false; }
    }

    /// <summary>PlayerPrefs-backed store (per device). Not applied anywhere yet; see A15_ONBOARDING.md.</summary>
    public sealed class PlayerPrefsOnboardingStore : IOnboardingStore
    {
        const string Prefix = "MuseDesk.Onboarding.";
        public bool IsDone(HintId id) { return PlayerPrefs.GetInt(Prefix + "done." + (int)id, 0) == 1; }
        public void MarkDone(HintId id) { PlayerPrefs.SetInt(Prefix + "done." + (int)id, 1); PlayerPrefs.Save(); }
        public int GetShownCount(HintId id) { return PlayerPrefs.GetInt(Prefix + "shown." + (int)id, 0); }
        public void SetShownCount(HintId id, int count) { PlayerPrefs.SetInt(Prefix + "shown." + (int)id, count); }
        public bool IsSkipped() { return PlayerPrefs.GetInt(Prefix + "skipped", 0) == 1; }
        public void SetSkipped(bool skipped) { PlayerPrefs.SetInt(Prefix + "skipped", skipped ? 1 : 0); PlayerPrefs.Save(); }
        public void Clear()
        {
            for (int i = 0; i < HintCatalog.Count; i++)
            {
                int id = (int)HintCatalog.At(i).Id;
                PlayerPrefs.DeleteKey(Prefix + "done." + id);
                PlayerPrefs.DeleteKey(Prefix + "shown." + id);
            }
            PlayerPrefs.DeleteKey(Prefix + "skipped");
            PlayerPrefs.Save();
        }
    }

    /// <summary>
    /// First-use coach. Pure logic: feed it real observed events with Observe, push context with SetContext,
    /// call Tick regularly, and show whatever Current says. No per-frame allocation.
    /// At most one hint is visible. A hint appears only after its trigger event, ends when the user does the
    /// action (Completes), is dismissed, or times out. Done hints are persisted and never return.
    /// </summary>
    public sealed class OnboardingCoach
    {
        public const float Cooldown = 3f;          // gap after an onboarding hint ends
        public const float RecoveryCooldown = 0.5f;

        readonly IOnboardingClock _clock;
        readonly IOnboardingStore _store;
        readonly int _n;
        readonly bool[] _pending;
        readonly double[] _triggeredAt;
        readonly bool[] _seen = new bool[Enum.GetValues(typeof(OnboardingEvent)).Length];
        OnboardingContext _ctx;
        int _current = -1;
        double _shownAt;
        double _lastEndedAt = -1e9;

        public event Action<HintDef> HintShown;
        public event Action<HintDef, HintEndReason> HintEnded;

        public OnboardingCoach(IOnboardingClock clock, IOnboardingStore store)
        {
            if (clock == null) throw new ArgumentNullException("clock");
            if (store == null) throw new ArgumentNullException("store");
            _clock = clock; _store = store;
            _n = HintCatalog.Count;
            _pending = new bool[_n];
            _triggeredAt = new double[_n];
        }

        public HintDef Current { get { return _current < 0 ? null : HintCatalog.At(_current); } }
        public bool IsShowing { get { return _current >= 0; } }
        /// <summary>False when Reduced Motion is on: the display layer should fade instead of animate.</summary>
        public bool Animate { get { return !_ctx.ReducedMotion; } }
        public bool IsPending(HintId id) { int i = HintCatalog.IndexOf(id); return i >= 0 && _pending[i]; }

        public void SetContext(OnboardingContext ctx) { _ctx = ctx; }

        /// <summary>Report a real event. Completes are handled before triggers so one event can do both.</summary>
        public void Observe(OnboardingEvent e)
        {
            if (_store.IsSkipped()) return;
            _seen[(int)e] = true;

            for (int i = 0; i < _n; i++)
            {
                var d = HintCatalog.At(i);
                if (!d.CompletesOn(e)) continue;
                if (i == _current) { End(HintEndReason.Completed); continue; }
                if (_pending[i])
                {
                    _pending[i] = false;
                    if (!d.Recovery) _store.MarkDone(d.Id);
                }
            }

            for (int i = 0; i < _n; i++)
            {
                var d = HintCatalog.At(i);
                if (d.Trigger != e) continue;
                if (i == _current || _pending[i]) continue;
                if (d.Recovery)
                {
                    if (_store.GetShownCount(d.Id) >= d.MaxShows) continue;
                }
                else
                {
                    if (_store.IsDone(d.Id)) continue;
                    // The user already did this action earlier in the session: never nag.
                    if (AlreadyDone(d)) { _store.MarkDone(d.Id); continue; }
                }
                _pending[i] = true;
                _triggeredAt[i] = _clock.Now;
            }
        }

        bool AlreadyDone(HintDef d)
        {
            for (int k = 0; k < d.Completes.Length; k++) if (_seen[(int)d.Completes[k]]) return true;
            return false;
        }

        public void Tick()
        {
            double now = _clock.Now;
            if (_store.IsSkipped()) { if (_current >= 0) End(HintEndReason.Dismissed); return; }

            if (_current >= 0)
            {
                var cur = HintCatalog.At(_current);
                if (_ctx.FocusActive || (_ctx.HandsBusy && !cur.ShowWhileHolding)) { End(HintEndReason.Suppressed); return; }
                if (!cur.Recovery && RecoveryReady(now) >= 0) { End(HintEndReason.Preempted); return; }
                if (now - _shownAt >= cur.ShowSeconds) End(HintEndReason.TimedOut);
                return;
            }

            if (_ctx.FocusActive) return;
            int pick = RecoveryReady(now);
            if (pick < 0) pick = OnboardingReady(now);
            if (pick >= 0) Show(pick, now);
        }

        bool Eligible(int i, double now)
        {
            if (!_pending[i]) return false;
            var d = HintCatalog.At(i);
            if ((d.Requires & _ctx.Flags) != d.Requires) return false;
            if (now - _triggeredAt[i] < d.DelaySeconds) return false;
            if (_ctx.HandsBusy && !d.ShowWhileHolding) return false;
            if (d.After != HintId.None && !_store.IsDone(d.After)) return false;
            return true;
        }

        int RecoveryReady(double now)
        {
            if (now - _lastEndedAt < RecoveryCooldown) return -1;
            for (int i = 0; i < _n; i++)
                if (HintCatalog.At(i).Recovery && Eligible(i, now)) return i;
            return -1;
        }

        int OnboardingReady(double now)
        {
            if (now - _lastEndedAt < Cooldown) return -1;
            for (int i = 0; i < _n; i++)
                if (!HintCatalog.At(i).Recovery && Eligible(i, now)) return i;
            return -1;
        }

        void Show(int i, double now)
        {
            var d = HintCatalog.At(i);
            _current = i;
            _shownAt = now;
            _pending[i] = false;
            _store.SetShownCount(d.Id, _store.GetShownCount(d.Id) + 1);
            if (HintShown != null) HintShown(d);
        }

        void End(HintEndReason reason)
        {
            if (_current < 0) return;
            int i = _current;
            var d = HintCatalog.At(i);
            _current = -1;
            _lastEndedAt = _clock.Now;
            switch (reason)
            {
                case HintEndReason.Completed:
                    if (!d.Recovery) _store.MarkDone(d.Id);
                    break;
                case HintEndReason.Dismissed:
                    if (!d.Recovery) _store.MarkDone(d.Id);
                    break;
                case HintEndReason.TimedOut:
                    if (!d.Recovery)
                    {
                        if (_store.GetShownCount(d.Id) >= d.MaxShows) _store.MarkDone(d.Id);
                        else { _pending[i] = true; _triggeredAt[i] = _clock.Now; }
                    }
                    break;
                case HintEndReason.Suppressed:
                case HintEndReason.Preempted:
                    // Not the user's fault: give the show back and try again later.
                    _store.SetShownCount(d.Id, Math.Max(0, _store.GetShownCount(d.Id) - 1));
                    _pending[i] = true;
                    break;
            }
            if (HintEnded != null) HintEnded(d, reason);
        }

        /// <summary>User dismissed the visible hint (a pinch on its close dot, for example).</summary>
        public void Dismiss() { End(HintEndReason.Dismissed); }

        /// <summary>User turned hints off for good.</summary>
        public void SkipAll()
        {
            _store.SetSkipped(true);
            End(HintEndReason.Dismissed);
            Array.Clear(_pending, 0, _n);
        }

        /// <summary>Replay the tour (a settings action).</summary>
        public void ResetProgress()
        {
            End(HintEndReason.Dismissed);
            _store.Clear();
            Array.Clear(_pending, 0, _n);
            Array.Clear(_seen, 0, _seen.Length);
            _lastEndedAt = -1e9;
        }
    }
}
