using System;
using UnityEngine;

namespace MuseDesk.Spatial.Wall
{
    // rc22 (owner, worn rc21): Select Agents "should be cleaner": the grab keeps hold, the agent lands where it is let go,
    // and Select mode switches itself off when the user does something else. Pure logic, no scene access, so the EditMode
    // tests reach it. The runtime (WallHabitatController.Interaction) feeds it rig-local points and the arbiter's settings.
    //
    //   DeskDropRule ..... where a released projection lands on the desk (rig space: +Z toward the user, desk top y = 0):
    //                      under the release point, pulled in from just off the desk, never nearer the user than the body
    //                      keep-out, never on the dock keys; out of range = the previous rule (the pad, or home).
    //   CarryGrip ........ while an agent is carried, the hand arbiter's release needs a longer seen-open pinch and a lost
    //                      hand is held longer (frozen); given back the moment the carry ends.
    //   SelectModeExit ... "the user did something else" (another key, a grab, a panel opened): armed Select switches off.

    [Serializable]
    public sealed class DeskDropSettings
    {
        [Tooltip("Off = the previous rule: the agent always lands on the fixed teal pad (or goes home).")]
        public bool enabled = true;
        [Tooltip("Half the usable desk width either side of the user's centre line (m, rig X). The pad is always inside.")]
        public float halfWidth = 0.42f;
        [Tooltip("The far edge of the usable desk (rig Z, toward the wall). The pad is always inside.")]
        public float farZ = 0.08f;
        [Tooltip("The near edge of the usable desk (rig Z, toward the user). The pad is always inside.")]
        public float nearZ = 0.48f;
        [Tooltip("Never nearer the user than this, horizontally in front of the eye (m): the seated body keep-out.")]
        public float bodyClearance = 0.14f;
        [Tooltip("A release further than this from the eye is not a desk placement (m). An arm stretched toward the wall puts the " +
                 "carry point ~0.7-0.8 m out (a put-back); a release over the desk is 0.35-0.6 m.")]
        public float reach = 0.66f;
        [Tooltip("A release this far outside the usable desk (m, horizontally) is pulled onto its edge; further is out of range.")]
        public float edgeMargin = 0.10f;
        [Tooltip("A release higher than this above the desk top is not a desk placement (m).")]
        public float maxAboveDesk = 0.45f;
        [Tooltip("A release lower than this under the desk top is not a desk placement (m).")]
        public float maxBelowDesk = 0.10f;
        [Tooltip("The agent never lands within this of the dock keys' centre (m, horizontally): it is moved out to this ring.")]
        public float dockClearRadius = 0.13f;
        [Tooltip("A landing this close to the pad lands exactly on it (m).")]
        public float snapToPad = 0.035f;
    }

    public enum DeskDropKind { Home = 0, Pad = 1, Spot = 2 }

    public struct DeskDropResult
    {
        public DeskDropKind kind;
        /// <summary>Where it lands (rig space, on the desk top). The pad for <see cref="DeskDropKind.Pad"/> and Home.</summary>
        public Vector3 spot;
        /// <summary>Why it was not placed where it was let go (null when it was).</summary>
        public string why;
        /// <summary>The release was pulled onto the usable desk (clamped) or moved off the dock.</summary>
        public bool adjusted;
    }

    public static class DeskDropRule
    {
        /// <summary>
        /// Where a released projection lands. All points in the near-user rig's space (metres; +Z toward the user, desk top
        /// y = 0). <paramref name="legacy"/> is the previous rule's answer (<see cref="VisitDropRule.Decide"/>), used when the
        /// release is not a desk placement. A desk placement needs ALL of: height within the desk window, within reach of the
        /// eye, nearer the eye than the agent's wall home (never on the habitat), and within edgeMargin of the usable desk
        /// (which always contains the pad and never comes nearer the user than eye.z - bodyClearance). The spot is the release
        /// point dropped onto the desk top and pulled inside the usable desk, then moved out of the dock's ring. A spot that
        /// cannot be cleared of the dock lands on the pad.
        /// </summary>
        public static DeskDropResult Decide(Vector3 release, Vector3 pad, Vector3 eye, Vector3 home, bool hasDock, Vector3 dock,
                                            VisitDrop legacy, DeskDropSettings s)
        {
            if (s == null || !s.enabled) return Fallback(legacy, pad, "free drop is off");
            float h = release.y - pad.y;
            if (h > s.maxAboveDesk) return Fallback(legacy, pad, "let go too high above the desk");
            if (h < -s.maxBelowDesk) return Fallback(legacy, pad, "let go under the desk");
            float toEye = (release - eye).magnitude;
            if (toEye > s.reach) return Fallback(legacy, pad, "out of reach");
            if (toEye >= (release - home).magnitude) return Fallback(legacy, pad, "nearer the wall than you");

            float minX, maxX, minZ, maxZ;
            Area(pad, eye, s, out minX, out maxX, out minZ, out maxZ);
            float ox = Outside(release.x, minX, maxX), oz = Outside(release.z, minZ, maxZ);
            if (ox * ox + oz * oz > s.edgeMargin * s.edgeMargin) return Fallback(legacy, pad, "off the desk");

            Vector2 c = new Vector2(Mathf.Clamp(release.x, minX, maxX), Mathf.Clamp(release.z, minZ, maxZ));
            bool adjusted = ox > 0f || oz > 0f;
            if (hasDock && s.dockClearRadius > 0f)
            {
                Vector2 d0 = new Vector2(dock.x, dock.z);
                Vector2 d = c - d0;
                float r = s.dockClearRadius;
                if (d.sqrMagnitude < r * r)
                {
                    Vector2 dir = d.sqrMagnitude > 1e-8f ? d.normalized : new Vector2(pad.x - dock.x, pad.z - dock.z);
                    if (dir.sqrMagnitude < 1e-8f) dir = Vector2.left;
                    dir.Normalize();
                    c = d0 + dir * r;
                    c = new Vector2(Mathf.Clamp(c.x, minX, maxX), Mathf.Clamp(c.y, minZ, maxZ));
                    adjusted = true;
                    if ((c - d0).sqrMagnitude < r * r * 0.998f)
                        return new DeskDropResult { kind = DeskDropKind.Pad, spot = pad, why = "too close to the controls", adjusted = true };
                }
            }
            Vector3 spot = new Vector3(c.x, pad.y, c.y);
            if ((spot - pad).sqrMagnitude <= s.snapToPad * s.snapToPad)
                return new DeskDropResult { kind = DeskDropKind.Pad, spot = pad, why = null, adjusted = adjusted };
            return new DeskDropResult { kind = DeskDropKind.Spot, spot = spot, why = null, adjusted = adjusted };
        }

