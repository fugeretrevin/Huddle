using System;
using System.Collections.Generic;
using Oculus.Interaction;
using UnityEngine;
using MuseDesk.Workspace;

namespace MuseDesk.Phase2.Board
{
    /// <summary>
    /// Opens and closes the full task board. The board starts collapsed (scaled to near zero, so the
    /// agents and their office are what the user sees) and grows to its authored scale when the user
    /// presses the Board key on the task dock. It never opens by itself.
    ///
    /// The board is never deactivated: Interaction SDK interactables that are inactive at start-up do
    /// not register, so a board shown later would have dead keys and cards. While collapsed, the
    /// interactables under the board (poke keys, card grabs, distance grabs) are switched off through
    /// their <c>enabled</c> flag, the SDK's supported way to take an interactable out of play, so
    /// nothing invisible can be poked or grabbed from a distance. They are switched back on the moment
    /// the board starts to open.
    ///
    /// Only the board's own local scale is animated. FaceUser (rotation), the presenter (card parents
    /// and local positions) and PokeVisualRestGuard (key cap rest pose) do not touch it; the guard is
    /// asked to re-read its offsets when the animation ends.
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

        /// <summary>True when the board is open or opening.</summary>
        public bool IsExpanded { get; private set; }
        /// <summary>True while the open / close animation is running.</summary>
        public bool IsAnimating => _animating;

        /// <summary>Raised when the board starts to open (true) or close (false).</summary>
        public event Action<bool> Changed;

        Vector3 _authored = Vector3.one;
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
            _authored = transform.localScale;
            GetComponentsInChildren(true, _guards);
            IsExpanded = startExpanded;
            _t = startExpanded ? 1f : 0f;
            Apply();
            // Interactables are left alone for the first frames so each one runs its own Start.
            _enforceFromFrame = Time.frameCount + 3;
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
            if (expanded) RestoreInteractables();
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
            float eased = _t * _t * (3f - 2f * _t);                    // smoothstep
            transform.localScale = _authored * Mathf.Lerp(collapsedFactor, 1f, eased);
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
