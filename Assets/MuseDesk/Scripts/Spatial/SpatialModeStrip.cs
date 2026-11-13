using TMPro;
using UnityEngine;

namespace MuseDesk.Spatial
{
    /// <summary>
    /// The compact, contextual strip of keys for the workspace modes. Three slots; which key sits
    /// in a slot depends on the state, and keys that do not apply are hidden by scale (never
    /// SetActive, so their poke interactables stay registered) and moved a few centimetres away so
    /// a hidden key cannot be pressed through a shown one:
    ///
    ///   both flags off .......... nothing (the idle scene has no extra keys)
    ///   Desk, idle .............. [Floor] [Ceiling]            (each only if its flag is on)
    ///   placement preview ....... [Next]  [Confirm] [Cancel]
    ///   Floor ................... [Desk]
    ///   Ceiling ................. [Desk]  [Lower / Raise]
    ///
    /// The strip is NOT a child of WorkspaceRoot: in Desk mode (and when the ceiling office is down
    /// beside the user) it sits at a fixed spot beside the Focus key; while the office is remote or
    /// moving it stays within reach at the published near-user pose, so the user can always get back.
    /// A one-line status plate above the keys says what is being offered, in words.
    /// </summary>
    public class SpatialModeStrip : MonoBehaviour
    {
        [Header("Anchors")]
        public Transform workspaceRoot;
        [Tooltip("Strip position in WorkspaceRoot-local space (Desk mode).")]
        public Vector3 deskLocalPosition;
        [Tooltip("Strip rotation in WorkspaceRoot-local space (Desk mode), Euler degrees.")]
        public Vector3 deskLocalEuler;
        [Tooltip("Strip offset from the near-user pose while the office is remote (its -X is the user's right).")]
        public Vector3 nearUserOffset = new Vector3(-0.20f, -0.03f, 0.02f);

        [Header("Keys (clones of FocusToggle)")]
        public Transform floorKey;
        public Transform ceilingKey;
        public Transform deskKey;
        public Transform nextKey;
        public Transform confirmKey;
        public Transform cancelKey;
        public Transform podKey;
        public TMP_Text podKeyLabel;
        [Tooltip("Uniform scale of a shown key.")]
        public float keyScale = 0.04f;
        public Vector3 slot0;
        public Vector3 slot1;
        public Vector3 slot2;
        [Tooltip("Where a hidden key is parked, relative to its slot.")]
        public Vector3 hiddenOffset = new Vector3(0f, -0.06f, 0.03f);

        [Header("Status line")]
        public Transform statusPlate;
        public Vector3 statusPlateScale = Vector3.one;
        public TMP_Text statusLabel;

        const float HiddenFactor = 0.001f;   // same as ScaleEase.HiddenFactor: never zero

        bool _anyShown;
        bool _followRoot = true;
        Quaternion _deskLocalRot = Quaternion.identity;
        string _status;
        string _podText;

        void Awake()
        {
            _deskLocalRot = Quaternion.Euler(deskLocalEuler);
            HideAll();
        }

        /// <summary>Hide every key and the status line (both flags off, or before the first refresh).</summary>
        public void HideAll()
        {
            _anyShown = false;
            Place(floorKey, slot0, false);
            Place(ceilingKey, slot1, false);
            Place(deskKey, slot0, false);
            Place(nextKey, slot0, false);
            Place(confirmKey, slot1, false);
            Place(cancelKey, slot2, false);
            Place(podKey, slot1, false);
            ShowStatus(null);
        }

        /// <summary>Lay the strip out for the current state. Called on state changes only.</summary>
        public void Apply(bool floorEnabled, bool ceilingEnabled, PlacementMode mode, bool previewing, bool canCycle,
                          bool transitioning, CeilingPodState podState, bool podWaiting, string status)
        {
            if (!floorEnabled && !ceilingEnabled && mode == PlacementMode.Desk && !previewing)
            {
                HideAll();
                return;
            }
            _anyShown = true;

            bool idleDesk = mode == PlacementMode.Desk && !previewing && !transitioning;
            Place(floorKey, slot0, idleDesk && floorEnabled);
            Place(ceilingKey, slot1, idleDesk && ceilingEnabled);

            Place(nextKey, slot0, previewing && canCycle);
            Place(confirmKey, slot1, previewing);
            Place(cancelKey, slot2, previewing);

            bool away = mode != PlacementMode.Desk && !previewing;
            Place(deskKey, slot0, away && !transitioning);
            bool podDown = podState == CeilingPodState.Engaged;
            bool podUp = podState == CeilingPodState.Docked;
            Place(podKey, slot1, away && mode == PlacementMode.Ceiling && !transitioning && (podDown || podUp));
            string podText = podDown ? (podWaiting ? "Waiting" : "Raise") : "Lower";
            if (podKeyLabel != null && podText != _podText)
            {
                _podText = podText;
                podKeyLabel.text = podText;
            }

            ShowStatus(status);
        }

        /// <summary>Where the strip sits: beside the Focus key (true) or at the near-user pose (false).</summary>
        public void SetFollowRoot(bool followRoot) { _followRoot = followRoot; }

        void Place(Transform key, Vector3 slot, bool shown)
        {
            if (key == null) return;
            key.localPosition = shown ? slot : slot + hiddenOffset;
            float s = shown ? keyScale : keyScale * HiddenFactor;
            key.localScale = new Vector3(s, s, s);
        }

        void ShowStatus(string status)
        {
            bool show = !string.IsNullOrEmpty(status);
            if (statusPlate != null) statusPlate.localScale = show ? statusPlateScale : statusPlateScale * HiddenFactor;
            if (statusLabel != null && status != _status)
            {
                _status = status;
                statusLabel.text = show ? status : "";
            }
        }

        void LateUpdate()
        {
            if (!_anyShown) return;                       // flags off: do nothing at all
            if (_followRoot || !SpatialWorkspaceState.HasNearUserPose)
            {
                if (workspaceRoot == null) return;
                transform.SetPositionAndRotation(workspaceRoot.TransformPoint(deskLocalPosition), workspaceRoot.rotation * _deskLocalRot);
            }
            else
            {
                Pose near = SpatialWorkspaceState.NearUserPose;
                transform.SetPositionAndRotation(near.position + near.rotation * nearUserOffset, near.rotation * _deskLocalRot);
            }
        }
    }
}
