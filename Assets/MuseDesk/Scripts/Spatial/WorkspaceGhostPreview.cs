using UnityEngine;
using MuseDesk.Art;
using MuseDesk.Workspace;

namespace MuseDesk.Spatial
{
    /// <summary>
    /// The placement preview, drawn where the office would go. It is only a preview: the office
    /// itself does not move until the user confirms.
    ///
    /// With the art assigned it is the PlacementGhostPreview_Floor model (floor spot) or the
    /// PlacementGhostPreview_Ceiling model (where the ceiling office comes down), sized by the
    /// builder to the real footprint, tinted teal / coral by the art-side
    /// PlacementGhostPreviewController and labelled in words (never colour alone): ready, not
    /// clear, or not checked against the room (a hand-placed spot). Without art it is the flat
    /// outline of quads the placement builder made, tinted teal or coral.
    /// Lives outside WorkspaceRoot, holds no interactables, and costs nothing while hidden.
    /// </summary>
    public class WorkspaceGhostPreview : MonoBehaviour
    {
        [Tooltip("Parent of the outline quads, authored in root-local layout at office scale 1. Used only when no art is assigned.")]
        public GameObject visual;
        [Tooltip("Renderers tinted teal (valid) or coral (not clear floor).")]
        public Renderer[] renderers;
        [Tooltip("Raise the outline this far above the surface so it does not z-fight the floor.")]
        public float lift = 0.004f;

        [Header("Art (optional; replaces the outline quads)")]
        [Tooltip("Holder of the PlacementGhostPreview_Floor model, laid out in root-local space.")]
        public GameObject floorArt;
        public PlacementGhostPreviewController floorArtController;
        [Tooltip("Holder of the PlacementGhostPreview_Ceiling model, laid out in root-local space. The floor model is used when empty.")]
        public GameObject ceilingArt;
        public PlacementGhostPreviewController ceilingArtController;
        public string floorReadyWords = "READY TO PLACE";
        public string ceilingReadyWords = "COMES DOWN HERE";
        public string notClearWords = "NOT CLEAR";
        public string notCheckedWords = "NOT CHECKED";

        MaterialPropertyBlock _block;
        bool _shown;
        bool _tinted;
        bool _lastValid, _lastChecked;
        GameObject _active;                 // the object currently switched on (art holder or the quads)
        PlacementGhostPreviewController _activeController;

        public bool Shown => _shown;
        /// <summary>True while one of the art models (not the fallback quads) is the preview.</summary>
        public bool ArtShown => _shown && _active != null && _active != visual;

        void Awake()
        {
            if (visual != null) visual.SetActive(false);
            if (floorArt != null) floorArt.SetActive(false);
            if (ceilingArt != null) ceilingArt.SetActive(false);
        }

        /// <summary>Show the floor preview at a candidate root pose (a spot checked against the room).</summary>
        public void Show(RootPose pose, bool valid) { Show(pose, valid, false, true); }

        /// <summary>Show the preview at a candidate root pose.</summary>
        /// <param name="ceiling">The ceiling office's landing spot (else a floor spot).</param>
        /// <param name="checkedAgainstRoom">False for a hand-placed spot: the words then say it was not checked.</param>
        public void Show(RootPose pose, bool valid, bool ceiling, bool checkedAgainstRoom)
        {
            transform.SetPositionAndRotation(pose.position + Vector3.up * lift, pose.rotation);
            transform.localScale = new Vector3(pose.scale, pose.scale, pose.scale);

            GameObject want = ceiling && ceilingArt != null ? ceilingArt : floorArt;
            PlacementGhostPreviewController controller = want == ceilingArt && ceilingArt != null ? ceilingArtController : floorArtController;
            if (want == null) { want = visual; controller = null; }

            bool swapped = want != _active || !_shown;
            if (swapped)
            {
                if (_active != null && _active != want) _active.SetActive(false);
                _active = want;
                _activeController = controller;
                _shown = true;
                if (want != null) want.SetActive(true);     // the art controller's Awake runs here the first time
            }

            if (swapped || !_tinted || valid != _lastValid || checkedAgainstRoom != _lastChecked)
            {
                _tinted = true;
                _lastValid = valid;
                _lastChecked = checkedAgainstRoom;
                if (_activeController != null)
                {
                    string words = !valid ? notClearWords
                                 : !checkedAgainstRoom ? notCheckedWords
                                 : (ceiling ? ceilingReadyWords : floorReadyWords);
                    _activeController.SetValid(valid, words);   // tint through a property block + the label
                }
                else
                {
                    if (_block == null) _block = new MaterialPropertyBlock();
                    Color c = valid ? OfficeTokens.Teal : OfficeTokens.Coral;
                    if (renderers != null)
                        for (int i = 0; i < renderers.Length; i++)
                            OfficeTokens.Tint(renderers[i], _block, c);
                }
            }
        }

        public void Hide()
        {
            if (!_shown) return;
            _shown = false;
            if (_active != null) _active.SetActive(false);
        }
    }
}
