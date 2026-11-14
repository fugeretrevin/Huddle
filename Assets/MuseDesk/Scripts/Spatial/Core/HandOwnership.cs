using System;
using UnityEngine;

namespace MuseDesk.Spatial
{
    // One owner per hand (rc6 stabilisation). Pure logic, no scene access, no allocation per tick, so
    // the EditMode tests reach it. The runtime side (HandInteractionProbe) only READS the Interaction
    // SDK interactors and fills AimHandSample.holding / nearHover / uiHover; the gestures that can move
    // the office (FloorAimSession, CeilingPullGesture) arm only through HandOwnershipArbiter.
    //
    // Priority, highest first:
    //   1 Holding ........... something is held: by this hand, by the other hand, or a paper's transition
    //                         hold. Spatial movement can never start while anything is held, and a
    //                         placement or summon gesture in progress is cancelled when something is picked up.
    //   2 CloseInteraction .. a grab (near or distance), a poke, or a ray over a UI control is hovering or
    //                         selecting, or was within the last nearClearSeconds. Its pinch belongs to it.
    //   3 Placement ......... an explicitly opened placement control (the floor footprint drag).
    //   4 Summon ............ a targeted distant summon gesture (the ceiling pull on the docked pod).
    //   5 Ambient ........... nothing claims the hand (gaze, glances).

    //   (Wall Habitat) AgentSelect ... the explicitly armed Select Agents far pinch. It only exists in Wall
    //                         mode, where no floor footprint drag or ceiling pull can be armed, so its
    //                         number (6) never competes with Placement/Summon; Holding and CloseInteraction
    //                         still beat it exactly as above (a paper, stamp or card pinch is never taken).
    public enum HandOwner { None = 0, Holding = 1, CloseInteraction = 2, Placement = 3, Summon = 4, Ambient = 5, AgentSelect = 6 }

    /// <summary>What a gesture claims a hand for. The number is the priority (lower wins).</summary>
    public enum HandClaim { None = 0, Placement = 3, Summon = 4, AgentSelect = 6 }

    /// <summary>What the arbiter tells the gesture about one hand on one tick.</summary>
    public enum HandGateSignal
    {
        /// <summary>Nothing for a gesture: open, spent, still debouncing, or another claimant's.</summary>
        None = 0,
        /// <summary>A fresh, debounced pinch on a clear hand: it may be claimed on THIS tick only.</summary>
        Ready = 1,
        /// <summary>Claimed and still pinched (or inside the release debounce).</summary>
        Holding = 2,
        /// <summary>Claimed, tracking lost, inside the grace time: do nothing, neither move nor release.</summary>
        Frozen = 3,
        /// <summary>Claimed, and the pinch was SEEN to open on a tracked hand: the only genuine release.</summary>
        Released = 4,
        /// <summary>Claimed, and taken away (tracking lost, something held, preempted). Never a release.</summary>
        Cancelled = 5,
    }

    /// <summary>Why the last pinch of a hand did not arm, or why its claim was cancelled.</summary>
    public enum HandBlock
    {
        None = 0,
        /// <summary>The pinch was closed before the hand was seen open (or after a cancel): open and pinch again.</summary>
        NotFresh = 1,
        /// <summary>Something is held (either hand, or a paper's transition hold).</summary>
        Holding = 2,
        /// <summary>A grab interactor of this hand hovered something (or did, within the clear window).</summary>
        CloseInteraction = 3,
        /// <summary>A poke or ray of this hand was over a UI control (or was, within the clear window).</summary>
        Ui = 4,
        /// <summary>The hand was lost (or came back open after a loss).</summary>
        TrackingLost = 5,
        /// <summary>A higher-priority claim took the hand.</summary>
        Preempted = 6,
        /// <summary>The pinch became ready but no gesture took it (not on a target): open and pinch again.</summary>
        NotTaken = 7,
    }

