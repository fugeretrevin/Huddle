using UnityEngine;

namespace MuseDesk.Art
{
    /// <summary>
    /// Shared scale-safety and socket-mounting helpers for MuseDesk's spatial expansion.
    /// Guarantees that existing character rigs, workstations, factory models, and documents
    /// preserve their exact intended world scale when mounted onto scalable platforms or pods.
    /// </summary>
    public static class SpatialScaleUtility
    {
        /// <summary>
        /// Parents <paramref name="child"/> under <paramref name="socket"/> while preserving
        /// <paramref name="desiredWorldScale"/> regardless of parent/platform lossyScale.
        /// </summary>
        public static void MountPreservingWorldScale(
            Transform child,
            Transform socket,
            Vector3 desiredWorldScale,
            bool resetLocalPose = true)
        {
            if (child == null || socket == null) return;

            child.SetParent(socket, false);
            if (resetLocalPose)
            {
                child.localPosition = Vector3.zero;
                child.localRotation = Quaternion.identity;
            }

            Vector3 parentScale = socket.lossyScale;
            child.localScale = new Vector3(
                SafeDiv(desiredWorldScale.x, parentScale.x),
                SafeDiv(desiredWorldScale.y, parentScale.y),
                SafeDiv(desiredWorldScale.z, parentScale.z));
        }

        /// <summary>
        /// Captures a child's current world scale before reparenting and maintains that exact
        /// world scale after mounting under <paramref name="socket"/>.
        /// </summary>
        public static void MountKeepingCurrentWorldScale(Transform child, Transform socket, bool resetLocalPose = true)
        {
            if (child == null || socket == null) return;
            Vector3 currentWorldScale = child.lossyScale;
            MountPreservingWorldScale(child, socket, currentWorldScale, resetLocalPose);
        }

        private static float SafeDiv(float num, float den)
        {
            return Mathf.Abs(den) > 1e-5f ? num / Mathf.Abs(den) : num;
        }
    }
}

