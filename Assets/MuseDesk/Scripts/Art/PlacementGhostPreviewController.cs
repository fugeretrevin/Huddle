using TMPro;
using UnityEngine;

namespace MuseDesk.Art
{
    /// <summary>
    /// Controls PlacementGhostPreview_Floor and PlacementGhostPreview_Ceiling visuals:
    /// switches valid/invalid translucent state tint cleanly via MaterialPropertyBlock
    /// (Teal #2FA39A valid vs Coral #D9534A invalid) and pulses briefly on confirm.
    /// </summary>
    [DisallowMultipleComponent]
    public class PlacementGhostPreviewController : MonoBehaviour
    {
        private static readonly Color ColValid   = new Color(0.184f, 0.639f, 0.604f, 0.36f); // Teal
        private static readonly Color ColInvalid = new Color(0.851f, 0.325f, 0.290f, 0.42f); // Coral

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        [Header("References")]
        [SerializeField] private Renderer ghostRenderer;
        [SerializeField] private Transform previewRoot;
        [SerializeField] private Transform footprintCenterSocket;
        [SerializeField] private Transform confirmationBadgeSocket;
        [SerializeField] private TextMeshPro statusBadgeLabel;

        private bool _isValid = true;
        private float _pulseTimer;
        private MaterialPropertyBlock _mpb;

        public Transform PreviewRoot => previewRoot;
        public Transform FootprintCenterSocket => footprintCenterSocket;
        public Transform ConfirmationBadgeSocket => confirmationBadgeSocket;

        private void Awake()
        {
            ResolveReferences();
            SetValid(true);
        }

        private void Update()
        {
            if (_pulseTimer > 0f)
            {
                _pulseTimer = Mathf.Max(0f, _pulseTimer - Time.unscaledDeltaTime);
                ApplyColor();
            }
        }

        public void ResolveReferences()
        {
            if (ghostRenderer == null)
                ghostRenderer = GetComponentInChildren<Renderer>(true);

            Transform[] all = GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                Transform t = all[i];
                switch (t.name)
                {
                    case "PreviewRoot":             previewRoot = t; break;
                    case "FootprintCenterSocket":   footprintCenterSocket = t; break;
                    case "ConfirmationBadgeSocket": confirmationBadgeSocket = t; break;
                    case "Preview_StatusBadgeTMP":  statusBadgeLabel = t.GetComponent<TextMeshPro>(); break;
                }
            }
        }

        public void SetValid(bool valid, string labelOverride = null)
        {
            if (ghostRenderer == null) ResolveReferences();
            _isValid = valid;
            if (statusBadgeLabel != null)
            {
                statusBadgeLabel.text = !string.IsNullOrEmpty(labelOverride)
                    ? labelOverride
                    : (valid ? "READY TO PLACE" : "BLOCKED SURFACE");
            }
            ApplyColor();
        }

        public void TriggerConfirmPulse()
        {
            _pulseTimer = 0.35f;
            ApplyColor();
        }

        private void ApplyColor()
        {
            if (ghostRenderer == null) return;
            if (_mpb == null) _mpb = new MaterialPropertyBlock();

            Color col = _isValid ? ColValid : ColInvalid;
            float boost = _pulseTimer > 0f ? 0.7f * (_pulseTimer / 0.35f) : 0.25f;

            ghostRenderer.GetPropertyBlock(_mpb);
            _mpb.SetColor(BaseColorId, col);
            _mpb.SetColor(EmissionColorId, new Color(col.r, col.g, col.b, 1f) * boost);
            ghostRenderer.SetPropertyBlock(_mpb);
        }
    }
}