    /// <summary>The timing of hand ownership. Seconds.</summary>
    [Serializable]
    public sealed class HandOwnershipSettings
    {
        [Tooltip("A pinch must be held this long before it can arm a placement or summon gesture. The Interaction SDK " +
                 "reports a grab or poke hover within 1 to 3 frames (14 to 42 ms at 72 Hz) of the hand reaching an " +
                 "object, and pinch strength takes about 50 to 100 ms to cross its threshold; 0.15 s (11 frames) leaves " +
                 "it room to claim the pinch first, and stays under the ~0.2 s at which a drag start feels late.")]
        public float minPinchSeconds = 0.15f;
        [Tooltip("The hand must have been tracked and free of any hold, grab hover, poke hover or UI ray hover for " +
                 "this long, continuously, when the pinch BEGINS (hysteresis on the hover), and stay so while it " +
                 "debounces. Hover flickers for a few frames while the fingers close on a paper; 0.3 s covers the " +
                 "approach-then-pinch, and re-aiming from a paper on the desk to a spot 1 to 3 m away, or from a key " +
                 "to the pod overhead, takes longer than that anyway.")]
        public float nearClearSeconds = 0.30f;
        [Tooltip("A hand must be seen open (tracked, not pinching) this long before its next pinch counts as fresh. " +
                 "Six frames: a one- or two-frame pinch dropout does not make a new pinch.")]
        public float minOpenSeconds = 0.08f;
        [Tooltip("A claimed pinch must read open (on a tracked hand) this long to count as released. Hand tracking " +
                 "drops a pinch for a frame or two; the previous floor aim used 0.12 s.")]
        public float releaseDebounce = 0.12f;
        [Tooltip("A claimed hand may be untracked this long and continue (frozen). Longer, or back with the pinch " +
                 "open, cancels: a lost hand is never a release.")]
        public float lostGraceSeconds = 0.20f;
    }

    /// <summary>Who has claimed each hand. Shared by every gesture of the running app; tests make their own.</summary>
    public sealed class HandClaimRegistry
    {
        /// <summary>The one registry of the running app (FloorAimController and CeilingPullGesture use it).</summary>
        public static readonly HandClaimRegistry Shared = new HandClaimRegistry();

        readonly HandClaim[] _owner = new HandClaim[2];

        public HandClaim Owner(int hand) { return _owner[hand & 1]; }

        /// <summary>Take a hand. Allowed when it is free, already this claim's, or held by a lower-priority claim
        /// (which then sees itself preempted).</summary>
        public bool TryClaim(int hand, HandClaim claim)
        {
            if (claim == HandClaim.None) return false;
            HandClaim cur = _owner[hand & 1];
            if (cur == HandClaim.None || cur == claim || (int)claim < (int)cur)
            {
                _owner[hand & 1] = claim;
                return true;
            }
            return false;
        }

        public void Release(int hand, HandClaim claim)
        {
            if (_owner[hand & 1] == claim) _owner[hand & 1] = HandClaim.None;
        }

        public void Clear() { _owner[0] = _owner[1] = HandClaim.None; }
    }

    /// <summary>
    /// The pinch of each hand, owned by at most one thing. A gesture that could move the office (the
    /// floor footprint drag, the ceiling pull) keeps one of these, ticks it with both hands every frame,
    /// and may take a hand only on the tick its signal is <see cref="HandGateSignal.Ready"/>:
    ///
    ///  - Fresh: the hand was seen open for minOpenSeconds before the pinch closed. A pinch that was
    ///    already closed (the hand that pressed a key, a hand that just lost tracking, any cancel) has
    ///    to open and close again.
    ///  - Debounced: the pinch has been held minPinchSeconds.
    ///  - Clear: nothing is held anywhere, and this hand was tracked and its grab, poke and UI ray hovered
    ///    nothing for nearClearSeconds BEFORE the pinch began. Any hold or hover during the debounce spends
    ///    the pinch too (it belongs to the object).
    ///  - Taken now or never: a Ready pinch nobody claims on that tick is spent, so a pinch that began off
    ///    the target cannot arm when it is swept onto it.
    ///
    /// Once claimed: Holding while pinched; Released only when a TRACKED hand is seen open for
    /// releaseDebounce; Frozen while tracking is lost within lostGraceSeconds; Cancelled when tracking is
    /// lost longer or the hand comes back open, when anything is picked up (either hand), or when a
    /// higher-priority claim takes the hand. After Released or Cancelled the next arm needs a fresh pinch.
    /// </summary>
    public sealed class HandOwnershipArbiter
    {
        enum GateState { Open = 0, Spent = 1, Pending = 2, Ready = 3, Claimed = 4, Lost = 5 }

        struct Gate
        {
            public GateState state;
            public float openTime, pinchTime, offTime, lostTime, clearTime;
            public bool began, tracked, holding, busyNear, busyUi;
            public HandBlock lastBusy;          // what made the hand busy most recently (for the block reason)
            public HandClaim claim;
            public HandGateSignal signal;
            public HandBlock block;
        }

        readonly HandClaimRegistry _registry;
        Gate _left, _right;
        bool _anyHeld;

        public HandOwnershipSettings settings = new HandOwnershipSettings();
        /// <summary>Something outside the hands' interactors is held (a paper's transition hold). Set before Tick.</summary>
        public bool externallyHeld;

