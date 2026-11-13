using System.Collections.Generic;
using UnityEngine;
using MuseDesk.Documents;
using MuseDesk.Models;
using MuseDesk.Networking;

namespace MuseDesk.Feedback
{
    /// <summary>The distinct sounds the office makes. One short, quiet cue per kind of moment.</summary>
    public enum FeedbackCue
    {
        /// <summary>A poke key was pressed.</summary>
        KeyPress = 0,
        /// <summary>The user took the decision paper from the agent.</summary>
        PaperTaken = 1,
        /// <summary>An agent needs a decision.</summary>
        Attention = 2,
        /// <summary>The backend confirmed an approval.</summary>
        Approve = 3,
        /// <summary>The backend confirmed a rejection.</summary>
        Reject = 4,
        /// <summary>The backend confirmed a request for changes.</summary>
        Changes = 5,
        /// <summary>Work was delivered (a completion that references an artifact).</summary>
        Delivered = 6,
        /// <summary>Something was refused or failed (a failed run, a response that was not saved, a refused drop).</summary>
        Error = 7,
    }

    /// <summary>
    /// Quiet audio feedback. Every clip is synthesised once at start-up (soft sine blips with a fast
    /// attack and an exponential decay, a different interval per cue), so there are no audio assets
    /// and nothing is allocated after start-up. Sound only accompanies a state that is also shown
    /// in words; it is never the only signal.
    ///
    /// Outcome cues (approve, reject, changes, delivered, error) are driven by session reducer
    /// events, so they sound only once the backend, or the labelled demo, has confirmed the outcome.
    /// Other code can ask for a cue through <see cref="Play(FeedbackCue)"/> or the static
    /// <see cref="PlayCue"/>; the same cue is not repeated within <see cref="minRepeatSeconds"/>,
    /// so two callers reporting one moment produce one sound.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class FeedbackController : MonoBehaviour
    {
        public SessionController controller;
        [Range(0f, 1f)] public float volume = 0.25f;

        [Tooltip("Optional: the decision paper, to sound a cue when the user takes it from the agent.")]
        public HeldPaperController paper;
        [Tooltip("Optional: where the sounds come from (the workspace). Empty: this object's position.")]
        public Transform soundAnchor;
        [Tooltip("Sound a soft click when any poke key in the scene is pressed.")]
        public bool keyPressSound = true;
        [Tooltip("The same cue is not played twice within this many seconds.")]
        public float minRepeatSeconds = 0.12f;

        /// <summary>The scene's feedback controller, or null before it has awoken.</summary>
        public static FeedbackController Instance { get; private set; }

        /// <summary>Play a cue on the scene's controller, if there is one. Safe to call from anywhere on the main thread.</summary>
        public static void PlayCue(FeedbackCue cue)
        {
            if (Instance != null) Instance.Play(cue);
        }

        const int CueCount = 8;

        AudioSource _voice;
        SessionReducer _reducer;
        bool _bound;
        bool _paperWasTaken;
        readonly AudioClip[] _clips = new AudioClip[CueCount];
        readonly float[] _gain = new float[CueCount];
        readonly float[] _lastPlayed = new float[CueCount];
        readonly List<Oculus.Interaction.PokeInteractable> _keys = new List<Oculus.Interaction.PokeInteractable>();

        void Awake()
        {
            Instance = this;

            // The required AudioSource stays on this object; the cues play from a child that can be
            // moved to the workspace, so they come from the office and not from wherever the
            // services object happens to be.
            var own = GetComponent<AudioSource>();
            own.playOnAwake = false;
            var voiceGo = new GameObject("FeedbackVoice");
            voiceGo.transform.SetParent(transform, false);
            _voice = voiceGo.AddComponent<AudioSource>();
            _voice.playOnAwake = false;
            _voice.loop = false;
            _voice.spatialBlend = 1f;          // localized in space
            _voice.dopplerLevel = 0f;
            _voice.minDistance = 0.6f;
            _voice.maxDistance = 6f;

            for (int i = 0; i < CueCount; i++) { _gain[i] = 1f; _lastPlayed[i] = -10f; }

            //                 name           notes (Hz)                       step    decay  tail
            Make(FeedbackCue.KeyPress,   "cue_key",       new[] { 1175f },                 0f,     55f, 0.07f, 0.7f);
            Make(FeedbackCue.PaperTaken, "cue_paper",     new[] { 587f, 784f },            0.06f,  22f, 0.14f, 0.9f);   // up a fourth
            Make(FeedbackCue.Attention,  "cue_attention", new[] { 660f, 880f },            0f,      6f, 0.40f, 1f);     // two notes together
            Make(FeedbackCue.Approve,    "cue_approve",   new[] { 523f, 659f, 784f },      0.085f, 12f, 0.26f, 1f);     // rising major triad
            Make(FeedbackCue.Reject,     "cue_reject",    new[] { 440f, 349f },            0.13f,  10f, 0.24f, 0.9f);   // falling major third
            Make(FeedbackCue.Changes,    "cue_changes",   new[] { 494f, 494f },            0.11f,  14f, 0.16f, 0.9f);   // one note, twice
            Make(FeedbackCue.Delivered,  "cue_delivered", new[] { 523f, 784f, 1047f, 1568f }, 0.10f, 5f, 0.50f, 1f);    // rising fifths and octave
            Make(FeedbackCue.Error,      "cue_error",     new[] { 233f, 220f },            0.14f,   9f, 0.26f, 1f);     // low, down a semitone
        }

        void Make(FeedbackCue cue, string clipName, float[] notes, float step, float decay, float tail, float gain)
        {
            _clips[(int)cue] = Blips(clipName, notes, step, decay, tail);
            _gain[(int)cue] = gain;
        }

        bool _started;

        void Start()
        {
            _started = true;
            if (keyPressSound) RegisterKeys(null);
        }

        void OnEnable()
        {
            // Re-enabled after a disable (which dropped every subscription): register the keys again.
            if (_started && keyPressSound) RegisterKeys(null);
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void Update()
        {
            if (!_bound && controller != null) Bind();

            // The paper has no "taken" event; notice the flag flip (two bool reads per frame).
            if (paper != null)
            {
                bool taken = paper.isActiveAndEnabled && paper.agentHoldPoint != null && paper.IsTaken;
                if (taken && !_paperWasTaken) Play(FeedbackCue.PaperTaken);
                _paperWasTaken = taken;
            }
        }

        void OnDisable()
        {
            if (_reducer != null)
            {
                _reducer.DecisionRequested -= OnRequested;
                _reducer.DecisionResolved -= OnResolved;
                _reducer.WorkDelivered -= OnDelivered;
                _reducer.TaskFailed -= OnFailed;
            }
            if (_bound && controller != null) controller.RespondFailed -= OnRespondFailed;
            _reducer = null; _bound = false;

            for (int i = 0; i < _keys.Count; i++)
                if (_keys[i] != null) _keys[i].WhenStateChanged -= OnKeyState;
            _keys.Clear();
        }

        void Bind()
        {
            _reducer = controller.Reducer;
            _reducer.DecisionRequested += OnRequested;
            _reducer.DecisionResolved += OnResolved;
            _reducer.WorkDelivered += OnDelivered;
            _reducer.TaskFailed += OnFailed;
            controller.RespondFailed += OnRespondFailed;
            _bound = true;
        }

        void OnRequested(string id, EventEnvelope e) => Play(FeedbackCue.Attention);

        void OnResolved(string id, string state)
        {
            string s = state ?? "";
            if (s.IndexOf("approv", System.StringComparison.OrdinalIgnoreCase) >= 0) Play(FeedbackCue.Approve);
            else if (s.IndexOf("change", System.StringComparison.OrdinalIgnoreCase) >= 0) Play(FeedbackCue.Changes);
            else Play(FeedbackCue.Reject);
        }

        /// <summary>Set (at run time) by a component that sequences the delivery payoff itself, the
        /// completion sequence. While true this controller does not sound <see cref="FeedbackCue.Delivered"/>
        /// on WorkDelivered; the owner plays it once, at its own beat. False (the default) keeps the
        /// original behaviour, so the cue is never lost if no sequence is present.</summary>
        public bool DeliveredCueSequenced { get; set; }

        void OnDelivered(EventEnvelope e)
        {
            if (DeliveredCueSequenced) return;
            Play(FeedbackCue.Delivered);
        }
        void OnFailed(EventEnvelope e) => Play(FeedbackCue.Error);
        void OnRespondFailed(string decisionId, string message) => Play(FeedbackCue.Error);

        // ── public API ──

        /// <summary>Play one cue, quietly. Ignored if the same cue played within <see cref="minRepeatSeconds"/>.</summary>
        public void Play(FeedbackCue cue)
        {
            int i = (int)cue;
            if (i < 0 || i >= CueCount || _voice == null) return;
            AudioClip clip = _clips[i];
            if (clip == null) return;
            float now = Time.unscaledTime;
            if (now - _lastPlayed[i] < minRepeatSeconds) return;
            _lastPlayed[i] = now;
            if (soundAnchor != null) _voice.transform.position = soundAnchor.position;
            _voice.PlayOneShot(clip, volume * _gain[i]);
        }

        public void PlayKeyPress() => Play(FeedbackCue.KeyPress);
        public void PlayPaperTaken() => Play(FeedbackCue.PaperTaken);
        public void PlayAttention() => Play(FeedbackCue.Attention);
        public void PlayApprove() => Play(FeedbackCue.Approve);
        public void PlayReject() => Play(FeedbackCue.Reject);
        public void PlayChanges() => Play(FeedbackCue.Changes);
        public void PlayDelivered() => Play(FeedbackCue.Delivered);
        public void PlayError() => Play(FeedbackCue.Error);

        /// <summary>
        /// Give poke keys the key-press click. Called once at start-up for every key in the scene
        /// (including inactive ones). Call it again with the root of anything instantiated later
        /// (for example a task card with its own keys); keys already registered are skipped.
        /// </summary>
        public void RegisterKeys(GameObject root)
        {
            Oculus.Interaction.PokeInteractable[] found = root != null
                ? root.GetComponentsInChildren<Oculus.Interaction.PokeInteractable>(true)
                : FindObjectsByType<Oculus.Interaction.PokeInteractable>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < found.Length; i++)
            {
                var key = found[i];
                if (key == null || _keys.Contains(key)) continue;
                key.WhenStateChanged += OnKeyState;
                _keys.Add(key);
            }
        }

        /// <summary>
        /// Give the poke keys under <paramref name="root"/> the key-press click, on the scene's
        /// controller if there is one. For code that instantiates keys after start-up (a task card
        /// taken fresh from its prefab): call it once, right after Instantiate. Safe to call again
        /// for the same object (registered keys are skipped), before the controller exists (ignored)
        /// and with the click switched off (ignored).
        /// </summary>
        public static void RegisterLateKeys(GameObject root)
        {
            if (root == null || Instance == null || !Instance.keyPressSound || !Instance.isActiveAndEnabled) return;
            Instance.RegisterKeys(root);
        }

        void OnKeyState(Oculus.Interaction.InteractableStateChangeArgs args)
        {
            if (args.NewState == Oculus.Interaction.InteractableState.Select) Play(FeedbackCue.KeyPress);
        }

        // ── procedural tones ──

        /// <summary>
        /// A short run of soft sine notes. Each note starts <paramref name="step"/> seconds after the
        /// previous one (0 = all together), rises in about 4 ms and dies away exponentially
        /// (<paramref name="decay"/> per second); the clip ends <paramref name="tail"/> seconds after
        /// the last note starts, with a short fade so it never clicks.
        /// </summary>
        static AudioClip Blips(string clipName, float[] notes, float step, float decay, float tail)
        {
            const int rate = 44100;
            const float attack = 0.004f;
            float length = step * (notes.Length - 1) + tail;
            int n = Mathf.Max(1, (int)(rate * length));
            var data = new float[n];
            float norm = 0.5f / Mathf.Sqrt(notes.Length);
            int fade = Mathf.Min(n, (int)(rate * 0.006f));
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / rate;
                float v = 0f;
                for (int k = 0; k < notes.Length; k++)
                {
                    float tk = t - k * step;
                    if (tk < 0f) break;
                    float env = (1f - Mathf.Exp(-tk / attack)) * Mathf.Exp(-decay * tk);
                    v += env * Mathf.Sin(2f * Mathf.PI * notes[k] * tk);
                }
                float sample = Mathf.Clamp(v * norm, -1f, 1f);
                int fromEnd = n - 1 - i;
                if (fromEnd < fade) sample *= (float)fromEnd / fade;
                data[i] = sample;
            }
            var clip = AudioClip.Create(clipName, n, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
