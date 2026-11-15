using System.Collections.Generic;
using UnityEngine;
using MuseDesk.Phase2.Contracts;

namespace MuseDesk.Phase2.Board
{
    /// <summary>
    /// A workstation drop target (P2-01). Dropping an eligible card onto a zone assigns that task to
    /// the zone's agent. The zone is a trigger volume plus a visible target halo; a deterministic
    /// nearest-valid-zone resolver (ResolveNearestValid) picks the target from the card's position so
    /// behavior is predictable with overlapping volumes.
    ///
    /// The zone does NOT issue the command and does NOT mutate task state — it only reports
    /// compatibility and shows a halo. The actual assignment goes through TaskDragController →
    /// BoardPresenter → gateway, and only canonical server/ demo-authoritative state moves the card.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class StationDropZone : MonoBehaviour
    {
        [Tooltip("Target agent this station assigns to: researcher | developer | tester.")]
        public string agentId = Phase2AgentIds.Researcher;

        [Tooltip("Where an assigned card snaps to rest at this station (optional; falls back to this transform).")]
        public Transform dropAnchor;

        [Tooltip("Halo shown while a held card hovers and this zone is the valid candidate (optional).")]
        public GameObject halo;

        [Tooltip("Tint the halo when the hovered card is a VALID target here.")]
        public Color validColor = WithAlpha(MuseDesk.Workspace.OfficeTokens.Success, 0.24f);
        [Tooltip("Tint the halo when the hovered card is INVALID here (e.g. running/terminal).")]
        public Color invalidColor = WithAlpha(MuseDesk.Workspace.OfficeTokens.Coral, 0.26f);

        static Color WithAlpha(Color c, float a) { c.a = a; return c; }

        [Tooltip("Optional: the pad's own plate. It grows a little while a held card would be accepted " +
                 "here, so the valid target is shown by shape as well as by colour.")]
        public Transform highlightPlate;
        [Tooltip("Scale of highlightPlate while this zone is the valid target (1 = no change).")]
        public float validPlateScale = 1.08f;

        Collider _collider;
        Renderer _haloRenderer;
        MaterialPropertyBlock _block;
        bool _wasOn, _wasValid = true;
        Vector3 _plateBase;
        bool _plateBaseRead;
        bool _plateGrown;

        /// <summary>Raised when the halo turns on/off or changes validity (zone, on, valid).</summary>
        public static event System.Action<StationDropZone, bool, bool> HighlightChanged;

        /// <summary>All enabled zones, for the nearest-valid resolver. Registered in OnEnable.</summary>
        public static readonly List<StationDropZone> Active = new List<StationDropZone>();

        public Vector3 Center => _collider != null ? _collider.bounds.center : transform.position;
        public Transform RestAnchor => dropAnchor != null ? dropAnchor : transform;

        void Awake()
        {
            _collider = GetComponent<Collider>();
            if (_collider != null) _collider.isTrigger = true;
            if (halo != null)
            {
                _haloRenderer = halo.GetComponent<Renderer>();
                halo.SetActive(false);
            }
        }

        void OnEnable() { if (!Active.Contains(this)) Active.Add(this); }
        void OnDisable() { Active.Remove(this); SetHighlight(false, valid: true); }

        /// <summary>
        /// Local eligibility check (the server is still authoritative and re-checks). A card is a valid
        /// target here when it is a safe reassignment checkpoint and not running/terminal. We do not
        /// know server-side role_lock, so role incompatibility is surfaced by the backend's 409
        /// ROLE_INCOMPATIBLE, which the drag controller reconciles. Dropping onto the card's current
        /// agent is treated as a no-op (not a fresh command).
        /// </summary>
        public bool IsCompatible(TaskCardV2 card)
        {
            if (card == null) return false;
            if (TaskStateSets.IsTerminal(card.TaskState)) return false;
            if (TaskStateSets.IsRunning(card.TaskState)) return false;
            if (!TaskStateSets.IsSafeAssignCheckpoint(card.TaskState)) return false;
            // A runnable card's pipeline starts with the Researcher, so only that zone is a valid
            // target (the others show the red halo). The backend enforces the same rule.
            if (card.Executable && agentId != Phase2AgentIds.Researcher) return false;
            return true;
        }

        /// <summary>Would a drop here actually change the assignee? False if already assigned to this agent.</summary>
        public bool WouldReassign(TaskCardV2 card) => card != null && card.AgentId != agentId;

        public bool Contains(Vector3 worldPoint)
            => _collider != null && _collider.bounds.Contains(worldPoint);

        public void SetHighlight(bool on, bool valid)
        {
            // The plate grows only for a drop that would be accepted (state by shape, not colour alone).
            bool grow = on && valid;
            if (highlightPlate != null && grow != _plateGrown)
            {
                if (!_plateBaseRead) { _plateBaseRead = true; _plateBase = highlightPlate.localScale; }
                _plateGrown = grow;
                highlightPlate.localScale = grow
                    ? new Vector3(_plateBase.x * validPlateScale, _plateBase.y * validPlateScale, _plateBase.z)
                    : _plateBase;
            }
            if (halo == null) return;
            halo.SetActive(on);
            if (on && _haloRenderer != null)
            {
                // Property block (no per-zone material instance). Green = a drop here assigns;
                // red = it would be refused. The card's own status line states the reason in words.
                if (_block == null) _block = new MaterialPropertyBlock();
                Color c = valid ? validColor : invalidColor;
                _haloRenderer.GetPropertyBlock(_block);
                _block.SetColor("_BaseColor", c);
                _block.SetColor("_Color", c);
                _haloRenderer.SetPropertyBlock(_block);
            }
            if (on != _wasOn || valid != _wasValid)
            {
                _wasOn = on; _wasValid = valid;
                HighlightChanged?.Invoke(this, on, valid);
            }
        }

        /// <summary>
        /// Deterministic nearest-valid-zone resolver. Returns the closest zone whose bounds contain the
        /// point (or are within <paramref name="snapRadius"/>) and for which the card is compatible.
        /// Ties broken by smallest center distance, then by instance id for stability.
        /// </summary>
        public static StationDropZone ResolveNearestValid(Vector3 point, TaskCardV2 card, float snapRadius)
        {
            StationDropZone best = null;
            float bestDist = float.MaxValue;
            foreach (var z in Active)
            {
                if (z == null || !z.isActiveAndEnabled) continue;
                bool inside = z.Contains(point);
                float d = Vector3.Distance(point, z.Center);
                if (!inside && d > snapRadius) continue;
                if (!z.IsCompatible(card)) continue;
                // Prefer inside-bounds zones; among equals, nearest center then stable id.
                float score = inside ? d : d + snapRadius;   // inside ranks ahead of merely-near
                if (score < bestDist - 1e-4f ||
                    (Mathf.Abs(score - bestDist) <= 1e-4f && (best == null || z.GetHashCode() < best.GetHashCode())))
                {
                    bestDist = score;
                    best = z;
                }
            }
            return best;
        }

        /// <summary>Nearest zone of ANY compatibility (to show a red halo when hovering an invalid target).</summary>
        public static StationDropZone ResolveNearestAny(Vector3 point, float snapRadius)
        {
            StationDropZone best = null;
            float bestDist = float.MaxValue;
            foreach (var z in Active)
            {
                if (z == null || !z.isActiveAndEnabled) continue;
                bool inside = z.Contains(point);
                float d = Vector3.Distance(point, z.Center);
                if (!inside && d > snapRadius) continue;
                if (d < bestDist) { bestDist = d; best = z; }
            }
            return best;
        }
    }
}
