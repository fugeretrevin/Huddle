namespace MuseDesk.Agents
{
    /// <summary>
    /// The agent that carries a decision paper walks up, raises it ONCE and then holds that pose until the
    /// user takes the paper (or the decision is resolved / cancelled). Nothing may extend and retract it in
    /// a loop. Pure decisions only, so EditMode tests reach them; the animator bridge, the registry and the
    /// presence layer ask these questions.
    ///
    /// Why this exists: the Offer_Document clip is authored as a LOOPING clip (loopTime on, loop blend off)
    /// whose cycle raises the paper and lowers it again, so simply staying in that state gave a repeated
    /// give / take-back. The bridge therefore holds the raise by freezing the animator once the offer clip
    /// has reached its raised pose (<see cref="ShouldFreeze"/>), and the periodic "here it is" lean is not
    /// repeated while the paper is out (<see cref="AllowPresentGesture"/>).
    /// </summary>
    public static class OfferHoldPolicy
    {
        /// <summary>Normalized time at which the one-shot Offer_Document_Present (the raise, 20 frames) is
        /// held: just short of its end, so its exit transition to the looping state never fires.</summary>
        public const float PresentClipHoldAt = 0.85f;

        /// <summary>Normalized time at which the looping Offer_Document is held when there is no separate
        /// raise clip: early in the cycle, while the paper is up and before it comes down.</summary>
        public const float LoopClipHoldAt = 0.15f;

        public static float HoldAt(bool hasPresentClip) { return hasPresentClip ? PresentClipHoldAt : LoopClipHoldAt; }

        /// <summary>Freeze the animator now? Only while the offer is the thing being shown (not walking, no
        /// one-shot over it), the cross-fade into it is finished, the current state is the offer state, and
        /// it has reached its hold time.</summary>
        public static bool ShouldFreeze(bool offerHold, bool moving, bool oneShot, bool inTransition,
                                        bool inOfferState, float normalizedTime, float holdAt)
        {
            if (!offerHold || moving || oneShot || inTransition || !inOfferState) return false;
            return normalizedTime >= holdAt;
        }

        /// <summary>Should the animator be released from a hold? Whenever the offer is no longer what the
        /// agent is doing.</summary>
        public static bool ShouldRelease(bool frozen, bool offerHold, bool moving, bool oneShot)
        {
            return frozen && (!offerHold || moving || oneShot);
        }

        /// <summary>The periodic "present the paper" lean / nod / point (AgentAttention's waiting-agent
        /// gesture). While the agent is already holding the paper out it is not repeated: the offer pose
        /// stays steady until the paper is taken.</summary>
        public static bool AllowPresentGesture(bool bridgeIsOffering)
        {
            return !bridgeIsOffering;
        }

        /// <summary>The offer was knocked off the agent (a reaction ended it, an idle layer reset the pose)
        /// although nothing legitimately ended it: raise it again, once, without a walk. False once the paper
        /// is taken, the decision is gone, the agent walks or is away from the hand-over spot, a one-shot is
        /// playing, or it is already offering.</summary>
        public static bool NeedsReassert(bool offerExpected, bool decisionPending, bool paperTaken,
                                         bool moving, bool atHome, bool offering, bool oneShot)
        {
            if (!offerExpected || !decisionPending || paperTaken) return false;
            if (moving || atHome || oneShot) return false;
            return !offering;
        }
    }
}
