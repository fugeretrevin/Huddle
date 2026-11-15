namespace MuseDesk.Agents
{
    // Sprint A3: pure reaction arbitration for the desk characters. No engine types, no static state, no
    // allocation after construction. Time is always injected (seconds), randomness is a seeded xorshift, so a
    // given call sequence always yields the same verdicts. Rule: no animation lies - callers only submit
    // reactions for confirmed events or real user actions; the arbiter only decides whether they may play NOW.

    /// <summary>Priority class. Higher never yields to lower; a lower class never interrupts a higher one.</summary>
    public enum ReactionClass { Ambient = 0, Social = 1, Task = 2, Critical = 3 }

    public enum ReactionKind
    {
        // Critical
        PaperTransfer = 0, Handoff = 1, ElevatorBoarding = 2, Walking = 3,
        // Task
        CompletionNod = 4, ErrorWorry = 5, WaitingOffer = 6,
        // Social
        Poke = 7, Wave = 8, Glance = 9, Selected = 10,
        // Ambient
        IdleVariation = 11,
    }

    public enum ReactionVerdict
    {
        Play = 0,
        Queued = 1,
        DroppedFocus = 2,
        DroppedHandsBusy = 3,
        DroppedCooldown = 4,
        DroppedRateCap = 5,
        DroppedLowerPriority = 6,
        DroppedStale = 7,
        DroppedQueueFull = 8,
        DroppedBadArgs = 9,
    }

    public struct ReactionRequest
    {
        public int Agent;
        public ReactionKind Kind;
        public float RequestedAt;
        /// <summary>True when a confirmed run/board/handoff status backs it (survives Focus for Social/Ambient).</summary>
        public bool StatusBacked;

        public ReactionRequest(int agent, ReactionKind kind, float requestedAt, bool statusBacked = false)
        { Agent = agent; Kind = kind; RequestedAt = requestedAt; StatusBacked = statusBacked; }
    }

    [System.Serializable]
    public sealed class ReactionArbiterConfig
    {
        public int AgentCount = 3;
        /// <summary>Per-agent minimum gap between any two non-critical reactions.</summary>
        public float PerAgentGap = 0.8f;
        /// <summary>Global cap: at most this many non-critical plays per <see cref="RateWindow"/> seconds.</summary>
        public int RateCapPlays = 4;
        public float RateWindow = 6f;
        /// <summary>Queued reactions older than this (since RequestedAt) are dropped as stale.</summary>
        public float MaxQueueAge = 2.5f;
        /// <summary>Same as NodGate: a confirmed-completion nod is silenced under Focus.</summary>
        public bool FocusSilencesCompletionNod = true;
        /// <summary>Safety net: an open-ended (Begin..End) reaction auto-expires after this long.</summary>
        public float MaxOpenEndedSeconds = 60f;
    }

    public sealed class ReactionArbiter
    {
        public const int KindCount = 12;
        public const int QueueCapacity = 8;

        public static ReactionClass ClassOf(ReactionKind k)
        {
            switch (k)
            {
                case ReactionKind.PaperTransfer:
                case ReactionKind.Handoff:
                case ReactionKind.ElevatorBoarding:
                case ReactionKind.Walking: return ReactionClass.Critical;
                case ReactionKind.CompletionNod:
                case ReactionKind.ErrorWorry:
                case ReactionKind.WaitingOffer: return ReactionClass.Task;
                case ReactionKind.Poke:
                case ReactionKind.Wave:
                case ReactionKind.Glance:
                case ReactionKind.Selected: return ReactionClass.Social;
                default: return ReactionClass.Ambient;
            }
        }

        /// <summary>Default clip length used to hold the agent's slot. Zero = open-ended, caller must <see cref="End"/>.</summary>
        public static float DefaultDuration(ReactionKind k)
        {
            switch (k)
            {
                case ReactionKind.PaperTransfer:
                case ReactionKind.Handoff:
                case ReactionKind.ElevatorBoarding:
                case ReactionKind.Walking: return 0f;
                case ReactionKind.CompletionNod: return 1.2f;
                case ReactionKind.ErrorWorry: return 2.0f;
                case ReactionKind.WaitingOffer: return 1.7f;
                case ReactionKind.Poke: return 0.9f;
                case ReactionKind.Wave: return 1.4f;
                case ReactionKind.Glance: return 1.2f;
                case ReactionKind.Selected: return 1.0f;
                default: return 2.0f;
            }
        }

        /// <summary>Per-agent per-kind cooldown (from the START of the last play of that kind).</summary>
        public static float KindCooldown(ReactionKind k)
        {
            switch (k)
            {
                case ReactionKind.CompletionNod: return NodGate.DefaultGapSeconds;
                case ReactionKind.ErrorWorry: return 6f;
                case ReactionKind.WaitingOffer: return 8f;
                case ReactionKind.Poke: return 0.3f;
                case ReactionKind.Wave: return 3f;
                case ReactionKind.Glance: return 5f;
                case ReactionKind.Selected: return 1.5f;
                case ReactionKind.IdleVariation: return 7f;
                default: return 0f; // Critical: governed by the body being free, not time
            }
        }

        readonly ReactionArbiterConfig _cfg;
        readonly int _n;
        // per agent
        readonly ReactionKind[] _activeKind;
        readonly bool[] _active;
        readonly float[] _activeUntil;     // <= 0 with _active: open-ended
        readonly float[] _activeSince;
        readonly float[] _lastNonCritical;
        readonly float[] _lastOfKind;      // [agent * KindCount + kind]
        // global rate ring
        readonly float[] _ring;
        int _ringHead, _ringCount;
        // queue (bounded, insertion order)
        readonly ReactionRequest[] _queue = new ReactionRequest[QueueCapacity];
        int _queueCount;

        bool _focus;
        bool _handsBusy;

        public int StaleDropped { get; private set; }
        public int QueuedCount { get { return _queueCount; } }

        public ReactionArbiter(ReactionArbiterConfig cfg = null)
        {
            _cfg = cfg ?? new ReactionArbiterConfig();
            _n = _cfg.AgentCount < 1 ? 1 : _cfg.AgentCount;
            _activeKind = new ReactionKind[_n];
            _active = new bool[_n];
            _activeUntil = new float[_n];
            _activeSince = new float[_n];
            _lastNonCritical = new float[_n];
            _lastOfKind = new float[_n * KindCount];
            for (int i = 0; i < _n; i++) _lastNonCritical[i] = -1e6f;
            for (int i = 0; i < _lastOfKind.Length; i++) _lastOfKind[i] = -1e6f;
            _ring = new float[_cfg.RateCapPlays < 1 ? 1 : _cfg.RateCapPlays];
        }

        public void SetFocus(bool on) { _focus = on; }
        public void SetHandsBusy(bool busy) { _handsBusy = busy; }

        /// <summary>Is the agent mid-reaction (its slot held)? Expires finished reactions lazily.</summary>
        public bool IsBusy(int agent, float now)
        {
            if (agent < 0 || agent >= _n) return false;
            Expire(agent, now);
            return _active[agent];
        }

        public bool TryGetActive(int agent, float now, out ReactionKind kind)
        {
            kind = ReactionKind.IdleVariation;
            if (!IsBusy(agent, now)) return false;
            kind = _activeKind[agent];
            return true;
        }

        void Expire(int a, float now)
        {
            if (!_active[a]) return;
            float until = _activeUntil[a];
            bool open = until <= 0f;
            if (open) { if (now - _activeSince[a] >= _cfg.MaxOpenEndedSeconds) _active[a] = false; }
            else if (now >= until) _active[a] = false;
        }

        /// <summary>Ask to play a reaction right now. On Play the slot is taken (Begin is implicit).</summary>
        public ReactionVerdict Request(float now, ReactionRequest r)
        {
            return Request(now, r, true);
        }

        ReactionVerdict Request(float now, ReactionRequest r, bool mayQueue)
        {
            if (r.Agent < 0 || r.Agent >= _n || (int)r.Kind < 0 || (int)r.Kind >= KindCount) return ReactionVerdict.DroppedBadArgs;
            ReactionClass cls = ClassOf(r.Kind);

            if (now - r.RequestedAt > _cfg.MaxQueueAge) { StaleDropped++; return ReactionVerdict.DroppedStale; }

            // Focus: Social + Ambient are dropped unless a confirmed status backs them.
            if (_focus && !r.StatusBacked && (cls == ReactionClass.Social || cls == ReactionClass.Ambient))
                return ReactionVerdict.DroppedFocus;
            if (_focus && cls == ReactionClass.Task && r.Kind == ReactionKind.CompletionNod && _cfg.FocusSilencesCompletionNod)
                return ReactionVerdict.DroppedFocus;

            // Hands busy silences Social (a hand-driven reaction while hands are occupied would be false).
            if (_handsBusy && cls == ReactionClass.Social) return ReactionVerdict.DroppedHandsBusy;

            // Body priority.
            Expire(r.Agent, now);
            if (_active[r.Agent])
            {
                ReactionClass cur = ClassOf(_activeKind[r.Agent]);
                if (cls <= cur) // equal never interrupts either
                    return Defer(r, cls, mayQueue, ReactionVerdict.DroppedLowerPriority);
                // strictly higher: preempts, falls through
            }

            if (cls != ReactionClass.Critical)
            {
                // The per-agent gap only throttles Social/Ambient chatter; a confirmed Task reaction is never gap-dropped.
                if (cls <= ReactionClass.Social && now - _lastNonCritical[r.Agent] < _cfg.PerAgentGap)
                    return Defer(r, cls, mayQueue, ReactionVerdict.DroppedCooldown);
                if (now - _lastOfKind[r.Agent * KindCount + (int)r.Kind] < KindCooldown(r.Kind)) return ReactionVerdict.DroppedCooldown;
                if (RateCapped(now)) return Defer(r, cls, mayQueue, ReactionVerdict.DroppedRateCap);
            }

            Take(now, r, cls);
            return ReactionVerdict.Play;
        }

        // Only Task reactions (and Social when merely body-blocked) are worth waiting for; Ambient is never queued.
        ReactionVerdict Defer(ReactionRequest r, ReactionClass cls, bool mayQueue, ReactionVerdict dropped)
        {
            if (!mayQueue || cls == ReactionClass.Ambient || cls == ReactionClass.Critical) return dropped;
            if (cls == ReactionClass.Social && dropped != ReactionVerdict.DroppedLowerPriority) return dropped;
            if (_queueCount >= QueueCapacity)
            {
                // Bounded: evict the oldest strictly-lower-or-equal-class entry for a Task; else drop the newcomer.
                int victim = -1;
                for (int i = 0; i < _queueCount; i++)
                    if (ClassOf(_queue[i].Kind) <= cls && (victim < 0 || _queue[i].RequestedAt < _queue[victim].RequestedAt)) victim = i;
                if (victim < 0) return ReactionVerdict.DroppedQueueFull;
                RemoveAt(victim);
            }
            _queue[_queueCount++] = r;
            return ReactionVerdict.Queued;
        }

        void RemoveAt(int i)
        {
            for (int j = i; j < _queueCount - 1; j++) _queue[j] = _queue[j + 1];
            _queueCount--;
        }

        bool RateCapped(float now)
        {
            if (_ringCount < _ring.Length) return false;
            // oldest play in the window
            float oldest = _ring[_ringHead];
            return now - oldest < _cfg.RateWindow;
        }

        void Take(float now, ReactionRequest r, ReactionClass cls)
        {
            int a = r.Agent;
            _active[a] = true;
            _activeKind[a] = r.Kind;
            _activeSince[a] = now;
            float d = DefaultDuration(r.Kind);
            _activeUntil[a] = d > 0f ? now + d : 0f;
            _lastOfKind[a * KindCount + (int)r.Kind] = now;
            if (cls != ReactionClass.Critical)
            {
                _lastNonCritical[a] = now;
                // ring buffer of play timestamps
                if (_ringCount < _ring.Length) { _ring[(_ringHead + _ringCount) % _ring.Length] = now; _ringCount++; }
                else { _ring[_ringHead] = now; _ringHead = (_ringHead + 1) % _ring.Length; }
            }
        }

        /// <summary>Release an open-ended reaction (walk finished, paper delivered, ...). Also cuts a timed one short.</summary>
        public void End(int agent, ReactionKind kind)
        {
            if (agent < 0 || agent >= _n) return;
            if (_active[agent] && _activeKind[agent] == kind) _active[agent] = false;
        }

        /// <summary>Drop stale queue entries, then return the oldest still-playable queued reaction (it is taken).
        /// Call once per frame or whenever a reaction ends. Returns false if nothing may play now.</summary>
        public bool Poll(float now, out ReactionRequest played)
        {
            played = default(ReactionRequest);
            for (int i = _queueCount - 1; i >= 0; i--)
                if (now - _queue[i].RequestedAt > _cfg.MaxQueueAge) { RemoveAt(i); StaleDropped++; }
            // Highest class first, then oldest.
            while (_queueCount > 0)
            {
                int best = 0;
                for (int i = 1; i < _queueCount; i++)
                {
                    ReactionClass ci = ClassOf(_queue[i].Kind), cb = ClassOf(_queue[best].Kind);
                    if (ci > cb || (ci == cb && _queue[i].RequestedAt < _queue[best].RequestedAt)) best = i;
                }
                ReactionRequest r = _queue[best];
                RemoveAt(best);
                ReactionVerdict v = Request(now, r, false);
                if (v == ReactionVerdict.Play) { played = r; return true; }
                if (v == ReactionVerdict.DroppedLowerPriority || v == ReactionVerdict.DroppedCooldown || v == ReactionVerdict.DroppedRateCap)
                {
                    // Still blocked: put it back and stop (do not spin).
                    _queue[_queueCount++] = r;
                    return false;
                }
                // Focus / hands busy / stale: gone for good, try the next one.
            }
            return false;
        }

        public void ClearQueue() { _queueCount = 0; }
    }

    /// <summary>Tiny deterministic xorshift32. Same seed, same sequence, on every platform.</summary>
    public sealed class SeededRng
    {
        uint _s;
        public SeededRng(int seed) { _s = (uint)seed * 2654435761u; if (_s == 0) _s = 0x9E3779B9u; }
        public uint NextUInt() { uint x = _s; x ^= x << 13; x ^= x >> 17; x ^= x << 5; _s = x; return x; }
        /// <summary>[0,1)</summary>
        public float NextFloat() { return (NextUInt() >> 8) * (1f / 16777216f); }
        public int NextInt(int minInclusive, int maxExclusive)
        {
            if (maxExclusive <= minInclusive) return minInclusive;
            return minInclusive + (int)(NextUInt() % (uint)(maxExclusive - minInclusive));
        }
    }

    /// <summary>Bounded ambient variation: which idle variant and how long until the next one.</summary>
    public sealed class AmbientVariation
    {
        readonly SeededRng _rng;
        readonly int _variants;
        readonly float _min, _max;
        int _last = -1;

        public AmbientVariation(int seed, int variantCount, float minDelay, float maxDelay)
        {
            _rng = new SeededRng(seed);
            _variants = variantCount < 1 ? 1 : variantCount;
            _min = minDelay < 0.5f ? 0.5f : minDelay;
            _max = maxDelay < _min ? _min : maxDelay;
        }

        /// <summary>Always within [min,max]. Slow multiplier (Focus) is clamped to 1..4.</summary>
        public float NextDelay(float slowMultiplier = 1f)
        {
            float m = slowMultiplier < 1f ? 1f : (slowMultiplier > 4f ? 4f : slowMultiplier);
            return (_min + (_max - _min) * _rng.NextFloat()) * m;
        }

        /// <summary>Variant in [0,count); never repeats the previous one when there is a choice.</summary>
        public int NextVariant()
        {
            int v = _rng.NextInt(0, _variants);
            if (_variants > 1 && v == _last) v = (v + 1 + _rng.NextInt(0, _variants - 1)) % _variants;
            _last = v;
            return v;
        }
    }
}
