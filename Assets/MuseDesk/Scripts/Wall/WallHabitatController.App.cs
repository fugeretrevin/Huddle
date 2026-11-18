using TMPro;
using UnityEngine;
using MuseDesk.Spatial.Wall;

namespace MuseDesk.Wall
{
    /// <summary>
    /// rc25 (owner: "add a window for that launcher, with a way to close it ... so we can test 'make an app that does X' and
    /// then we can see it in MuseDesk"): an Open app / Close app key under the Screen panel. Open asks the PC to open the
    /// program the agents built in this session (newest work) in a window; the PC then sends only that window, so the
    /// panel shows the app. A fingertip pressed onto the picture taps the app on the PC (its buttons, its canvas).
    /// The window also closes from its own Close app key on the PC. Nothing here knows what the app is.
    /// </summary>
    public partial class WallHabitatController
    {
        Transform _appPivot;
        WallKey _kApp;
        TMP_Text _appCaption;
        readonly ScreenTapLogic _appTaps = new ScreenTapLogic(2);
        bool _screenIsApp, _appBusy;
        float _appCaptionUntil;
        Transform _tapDot;
        float _tapDotT = 1f;
        // The cloned key's face looks along its parent's -Z, like the panel's picture (checked in Play: at 180 only its back showed).
        const float AppKeyYaw = 0f;

        /// <summary>For drivers: the panel shows the agents' app (not the whole screen) / taps sent to it / the last words.</summary>
        public bool ScreenShowsApp => _screenIsApp;
        public int AppTapsSent { get; private set; }
        public string AppCaption => _appCaption != null ? _appCaption.text : "";

        void EnsureAppKey()
        {
            if (_kApp != null || nearUserRig == null || keyTemplate == null || _screenRoot == null) return;
            // The keys face +Z of their parent (toward the user); the panel's +Z points away from the eye. A pivot under the
            // rig (always active: pokes are never SetActive'd) follows the panel, turned to face the eye.
            _appPivot = new GameObject("PcScreenAppKeys").transform;
            _appPivot.SetParent(nearUserRig, false);
            _kApp = WallUi.MakeKey(keyTemplate, _appPivot, "Key_App", "Open app", Vector3.zero, keyScale, PressApp, 1.9f);
            _kApp.Show(false, true);
            _appCaption = WallUi.MakeText(_screenRoot, "AppCaption", font, Vector3.zero, new Vector2(40f, 4f), 4.2f,
                                          new Color(0.86f, 0.88f, 0.9f), TextAlignmentOptions.Left);
            _tapDot = GameObject.CreatePrimitive(PrimitiveType.Sphere).transform;
            var c = _tapDot.GetComponent<Collider>(); if (c != null) Destroy(c);
            _tapDot.name = "TapDot";
            _tapDot.SetParent(_screenRoot, false);
            var r = _tapDot.GetComponent<Renderer>();
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            Shader sh = Shader.Find("Universal Render Pipeline/Unlit");
            if (sh != null) r.sharedMaterial = new Material(sh) { color = WallHabitatView.Teal };
            _tapDot.localScale = Vector3.zero;
        }

        /// <summary>Key target: open the newest app the agents built, or close the one that is open.</summary>
        public void PressApp()
        {
            var gateway = _backend as WallBackendGateway;
            if (_appBusy) return;
            if (gateway == null || !gateway.IsLive) { AppSay("Apps open on the PC when MuseDesk is connected to the PC app."); return; }
            _appBusy = true;
            if (_screenIsApp)
            {
                _kApp?.SetLabel("Closing...");
                gateway.AppCommand("close", "{}", (ok, words) =>
                {
                    _appBusy = false;
                    if (this == null) return;
                    _screenIsApp = false;
                    _appTaps.Reset();
                    AppSay("The app is closed.");
                    SyncAppKey();
                });
                return;
            }
            _kApp?.SetLabel("Opening...");
            AppSay("Opening the newest app on the PC...", 15f);
            gateway.AppCommand("open", "{}", (ok, words) =>
            {
                _appBusy = false;
                if (this == null) return;
                if (ok)
                {
                    _screenIsApp = true;            // the next frame confirms it (X-App: 1)
                    AppSay((string.IsNullOrEmpty(words) ? "The app" : words) + " is open. Tap it with a fingertip.");
                    if (!_screenOpen) PressScreen();
                    _screenSeq = -1;                 // ask for a fresh frame now: the picture changes to the window
                    _screenNextAt = 0f;
                }
                else AppSay(string.IsNullOrEmpty(words) ? "Nothing opened." : words, 8f);
                SyncAppKey();
            });
        }

