using UnityEngine;
using MuseDesk.Workspace;

namespace MuseDesk.Spatial
{
    /// <summary>
    /// Three-stage sound of the ceiling pod: unlock (a latch release), travel (a quiet spool hum,
    /// lower going down, higher going up) and lock (a thunk and a chime). It listens to the pod
    /// machine's StateChanged (nothing is polled except two fades) and is silent outside Ceiling
    /// mode. Clips are synthesised once at start-up (<see cref="CeilingPodAudioSynth"/>); nothing is
    /// allocated afterwards. Rules live in <see cref="CeilingPodAudioCues"/>.
    ///
    /// Positions are found through the pod controller each time they are needed, so a swapped
    /// CeilingMount object is followed: one-shots play at the mount or at the office root, the
    /// loop follows the office root. The Meta XR Audio spatializer is used only when the project's
    /// audio settings name a spatializer plugin; otherwise plain Unity 3D audio.
    /// </summary>
    public class CeilingPodAudio : MonoBehaviour
    {
        public CeilingPodController pod;
        [Range(0f, 1f)] public float masterVolume = 0.6f;
        [Tooltip("Silences every pod sound. FeedbackController has no app-wide mute to follow.")]
        public bool muted;
        [Range(0f, 1f)] public float travelVolume = 0.5f;
        public float minDistance = 0.5f;
        public float maxDistance = 6f;

        AudioSource _cueSource, _loopSource;
        AudioClip _unlock, _lock, _loop;
        CeilingPodMachine _machine;
        CeilingPodState _state = CeilingPodState.Docked;
        bool _stateKnown, _travel;
        float _loopLevel;        // 0..1, fades over CeilingPodAudioCues.FadeSeconds
        float _pitch = 1f;
        int _cuesPlayed;

        // ── read by the Play Mode driver ──
        public bool Bound => _machine != null;
        public CeilingAudioCue LastCue { get; private set; }
        public CeilingAudioSite LastCueSite { get; private set; }
        public int CuesPlayed => _cuesPlayed;
        public bool LoopPlaying => _loopSource != null && _loopSource.isPlaying;
        public float LoopPitch => _pitch;
        public float LoopLevel => _loopLevel;
        public CeilingPodState AudioState => _state;
        public bool Spatialized => _cueSource != null && _cueSource.spatialize;

        void Awake()
        {
            _unlock = CeilingPodAudioSynth.UnlockClip();
            _lock = CeilingPodAudioSynth.LockClip();
            _loop = CeilingPodAudioSynth.LoopClip();
            _cueSource = MakeSource("CeilingPodCueVoice", false);
            _loopSource = MakeSource("CeilingPodTravelVoice", true);
            _loopSource.clip = _loop;
            _loopSource.volume = 0f;
        }

        AudioSource MakeSource(string name, bool loop)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var s = go.AddComponent<AudioSource>();
            s.playOnAwake = false;
            s.loop = loop;
            s.spatialBlend = 1f;
            s.dopplerLevel = 0f;
            s.rolloffMode = AudioRolloffMode.Logarithmic;
            s.minDistance = minDistance;
            s.maxDistance = maxDistance;
            s.spatialize = !string.IsNullOrEmpty(AudioSettings.GetSpatializerPluginName());
            return s;
        }

        void OnDisable()
        {
            Unbind();
            if (_loopSource != null) { _loopSource.Stop(); _loopSource.volume = 0f; }
            _loopLevel = 0f;
            _travel = false;
        }

        void Unbind()
        {
            if (_machine != null) _machine.StateChanged -= OnPodChanged;
            _machine = null;
            _stateKnown = false;
        }

