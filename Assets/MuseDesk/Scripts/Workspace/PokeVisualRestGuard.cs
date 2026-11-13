using System;
using System.Collections;
using System.Reflection;
using UnityEngine;

namespace MuseDesk.Workspace
{
    /// <summary>
    /// Guards Oculus.Interaction.PokeInteractableVisual against stale world-space offset captures
    /// (for instance when a button's parent unfolds from a smaller scale, or after fast/unusual
    /// releases): locks the button visual cleanly to its authored resting localPosition whenever no
    /// poke interactor is hovering/pressing it, and re-syncs ISDK's cached offsets when scale changes.
    /// Keeps no hard ISDK compile dependency via reflection.
    /// </summary>
    [DefaultExecutionOrder(500)]
    public class PokeVisualRestGuard : MonoBehaviour
    {
        [Tooltip("Authored resting localPosition relative to Visuals parent (captured automatically if left zero-checked).")]
        public Vector3 restLocalPosition;
        [HideInInspector] public bool hasAuthoredRest;

        Component _pokeVis;
        ICollection _interactors;
        FieldInfo _fInteractors;
        FieldInfo _fMaxOffset;
        FieldInfo _fPlanarOffset;
        FieldInfo _fBase;
        Vector3 _capturedLossyScale;
        bool _resolved;

        void Awake()
        {
            if (!hasAuthoredRest)
            {
                restLocalPosition = transform.localPosition;
                hasAuthoredRest = true;
            }
            _capturedLossyScale = transform.lossyScale;
        }

        void OnEnable()
        {
            if (hasAuthoredRest && (! _resolved || !IsAnyInteractorActive()))
                transform.localPosition = restLocalPosition;
        }

        void Start() => TryResolve();

        void TryResolve()
        {
            if (_resolved) return;
            _pokeVis = GetComponent("PokeInteractableVisual");
            if (_pokeVis == null) { _resolved = true; return; }
            var t = _pokeVis.GetType();
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
            _fInteractors = t.GetField("_pokeInteractors", flags);
            _fMaxOffset = t.GetField("_maxOffsetAlongNormal", flags);
            _fPlanarOffset = t.GetField("_planarOffset", flags);
            _fBase = t.GetField("_buttonBaseTransform", flags);
            _resolved = true;
            SyncToCurrentScale();
        }

        public void SyncToCurrentScale()
        {
            if (!_resolved) TryResolve();
            if (hasAuthoredRest && !IsAnyInteractorActive())
                transform.localPosition = restLocalPosition;
            _capturedLossyScale = transform.lossyScale;
            if (_pokeVis == null || _fBase == null) return;
            var baseTf = _fBase.GetValue(_pokeVis) as Transform;
            if (baseTf == null) return;

            float maxOff = Vector3.Dot(transform.position - baseTf.position, -1f * baseTf.forward);
            Vector3 onPlane = transform.position - maxOff * baseTf.forward;
            var planar = new Vector2(
                Vector3.Dot(onPlane - baseTf.position, baseTf.right),
                Vector3.Dot(onPlane - baseTf.position, baseTf.up));
            try
            {
                _fMaxOffset?.SetValue(_pokeVis, maxOff);
                _fPlanarOffset?.SetValue(_pokeVis, planar);
            }
            catch { /* non-fatal */ }
        }

        // "Is a finger on this key?" is asked of the PokeInteractable itself (Hover or Select).
        // The previous version cast PokeInteractableVisual's private HashSet<PokeInteractor> to the
        // non-generic ICollection, which HashSet does not implement: the cast was always null, this
        // always returned false, and LateUpdate snapped the cap back to rest every frame, so no key
        // ever visibly pressed. If the interactable cannot be found we report "active", which leaves
        // the SDK in charge (fail open: a key that presses matters more than the rest guard).
        Oculus.Interaction.IInteractableView _interactable;
        bool _interactableResolved;

        bool IsAnyInteractorActive()
        {
            if (!_interactableResolved)
            {
                _interactableResolved = true;
                var poke = GetComponentInParent<Oculus.Interaction.PokeInteractable>(true);
                _interactable = poke;
            }
            if (_interactable == null) return true;
            var s = _interactable.State;
            return s == Oculus.Interaction.InteractableState.Hover || s == Oculus.Interaction.InteractableState.Select;
        }

        void LateUpdate()
        {
            if (!_resolved) TryResolve();
            if ((transform.lossyScale - _capturedLossyScale).sqrMagnitude > 1e-6f)
                SyncToCurrentScale();

            if (!IsAnyInteractorActive() && hasAuthoredRest)
            {
                if ((transform.localPosition - restLocalPosition).sqrMagnitude > 1e-8f)
                    transform.localPosition = restLocalPosition;
            }
        }
    }
}

