using TMPro;
using UnityEngine;

namespace MuseDesk.Agents
{
    /// <summary>
    /// Builds the flat rounded plate used by the speech bubble (with a small tail) and the status
    /// chip (without). The plate lies in the local XY plane, is visible from local -Z (the side
    /// text is read from) and has its origin at the bottom centre (the tip of the tail, if any).
    /// Called only when a bubble's text changes, never per frame.
    /// </summary>
    public static class RoundedPlateMesh
    {
        const int Segments = 5;   // per corner

        /// <summary>Fill <paramref name="mesh"/> with a plate <paramref name="width"/> x
        /// <paramref name="height"/> metres whose bottom edge is <paramref name="tailHeight"/> above
        /// the origin. <paramref name="tailWidth"/> 0 = no tail.</summary>
        public static void Build(Mesh mesh, float width, float height, float radius, float tailWidth, float tailHeight)
        {
            if (mesh == null) return;
            float hw = width * 0.5f;
            float r = Mathf.Clamp(radius, 0f, Mathf.Min(hw, height * 0.5f));
            bool tail = tailWidth > 1e-5f && tailHeight > 1e-5f;
            float y0 = tail ? tailHeight : 0f;
            float y1 = y0 + height;

            int perimeter = 4 * (Segments + 1) + (tail ? 3 : 0);
            var verts = new Vector3[perimeter + 1];
            var normals = new Vector3[perimeter + 1];
            var uvs = new Vector2[perimeter + 1];
            var tris = new int[perimeter * 3];

            verts[0] = new Vector3(0f, (y0 + y1) * 0.5f, 0f);
            int v = 1;
            // Clockwise as seen from -Z (x to the right, y up): top-left, top-right, bottom-right,
            // the tail, bottom-left.
            v = Corner(verts, v, -hw + r, y1 - r, r, 180f, 90f);
            v = Corner(verts, v, hw - r, y1 - r, r, 90f, 0f);
            v = Corner(verts, v, hw - r, y0 + r, r, 0f, -90f);
            if (tail)
            {
                float tw = Mathf.Min(tailWidth * 0.5f, Mathf.Max(0.0005f, hw - r));
                verts[v++] = new Vector3(tw, y0, 0f);
                verts[v++] = new Vector3(0f, 0f, 0f);
                verts[v++] = new Vector3(-tw, y0, 0f);
            }
            v = Corner(verts, v, -hw + r, y0 + r, r, -90f, -180f);

            for (int i = 0; i <= perimeter; i++)
            {
                normals[i] = Vector3.back;
                uvs[i] = new Vector2(verts[i].x / Mathf.Max(1e-5f, width) + 0.5f, verts[i].y / Mathf.Max(1e-5f, y1));
            }
            for (int i = 0; i < perimeter; i++)
            {
                tris[i * 3] = 0;
                tris[i * 3 + 1] = 1 + i;
                tris[i * 3 + 2] = 1 + (i + 1) % perimeter;
            }

            mesh.Clear();
            mesh.vertices = verts;
            mesh.normals = normals;
            mesh.uv = uvs;
            mesh.triangles = tris;
            mesh.RecalculateBounds();
        }

        static int Corner(Vector3[] verts, int v, float cx, float cy, float r, float fromDeg, float toDeg)
        {
            for (int s = 0; s <= Segments; s++)
            {
                float a = Mathf.Lerp(fromDeg, toDeg, s / (float)Segments) * Mathf.Deg2Rad;
                verts[v++] = new Vector3(cx + Mathf.Cos(a) * r, cy + Mathf.Sin(a) * r, 0f);
            }
            return v;
        }
    }

    /// <summary>
    /// A speech bubble above one character: a rounded Paper-coloured plate with Ink text and a small
    /// tail. One per character; a new line replaces the old one. It sizes itself to one to three
    /// short lines (a longer text ends in an ellipsis). It pops in and out by scale.
    ///
    /// It does not place itself: AgentAttention puts it above the character each frame (upright,
    /// turned to the user by yaw only, scaled for the viewing distance, clear of the other bubbles,
    /// the "!" mark and the far-office beacon) and multiplies its scale by <see cref="Ease"/>.
    /// The object is not under the character (the rig is scaled), it sits under WorkspaceRoot.
    /// </summary>
    [DisallowMultipleComponent]
    public class AgentSpeechBubble : MonoBehaviour
    {
        public MeshFilter plate;
        public MeshRenderer plateRenderer;
        public TMP_Text label;

        [Header("Layout (metres at scale 1; text sizes in millimetres)")]
        [Tooltip("Widest text block, centimetres.")]
        public float maxTextWidthCm = 8f;
        public float fontMm = 6.5f;
        public float padX = 0.005f;
        public float padY = 0.0038f;
        public float cornerRadius = 0.004f;
        public float tailWidth = 0.007f;
        public float tailHeight = 0.006f;
        [Tooltip("Text sits this far in front of the plate (toward the reader, local -Z).")]
        public float textProud = 0.0005f;
        public float popSeconds = 0.22f;

        Mesh _mesh;
        float _k, _goal;
        float _hideAt;
        string _text = "";
        string _footer;

