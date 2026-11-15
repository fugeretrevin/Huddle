using UnityEngine;

namespace MuseDesk.Config
{
    /// <summary>
    /// The one place a release is configured. Asset: Assets/MuseDesk/Resources/ReleaseConfig.asset,
    /// loaded by <see cref="ReleaseConfigApplier"/> at start-up. Defaults are the current behaviour.
    /// Desk mode has no flag: it is always available.
    /// </summary>
    [CreateAssetMenu(fileName = "ReleaseConfig", menuName = "MuseDesk/Release Config")]
    public class MuseDeskReleaseConfig : ScriptableObject
    {
        public const string ResourceName = "ReleaseConfig";

        [Header("Spatial modes")]
        public bool floorMode = true;
        public bool ceilingMode = true;

        [Header("Ceiling extras")]
        [Tooltip("Reach up and pull the pod down (CeilingPullGesture).")]
        public bool ceilingPullGesture = true;
        [Tooltip("Aperture, tether and arrival particles of the ceiling deployment.")]
        public bool ceilingParticles = true;
        [Tooltip("The holographic portal cone between ceiling and pod.")]
        public bool ceilingPortalCone = true;

        [Header("Placement")]
        [Tooltip("The scan-reveal grids on the floor and ceiling while a placement is previewed.")]
        public bool placementScanGrids = true;

        [Header("Voice")]
        [Tooltip("Voice commands, microphone key, voice chip on the paper, pinch-to-talk.")]
        public bool voice = true;

        [Header("Wall Habitat (Phase 3) — ALL OFF until each is verified on a worn Quest")]
        [Tooltip("Wall mode: the treehouse habitat on a real wall (03_INTEGRATION_CONTRACT wall_habitat_enabled).")]
        public bool wallHabitat = false;
        [Tooltip("The Manager office, plan card and real task delegation (wall_manager_enabled). Needs wallHabitat.")]
        public bool wallManager = false;
        [Tooltip("Select Agents far pinch and the small desk visit (agent_select_enabled). Needs wallHabitat.")]
        public bool agentSelect = false;
        [Tooltip("Read-only /btw questions during a visit (agent_btw_enabled). Needs agentSelect.")]
        public bool agentBtw = false;
        [Tooltip("Meet Larger: the large-agent portal beside the user (large_agent_portal_enabled). Needs agentSelect.")]
        public bool largeAgentPortal = false;
        [Tooltip("Elevator door/indicator light effects (wall_elevator_vfx_enabled). Cosmetic.")]
        public bool wallElevatorVfx = false;

        [Header("Comfort (polish pass)")]
        [Tooltip("Reduced Motion by default for this build: shorter transitions, no overshoot. A per-device choice saved " +
                 "in PlayerPrefs (MuseDesk.ReducedMotion = 1/0) overrides this.")]
        public bool reducedMotion = false;
        [Tooltip("Show the in-app 'Calm motion' poke key (flips Reduced Motion live and saves it for this device). " +
                 "Off in the committed release: the key sits at its hidden scale and nothing else changes.")]
        public bool reducedMotionToggle = false;

        [Header("Build")]
        public string buildLabel = "rc6-dev";

        public string Summary()
        {
            return buildLabel + " floor=" + floorMode + " ceiling=" + ceilingMode + " pull=" + ceilingPullGesture
                + " particles=" + ceilingParticles + " cone=" + ceilingPortalCone + " grids=" + placementScanGrids + " voice=" + voice
                + " wall=" + wallHabitat + " manager=" + wallManager + " select=" + agentSelect + " btw=" + agentBtw
                + " portal=" + largeAgentPortal + " elevatorVfx=" + wallElevatorVfx
                + " reducedMotion=" + reducedMotion + " motionToggle=" + reducedMotionToggle;
        }

        public static MuseDeskReleaseConfig Load()
        {
            return Resources.Load<MuseDeskReleaseConfig>(ResourceName);
        }
    }
}
