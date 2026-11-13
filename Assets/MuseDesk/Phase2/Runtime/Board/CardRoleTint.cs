using UnityEngine;
using MuseDesk.Phase2.Contracts;

namespace MuseDesk.Phase2.Board
{
    /// <summary>
    /// Cute, non-authoritative per-card decoration (P2-01): tints a card's header strip by the role of
    /// its currently assigned agent — researcher = blue, developer = green, tester = amber, and a warm
    /// neutral cream while unassigned. Purely cosmetic; the authoritative task truth always comes from
    /// the Phase2Reducer via <see cref="TaskCardView"/>. This only READS TaskCardView.Data, never mutates it.
    ///
    /// Added to the card template by AgentScripts/BuildBoard, so it exists only when the board feature
    /// has been built — it is additive and cannot affect Phase 1 or a board that was never constructed.
    /// </summary>
    [RequireComponent(typeof(TaskCardView))]
    public class CardRoleTint : MonoBehaviour
    {
        [Tooltip("The role-colored header strip renderer on the card (set by the Editor builder).")]
        public Renderer headerStrip;

        [Header("Role colors")]
        public Color unassigned = new Color(0.93f, 0.88f, 0.78f);   // warm cream
        public Color researcher = new Color(0.30f, 0.55f, 0.95f);   // blue
        public Color developer  = new Color(0.30f, 0.80f, 0.45f);   // green
        public Color tester     = new Color(0.95f, 0.65f, 0.25f);   // amber

        [Tooltip("Emission colour multiplier. Only has an effect if the strip's material already has emission on.")]
        [Range(0f, 1f)] public float emission = 0.25f;

        TaskCardView _view;
        string _lastAgent = "\u0001";   // sentinel that no real agent_id (or null) will equal

        void Awake() => _view = GetComponent<TaskCardView>();

        void OnEnable() => _lastAgent = "\u0001";   // force a re-apply when a pooled card is reactivated

        void LateUpdate()
        {
            if (_view == null || headerStrip == null) return;
            string agentId = _view.Data?.AgentId;
            if (agentId == _lastAgent) return;       // string == handles null safely
            _lastAgent = agentId;
            Apply(ColorFor(agentId));
        }

        Color ColorFor(string agentId)
        {
            switch (agentId)
            {
                case Phase2AgentIds.Researcher: return researcher;
                case Phase2AgentIds.Developer:  return developer;
                case Phase2AgentIds.Tester:     return tester;
                default:                        return unassigned;   // null / empty / unknown
            }
        }

        MaterialPropertyBlock _block;
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int ColorId = Shader.PropertyToID("_Color");
        static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        void Apply(Color c)
        {
            // A property block, not renderer.material: that made a material instance per card and
            // switched on an emission keyword (both ruled out by the redesign brief). Pooled cards
            // still tint independently. The emission colour only has an effect on a material that
            // already has emission enabled.
            if (_block == null) _block = new MaterialPropertyBlock();
            headerStrip.GetPropertyBlock(_block);
            _block.SetColor(BaseColorId, c);
            _block.SetColor(ColorId, c);
            _block.SetColor(EmissionColorId, c * emission);
            headerStrip.SetPropertyBlock(_block);
        }
    }
}
