using UnityEngine;
using MuseDesk.Workspace;

namespace MuseDesk.Spatial
{
    /// <summary>
    /// The placement preview: a flat teal outline of the office footprint with an arrow showing
    /// which way the office will face (toward the user), drawn where the office would go. It is
    /// only a preview: the office itself does not move until the user confirms. The outline turns
    /// coral when a hand-adjusted spot fails the room checks (the status text says why in words).
    /// Lives outside WorkspaceRoot, holds no interactables, and costs nothing while hidden.
    /// </summary>
    public class WorkspaceGhostPreview : MonoBehaviour
    {
        [Tooltip("Parent of the outline quads, authored in root-local layout at office scale 1.")]
        public GameObject visual;
        [Tooltip("Renderers tinted teal (valid) or coral (not clear floor).")]
        public Renderer[] renderers;
        [Tooltip("Raise the outline this far above the surface so it does not z-fight the floor.")]
        public float lift = 0.004f;

        MaterialPropertyBlock _block;
        bool _shown;
        bool _tinted;
        bool _lastValid;

        public bool Shown => _shown;

        void Awake()
        {
            if (visual != null) visual.SetActive(false);
        }

        /// <summary>Show the outline at a candidate root pose.</summary>
        public void Show(RootPose pose, bool valid)
        {
            transform.SetPositionAndRotation(pose.position + Vector3.up * lift, pose.rotation);
            transform.localScale = new Vector3(pose.scale, pose.scale, pose.scale);
            if (!_shown)
            {
                _shown = true;
                if (visual != null) visual.SetActive(true);
            }
            if (!_tinted || valid != _lastValid)
            {
                _tinted = true;
                _lastValid = valid;
                if (_block == null) _block = new MaterialPropertyBlock();
                Color c = valid ? OfficeTokens.Teal : OfficeTokens.Coral;
                if (renderers != null)
                    for (int i = 0; i < renderers.Length; i++)
                        OfficeTokens.Tint(renderers[i], _block, c);
            }
        }

        public void Hide()
        {
            if (!_shown) return;
            _shown = false;
            if (visual != null) visual.SetActive(false);
        }
    }
}
