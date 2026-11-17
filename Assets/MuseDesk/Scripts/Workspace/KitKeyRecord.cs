using TMPro;
using UnityEngine;

namespace MuseDesk.Workspace
{
    /// <summary>
    /// What a poke key looked like before the Kit builder (AgentScripts/BuildKitV3.cs) touched it,
    /// so that BuildKitV3.Revert() can put exactly that back. Data only: no behaviour, no Update.
    /// Written once, the first time the builder sees the key; later runs leave it alone.
    /// </summary>
    [DisallowMultipleComponent]
    public class KitKeyRecord : MonoBehaviour
    {
        [Tooltip("True once the 'before' values below have been captured.")]
        public bool captured;

        [Header("Before")]
        public Mesh capMesh;
        public Mesh housingMesh;
        public bool hasHousing;
        public Vector3 housingLocalPosition;

        [Tooltip("The key root and its wrapper ancestors, with their local scales before normalising.")]
        public Transform[] scaledTransforms;
        public Vector3[] scalesBefore;

        [Tooltip("Poke surface clippers (Oculus.Interaction.Surfaces.BoundsClipper) and their _size.")]
        public Component[] clippers;
        public Vector3[] clipperSizes;

        public BoxCollider[] colliders;
        public Vector3[] colliderSizes;

        public TMP_Text label;
        public Vector2 labelSize;
        public Vector3 labelScale;
        public Vector3 labelPosition;

        [Tooltip("Components that were enabled and that the builder disabled (colour visual, cap block editor).")]
        public Behaviour[] disabledByKit;

        public bool markerAddedByKit;
        public bool guardAddedByKit;

        [Header("Now")]
        [Tooltip("1 for a square key, otherwise the housing's width / height.")]
        public float wideRatio = 1f;
        public string meshSource = "";
    }
}
