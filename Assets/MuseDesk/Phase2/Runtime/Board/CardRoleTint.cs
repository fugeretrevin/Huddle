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

        [Tooltip("Emission multiplier so the header reads in passthrough.")]
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

        void Apply(Color c)
        {
            // Use the instance material (not shared) so pooled cards tint independently.
            var m = headerStrip.material;
            if (m == null) return;
            m.color = c;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (emission > 0f && m.HasProperty("_EmissionColor"))
            {
                m.EnableKeyword("_EMISSION");
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                m.SetColor("_EmissionColor", c * emission);
            }
        }
    }
}
