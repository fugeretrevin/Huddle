using UnityEngine;

namespace MuseDesk.Agents
{
    // Pure logic of the "characters respond to the person" pass. No scene access, no MonoBehaviour,
    // no allocation after construction. Lives in its own assembly (MuseDesk.Agents.Core) so the
    // EditMode tests can reference it; the runtime (Assembly-CSharp) references it automatically.

    /// <summary>What an agent is doing, as the status sentence builder needs it. The runtime maps
    /// WorkstationStatusDisplay.Classify (the one shared state-to-activity mapping) onto this.</summary>
    public enum AgentStatusKind
    {
        Idle, Researching, Coding, Testing, Reporting, Approaching,
        Waiting, WaitingForUser, Complete, Error
    }

    /// <summary>
    /// Builds the short sentence a character "says" about itself. Every sentence is a function of
    /// the true run state passed in (the reducer's agent state, whether a decision from this agent
    /// awaits the user, whether a run is in flight); nothing is generated, in scripted and live
    /// mode alike. All results are constant strings or a concatenation done on demand (never per
    /// frame). ASCII only.
    /// </summary>
    public static class AgentStatusSentence
    {
        public const string Researcher = "Researcher";
        public const string Developer = "Developer";
        public const string Tester = "Tester";

        /// <summary>"researcher" / "Researcher" / " TESTER " to the canonical role name, or null.</summary>
        public static string NormalizeRole(string role)
        {
            if (string.IsNullOrEmpty(role)) return null;
            string r = role.Trim();
            if (string.Equals(r, Researcher, System.StringComparison.OrdinalIgnoreCase)) return Researcher;
            if (string.Equals(r, Developer, System.StringComparison.OrdinalIgnoreCase)) return Developer;
            if (string.Equals(r, Tester, System.StringComparison.OrdinalIgnoreCase)) return Tester;
            return null;
        }

        /// <summary>Position of a role in the fixed pipeline (research, build, test): 1..3, or 0.</summary>
        public static int StepOf(string role)
        {
            string r = NormalizeRole(role);
            if (r == Researcher) return 1;
            if (r == Developer) return 2;
            if (r == Tester) return 3;
            return 0;
        }

        public static bool IsWorking(AgentStatusKind kind)
        {
            return kind == AgentStatusKind.Researching || kind == AgentStatusKind.Coding
                || kind == AgentStatusKind.Testing || kind == AgentStatusKind.Reporting;
        }

        static string WorkWords(AgentStatusKind kind)
        {
            switch (kind)
            {
                case AgentStatusKind.Researching: return "Reading the brief.";
                case AgentStatusKind.Coding: return "Writing the code.";
                case AgentStatusKind.Testing: return "Running the tests.";
                case AgentStatusKind.Reporting: return "Writing the report.";
                default: return null;
            }
        }

        static string WorkPhrase(AgentStatusKind kind)
        {
            switch (kind)
            {
                case AgentStatusKind.Researching: return "reading the brief";
                case AgentStatusKind.Coding: return "writing the code";
                case AgentStatusKind.Testing: return "running the tests";
                case AgentStatusKind.Reporting: return "writing the report";
                default: return null;
            }
        }

        /// <summary>
        /// One or two short sentences about what <paramref name="role"/> is doing.
        /// <paramref name="runInProgress"/> / <paramref name="hasStartedRun"/> come from the session;
        /// <paramref name="workingTeammate"/> (may be null) is another role that is working right now
        /// and <paramref name="teammateKind"/> what it is doing.
        /// </summary>
        public static string Describe(string role, AgentStatusKind kind, bool runInProgress, bool hasStartedRun,
                                      string workingTeammate = null,
                                      AgentStatusKind teammateKind = AgentStatusKind.Idle)
        {
            switch (kind)
            {
                case AgentStatusKind.Researching:
                case AgentStatusKind.Coding:
                case AgentStatusKind.Testing:
                case AgentStatusKind.Reporting:
                {
                    int step = StepOf(role);
                    string words = WorkWords(kind);
                    if (step == 1) return words + " Step 1 of 3.";
                    if (step == 2) return words + " Step 2 of 3.";
                    if (step == 3) return words + " Step 3 of 3.";
                    return words;
                }
                case AgentStatusKind.Approaching: return "Bringing you a paper to decide on.";
                case AgentStatusKind.WaitingForUser: return "Waiting for your decision.";
                case AgentStatusKind.Waiting: return "Waiting.";
                case AgentStatusKind.Complete: return "Done. My part is finished.";
                case AgentStatusKind.Error: return "Something went wrong. This needs your attention.";
                default:
                {
                    if (runInProgress)
                    {
                        string mate = NormalizeRole(workingTeammate);
                        string phrase = WorkPhrase(teammateKind);
                        if (mate != null && phrase != null) return "Standing by. The " + mate + " is " + phrase + ".";
                        return "Standing by for my turn.";
                    }
                    return hasStartedRun ? "Idle. The last run is finished." : "Idle. Press Start to begin.";
                }
            }
        }

