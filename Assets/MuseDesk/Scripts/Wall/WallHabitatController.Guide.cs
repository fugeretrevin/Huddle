using UnityEngine;
using MuseDesk.Spatial;
using MuseDesk.Spatial.Wall;

namespace MuseDesk.Wall
{
    /// <summary>
    /// The guided card for Wall placement (rc9, owner: "the user should get to choose which mode to do and have the
    /// onboarding placing process"). Floor and Ceiling already had a look-at card (<see cref="PlacementGuide"/>); the
    /// wall's only instruction was the dock's status line. Now the same card shows from the moment the wall preview
    /// opens (<see cref="RequestWall"/>) until Place, Cancel or leaving Wall:
    ///   step 1 "Look at a wall"              the ring fills while the gaze rests on a wall spot the evaluator accepts
    ///   step 2 "Point and pinch to move it"  then press Place (the dock's Place / Next spot / Cancel keys; the card has none)
    ///
    /// This file only answers the card's questions (<see cref="IWallGuideSource"/>). Every answer is read-only except
    /// <see cref="IWallGuideSource.WallGuideAimAt"/>, which moves the preview's outline exactly as a pinch-and-point aim
    /// does (<see cref="WallModeFlow.Aim"/>): nothing is placed until Place.
    /// </summary>
    public partial class WallHabitatController : IWallGuideSource
    {
        [Header("Guide card (rc9: the same look-at card as Floor and Ceiling)")]
        [Tooltip("The scene's PlacementGuide (LookGuide). Found when empty; none = no card, the dock's status line still guides.")]
        public PlacementGuide guide;

        PlacementGuide ResolveGuide()
        {
            if (guide == null) guide = PlacementGuide.Find();
            return guide;
        }

        /// <summary>The wall preview just opened: show the card at step 1.</summary>
        void BeginGuideCard()
        {
            PlacementGuide g = ResolveGuide();
            if (g != null && WallPreviewing) g.BeginWall(this);
        }

        /// <summary>Place, Cancel or leaving Wall: hide the card (no-op when it is not on the wall).</summary>
        void EndGuideCard()
        {
            PlacementGuide g = ResolveGuide();
            if (g != null) g.EndWall();
        }

        // ── IWallGuideSource ──

        bool IWallGuideSource.WallGuidePreviewOpen => WallPreviewing;

        bool IWallGuideSource.TryGetWallGuideSpot(out Vector3 center, out Quaternion rotation, out Vector3 halfExtents, out bool valid)
        {
            if (_flow == null || !WallPreviewing || !_flow.HasShown)
            {
                center = Vector3.zero; rotation = Quaternion.identity; halfExtents = Vector3.zero; valid = false;
                return false;
            }
            WallCandidate c = _flow.Shown;
            center = c.VolumeCenter;
            rotation = c.rotation;
            halfExtents = c.size * 0.5f;
            valid = c.valid;
            return true;
        }

        bool IWallGuideSource.WallGuideGazeAccepted(Vector3 origin, Vector3 direction)
        {
            if (_flow == null || !WallPreviewing) return false;
            WallCandidate c;
            return WallPlacementEvaluator.FromRay(roomService != null ? roomService.Room : null, roomService != null ? roomService.Walls : null,
                                                  origin, direction, _flow.Layout, ReadUser(), placement, out c)
                   && c.valid;
        }

        bool IWallGuideSource.WallGuideAimAt(Vector3 origin, Vector3 direction)
        {
            if (_flow == null || !WallPreviewing) return false;
            if (!_flow.Aim(origin, direction)) return false;
            ShowGhost();
            return true;
        }

        /// <summary>Play Mode driver: the spot the outline shows (centre of the habitat volume), for aiming an injected gaze.</summary>
        public bool TryGetShownSpotForDriver(out Vector3 volumeCenter, out bool valid)
        {
            Quaternion r; Vector3 he;
            return ((IWallGuideSource)this).TryGetWallGuideSpot(out volumeCenter, out r, out he, out valid);
        }
    }
}
