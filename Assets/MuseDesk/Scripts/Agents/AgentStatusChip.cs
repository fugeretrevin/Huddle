using System.Text;
using TMPro;
using UnityEngine;

namespace MuseDesk.Agents
{
    /// <summary>
    /// The small sign that appears above a character the user has looked at steadily: a dark
    /// rounded plate with a role-colour strip, the role name, LIVE or SCRIPTED DEMO, and what the
    /// character is doing in the same words as its desk nameplate. The words are given by
    /// AgentAttention from the reducer's state; nothing is made up here.
    ///
    /// Like the speech bubble it does not place itself: AgentAttention puts it above the character
    /// (upright, yaw only, scaled for the viewing distance) and multiplies its scale by
    /// <see cref="Ease"/>. Origin at the bottom centre.
    /// </summary>
    [DisallowMultipleComponent]
    public class AgentStatusChip : MonoBehaviour
    {
        public MeshFilter plate;
        public MeshRenderer plateRenderer;
        public MeshRenderer strip;
        public TMP_Text label;

        [Header("Layout (metres at scale 1)")]
        public float width = 0.084f;
        public float height = 0.031f;
        public float cornerRadius = 0.0042f;
        public float fadeSeconds = 0.2f;

        Mesh _mesh, _stripMesh;
        MaterialPropertyBlock _mpb;
        float _k, _goal;
        readonly StringBuilder _sb = new StringBuilder(96);
        string _role, _mode, _words;

        public float Width => width;
        public float Height => height;
        public bool Wanted => _goal > 0.5f;
        public bool Visible => _k > 0.0001f || _goal > 0.5f;

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
            _mpb = new MaterialPropertyBlock();
            if (plate != null)
            {
                _mesh = new Mesh { name = "AgentStatusChipPlate" };
                RoundedPlateMesh.Build(_mesh, width, height, cornerRadius, 0f, 0f);
                plate.sharedMesh = _mesh;
            }
            MeshFilter stripFilter = strip != null ? strip.GetComponent<MeshFilter>() : null;
            if (stripFilter != null)
            {
                // The role-colour bar down the left edge (origin at its bottom centre).
                _stripMesh = new Mesh { name = "AgentStatusChipStrip" };
                RoundedPlateMesh.Build(_stripMesh, 0.0032f, Mathf.Max(0.004f, height - 0.009f), 0.0014f, 0f, 0f);
                stripFilter.sharedMesh = _stripMesh;
            }
            SetDrawn(false);
        }

        void OnDestroy()
        {
            if (_mesh != null) Destroy(_mesh);
            if (_stripMesh != null) Destroy(_stripMesh);
        }

        /// <summary>Set the three texts and the strip colour. Rebuilds the label only on a change.</summary>
        public void SetContent(string role, string modeWord, string words, Color roleColor)
        {
            if (ReferenceEquals(role, _role) && ReferenceEquals(modeWord, _mode) && ReferenceEquals(words, _words)) return;
            if (role == _role && modeWord == _mode && words == _words) return;
            _role = role; _mode = modeWord; _words = words;
            if (label != null)
            {
                _sb.Clear();
                // Role 7.5 mm bold, activity 6.5 mm, provenance 5.5 mm (relative sizes, so the
                // label's auto-size can still shrink an unexpectedly long line).
                _sb.Append("<b>").Append(role).Append("</b>");
                _sb.Append('\n').Append("<size=87%>").Append(words).Append("</size>");
                if (!string.IsNullOrEmpty(modeWord)) _sb.Append('\n').Append("<size=73%>").Append(modeWord).Append("</size>");
                label.SetText(_sb);
            }
            if (strip != null)
            {
                if (_mpb == null) _mpb = new MaterialPropertyBlock();
                MuseDesk.Workspace.OfficeTokens.Tint(strip, _mpb, roleColor);
            }
        }

        public void Show() { _goal = 1f; SetDrawn(true); }
        public void Hide() { _goal = 0f; }

        /// <summary>Called once per frame by AgentAttention.</summary>
        public void Tick(float dt)
        {
            if (_k == _goal) return;
            _k = Mathf.MoveTowards(_k, _goal, dt / Mathf.Max(0.05f, fadeSeconds));
            if (_k <= 0f && _goal <= 0f) SetDrawn(false);
        }

        void SetDrawn(bool on)
        {
            if (plateRenderer != null && plateRenderer.enabled != on) plateRenderer.enabled = on;
            if (strip != null && strip.enabled != on) strip.enabled = on;
            if (label != null && label.enabled != on) label.enabled = on;
        }
    }
}
