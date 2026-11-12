namespace MuseDesk.Phase2.Contracts
{
    /// <summary>
    /// Phase 2 feature flags. EVERY flag defaults OFF so none of the Phase 2 runtime
    /// (gateways, reducer, board presenters, drag controller) activates until an integrator
    /// deliberately turns one on — in a bootstrap script, an Editor menu, or a build define.
    /// This guarantees Phase 2 code is inert in a normal Phase 1 build and features can be
    /// enabled one at a time in live and demo mode (master §0.1, §14).
    ///
    /// These are plain static bools (no serialization) so they can be flipped from a single
    /// place at startup. A gating MonoBehaviour that reads these is additive and safe to leave
    /// in the scene while every flag is false.
    /// </summary>
    public static class Phase2Flags
    {
        /// <summary>Master kill-switch. When false, treat ALL Phase 2 features as off
        /// regardless of the per-feature flags.</summary>
        public static bool Phase2Enabled = false;

        // Per-track flags (names mirror the release flags in master §14).
        public static bool Board = false;        // p2_board   (P2-01)
        public static bool Artifacts = false;    // p2_artifacts (P2-02)
        public static bool Handoffs = false;     // p2_handoffs (P2-03)
        public static bool Factory = false;      // p2_factory (P2-07)
        public static bool Focus = false;        // p2_focus   (P2-06)
        public static bool Room = false;         // p2_room    (P2-05)
        public static bool Diagrams = false;     // p2_diagrams (P2-04)

        public static bool BoardActive => Phase2Enabled && Board;
        public static bool ArtifactsActive => Phase2Enabled && Artifacts;
        public static bool HandoffsActive => Phase2Enabled && Handoffs;
        public static bool FactoryActive => Phase2Enabled && Factory;
        public static bool FocusActive => Phase2Enabled && Focus;
        public static bool RoomActive => Phase2Enabled && Room;
        public static bool DiagramsActive => Phase2Enabled && Diagrams;
    }
}
