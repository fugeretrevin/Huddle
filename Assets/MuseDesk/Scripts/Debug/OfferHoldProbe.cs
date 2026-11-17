using System.Text;
using UnityEngine;
using MuseDesk.Agents;
using MuseDesk.Documents;

namespace MuseDesk.DebugTools
{
    /// <summary>
    /// Play Mode test probe for AgentScripts/OfferHoldDrive. It lives in the scene (added at run time by the driver)
    /// because each driver call compiles a fresh assembly, so state kept in the driver would not survive between
    /// Start and Report. Samples the offering agent every frame and never touches the paper.
    /// "Retracts" counts frames where the offer ended while the paper was still in the agent's hand, untaken,
    /// with the agent standing at the hand-over spot.
    /// </summary>
    public sealed class OfferHoldProbe : MonoBehaviour
    {
        public float sampleSeconds = 20f;
        public AgentAnimatorBridge bridge;
        public AgentMovement move;
        public HeldPaperController paper;
        public Transform ws;

        bool _offeredOnce, _done, _prevOffering;
        float _startedAt, _firstOfferAt = -1f;
        int _frames, _offeringFrames, _heldFrames, _retracts, _walkOffering;
        Vector3 _min = Vector3.positiveInfinity, _max = Vector3.negativeInfinity;
        string _endReason = "";

        void OnEnable() { _startedAt = Time.time; }

        void Update()
        {
            if (_done || bridge == null || move == null) return;
            _frames++;
            bool inHand = paper != null && paper.gameObject.activeInHierarchy && !paper.IsTaken;
            bool offering = bridge.IsOffering;
            bool standing = !move.IsMoving && !move.AtHome;

            if (offering && inHand && standing && !_offeredOnce) { _offeredOnce = true; _firstOfferAt = Time.time; }

            if (_offeredOnce)
            {
                if (inHand && standing)
                {
                    if (offering) _offeringFrames++;
                    if (bridge.OfferPoseHeld) _heldFrames++;
                    if (_prevOffering && !offering) _retracts++;
                    if (ws != null && paper != null)
                    {
                        Vector3 p = ws.InverseTransformPoint(paper.transform.position);
                        _min = Vector3.Min(_min, p); _max = Vector3.Max(_max, p);
                    }
                }
                else if (offering && move.IsMoving) _walkOffering++;
                _prevOffering = offering && inHand && standing;
                if (paper != null && paper.IsTaken) { _done = true; _endReason = "paper taken (not by the probe)"; }
                else if (Time.time - _firstOfferAt >= sampleSeconds) { _done = true; _endReason = $"{sampleSeconds:F0} s elapsed without a grab"; }
            }
            else if (Time.time - _startedAt > 90f) { _done = true; _endReason = "no paper offer within 90 s"; }
        }

        public string Status() => _done ? "done: " + _endReason : (_offeredOnce ? $"sampling, {Time.time - _firstOfferAt:F1} s into the offer" : "waiting for the offer");

        public string Report()
        {
            var sb = new StringBuilder();
            sb.AppendLine("status: " + Status());
            sb.AppendLine($"offer seen: {_offeredOnce}{(_offeredOnce ? $" at t+{_firstOfferAt - _startedAt:F1} s" : "")}");
            if (_offeredOnce)
            {
                sb.AppendLine($"frames at the hand-over spot with the paper in hand: IsOffering {_offeringFrames}, pose frozen {_heldFrames}");
                sb.AppendLine($"RETRACTS: {_retracts}   <- must be 0");
                sb.AppendLine($"frames offering while walking (arrival): {_walkOffering}");
                if (!float.IsInfinity(_min.x))
                    sb.AppendLine($"paper workspace-local range while held: x {_min.x:F3}..{_max.x:F3}, y {_min.y:F3}..{_max.y:F3}, z {_min.z:F3}..{_max.z:F3}");
                sb.AppendLine($"sampled {Time.time - _firstOfferAt:F1} s, {_frames} frames");
            }
            sb.AppendLine($"PASS: {(_offeredOnce && _retracts == 0 && _heldFrames > 0 ? "yes" : "NO")} (offer seen, zero retracts, frozen hold; paper not grabbed)");
            return sb.ToString();
        }
    }
}
