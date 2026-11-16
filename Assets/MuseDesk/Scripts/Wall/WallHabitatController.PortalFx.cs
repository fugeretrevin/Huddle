using UnityEngine;
using UnityEngine.Rendering;
using MuseDesk.Spatial.Wall;

namespace MuseDesk.Wall
{
    /// <summary>
    /// rc12 (owner: "the portal wasn't animated, it was just an agent growing from nothing"). The portal now uses
    /// Antigravity's floor portal (WallHabitat_LargeAgentPortal: brass and teal pad, Portal_Open_Expand / Active_Idle /
    /// Close_Collapse, sparse motes) plus a soft light column, and the agent arrives inside the column with a small pop,
    /// then steps out with its own Portal_Emerge_Step clip (see <see cref="PortalVisitorLife"/>):
    ///
    ///   Preview / AwaitConfirm ... the validity ring only (lavender, amber unchecked, coral refused), as before;
    ///   Opening .................. the pad opens (Portal_Open_Expand) and the column rises and brightens;
    ///   Emerging ................. the agent pops in at the column's brightest (0.55 -> 1 with a little overshoot), steps
    ///                              out and waves; the column fades;
    ///   Conversing ............... the pad idles (Portal_Active_Idle) with its motes; the column is off;
    ///   Returning ................ the agent turns and waves (Portal_Return_Step), the column brightens again and the agent
    ///                              is drawn up into it (narrower and taller as it shrinks);
    ///   Closing .................. the pad collapses (Portal_Close_Collapse), the column fades.
    /// Small visitors (no large spot) get the same sequence at their size. Presentation only; timings are the PortalFlow's.
    /// </summary>
    public partial class WallHabitatController
    {
        [Header("Portal art (rc12; PortalArtV1 assigns the prefab)")]
        [Tooltip("Antigravity's floor portal prefab (WallHabitat_LargeAgentPortal). Empty: the ring of segments only.")]
        public GameObject portalArt;
        [Tooltip("Reduced-motion builds: the low-effects portal (fewer motes).")]
        public GameObject portalArtLowFx;
        [Range(0f, 1f)] public float portalColumnAlpha = 0.55f;

        Transform _portalPad, _portalColumn;
        MeshRenderer _portalRingSmooth;
        Renderer _portalColumnR;
        Material _portalColumnMat;
        MuseDesk.Art.WallHabitatLargePortalVisualController _portalPadCtl;
        PortalState _portalFxState = PortalState.Hidden;
        PortalVisitorLife _portalLife;
        int _portalHeard = -1;
        bool _portalAsking;
        static readonly int FxBaseColor = Shader.PropertyToID("_BaseColor"), FxColor = Shader.PropertyToID("_Color");

        /// <summary>For drivers: the visitor's life (null until it is out).</summary>
        public PortalVisitorLife PortalLife => _portalLife;
        public bool PortalPadShown => _portalPad != null && _portalPad.gameObject.activeInHierarchy;
        public float PortalColumnAlpha { get; private set; }

        void EnsurePortalFx()
        {
            if (_portalRoot == null) return;
            if (_portalPad == null)
            {
                GameObject art = Comfort && portalArtLowFx != null ? portalArtLowFx : portalArt;
                if (art != null)
                {
                    GameObject pad = Instantiate(art, _portalRoot, false);
                    pad.name = "PortalPad";
                    foreach (var col in pad.GetComponentsInChildren<Collider>(true)) Destroy(col);     // never in the way of a hand ray
                    _portalPadCtl = pad.GetComponent<MuseDesk.Art.WallHabitatLargePortalVisualController>();
                    if (_portalPadCtl != null) { _portalPadCtl.ResolveReferences(); _portalPadCtl.SetComfortReducedMotion(Comfort); }
                    _portalPad = pad.transform;
                    pad.SetActive(false);
                }
            }
            if (_portalColumn == null)
            {
                var go = new GameObject("PortalColumn");
                go.transform.SetParent(_portalRoot, false);
                go.AddComponent<MeshFilter>().sharedMesh = ColumnMesh(28);
                var mr = go.AddComponent<MeshRenderer>();
                mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = false;
                // The pad's own soft particle material (URP Particles/Unlit, transparent): already in the build with the
                // right keywords. A copy with a white texture, so the column is an even glow faded by its vertex alpha.
                Material src = null;
                if (_portalPad != null)
                {
                    var psr = _portalPad.GetComponentInChildren<ParticleSystemRenderer>(true);
                    if (psr != null) src = psr.sharedMaterial;
                }
                _portalColumnMat = src != null ? new Material(src) : new Material(ResolveMaterial());
                _portalColumnMat.name = "PortalColumn_Glow";
                if (_portalColumnMat.HasProperty("_BaseMap")) _portalColumnMat.SetTexture("_BaseMap", Texture2D.whiteTexture);
                if (_portalColumnMat.HasProperty("_MainTex")) _portalColumnMat.SetTexture("_MainTex", Texture2D.whiteTexture);
                if (_portalColumnMat.HasProperty("_Cull")) _portalColumnMat.SetFloat("_Cull", 0f);
                mr.sharedMaterial = _portalColumnMat;
                _portalColumnR = mr;
                _portalColumn = go.transform;
                go.SetActive(false);
            }
        }

