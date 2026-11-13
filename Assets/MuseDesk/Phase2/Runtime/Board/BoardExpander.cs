using System;
using System.Collections.Generic;
using Oculus.Interaction;
using UnityEngine;
using MuseDesk.Workspace;

namespace MuseDesk.Phase2.Board
{
    /// <summary>
    /// Opens and closes the full task board. The board starts collapsed (scaled to near zero, so the
    /// agents and their office are what the user sees) and opens when the user presses the Board key
    /// on the task dock. It never opens by itself.
    ///
    /// With an inspect pose (wired by AgentScripts/BuildBoardV3) the board grows out of the task
    /// dock: collapsed it sits just behind the dock at near-zero scale, and it travels to a pose
    /// nearer the user, smaller than its authored size and turned to the seated eye, so every board
    /// key is within comfortable reach. Without an inspect pose it scales in place, as before.
    ///
    /// The board is never deactivated: Interaction SDK interactables that are inactive at start-up do
    /// not register, so a board shown later would have dead keys and cards. While collapsed, the
    /// interactables under the board (poke keys, card grabs, distance grabs) are switched off through
    /// their <c>enabled</c> flag, the SDK's supported way to take an interactable out of play, so
    /// nothing invisible can be poked or grabbed from a distance. They are switched back on the moment
    /// the board starts to open.
    ///
    /// The board carries poke keys, so it is not billboarded while it can be pressed (a key that
    /// turns under the finger changes its press axis mid-press). The FaceUser on the board is switched
    /// off here; the board is oriented once, when it opens.
    /// </summary>
    [DisallowMultipleComponent]
    public class BoardExpander : MonoBehaviour
    {
        [Tooltip("Seconds for the open / close animation.")]
        public float duration = 0.25f;
        [Tooltip("Scale factor while collapsed. Near zero, never zero.")]
        public float collapsedFactor = 0.001f;
        [Tooltip("Open when the scene starts. The redesign wants it closed until the user asks.")]
        public bool startExpanded;
        [Tooltip("Switch off poke / grab interactables under the board while it is collapsed. " +
                 "Turn this off if cards or board keys stop responding after the board opens.")]
        public bool disableInteractablesWhileCollapsed = true;

        [Header("Inspect pose (wired by AgentScripts/BuildBoardV3)")]
        [Tooltip("Use the pose below when open, and travel to it from the collapsed anchor. " +
                 "Off: the board scales in place at whatever pose the scene gives it.")]
        public bool useInspectPose;
        [Tooltip("Local position of the open board (in its parent's space).")]
        public Vector3 expandedLocalPosition;
        [Tooltip("Local rotation (Euler degrees) of the open board.")]
        public Vector3 expandedLocalEuler;
        [Tooltip("Local scale of the open board.")]
        public Vector3 expandedScale = Vector3.one;
        [Tooltip("Builder bookkeeping: the board's local scale before the inspect scale-down was " +
                 "applied. Zero means not recorded yet.")]
        public Vector3 authoredBase;
        [Tooltip("Where the collapsed board sits (the task dock), so it appears to grow out of it.")]
        public Transform collapsedAnchor;
        [Tooltip("Offset from the anchor, in the anchor's local space (just behind the dock plate, " +
                 "so the collapsed board's tiny colliders are clear of the dock keys).")]
        public Vector3 collapsedAnchorOffset = new Vector3(0f, 0f, 0.012f);
        [Tooltip("A billboard on the board (FaceUser). Switched off: a board with poke keys is " +
                 "oriented once when it opens, never every frame.")]
        public Behaviour billboard;
        [Tooltip("Turn the board (yaw only) toward the user's head once, when it opens, instead of " +
                 "using the authored rotation. Limited to maxAimDegrees from the authored rotation.")]
        public bool aimAtHeadOnOpen;
        [Tooltip("Largest turn away from the authored rotation that aimAtHeadOnOpen may make.")]
        public float maxAimDegrees = 25f;

        /// <summary>True when the board is open or opening.</summary>
        public bool IsExpanded { get; private set; }
        /// <summary>True while the open / close animation is running.</summary>
        public bool IsAnimating => _animating;

        /// <summary>Raised when the board starts to open (true) or close (false).</summary>
        public event Action<bool> Changed;

        Vector3 _authored = Vector3.one;
        Vector3 _expandedPos;
        Quaternion _expandedRot = Quaternion.identity;
        Quaternion _openRot = Quaternion.identity;
        Vector3 _collapsedPos;
        float _t;                       // 0 = collapsed, 1 = expanded
        bool _init;
        bool _animating;
        float _nextEnforce;
        int _enforceFromFrame;

        readonly List<MonoBehaviour> _scan = new List<MonoBehaviour>(96);
        readonly List<Behaviour> _switchedOff = new List<Behaviour>(48);
        readonly List<PokeVisualRestGuard> _guards = new List<PokeVisualRestGuard>(8);

        void Awake() => EnsureInit();

        // Re-activation (the workspace is hidden during placement): give re-registering
        // interactables a few frames before taking them out of play again.
        void OnEnable() => _enforceFromFrame = Time.frameCount + 3;

