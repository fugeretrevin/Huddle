using UnityEngine;

namespace MuseDesk.Art
{
    public enum WallPlacementStatus
    {
        ValidTeal = 0,
        ReviewAmber = 1,
        InvalidRed = 2,
    }

    public enum AgentPresentationVisualState
    {
        Home = 0,
        Selected = 1,
        CarriedProxy = 2,
        DeskVisit = 3,
        LargePortal = 4,
        Returning = 5,
    }

    /// <summary>
    /// Visual/animation controller for the modular Workshop Treehouse Elevator Spine & Panorama Car.
    /// Moves ElevatorCarSocket cleanly between actual floor stop sockets (works identically in 2-Tier & 3-Tier)
    /// while driving local landing door open/close & cab gate animation clips.
    /// </summary>
    [DisallowMultipleComponent]
    public class WallHabitatElevatorVisualController : MonoBehaviour
    {
        [Header("Animator & Socket References")]
        [SerializeField] private Animator spineAnimator;
        [SerializeField] private Animator cabAnimator;
        [SerializeField] private Transform elevatorCarPivot;
        [SerializeField] private Transform stopLower;
        [SerializeField] private Transform stopMid;
        [SerializeField] private Transform stopManager;

        [SerializeField] private float travelSpeedMetersPerSec = 0.24f;
        private float _targetLocalY;
        private bool _hasTarget;
        private bool _isTraveling;

        public Animator SpineAnimator => spineAnimator;
        public Animator CabAnimator => cabAnimator;
        public Transform ElevatorCarPivot => elevatorCarPivot;
        public bool IsTraveling => _isTraveling;

        private void Awake()
        {
            ResolveReferences();
        }

        public void ResolveReferences()
        {
            Transform[] all = GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                Transform t = all[i];
                switch (t.name)
                {
                    case "ElevatorCarSocket":
                        elevatorCarPivot = t;
                        if (cabAnimator == null) cabAnimator = t.GetComponentInChildren<Animator>(true);
                        break;
                    case "ElevatorStop_Lower":   stopLower = t; break;
                    case "ElevatorStop_Mid":     stopMid = t; break;
                    case "ElevatorStop_Manager": stopManager = t; break;
                }
            }

            if (spineAnimator == null)
            {
                var anims = GetComponentsInChildren<Animator>(true);
                for (int i = 0; i < anims.Length; i++)
                {
                    if (anims[i] != cabAnimator)
                    {
                        spineAnimator = anims[i];
                        break;
                    }
                }
            }
        }

        public void MoveCabToStop(string stopSocketName, bool instant = false)
        {
            if (elevatorCarPivot == null) ResolveReferences();
            Transform targetStop = stopSocketName switch
            {
                "ElevatorStop_Lower" => stopLower,
                "ElevatorStop_Mid" => stopMid ?? stopManager,
                "ElevatorStop_Manager" => stopManager,
                _ => stopLower,
            };
            if (targetStop == null || elevatorCarPivot == null) return;

            float targetWorldY = targetStop.position.y;
            float parentWorldY = elevatorCarPivot.parent != null ? elevatorCarPivot.parent.position.y : 0f;
            float parentScaleY = elevatorCarPivot.parent != null ? Mathf.Max(0.001f, elevatorCarPivot.parent.lossyScale.y) : 1f;
            _targetLocalY = (targetWorldY - parentWorldY) / parentScaleY;
            _hasTarget = true;

            if (instant)
            {
                Vector3 lp = elevatorCarPivot.localPosition;
                lp.y = _targetLocalY;
                elevatorCarPivot.localPosition = lp;
                _isTraveling = false;
                if (cabAnimator != null && HasState(cabAnimator, "Cabin_Idle_Closed"))
                    cabAnimator.Play("Cabin_Idle_Closed", 0, 0f);
            }
            else
            {
                _isTraveling = true;
                if (cabAnimator != null && HasState(cabAnimator, "Cabin_Travel_Bob"))
                    cabAnimator.Play("Cabin_Travel_Bob", 0, 0f);
                if (spineAnimator != null && HasState(spineAnimator, "ElevatorWinch_Spin"))
                    spineAnimator.Play("ElevatorWinch_Spin", 0, 0f);
            }
        }

        public void SetDoorsOpen(string floorName, bool open)
        {
            string suffix = floorName.Contains("Manager") ? "Manager" : (floorName.Contains("Mid") ? "Mid" : "Lower");
            if (spineAnimator != null)
            {
                string clip = open ? $"ElevatorDoors_Open_{suffix}" : $"ElevatorDoors_Close_{suffix}";
                if (HasState(spineAnimator, clip)) spineAnimator.Play(clip, 0, 0f);
            }
            if (cabAnimator != null)
            {
                string cabClip = open ? "Cabin_Gate_Open" : "Cabin_Gate_Close";
                if (HasState(cabAnimator, cabClip)) cabAnimator.Play(cabClip, 0, 0f);
            }
        }

        private void Update()
        {
            if (!_hasTarget || !_isTraveling || elevatorCarPivot == null) return;
            Vector3 lp = elevatorCarPivot.localPosition;
            float nextY = Mathf.MoveTowards(lp.y, _targetLocalY, travelSpeedMetersPerSec * Time.deltaTime);
            lp.y = nextY;
            elevatorCarPivot.localPosition = lp;

            if (Mathf.Abs(nextY - _targetLocalY) <= 0.0005f)
            {
                _isTraveling = false;
                if (cabAnimator != null && HasState(cabAnimator, "Cabin_Arrive_Chime"))
                    cabAnimator.Play("Cabin_Arrive_Chime", 0, 0f);
                if (spineAnimator != null && HasState(spineAnimator, "ElevatorDoors_Idle_Closed"))
                    spineAnimator.Play("ElevatorDoors_Idle_Closed", 0, 0f);
            }
        }

        private static bool HasState(Animator anim, string stateName)
        {
            if (anim == null || anim.runtimeAnimatorController == null) return false;
            return anim.HasState(0, Animator.StringToHash(stateName));
        }
    }
}
