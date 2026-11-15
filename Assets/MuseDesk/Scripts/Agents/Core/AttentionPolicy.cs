namespace MuseDesk.Agents
{
    // Sprint A3: pure "where does this agent look" policy. One instance per agent; time injected. No engine types.
    // The caller maps AttentionTarget to a Transform (user head, colleague head, workstation) and feeds
    // AgentGaze.SetAttention*(target, seconds, weight) - this class only decides WHICH and for how long.

    public enum AttentionTarget { Workstation = 0, User = 1, Colleague = 2 }

    [System.Serializable]
    public sealed class AttentionConfig
    {
        /// <summary>Max time the gaze stays on the user after being addressed, then back to work.</summary>
        public float MaxAddressedDwell = 3.0f;
        /// <summary>Max time on the user for an unprompted glance request.</summary>
        public float MaxGlanceDwell = 1.0f;
        /// <summary>After any user-look ends, no new unprompted user-look for this long.</summary>
        public float UserCooldown = 5.0f;
        /// <summary>A fresh address during cooldown still gets this short acknowledgement (never ignored outright).</summary>
        public float CooldownAckDwell = 1.2f;
        public float BlendToUser = 0.25f;
        public float BlendToWork = 0.5f;
        public float BlendToColleague = 0.35f;
        public float WeightUser = 0.9f, WeightColleague = 0.7f, WeightWork = 0.5f;
    }

    public struct AttentionContext
    {
        /// <summary>The user is addressing this agent right now (level; edges are detected internally).</summary>
        public bool AddressedByUser;
        /// <summary>An unprompted glance at the user was requested (edge-detected, budgeted).</summary>
        public bool GlanceAtUserRequested;
        /// <summary>A handoff with a colleague is in progress (confirmed state).</summary>
        public bool HandoffActive;
        /// <summary>The agent's confirmed status is a working one.</summary>
        public bool Working;
    }

    public struct AttentionDecision
    {
        public AttentionTarget Target;
        public float Weight;
        /// <summary>Suggested blend time into this target (seconds); smooth, never a snap.</summary>
        public float BlendSeconds;
        /// <summary>True on the frame the target changed (caller re-issues SetAttention only then).</summary>
        public bool Switched;
    }

    public sealed class AttentionPolicy
    {
        readonly AttentionConfig _c;
        bool _wasAddressed, _wasGlance;
        bool _lookingAtUser;
        float _userSince, _userUntil;
        float _lastUserEnd = -1e6f;
        AttentionTarget _current = AttentionTarget.Workstation;
        bool _started;

        public AttentionPolicy(AttentionConfig cfg = null) { _c = cfg ?? new AttentionConfig(); }

        public AttentionTarget Current { get { return _current; } }
        /// <summary>Total seconds the current continuous user-look has lasted (0 if not looking at the user).</summary>
        public float UserLookSeconds(float now) { return _lookingAtUser ? now - _userSince : 0f; }

        public AttentionDecision Update(float now, AttentionContext ctx)
        {
            bool addrEdge = ctx.AddressedByUser && !_wasAddressed;
            bool glanceEdge = ctx.GlanceAtUserRequested && !_wasGlance;
            _wasAddressed = ctx.AddressedByUser;
            _wasGlance = ctx.GlanceAtUserRequested;

            // End a user-look that ran out (hard dwell limit: never a continuous stare).
            if (_lookingAtUser && now >= _userUntil) { _lookingAtUser = false; _lastUserEnd = now; }

            bool cooling = now - _lastUserEnd < _c.UserCooldown;

            // A handoff is real work between colleagues; it outranks a new look at the user, and cuts one short.
            if (ctx.HandoffActive)
            {
                if (_lookingAtUser) { _lookingAtUser = false; _lastUserEnd = now; }
            }
            else if (!_lookingAtUser)
            {
                if (addrEdge)
                {
                    float dwell = cooling ? _c.CooldownAckDwell : _c.MaxAddressedDwell;
                    if (dwell > _c.MaxAddressedDwell) dwell = _c.MaxAddressedDwell;
                    StartUser(now, dwell);
                }
                else if (glanceEdge && !cooling)
                {
                    StartUser(now, _c.MaxGlanceDwell);
                }
            }
            else if (addrEdge && !cooling)
            {
                // re-addressed mid-look: extend, but never beyond MaxAddressedDwell from now
                _userUntil = now + _c.MaxAddressedDwell;
            }

            AttentionTarget t;
            if (ctx.HandoffActive) t = AttentionTarget.Colleague;
            else if (_lookingAtUser) t = AttentionTarget.User;
            else t = AttentionTarget.Workstation; // working or idle: the desk, not the user's head

            AttentionDecision d = new AttentionDecision();
            d.Target = t;
            d.Switched = !_started || t != _current;
            _started = true;
            _current = t;
            switch (t)
            {
                case AttentionTarget.User: d.Weight = _c.WeightUser; d.BlendSeconds = _c.BlendToUser; break;
                case AttentionTarget.Colleague: d.Weight = _c.WeightColleague; d.BlendSeconds = _c.BlendToColleague; break;
                default: d.Weight = ctx.Working ? _c.WeightWork : _c.WeightWork * 0.6f; d.BlendSeconds = _c.BlendToWork; break;
            }
            return d;
        }

        void StartUser(float now, float dwell)
        {
            _lookingAtUser = true;
            _userSince = now;
            _userUntil = now + dwell;
        }
    }
}
