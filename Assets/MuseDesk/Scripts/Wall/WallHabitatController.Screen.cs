using System;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using MuseDesk.Spatial;
using MuseDesk.Spatial.Wall;

namespace MuseDesk.Wall
{
    /// <summary>
    /// rc20 (owner, worn rc19): "we need a way for the computer screen to have an overlay ... so the user can see their
    /// screen as a panel rather than strictly through passthrough". A Screen key beside the dock opens a panel showing the
    /// PC's main screen (live mode: the backend reads it on request, over USB; nothing is stored). The panel has a grab
    /// handle under it like the cards; once moved it opens there again. Offline (the scripted demo) the key says why it
    /// cannot show anything. Presentation only: nothing on the PC can be clicked from the panel.
    /// </summary>
    public partial class WallHabitatController
    {
        [Header("PC screen panel (rc20)")]
        public bool screenPanelEnabled = true;
        [Tooltip("Panel width (m); the height follows the screen's shape.")]
        public float screenPanelWidth = 0.72f;
        [Tooltip("Where the panel opens, in the near-user rig (the eye is about (0, 0.30, 0.62); -Z is away from the user).")]
        public Vector3 screenPanelLocal = new Vector3(0f, 0.36f, -0.30f);
        [Tooltip("Frames asked for per second while the panel is open.")]
        public float screenFps = 5f;
        [Tooltip("Width in pixels asked of the PC (the PC scales the screen down to this).")]
        public int screenPixels = 1280;

        Transform _screenRoot, _screenQuad, _screenHandle;
        Renderer _screenQuadR, _screenHandleR;
        TMP_Text _screenText;
        WallKey _kScreen;
        Material _screenMat;
        Texture2D _screenTex;
        bool _screenOpen, _screenUserMoved;
        volatile bool _screenBusy;
        Vector3 _screenUserLocal;
        float _screenNextAt, _screenAspect = 16f / 9f;
        int _screenHand = -1;
        Vector3 _screenGrabOffset;
        readonly bool[] _screenPrevPinch = new bool[2];
        volatile byte[] _screenPixels;
        int _screenW, _screenH;
        string _screenProblem;

        /// <summary>For drivers: the panel is open / frames shown / the last reason nothing shows.</summary>
        public bool ScreenOpen => _screenOpen;
        public int ScreenFrames { get; private set; }
        public string ScreenProblem => _screenProblem ?? "";
        public Transform ScreenPanel => _screenRoot;

        public void PressScreen()
        {
            if (!screenPanelEnabled) return;
            EnsureScreen();
            if (_screenRoot == null) return;
            _screenOpen = !_screenOpen;
            _screenRoot.gameObject.SetActive(_screenOpen);
            if (_screenOpen)
            {
                PlaceScreen();
                _screenNextAt = 0f;
                if (_backend == null || !_backend.IsLive) ShowScreenWords("Your PC screen shows here when MuseDesk is connected to the PC app.");
            }
            if (_kScreen != null) _kScreen.SetLabel(_screenOpen ? "Hide" : "Screen");
        }

        void EnsureScreen()
        {
            if (!screenPanelEnabled || nearUserRig == null || keyTemplate == null) return;
            if (_kScreen == null) PlaceScreenKey();
            if (_screenRoot != null) return;

            _screenRoot = new GameObject("PcScreenPanel").transform;
            _screenRoot.SetParent(nearUserRig, false);
            var art = ArtHelper();
            float w = screenPanelWidth, h = w / _screenAspect;
            // A dark bezel behind the picture, a little larger, so it reads as a monitor in the room.
            art.Box(_screenRoot, "Bezel", new Vector3(0f, 0f, 0.006f), new Vector3(w + 0.024f, h + 0.024f, 0.01f), new Color(0.13f, 0.13f, 0.15f));
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            var col = quad.GetComponent<Collider>(); if (col != null) Destroy(col);
            quad.name = "Picture";
            _screenQuad = quad.transform;
            _screenQuad.SetParent(_screenRoot, false);
            _screenQuad.localPosition = Vector3.zero;
            _screenQuad.localRotation = Quaternion.identity;     // a quad shows its -Z side: the root's +Z points away from the eye
            _screenQuad.localScale = new Vector3(w, h, 1f);
            _screenQuadR = quad.GetComponent<Renderer>();
            _screenQuadR.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _screenQuadR.receiveShadows = false;
            Shader sh = Shader.Find("Universal Render Pipeline/Unlit");
            var res = Resources.Load<Material>("PcScreenMat");
            _screenMat = res != null ? new Material(res) : new Material(sh != null ? sh : Shader.Find("Unlit/Texture"));
            _screenMat.color = new Color(0.16f, 0.17f, 0.19f);
            _screenQuadR.sharedMaterial = _screenMat;
            _screenText = WallUi.MakeText(_screenRoot, "Words", font, new Vector3(0f, 0f, -0.002f), new Vector2(w * 100f - 6f, h * 100f - 6f), 6f,
                                          new Color(0.85f, 0.87f, 0.9f), TextAlignmentOptions.Center);
            _screenHandleR = WallUi.Plate(_screenRoot, "GrabHandle", new Vector3(0f, -h * 0.5f - 0.022f, 0.002f), 0.09f, 0.009f, 0.004f,
                                          HandleIdle, WallUi.PlateMaterial, art);
            _screenHandle = _screenHandleR.transform;
            _screenRoot.gameObject.SetActive(false);
        }

