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
        public float screenFps = 20f;
        [Tooltip("Width in pixels asked of the PC (the PC scales the screen down to this).")]
        public int screenPixels = 1280;

        Transform _screenRoot, _screenQuad, _screenHandle, _screenSize;
        Renderer _screenQuadR, _screenHandleR, _screenSizeR;
        // rc22: upright panel + resize handle at the lower-right corner. The width is remembered with the position.
        const float SizeOff = 0.02f, MinW = 0.35f, MaxW = 1.6f;
        float _screenWidth;
        int _screenSizeHand = -1;
        Vector3 _rsUL, _rsGrabOff;
        /// <summary>For drivers: the panel width now (m).</summary>
        public float ScreenWidth => _screenWidth > 0f ? _screenWidth : screenPanelWidth;
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
        // rc21: frames come through reused buffers (no per-frame arrays): two for the compressed bytes, three for decoded pixels.
        readonly byte[][] _screenZ = new byte[2][];
        readonly byte[][] _screenPx = new byte[3][];
        int _screenZWrite;
        volatile int _screenReady = -1, _screenHold = -1;
        volatile bool _screenDecoding;
        int _screenSeq = -1, _screenFpsCount;
        float _screenFpsAt;
        int _screenW, _screenH;
        /// <summary>For drivers: frames uploaded per second, measured over the last second while the panel is open.</summary>
        public float ScreenFps { get; private set; }
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
                _screenSeq = -1;
                if (_backend == null || !_backend.IsLive) ShowScreenWords("Your PC screen shows here when MuseDesk is connected to the PC app.");
            }
            if (_kScreen != null) _kScreen.SetLabel(_screenOpen ? "Hide\nscreen" : "Screen");   // "Hide screen" (11 chars) does not fit one line at the 5.5 mm key floor (FitsOnKey): two lines
        }

        void EnsureScreen()
        {
            if (!screenPanelEnabled || nearUserRig == null || keyTemplate == null) return;
            if (_kScreen == null) PlaceScreenKey();
            if (_screenRoot != null) return;

            _screenRoot = new GameObject("PcScreenPanel").transform;
            _screenRoot.SetParent(nearUserRig, false);
            var art = ArtHelper();
            float w = ScreenWidth, h = w / _screenAspect;
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
            _screenSizeR = WallUi.Plate(_screenRoot, "SizeHandle", new Vector3(w * 0.5f + SizeOff, -h * 0.5f - SizeOff, 0.002f), 0.03f, 0.03f, 0.004f,
                                        HandleIdle, WallUi.PlateMaterial, art);
            _screenSize = _screenSizeR.transform;
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
            MoveScreenKeyToFocusSlot();          // rc21: the Focus key's slot, when the dock has one
        }

        void PlaceScreen()
        {
            if (_screenRoot == null) return;
            LayoutScreen();
            Vector3 local = _screenUserMoved ? _screenUserLocal : screenPanelLocal;
            _screenRoot.localPosition = local;
            Transform h = ResolveHead();
            Vector3 eye = h != null ? h.position : nearUserRig.TransformPoint(new Vector3(0f, 0.30f, 0.62f));
            _screenRoot.rotation = ScreenFacing(_screenRoot.position, eye);
        }

        static Quaternion ScreenFacing(Vector3 pos, Vector3 eye)
        {
            Vector3 away = pos - eye;                     // +Z of the root points away from the eye
            away.y = 0f;                                  // rc22: always upright (yaw only: no pitch, no roll)
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
            HideFocusKey();
            if (!_screenOpen || _screenRoot == null) return;
            TickScreenDrag(dt);

            // A decoded frame is waiting: upload it (main thread). The next request is already on its way.
            int ready = _screenReady;
            if (ready >= 0)
            {
                _screenHold = ready;
                _screenReady = -1;
                byte[] px = _screenPx[ready];
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
                if (px != null && px.Length == _screenW * _screenH * 4)
                {
                    _screenTex.LoadRawTextureData(px);
                    _screenTex.Apply(false, false);
                    ScreenFrames++;
                    _screenFpsCount++;
                    if (!string.IsNullOrEmpty(_screenProblem)) ShowScreenWords("");
                }
                _screenHold = -1;
            }
            float now = Time.unscaledTime;
            if (now - _screenFpsAt >= 1f) { ScreenFps = _screenFpsCount / (now - _screenFpsAt); _screenFpsCount = 0; _screenFpsAt = now; }

            if (_screenBusy || now < _screenNextAt) return;
            var gateway = _backend as WallBackendGateway;
            UnityWebRequest req = gateway != null ? gateway.ScreenFrameRequest(screenPixels, _screenSeq) : null;
            if (req == null)
            {
                ShowScreenWords("Your PC screen shows here when MuseDesk is connected to the PC app.");
                _screenNextAt = now + 1f;
                return;
            }
            _screenBusy = true;
            // A scene saved with the rc20 default (5) would hold the panel back: anything under 10 means "the new default".
            _screenNextAt = now + 1f / (screenFps < 10f ? 20f : screenFps);
            // The compressed frame lands in a reused buffer (no per-frame array); one is decoding while the other fills.
            int zi = _screenZWrite;
            var dl = new ScreenDownload(_screenZ[zi]);
            req.downloadHandler = dl;
            var op = req.SendWebRequest();
            op.completed += _ =>
            {
                long code = req.responseCode;
                bool ok = req.result == UnityWebRequest.Result.Success;
                int w = 0, h = 0, seq = -1;
                if (ok && code == 200)
                {
                    int.TryParse(req.GetResponseHeader("X-Width") ?? "", out w);
                    int.TryParse(req.GetResponseHeader("X-Height") ?? "", out h);
                    int.TryParse(req.GetResponseHeader("X-Seq") ?? "", out seq);
                }
                int len = dl.Length;
                _screenZ[zi] = dl.Buffer;
                req.Dispose();
                if (this == null) return;
                if (ok && code == 304)
                {
                    _screenBusy = false;                 // the picture has not changed: ask again
                    _screenNextAt = Time.unscaledTime;
                    return;
                }
                if (!ok || len <= 0 || w <= 0 || h <= 0)
                {
                    _screenBusy = false;
                    _screenSeq = -1;
                    ShowScreenWords(code == 403 ? "Screen sharing is off on the PC (the backend was started with -NoScreen)."
                                  : "The PC screen could not be read" + (code > 0 ? " (" + code + ")." : "."));
                    _screenNextAt = Time.unscaledTime + 1.5f;
                    return;
                }
                _screenSeq = seq;
                _screenBusy = false;                      // the next request goes out now, while this frame decodes
                if (_screenDecoding) return;              // still decoding the last one: drop this frame
                _screenDecoding = true;
                _screenZWrite = zi ^ 1;
                byte[] z = _screenZ[zi];
                Task.Run(() =>
                {
                    try
                    {
                        int need = w * h * 4;
                        int bi = 0;
                        while (bi == _screenHold || bi == _screenReady) bi++;      // three buffers: one is always free
                        byte[] outBuf = _screenPx[bi];
                        if (outBuf == null || outBuf.Length != need) _screenPx[bi] = outBuf = new byte[need];
                        if (Inflate(z, len, outBuf))
                        {
                            _screenW = w; _screenH = h;
                            _screenReady = bi;
                        }
                    }
                    catch (Exception) { }
                    finally { _screenDecoding = false; }
                });
            };
        }

        /// <summary>The compressed frame, received into a reused buffer (grown only when a frame is bigger than any before).</summary>
        sealed class ScreenDownload : DownloadHandlerScript
        {
            static readonly byte[] Chunk = new byte[64 * 1024];
            public byte[] Buffer;
            public int Length;
            public ScreenDownload(byte[] buffer) : base(Chunk) { Buffer = buffer != null && buffer.Length > 0 ? buffer : new byte[1 << 20]; }
            protected override bool ReceiveData(byte[] data, int dataLength)
            {
                if (data == null || dataLength <= 0) return true;
                if (Length + dataLength > Buffer.Length) Array.Resize(ref Buffer, Mathf.Max(Buffer.Length * 2, Length + dataLength));
                System.Buffer.BlockCopy(data, 0, Buffer, Length, dataLength);
                Length += dataLength;
                return true;
            }
        }

        /// <summary>zlib (RFC 1950) to raw bytes: skip the 2-byte header, inflate the deflate body into <paramref name="outBuf"/>.</summary>
        static bool Inflate(byte[] z, int zLen, byte[] outBuf)
        {
            if (z == null || zLen < 6) return false;
            using (var ms = new MemoryStream(z, 2, zLen - 2))
            using (var ds = new DeflateStream(ms, CompressionMode.Decompress))
            {
                int got = 0, expected = outBuf.Length;
                while (got < expected)
                {
                    int n = ds.Read(outBuf, got, expected - got);
                    if (n <= 0) break;
                    got += n;
                }
                return got == expected;
            }
        }

        // ------------------------------------------------------------------ rc21: Focus key out, Screen key in its slot

        Transform _focusSlotParent;
        Vector3 _focusSlotPos;
        Quaternion _focusSlotRot = Quaternion.identity;
        Vector3 _focusSlotScale = Vector3.one;
        bool _focusSlotKnown;
        Transform _focusHidden;
        int _focusHideTick;

        /// <summary>The Focus key is gone (owner, rc20: "it's just distracting"). It is hidden by scale, never SetActive(false) (poke
        /// interactables break), its pokes are switched off, and this re-asserts it every few frames so nothing re-shows it.</summary>
        void HideFocusKey()
        {
            if (!_focusSlotKnown)
            {
                var console = FindAnyObjectByType<MuseDesk.Spatial.ModeConsole>();
                if (console == null || console.focusKey == null) return;
                Transform fk = console.focusKey;
                _focusSlotParent = fk.parent;
                _focusSlotPos = fk.localPosition;
                _focusSlotRot = fk.localRotation;
                _focusSlotScale = fk.localScale;
                _focusSlotKnown = true;
                _focusHidden = fk;
                if (_kScreen != null) MoveScreenKeyToFocusSlot();
            }
            if (_focusHidden == null) return;
            if ((++_focusHideTick & 7) != 0) return;
            Vector3 hidden = _focusSlotScale * Workspace.ScaleEase.HiddenFactor;
            if ((_focusHidden.localScale - hidden).sqrMagnitude > 1e-12f) _focusHidden.localScale = hidden;
            if (_focusHideTick < 64 || (_focusHideTick & 63) == 0)
                foreach (var p in _focusHidden.GetComponentsInChildren<Oculus.Interaction.PokeInteractable>(true))
                    if (p.enabled) p.enabled = false;
        }

        void MoveScreenKeyToFocusSlot()
        {
            if (_kScreen == null || _kScreen.root == null || !_focusSlotKnown || _focusSlotParent == null) return;
            _kScreen.root.SetParent(_focusSlotParent, false);
            _kScreen.root.localPosition = _focusSlotPos;
            _kScreen.root.localRotation = _focusSlotRot;
            var console = FindAnyObjectByType<MuseDesk.Spatial.ModeConsole>();
            if (console != null && console.deskKey != null && console.deskKey.root != null)
                _kScreen.shownScale = console.deskKey.shownScale;          // the same size as the other dock keys
            else _kScreen.shownScale = _focusSlotScale;
            _kScreen.root.localScale = _kScreen.shownScale * (_kScreen.shown ? 1f : Workspace.ScaleEase.HiddenFactor);
        }

        /// <summary>For drivers: where the Screen key and the Focus key are, and whether Focus is hidden.</summary>
        public string DriverScreenKeyReport()
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("screenKey=").Append(_kScreen != null && _kScreen.root != null ? _kScreen.root.name : "none");
            if (_kScreen != null && _kScreen.root != null)
                sb.Append(" parent=").Append(_kScreen.root.parent != null ? _kScreen.root.parent.name : "none")
                  .Append(" local=").Append(_kScreen.root.localPosition.ToString("F4")).Append(" rot=").Append(_kScreen.root.localRotation.eulerAngles.ToString("F1"))
                  .Append(" scale=").Append(_kScreen.root.localScale.ToString("F4")).Append(" shown=").Append(_kScreen.shown);
            sb.Append(" | focusKnown=").Append(_focusSlotKnown);
            if (_focusSlotKnown && _focusHidden != null)
                sb.Append(" focusScale=").Append(_focusHidden.localScale.ToString("F5")).Append(" slotLocal=").Append(_focusSlotPos.ToString("F4"))
                  .Append(" slotRot=").Append(_focusSlotRot.eulerAngles.ToString("F1")).Append(" slotScale=").Append(_focusSlotScale.ToString("F4"));
            return sb.ToString();
        }

        void ResizeScreen(float aspect)
        {
            _screenAspect = aspect;
            LayoutScreen();
        }

        /// <summary>Sizes the picture, bezel, text, grab bar and corner handle from the current width and aspect.</summary>
        void LayoutScreen()
        {
            if (_screenRoot == null) return;
            float w = ScreenWidth, h = w / _screenAspect;
            if (_screenQuad != null) _screenQuad.localScale = new Vector3(w, h, 1f);
            var bezel = _screenRoot.Find("Bezel");
            if (bezel != null) bezel.localScale = new Vector3(w + 0.024f, h + 0.024f, 0.01f);
            if (_screenHandle != null) _screenHandle.localPosition = new Vector3(0f, -h * 0.5f - 0.022f, 0.002f);
            if (_screenSize != null) _screenSize.localPosition = new Vector3(w * 0.5f + SizeOff, -h * 0.5f - SizeOff, 0.002f);
            if (_screenText != null)
            {
                _screenText.rectTransform.sizeDelta = new Vector2(Mathf.Max(1f, w * 100f - 6f), Mathf.Max(1f, h * 100f - 6f));
                _screenText.fontSize = 6f * w / 0.72f;
            }
        }

        /// <summary>Sets the width (clamped) keeping the upper-left corner where it is.</summary>
        void SetScreenWidth(float width)
        {
            if (_screenRoot == null) return;
            float sc = nearUserRig != null ? nearUserRig.lossyScale.x : 1f;
            Quaternion q = _screenRoot.rotation;
            float w0 = ScreenWidth, h0 = w0 / _screenAspect;
            Vector3 ul = _screenRoot.position + q * new Vector3(-w0 * 0.5f, h0 * 0.5f, 0f) * sc;
            _screenWidth = Mathf.Clamp(width, MinW, MaxW);
            LayoutScreen();
            float w = _screenWidth, h = w / _screenAspect;
            _screenRoot.position = ul + q * new Vector3(w * 0.5f, -h * 0.5f, 0f) * sc;
        }

        void SaveScreenPlace()
        {
            _screenUserMoved = true;
            _screenUserLocal = nearUserRig.InverseTransformPoint(_screenRoot.position);
        }

        /// <summary>For drivers: set the panel width in metres (clamped 0.35-1.6), as the corner handle would.</summary>
        public bool DriverResizeScreen(float widthMetres)
        {
            if (_screenRoot == null || !_screenOpen) return false;
            SetScreenWidth(widthMetres);
            SaveScreenPlace();
            return true;
        }

        void TickScreenDrag(float dt)
        {
            if (_screenHandle == null) return;
            Transform head = ResolveHead();
            Vector3 eye = head != null ? head.position : nearUserRig.TransformPoint(new Vector3(0f, 0.30f, 0.62f));
            bool close = false, sizeNear = false;
            for (int i = 0; i < 2; i++)
            {
                AimHandSample s = _simOn[i] ? _sim[i] : ReadHand(i);
                bool pinch = s.tracked && s.pinching;
                bool began = pinch && !_screenPrevPinch[i];
                _screenPrevPinch[i] = pinch;
                if (_screenSizeHand == i)
                {
                    if (!pinch) { _screenSizeHand = -1; SaveScreenPlace(); continue; }
                    float sc = nearUserRig.lossyScale.x;
                    Vector3 rel = Quaternion.Inverse(_screenRoot.rotation) * (s.origin - _rsUL) / sc + _rsGrabOff;
                    Vector2 dir = new Vector2(1f, -1f / _screenAspect);
                    float nw = (rel.x - SizeOff) * dir.x + (rel.y + SizeOff) * dir.y;
                    nw /= dir.sqrMagnitude;
                    SetScreenWidth(Mathf.Lerp(ScreenWidth, nw, 1f - Mathf.Exp(-dt * 18f)));
                    close = true;
                    continue;
                }
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
                if (_screenHand >= 0 || _screenSizeHand >= 0 || !s.tracked) continue;
                if (_screenSize != null && (s.origin - _screenSize.position).sqrMagnitude <= cardGrabReach * cardGrabReach)
                {
                    sizeNear = true;
                    if (began && i != _carryHand && i != _aimHand && !HandBusyWithCard(i))
                    {
                        float sc0 = nearUserRig.lossyScale.x;
                        float w0 = ScreenWidth, h0 = w0 / _screenAspect;
                        _rsUL = _screenRoot.position + _screenRoot.rotation * new Vector3(-w0 * 0.5f, h0 * 0.5f, 0f) * sc0;
                        Vector3 handLocal = Quaternion.Inverse(_screenRoot.rotation) * (s.origin - _rsUL) / sc0;
                        _rsGrabOff = new Vector3(w0 + SizeOff, -(h0 + SizeOff), 0f) - handLocal;
                        _screenSizeHand = i;
                        Note("Resizing the screen");
                        continue;
                    }
                }
                bool near = (s.origin - _screenHandle.position).sqrMagnitude <= cardGrabReach * cardGrabReach;
                close |= near;
                if (began && near && i != _carryHand && i != _aimHand && !HandBusyWithCard(i))
                {
                    _screenHand = i;
                    _screenGrabOffset = _screenRoot.position - s.origin;
                    Note("Moving the screen");
                }
            }
            if (_screenHandleR != null) ArtHelper().Tint(_screenHandleR, _screenHand >= 0 ? WallHabitatView.Teal : close && !sizeNear && _screenSizeHand < 0 ? HandleHover : HandleIdle);
            if (_screenSizeR != null) ArtHelper().Tint(_screenSizeR, _screenSizeHand >= 0 ? WallHabitatView.Teal : sizeNear ? HandleHover : HandleIdle);
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
