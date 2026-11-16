using System.Collections.Generic;
using UnityEngine;
using Oculus.Interaction;
using Oculus.Interaction.HandGrab;
using MuseDesk.Spatial.Wall;

namespace MuseDesk.Wall
{
    /// <summary>
    /// AgentSelectGrabV1: keeps invisible or competing Interaction SDK interactables from eating the Select Agents pinch.
    ///
    ///  - Hidden office: in Wall mode the desk office is hidden by switching its renderers off only
    ///    (<see cref="WorkspaceVisualSuppressor"/>); its grab, distance-grab, ray and poke interactables stayed live and
    ///    invisible between the seated user and the wall. A hand ray or distance-grab cone crossing them marked the hand
    ///    busy (HandInteractionProbe), so the arbiter spent every pinch aimed at a wall agent, or the pinch grabbed an
    ///    invisible card. While Wall is active these are switched off (component <c>enabled</c>, the SDK's own
    ///    unregister path; nothing is deactivated or moved). Anything under a never-hide root is left alone.
    ///  - Distance grabs: while Select Agents is armed or carrying, the visible distance-grab interactables (the stamps,
    ///    the paper) are switched off too, so a far pinch means "agent". Near grabs and pokes stay on and still win.
    ///
    /// Only what this component switched off is switched back on (<see cref="EnableLedger{T}"/>); anything selected
    /// (held) is never touched. Everything is given back when Wall ends, the owner is disabled, or this is destroyed.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AgentSelectInteractableMute : MonoBehaviour
    {
        public WallHabitatController owner;
        [Tooltip("Seconds between rescans (new cards appear, the paper moves under a never-hide root).")]
        public float rescanSeconds = 0.5f;

        readonly EnableLedger<Behaviour> _office = new EnableLedger<Behaviour>();
        readonly EnableLedger<Behaviour> _distance = new EnableLedger<Behaviour>();
        readonly List<MonoBehaviour> _scratch = new List<MonoBehaviour>(256);
        Transform _root;
        Transform[] _exclude;
        bool _officeOn, _distanceOn;
        float _nextOffice, _nextDistance;

        public int OfficeMuted => _office.Count;
        public int DistanceMuted => _distance.Count;
        public bool OfficeActive => _officeOn;
        public bool DistanceActive => _distanceOn;

        static bool IsOn(Behaviour b) { return b != null && b.enabled; }
        static void SetOn(Behaviour b, bool on) { if (b != null) b.enabled = on; }
        static bool IsBusy(Behaviour b)
        {
            var v = b as IInteractableView;
            return v != null && v.State == InteractableState.Select;          // held: never pulled out from under a hand
        }

        /// <summary>Mute (true) or give back (false) the hidden office's interactables.</summary>
        public void SetOffice(bool mute, Transform root, Transform[] exclude)
        {
            if (!mute)
            {
                if (_officeOn || _office.Count > 0) _office.RestoreAll(SetOn);
                _officeOn = false;
                return;
            }
            if (!_officeOn || root != _root) { _nextOffice = 0f; }
            _officeOn = true;
            _root = root;
            _exclude = exclude;
            if (Time.unscaledTime >= _nextOffice) ScanOffice();
        }

        /// <summary>Mute (true) or give back (false) the distance-grab interactables of the whole scene.</summary>
        public void SetDistance(bool mute)
        {
            if (!mute)
            {
                if (_distanceOn || _distance.Count > 0) _distance.RestoreAll(SetOn);
                _distanceOn = false;
                return;
            }
            if (!_distanceOn) _nextDistance = 0f;
            _distanceOn = true;
            if (Time.unscaledTime >= _nextDistance) ScanDistance();
        }

        public void RestoreAll()
        {
            _office.RestoreAll(SetOn);
            _distance.RestoreAll(SetOn);
            _officeOn = _distanceOn = false;
        }

        void ScanOffice()
        {
            _nextOffice = Time.unscaledTime + rescanSeconds;
            if (_root == null) return;
            // Something that left the office or moved under a never-hide root (the paper) is given back first.
            _office.Restore(b => b == null || !b.transform.IsChildOf(_root) || Excluded(b.transform), SetOn);
            _root.GetComponentsInChildren(true, _scratch);
            for (int i = 0; i < _scratch.Count; i++)
            {
                MonoBehaviour mb = _scratch[i];
                if (mb == null || !(mb is IInteractable) || Excluded(mb.transform)) continue;
                _office.Mute(mb, IsOn, IsBusy, SetOn);
            }
            _scratch.Clear();
        }

        void ScanDistance()
        {
            _nextDistance = Time.unscaledTime + rescanSeconds;
            foreach (var d in FindObjectsByType<DistanceHandGrabInteractable>(FindObjectsSortMode.None)) _distance.Mute(d, IsOn, IsBusy, SetOn);
            foreach (var d in FindObjectsByType<DistanceGrabInteractable>(FindObjectsSortMode.None)) _distance.Mute(d, IsOn, IsBusy, SetOn);
        }

        bool Excluded(Transform t)
        {
            if (_exclude == null) return false;
            for (int i = 0; i < _exclude.Length; i++) if (_exclude[i] != null && t.IsChildOf(_exclude[i])) return true;
            return false;
        }

        void Update()
        {
            // The owner stops ticking when the wall flag goes off or it is disabled: give everything back from here.
            if (owner == null || !owner.isActiveAndEnabled || !owner.WallActive) { if (_office.Count > 0 || _distance.Count > 0 || _officeOn || _distanceOn) RestoreAll(); }
        }

        void OnDisable() { RestoreAll(); }
        void OnDestroy() { RestoreAll(); }
    }
}
