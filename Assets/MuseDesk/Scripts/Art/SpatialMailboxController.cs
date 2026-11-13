using TMPro;
using UnityEngine;

namespace MuseDesk.Art
{
    /// <summary>
    /// Controls the physical SpatialMailbox accessory usable on both Floor and Ceiling workspaces.
    /// Exposes the 3 typed slot sockets (Completed, Suggestion, PendingDecision), the pickup socket,
    /// and a crisp TMP numeric count readout without creating duplicate backend documents.
    /// </summary>
    [DisallowMultipleComponent]
    public class SpatialMailboxController : MonoBehaviour
    {
        [Header("Sockets")]
        [SerializeField] private Transform mailboxRoot;
        [SerializeField] private Transform completedDocSocket;
        [SerializeField] private Transform suggestionDocSocket;
        [SerializeField] private Transform pendingDecisionSocket;
        [SerializeField] private Transform documentPickupPoint;

        [Header("Readout")]
        [SerializeField] private TextMeshPro countLabel;
        [SerializeField] private Renderer highlightStripRenderer;

        private static readonly Color ColCalm     = new Color(0.184f, 0.639f, 0.604f, 1f); // Teal
        private static readonly Color ColPending  = new Color(0.878f, 0.627f, 0.188f, 1f); // Warm Amber
        private static readonly Color ColReady    = new Color(0.180f, 0.608f, 0.373f, 1f); // Success Green
        private static readonly Color ColDim      = new Color(0.220f, 0.240f, 0.280f, 1f);

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
        private MaterialPropertyBlock _mpb;

        public Transform MailboxRoot => mailboxRoot;
        public Transform CompletedDocSocket => completedDocSocket;
        public Transform SuggestionDocSocket => suggestionDocSocket;
        public Transform PendingDecisionSocket => pendingDecisionSocket;
        public Transform DocumentPickupPoint => documentPickupPoint;

        private void Awake()
        {
            ResolveReferences();
            UpdateCounts(0, 0, 0);
        }

        public void ResolveReferences()
        {
            Transform[] all = GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                Transform t = all[i];
                switch (t.name)
                {
                    case "MailboxRoot":           mailboxRoot = t; break;
                    case "CompletedDocSocket":    completedDocSocket = t; break;
                    case "SuggestionDocSocket":   suggestionDocSocket = t; break;
                    case "PendingDecisionSocket": pendingDecisionSocket = t; break;
                    case "DocumentPickupPoint":   documentPickupPoint = t; break;
                    case "Mailbox_CountTMP":      countLabel = t.GetComponent<TextMeshPro>(); break;
                }
            }

            if (highlightStripRenderer == null)
            {
                Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
                for (int i = 0; i < renderers.Length; i++)
                {
                    if (renderers[i].name.Contains("SlotAccent"))
                    {
                        highlightStripRenderer = renderers[i];
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// Updates the small integrated numeric indicator and gentle colour cue.
        /// Mirrors persisted document/event counts provided by runtime logic.
        /// </summary>
        public void UpdateCounts(int completed, int suggestions, int pendingDecisions)
        {
            if (mailboxRoot == null) ResolveReferences();

            int total = Mathf.Max(0, completed) + Mathf.Max(0, suggestions) + Mathf.Max(0, pendingDecisions);
            if (countLabel != null)
            {
                countLabel.text = total > 99 ? "99+" : total.ToString();
            }

            if (highlightStripRenderer != null)
            {
                if (_mpb == null) _mpb = new MaterialPropertyBlock();
                Color col = pendingDecisions > 0 ? ColPending
                          : completed > 0        ? ColReady
                          : suggestions > 0      ? ColCalm
                                                 : ColDim;
                float em = total > 0 ? 0.38f : 0.05f;
                highlightStripRenderer.GetPropertyBlock(_mpb);
                _mpb.SetColor(BaseColorId, col);
                _mpb.SetColor(EmissionColorId, col * em);
                highlightStripRenderer.SetPropertyBlock(_mpb);
            }
        }

        /// <summary>
        /// Mounts a visual document prop into the specified mailbox slot while preserving world scale.
        /// </summary>
        public void MountDocumentVisual(Transform docTransform, int slotType, Vector3 desiredWorldScale)
        {
            if (docTransform == null) return;
            if (mailboxRoot == null) ResolveReferences();

            Transform socket = slotType == 0 ? completedDocSocket
                             : slotType == 1 ? suggestionDocSocket
                             : slotType == 2 ? pendingDecisionSocket
                                             : documentPickupPoint;
            if (socket == null) socket = transform;
            SpatialScaleUtility.MountPreservingWorldScale(docTransform, socket, desiredWorldScale, resetLocalPose: true);
        }
    }
}