        /// <summary>The usable desk rectangle (rig X/Z). It always contains the pad; its near edge is the nearer of nearZ and
        /// the body keep-out line (eye.z - bodyClearance), but never nearer the wall than the pad.</summary>
        public static void Area(Vector3 pad, Vector3 eye, DeskDropSettings s, out float minX, out float maxX, out float minZ, out float maxZ)
        {
            minX = Mathf.Min(-Mathf.Abs(s.halfWidth), pad.x);
            maxX = Mathf.Max(Mathf.Abs(s.halfWidth), pad.x);
            minZ = Mathf.Min(s.farZ, pad.z);
            maxZ = Mathf.Max(pad.z, Mathf.Min(s.nearZ, eye.z - s.bodyClearance));
            if (maxZ < minZ) maxZ = minZ;
        }

        static float Outside(float v, float lo, float hi) { return v < lo ? lo - v : v > hi ? v - hi : 0f; }

        static DeskDropResult Fallback(VisitDrop legacy, Vector3 pad, string why)
        {
            return new DeskDropResult { kind = legacy == VisitDrop.Perch ? DeskDropKind.Pad : DeskDropKind.Home, spot = pad, why = why };
        }

        /// <summary>Where the visit card stands for an agent at <paramref name="spot"/>: the same offset it has from the pad,
        /// mirrored to the other side when the agent is on the other side of the user's centre line (it stays outboard).</summary>
        public static Vector3 CardFor(Vector3 spot, Vector3 pad, Vector3 cardAtPad)
        {
            Vector3 off = cardAtPad - pad;
            if ((spot.x < 0f) != (pad.x < 0f)) off.x = -off.x;
            return spot + off;
        }
    }

    /// <summary>
    /// The carry's grip on the hand arbiter. While carrying, a claimed pinch must read open (on a TRACKED hand) for
    /// carryReleaseSeconds to count as a release (a 0.1 s pinch drop-out keeps the agent), and a lost hand is frozen for
    /// carryLostSeconds before the carry is cancelled (back pinching = carry on; back open = cancelled, as before: a lost hand
    /// is never a release). The arbiter's own values come back the moment the carry ends. Never shortens a value.
    /// </summary>
    public sealed class CarryGrip
    {
        float _baseRelease, _baseLost;
        public bool Applied { get; private set; }

        public void Apply(HandOwnershipSettings s, bool carrying, AgentSelectGrabSettings g)
        {
            if (s == null) return;
            if (carrying && g != null)
            {
                if (!Applied) { _baseRelease = s.releaseDebounce; _baseLost = s.lostGraceSeconds; Applied = true; }
                s.releaseDebounce = Mathf.Max(_baseRelease, g.carryReleaseSeconds);
                s.lostGraceSeconds = Mathf.Max(_baseLost, g.carryLostSeconds);
                return;
            }
            if (!Applied) return;
            s.releaseDebounce = _baseRelease;
            s.lostGraceSeconds = _baseLost;
            Applied = false;
        }
    }

    /// <summary>
    /// Select mode leaves when the user does something else (owner, worn rc21). Key paths call <see cref="Raise"/> before
    /// the key acts (so, e.g., Move is not refused because Select was armed); the Wall controller listens. A grab, the plan
    /// paper, the Meet chooser, the screen panel or a card drag starting is checked per frame as a rising edge. Only ARMED
    /// Select is switched off: never the pick itself, never a carry.
    /// </summary>
    public static class SelectModeExit
    {
        public const string OffNote = "Select off";

        /// <summary>Something other than Select was pressed (the key's name). Raised before the key acts.</summary>
        public static event Action<string> OtherAction;
        public static int Raised { get; private set; }
        public static string Last { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { OtherAction = null; Raised = 0; Last = null; }

        public static void Raise(string what)
        {
            Raised++;
            Last = what;
            Action<string> h = OtherAction;
            if (h == null) return;
            try { h(what); } catch (Exception e) { Debug.LogException(e); }
        }

        /// <summary>The Select key itself (the Spaces dock's "Select", the fallback dock's "Key_Select").</summary>
        public static bool IsSelectControl(string what)
        {
            return string.Equals(what, "Select", StringComparison.OrdinalIgnoreCase) || string.Equals(what, "Key_Select", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>A key press switches Select off when Select is armed and the key is not Select.</summary>
        public static bool ShouldDisarm(bool armed, string what) { return armed && !IsSelectControl(what); }

        /// <summary>Per frame: something else BEGAN (false -> true) while Select is armed. Something that was already going on
        /// when Select was armed does not switch it off.</summary>
        public static bool EdgeTrips(bool armed, bool wasBusy, bool isBusy) { return armed && isBusy && !wasBusy; }
    }
}
