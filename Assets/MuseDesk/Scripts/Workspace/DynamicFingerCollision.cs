using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace MuseDesk.Workspace
{
    /// <summary>
    /// DYNAMIC finger collision (no recorded poses): each finger curls with the real tracked motion
    /// but STOPS on the surface of a grabbable, so the fingers wrap the outside of whatever shape they
    /// land on instead of passing through. Visual-only — overrides the ISDK HandVisual joint transforms
    /// in LateUpdate (after the tracked pose is written), which does NOT fight the grab transformer
    /// (that moves the object, not the hand) or tracking. Reflection-based so we keep no hard ISDK
    /// compile dependency. Only drives the two rendered hands (OVRHandVisualLeft/Right) and only casts
    /// against the grabbable layer, so it never clamps on the desk/room.
    /// Based on the researched Approach 1 (docs/DYNAMIC_GRAB_POSES.md Part II).
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(2000)]   // run LateUpdate after ISDK's own joint writers
    public class DynamicFingerCollision : MonoBehaviour
    {
        // Subscribes to ISDK HandVisual.WhenHandVisualUpdated so joint poses clamp right after
        // UpdateSkeleton() writes them, instead of flickering against LateUpdate ordering.
        public static bool GloballyEnabled = true;

        [Tooltip("Layer(s) the grabbable colliders live on. Keep narrow so casts are cheap and never hit the desk.")]
        public LayerMask grabbableMask = 1 << 8;
        [Tooltip("Approx finger-bone thickness for the sweeps (m).")]
        public float boneRadius = 0.008f;
        [Tooltip("Only solve when a grabbable is within this distance of the wrist (m).")]
        public float proximityGate = 0.16f;
        [Range(0f, 0.95f)] public float smoothing = 0.35f;
        [Tooltip("Only drive HandVisuals whose GameObject name starts with this (the rendered hands).")]
        public string handVisualNamePrefix = "OVRHandVisual";

        const string T_HandVisual = "Oculus.Interaction.HandVisual";
        const string T_HandJointId = "Oculus.Interaction.Input.HandJointId";

        static readonly string[][] FingerChains =
        {
            new[] { "HandThumb0", "HandThumb1", "HandThumb2", "HandThumb3", "HandThumbTip" },
            new[] { "HandIndex1", "HandIndex2", "HandIndex3", "HandIndexTip" },
            new[] { "HandMiddle1", "HandMiddle2", "HandMiddle3", "HandMiddleTip" },
            new[] { "HandRing1", "HandRing2", "HandRing3", "HandRingTip" },
            new[] { "HandPinky0", "HandPinky1", "HandPinky2", "HandPinky3", "HandPinkyTip" },
        };

        class HandRig
        {
            public Component handVisual;
            public MethodInfo getTransform;
            public object wristJointId;
            public object[][] fingerJointIds;
            public Dictionary<Transform, Quaternion> lastWorld = new Dictionary<Transform, Quaternion>();
            public Delegate handler;
        }

        readonly List<HandRig> _rigs = new List<HandRig>();
        Type _handJointIdType;
        EventInfo _whenUpdatedEvent;
        bool _ready;
        bool _usedEvent;
        bool _hooked;
        int _hits;
        float _logT;

        void Start()
        {
            try { Resolve(); }
            catch (Exception e) { Debug.LogError($"MDFINGER resolve failed: {e.Message}"); enabled = false; }
            if (!_ready) { enabled = false; return; }
            HookEvents();
        }

        void OnEnable()
        {
            if (_ready) HookEvents();
        }

        void OnDisable() => UnhookEvents();
        void OnDestroy() => UnhookEvents();

        void Resolve()
        {
            // Never allow an unconfigured all-layers mask to clamp fingers onto desks or room colliders.
            if (grabbableMask.value == ~0 || grabbableMask.value == 0)
            {
                int fc = LayerMask.NameToLayer("FingerCollide");
                if (fc >= 0) grabbableMask = 1 << fc;
            }

            var hvType = ResolveType(T_HandVisual);
            _handJointIdType = ResolveType(T_HandJointId);
            if (hvType == null || _handJointIdType == null) { Debug.LogError("MDFINGER ISDK types not found"); return; }

            var getT = hvType.GetMethod("GetTransformByHandJointId",
                BindingFlags.Instance | BindingFlags.Public, null, new[] { _handJointIdType }, null);
            if (getT == null) { Debug.LogError("MDFINGER GetTransformByHandJointId not found"); return; }

            _whenUpdatedEvent = hvType.GetEvent("WhenHandVisualUpdated", BindingFlags.Instance | BindingFlags.Public);
            if (_whenUpdatedEvent != null && _whenUpdatedEvent.EventHandlerType != typeof(Action))
                _whenUpdatedEvent = null;
            _usedEvent = _whenUpdatedEvent != null;

            object wrist = EnumVal("HandWristRoot");
            var chains = new object[FingerChains.Length][];
            for (int f = 0; f < FingerChains.Length; f++)
                chains[f] = FingerChains[f].Select(EnumVal).Where(v => v != null).ToArray();

            foreach (var obj in UnityEngine.Object.FindObjectsOfType(hvType, true).Cast<Component>())
            {
                if (!string.IsNullOrEmpty(handVisualNamePrefix) &&
                    !obj.gameObject.name.StartsWith(handVisualNamePrefix)) continue;
                var rig = new HandRig { handVisual = obj, getTransform = getT, wristJointId = wrist, fingerJointIds = chains };
                if (_usedEvent)
                    rig.handler = (Action)(() =>
                    {
                        if (_ready && GloballyEnabled && isActiveAndEnabled) SolveHand(rig);
                    });
                _rigs.Add(rig);
            }

            _ready = _rigs.Count > 0;
            Debug.Log($"MDFINGER ready={_ready} rendered-hands={_rigs.Count} mask={grabbableMask.value} hookedEvent={_usedEvent}");
        }

        void HookEvents()
        {
            if (_hooked || !_usedEvent || _whenUpdatedEvent == null) return;
            foreach (var rig in _rigs)
                if (rig.handVisual != null && rig.handler != null)
                    _whenUpdatedEvent.AddEventHandler(rig.handVisual, rig.handler);
            _hooked = true;
        }

        void UnhookEvents()
        {
            if (!_hooked || _whenUpdatedEvent == null) return;
            foreach (var rig in _rigs)
                if (rig.handVisual != null && rig.handler != null)
                    _whenUpdatedEvent.RemoveEventHandler(rig.handVisual, rig.handler);
            _hooked = false;
        }

        void LateUpdate()
        {
            // Fallback only when HandVisual.WhenHandVisualUpdated is unavailable.
            if (_usedEvent || !_ready || !GloballyEnabled) return;
            foreach (var rig in _rigs) SolveHand(rig);
        }

        void SolveHand(HandRig rig)
        {
            Transform wrist = GetJoint(rig, rig.wristJointId);
            if (wrist == null) return;
            if (proximityGate > 0f && !Physics.CheckSphere(wrist.position, proximityGate, grabbableMask, QueryTriggerInteraction.Collide))
                return;

            foreach (var jointIds in rig.fingerJointIds)
            {
                if (jointIds.Length < 2) continue;
                for (int k = 0; k < jointIds.Length - 1; k++)
                {
                    Transform j0 = GetJoint(rig, jointIds[k]);
                    Transform j1 = GetJoint(rig, jointIds[k + 1]);
                    if (j0 == null || j1 == null) continue;

                    Vector3 p0 = j0.position, p1 = j1.position, delta = p1 - p0;
                    float len = delta.magnitude;
                    if (len < 1e-5f) continue;
                    Vector3 dir = delta / len;

                    if (Physics.SphereCast(p0, boneRadius, dir, out RaycastHit hit, len, grabbableMask, QueryTriggerInteraction.Collide))
                    {
                        Vector3 desiredEnd = hit.point + hit.normal * boneRadius;
                        Vector3 desiredDir = desiredEnd - p0;
                        if (desiredDir.sqrMagnitude < 1e-8f) break;
                        desiredDir.Normalize();

                        Quaternion target = Quaternion.FromToRotation(dir, desiredDir) * j0.rotation;
                        if (smoothing > 0f && rig.lastWorld.TryGetValue(j0, out Quaternion prev))
                            target = Quaternion.Slerp(target, prev, smoothing);
                        j0.rotation = target;
                        rig.lastWorld[j0] = target;
                        break; // first contact freezes the rest of the finger
                    }
                }
            }
        }

        Transform GetJoint(HandRig rig, object boxedJointId)
            => boxedJointId == null ? null : rig.getTransform.Invoke(rig.handVisual, new[] { boxedJointId }) as Transform;

        object EnumVal(string member) { try { return Enum.Parse(_handJointIdType, member); } catch { return null; } }

        static Type ResolveType(string fullName)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                var t = asm.GetType(fullName, false);
                if (t != null) return t;
            }
            return null;
        }
    }
}
