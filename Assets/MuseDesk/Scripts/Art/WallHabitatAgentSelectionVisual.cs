using UnityEngine;

namespace MuseDesk.Art
{
    /// <summary>
    /// Controls visual state markers for distance-selection and projection states:
    /// home, selected, carried_proxy, desk_visit, large_portal, returning.
    /// </summary>
    [DisallowMultipleComponent]
    public class WallHabitatAgentSelectionVisual : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        [SerializeField] private Renderer[] ringRenderers;
        [SerializeField] private GameObject pointerArrowRoot;

        private MaterialPropertyBlock _mpb;
        private AgentPresentationVisualState _state = AgentPresentationVisualState.Home;

        public AgentPresentationVisualState State => _state;

        private void Awake()
        {
            ResolveReferences();
            ApplyState(AgentPresentationVisualState.Home);
        }

        public void ResolveReferences()
        {
            ringRenderers = GetComponentsInChildren<Renderer>(true);
            Transform[] all = GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i].name.Contains("Pointer"))
                {
                    pointerArrowRoot = all[i].gameObject;
                    break;
                }
            }
        }

        public void ApplyState(AgentPresentationVisualState nextState)
        {
            if (ringRenderers == null || ringRenderers.Length == 0) ResolveReferences();
            _state = nextState;

            bool showVisual = nextState != AgentPresentationVisualState.Home;
            for (int i = 0; i < ringRenderers.Length; i++)
            {
                if (ringRenderers[i] != null) ringRenderers[i].enabled = showVisual;
            }
            if (pointerArrowRoot != null)
                pointerArrowRoot.SetActive(nextState == AgentPresentationVisualState.Selected || nextState == AgentPresentationVisualState.CarriedProxy);

            if (!showVisual) return;

            Color c = nextState switch
            {
                AgentPresentationVisualState.Selected => new Color(0.28f, 0.85f, 0.80f, 1f),
                AgentPresentationVisualState.CarriedProxy => new Color(1.00f, 0.80f, 0.30f, 1f),
                AgentPresentationVisualState.DeskVisit => new Color(0.35f, 0.82f, 0.62f, 1f),
                AgentPresentationVisualState.LargePortal => new Color(0.56f, 0.48f, 0.90f, 1f),
                AgentPresentationVisualState.Returning => new Color(0.75f, 0.80f, 0.90f, 1f),
                _ => Color.white,
            };

            if (_mpb == null) _mpb = new MaterialPropertyBlock();
            for (int i = 0; i < ringRenderers.Length; i++)
            {
                Renderer r = ringRenderers[i];
                if (r == null) continue;
                r.GetPropertyBlock(_mpb);
                _mpb.SetColor(BaseColorId, c);
                _mpb.SetColor(EmissionColorId, c * 0.65f);
                r.SetPropertyBlock(_mpb);
            }
        }
    }
}
