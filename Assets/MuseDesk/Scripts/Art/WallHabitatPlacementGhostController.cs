using UnityEngine;

namespace MuseDesk.Art
{
    /// <summary>
    /// 3-state Wall Placement Preview visual controller (Teal=Valid, Amber=NeedsReview, Red=Invalid).
    /// Modulates MaterialPropertyBlock cleanly with zero material instantiation leaks.
    /// </summary>
    [DisallowMultipleComponent]
    public class WallHabitatPlacementGhostController : MonoBehaviour
    {
        private static readonly Color ColValid   = new Color(0.184f, 0.639f, 0.604f, 0.48f);
        private static readonly Color ColReview  = new Color(0.960f, 0.680f, 0.220f, 0.50f);
        private static readonly Color ColInvalid = new Color(0.851f, 0.300f, 0.270f, 0.52f);

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        [SerializeField] private Renderer[] ghostRenderers;
        [SerializeField] private Transform wallContact;
        [SerializeField] private Transform confirmationBadgeSocket;

        private MaterialPropertyBlock _mpb;

        public Transform WallContact => wallContact;
        public Transform ConfirmationBadgeSocket => confirmationBadgeSocket;

        private void Awake()
        {
            ResolveReferences();
            SetStatus(WallPlacementStatus.ValidTeal);
        }

        public void ResolveReferences()
        {
            ghostRenderers = GetComponentsInChildren<Renderer>(true);
            Transform[] all = GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i].name == "WallContact") wallContact = all[i];
                else if (all[i].name == "ConfirmationBadgeSocket") confirmationBadgeSocket = all[i];
            }
        }

        public void SetStatus(WallPlacementStatus status)
        {
            if (ghostRenderers == null || ghostRenderers.Length == 0) ResolveReferences();
            Color col = status switch
            {
                WallPlacementStatus.ValidTeal => ColValid,
                WallPlacementStatus.ReviewAmber => ColReview,
                _ => ColInvalid,
            };

            if (_mpb == null) _mpb = new MaterialPropertyBlock();
            for (int i = 0; i < ghostRenderers.Length; i++)
            {
                Renderer r = ghostRenderers[i];
                if (r == null) continue;
                r.GetPropertyBlock(_mpb);
                _mpb.SetColor(BaseColorId, col);
                _mpb.SetColor(EmissionColorId, new Color(col.r, col.g, col.b, 1f) * 0.55f);
                r.SetPropertyBlock(_mpb);
            }
        }
    }
}
