using UnityEngine;

namespace MuseDesk.Art
{
    /// <summary>
    /// Restrained Large-Agent Portal visual & optional sparse mote controller.
    /// Exposes enlarged proxy scale bounds (~0.9-1.2m target height) and side DialoguePanelSocket.
    /// </summary>
    [DisallowMultipleComponent]
    public class WallHabitatLargePortalVisualController : MonoBehaviour
    {
        [SerializeField] private Animator portalAnimator;
        [SerializeField] private ParticleSystem optionalSparseMotes;
        [SerializeField] private Transform agentMountPose;
        [SerializeField] private Transform dialoguePanelSocket;
        [SerializeField] private bool comfortReducedMotion;

        public Animator PortalAnimator => portalAnimator;
        public Transform AgentMountPose => agentMountPose;
        public Transform DialoguePanelSocket => dialoguePanelSocket;

        private void Awake()
        {
            ResolveReferences();
        }

        public void ResolveReferences()
        {
            if (portalAnimator == null) portalAnimator = GetComponentInChildren<Animator>(true);
            if (optionalSparseMotes == null) optionalSparseMotes = GetComponentInChildren<ParticleSystem>(true);
            Transform[] all = GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i].name == "AgentHomePose") agentMountPose = all[i];
                else if (all[i].name == "DialoguePanelSocket") dialoguePanelSocket = all[i];
            }
        }

        public void SetComfortReducedMotion(bool reduced)
        {
            comfortReducedMotion = reduced;
            if (optionalSparseMotes != null && reduced)
                optionalSparseMotes.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        public void PlayOpen()
        {
            if (portalAnimator != null) portalAnimator.Play("Portal_Open_Expand", 0, 0f);
            if (optionalSparseMotes != null && !comfortReducedMotion) optionalSparseMotes.Play(true);
        }

        public void PlayClose()
        {
            if (portalAnimator != null) portalAnimator.Play("Portal_Close_Collapse", 0, 0f);
            if (optionalSparseMotes != null) optionalSparseMotes.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }
    }
}
