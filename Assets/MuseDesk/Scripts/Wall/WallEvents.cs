using System;

namespace MuseDesk.Wall
{
    /// <summary>
    /// Tiny static event hub for things that really happened on the wall (raised by WallHabitatController.Hints.cs from the
    /// controller's true state). The first-use hint presenter observes these; nothing else depends on them. Static
    /// subscribers must unsubscribe in OnDisable.
    /// </summary>
    public static class WallEvents
    {
        /// <summary>The Manager plan paper opened and is awaiting a decision.</summary>
        public static event Action ManagerPlanShown;
        /// <summary>The plan was approved (by stamp, after the backend accepted it).</summary>
        public static event Action ManagerPlanApproved;
        /// <summary>An agent was picked up from the wall (Select, Meet or a carry).</summary>
        public static event Action AgentSelected;
        /// <summary>The large agent is standing beside the user (the portal projection opened).</summary>
        public static event Action PortalOpened;

        public static void RaiseManagerPlanShown() { var h = ManagerPlanShown; if (h != null) h(); }
        public static void RaiseManagerPlanApproved() { var h = ManagerPlanApproved; if (h != null) h(); }
        public static void RaiseAgentSelected() { var h = AgentSelected; if (h != null) h(); }
        public static void RaisePortalOpened() { var h = PortalOpened; if (h != null) h(); }
        /// <summary>rc12: the user touched or waved at a visitor (portal or desk perch): first-use hint bookkeeping.</summary>
        public static event Action VisitorGreeted;
        public static void RaiseVisitorGreeted() { var h = VisitorGreeted; if (h != null) h(); }
    }
}
