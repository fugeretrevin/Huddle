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
        }
    }
}