        /// <summary>Per frame from TickPortalVisual (after the preview branch): pad, column and the visitor's cues.</summary>
        void TickPortalFx(PortalState st, float k)
        {
            EnsurePortalFx();
            float h = Mathf.Max(0.2f, _portal.Spot.avatarHeight);
            float padScale = Mathf.Clamp(h / 1.05f, 0.3f, 1.2f);
            if (st != _portalFxState)
            {
                PortalState was = _portalFxState;
                _portalFxState = st;
                bool open = st >= PortalState.Opening;
                SetRingVisible(!open);
                if (_portalPad != null)
                {
                    if (open && !_portalPad.gameObject.activeSelf)
                    {
                        _portalPad.localPosition = Vector3.zero;
                        _portalPad.localRotation = Quaternion.identity;
                        _portalPad.localScale = Vector3.one * padScale;
                        _portalPad.gameObject.SetActive(true);
                    }
                    if (st == PortalState.Opening && _portalPadCtl != null) _portalPadCtl.PlayOpen();
                    if (st == PortalState.Closing && _portalPadCtl != null) _portalPadCtl.PlayClose();
                    if (!open && _portalPad.gameObject.activeSelf) _portalPad.gameObject.SetActive(false);
                }
                if (st == PortalState.Emerging && _portalLife != null) _portalLife.Arrive();
                if (st == PortalState.Returning && _portalLife != null) _portalLife.Depart();
                if (was == PortalState.Hidden || st == PortalState.Hidden) { _portalHeard = -1; _portalAsking = false; }
            }

            // The light column: up with the pad, brightest when the agent pops in or leaves, off while talking.
            float a;
            switch (st)
            {
                case PortalState.Opening: a = Mathf.SmoothStep(0f, 1f, k); break;
                case PortalState.Emerging: a = k < 0.35f ? 1f : Mathf.SmoothStep(1f, 0f, (k - 0.35f) / 0.65f); break;
                case PortalState.Returning: a = k < 0.45f ? 0f : Mathf.SmoothStep(0f, 1f, (k - 0.45f) / 0.35f); break;
                case PortalState.Closing: a = Mathf.SmoothStep(1f, 0f, k); break;
                default: a = 0f; break;
            }
            PortalColumnAlpha = a;
            if (_portalColumn != null)
            {
                bool show = a > 0.01f;
                if (_portalColumn.gameObject.activeSelf != show) _portalColumn.gameObject.SetActive(show);
                if (show)
                {
                    float rise = st == PortalState.Opening ? Mathf.SmoothStep(0.15f, 1f, k) : 1f;
                    float r = portalSettings.footprintRadius * padScale * 0.78f;
                    _portalColumn.localPosition = Vector3.up * 0.02f;
                    _portalColumn.localScale = new Vector3(r, h * 1.18f * rise, r);
                    Color c = Color.Lerp(WallHabitatView.Teal, Color.white, 0.35f);
                    c.a = a * portalColumnAlpha;
                    _portalColumnMat.SetColor(FxBaseColor, c);
                    if (_portalColumnMat.HasProperty(FxColor)) _portalColumnMat.SetColor(FxColor, c);
                }
            }

            // The conversation drives the visitor: thinking while a question is out, gesturing when the answer lands.
            if (st == PortalState.Conversing) TickLifeConversation(_portalLife, ref _portalHeard, ref _portalAsking);
        }

