using System.Collections.Generic;
using UnityEngine;

namespace MuseDesk.Spatial
{
    /// <summary>
    /// Keeps the user's own controls where they are when the office goes away.
    ///
    /// Two kinds of things stay with the user, both through one rig (Spatial/NearUserRig) that
    /// stands at the office's DESK pose:
    ///
    ///  1. MOVABLE PANELS (<see cref="MovablePanel"/>: the task dock with its board, the mode plaque,
    ///     the mode console with the Focus key, the completion receipt) live under the rig
    ///     PERMANENTLY. While the office is at the desk and at rest this component copies the office
    ///     root's pose, scale and active state onto the rig every frame, so a panel the user never
    ///     moved is exactly where it was as a child of the root (a panel holder has identity local
    ///     pose), and it follows a new desk placement or a recenter. When the office leaves for the
    ///     floor or the ceiling the rig simply stops following: nothing is re-parented, so a panel
    ///     keeps whatever pose the user gave it, and its saved pose stays relative to the desk.
    ///
    ///  2. LOOSE ITEMS (the stamps and their pad, the artifact viewer) are still children of the
    ///     office root, because their own scripts reference it. As before, they are moved under the
    ///     rig when the office leaves Desk mode, keeping every local position unchanged, and moved
    ///     back when the office is at the desk again and at rest. The stamps are additionally shown
    ///     only while a decision waits (<see cref="decisionOnly"/>), by scale, exactly as before.
    ///
    /// Presentation only. It never touches session or task state.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public class NearUserKit : MonoBehaviour
    {
        [Tooltip("Loose objects that stay with the user while the office is away. Direct or indirect children of the office root. Movable panels are NOT listed here: they live under the rig.")]
        public Transform[] items;
        [Tooltip("Rig that stands at the office's desk pose. Not a child of the office root. Movable panels are its children.")]
        public Transform rig;
        [Tooltip("The office root, used until the director has published it. Optional.")]
        public Transform workspaceRoot;
        [Tooltip("Keep the rig on the office root while the office is at the desk and at rest (needed for movable panels). " +
                 "Off: the rig is only placed when the office leaves, as before the panels existed.")]
        public bool followDesk = true;

        readonly List<Transform> _homes = new List<Transform>();
        readonly List<int> _siblingIndex = new List<int>();
        bool _detached;
        bool _subscribed;
        bool _hasDeskPose;

        /// <summary>True while the items are parked at the desk and the office is elsewhere.</summary>
        public bool Detached => _detached;
        /// <summary>True once the rig has taken the office's desk pose at least once.</summary>
        public bool HasDeskPose => _hasDeskPose;

        Transform Root()
        {
            Transform root = SpatialWorkspaceState.WorkspaceRoot;
            return root != null ? root : workspaceRoot;
        }

        void OnEnable()
        {
            if (!_subscribed) { SpatialWorkspaceState.Changed += Sync; _subscribed = true; }
            FollowDesk();
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

        /// <summary>
        /// While the office is at the desk and at rest: the rig takes the root's pose, scale and
        /// active state. Otherwise the rig keeps the last desk pose and stays active.
        /// </summary>
        void FollowDesk()
        {
            if (!followDesk || rig == null) return;
            Transform root = Root();
            if (root == null) return;
            bool atDesk = SpatialWorkspaceState.Mode == WorkspaceMode.Desk && !SpatialWorkspaceState.IsTransitioning && !_detached;
            if (!atDesk)
            {
                if (!rig.gameObject.activeSelf) rig.gameObject.SetActive(true);
                return;
            }
            if (rig.position != root.position || rig.rotation != root.rotation)
                rig.SetPositionAndRotation(root.position, root.rotation);
            Vector3 scale = root.lossyScale;
            if (rig.localScale != scale) rig.localScale = scale;
            // The office is hidden until the desk is placed; its controls are hidden with it.
            bool on = root.gameObject.activeInHierarchy;
            if (rig.gameObject.activeSelf != on) rig.gameObject.SetActive(on);
            _hasDeskPose = true;
        }

        void Detach()
        {
            Transform root = Root();
            if (root == null || rig == null) return;
            // The rig stands at the desk pose. If it has been following the root it already has it
            // (and the root may have started to move this frame); otherwise take the root's pose now.
            if (!(followDesk && _hasDeskPose))
            {
                rig.SetPositionAndRotation(root.position, root.rotation);
                rig.localScale = root.lossyScale;
            }
            if (!rig.gameObject.activeSelf) rig.gameObject.SetActive(true);
            _homes.Clear(); _siblingIndex.Clear();
            if (items != null)
            {
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
            }
            _detached = true;
        }

        void Reattach()
        {
            if (items != null)
            {
                Transform root = Root();
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

        [Header("Only when needed (while the office is away)")]
        [Tooltip("Shown at the desk only while a decision is waiting (the stamps and their pad). In Desk mode they are always shown, as before.")]
        public Transform[] decisionOnly;
        [Tooltip("Seconds to grow in or shrink away.")]
        public float revealSeconds = 0.25f;

        Vector3[] _fullScale;
        float _reveal = 1f;      // 0 hidden .. 1 shown

        /// <summary>True while the decision-only items (stamps) are shown.</summary>
        public bool DecisionItemsShown => _reveal > 0.5f;

        void Update()
        {
            if (decisionOnly == null || decisionOnly.Length == 0) return;
            if (_fullScale == null || _fullScale.Length != decisionOnly.Length)
            {
                _fullScale = new Vector3[decisionOnly.Length];
                for (int i = 0; i < decisionOnly.Length; i++)
                    _fullScale[i] = decisionOnly[i] != null ? decisionOnly[i].localScale : Vector3.one;
            }
            // The user's desk stays clear while the office is away: the stamps come out only when
            // there is something to stamp. At the desk nothing changes.
            bool want = !_detached;
            if (_detached)
            {
                AttentionController a = AttentionController.Instance;
                want = a != null && a.PendingDecisionCount > 0;
            }
            float target = want ? 1f : 0f;
            if (Mathf.Approximately(_reveal, target)) return;
            _reveal = Mathf.MoveTowards(_reveal, target, Time.unscaledDeltaTime / Mathf.Max(0.01f, revealSeconds));
            float e = 1f - (1f - _reveal) * (1f - _reveal);          // ease out
            float k = Mathf.Lerp(0.001f, 1f, e);
            for (int i = 0; i < decisionOnly.Length; i++)
                if (decisionOnly[i] != null) decisionOnly[i].localScale = _fullScale[i] * k;
        }

        void LateUpdate()
        {
            // Returning to the desk ends with a state change, but if that was missed (domain reload,
            // disabled at the time) pick it up here; cheap, and only while detached.
            if (_detached && SpatialWorkspaceState.Mode == WorkspaceMode.Desk && !SpatialWorkspaceState.IsTransitioning)
                Reattach();
            FollowDesk();
        }
    }
}
