using System;
using TMPro;
using UnityEngine;
using Oculus.Interaction;
using MuseDesk.Workspace;

namespace MuseDesk.Spatial
{
    /// <summary>
    /// Lets the user call the far office from the seat. Two ways, both ending in a
    /// <see cref="SummonRequests"/> event that the placement director acts on:
    ///
    ///  1. Near-user keys (always built; hands-first): "Bring it here" / "Send it back", and
    ///     "Status". They sit at the edge of the near-user area, exist only while the mode is not
    ///     Desk, and are shown and hidden by scale so their poke interactables stay registered.
    ///     The Bring key's words follow <see cref="SpatialWorkspaceState.IsRemote"/>, so they
    ///     change only after the placement side confirmed the move.
    ///  2. Hand ray + pinch (only if the rig has Interaction SDK ray interactors, in which case the
    ///     builder adds RayInteractables): selecting the attention beacon asks for the office;
    ///     selecting an agent asks for that agent's summary. No second interaction framework is
    ///     involved; without ray interactors these references stay empty and only the keys exist.
    ///
    /// In Desk mode everything here is hidden (keys at near-zero scale, ray targets at near-zero
    /// scale) and no request can be raised from it.
    /// </summary>
    public class SummonController : MonoBehaviour
    {
        [Header("Near-user keys")]
        [Tooltip("Holder of the keys (NOT under WorkspaceRoot). Shown and hidden by scale.")]
        public Transform keysRoot;
        public Vector3 keysShownScale = Vector3.one;
        [Tooltip("Label of the bring / send key.")]
        public TMP_Text bringLabel;
        [Tooltip("Offset from the near-user point: x = to the user's right, y = up, z = away from the user.")]
        public Vector3 keysOffset = new Vector3(-0.19f, -0.02f, 0.02f);
        [Tooltip("The keys move only after the near-user point moved this far (m).")]
        public float rePlaceDistance = 0.15f;

        [Header("Hand ray (optional)")]
        [Tooltip("RayInteractable on the attention beacon's office cue. Empty when the rig has no ray interactors.")]
        public RayInteractable beaconRayTarget;
        [Tooltip("RayInteractables that stand in for the agents, in the order of agentIds. May be empty.")]
        public RayInteractable[] agentRayTargets;
        public string[] agentIds;
        [Tooltip("Stations the agent ray targets follow, in the order of agentIds.")]
        public Transform[] stationAnchors;
        [Tooltip("Height of an agent ray target's centre above its station, in office-root metres.")]
        public float agentTargetHeight = 0.17f;

        const string BringWords = "Bring it\nhere";
        const string SendWords = "Send it\nback";

        readonly ScaleEase _ease = new ScaleEase();
        bool _eased, _wasAnimating, _placed, _targetsShown;
        Vector3 _placedAt;
        bool _labelIsBring;
        bool _labelSet;
        Action<InteractableStateChangeArgs> _beaconHandler;
        Action<InteractableStateChangeArgs>[] _agentHandlers;

        /// <summary>True while the near-user keys are shown.</summary>
        public bool KeysShown => _eased && _ease.Shown;

        void Awake()
        {
            if (keysRoot != null)
                _ease.Init(keysRoot, ScaleEase.ResolveFull(keysRoot, keysShownScale), 0.25f, false);
            _eased = true;
        }

        void OnEnable()
        {
            SpatialWorkspaceState.Changed += Refresh;
            if (beaconRayTarget != null)
            {
                if (_beaconHandler == null) _beaconHandler = OnBeaconRay;
                beaconRayTarget.WhenStateChanged += _beaconHandler;
            }
            if (agentRayTargets != null)
            {
                if (_agentHandlers == null || _agentHandlers.Length != agentRayTargets.Length)
                {
                    _agentHandlers = new Action<InteractableStateChangeArgs>[agentRayTargets.Length];
                    for (int i = 0; i < _agentHandlers.Length; i++)
                    {
                        int index = i;
                        _agentHandlers[i] = args => OnAgentRay(index, args);
                    }
                }
                for (int i = 0; i < agentRayTargets.Length; i++)
                    if (agentRayTargets[i] != null) agentRayTargets[i].WhenStateChanged += _agentHandlers[i];
            }
            Refresh();
        }

        void OnDisable()
        {
            SpatialWorkspaceState.Changed -= Refresh;
            if (beaconRayTarget != null && _beaconHandler != null) beaconRayTarget.WhenStateChanged -= _beaconHandler;
            if (agentRayTargets != null && _agentHandlers != null)
                for (int i = 0; i < agentRayTargets.Length && i < _agentHandlers.Length; i++)
                    if (agentRayTargets[i] != null) agentRayTargets[i].WhenStateChanged -= _agentHandlers[i];
        }

