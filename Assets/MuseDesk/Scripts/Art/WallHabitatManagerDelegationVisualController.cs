using UnityEngine;

namespace MuseDesk.Art
{
    /// <summary>
    /// Drives the Manager Lookout &amp; WallHabitat_TaskPrinter delegation sequence
    /// (reviewing, printing task cards, dispatching delegation) with optional particle
    /// and AudioClip hooks. Never loops when idle.
    /// </summary>
    [DisallowMultipleComponent]
    public class WallHabitatManagerDelegationVisualController : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        private static readonly int PrinterIdleHash = Animator.StringToHash("Printer_Idle");
        private static readonly int PrinterQueuePulseHash = Animator.StringToHash("Printer_Queue_Pulse");
        private static readonly int PrinterPrintCardHash = Animator.StringToHash("Printer_Print_TaskCard");
        private static readonly int PrinterDispatchCardHash = Animator.StringToHash("Printer_Dispatch_Card");

        private static readonly int ManagerIdleHash = Animator.StringToHash("Manager_Lookout_Idle");
        private static readonly int ManagerReviewHash = Animator.StringToHash("Manager_Review_Plan");
        private static readonly int ManagerDelegateHash = Animator.StringToHash("Manager_Delegate_Point");

        private static readonly Color ColIdle = new Color(0.184f, 0.639f, 0.604f, 1f);
        private static readonly Color ColReviewAmber = new Color(0.878f, 0.627f, 0.188f, 1f);
        private static readonly Color ColPrintTeal = new Color(0.280f, 0.850f, 0.800f, 1f);
        private static readonly Color ColCompleteGreen = new Color(0.180f, 0.608f, 0.373f, 1f);

        [Header("Animator & Visual References")]
        [SerializeField] private Animator taskPrinterAnimator;
        [SerializeField] private Animator managerAnimator;
        [SerializeField] private Renderer statusOrbRenderer;
        [SerializeField] private ParticleSystem optionalPrintParticles;

        [Header("Optional Audio Hooks")]
        [SerializeField] private AudioSource audioSource;
        [SerializeField] private AudioClip managerReviewClip;
        [SerializeField] private AudioClip taskCardPrintedClip;
        [SerializeField] private AudioClip delegationCompleteClip;

        [Header("Timing & Comfort")]
        [SerializeField, Min(0f)] private float autoReturnToIdleSeconds = 1.4f;
        [SerializeField] private bool reducedMotion;

        private MaterialPropertyBlock _mpb;
        private float _idleCountdown;
        private bool _pendingIdleReturn;
        private bool _isIdle = true;

        public Animator TaskPrinterAnimator => taskPrinterAnimator;
        public Animator ManagerAnimator => managerAnimator;
        public bool IsIdle => _isIdle;

        private void Awake()
        {
            ResolveReferences();
            ResetIdle();
        }

        public void ResolveReferences()
        {
            Transform[] all = GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                Transform t = all[i];
                if (taskPrinterAnimator == null && t.name.Contains("TaskPrinter"))
                {
                    taskPrinterAnimator = t.GetComponentInChildren<Animator>(true);
                }
                if (managerAnimator == null && t.name.Contains("Agent_Manager"))
                {
                    managerAnimator = t.GetComponentInChildren<Animator>(true);
                }
                if (statusOrbRenderer == null && t.name.Contains("StatusOrb"))
                {
                    statusOrbRenderer = t.GetComponent<Renderer>();
                }
            }

