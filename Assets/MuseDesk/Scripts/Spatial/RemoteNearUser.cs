using UnityEngine;

namespace MuseDesk.Spatial
{
    /// <summary>
    /// Where "near the user" is, for everything the reach side brings to the seat (the hovering
    /// paper, the summon keys, the perch, the mirror cue). It prefers the placement side's
    /// <see cref="SpatialWorkspaceState.NearUserPose"/>; if that is not valid it falls back to a
    /// point in front of and below the head, so a delivered paper is always reachable.
    ///
    /// Orientation is always worked out from the head position (things turn their local -Z to
    /// the reader, as TextMeshPro text and poke keys do), never from the pose's rotation, so it
    /// does not depend on which way the placement side defines the pose's forward.
    /// </summary>
    public static class RemoteNearUser
    {
        /// <summary>Fallback distance in front of the head (m) when no near-user pose is published.</summary>
        public const float FallbackForward = 0.45f;
        /// <summary>Fallback drop below the eyes (m).</summary>
        public const float FallbackDrop = 0.22f;

        static Transform _head;
        static float _nextLookup;

        /// <summary>The head (main camera) transform, looked up at most twice a second until found.</summary>
        public static Transform Head()
        {
            if (_head != null) return _head;
            float now = Time.unscaledTime;
            if (_nextLookup > now + 1f) _nextLookup = 0f;      // play mode re-entered without a domain reload
            if (now < _nextLookup) return null;
            _nextLookup = now + 0.5f;
            var cam = Camera.main;
            if (cam != null) _head = cam.transform;
            return _head;
        }

        /// <summary>Use a specific transform as the head (Editor drivers and tests). Null clears it.</summary>
        public static void SetHead(Transform head) { _head = head; _nextLookup = 0f; }

        /// <summary>The point a delivered paper should come to rest at. False only when there is
        /// neither a published near-user pose nor a camera.</summary>
        public static bool TryGetTarget(out Vector3 position)
        {
            if (SpatialWorkspaceState.HasNearUserPose)
            {
                position = SpatialWorkspaceState.NearUserPose.position;
                return true;
            }
            Transform head = Head();
            if (head == null) { position = Vector3.zero; return false; }
            Vector3 fwd = head.forward; fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
            position = head.position + fwd.normalized * FallbackForward + Vector3.down * FallbackDrop;
            return true;
        }

        /// <summary>
        /// The near-user point and two level directions around it: <paramref name="right"/> is the
        /// user's right, <paramref name="away"/> points from the user past the point.
        /// </summary>
        public static bool TryGetFrame(out Vector3 origin, out Vector3 right, out Vector3 away)
        {
            right = Vector3.right; away = Vector3.forward;
            if (!TryGetTarget(out origin)) return false;
            Transform head = Head();
            Vector3 a;
            if (head != null) a = origin - head.position;
            else a = SpatialWorkspaceState.NearUserPose.rotation * Vector3.forward;
            a.y = 0f;
            if (a.sqrMagnitude < 1e-6f) a = Vector3.forward;
            away = a.normalized;
            right = Vector3.Cross(Vector3.up, away);
            return true;
        }

        /// <summary>Rotation that turns an object's local -Z (its printed / pokeable side) to the
        /// head. <paramref name="fallback"/> is returned when there is no head or the direction is
        /// degenerate.</summary>
        public static Quaternion FacingHead(Vector3 position, Quaternion fallback)
        {
            Transform head = Head();
            if (head == null) return fallback;
            Vector3 d = position - head.position;
            if (d.sqrMagnitude < 1e-6f) return fallback;
            d.Normalize();
            // Looking almost straight up or down: keep the text's top toward where the head faces.
            Vector3 up = Mathf.Abs(d.y) > 0.97f ? head.up : Vector3.up;
            return Quaternion.LookRotation(d, up);
        }

        /// <summary>True when every component is a finite number.</summary>
        public static bool IsFinite(Vector3 v)
        {
            return !(float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsNaN(v.z)
                  || float.IsInfinity(v.x) || float.IsInfinity(v.y) || float.IsInfinity(v.z));
        }
    }
}
