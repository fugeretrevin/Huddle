namespace MuseDesk.Spatial
{
    /// <summary>
    /// The deployment effects' phases, named after the art controller's pod states they drive
    /// (MuseDesk.Art.CeilingWorkspacePodController.PodState). Kept here, free of the art assembly,
    /// so the mapping from the real pod machine can be tested. Pure data; no safety logic.
    /// </summary>
    public enum CeilingVfxPhase
    {
        DockedIdle = 0,
        Unlocking = 1,
        Descending = 2,
        Stabilizing = 3,
        DeployedIdle = 4,
        ClosingShelf = 5,
        ReturningToDock = 6,
        DockLocking = 7,
    }

    public static class CeilingVfxMap
    {
        /// <summary>The effect phase for a real pod state. Descent shows a downward cyan flow, the way
        /// back up (Retracting and error recovery) an upward gold flow, and the dock lock and the
        /// hand-over to Docked fire the lock ripple and the settle ripple.</summary>
        public static CeilingVfxPhase Map(CeilingPodState state)
        {
            switch (state)
            {
                case CeilingPodState.PreparingToDescend: return CeilingVfxPhase.Unlocking;
                case CeilingPodState.Descending: return CeilingVfxPhase.Descending;
                case CeilingPodState.Stabilizing: return CeilingVfxPhase.Stabilizing;
                case CeilingPodState.Engaged: return CeilingVfxPhase.DeployedIdle;
                case CeilingPodState.PreparingToRetract: return CeilingVfxPhase.ClosingShelf;   // calm: nothing moves yet
                case CeilingPodState.Retracting: return CeilingVfxPhase.ReturningToDock;
                case CeilingPodState.ReturningToDock: return CeilingVfxPhase.DockLocking;
                case CeilingPodState.ErrorRecovery: return CeilingVfxPhase.ReturningToDock;
                default: return CeilingVfxPhase.DockedIdle;
            }
        }

        /// <summary>Effects are shown only in Ceiling mode, with the pod's world-fixed visuals on,
        /// and with the switch on. Anything else: hidden and stopped.</summary>
        public static bool ShouldShow(bool ceilingMode, bool podVisualsShown, bool vfxEnabled)
        {
            return vfxEnabled && podVisualsShown && ceilingMode;
        }
    }
}