        /// <summary>Thinking while a question is out, gesturing for a while when its answer lands (portal and desk visitor).</summary>
        void TickLifeConversation(PortalVisitorLife life, ref int heardCount, ref bool wasAsking)
        {
            if (life == null || _side == null) return;
            bool asking = _side.State == SideAskState.Asking;
            if (asking && !wasAsking) life.Asked();
            wasAsking = asking;
            int heard = _side.History.Count;
            if (heardCount < 0) heardCount = heard;
            else if (heard > heardCount)
            {
                heardCount = heard;
                string reply = _side.History[heard - 1].reply;
                life.Speak(2.5f + (reply != null ? Mathf.Min(6f, reply.Length * 0.035f) : 0f));
            }
        }

        // rc12: the small Select Agents visitor gets the same life on the bones (its root is placed by TickProxy).
        PortalVisitorLife _proxyLife;
        int _proxyHeard = -1;
        bool _proxyAsking;
        public PortalVisitorLife ProxyLife => _proxyLife;

        void AttachProxyLife(float height)
        {
            if (_proxy == null) return;
            _proxyLife = _proxy.gameObject.AddComponent<PortalVisitorLife>();
            _proxyLife.UsePerchClips();
            _proxyLife.height = height;
            _proxyLife.eye = ResolveHead();
            _proxyLife.SetCarried(true);
            _proxyHeard = -1; _proxyAsking = false;
        }

        void SetRingVisible(bool on)
        {
            if (_portalRoot == null) return;
            // The twelve segments only when there is no smooth ring (no unlit accent material).
            bool segs = on && _portalRingSmooth == null;
            for (int i = 0; i < 12 && i < _portalRoot.childCount; i++)
            {
                var r = _portalRoot.GetChild(i).GetComponent<Renderer>();
                if (r != null && r.enabled != segs) r.enabled = segs;
            }
            if (_portalRingSmooth != null && _portalRingSmooth.enabled != on) _portalRingSmooth.enabled = on;
        }

        /// <summary>The agent's scale factor through the sequence (1 = fully out), and its stretch (y / xz).</summary>
        static float PortalGrow(PortalState st, float k, out float stretch)
        {
            stretch = 1f;
            switch (st)
            {
                case PortalState.Emerging:
                {
                    if (k >= 0.4f) return 1f;
                    float t = k / 0.4f;
                    // Ease-out-back: a small overshoot, so it lands with a little bounce instead of inflating.
                    const float c1 = 1.70158f, c3 = c1 + 1f;
                    float e = 1f + c3 * Mathf.Pow(t - 1f, 3f) + c1 * Mathf.Pow(t - 1f, 2f);
                    stretch = 1f + 0.12f * (1f - t);
                    return Mathf.LerpUnclamped(0.55f, 1f, e);
                }
                case PortalState.Conversing: return 1f;
                case PortalState.Returning:
                {
                    if (k < 0.55f) return 1f;
                    float t = Mathf.SmoothStep(0f, 1f, (k - 0.55f) / 0.45f);
                    stretch = 1f + 0.6f * t;
                    return Mathf.Lerp(1f, 0.04f, t);
                }
                default: return 0.001f;
            }
        }

        /// <summary>An open cylinder of unit radius and height, base at 0. Vertex alpha fades from the floor to the top.</summary>
        static Mesh ColumnMesh(int segments)
        {
            int rows = 4;
            var v = new Vector3[(segments + 1) * (rows + 1)];
            var col = new Color[v.Length];
            var tris = new int[segments * rows * 12];
            for (int r = 0; r <= rows; r++)
            {
                float y = r / (float)rows;
                float alpha = Mathf.Pow(1f - y, 1.6f);
                for (int i = 0; i <= segments; i++)
                {
                    float a = i / (float)segments * Mathf.PI * 2f;
                    int idx = r * (segments + 1) + i;
                    v[idx] = new Vector3(Mathf.Cos(a), y, Mathf.Sin(a));
                    col[idx] = new Color(1f, 1f, 1f, alpha);
                }
            }
            int t = 0;
            for (int r = 0; r < rows; r++)
                for (int i = 0; i < segments; i++)
                {
                    int a0 = r * (segments + 1) + i, a1 = a0 + 1, b0 = a0 + segments + 1, b1 = b0 + 1;
                    tris[t++] = a0; tris[t++] = b0; tris[t++] = a1; tris[t++] = a1; tris[t++] = b0; tris[t++] = b1;
                    tris[t++] = a0; tris[t++] = a1; tris[t++] = b0; tris[t++] = a1; tris[t++] = b1; tris[t++] = b0;
                }
            var m = new Mesh { name = "PortalColumn", vertices = v, colors = col, triangles = tris };
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }
    }
}
