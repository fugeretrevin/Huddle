using UnityEngine;
using MuseDesk.Workspace;

namespace MuseDesk.Spatial
{
    /// <summary>
    /// The small "Reset layout" key that appears above a panel's grab bar after the bar has been
    /// held still for a moment (<see cref="MovablePanel.longPinchSeconds"/>). Poking it puts every
    /// movable panel back where the scene has it. It hides by itself after a few seconds, and is
    /// shown and hidden by scale so its poke interactable stays registered. One chip serves every
    /// panel; it is a child of the near-user rig, not of a panel.
    /// </summary>
    [DisallowMultipleComponent]
    public class PanelResetChip : MonoBehaviour
    {
        [Tooltip("Scale of the key when shown (the scene saves it hidden).")]
        public Vector3 shownScale = Vector3.one * 0.03f;
        [Tooltip("Seconds the chip stays before it hides again.")]
        public float showSeconds = 6f;

        readonly ScaleEase _ease = new ScaleEase();
        bool _inited, _wasAnimating;
        float _hideAt;

        public bool Shown => _inited && _ease.Shown;

        void Awake() { EnsureInit(); }

        void EnsureInit()
        {
            if (_inited) return;
            _inited = true;
            _ease.Init(transform, ScaleEase.ResolveFull(transform, shownScale), 0.2f, false);
        }

        public void ShowAt(Vector3 position, Quaternion rotation)
        {
            EnsureInit();
            if (!_ease.Shown) transform.SetPositionAndRotation(position, rotation);
            _ease.Show();
            _hideAt = Time.unscaledTime + showSeconds;
        }

        public void Hide()
        {
            EnsureInit();
            _ease.Hide();
        }

        /// <summary>The key's target: reset the layout (only while the chip is on show).</summary>
        public void Press()
        {
            if (!Shown) return;
            MovablePanel.ResetAll();
            Hide();
        }

        void Update()
        {
            _ease.Tick(Time.unscaledDeltaTime);
            if (_ease.Shown && Time.unscaledTime >= _hideAt) _ease.Hide();
            bool animating = _ease.Animating;
            // Let the key's press visual re-read its resting offsets at the final scale.
            if (_wasAnimating && !animating && _ease.Shown)
                BroadcastMessage("SyncToCurrentScale", SendMessageOptions.DontRequireReceiver);
            _wasAnimating = animating;
        }
    }
}
