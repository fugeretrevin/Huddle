using UnityEngine;

namespace MuseDesk.Art
{
    /// <summary>
    /// Modular art-layer controller for the Miniature Software Factory (Section 8).
    /// Exposes the 10 canonical Phase 2 factory animation hooks so backend/application
    /// events can trigger visual production stages without simulating fake task progress.
    /// </summary>
    [DisallowMultipleComponent]
    public class SoftwareFactoryVisualController : MonoBehaviour
    {
        public const string ClipBlueprintPrint      = "1_Blueprint_Print";
        public const string ClipBlueprintTransfer   = "2_Blueprint_Transfer";
        public const string ClipArtifactAssemble    = "3_Artifact_Assemble";
        public const string ClipDevelopmentComplete = "4_Development_Complete";
        public const string ClipArtifactTransport   = "5_Artifact_Transport";
        public const string ClipTestingActivate     = "6_Testing_Activate";
        public const string ClipTestSuccess         = "7_Test_Success";
        public const string ClipTestFailed          = "8_Test_Failed";
        public const string ClipRejectedReturn      = "9_Rejected_Return";
        public const string ClipCompletedDelivery   = "10_Completed_Delivery";

        [Header("Animator & Socket Anchors")]
        [SerializeField] private Animator factoryAnimator;
        [SerializeField] private Transform researchInputSocket;
        [SerializeField] private Transform devBuildSocket;
        [SerializeField] private Transform qaChamberSocket;
        [SerializeField] private Transform deliveryOutputSocket;
        [SerializeField] private Transform failedReturnSocket;

        public Animator FactoryAnimator => factoryAnimator;
        public Transform ResearchInputSocket => researchInputSocket;
        public Transform DevBuildSocket => devBuildSocket;
        public Transform QaChamberSocket => qaChamberSocket;
        public Transform DeliveryOutputSocket => deliveryOutputSocket;
        public Transform FailedReturnSocket => failedReturnSocket;

        /// <summary>THE single off-switch. Owner feedback (rc8): the factory/conveyor looked wonky and made
        /// no sense, so it is removed from the experience. False = hidden and inert; flip to true to restore.</summary>
        public static bool ShowFactory = false;

        private void Awake()
        {
            if (!ShowFactory) { gameObject.SetActive(false); return; }
            ResolveReferences();
        }

        public void ResolveReferences()
        {
            if (factoryAnimator == null)
            {
                factoryAnimator = GetComponentInChildren<Animator>();
            }

            Transform[] children = GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < children.Length; i++)
            {
                Transform t = children[i];
                if (t.name == "Research_Input_Socket")  researchInputSocket  = t;
                else if (t.name == "Dev_Build_Socket")       devBuildSocket       = t;
                else if (t.name == "QA_Chamber_Socket")      qaChamberSocket      = t;
                else if (t.name == "Delivery_Output_Socket") deliveryOutputSocket = t;
                else if (t.name == "Failed_Return_Socket")   failedReturnSocket   = t;
            }
        }

        public void PlayStageClip(string clipName, float crossfade = 0.2f)
        {
            if (!ShowFactory) return;
            if (factoryAnimator == null)
            {
                factoryAnimator = GetComponentInChildren<Animator>();
            }
            if (factoryAnimator != null && factoryAnimator.runtimeAnimatorController != null)
            {
                int hash = Animator.StringToHash(clipName);
                if (factoryAnimator.HasState(0, hash))
                {
                    factoryAnimator.CrossFadeInFixedTime(hash, crossfade, 0);
                }
            }
        }

        // --- The 10 Canonical Factory Visual Hooks (Section 8) ---
        public void PlayBlueprintPrint()      => PlayStageClip(ClipBlueprintPrint);
        public void PlayBlueprintTransfer()   => PlayStageClip(ClipBlueprintTransfer);
        public void PlayArtifactAssemble()    => PlayStageClip(ClipArtifactAssemble);
        public void PlayDevelopmentComplete() => PlayStageClip(ClipDevelopmentComplete);
        public void PlayArtifactTransport()   => PlayStageClip(ClipArtifactTransport);
        public void PlayTestingActivate()     => PlayStageClip(ClipTestingActivate);
        public void PlayTestSuccess()         => PlayStageClip(ClipTestSuccess);
        public void PlayTestFailed()          => PlayStageClip(ClipTestFailed);
        public void PlayRejectedReturn()      => PlayStageClip(ClipRejectedReturn);
        public void PlayCompletedDelivery()   => PlayStageClip(ClipCompletedDelivery);
    }
}

