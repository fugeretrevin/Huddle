using System.Collections.Generic;
using UnityEngine;
using MuseDesk.Documents;
using MuseDesk.Spatial;
using MuseDesk.Spatial.Wall;

namespace MuseDesk.Wall
{
    /// <summary>
    /// The desk's physical stamps work on the Manager plan paper too, as an alternative to the Approve and Reject keys.
    ///
    /// A held Approve stamp pressed onto the paper's stamp spot calls <see cref="ApprovePlan"/>; a held Reject stamp calls
    /// <see cref="RejectPlan"/>. Those are the same guarded methods the keys call, so the same rules apply (a risky step still
    /// needs its separate OK, one commit in flight, a stable idempotency key, nothing created before the backend confirms), and the
    /// APPROVED / REJECTED mark is still drawn only after the backend (or the labelled offline board) confirmed.
    ///
    /// Hand ownership: a stamp press needs the stamp to be HELD, and holding outranks every far gesture in the hand arbiter, so
    /// the pinch of Select Agents, placement or a visit carry cannot be in progress on that hand; a press is also ignored while
    /// the Wall itself is carrying an agent or aiming.
    ///
    /// While the paper awaits approval the stamps are shown (the same scale reveal the desk uses while a decision waits) and
    /// summoned if they had left; when it is settled they leave again unless a desk decision still needs them.
    /// </summary>
    public partial class WallHabitatController
    {
        readonly PlanStampGate[] _stampGates = { new PlanStampGate(), new PlanStampGate(), new PlanStampGate(), new PlanStampGate() };
        StampTool[] _stamps = new StampTool[0];
        StampPoof[] _stampPoofs = new StampPoof[0];
        float _stampScanAt, _stampDemandUntil;
        bool _stampDemand;

        /// <summary>Presses of a stamp on the plan paper that were accepted (drivers and tests read these).</summary>
        public int PlanStampApproves { get; private set; }
        public int PlanStampRejects { get; private set; }

        bool PlanAwaitsStamp
        {
            get
            {
                if (_manager == null || !flags.wallManagerEnabled || !_planCardOpen || _planCard == null || !_planCard.Visible) return false;
                return _manager.State == ManagerFlowState.AwaitingApproval && !_rejectBusy;
            }
        }

        void TickPlanStamp(float dt, float now)
        {
            bool needed = flags.wallManagerEnabled && _planCardOpen && _manager != null
                          && (_manager.State == ManagerFlowState.AwaitingApproval || _manager.State == ManagerFlowState.Committing);
            if (needed) _stampDemandUntil = now + 3f;                    // the stamp stays a moment after the answer
            bool demand = now < _stampDemandUntil;
            if (demand != _stampDemand)
            {
                _stampDemand = demand;
                NearUserKit.ExternalStampDemand = demand;
                if (demand) SummonStamps();
                else DismissStamps();
            }
            if (!demand) return;

            if (now >= _stampScanAt || HasDeadStamp())
            {
                _stampScanAt = now + 2f;
                _stamps = FindObjectsByType<StampTool>(FindObjectsInactive.Exclude);
                _stampPoofs = FindObjectsByType<StampPoof>(FindObjectsInactive.Exclude);
                if (demand) SummonStamps();
            }

            bool armed = PlanAwaitsStamp && _carryHand < 0 && _aimHand < 0;
            float scale = nearUserRig != null ? Mathf.Max(0.01f, nearUserRig.lossyScale.x) : 1f;
            Vector3 spot = _paper != null ? _paper.StampSpotWorld : Vector3.zero;
            for (int i = 0; i < _stamps.Length && i < _stampGates.Length; i++)
            {
                StampTool s = _stamps[i];
                if (s == null || s.inkHead == null) continue;
                PlanStampGate g = _stampGates[i];
                float dist = (s.inkHead.position - spot).magnitude / scale;
                if (!g.Tick(dt, s.Held, armed, dist)) continue;
                PressPlanStamp(s);
            }
        }

        bool HasDeadStamp()
        {
            for (int i = 0; i < _stamps.Length; i++) if (_stamps[i] == null) return true;
            return false;
        }

        /// <summary>A stamp was pressed onto the paper: the same guarded path as the Approve or Reject key.</summary>
        void PressPlanStamp(StampTool s)
        {
            bool approve = s.action == StampTool.StampAction.Approve;
            bool sent = approve ? ApprovePlan() : RejectStampPlan();
            if (!sent) return;                       // refused (a risky step needs its OK first): the note says why
            if (approve) PlanStampApproves++; else PlanStampRejects++;
            s.PlayPressCue();
            Debug.Log("[MuseDesk.Wall] Plan stamp " + s.action + " pressed: request sent (the mark appears when the backend confirms).");
        }

        bool RejectStampPlan()
        {
            if (_manager.State != ManagerFlowState.AwaitingApproval || _rejectBusy || _backend == null) return false;
            RejectPlan();
            return _rejectBusy || _manager.State == ManagerFlowState.Rejected;     // the offline board answers at once
        }

        /// <summary>Driver/test hook: press the first stamp of that kind as a hand would (needs a stamp in the scene).</summary>
        public bool PressPlanStampForDriver(bool approve)
        {
            if (!PlanAwaitsStamp) return false;
            StampTool s = FindStamp(approve);
            if (s == null) return false;
            PressPlanStamp(s);
            return true;
        }

        StampTool FindStamp(bool approve)
        {
            StampTool[] all = _stamps.Length > 0 ? _stamps : FindObjectsByType<StampTool>(FindObjectsInactive.Exclude);
            for (int i = 0; i < all.Length; i++)
                if (all[i] != null && (all[i].action == StampTool.StampAction.Approve) == approve) return all[i];
            return null;
        }

        void SummonStamps()
        {
            StampPoof[] poofs = _stampPoofs.Length > 0 ? _stampPoofs : FindObjectsByType<StampPoof>(FindObjectsInactive.Exclude);
            for (int i = 0; i < poofs.Length; i++) if (poofs[i] != null) poofs[i].Summon();
        }

        void DismissStamps()
        {
            // A desk decision that still waits keeps its stamps.
            AttentionController a = AttentionController.Instance;
            if (a != null && a.PendingDecisionCount > 0) return;
            StampPoof[] poofs = _stampPoofs.Length > 0 ? _stampPoofs : FindObjectsByType<StampPoof>(FindObjectsInactive.Exclude);
            for (int i = 0; i < poofs.Length; i++) if (poofs[i] != null) poofs[i].Dismiss();
        }

        void ResetPlanStamp()
        {
            for (int i = 0; i < _stampGates.Length; i++) _stampGates[i].Reset();
            if (_stampDemand) { _stampDemand = false; _stampDemandUntil = 0f; NearUserKit.ExternalStampDemand = false; }
        }
    }
}
