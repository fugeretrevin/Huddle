using UnityEngine;

namespace MuseDesk.Agents
{
    /// <summary>
    /// A small additive rotation layered on ONE animated bone, applied in LateUpdate (after the
    /// Animator has written the clip pose). It never accumulates:
    ///   - if the Animator rewrote the bone this frame, the value we read is the clean clip pose;
    ///   - if it did not (the clip does not key this bone), the value is still exactly what we wrote
    ///     last frame, so we go back to the clean pose we remembered before adding to it again.
    /// Mutable struct: keep it in a field and call it on the field (never on a copy).
    /// </summary>
    public struct BoneRotationLayer
    {
        Quaternion _clean;
        Quaternion _written;
        bool _has;

        /// <summary>Rotate the bone by <paramref name="worldDelta"/> (a world-space rotation) on top of its clip pose.</summary>
        public void Apply(Transform bone, Quaternion worldDelta)
        {
            Quaternion cur = bone.localRotation;
            if (!_has || !Same(cur, _written)) _clean = cur;   // the Animator wrote a fresh pose
            bone.localRotation = _clean;
            bone.rotation = worldDelta * bone.rotation;
            _written = bone.localRotation;
            _has = true;
        }

        /// <summary>Remove our contribution (if it is still on the bone) and forget it.</summary>
        public void Release(Transform bone)
        {
            if (_has && bone != null && Same(bone.localRotation, _written)) bone.localRotation = _clean;
            _has = false;
        }

        static bool Same(Quaternion a, Quaternion b)
        {
            return a.x == b.x && a.y == b.y && a.z == b.z && a.w == b.w;
        }
    }

    /// <summary>Same idea as <see cref="BoneRotationLayer"/>, for a multiplicative local scale.</summary>
    public struct BoneScaleLayer
    {
        Vector3 _clean;
        Vector3 _written;
        bool _has;

        public void Apply(Transform bone, Vector3 multiplier)
        {
            Vector3 cur = bone.localScale;
            if (!_has || !Same(cur, _written)) _clean = cur;
            bone.localScale = Vector3.Scale(_clean, multiplier);
            _written = bone.localScale;
            _has = true;
        }

        public void Release(Transform bone)
        {
            if (_has && bone != null && Same(bone.localScale, _written)) bone.localScale = _clean;
            _has = false;
        }

        static bool Same(Vector3 a, Vector3 b)
        {
            return a.x == b.x && a.y == b.y && a.z == b.z;
        }
    }
}
