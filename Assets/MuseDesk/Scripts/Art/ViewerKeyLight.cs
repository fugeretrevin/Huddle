using UnityEngine;
using UnityEngine.Rendering;

namespace MuseDesk.Art
{
    /// <summary>
    /// rc18 look pass (owner, worn rc17: "it all looks kinda dull"). The scene had no light at all: every URP Lit material
    /// was shaded by a flat grey skybox ambient, so nothing had form or colour. This adds ONE soft, warm key light that
    /// follows the viewer's heading (yaw only, smoothed): it always comes from over the viewer's shoulder, so the side of the
    /// desk office, the wall habitat and the agents that faces the user is lit wherever they are placed. No shadows (Quest).
    /// It also sets a gradient (trilight) ambient so the shade side stays soft and slightly cool instead of grey.
    /// Presentation only.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public class ViewerKeyLight : MonoBehaviour
    {
        public Light keyLight;
        [Tooltip("Degrees below the horizon the light comes from.")]
        public float elevation = 38f;
        [Tooltip("Degrees to the viewer's right the light comes from (over the shoulder).")]
        public float sideAngle = 28f;
        public Color keyColor = new Color(1f, 0.94f, 0.85f);
        public float keyIntensity = 0.95f;
        [Tooltip("Seconds for the light to follow a head turn (slow: never flickers with small head motion).")]
        public float followSeconds = 1.2f;

        [Header("Ambient (gradient)")]
        public bool setAmbient = true;
        public Color ambientSky = new Color(0.50f, 0.54f, 0.62f);
        public Color ambientEquator = new Color(0.42f, 0.41f, 0.40f);
        public Color ambientGround = new Color(0.26f, 0.24f, 0.22f);

        Transform _head;
        float _yaw;
        bool _yawSet;
        float _nextHeadLookup;

        void Awake()
        {
            if (keyLight == null)
            {
                var go = new GameObject("MuseKeyLight");
                go.transform.SetParent(transform, false);
                keyLight = go.AddComponent<Light>();
            }
            keyLight.type = LightType.Directional;
            keyLight.shadows = LightShadows.None;
            keyLight.color = keyColor;
            keyLight.intensity = keyIntensity;
            keyLight.renderMode = LightRenderMode.ForcePixel;
            keyLight.enabled = true;
            if (setAmbient) ApplyAmbient();
        }

        void OnEnable() { if (keyLight != null) keyLight.enabled = true; }
        void OnDisable() { if (keyLight != null) keyLight.enabled = false; }

        public void ApplyAmbient()
        {
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = ambientSky;
            RenderSettings.ambientEquatorColor = ambientEquator;
            RenderSettings.ambientGroundColor = ambientGround;
            RenderSettings.ambientIntensity = 1f;
            // The ambient probe is what URP actually samples: build it from the gradient so it holds on device without baking.
            var sh = new SphericalHarmonicsL2();
            sh.AddAmbientLight(ambientEquator);
            sh.AddDirectionalLight(Vector3.up, (ambientSky - ambientEquator) * 0.9f, 1f);
            sh.AddDirectionalLight(Vector3.down, (ambientGround - ambientEquator) * 0.9f, 1f);
            RenderSettings.ambientProbe = sh;
        }

        void LateUpdate()
        {
            if (keyLight == null) return;
            if (_head == null && Time.unscaledTime >= _nextHeadLookup)
            {
                _nextHeadLookup = Time.unscaledTime + 0.5f;
                var cam = Camera.main;
                if (cam != null) _head = cam.transform;
            }
            float headYaw = 0f;
            if (_head != null)
            {
                Vector3 f = _head.forward; f.y = 0f;
                if (f.sqrMagnitude > 1e-4f) headYaw = Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg;
            }
            if (!_yawSet) { _yaw = headYaw; _yawSet = true; }
            float k = 1f - Mathf.Exp(-Time.unscaledDeltaTime / Mathf.Max(0.05f, followSeconds));
            _yaw = Mathf.LerpAngle(_yaw, headYaw, k);
            keyLight.transform.rotation = Quaternion.Euler(elevation, _yaw - sideAngle, 0f);
        }
    }
}