        void AppSay(string s, float seconds = 6f)
        {
            if (_appCaption != null) _appCaption.text = s ?? "";
            _appCaptionUntil = Time.unscaledTime + seconds;
        }

        void SyncAppKey()
        {
            if (_kApp == null) return;
            if (!_appBusy) _kApp.SetLabel(_screenIsApp ? "Close app" : "Open app");
            _kApp.Show(_screenOpen, !_appBusy);
        }

        /// <summary>Per frame (TickScreen): the key follows the panel; fingertips on the picture become taps on the app.</summary>
        void TickApp(float dt)
        {
            if (_screenRoot == null) return;
            EnsureAppKey();
            if (_kApp == null) return;
            if (_kApp.shown != _screenOpen || (!_appBusy && _kApp.label != null && _kApp.label.text != (_screenIsApp ? "Close app" : "Open app")))
                SyncAppKey();
            if (_appCaption != null && _appCaptionUntil > 0f && Time.unscaledTime > _appCaptionUntil)
            {
                _appCaption.text = "";
                _appCaptionUntil = 0f;
            }
            if (!_screenOpen) return;

            // Under the panel's lower-left corner; the grab bar stays in the middle and the size handle on the right.
            float w = ScreenWidth, h = w / _screenAspect;
            _appPivot.SetPositionAndRotation(_screenRoot.TransformPoint(new Vector3(-w * 0.5f + 0.045f, -h * 0.5f - 0.026f, -0.004f)),
                                             _screenRoot.rotation * Quaternion.Euler(0f, AppKeyYaw, 0f));
            if (_appCaption != null)
            {
                // A line just above the picture, as wide as it (the key, grab bar and size handle are below).
                _appCaption.rectTransform.sizeDelta = new Vector2(w * 100f, 4f);
                _appCaption.transform.localPosition = new Vector3(0f, h * 0.5f + 0.035f, -0.002f);
            }
            if (_tapDot != null && _tapDotT < 1f)
            {
                _tapDotT = Mathf.Min(1f, _tapDotT + dt / 0.3f);
                _tapDot.localScale = Vector3.one * (0.014f * (1f - _tapDotT));
            }

            if (!_screenIsApp || _screenQuad == null || _screenHand >= 0 || _screenSizeHand >= 0) { _appTaps.Reset(); return; }
            var tips = MuseDesk.Agents.HandFingertips.Get();
            int finger = 0;
            for (int i = 1; i < tips.Count && finger < 2; i += 5, finger++)      // thumb, INDEX, middle, ring, pinky per hand
            {
                Transform t = tips[i];
                bool tracked = t != null && t.gameObject.activeInHierarchy;
                Vector3 p = tracked ? _screenQuad.InverseTransformPoint(t.position) : Vector3.zero;
                // The quad's local z is in the panel's units; the eye is on its -Z side.
                float depth = p.z * _screenQuad.lossyScale.z;
                if (_appTaps.Step(finger, tracked, p.x, p.y, depth, out float u, out float v)) SendAppTap(u, v, p);
            }
        }

        void SendAppTap(float u, float v, Vector3 quadLocal)
        {
            var gateway = _backend as WallBackendGateway;
            if (gateway == null) return;
            AppTapsSent++;
            if (_tapDot != null && _screenQuad != null)
            {
                _tapDot.position = _screenQuad.TransformPoint(new Vector3(quadLocal.x, quadLocal.y, 0f)) - _screenRoot.forward * 0.003f;
                _tapDotT = 0f;
            }
            MuseDesk.Feedback.FeedbackController.PlayCue(MuseDesk.Feedback.FeedbackCue.KeyPress);
            gateway.AppCommand("tap", "{\"u\":" + u.ToString("0.0000", System.Globalization.CultureInfo.InvariantCulture)
                                      + ",\"v\":" + v.ToString("0.0000", System.Globalization.CultureInfo.InvariantCulture) + "}", null);
        }

        /// <summary>For drivers: a tap at (u, v) as a fingertip on the picture would send it.</summary>
        public bool DriverTapApp(float u, float v)
        {
            if (!_screenOpen || !_screenIsApp) return false;
            SendAppTap(u, v, new Vector3(u - 0.5f, 0.5f - v, 0f));
            return true;
        }
    }
}
