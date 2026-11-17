using UnityEngine;
using MuseDesk.Agents;
using MuseDesk.Workspace;

namespace MuseDesk.Spatial
{
    /// <summary>
    /// Owner rc13: "when the agent hands out a paper, aren't they supposed to walk out to the end dock of the ceiling
    /// thing?" While the ceiling pod is lowered beside the user (Engaged, not remote) the hand-over spot is the pod's
    /// front shelf (the model's AgentInteractionSocket, a little inset), not the front of the desks: the Researcher walks
    /// out onto the shelf and holds the paper there. Everywhere else the registry's own anchor is used, unchanged.
    ///
    /// Presentation only. It swaps <see cref="AgentRegistry.deliveryAnchor"/> between the original transform and a shelf
    /// marker it owns (a child of the office root, so it travels with the pod), and when the spot changes under a paper
    /// that is already out it asks the registry to walk the Researcher to the new spot (<see cref="AgentRegistry.Redeliver"/>).
    /// The decision flow, the paper and the courier (remote modes) are not touched. Added at runtime by
    /// <see cref="CeilingPodArt"/>; no scene change.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CeilingShelfHandOver : MonoBehaviour
    {
        public CeilingPodArt art;

        AgentRegistry _registry;
        Transform _original, _shelf, _socket;
        bool _atShelf;
        float _nextLook;

        /// <summary>For drivers: true while hand-overs go to the shelf.</summary>
        public bool AtShelf => _atShelf;
        public Transform ShelfAnchor => _shelf;

        void Update()
        {
            if (art == null || !art.handOverAtShelf) { Restore(); return; }
            if (_registry == null)
            {
                if (Time.unscaledTime < _nextLook) return;
                _nextLook = Time.unscaledTime + 1f;
                _registry = FindAnyObjectByType<AgentRegistry>(FindObjectsInactive.Include);
                if (_registry == null) return;
                _original = _registry.deliveryAnchor;
            }
            if (_socket == null && art.podVisual != null)
                foreach (var t in art.podVisual.GetComponentsInChildren<Transform>(true))
                    if (t.name == "AgentInteractionSocket") { _socket = t; break; }

            Transform ws = SpatialWorkspaceState.WorkspaceRoot;
            bool want = _socket != null && ws != null && _original != null
                        && SpatialWorkspaceState.Mode == WorkspaceMode.Ceiling && !SpatialWorkspaceState.IsRemote
                        && !SpatialWorkspaceState.IsTransitioning
                        && art.pod != null && art.pod.State == CeilingPodState.Engaged;
            if (want) PlaceShelf(ws);
            if (want == _atShelf) return;
            Transform from = _registry.deliveryAnchor;
            _atShelf = want;
            _registry.deliveryAnchor = want ? _shelf : _original;
            // Only toward the shelf: when the pod goes back up the paper is the courier's (remote) and the walk home
            // keeps the lane it came by.
            if (want) _registry.Redeliver(from);
        }

        void PlaceShelf(Transform ws)
        {
            if (_shelf == null)
            {
                _shelf = new GameObject("PodShelfDeliveryZone").transform;
                _shelf.SetParent(ws, false);
            }
            else if (_shelf.parent != ws) _shelf.SetParent(ws, false);
            Vector3 local = ws.InverseTransformPoint(_socket.position);
            Vector3 o = _original.parent == ws ? _original.localPosition : ws.InverseTransformPoint(_original.position);
            _shelf.localPosition = new Vector3(local.x, o.y, local.z - shelfInsetScaled(ws));
            _shelf.localRotation = _original.parent == ws ? _original.localRotation : Quaternion.Inverse(ws.rotation) * _original.rotation;
        }

        float shelfInsetScaled(Transform ws) { return art != null ? art.shelfInset : 0.045f; }

        void Restore()
        {
            if (!_atShelf || _registry == null) return;
            _atShelf = false;
            _registry.deliveryAnchor = _original;
        }

        void OnDisable() { Restore(); }
    }
}