        void EnsureInit()
        {
            if (_init) return;
            _init = true;
            if (useInspectPose)
            {
                _expandedPos = expandedLocalPosition;
                _expandedRot = Quaternion.Euler(expandedLocalEuler);
                _authored = expandedScale.sqrMagnitude > 1e-6f ? expandedScale : transform.localScale;
                // Oriented once on opening; never billboarded while its keys can be pressed.
                if (billboard != null) billboard.enabled = false;
            }
            else
            {
                _expandedPos = transform.localPosition;
                _expandedRot = transform.localRotation;
                _authored = transform.localScale;
            }
            _openRot = _expandedRot;
            _collapsedPos = ResolveCollapsedPosition();
            GetComponentsInChildren(true, _guards);
            IsExpanded = startExpanded;
            _t = startExpanded ? 1f : 0f;
            Apply();
            // Interactables are left alone for the first frames so each one runs its own Start.
            _enforceFromFrame = Time.frameCount + 3;
        }

        /// <summary>Where the collapsed board sits, in the board's parent space.</summary>
        Vector3 ResolveCollapsedPosition()
        {
            if (!useInspectPose || collapsedAnchor == null) return _expandedPos;
            Vector3 world = collapsedAnchor.TransformPoint(collapsedAnchorOffset);
            Transform parent = transform.parent;
            return parent != null ? parent.InverseTransformPoint(world) : world;
        }

        /// <summary>The rotation the board opens at: the authored one, or (optionally) a yaw-only
        /// turn toward the head, taken once.</summary>
        Quaternion ResolveOpenRotation()
        {
            if (!useInspectPose || !aimAtHeadOnOpen) return _expandedRot;
            var cam = Camera.main;                                     // once per opening, not per frame
            if (cam == null) return _expandedRot;
            Transform parent = transform.parent;
            Vector3 world = parent != null ? parent.TransformPoint(_expandedPos) : _expandedPos;
            Vector3 away = world - cam.transform.position;
            away.y = 0f;
            if (away.sqrMagnitude < 1e-4f) return _expandedRot;
            Quaternion look = Quaternion.LookRotation(away.normalized, Vector3.up);
            Quaternion local = parent != null ? Quaternion.Inverse(parent.rotation) * look : look;
            if (Quaternion.Angle(local, _expandedRot) > maxAimDegrees)
                local = Quaternion.RotateTowards(_expandedRot, local, maxAimDegrees);
            return local;
        }

        public void Expand() => SetExpanded(true);
        public void Collapse() => SetExpanded(false);
        /// <summary>Poke target: open the board if it is closed, close it if it is open.</summary>
        public void Toggle() => SetExpanded(!IsExpanded);

        public void SetExpanded(bool expanded)
        {
            EnsureInit();
            if (expanded == IsExpanded) return;
            IsExpanded = expanded;
            if (expanded)
            {
                RestoreInteractables();
                _collapsedPos = ResolveCollapsedPosition();
                _openRot = ResolveOpenRotation();
            }
            else { _enforceFromFrame = Time.frameCount + 1; _nextEnforce = 0f; }

            if (duration <= 0f || !isActiveAndEnabled)
            {
                _t = expanded ? 1f : 0f;
                Apply();
                SyncGuards();
                _animating = false;
            }
            else _animating = true;

            var handler = Changed;
            if (handler != null) handler(expanded);
        }

        void Update()
        {
            if (_animating)
            {
                float target = IsExpanded ? 1f : 0f;
                _t = Mathf.MoveTowards(_t, target, Time.unscaledDeltaTime / duration);
                Apply();
                if (Mathf.Approximately(_t, target))
                {
                    _animating = false;
                    SyncGuards();
                }
                return;
            }

            // Collapsed and at rest: keep everything under the board out of play. Repeated on a slow
            // timer because cards are pooled and re-created on a snapshot reload or a demo replay.
            if (IsExpanded || !disableInteractablesWhileCollapsed) return;
            if (Time.frameCount < _enforceFromFrame || Time.unscaledTime < _nextEnforce) return;
            _nextEnforce = Time.unscaledTime + 0.5f;
            SwitchOffInteractables();
        }

        void Apply()
        {
            float inv = 1f - _t;
            float eased = 1f - inv * inv * inv;                        // ease-out cubic
            transform.localScale = _authored * Mathf.Lerp(collapsedFactor, 1f, eased);
            if (!useInspectPose) return;
            transform.localPosition = Vector3.LerpUnclamped(_collapsedPos, _expandedPos, eased);
            transform.localRotation = _openRot;
        }

        void SwitchOffInteractables()
        {
            GetComponentsInChildren(false, _scan);                     // fills the cached list: no garbage
            for (int i = 0; i < _scan.Count; i++)
            {
                var mb = _scan[i];
                if (mb == null || !mb.enabled || !(mb is IInteractable)) continue;
                mb.enabled = false;
                if (!_switchedOff.Contains(mb)) _switchedOff.Add(mb);
            }
            _scan.Clear();
        }

        void RestoreInteractables()
        {
            for (int i = 0; i < _switchedOff.Count; i++)
                if (_switchedOff[i] != null) _switchedOff[i].enabled = true;
            _switchedOff.Clear();
        }

        void SyncGuards()
        {
            for (int i = 0; i < _guards.Count; i++)
                if (_guards[i] != null) _guards[i].SyncToCurrentScale();
        }

        void OnDisable()
        {
            // Never leave interactables switched off behind a component that can no longer restore them.
            RestoreInteractables();
            if (_animating)
            {
                _animating = false;
                _t = IsExpanded ? 1f : 0f;
                Apply();
            }
        }
    }
}