        public HandOwnershipArbiter(HandClaimRegistry registry = null)
        {
            _registry = registry ?? new HandClaimRegistry();
            Reset();
        }

        public HandClaimRegistry Registry => _registry;

        /// <summary>True when anything was held on the last tick (either hand or the transition hold).</summary>
        public bool AnyHeld => _anyHeld;

        /// <summary>Forget everything and give back any claim. A pinch closed at the next tick is not fresh.</summary>
        public void Reset()
        {
            Drop(ref _left, 0);
            Drop(ref _right, 1);
            _left = default(Gate);
            _right = default(Gate);
            _anyHeld = false;
        }

        void Drop(ref Gate g, int hand)
        {
            if (g.claim != HandClaim.None) _registry.Release(hand, g.claim);
            g.claim = HandClaim.None;
        }

        public HandGateSignal Signal(int hand) { return hand == 0 ? _left.signal : _right.signal; }
        /// <summary>True on the tick a fresh pinch closed (the gesture records whether it began on its target).</summary>
        public bool Began(int hand) { return hand == 0 ? _left.began : _right.began; }
        public HandBlock LastBlock(int hand) { return hand == 0 ? _left.block : _right.block; }
        public HandClaim Claim(int hand) { return hand == 0 ? _left.claim : _right.claim; }
        public bool IsClaimed(int hand) { GateState s = hand == 0 ? _left.state : _right.state; return s == GateState.Claimed || s == GateState.Lost; }

        /// <summary>Who owns the hand now, in the priority order of the class comment.</summary>
        public HandOwner Owner(int hand)
        {
            Gate g = hand == 0 ? _left : _right;
            if (g.claim != HandClaim.None) return (HandOwner)(int)g.claim;
            if (!g.tracked) return HandOwner.None;
            if (g.holding || _anyHeld) return HandOwner.Holding;
            if (g.busyNear || g.busyUi || g.clearTime < settings.nearClearSeconds) return HandOwner.CloseInteraction;
            return HandOwner.Ambient;
        }

        /// <summary>One frame for both hands. Read the signals after this, then claim at most on Ready.</summary>
        public void Tick(float dt, AimHandSample left, AimHandSample right)
        {
            if (dt < 0f) dt = 0f;
            _anyHeld = externallyHeld || left.holding || right.holding;
            Step(ref _left, 0, left, dt);
            Step(ref _right, 1, right, dt);
        }

        /// <summary>Take the hand for a gesture. Only on the tick its signal is Ready; false otherwise (and when a
        /// higher-priority claim holds it, in which case this pinch is spent).</summary>
        public bool TryClaim(int hand, HandClaim claim)
        {
            if (hand < 0 || hand > 1 || claim == HandClaim.None) return false;
            if (hand == 0) return Claim(ref _left, 0, claim);
            return Claim(ref _right, 1, claim);
        }

        bool Claim(ref Gate g, int hand, HandClaim claim)
        {
            if (g.state != GateState.Ready) return false;
            if (!_registry.TryClaim(hand, claim))
            {
                Spend(ref g, hand, HandBlock.Preempted);
                return false;
            }
            g.state = GateState.Claimed;
            g.claim = claim;
            g.offTime = 0f;
            g.lostTime = 0f;
            g.signal = HandGateSignal.Holding;
            g.block = HandBlock.None;
            return true;
        }

        /// <summary>The gesture is done with the hand (a key ended its drag, it fired, ...). The pinch is spent:
        /// the next arm needs a fresh one.</summary>
        public void Relinquish(int hand)
        {
            if (hand < 0 || hand > 1) return;
            if (hand == 0) Spend(ref _left, 0, HandBlock.None); else Spend(ref _right, 1, HandBlock.None);
        }

        void Spend(ref Gate g, int hand, HandBlock why)
        {
            if (g.claim != HandClaim.None) _registry.Release(hand, g.claim);
            g.claim = HandClaim.None;
            g.state = GateState.Spent;
            if (why != HandBlock.None) g.block = why;
        }

        void Cancel(ref Gate g, int hand, HandBlock why)
        {
            if (g.claim != HandClaim.None) _registry.Release(hand, g.claim);
            g.claim = HandClaim.None;
            g.state = GateState.Spent;
            g.block = why;
            g.signal = HandGateSignal.Cancelled;
        }