            if (taskPrinterAnimator == null)
            {
                taskPrinterAnimator = GetComponentInChildren<Animator>(true);
            }
            if (optionalPrintParticles == null)
            {
                optionalPrintParticles = GetComponentInChildren<ParticleSystem>(true);
            }
            if (audioSource == null)
            {
                audioSource = GetComponent<AudioSource>();
            }
            if (_mpb == null)
            {
                _mpb = new MaterialPropertyBlock();
            }
        }

        public void SetReducedMotion(bool reduced)
        {
            reducedMotion = reduced;
            if (reduced && optionalPrintParticles != null)
            {
                optionalPrintParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
        }

        /// <summary>
        /// Triggers the Manager plan-review / printer queue-pulse cue.
        /// </summary>
        public void PlayManagerReview()
        {
            _isIdle = false;
            PlayAnimatorState(taskPrinterAnimator, PrinterQueuePulseHash);
            PlayAnimatorState(managerAnimator, ManagerReviewHash);
            ApplyStatusOrb(ColReviewAmber, 0.55f);
            PlayOptionalAudio(managerReviewClip);
            ScheduleIdleReturn();
        }

        /// <summary>
        /// Triggers task card printing animation, optional particle burst, and audio cue.
        /// </summary>
        public void PlayTaskCardPrinted()
        {
            _isIdle = false;
            PlayAnimatorState(taskPrinterAnimator, PrinterPrintCardHash);
            PlayAnimatorState(managerAnimator, ManagerDelegateHash);
            ApplyStatusOrb(ColPrintTeal, 0.75f);

            if (optionalPrintParticles != null && !reducedMotion)
            {
                optionalPrintParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                optionalPrintParticles.Play(true);
            }

            PlayOptionalAudio(taskCardPrintedClip);
            ScheduleIdleReturn();
        }

        /// <summary>
        /// Triggers final delegation dispatch animation and settles cleanly back to idle without looping.
        /// </summary>
        public void PlayDelegationComplete()
        {
            _isIdle = false;
            PlayAnimatorState(taskPrinterAnimator, PrinterDispatchCardHash);
            PlayAnimatorState(managerAnimator, ManagerIdleHash);
            ApplyStatusOrb(ColCompleteGreen, 0.65f);

            if (optionalPrintParticles != null)
            {
                optionalPrintParticles.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            }

            PlayOptionalAudio(delegationCompleteClip);
            ScheduleIdleReturn();
        }

        /// <summary>
        /// Immediately restores idle pose, stops particles, and disables idle animation updates.
        /// </summary>
        public void ResetIdle()
        {
            _isIdle = true;
            _pendingIdleReturn = false;
            _idleCountdown = 0f;

            PlayAnimatorState(taskPrinterAnimator, PrinterIdleHash);
            PlayAnimatorState(managerAnimator, ManagerIdleHash);
            ApplyStatusOrb(ColIdle, 0.22f);

            if (optionalPrintParticles != null)
            {
                optionalPrintParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
        }

        private void Update()
        {
            if (!_pendingIdleReturn) return;

            _idleCountdown -= Time.deltaTime;
            if (_idleCountdown <= 0f)
            {
                ResetIdle();
            }
        }

        private void ScheduleIdleReturn()
        {
            if (autoReturnToIdleSeconds > 0f)
            {
                _idleCountdown = autoReturnToIdleSeconds;
                _pendingIdleReturn = true;
            }
            else
            {
                _pendingIdleReturn = false;
            }
        }

        private void PlayOptionalAudio(AudioClip clip)
        {
            if (audioSource != null && clip != null)
            {
                audioSource.PlayOneShot(clip);
            }
        }

        private void ApplyStatusOrb(Color color, float emissionIntensity)
        {
            if (statusOrbRenderer == null) return;
            if (_mpb == null) _mpb = new MaterialPropertyBlock();

            statusOrbRenderer.GetPropertyBlock(_mpb);
            _mpb.SetColor(BaseColorId, color);
            _mpb.SetColor(ColorId, color);
            _mpb.SetColor(EmissionColorId, color * emissionIntensity);
            statusOrbRenderer.SetPropertyBlock(_mpb);
        }

        private static void PlayAnimatorState(Animator anim, int stateHash)
        {
            if (anim == null || anim.runtimeAnimatorController == null) return;
            if (anim.HasState(0, stateHash))
            {
                anim.Play(stateHash, 0, 0f);
            }
        }
    }
}
