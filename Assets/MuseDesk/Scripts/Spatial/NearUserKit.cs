using System.Collections.Generic;
using UnityEngine;

namespace MuseDesk.Spatial
{
    /// <summary>
    /// Keeps the user's own controls where they are when the office goes away.
    ///
    /// The task dock, stamps, mode plaque, receipt, Focus key, task board and artifact viewer are
    /// children of the office root, so in Floor or Ceiling mode they would travel 1 to 2 m away with
    /// it and the seated user would have nothing to press. When the office leaves Desk mode this
    /// component moves those objects under a rig that stays at the office's desk pose, keeping every
    /// local position unchanged (so stamp rest poses, the board's open pose and layout code keep
    /// working), and moves them back when the office is at the desk again and at rest.
    ///
    /// Presentation only. It never touches session or task state.
    /// </summary>
    public class NearUserKit : MonoBehaviour
    {
        [Tooltip("Objects that stay with the user while the office is away. Direct or indirect children of the office root.")]
        public Transform[] items;
        [Tooltip("Rig that holds the items while the office is away. Not a child of the office root.")]
        public Transform rig;

        readonly List<Transform> _homes = new List<Transform>();
        readonly List<int> _siblingIndex = new List<int>();
        bool _detached;
        bool _subscribed;

        /// <summary>True while the items are parked at the desk and the office is elsewhere.</summary>
        public bool Detached => _detached;

        void OnEnable()
        {
            if (!_subscribed) { SpatialWorkspaceState.Changed += Sync; _subscribed = true; }
            Sync();
        }

        void OnDisable()
        {
            if (_subscribed) { SpatialWorkspaceState.Changed -= Sync; _subscribed = false; }
            // Never leave the controls stranded under a rig nobody manages.
            if (_detached) Reattach();
        }

        void Sync()
        {
            bool away = SpatialWorkspaceState.Mode != WorkspaceMode.Desk;
            if (away && !_detached) Detach();
            else if (!away && _detached && !SpatialWorkspaceState.IsTransitioning) Reattach();
        }

        void Detach()
        {
            Transform root = SpatialWorkspaceState.WorkspaceRoot;
            if (root == null || rig == null || items == null) return;
            // The rig takes the office's current pose and scale, so re-parenting changes no local value.
            rig.SetPositionAndRotation(root.position, root.rotation);
            rig.localScale = root.lossyScale;
            _homes.Clear(); _siblingIndex.Clear();
            for (int i = 0; i < items.Length; i++)
            {
                Transform t = items[i];
                _homes.Add(t != null ? t.parent : null);
                _siblingIndex.Add(t != null ? t.GetSiblingIndex() : 0);
                if (t == null) continue;
                Vector3 lp = t.localPosition; Quaternion lr = t.localRotation; Vector3 ls = t.localScale;
                // Only items directly under the root keep identical locals; deeper ones keep world pose.
                if (t.parent == root) { t.SetParent(rig, false); t.localPosition = lp; t.localRotation = lr; t.localScale = ls; }
                else t.SetParent(rig, true);
            }
            _detached = true;
        }

        void Reattach()
        {
            if (items != null)
            {
                Transform root = SpatialWorkspaceState.WorkspaceRoot;
                for (int i = 0; i < items.Length && i < _homes.Count; i++)
                {
                    Transform t = items[i]; Transform home = _homes[i];
                    if (t == null || home == null) continue;
                    Vector3 lp = t.localPosition; Quaternion lr = t.localRotation; Vector3 ls = t.localScale;
                    if (home == root) { t.SetParent(home, false); t.localPosition = lp; t.localRotation = lr; t.localScale = ls; }
                    else t.SetParent(home, true);
                    t.SetSiblingIndex(Mathf.Min(_siblingIndex[i], home.childCount - 1));
                }
            }
            _homes.Clear(); _siblingIndex.Clear();
            _detached = false;
        }

        void LateUpdate()
        {
            // Returning to the desk ends with a state change, but if that was missed (domain reload,
            // disabled at the time) pick it up here; cheap, and only while detached.
            if (_detached && SpatialWorkspaceState.Mode == WorkspaceMode.Desk && !SpatialWorkspaceState.IsTransitioning)
                Reattach();
        }
    }
}
