using UnityEngine;
using MuseDesk.Models;
using MuseDesk.Networking;

namespace MuseDesk.Feedback
{
    /// <summary>
    /// Light audio "juice": soft, localized cues on delivery / approve / reject / completion.
    /// Clips are generated procedurally (no audio assets, no licensing), kept quiet and brief
    /// per the UX spec — sound augments state, it is never required to convey it.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class FeedbackController : MonoBehaviour
    {
        public SessionController controller;
        [Range(0f, 1f)] public float volume = 0.35f;

        AudioSource _src;
        SessionReducer _reducer;
        bool _bound;
        AudioClip _chime, _success, _soft, _complete;

        void Awake()
        {
            _src = GetComponent<AudioSource>();
            _src.playOnAwake = false;
            _src.spatialBlend = 1f;   // localized in space
            _chime    = Tone(new[] { 660f, 880f }, 0.35f);
            _success  = Arp(new[] { 523f, 659f, 784f, 1046f }, 0.11f);
            _soft     = Arp(new[] { 440f, 349f }, 0.16f);
            _complete = Tone(new[] { 784f, 1046f, 1568f }, 0.5f);
        }

        void Update() { if (!_bound && controller != null) Bind(); }

        void OnDisable()
        {
            if (_reducer != null)
            {
                _reducer.DecisionRequested -= OnRequested;
                _reducer.DecisionResolved -= OnResolved;
                _reducer.WorkDelivered -= OnCompleted;
            }
            _reducer = null; _bound = false;
        }

        void Bind()
        {
            _reducer = controller.Reducer;
            _reducer.DecisionRequested += OnRequested;
            _reducer.DecisionResolved += OnResolved;
            _reducer.WorkDelivered += OnCompleted;
            _bound = true;
        }

        void OnRequested(string id, EventEnvelope e) => Play(_chime);
        void OnResolved(string id, string state) => Play(state == "approved" ? _success : _soft);
        void OnCompleted(EventEnvelope e) => Play(_complete);

        void Play(AudioClip clip) { if (clip != null) _src.PlayOneShot(clip, volume); }

        // ── procedural tone helpers ──
        static AudioClip Tone(float[] freqs, float dur)
        {
            int rate = 44100, n = Mathf.Max(1, (int)(rate * dur));
            var data = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / rate, env = Mathf.Exp(-3.2f * t), v = 0f;
                foreach (var f in freqs) v += Mathf.Sin(2f * Mathf.PI * f * t);
                data[i] = 0.9f * env * v / freqs.Length;
            }
            var c = AudioClip.Create("tone", n, 1, rate, false);
            c.SetData(data, 0);
            return c;
        }

        static AudioClip Arp(float[] notes, float noteDur)
        {
            int rate = 44100, per = Mathf.Max(1, (int)(rate * noteDur)), n = per * notes.Length;
            var data = new float[n];
            for (int k = 0; k < notes.Length; k++)
                for (int i = 0; i < per; i++)
                {
                    float t = (float)i / rate, env = Mathf.Exp(-5f * t);
                    data[k * per + i] = 0.9f * env * Mathf.Sin(2f * Mathf.PI * notes[k] * t);
                }
            var c = AudioClip.Create("arp", n, 1, rate, false);
            c.SetData(data, 0);
            return c;
        }
    }
}
