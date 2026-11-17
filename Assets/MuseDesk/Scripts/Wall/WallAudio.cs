using UnityEngine;
using MuseDesk.Workspace;

namespace MuseDesk.Wall
{
    public enum WallCue { DoorSlide = 0, Ding = 1, PrintTick = 2, PortalOpen = 3, PortalClose = 4, Land = 5 }

    /// <summary>
    /// Quiet, short, spatialised one-shots for the Wall Habitat (polish spec 8: miniature lift door, small chime, card
    /// tick, portal whoosh). Procedural like FeedbackController (the project ships no audio files), never looping, and
    /// silent while Focus mode is on. Each cue is rate-limited so a burst of events produces one sound. Cues follow
    /// confirmed state only: the elevator machine's real transitions, a printed (server-confirmed) card, the portal flow.
    /// </summary>
    public sealed class WallAudio
    {
        const int Count = 6;
        readonly AudioSource _habitat, _near;
        readonly AudioClip[] _clips = new AudioClip[Count];
        readonly float[] _gain = { 0.45f, 0.6f, 0.5f, 0.55f, 0.5f, 0.45f };
        readonly float[] _last = new float[Count];
        public float volume = 0.22f;

        /// <param name="habitatAnchor">Sounds of the treehouse come from here (the habitat root).</param>
        /// <param name="nearAnchor">Sounds near the user (portal, desk visit) come from here.</param>
        public WallAudio(Transform habitatAnchor, Transform nearAnchor)
        {
            _habitat = MakeSource(habitatAnchor, "WallAudio_Habitat", 0.3f, 4f);
            _near = MakeSource(nearAnchor, "WallAudio_Near", 0.25f, 3f);
            for (int i = 0; i < Count; i++) _last[i] = -10f;
            _clips[(int)WallCue.DoorSlide] = Noise("wall_door", 0.22f, 2400f, 900f, 0.35f);
            _clips[(int)WallCue.Ding] = Bell("wall_ding", new[] { 1318.5f, 1975.5f }, 0.07f, 4.5f, 0.6f);
            _clips[(int)WallCue.PrintTick] = Bell("wall_print", new[] { 1760f, 2349f }, 0.045f, 40f, 0.09f);
            _clips[(int)WallCue.PortalOpen] = Noise("wall_portal_open", 0.55f, 600f, 2600f, 0.8f);
            _clips[(int)WallCue.PortalClose] = Noise("wall_portal_close", 0.45f, 2600f, 500f, 0.8f);
            _clips[(int)WallCue.Land] = Bell("wall_land", new[] { 880f }, 0f, 30f, 0.1f);
        }

        static AudioSource MakeSource(Transform parent, string name, float min, float max)
        {
            if (parent == null) return null;
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var s = go.AddComponent<AudioSource>();
            s.playOnAwake = false; s.loop = false; s.spatialBlend = 1f; s.dopplerLevel = 0f;
            s.minDistance = min; s.maxDistance = max; s.rolloffMode = AudioRolloffMode.Logarithmic;
            return s;
        }

        public void Play(WallCue cue, bool nearUser = false)
        {
            if (FocusModeController.Active) return;                 // Focus Quiet: no ambient habitat sounds
            int i = (int)cue;
            float now = Time.unscaledTime;
            if (now - _last[i] < 0.15f) return;
            _last[i] = now;
            AudioSource s = nearUser ? _near : _habitat;
            if (s == null || !s.isActiveAndEnabled || _clips[i] == null) return;
            s.PlayOneShot(_clips[i], volume * _gain[i]);
        }

        public void Destroy()
        {
            if (_habitat != null) Object.Destroy(_habitat.gameObject);
            if (_near != null) Object.Destroy(_near.gameObject);
            for (int i = 0; i < Count; i++) if (_clips[i] != null) Object.Destroy(_clips[i]);
        }

        // ── procedural clips ──

        const int Rate = 44100;

        /// <summary>Soft sine partials with a fast attack and exponential decay (a small bell or tick).</summary>
        static AudioClip Bell(string name, float[] partials, float step, float decay, float length)
        {
            int n = Mathf.Max(1, (int)(Rate * (length + step * (partials.Length - 1))));
            var d = new float[n];
            float norm = 0.5f / Mathf.Sqrt(partials.Length);
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / Rate, v = 0f;
                for (int k = 0; k < partials.Length; k++)
                {
                    float tk = t - k * step;
                    if (tk < 0f) break;
                    v += (1f - Mathf.Exp(-tk / 0.003f)) * Mathf.Exp(-decay * tk) * Mathf.Sin(2f * Mathf.PI * partials[k] * tk);
                }
                d[i] = Mathf.Clamp(v * norm, -1f, 1f);
            }
            Fade(d);
            var c = AudioClip.Create(name, n, 1, Rate, false);
            c.SetData(d, 0);
            return c;
        }

        /// <summary>Filtered noise whose one-pole low-pass cutoff sweeps from <paramref name="fromHz"/> to
        /// <paramref name="toHz"/> under a smooth hump envelope: a soft "shff" (door) or "whoosh" (portal).
        /// Deterministic (fixed seed) so it sounds the same every launch.</summary>
        static AudioClip Noise(string name, float length, float fromHz, float toHz, float level)
        {
            int n = Mathf.Max(1, (int)(Rate * length));
            var d = new float[n];
            var rng = new System.Random(1234);
            float y = 0f;
            for (int i = 0; i < n; i++)
            {
                float u = (float)i / n;
                float cutoff = Mathf.Lerp(fromHz, toHz, u);
                float a = 1f - Mathf.Exp(-2f * Mathf.PI * cutoff / Rate);
                float x = (float)(rng.NextDouble() * 2.0 - 1.0);
                y += a * (x - y);
                float env = Mathf.Sin(Mathf.PI * u);                 // smooth in and out, no click
                d[i] = Mathf.Clamp(y * env * level, -1f, 1f);
            }
            Fade(d);
            var c = AudioClip.Create(name, n, 1, Rate, false);
            c.SetData(d, 0);
            return c;
        }

        static void Fade(float[] d)
        {
            int f = Mathf.Min(d.Length, (int)(Rate * 0.006f));
            for (int i = 0; i < f; i++) d[d.Length - 1 - i] *= (float)i / f;
        }
    }
}