        void Step(ref Gate g, int hand, AimHandSample s, float dt)
        {
            g.signal = HandGateSignal.None;
            g.began = false;
            g.tracked = s.tracked;
            g.holding = s.holding;
            g.busyNear = s.nearHover;
            g.busyUi = s.uiHover;
            bool pinching = s.tracked && s.pinching;

            // Clear streak: an untracked hand is not known to be clear.
            bool busy = s.holding || s.nearHover || s.uiHover || _anyHeld || !s.tracked;
            HandBlock busyWhy = _anyHeld || s.holding ? HandBlock.Holding : s.uiHover ? HandBlock.Ui
                              : s.nearHover ? HandBlock.CloseInteraction : HandBlock.TrackingLost;
            if (busy) { g.clearTime = 0f; g.lastBusy = busyWhy; } else g.clearTime += dt;

            // A higher-priority claim took this hand in the registry.
            if ((g.state == GateState.Claimed || g.state == GateState.Lost) && _registry.Owner(hand) != g.claim)
            {
                g.claim = HandClaim.None;                       // not ours to release any more
                g.state = GateState.Spent;
                g.block = HandBlock.Preempted;
                g.signal = HandGateSignal.Cancelled;
                return;
            }

            // A Ready pinch nobody took on its tick is spent.
            if (g.state == GateState.Ready) { g.state = GateState.Spent; g.block = HandBlock.NotTaken; }

            if (!s.tracked)
            {
                g.openTime = 0f;
                switch (g.state)
                {
                    case GateState.Claimed:
                    case GateState.Lost:
                        if (g.state == GateState.Claimed) { g.state = GateState.Lost; g.lostTime = 0f; }
                        g.lostTime += dt;
                        if (g.lostTime > settings.lostGraceSeconds) Cancel(ref g, hand, HandBlock.TrackingLost);
                        else g.signal = HandGateSignal.Frozen;
                        return;
                    case GateState.Pending:
                        g.state = GateState.Spent;
                        g.block = HandBlock.TrackingLost;
                        return;
                    default:
                        if (g.state == GateState.Open) return;
                        return;                                  // Spent stays spent until seen open
                }
            }

            switch (g.state)
            {
                case GateState.Lost:
                    // Back from a tracking gap: pinched, carry on; open, cancel (nobody was seen letting go).
                    if (!pinching) { Cancel(ref g, hand, HandBlock.TrackingLost); return; }
                    g.state = GateState.Claimed;
                    g.lostTime = 0f;
                    goto case GateState.Claimed;

                case GateState.Claimed:
                    if (_anyHeld) { Cancel(ref g, hand, HandBlock.Holding); return; }
                    if (pinching) { g.offTime = 0f; g.signal = HandGateSignal.Holding; return; }
                    g.offTime += dt;
                    if (g.offTime >= settings.releaseDebounce)
                    {
                        _registry.Release(hand, g.claim);
                        g.claim = HandClaim.None;
                        g.state = GateState.Open;
                        g.openTime = g.offTime;
                        g.signal = HandGateSignal.Released;
                        g.block = HandBlock.None;
                    }
                    else g.signal = HandGateSignal.Holding;
                    return;

                case GateState.Open:
                    if (!pinching) { g.openTime += dt; return; }
                    if (g.openTime < settings.minOpenSeconds) { g.state = GateState.Spent; g.block = HandBlock.NotFresh; return; }
                    g.state = GateState.Pending;
                    g.pinchTime = 0f;
                    g.offTime = 0f;
                    g.began = true;
                    // The hand must have been clear for the whole window BEFORE the pinch began: a pinch that
                    // starts as the hand leaves a paper, a stamp or a key belongs to that, not to a gesture.
                    if (!busy && g.clearTime < settings.nearClearSeconds) { g.state = GateState.Spent; g.block = g.lastBusy == HandBlock.None ? HandBlock.CloseInteraction : g.lastBusy; return; }
                    goto case GateState.Pending;

                case GateState.Pending:
                    if (!pinching)
                    {
                        // Inside the debounce a dropout is tolerated (the timer pauses); longer, it was a tap.
                        g.offTime += dt;
                        if (g.offTime >= settings.releaseDebounce) { g.state = GateState.Open; g.openTime = g.offTime; }
                        return;
                    }
                    g.offTime = 0f;
                    if (busy) { g.state = GateState.Spent; g.block = busyWhy; return; }
                    if (!g.began) g.pinchTime += dt;
                    if (g.pinchTime >= settings.minPinchSeconds)
                    {
                        g.state = GateState.Ready;
                        g.signal = HandGateSignal.Ready;
                    }
                    return;

                case GateState.Spent:
                    if (!pinching) { g.state = GateState.Open; g.openTime = dt; }
                    return;
            }
        }
    }
}