        /// <summary>Plate width and total height (tail included), metres at scale 1.</summary>
        public float Width { get; private set; } = 0.05f;
        public float Height { get; private set; } = 0.02f;
        /// <summary>Time.time of the last Show.</summary>
        public float ShownAt { get; private set; }
        public string Text => _text;
        /// <summary>True while the bubble is up or on its way up.</summary>
        public bool Wanted => _goal > 0.5f;
        /// <summary>True while anything of it is drawn.</summary>
        public bool Visible => _k > 0.0001f || _goal > 0.5f;

        /// <summary>Eased 0..1 scale factor of the pop.</summary>
        public float Ease
        {
            get
            {
                float inv = 1f - _k;
                return 1f - inv * inv * inv;
            }
        }

        void Awake()
        {
            SetDrawn(false);
        }

        void OnDestroy()
        {
            if (_mesh != null) Destroy(_mesh);
        }

        /// <summary>Show <paramref name="text"/> for <paramref name="seconds"/>. Replaces what is there.</summary>
        public void Show(string text, float seconds) { Show(text, seconds, null); }

        /// <summary>Show with a provenance footer ("LIVE" / "SCRIPTED DEMO", see <see cref="BubbleProvenance"/>). A null footer
        /// shows the text alone.</summary>
        public void Show(string text, float seconds, string footer)
        {
            if (string.IsNullOrEmpty(text)) { Hide(); return; }
            float now = Time.time;
            bool changed = !string.Equals(text, _text, System.StringComparison.Ordinal)
                           || !string.Equals(footer, _footer, System.StringComparison.Ordinal);
            _text = text;
            _footer = footer;
            if (changed || _mesh == null) Layout();
            if (changed && _k > 0.6f) _k = 0.6f;      // a new line gives a small second pop
            _goal = 1f;
            _hideAt = now + Mathf.Clamp(seconds, 0.5f, 20f);
            ShownAt = now;
            SetDrawn(true);
        }

        /// <summary>Keep the current bubble up at least this much longer.</summary>
        public void Extend(float seconds)
        {
            if (_goal < 0.5f) return;
            _hideAt = Mathf.Max(_hideAt, Time.time + seconds);
        }

        public void Hide() { _goal = 0f; }

        /// <summary>Called once per frame by AgentAttention.</summary>
        public void Tick(float now, float dt)
        {
            if (_goal > 0.5f && now >= _hideAt) _goal = 0f;
            if (_k == _goal) return;
            _k = Mathf.MoveTowards(_k, _goal, dt / Mathf.Max(0.05f, popSeconds));
            if (_k <= 0f && _goal <= 0f) SetDrawn(false);
        }

        void SetDrawn(bool on)
        {
            if (plateRenderer != null && plateRenderer.enabled != on) plateRenderer.enabled = on;
            if (label != null && label.enabled != on) label.enabled = on;
        }

        /// <summary>Fit the text block and rebuild the plate. Editor builders may call it to save a
        /// bubble with a real plate.</summary>
        public void Layout()
        {
            if (label == null) return;
            float lineCm = fontMm * 0.125f;                    // line pitch: 1.25 em, in centimetres
            label.fontSize = fontMm;
            label.enableAutoSizing = false;
            label.textWrappingMode = TextWrappingModes.Normal;
            label.overflowMode = TextOverflowModes.Ellipsis;
            label.alignment = TextAlignmentOptions.Center;
            string shown = BubbleProvenance.Compose(_text, _footer);
            label.text = shown;

            // Width: as narrow as the text allows, at most the widest block.
            Vector2 pref = label.GetPreferredValues(shown, maxTextWidthCm, 0f);
            float wCm = Mathf.Clamp(pref.x + 0.05f, 1.6f, maxTextWidthCm);
            RectTransform rt = label.rectTransform;
            rt.sizeDelta = new Vector2(wCm, lineCm * 12f);
            label.ForceMeshUpdate();
            // Height from the measured text (the label is disabled until the bubble is drawn, and a disabled TMP label reports one
            // line), at most three lines of text plus one for the footer.
            Vector2 prefAtWidth = label.GetPreferredValues(shown, wCm, 0f);
            float textCm = Mathf.Clamp(prefAtWidth.y, lineCm, lineCm * BubbleProvenance.MaxLines(_footer));
            float hCm = textCm + 0.1f;
            rt.sizeDelta = new Vector2(wCm, hCm);              // anything past three lines is cut with an ellipsis
            label.ForceMeshUpdate();

            float w = wCm * 0.01f + padX * 2f;
            float h = hCm * 0.01f + padY * 2f;
            Width = w;
            Height = h + tailHeight;
            rt.localPosition = new Vector3(0f, tailHeight + h * 0.5f, -textProud);

            if (plate != null)
            {
                if (_mesh == null)
                {
                    _mesh = new Mesh { name = "AgentSpeechBubblePlate" };
                    _mesh.MarkDynamic();
                }
                RoundedPlateMesh.Build(_mesh, w, h, cornerRadius, tailWidth, tailHeight);
                plate.sharedMesh = _mesh;
            }
        }
    }
}