        void Update()
        {
            if (pod == null) return;
            CeilingPodMachine m = pod.Machine;      // null until the pod has its transition controller
            if (m != _machine)
            {
                Unbind();
                if (m != null)
                {
                    _machine = m;
                    _machine.StateChanged += OnPodChanged;
                    OnPodChanged();                  // learn the state; the first one makes no cue
                }
            }

            FlushPendingCue();
            bool want = _travel && !muted && SpatialWorkspaceState.Mode == WorkspaceMode.Ceiling;
            float dt = Time.unscaledDeltaTime;
            _loopLevel = Mathf.MoveTowards(_loopLevel, want ? 1f : 0f, dt / CeilingPodAudioCues.FadeSeconds);

            if (_loopLevel <= 0f)
            {
                if (_loopSource.isPlaying) _loopSource.Stop();
                return;
            }
            if (!_loopSource.isPlaying) _loopSource.Play();

            float targetPitch = CeilingPodAudioCues.TravelPitch(CeilingPodAudioCues.Direction(_state));
            _pitch = Mathf.MoveTowards(_pitch, targetPitch, CeilingPodAudioCues.PitchGlidePerSecond * dt);
            _loopSource.pitch = _pitch;
            _loopSource.volume = _loopLevel * masterVolume * travelVolume;
            Transform root = OfficeRoot();
            if (root != null) _loopSource.transform.position = root.position;
        }

        /// <summary>The pod machine raised StateChanged (also for status text: only a real change counts).</summary>
        void OnPodChanged()
        {
            if (_machine == null) return;
            CeilingPodState next = _machine.State;
            if (_stateKnown && next == _state) return;
            CeilingPodState prev = _state;
            bool first = !_stateKnown;
            _state = next;
            _stateKnown = true;

            _travel = CeilingPodAudioCues.IsTravel(next);
            if (_travel && _loopLevel <= 0f)
            {
                // Starting a trip: begin at the right pitch, not gliding from the last one.
                _pitch = CeilingPodAudioCues.TravelPitch(CeilingPodAudioCues.Direction(next));
            }
            if (first) return;

            CeilingAudioCue cue = CeilingPodAudioCues.CueFor(prev, next);
            if (cue != CeilingAudioCue.None) PlayCue(cue, CeilingPodAudioCues.SiteFor(next));
        }

        Transform OfficeRoot()
        {
            if (pod != null && pod.transition != null && pod.transition.workspaceRoot != null) return pod.transition.workspaceRoot;
            return SpatialWorkspaceState.WorkspaceRoot;
        }

        Transform MountPoint()
        {
            if (pod != null)
            {
                if (pod.ceilingMount != null) return pod.ceilingMount;
                if (pod.mastBottom != null) return pod.mastBottom;
            }
            return OfficeRoot();
        }

        // A cue raised in the instant before Ceiling mode is set (the first trip up from the desk
        // starts the pod a frame before the mode changes) waits briefly instead of being dropped.
        const float PendingCueSeconds = 0.3f;
        CeilingAudioCue _pendingCue;
        CeilingAudioSite _pendingSite;
        float _pendingUntil;

        bool CueAllowedNow()
        {
            return SpatialWorkspaceState.Mode == WorkspaceMode.Ceiling && _machine != null && _machine.Active;
        }

        void FlushPendingCue()
        {
            if (_pendingCue == CeilingAudioCue.None) return;
            if (Time.unscaledTime > _pendingUntil) { _pendingCue = CeilingAudioCue.None; return; }
            if (!CueAllowedNow()) return;
            CeilingAudioCue cue = _pendingCue;
            _pendingCue = CeilingAudioCue.None;
            PlayCue(cue, _pendingSite);
        }

        void PlayCue(CeilingAudioCue cue, CeilingAudioSite site)
        {
            if (muted) return;
            if (!CueAllowedNow())
            {
                _pendingCue = cue;
                _pendingSite = site;
                _pendingUntil = Time.unscaledTime + PendingCueSeconds;
                return;
            }
            Transform at = site == CeilingAudioSite.Mount ? MountPoint() : OfficeRoot();
            if (at == null) at = transform;
            _cueSource.transform.position = at.position;
            AudioClip clip = cue == CeilingAudioCue.Unlock ? _unlock : _lock;
            _cueSource.PlayOneShot(clip, masterVolume);
            LastCue = cue;
            LastCueSite = site;
            _cuesPlayed++;
        }
    }
}
