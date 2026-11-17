using UnityEngine;

namespace MuseDesk.Art
{
    /// <summary>
    /// Pooled, zero-allocation VFX and spatial AudioSource player for restrained studio effects.
    /// Pre-warms fixed-size ring buffers on initialization and supports comfort reduced-motion gating.
    /// </summary>
    [DisallowMultipleComponent]
    public class MuseVfxPoolPlayer : MonoBehaviour
    {
        [Header("Pool Configuration")]
        [SerializeField, Min(1)] private int poolSize = 4;
        [SerializeField] private ParticleSystem particleEffectPrefab;
        [SerializeField] private ParticleSystem[] preboundParticlePool;
        [SerializeField] private AudioSource[] preboundAudioPool;

        [Header("Default Audio Cue")]
        [SerializeField] private AudioClip defaultAudioClip;
        [SerializeField, Range(0f, 1f)] private float defaultVolume = 0.75f;
        [SerializeField, Range(0f, 1f)] private float spatialBlend = 1f;
        [SerializeField] private float minDistance = 0.5f;
        [SerializeField] private float maxDistance = 6.0f;
        [SerializeField, Range(0f, 0.08f)] private float pitchJitter = 0.02f;

        [Header("Comfort")]
        [SerializeField] private bool reducedMotion;

        private ParticleSystem[] _vfxPool;
        private AudioSource[] _audioPool;
        private Transform _poolRoot;
        private int _vfxCursor;
        private int _audioCursor;
        private bool _initialized;

        public bool ReducedMotion => reducedMotion;

        private void Awake()
        {
            EnsureInitialized();
        }

        public void EnsureInitialized()
        {
            if (_initialized) return;
            _initialized = true;

            int count = Mathf.Max(1, poolSize);

            if (preboundParticlePool != null && preboundParticlePool.Length > 0)
            {
                _vfxPool = preboundParticlePool;
            }
            else if (particleEffectPrefab != null)
            {
                EnsurePoolRoot();
                _vfxPool = new ParticleSystem[count];
                for (int i = 0; i < count; i++)
                {
                    ParticleSystem ps = Instantiate(particleEffectPrefab, _poolRoot);
                    ps.name = $"{particleEffectPrefab.name}_Pooled_{i}";
                    var main = ps.main;
                    main.playOnAwake = false;
                    ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                    _vfxPool[i] = ps;
                }
            }
            else
            {
                _vfxPool = System.Array.Empty<ParticleSystem>();
            }

            if (preboundAudioPool != null && preboundAudioPool.Length > 0)
            {
                _audioPool = preboundAudioPool;
                for (int i = 0; i < _audioPool.Length; i++)
                {
                    ConfigureAudioSource(_audioPool[i]);
                }
            }
            else
            {
                EnsurePoolRoot();
                _audioPool = new AudioSource[count];
                for (int i = 0; i < count; i++)
                {
                    var go = new GameObject($"PooledAudioSource_{i}");
                    go.transform.SetParent(_poolRoot, false);
                    AudioSource src = go.AddComponent<AudioSource>();
                    ConfigureAudioSource(src);
                    _audioPool[i] = src;
                }
            }
        }

        private void EnsurePoolRoot()
        {
            if (_poolRoot != null) return;
            var rootGo = new GameObject("VfxAudioPool");
            rootGo.transform.SetParent(transform, false);
            _poolRoot = rootGo.transform;
        }

        private void ConfigureAudioSource(AudioSource src)
        {
            if (src == null) return;
            src.playOnAwake = false;
            src.loop = false;
            src.spatialBlend = spatialBlend;
            src.minDistance = minDistance;
            src.maxDistance = maxDistance;
            src.rolloffMode = AudioRolloffMode.Linear;
        }

        /// <summary>
        /// Toggles comfort reduced motion. When enabled, particle emission is suppressed and active bursts are cleared.
        /// </summary>
        public void SetReducedMotion(bool reduced)
        {
            reducedMotion = reduced;
            if (reduced)
            {
                ClearParticlePool();
            }
        }

        /// <summary>
        /// Plays the pooled VFX and default audio cue at this component's position.
        /// </summary>
        public void PlayEffect()
        {
            PlayEffect(transform.position, defaultAudioClip, defaultVolume);
        }

        /// <summary>
        /// Plays the pooled VFX and default audio cue at the specified world position.
        /// </summary>
        public void PlayEffect(Vector3 worldPosition)
        {
            PlayEffect(worldPosition, defaultAudioClip, defaultVolume);
        }

        /// <summary>
        /// Plays the pooled VFX and an optional audio clip override at the specified world position.
        /// </summary>
        public void PlayEffect(Vector3 worldPosition, AudioClip clipOverride, float volume = 1f)
        {
            EnsureInitialized();

            if (!reducedMotion && _vfxPool != null && _vfxPool.Length > 0)
            {
                ParticleSystem ps = _vfxPool[_vfxCursor];
                _vfxCursor = (_vfxCursor + 1) % _vfxPool.Length;
                if (ps != null)
                {
                    ps.transform.position = worldPosition;
                    ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                    ps.Play(true);
                }
            }

            AudioClip clip = clipOverride != null ? clipOverride : defaultAudioClip;
            PlayPooledAudio(worldPosition, clip, volume);
        }

        /// <summary>
        /// Plays a caller-owned ParticleSystem (non-pooled override) and pooled audio cue at worldPosition.
        /// </summary>
        public void PlayEffect(ParticleSystem explicitParticleSystem, Vector3 worldPosition, AudioClip clipOverride = null, float volume = 1f)
        {
            EnsureInitialized();

            if (!reducedMotion && explicitParticleSystem != null)
            {
                explicitParticleSystem.transform.position = worldPosition;
                explicitParticleSystem.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                explicitParticleSystem.Play(true);
            }

            AudioClip clip = clipOverride != null ? clipOverride : defaultAudioClip;
            PlayPooledAudio(worldPosition, clip, volume);
        }

        /// <summary>
        /// Immediately stops and clears all pooled particles and active audio voices.
        /// </summary>
        public void StopAndClear()
        {
            if (!_initialized) return;

            ClearParticlePool();

            if (_audioPool != null)
            {
                for (int i = 0; i < _audioPool.Length; i++)
                {
                    AudioSource src = _audioPool[i];
                    if (src != null && src.isPlaying)
                    {
                        src.Stop();
                    }
                }
            }
        }

        private void ClearParticlePool()
        {
            if (_vfxPool == null) return;
            for (int i = 0; i < _vfxPool.Length; i++)
            {
                ParticleSystem ps = _vfxPool[i];
                if (ps != null)
                {
                    ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                }
            }
        }

        private void PlayPooledAudio(Vector3 worldPosition, AudioClip clip, float volume)
        {
            if (clip == null || _audioPool == null || _audioPool.Length == 0) return;
            float clampedVol = Mathf.Clamp01(volume) * defaultVolume;
            if (clampedVol <= 0.0001f) return;

            AudioSource src = _audioPool[_audioCursor];
            _audioCursor = (_audioCursor + 1) % _audioPool.Length;
            if (src == null) return;

            src.transform.position = worldPosition;
            src.pitch = pitchJitter > 0f ? 1f + Random.Range(-pitchJitter, pitchJitter) : 1f;
            src.PlayOneShot(clip, clampedVol);
        }
    }
}