        /// <summary>The Screen key sits just right of the Spaces dock (or the Wall's own dock).</summary>
        void PlaceScreenKey()
        {
            Transform dock = _dock;
            var console = FindAnyObjectByType<MuseDesk.Spatial.ModeConsole>();
            if (_externalDock && console != null) dock = console.transform;
            Transform parent = dock != null ? dock : nearUserRig;
            Vector3 local = Vector3.zero;
            if (dock != null)
            {
                // Right edge of everything the dock shows, in the dock's own frame.
                float maxX = 0f, midY = 0f; bool any = false;
                foreach (var r in dock.GetComponentsInChildren<Renderer>())
                {
                    if (!r.enabled) continue;
                    Vector3 c = dock.InverseTransformPoint(r.bounds.center);
                    Vector3 e = dock.InverseTransformVector(r.bounds.extents);
                    float x = c.x + Mathf.Abs(e.x);
                    if (!any || x > maxX) { maxX = x; midY = c.y; }
                    any = true;
                }
                local = new Vector3((any ? maxX : 0.1f) + 0.035f, any ? midY : 0f, 0f);
            }
            else local = dockLocal + new Vector3(0.14f, 0f, 0f);
            _kScreen = WallUi.MakeKey(keyTemplate, parent, "Key_Screen", "Screen", local, keyScale, PressScreen);
            _kScreen.Show(true, true);
        }

        void PlaceScreen()
        {
            if (_screenRoot == null) return;
            Vector3 local = _screenUserMoved ? _screenUserLocal : screenPanelLocal;
            _screenRoot.localPosition = local;
            Transform h = ResolveHead();
            Vector3 eye = h != null ? h.position : nearUserRig.TransformPoint(new Vector3(0f, 0.30f, 0.62f));
            _screenRoot.rotation = ScreenFacing(_screenRoot.position, eye);
        }

        static Quaternion ScreenFacing(Vector3 pos, Vector3 eye)
        {
            Vector3 away = pos - eye;                     // +Z of the root points away from the eye
            away.y *= 0.5f;                               // a gentle tilt only
            return Quaternion.LookRotation(away.sqrMagnitude < 1e-6f ? Vector3.forward : away.normalized, Vector3.up);
        }

        void ShowScreenWords(string s)
        {
            _screenProblem = s;
            if (_screenText != null) _screenText.text = s ?? "";
        }

        void TickScreen(float dt)
        {
            if (!screenPanelEnabled) return;
            if (_kScreen == null && nearUserRig != null && keyTemplate != null) EnsureScreen();
            if (!_screenOpen || _screenRoot == null) return;
            TickScreenDrag(dt);

            // A decoded frame is waiting: upload it (main thread), then ask for the next one.
            byte[] px = _screenPixels;
            if (px != null)
            {
                _screenPixels = null;
                if (_screenTex == null || _screenTex.width != _screenW || _screenTex.height != _screenH)
                {
                    if (_screenTex != null) Destroy(_screenTex);
                    _screenTex = new Texture2D(_screenW, _screenH, TextureFormat.BGRA32, false) { name = "PcScreen", wrapMode = TextureWrapMode.Clamp };
                    _screenMat.mainTexture = _screenTex;
                    _screenMat.SetTexture("_BaseMap", _screenTex);
                    _screenMat.mainTextureScale = new Vector2(1f, -1f);            // the PC sends the top row first
                    _screenMat.mainTextureOffset = new Vector2(0f, 1f);
                    _screenMat.SetTextureScale("_BaseMap", new Vector2(1f, -1f));
                    _screenMat.SetTextureOffset("_BaseMap", new Vector2(0f, 1f));
                    _screenMat.color = Color.white;
                    float aspect = _screenW / (float)Mathf.Max(1, _screenH);
                    if (Mathf.Abs(aspect - _screenAspect) > 0.01f) ResizeScreen(aspect);
                }
                if (px.Length == _screenW * _screenH * 4)
                {
                    _screenTex.LoadRawTextureData(px);
                    _screenTex.Apply(false, false);
                    ScreenFrames++;
                    if (!string.IsNullOrEmpty(_screenProblem)) ShowScreenWords("");
                }
            }

            if (_screenBusy || Time.unscaledTime < _screenNextAt) return;
            var gateway = _backend as WallBackendGateway;
            UnityWebRequest req = gateway != null ? gateway.ScreenFrameRequest(screenPixels) : null;
            if (req == null)
            {
                ShowScreenWords("Your PC screen shows here when MuseDesk is connected to the PC app.");
                _screenNextAt = Time.unscaledTime + 1f;
                return;
            }
            _screenBusy = true;
            _screenNextAt = Time.unscaledTime + 1f / Mathf.Max(0.5f, screenFps);
            var op = req.SendWebRequest();
            op.completed += _ =>
            {
                long code = req.responseCode;
                bool ok = req.result == UnityWebRequest.Result.Success;
                byte[] data = ok ? req.downloadHandler.data : null;
                int w = 0, h = 0;
                int.TryParse(req.GetResponseHeader("X-Width") ?? "", out w);
                int.TryParse(req.GetResponseHeader("X-Height") ?? "", out h);
                req.Dispose();
                if (this == null) return;
                if (!ok || data == null || w <= 0 || h <= 0)
                {
                    _screenBusy = false;
                    ShowScreenWords(code == 403 ? "Screen sharing is off on the PC (the backend was started with -NoScreen)."
                                  : "The PC screen could not be read" + (code > 0 ? " (" + code + ")." : "."));
                    _screenNextAt = Time.unscaledTime + 1.5f;
                    return;
                }
                // Inflate off the main thread; the upload happens in TickScreen.
                Task.Run(() =>
                {
                    byte[] raw = null;
                    try { raw = Inflate(data, w * h * 4); } catch (Exception) { raw = null; }
                    if (raw != null) { _screenW = w; _screenH = h; _screenPixels = raw; }
                    _screenBusy = false;
                });
            };
        }

