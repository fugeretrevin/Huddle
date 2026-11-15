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
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        private static readonly Color ColIndicatorActive = new Color(0.280f, 0.850f, 0.800f, 1f);
        private static readonly Color ColIndicatorCall = new Color(0.878f, 0.627f, 0.188f, 1f);
        private static readonly Color ColIndicatorDim = new Color(0.160f, 0.200f, 0.240f, 1f);

        private static readonly int CabIdleClosedHash = Animator.StringToHash("Cabin_Idle_Closed");
        private static readonly int CabTravelBobHash = Animator.StringToHash("Cabin_Travel_Bob");
        private static readonly int CabArriveChimeHash = Animator.StringToHash("Cabin_Arrive_Chime");
        private static readonly int CabGateOpenHash = Animator.StringToHash("Cabin_Gate_Open");
        private static readonly int CabGateCloseHash = Animator.StringToHash("Cabin_Gate_Close");

        private static readonly int SpineWinchSpinHash = Animator.StringToHash("ElevatorWinch_Spin");
        private static readonly int SpineDoorsIdleClosedHash = Animator.StringToHash("ElevatorDoors_Idle_Closed");
        private static readonly int DoorsOpenLowerHash = Animator.StringToHash("ElevatorDoors_Open_Lower");
        private static readonly int DoorsOpenMidHash = Animator.StringToHash("ElevatorDoors_Open_Mid");
        private static readonly int DoorsOpenManagerHash = Animator.StringToHash("ElevatorDoors_Open_Manager");
        private static readonly int DoorsCloseLowerHash = Animator.StringToHash("ElevatorDoors_Close_Lower");
        private static readonly int DoorsCloseMidHash = Animator.StringToHash("ElevatorDoors_Close_Mid");
        private static readonly int DoorsCloseManagerHash = Animator.StringToHash("ElevatorDoors_Close_Manager");

        [Header("Animator & Socket References")]
        [SerializeField] private Animator spineAnimator;
        [SerializeField] private Animator cabAnimator;
        [SerializeField] private Transform elevatorCarPivot;
        [SerializeField] private Transform stopLower;
        [SerializeField] private Transform stopMid;
        [SerializeField] private Transform stopManager;

        [Header("Floor Indicator Renderers (MPB)")]
        [SerializeField] private Renderer lowerFloorIndicator;
        [SerializeField] private Renderer midFloorIndicator;
        [SerializeField] private Renderer managerFloorIndicator;

        [Header("Optional Arrival VFX & Audio Hooks")]
        [SerializeField] private ParticleSystem arrivalParticles;
        [SerializeField] private AudioSource elevatorAudioSource;
        [SerializeField] private AudioClip callClip;
        [SerializeField] private AudioClip arrivalClip;
        [SerializeField] private AudioClip doorsOpenClip;
        [SerializeField] private AudioClip doorsCloseClip;
        [SerializeField] private bool reducedMotion;

        [SerializeField] private float travelSpeedMetersPerSec = 0.24f;
        private float _targetLocalY;
        private bool _hasTarget;
        private bool _isTraveling;
        private int _activeFloorIndex;
        private string _activeStopId = "ElevatorStop_Lower";
        private MaterialPropertyBlock _mpb;

        public Animator SpineAnimator => spineAnimator;
        public Animator CabAnimator => cabAnimator;
        public Transform ElevatorCarPivot => elevatorCarPivot;
        public bool IsTraveling => _isTraveling;
        public int ActiveFloorIndex => _activeFloorIndex;

        private void Awake()
        {
            ResolveReferences();
            HighlightFloorIndicator(_activeFloorIndex);
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

                if (lowerFloorIndicator == null && t.name.Contains("IndGem_Lower"))
                    lowerFloorIndicator = t.GetComponent<Renderer>();
                else if (midFloorIndicator == null && t.name.Contains("IndGem_Mid"))
                    midFloorIndicator = t.GetComponent<Renderer>();
                else if (managerFloorIndicator == null && t.name.Contains("IndGem_Manager"))
                    managerFloorIndicator = t.GetComponent<Renderer>();
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

            if (arrivalParticles == null)
            {
                arrivalParticles = GetComponentInChildren<ParticleSystem>(true);
            }
            if (elevatorAudioSource == null)
            {
                elevatorAudioSource = GetComponent<AudioSource>();
            }
            if (_mpb == null)
            {
                _mpb = new MaterialPropertyBlock();
            }
        }

        public void SetReducedMotion(bool reduced)
        {
            reducedMotion = reduced;
            if (reduced && arrivalParticles != null)
            {
                arrivalParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
        }

        public void MoveCabToStop(string stopSocketName, bool instant = false)
        {
            if (elevatorCarPivot == null) ResolveReferences();
            _activeStopId = string.IsNullOrEmpty(stopSocketName) ? "ElevatorStop_Lower" : stopSocketName;
            _activeFloorIndex = StopIdToFloorIndex(_activeStopId);

            Transform targetStop = _activeStopId switch
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
                if (cabAnimator != null && HasState(cabAnimator, CabIdleClosedHash))
                    cabAnimator.Play(CabIdleClosedHash, 0, 0f);
                HighlightFloorIndicator(_activeFloorIndex);
            }
            else
            {
                _isTraveling = true;
                if (cabAnimator != null && HasState(cabAnimator, CabTravelBobHash))
                    cabAnimator.Play(CabTravelBobHash, 0, 0f);
                if (spineAnimator != null && HasState(spineAnimator, SpineWinchSpinHash))
                    spineAnimator.Play(SpineWinchSpinHash, 0, 0f);
            }
        }

        /// <summary>
        /// Triggers the elevator call cue (audio + winch engagement) at the current target stop.
        /// </summary>
        public void PlayElevatorCall()
        {
            PlayElevatorCall(_activeFloorIndex);
        }

        /// <summary>
        /// Triggers the elevator call cue and highlights the requested floor indicator in call amber.
        /// </summary>
        public void PlayElevatorCall(int floorIndex)
        {
            int clamped = ClampFloorIndex(floorIndex);
            HighlightFloorIndicator(clamped, ColIndicatorCall, 0.75f);
            if (!_isTraveling && spineAnimator != null && HasState(spineAnimator, SpineWinchSpinHash))
            {
                spineAnimator.Play(SpineWinchSpinHash, 0, 0f);
            }
            PlayOptionalAudio(callClip);
        }

        /// <summary>
        /// Triggers arrival chime animation, floor indicator MPB highlight, and optional arrival VFX/audio.
        /// </summary>
        public void PlayElevatorArrival(int floorIndex)
        {
            int clamped = ClampFloorIndex(floorIndex);
            _activeFloorIndex = clamped;
            _activeStopId = FloorIndexToStopId(clamped);

            HighlightFloorIndicator(clamped);

            if (cabAnimator != null && HasState(cabAnimator, CabArriveChimeHash))
            {
                cabAnimator.Play(CabArriveChimeHash, 0, 0f);
            }

            if (arrivalParticles != null && !reducedMotion)
            {
                Transform anchor = GetStopTransform(clamped) ?? elevatorCarPivot;
                if (anchor != null)
                {
                    arrivalParticles.transform.position = anchor.position;
                }
                arrivalParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                arrivalParticles.Play(true);
            }

            PlayOptionalAudio(arrivalClip);
        }

        /// <summary>
        /// Opens the landing doors and cab gate for the given stopId (e.g., "ElevatorStop_Lower", "Mid", "Manager").
        /// </summary>
        public void PlayDoorsOpen(string stopId)
        {
            _activeStopId = string.IsNullOrEmpty(stopId) ? "ElevatorStop_Lower" : stopId;
            _activeFloorIndex = StopIdToFloorIndex(_activeStopId);
            HighlightFloorIndicator(_activeFloorIndex);
            SetDoorsOpen(_activeStopId, true);
            PlayOptionalAudio(doorsOpenClip);
        }

        /// <summary>
        /// Closes the active landing doors and cab gate cleanly.
        /// </summary>
        public void PlayDoorsClose()
        {
            string stopId = string.IsNullOrEmpty(_activeStopId) ? "ElevatorStop_Lower" : _activeStopId;
            SetDoorsOpen(stopId, false);
            PlayOptionalAudio(doorsCloseClip);
        }

        public void SetDoorsOpen(string floorName, bool open)
        {
            string safeName = floorName ?? string.Empty;
            if (spineAnimator != null)
            {
                int doorHash = ResolveDoorStateHash(safeName, open);
                if (HasState(spineAnimator, doorHash)) spineAnimator.Play(doorHash, 0, 0f);
            }
            if (cabAnimator != null)
            {
                int cabHash = open ? CabGateOpenHash : CabGateCloseHash;
                if (HasState(cabAnimator, cabHash)) cabAnimator.Play(cabHash, 0, 0f);
            }
        }

        /// <summary>
        /// Highlights the specified floor indicator via MaterialPropertyBlock and dims all others.
        /// </summary>
        public void HighlightFloorIndicator(int floorIndex)
        {
            HighlightFloorIndicator(floorIndex, ColIndicatorActive, 0.90f);
        }

        private void HighlightFloorIndicator(int floorIndex, Color activeColor, float activeEmission)
        {
            int clamped = ClampFloorIndex(floorIndex);
            ApplyIndicatorColor(lowerFloorIndicator, clamped == 0 ? activeColor : ColIndicatorDim, clamped == 0 ? activeEmission : 0.05f);
            ApplyIndicatorColor(midFloorIndicator, clamped == 1 ? activeColor : ColIndicatorDim, clamped == 1 ? activeEmission : 0.05f);
            ApplyIndicatorColor(managerFloorIndicator, clamped == 2 ? activeColor : ColIndicatorDim, clamped == 2 ? activeEmission : 0.05f);
        }

        private void ApplyIndicatorColor(Renderer r, Color col, float emission)
        {
            if (r == null) return;
            if (_mpb == null) _mpb = new MaterialPropertyBlock();

            r.GetPropertyBlock(_mpb);
            _mpb.SetColor(BaseColorId, col);
            _mpb.SetColor(ColorId, col);
            _mpb.SetColor(EmissionColorId, col * emission);
            r.SetPropertyBlock(_mpb);
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
                if (spineAnimator != null && HasState(spineAnimator, SpineDoorsIdleClosedHash))
                    spineAnimator.Play(SpineDoorsIdleClosedHash, 0, 0f);
                PlayElevatorArrival(_activeFloorIndex);
            }
        }

        private void PlayOptionalAudio(AudioClip clip)
        {
            if (elevatorAudioSource != null && clip != null)
            {
                elevatorAudioSource.PlayOneShot(clip);
            }
        }

        private Transform GetStopTransform(int floorIndex)
        {
            return floorIndex switch
            {
                0 => stopLower,
                1 => stopMid ?? stopManager,
                _ => stopManager,
            };
        }

        private int ClampFloorIndex(int floorIndex)
        {
            if (floorIndex <= 0) return 0;
            if (floorIndex == 1) return stopMid != null ? 1 : 2;
            return 2;
        }

        private int StopIdToFloorIndex(string stopId)
        {
            if (string.IsNullOrEmpty(stopId)) return 0;
            if (stopId.IndexOf("Manager", System.StringComparison.OrdinalIgnoreCase) >= 0) return 2;
            if (stopId.IndexOf("Mid", System.StringComparison.OrdinalIgnoreCase) >= 0) return stopMid != null ? 1 : 2;
            return 0;
        }

        private static string FloorIndexToStopId(int floorIndex)
        {
            return floorIndex switch
            {
                1 => "ElevatorStop_Mid",
                2 => "ElevatorStop_Manager",
                _ => "ElevatorStop_Lower",
            };
        }

        private static int ResolveDoorStateHash(string floorName, bool open)
        {
            if (floorName.IndexOf("Manager", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return open ? DoorsOpenManagerHash : DoorsCloseManagerHash;
            }
            if (floorName.IndexOf("Mid", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return open ? DoorsOpenMidHash : DoorsCloseMidHash;
            }
            return open ? DoorsOpenLowerHash : DoorsCloseLowerHash;
        }

        private static bool HasState(Animator anim, int stateHash)
        {
            if (anim == null || anim.runtimeAnimatorController == null) return false;
            return anim.HasState(0, stateHash);
        }
    }
}
