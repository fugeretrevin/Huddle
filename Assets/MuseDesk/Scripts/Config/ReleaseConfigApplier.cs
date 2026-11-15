using System.Collections.Generic;
using UnityEngine;
using MuseDesk.Art;
using MuseDesk.Spatial;
using MuseDesk.Voice;

namespace MuseDesk.Config
{
    /// <summary>
    /// Applies <see cref="MuseDeskReleaseConfig"/> to the scene. Awake runs before every other script
    /// (execution order) so the director's own Awake and Start already see the flags; Start applies
    /// again once all Awakes have run (the scan quads are created in Awake) and logs one line.
    /// Everything is reversible: components are only disabled, and their authored state is
    /// remembered and restored when a flag is switched back on. Nothing is destroyed.
    /// </summary>
    [DefaultExecutionOrder(-10000)]
    public class ReleaseConfigApplier : MonoBehaviour
    {
        [Tooltip("Optional override. Empty: Resources/ReleaseConfig.")]
        public MuseDeskReleaseConfig config;

        public static MuseDeskReleaseConfig Active { get; private set; }

        static readonly Dictionary<Behaviour, bool> Original = new Dictionary<Behaviour, bool>();
        static readonly Dictionary<Object, bool> OriginalFlag = new Dictionary<Object, bool>();
        static bool _micHidden;

        void Awake()
        {
            if (config == null) config = MuseDeskReleaseConfig.Load();
            if (config == null)
            {
                Debug.LogWarning("[MuseDesk.Release] Resources/ReleaseConfig missing; using defaults (everything on).");
                config = ScriptableObject.CreateInstance<MuseDeskReleaseConfig>();
            }
            Original.Clear(); OriginalFlag.Clear(); _micHidden = false;
            Apply(config);
        }

        void Start()
        {
            Apply(config);
            Debug.Log("[MuseDesk.Release] " + config.Summary());
        }

        public const string ReducedMotionKey = MuseDesk.Motion.ReducedMotionPreference.Key;

        sealed class PlayerPrefsStore : MuseDesk.Motion.IPrefStore
        {
            public int GetInt(string key, int fallback) { return PlayerPrefs.GetInt(key, fallback); }
            public void SetInt(string key, int value) { PlayerPrefs.SetInt(key, value); }
        }
        static readonly PlayerPrefsStore Prefs = new PlayerPrefsStore();

        /// <summary>Save the user's Reduced Motion choice on this device and apply it now.</summary>
        public static void SetReducedMotionPreference(bool on)
        {
            MuseDesk.Motion.ReducedMotionPreference.Save(Prefs, on);
            PlayerPrefs.Save();
            MuseDesk.Motion.MotionSettings.SetReducedMotion(on);
        }

        /// <summary>Apply a config to the live scene. Safe to call repeatedly (a driver does).</summary>
        public static void Apply(MuseDeskReleaseConfig c)
        {
            if (c == null) return;
            Active = c;

            // Comfort: Reduced Motion (build default, overridden by a saved per-device choice).
            MuseDesk.Motion.MotionSettings.SetReducedMotion(MuseDesk.Motion.ReducedMotionPreference.Resolve(Prefs, c.reducedMotion));

            // The in-app Calm motion key follows its flag (hidden by scale when off).
            MuseDesk.Workspace.MotionToggleKey.RefreshAll();

            // Spatial modes.
            var director = Object.FindAnyObjectByType<SpatialWorkspaceDirector>(FindObjectsInactive.Include);
            if (director != null) director.SetFlags(c.floorMode, c.ceilingMode);

            // Wall Habitat (Phase 3): every flag off by default; a dependent flag is off unless its parent is on.
            foreach (var w in Object.FindObjectsByType<MuseDesk.Wall.WallHabitatController>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                w.SetFlags(new MuseDesk.Spatial.Wall.WallFeatureFlags
                {
                    wallHabitatEnabled = c.wallHabitat,
                    wallManagerEnabled = c.wallHabitat && c.wallManager,
                    agentSelectEnabled = c.wallHabitat && c.agentSelect,
                    agentBtwEnabled = c.wallHabitat && c.agentSelect && c.agentBtw,
                    largeAgentPortalEnabled = c.wallHabitat && c.agentSelect && c.largeAgentPortal,
                    wallElevatorVfxEnabled = c.wallHabitat && c.wallElevatorVfx,
                });

            // Pull gesture: only meaningful with the ceiling mode.
            bool pull = c.ceilingPullGesture && c.ceilingMode;
            foreach (var g in Object.FindObjectsByType<CeilingPullGesture>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                SetEnabled(g, pull);

            // Ceiling VFX: the controller honours these every frame (it would re-activate the cone itself).
            foreach (var v in Object.FindObjectsByType<CeilingDeploymentVFXController>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                v.particlesEnabled = c.ceilingParticles;
                v.portalConeEnabled = c.ceilingPortalCone;
                if (!c.ceilingParticles)
                    foreach (var ps in v.GetComponentsInChildren<ParticleSystem>(true))
                        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }

            // Scan grids: stop the reveal's Update (which is the only thing that shows its quad) and hide the quad.
            foreach (var r in Object.FindObjectsByType<CeilingScanReveal>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                SetEnabled(r, c.placementScanGrids);
                if (!c.placementScanGrids) HideQuad(r.Quad);
            }
            foreach (var r in Object.FindObjectsByType<FloorScanReveal>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                SetEnabled(r, c.placementScanGrids);
                if (!c.placementScanGrids) HideQuad(r.Quad);
            }

            // Voice.
            foreach (var d in Object.FindObjectsByType<VoiceDecisionController>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                SetFlag(d, d.voiceEnabled, c.voice, x => d.voiceEnabled = x);
            foreach (var gest in Object.FindObjectsByType<VoiceGestureActivator>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                SetFlag(gest, gest.gestureEnabled, c.voice, x => gest.gestureEnabled = x);
                SetEnabled(gest, c.voice);
            }
            foreach (var cc in Object.FindObjectsByType<VoiceCommandCenter>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                SetEnabled(cc, c.voice);
            foreach (var w in Object.FindObjectsByType<WitVoiceSource>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                SetEnabled(w, c.voice);
            foreach (var hud in Object.FindObjectsByType<VoiceHud>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (!c.voice) { hud.SetMic(VoiceMicVisual.Off); _micHidden = true; }
                else if (_micHidden) { hud.SetMic(VoiceMicVisual.Idle); }
            }
            if (c.voice) _micHidden = false;
        }

        static void HideQuad(Transform quad)
        {
            if (quad == null) return;
            var mr = quad.GetComponent<MeshRenderer>();
            if (mr != null) mr.enabled = false;
        }

        // Disable, remembering the authored state; restore it when switched back on.
        static void SetEnabled(Behaviour b, bool on)
        {
            if (b == null) return;
            if (!on)
            {
                if (!Original.ContainsKey(b)) Original[b] = b.enabled;
                b.enabled = false;
            }
            else if (Original.TryGetValue(b, out bool was))
            {
                b.enabled = was;
                Original.Remove(b);
            }
        }

        // Force a bool field off, remembering the authored value; restore it when switched back on.
        static void SetFlag(Object key, bool current, bool on, System.Action<bool> set)
        {
            if (key == null) return;
            if (!on)
            {
                if (!OriginalFlag.ContainsKey(key)) OriginalFlag[key] = current;
                set(false);
            }
            else if (OriginalFlag.TryGetValue(key, out bool was))
            {
                set(was);
                OriginalFlag.Remove(key);
            }
        }
    }
}