        void Update()
        {
            _ease.Tick(Time.unscaledDeltaTime);
            bool animating = _ease.Animating;
            // When the pop has finished, let the keys' press visuals re-read their resting offsets
            // at the final scale (PokeVisualRestGuard.SyncToCurrentScale, as the receipt does).
            if (_wasAnimating && !animating && _ease.Shown && keysRoot != null)
                keysRoot.BroadcastMessage("SyncToCurrentScale", SendMessageOptions.DontRequireReceiver);
            _wasAnimating = animating;

            if (keysRoot != null && _ease.Shown) PlaceKeys(false);
            if (_targetsShown) PlaceAgentTargets();
        }

        void Refresh()
        {
            if (!_eased) return;
            bool spatial = SpatialWorkspaceState.Mode != WorkspaceMode.Desk;
            bool remote = SpatialWorkspaceState.IsRemote;

            // Keys: only outside Desk mode, and not while the office is on the move.
            bool show = keysRoot != null && spatial && !SpatialWorkspaceState.IsTransitioning;
            if (show)
            {
                if (bringLabel != null && (!_labelSet || _labelIsBring != remote))
                {
                    bringLabel.text = remote ? BringWords : SendWords;
                    _labelIsBring = remote;
                    _labelSet = true;
                }
                if (!_ease.Shown) { _placed = false; PlaceKeys(true); }
                _ease.Show();
            }
            else _ease.Hide();

            // Agent ray targets are usable only while the office is remote.
            bool targets = remote && agentRayTargets != null && agentRayTargets.Length > 0;
            if (targets != _targetsShown)
            {
                _targetsShown = targets;
                float s = targets ? 1f : ScaleEase.HiddenFactor;
                for (int i = 0; i < agentRayTargets.Length; i++)
                    if (agentRayTargets[i] != null) agentRayTargets[i].transform.localScale = new Vector3(s, s, s);
                if (targets) PlaceAgentTargets();
            }
        }

        void PlaceKeys(bool force)
        {
            if (!RemoteNearUser.TryGetFrame(out Vector3 origin, out Vector3 right, out Vector3 away)) return;
            Vector3 pos = origin + right * keysOffset.x + Vector3.up * keysOffset.y + away * keysOffset.z;
            // Keys must hold still to be pressed: move only when the near-user area itself moved.
            if (!force && _placed && (pos - _placedAt).sqrMagnitude < rePlaceDistance * rePlaceDistance) return;
            _placed = true;
            _placedAt = pos;
            keysRoot.SetPositionAndRotation(pos, RemoteNearUser.FacingHead(pos, keysRoot.rotation));
        }

        void PlaceAgentTargets()
        {
            if (agentRayTargets == null || stationAnchors == null) return;
            Transform ws = SpatialWorkspaceState.WorkspaceRoot;
            float rootScale = ws != null ? Mathf.Abs(ws.lossyScale.y) : 1f;
            int n = Mathf.Min(agentRayTargets.Length, stationAnchors.Length);
            for (int i = 0; i < n; i++)
            {
                if (agentRayTargets[i] == null || stationAnchors[i] == null) continue;
                agentRayTargets[i].transform.position =
                    stationAnchors[i].position + Vector3.up * (agentTargetHeight * rootScale);
            }
        }

        // ── key targets (wired by the builder to the keys' select events) ──

        /// <summary>The bring / send key: asks for the office when it is remote, sends it back when
        /// it is near. Ignored in Desk mode and while the office is moving.</summary>
        public void PressBring()
        {
            if (SpatialWorkspaceState.Mode == WorkspaceMode.Desk || SpatialWorkspaceState.IsTransitioning) return;
            if (SpatialWorkspaceState.IsRemote) SummonRequests.RaiseBringOfficeHere();
            else SummonRequests.RaiseSendOfficeBack();
        }

        /// <summary>The Status key: asks for the latest update near the user.</summary>
        public void PressStatus()
        {
            if (SpatialWorkspaceState.Mode == WorkspaceMode.Desk) return;
            SummonRequests.RaiseShowLatestUpdate();
        }

        // ── hand ray ──

        void OnBeaconRay(InteractableStateChangeArgs args)
        {
            if (args.NewState != InteractableState.Select) return;
            if (!SpatialWorkspaceState.IsRemote || SpatialWorkspaceState.IsTransitioning) return;
            SummonRequests.RaiseBringOfficeHere();
        }

        void OnAgentRay(int index, InteractableStateChangeArgs args)
        {
            if (args.NewState != InteractableState.Select) return;
            if (!SpatialWorkspaceState.IsRemote) return;
            if (agentIds == null || index < 0 || index >= agentIds.Length) return;
            SummonRequests.RaiseAskAgent(agentIds[index]);
        }
    }
}
