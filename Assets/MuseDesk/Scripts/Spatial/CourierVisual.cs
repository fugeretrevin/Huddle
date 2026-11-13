using UnityEngine;
using MuseDesk.Workspace;

namespace MuseDesk.Spatial
{
    /// <summary>
    /// The small tray that carries the decision paper from the far office to the user. Presentation
    /// only: it is posed by <see cref="RemoteDocumentDeliveryController"/> and shown / hidden by
    /// scale (opaque primitives, no particles). The art can be replaced by swapping the children
    /// of this object; nothing else refers to them.
    /// </summary>
    public class CourierVisual : MonoBehaviour
    {
        [Tooltip("Scale of the courier when shown (used when the builder saved it hidden).")]
        public Vector3 shownScale = Vector3.one;
        public float easeSeconds = 0.25f;

        readonly ScaleEase _ease = new ScaleEase();
        bool _ready;

        /// <summary>True when the courier is shown or on its way to being shown.</summary>
        public bool Shown => _ready && _ease.Shown;

        void Awake() { Ensure(); }

        void Ensure()
        {
            if (_ready) return;
            _ease.Init(transform, ScaleEase.ResolveFull(transform, shownScale), easeSeconds, false);
            _ready = true;
        }

        void Update() { _ease.Tick(Time.unscaledDeltaTime); }

        public void Show() { Ensure(); _ease.Show(); }
        public void Hide() { Ensure(); _ease.Hide(); }
        /// <summary>Hide at once, without the ease (session reset).</summary>
        public void HideNow() { Ensure(); _ease.Snap(false); }

        public void SetPose(Vector3 position, Quaternion rotation)
        {
            transform.SetPositionAndRotation(position, rotation);
        }
    }
}
