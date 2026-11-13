using UnityEngine;

namespace MuseDesk.Art
{
    /// <summary>
    /// Modular art-layer controller for FloorOfficePlatform (and FloorOfficePlatform_Compact).
    /// Exposes all 10 required sockets, enforces a validated uniform scale range, and drives
    /// the subtle embedded perimeter status strip via MaterialPropertyBlock (zero material leaks).
    /// </summary>
    [DisallowMultipleComponent]
    public class FloorOfficePlatformController : MonoBehaviour
    {
        public static readonly Color DefaultTeal = new Color(0.184f, 0.639f, 0.604f, 1f);
        public static readonly Color AmberColor  = new Color(0.878f, 0.627f, 0.188f, 1f);
        public static readonly Color GreenColor  = new Color(0.180f, 0.608f, 0.373f, 1f);
        public static readonly Color RedColor    = new Color(0.851f, 0.325f, 0.290f, 1f);

        [Header("Scale Validation")]
        [SerializeField] private float minUniformScale = 0.75f;
        [SerializeField] private float maxUniformScale = 1.30f;

        [Header("Required Floor Office Sockets")]
        [SerializeField] private Transform workspaceRoot;
        [SerializeField] private Transform floorContact;
        [SerializeField] private Transform researchStationSocket;
        [SerializeField] private Transform developerStationSocket;
        [SerializeField] private Transform testerStationSocket;
        [SerializeField] private Transform factorySocket;
        [SerializeField] private Transform dispatchStationSocket;
        [SerializeField] private Transform statusBeaconSocket;
        [SerializeField] private Transform navigationBounds;
        [SerializeField] private Transform interactionPresentationSocket;
        [SerializeField] private Transform mailboxSocket;

        [Header("Visual Elements")]
        [SerializeField] private Renderer statusStripRenderer;
        [SerializeField] private BoxCollider navigationBoundsCollider;

        private MaterialPropertyBlock _mpb;
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        public Transform WorkspaceRoot => workspaceRoot;
        public Transform FloorContact => floorContact;
        public Transform ResearchStationSocket => researchStationSocket;
        public Transform DeveloperStationSocket => developerStationSocket;
        public Transform TesterStationSocket => testerStationSocket;
        public Transform FactorySocket => factorySocket;
        public Transform DispatchStationSocket => dispatchStationSocket;
        public Transform StatusBeaconSocket => statusBeaconSocket;
        public Transform NavigationBounds => navigationBounds;
        public Transform InteractionPresentationSocket => interactionPresentationSocket;
        public Transform MailboxSocket => mailboxSocket;
        public BoxCollider NavigationBoundsCollider => navigationBoundsCollider;

        public float MinUniformScale => minUniformScale;
        public float MaxUniformScale => maxUniformScale;

        private void Awake()
        {
            ResolveSockets();
        }

        public void ResolveSockets()
        {
            Transform[] all = GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                Transform t = all[i];
                switch (t.name)
                {
                    case "WorkspaceRoot":                 workspaceRoot = t; break;
                    case "FloorContact":                  floorContact = t; break;
                    case "ResearchStationSocket":         researchStationSocket = t; break;
                    case "DeveloperStationSocket":        developerStationSocket = t; break;
                    case "TesterStationSocket":           testerStationSocket = t; break;
                    case "FactorySocket":                 factorySocket = t; break;
                    case "DispatchStationSocket":         dispatchStationSocket = t; break;
                    case "StatusBeaconSocket":            statusBeaconSocket = t; break;
                    case "NavigationBounds":              navigationBounds = t; break;
                    case "InteractionPresentationSocket": interactionPresentationSocket = t; break;
                    case "MailboxSocket":                 mailboxSocket = t; break;
                }
            }

            if (navigationBounds != null && navigationBoundsCollider == null)
            {
                navigationBoundsCollider = navigationBounds.GetComponent<BoxCollider>();
            }

            if (statusStripRenderer == null)
            {
                Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
                for (int i = 0; i < renderers.Length; i++)
                {
                    if (renderers[i].name.Contains("StatusStrip"))
                    {
                        statusStripRenderer = renderers[i];
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// Reposition and uniformly scale the platform within the validated Quest 3 bounds.
        /// Preserves bottom-center FloorContact pivot.
        /// </summary>
        public float SetValidatedUniformScale(float requestedScale)
        {
            float clamped = Mathf.Clamp(requestedScale, minUniformScale, maxUniformScale);
            transform.localScale = Vector3.one * clamped;
            return clamped;
        }

        public void SetPoseOnFloor(Vector3 floorPosition, Quaternion yawRotation, float uniformScale = 1f)
        {
            transform.SetPositionAndRotation(floorPosition, yawRotation);
            SetValidatedUniformScale(uniformScale);
        }

        /// <summary>
        /// Tints the subtle embedded perimeter status strip using MaterialPropertyBlock.
        /// </summary>
        public void SetStatusStripColor(Color baseColor, float emissionIntensity = 0.25f)
        {
            if (statusStripRenderer == null) return;
            if (_mpb == null) _mpb = new MaterialPropertyBlock();
            statusStripRenderer.GetPropertyBlock(_mpb);
            _mpb.SetColor(BaseColorId, baseColor);
            _mpb.SetColor(EmissionColorId, baseColor * emissionIntensity);
            statusStripRenderer.SetPropertyBlock(_mpb);
        }
    }
}

