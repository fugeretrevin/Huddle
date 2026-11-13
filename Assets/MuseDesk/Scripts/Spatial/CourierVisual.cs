using UnityEngine;
using MuseDesk.Workspace;

namespace MuseDesk.Spatial
{
    /// <summary>
    /// The courier that carries the decision paper from the far office to the user. Presentation
    /// only: it is posed by <see cref="RemoteDocumentDeliveryController"/> (the single owner of its
    /// travel) and shown / hidden by scale.
    ///
    /// With the art assigned (the DocumentCourier model under <see cref="model"/>): the pose it is
    /// given is where the paper's lower edge is, so the courier is placed with its
    /// DocumentAttachSocket (<see cref="attachSocket"/>) exactly there; the paper itself is never
    /// parented to it. Only the model's own clips are played (idle, load, fly, present, wait,
    /// dock) and the station's lift / release / receive clips; the art-side
    /// DocumentCourierController is NOT used, because it moves the courier along its own route.
    /// The model is switched off while the courier is hidden, so nothing animates in Desk mode.
    /// Without art it is the primitive tray under this object, as before.
    /// </summary>
    public class CourierVisual : MonoBehaviour
    {
        [Tooltip("Scale of the courier when shown (used when the builder saved it hidden).")]
        public Vector3 shownScale = Vector3.one;
        public float easeSeconds = 0.25f;

        [Header("Art (optional)")]
        [Tooltip("Holder of the DocumentCourier model. Switched off while the courier is hidden.")]
        public GameObject model;
        [Tooltip("The model's Animator (controller MuseDesk_DocumentCourier).")]
        public Animator animator;
        [Tooltip("The FloorCourierStation's Animator at the office (controller MuseDesk_FloorCourierStation). Skipped while its object is inactive.")]
        public Animator stationAnimator;
        [Tooltip("The model's DocumentAttachSocket: SetPose puts this point, not the object's origin, at the given position.")]
        public Transform attachSocket;
        [Tooltip("Seconds of the present clip before the waiting idle.")]
        public float presentSeconds = 0.6f;
        [Tooltip("Seconds of the docking / receive clips before the idles.")]
        public float dockSeconds = 0.6f;

        static readonly int CourierDockedIdle = Animator.StringToHash("Courier_Docked_Idle");
        static readonly int CourierLoad = Animator.StringToHash("Courier_Load_Document");
        static readonly int CourierFly = Animator.StringToHash("Courier_Fly_Loop");
        static readonly int CourierPresent = Animator.StringToHash("Courier_Arrive_Present");
        static readonly int CourierWaiting = Animator.StringToHash("Courier_Waiting_Idle");
        static readonly int CourierReturnDock = Animator.StringToHash("Courier_Return_Dock");
        static readonly int StationIdle = Animator.StringToHash("Station_Idle");
        static readonly int StationLoad = Animator.StringToHash("Station_Load_Lift");
        static readonly int StationRelease = Animator.StringToHash("Station_Dispatch_Release");
        static readonly int StationReceive = Animator.StringToHash("Station_Dock_Receive");

        readonly ScaleEase _ease = new ScaleEase();
        bool _ready;
        int _nextCourier, _nextStation;
        float _nextCourierAt, _nextStationAt;

        /// <summary>True when the courier is shown or on its way to being shown.</summary>
        public bool Shown => _ready && _ease.Shown;

        /// <summary>The point the paper rides at: the attach socket when there is one, else this object's origin.</summary>
        public Vector3 CarryPoint => attachSocket != null ? attachSocket.position : transform.position;

        void Awake() { Ensure(); }

        void Ensure()
        {
            if (_ready) return;
            _ease.Init(transform, ScaleEase.ResolveFull(transform, shownScale), easeSeconds, false);
            _ready = true;
            if (model != null && model.activeSelf) model.SetActive(false);
        }

        void Update()
        {
            _ease.Tick(Time.unscaledDeltaTime);

            float now = Time.unscaledTime;
            if (_nextCourier != 0 && now >= _nextCourierAt) { Cross(animator, _nextCourier, 0.15f); _nextCourier = 0; }
            if (_nextStation != 0 && now >= _nextStationAt) { Cross(stationAnimator, _nextStation, 0.15f); _nextStation = 0; }

            // Fully hidden: the model (three skinned parts and an animator) is switched off.
            if (model != null && !_ease.Shown && !_ease.Animating && model.activeSelf)
            {
                model.SetActive(false);
                _nextCourier = 0;
            }
        }

        public void Show()
        {
            Ensure();
            if (model != null && !model.activeSelf) model.SetActive(true);
            _ease.Show();
        }

        public void Hide() { Ensure(); _ease.Hide(); }

        /// <summary>Hide at once, without the ease (session reset).</summary>
        public void HideNow()
        {
            Ensure();
            _ease.Snap(false);
            _nextCourier = 0;
            if (model != null && model.activeSelf) model.SetActive(false);
        }

        /// <summary>Pose the courier. With an attach socket, <paramref name="position"/> is where the
        /// socket goes (measured live, so it holds at any scale); otherwise it is the origin.</summary>
        public void SetPose(Vector3 position, Quaternion rotation)
        {
            if (attachSocket != null)
            {
                Transform t = transform;
                Vector3 local = Quaternion.Inverse(t.rotation) * (attachSocket.position - t.position);
                position -= rotation * local;
            }
            transform.SetPositionAndRotation(position, rotation);
        }

        // ── clips (local animation only; travel stays with RemoteDocumentDeliveryController) ──

        /// <summary>The paper is being put on the courier at the office.</summary>
        public void PlayLoad()
        {
            Cross(animator, CourierLoad, 0.12f); _nextCourier = 0;
            Cross(stationAnimator, StationLoad, 0.12f); _nextStation = 0;
        }

        /// <summary>The courier leaves the office with the paper.</summary>
        public void PlayFly()
        {
            Cross(animator, CourierFly, 0.15f); _nextCourier = 0;
            Cross(stationAnimator, StationRelease, 0.12f);
            _nextStation = StationIdle; _nextStationAt = Time.unscaledTime + dockSeconds;
        }

        /// <summary>The courier arrived in front of the user: present, then wait.</summary>
        public void PlayPresent()
        {
            Cross(animator, CourierPresent, 0.12f);
            _nextCourier = CourierWaiting; _nextCourierAt = Time.unscaledTime + presentSeconds;
        }

        /// <summary>The empty courier flies home.</summary>
        public void PlayReturn()
        {
            Cross(animator, CourierFly, 0.15f); _nextCourier = 0;
        }

        /// <summary>The courier is back at the office.</summary>
        public void PlayDocked()
        {
            Cross(animator, CourierReturnDock, 0.12f);
            _nextCourier = CourierDockedIdle; _nextCourierAt = Time.unscaledTime + dockSeconds;
            Cross(stationAnimator, StationReceive, 0.12f);
            _nextStation = StationIdle; _nextStationAt = Time.unscaledTime + dockSeconds;
        }

        static void Cross(Animator a, int state, float fade)
        {
            if (a == null || !a.isActiveAndEnabled || a.runtimeAnimatorController == null) return;
            if (a.HasState(0, state)) a.CrossFadeInFixedTime(state, fade, 0);
        }
    }
}
