using UnityEngine;
using MuseDesk.Spatial.Wall;

namespace MuseDesk.Wall
{
    /// <summary>
    /// First-use hint sources. <see cref="TickHints"/> polls state the controller already owns and raises
    /// <see cref="WallEvents"/> on each rising edge. It reads only; it changes nothing and allocates nothing.
    /// Call it once per frame from Update (after TickManager).
    /// </summary>
    public partial class WallHabitatController
    {
        bool _hintPlanShown, _hintAgentOut, _hintPortal;
        bool _hintPlanWasAwaiting;

        public void TickHints()
        {
            // Plan paper open and waiting for a decision.
            bool awaiting = _manager != null && _planCardOpen && _planCard != null && _planCard.Visible
                            && _manager.State == ManagerFlowState.AwaitingApproval;
            if (awaiting && !_hintPlanShown) WallEvents.RaiseManagerPlanShown();
            _hintPlanShown = awaiting;

            // Approved: it left AwaitingApproval for a state that means the backend accepted it.
            if (_manager != null)
            {
                ManagerFlowState s = _manager.State;
                bool accepted = s == ManagerFlowState.Committing || s == ManagerFlowState.Delegating || s == ManagerFlowState.Monitoring;
                if (s == ManagerFlowState.AwaitingApproval) _hintPlanWasAwaiting = true;
                else if (_hintPlanWasAwaiting && accepted) { _hintPlanWasAwaiting = false; WallEvents.RaiseManagerPlanApproved(); }
                else _hintPlanWasAwaiting = false;
            }

            // An agent picked up from the wall.
            bool agentOut = _visit != null && (_visit.State == VisitState.Carrying || _visit.State == VisitState.Perched);
            if (agentOut && !_hintAgentOut) WallEvents.RaiseAgentSelected();
            _hintAgentOut = agentOut;

            // The large agent standing beside the user.
            bool portal = HasPortalAvatar;
            if (portal && !_hintPortal) WallEvents.RaisePortalOpened();
            _hintPortal = portal;
        }
    }
}