        /// <summary>The role that needs the user right now, or null. A decision waiting for the user
        /// comes first, then a paper on its way, then an error. Arrays are parallel.</summary>
        public static string RoleThatNeedsUser(string[] roles, AgentStatusKind[] kinds, int count)
        {
            if (roles == null || kinds == null) return null;
            int n = Mathf.Min(count, Mathf.Min(roles.Length, kinds.Length));
            for (int i = 0; i < n; i++) if (kinds[i] == AgentStatusKind.WaitingForUser) return roles[i];
            for (int i = 0; i < n; i++) if (kinds[i] == AgentStatusKind.Approaching) return roles[i];
            for (int i = 0; i < n; i++) if (kinds[i] == AgentStatusKind.Error) return roles[i];
            return null;
        }

        /// <summary>A sentence naming who needs the user, or saying that nobody does.</summary>
        public static string WhoNeedsUser(string[] roles, AgentStatusKind[] kinds, int count)
        {
            string role = RoleThatNeedsUser(roles, kinds, count);
            if (role == null) return "Nobody needs you right now.";
            int n = Mathf.Min(count, Mathf.Min(roles.Length, kinds.Length));
            for (int i = 0; i < n; i++)
            {
                if (roles[i] != role) continue;
                string name = NormalizeRole(role) ?? role;
                if (kinds[i] == AgentStatusKind.WaitingForUser) return "The " + name + " is waiting for your decision.";
                if (kinds[i] == AgentStatusKind.Approaching) return "The " + name + " is bringing you a paper.";
                return "The " + name + " needs your attention. Something went wrong.";
            }
            return "Nobody needs you right now.";
        }
    }

    /// <summary>
    /// Gaze dwell: looking steadily at one target for <see cref="dwellSeconds"/> shows it; it stays
    /// shown while the gaze stays, and leaves <see cref="lingerSeconds"/> after the gaze moved away.
    /// A loss of the target shorter than <see cref="graceSeconds"/> (a blink of tracking, a small
    /// head wobble) does not restart the dwell. Targets are small non-negative indices; -1 = none.
    /// </summary>
    public sealed class GazeDwellTimer
    {
        public float dwellSeconds = 1.2f;
        public float graceSeconds = 0.25f;
        public float lingerSeconds = 0.6f;

        int _candidate = -1;
        float _dwell, _lost, _away;

        /// <summary>The target currently shown, or -1.</summary>
        public int Shown { get; private set; } = -1;
        /// <summary>The target being dwelled on (not necessarily shown yet), or -1.</summary>
        public int Candidate => _candidate;
        /// <summary>0..1 progress of the dwell on the candidate.</summary>
        public float Progress => _candidate < 0 ? 0f : Mathf.Clamp01(_dwell / Mathf.Max(0.01f, dwellSeconds));

        public void Reset()
        {
            _candidate = -1; _dwell = _lost = _away = 0f; Shown = -1;
        }

        /// <summary>Advance by <paramref name="dt"/> with the target the gaze is on now. Returns <see cref="Shown"/>.</summary>
        public int Tick(int target, float dt)
        {
            if (dt < 0f) dt = 0f;
            if (target == _candidate)
            {
                _lost = 0f;
                if (_candidate >= 0) _dwell += dt;
            }
            else
            {
                _lost += dt;
                if (_lost > graceSeconds)
                {
                    _candidate = target;
                    _dwell = 0f;
                    _lost = 0f;
                }
            }

            if (_candidate >= 0 && _dwell >= dwellSeconds) { Shown = _candidate; }

            if (Shown >= 0)
            {
                if (target == Shown) _away = 0f;
                else
                {
                    _away += dt;
                    if (_away >= lingerSeconds) { Shown = -1; _away = 0f; }
                }
            }
            return Shown;
        }
    }

    /// <summary>
    /// A wave: an open hand moving side to side. Fed the hand's lateral position (metres along the
    /// viewer's right) each frame; fires once when it has reversed direction <see cref="swings"/>
    /// times, each swing at least <see cref="minSwing"/> long, within <see cref="window"/> seconds.
    /// A steady drift, a slow sway or a single reach never fires.
    /// </summary>
    public sealed class WaveDetector
    {
        public float minSwing = 0.035f;
        public int swings = 3;
        public float window = 1.3f;
        public float maxGap = 0.6f;

        bool _has;
        float _extreme;
        int _dir;
        int _count;
        float _first, _last;

        public void Reset() { _has = false; _dir = 0; _count = 0; }

        /// <summary>Returns true on the frame a wave is recognised. <paramref name="valid"/> = the
        /// hand is open and in view; anything else resets the detector.</summary>
        public bool Tick(float lateral, bool valid, float now)
        {
            if (!valid) { Reset(); return false; }
            if (!_has) { _has = true; _extreme = lateral; _dir = 0; _count = 0; return false; }

            if (_dir != 0 && now - _last > maxGap)
            {
                // Paused too long: whatever came before was not one continuous wave.
                _dir = 0; _count = 0; _extreme = lateral;
                return false;
            }

            float d = lateral - _extreme;
            if (_dir == 0)
            {
                if (Mathf.Abs(d) >= minSwing)
                {
                    _dir = d > 0f ? 1 : -1;
                    _extreme = lateral;
                    _count = 1;
                    _first = _last = now;
                }
                return false;
            }

            if (d * _dir > 0f)
            {
                _extreme = lateral;                    // still travelling the same way
            }
            else if (-d * _dir >= minSwing)
            {
                _dir = -_dir;                          // turned round and came back far enough
                _extreme = lateral;
                _count++;
                _last = now;
                if (now - _first > window) { _count = 1; _first = now; }
                if (_count >= swings + 1)
                {
                    Reset();
                    return true;
                }
            }
            return false;
        }
    }

