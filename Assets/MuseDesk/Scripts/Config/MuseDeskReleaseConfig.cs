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

        [Header("Build")]
        public string buildLabel = "rc6-dev";

        public string Summary()
        {
            return buildLabel + " floor=" + floorMode + " ceiling=" + ceilingMode + " pull=" + ceilingPullGesture
                + " particles=" + ceilingParticles + " cone=" + ceilingPortalCone + " grids=" + placementScanGrids + " voice=" + voice;
        }

        public static MuseDeskReleaseConfig Load()
        {
            return Resources.Load<MuseDeskReleaseConfig>(ResourceName);
        }
    }
}