        /// <summary>zlib (RFC 1950) to raw bytes: skip the 2-byte header, inflate the deflate body.</summary>
        static byte[] Inflate(byte[] z, int expected)
        {
            if (z == null || z.Length < 6) return null;
            var outBuf = new byte[expected];
            using (var ms = new MemoryStream(z, 2, z.Length - 2))
            using (var ds = new DeflateStream(ms, CompressionMode.Decompress))
            {
                int got = 0;
                while (got < expected)
                {
                    int n = ds.Read(outBuf, got, expected - got);
                    if (n <= 0) break;
                    got += n;
                }
                return got == expected ? outBuf : null;
            }
        }

        void ResizeScreen(float aspect)
        {
            _screenAspect = aspect;
            float w = screenPanelWidth, h = w / aspect;
            if (_screenQuad != null) _screenQuad.localScale = new Vector3(w, h, 1f);
            var bezel = _screenRoot.Find("Bezel");
            if (bezel != null) bezel.localScale = new Vector3(w + 0.024f, h + 0.024f, 0.01f);
            if (_screenHandle != null) _screenHandle.localPosition = new Vector3(0f, -h * 0.5f - 0.022f, 0.002f);
        }

        void TickScreenDrag(float dt)
        {
            if (_screenHandle == null) return;
            Transform head = ResolveHead();
            Vector3 eye = head != null ? head.position : nearUserRig.TransformPoint(new Vector3(0f, 0.30f, 0.62f));
            bool close = false;
            for (int i = 0; i < 2; i++)
            {
                AimHandSample s = _simOn[i] ? _sim[i] : ReadHand(i);
                bool pinch = s.tracked && s.pinching;
                bool began = pinch && !_screenPrevPinch[i];
                _screenPrevPinch[i] = pinch;
                if (_screenHand == i)
                {
                    if (!pinch)
                    {
                        _screenHand = -1;
                        _screenUserMoved = true;
                        _screenUserLocal = nearUserRig.InverseTransformPoint(_screenRoot.position);
                        continue;
                    }
                    Vector3 target = s.origin + _screenGrabOffset;
                    Vector3 pos = Vector3.Lerp(_screenRoot.position, target, 1f - Mathf.Exp(-dt * 18f));
                    _screenRoot.SetPositionAndRotation(pos, ScreenFacing(pos, eye));
                    close = true;
                    continue;
                }
                if (_screenHand >= 0 || !s.tracked) continue;
                bool near = (s.origin - _screenHandle.position).sqrMagnitude <= cardGrabReach * cardGrabReach;
                close |= near;
                if (began && near && i != _carryHand && i != _aimHand && !HandBusyWithCard(i))
                {
                    _screenHand = i;
                    _screenGrabOffset = _screenRoot.position - s.origin;
                    Note("Moving the screen");
                }
            }
            if (_screenHandleR != null) ArtHelper().Tint(_screenHandleR, _screenHand >= 0 ? WallHabitatView.Teal : close ? HandleHover : HandleIdle);
        }

        /// <summary>For drivers: move the panel by <paramref name="delta"/> (world m) as a pinch on its handle would.</summary>
        public bool DriverDragScreen(Vector3 delta)
        {
            if (_screenRoot == null || !_screenOpen) return false;
            Transform head = ResolveHead();
            Vector3 pos = _screenRoot.position + delta;
            _screenRoot.SetPositionAndRotation(pos, ScreenFacing(pos, head != null ? head.position : pos + Vector3.back));
            _screenUserMoved = true;
            _screenUserLocal = nearUserRig.InverseTransformPoint(pos);
            return true;
        }
    }
}