    /// <summary>
    /// A point held still inside a zone: true once it has stayed within <see cref="tolerance"/> of
    /// where it settled for <see cref="holdSeconds"/>. Fires once per hold; leaving the zone re-arms.
    /// </summary>
    public sealed class HoldStillDetector
    {
        public float holdSeconds = 0.6f;
        public float tolerance = 0.012f;

        bool _in, _fired;
        Vector3 _anchor;
        float _held;

        public void Reset() { _in = false; _fired = false; _held = 0f; }

        public bool Tick(Vector3 point, bool inZone, float dt)
        {
            if (!inZone) { Reset(); return false; }
            if (!_in) { _in = true; _anchor = point; _held = 0f; _fired = false; return false; }
            if ((point - _anchor).sqrMagnitude > tolerance * tolerance)
            {
                _anchor = point; _held = 0f;            // it moved: start again from here
                return false;
            }
            _held += dt;
            if (!_fired && _held >= holdSeconds) { _fired = true; return true; }
            return false;
        }
    }

    /// <summary>Counts pokes on one character: a poke within <see cref="comboSeconds"/> of the
    /// previous one is a follow-up (1, 2, ...); otherwise it is a first poke (0).</summary>
    public struct PokeCombo
    {
        public const float comboSeconds = 2f;
        float _lastAt;
        int _streak;
        bool _any;

        public int Register(float now)
        {
            if (_any && now - _lastAt <= comboSeconds) _streak++;
            else _streak = 0;
            _any = true;
            _lastAt = now;
            return _streak;
        }
    }

    /// <summary>A simple "not more often than" gate.</summary>
    public struct Cooldown
    {
        float _readyAt;
        public bool Ready(float now) => now >= _readyAt;
        public void Start(float now, float seconds) { _readyAt = now + seconds; }
        public bool TryFire(float now, float seconds)
        {
            if (now < _readyAt) return false;
            _readyAt = now + seconds;
            return true;
        }
    }

    /// <summary>Sizes and thresholds that depend on the viewing distance.</summary>
    public static class AgentViewingMath
    {
        /// <summary>
        /// Scale factor for a chip or bubble so its text keeps the same apparent size as the office
        /// moves away. The text is authored for <paramref name="referenceDistance"/> (cap height
        /// about 4.7 mm at 0.5 m, that is 4 to 5 mm per 0.5 m); beyond it the scale grows linearly
        /// with distance, never below 1 and never above <paramref name="maxScale"/>.
        /// </summary>
        public static float DistanceScale(float distance, float referenceDistance = 0.5f, float maxScale = 7f)
        {
            if (referenceDistance <= 1e-4f) return 1f;
            return Mathf.Clamp(distance / referenceDistance, 1f, Mathf.Max(1f, maxScale));
        }

        /// <summary>
        /// Half-angle (degrees) of the gaze cone for a character at <paramref name="distance"/>:
        /// generous up close, and never narrower than the character itself looks from afar.
        /// </summary>
        public static float GazeConeDegrees(float distance, float characterRadius = 0.06f,
                                            float minDegrees = 5f, float maxDegrees = 11f)
        {
            float d = Mathf.Max(0.05f, distance);
            float subtended = Mathf.Atan2(characterRadius * 2.2f, d) * Mathf.Rad2Deg;
            return Mathf.Clamp(subtended, minDegrees, maxDegrees);
        }

        /// <summary>Index of the smallest angle that is inside its own cone, or -1. Arrays are parallel.</summary>
        public static int PickGazeTarget(float[] angles, float[] cones, int count)
        {
            int best = -1;
            float bestAngle = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                if (angles[i] > cones[i]) continue;
                if (angles[i] < bestAngle) { bestAngle = angles[i]; best = i; }
            }
            return best;
        }

        /// <summary>Squash-and-stretch: the sideways factor that keeps volume for a vertical factor.</summary>
        public static float SideForVertical(float vertical)
        {
            return 1f / Mathf.Sqrt(Mathf.Max(0.25f, vertical));
        }

        /// <summary>Piecewise smooth curve through (times[i], values[i]); t outside the range clamps.</summary>
        public static float Keys(float[] times, float[] values, float t)
        {
            int n = times.Length;
            if (t <= times[0]) return values[0];
            for (int i = 1; i < n; i++)
            {
                if (t <= times[i])
                {
                    float u = (t - times[i - 1]) / Mathf.Max(1e-5f, times[i] - times[i - 1]);
                    u = u * u * (3f - 2f * u);
                    return Mathf.Lerp(values[i - 1], values[i], u);
                }
            }
            return values[n - 1];
        }
    }
}
