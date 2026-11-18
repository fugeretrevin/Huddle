using UnityEngine;
using MuseDesk.Workspace;

namespace MuseDesk.Phase2.Board
{
    /// <summary>
    /// Shows and hides an object by scaling it between its authored local scale and near zero, with
    /// a short eased animation. Used instead of SetActive because Interaction SDK interactables that
    /// are inactive at start-up never register: a hidden key stays active, just too small to see or
    /// touch.
    ///
    /// The authored scale is whatever the transform has when this component wakes, so a builder can
    /// re-pose the object freely. The component disables itself when the animation ends, so it costs
    /// nothing while idle. Presentation only: it changes no task state.
    /// </summary>
    [DisallowMultipleComponent]
    public class ScaleReveal : MonoBehaviour
    {
        [Tooltip("Seconds for a full show or hide.")]
        public float duration = 0.18f;
        [Tooltip("Scale factor while hidden. Near zero, never zero (a zero scale breaks poke surfaces).")]
        public float hiddenFactor = 0.001f;
        [Tooltip("Shown when the scene starts. Contextual controls start hidden.")]
        public bool startShown;

        /// <summary>True when the object is shown or on its way to being shown.</summary>
        public bool Shown { get; private set; }

        Vector3 _authored = Vector3.one;
        float _t;                 // 0 = hidden, 1 = shown
        bool _init;
        PokeVisualRestGuard[] _guards;

        void Awake() => EnsureInit();

        void EnsureInit()
        {
            if (_init) return;
            _init = true;
            _authored = transform.localScale;
            // Key caps cache world-space press offsets; the guard re-reads them after a scale change.
            _guards = GetComponentsInChildren<PokeVisualRestGuard>(true);
            Shown = startShown;
            _t = startShown ? 1f : 0f;
            Apply();
            enabled = false;
        }

        /// <summary>The scale the object has when shown (what it had when this component woke).</summary>
        public Vector3 AuthoredScale { get { EnsureInit(); return _authored; } }

        public void Show() => Set(true);
        public void Hide() => Set(false);

        /// <summary>Show or hide. Safe to call every frame: it does nothing when already there.</summary>
        public void Set(bool shown)
        {
            EnsureInit();
            if (shown == Shown) return;
            Shown = shown;
            if (duration <= 0f || !gameObject.activeInHierarchy)
            {
                _t = shown ? 1f : 0f;
                Apply();
                SyncGuards();
                return;
            }
            enabled = true;
        }

        /// <summary>Jump to the shown or hidden pose without animating.</summary>
        public void SetImmediate(bool shown)
        {
            EnsureInit();
            Shown = shown;
            _t = shown ? 1f : 0f;
            Apply();
            SyncGuards();
            enabled = false;
        }

        // The animation runs in Update, which needs an active object; Set applies the pose at once
        // when the object is inactive.
        void Update()
        {
            float target = Shown ? 1f : 0f;
            float step = duration > 0f ? Time.unscaledDeltaTime / duration : 1f;
            _t = Mathf.MoveTowards(_t, target, step);
            Apply();
            if (Mathf.Approximately(_t, target))
            {
                SyncGuards();
                enabled = false;
            }
        }

        void Apply()
        {
            float eased = _t * _t * (3f - 2f * _t);                    // smoothstep
            float f = Mathf.Lerp(hiddenFactor, 1f, eased);
            transform.localScale = _authored * f;
        }

        void SyncGuards()
        {
            if (_guards == null) return;
            for (int i = 0; i < _guards.Length; i++)
                if (_guards[i] != null) _guards[i].SyncToCurrentScale();
        }
    }
}
