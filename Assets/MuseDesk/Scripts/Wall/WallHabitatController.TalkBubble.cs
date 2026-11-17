using TMPro;
using UnityEngine;
using MuseDesk.Spatial.Wall;

namespace MuseDesk.Wall
{
    /// <summary>
    /// rc21 (owner, worn rc20): "when agents are talking there should be a little speech bubble so you know who is, and a small
    /// head bob". A Paper bubble with a tail above the head of the agent that is with the user (the perched desk visitor or
    /// the life-size portal visitor), showing the first sentence(s) of the line it speaks, up while it talks and ~1.5 s
    /// after, plus a head nod through WallTalkBob. Driven by polling AgentSpeech.SaidCount (no event subscription to leak),
    /// so a line shows even when the voice is unavailable (then for a time based on its length).
    /// Everything is created lazily, once; per frame there are no allocations (a new line rebuilds the plate once).
    /// </summary>
    public partial class WallHabitatController
    {
        const float TalkFontMm = 6.5f, TalkMaxWidthCm = 11f, TalkTail = 0.007f;
        const int TalkMaxChars = 120;

        Transform _talkRoot;
        MeshFilter _talkPlateMf;
        Renderer _talkPlateR, _talkTailR;
        TMP_Text _talkLabel;
        Mesh _talkPlateMesh, _talkTailMesh;
        float _talkW = 0.05f, _talkH = 0.02f;       // plate size in metres at scale 1 (tail excluded)
        float _talkK;                                // 0..1 pop
        string _talkText = "";
        string _talkAgent;
        int _talkSeenCount;
        float _talkStart, _talkEstEnd, _talkLastSpoke;
        bool _talkTtsSeen, _talkHaveLine;
        WallTalkBob _talkBob;
        Transform _talkBobFor;
        Vector3 _talkTop;
        float _talkScale;

        /// <summary>The one hook: call once per frame (end of TickMeet).</summary>
        void TickTalkBubble(float dt)
        {
            string agent = TalkActiveAgent(out Transform avatar, out float height);
            float now = Time.unscaledTime;

            // A new spoken line (AgentSpeech counts every Say, even when the voice itself is unavailable).
            int count = MuseDesk.Voice.AgentSpeech.SaidCount;
            if (count != _talkSeenCount)
            {
                _talkSeenCount = count;
                if (agent != null && string.Equals(MuseDesk.Voice.AgentSpeech.LastSpeaker, agent, System.StringComparison.OrdinalIgnoreCase))
                    BeginTalkLine(agent, MuseDesk.Voice.AgentSpeech.LastSaid, now);
            }

            if (agent == null || !string.Equals(agent, _talkAgent, System.StringComparison.OrdinalIgnoreCase))
            {
                // Not with the user (or a different agent): no bubble, no bob.
                _talkHaveLine = false;
                _talkAgent = agent;
            }

            bool speaking = agent != null && MuseDesk.Voice.AgentSpeech.IsSpeaking(agent);
            if (speaking && _talkHaveLine) { _talkTtsSeen = true; _talkLastSpoke = now; }
            bool talking = _talkHaveLine && (speaking || (!_talkTtsSeen && now < _talkEstEnd));
            float hideAt = _talkTtsSeen ? _talkLastSpoke + 1.5f : _talkEstEnd + 1.5f;
            bool show = _talkHaveLine && avatar != null && now < hideAt;

            // Head bob follows the talk window, on whichever avatar is out.
            if (avatar != null)
            {
                if (_talkBobFor != avatar)
                {
                    _talkBobFor = avatar;
                    _talkBob = avatar.GetComponent<WallTalkBob>();
                    if (_talkBob == null) _talkBob = avatar.gameObject.AddComponent<WallTalkBob>();
                }
                _talkBob.amplitudeDeg = Comfort ? 1.8f : 3.5f;
                _talkBob.Talking = talking;
            }
            else if (_talkBob != null) { _talkBob.Talking = false; }

            _talkK = Mathf.MoveTowards(_talkK, show ? 1f : 0f, dt / (show ? 0.2f : 0.3f));
            if (_talkRoot == null) { if (!show) return; BuildTalkBubble(); }
            bool visible = _talkK > 0.001f && avatar != null;
            if (_talkRoot.gameObject.activeSelf != visible) _talkRoot.gameObject.SetActive(visible);
            if (!visible) return;
            PlaceTalkBubble(avatar, height);
        }

        /// <summary>The agent that is with the user and its avatar (null when none): the portal visitor, else the perched desk visitor.</summary>
        string TalkActiveAgent(out Transform avatar, out float height)
        {
            avatar = null; height = 0f;
            if (_portalAvatar != null && _portal.Agent != null && _portal.State >= PortalState.Emerging && _portal.State <= PortalState.Conversing)
            {
                avatar = _portalAvatar;
                height = 0f;   // measured from the head bone in PlaceTalkBubble (the portal avatar is stretched and lifted)
                return _portal.Agent;
            }
            if (_visit != null && _visit.State == VisitState.Perched && _proxy != null && _visit.Agent != null)
            {
                avatar = _proxy;
                height = AvatarWorldHeight * (_proxy.localScale.x / Mathf.Max(1e-5f, _proxyScaleCarry));
                return _visit.Agent;
            }
            return null;
        }

        void BeginTalkLine(string agent, string line, float now)
        {
            string shortLine = ShortTalkLine(line);
            if (string.IsNullOrEmpty(shortLine)) return;
            _talkAgent = agent;
            _talkHaveLine = true;
            _talkTtsSeen = false;
            _talkStart = now;
            _talkLastSpoke = now;
            // Spoken at roughly 14 characters a second (a floor of 2 s, a cap of 12 s); the full line, not the cut one.
            _talkEstEnd = now + Mathf.Clamp((line != null ? line.Length : 0) / 14f, 2f, 12f);
            if (_talkRoot == null) BuildTalkBubble();
            if (!string.Equals(shortLine, _talkText, System.StringComparison.Ordinal) || _talkPlateMesh == null) LayoutTalkBubble(shortLine);
            if (_talkK > 0.6f) _talkK = 0.6f;   // a new line gives a small second pop
        }

        /// <summary>First sentence(s) up to ~120 characters, ellipsis when cut.</summary>
        public static string ShortTalkLine(string line)
        {
            if (string.IsNullOrEmpty(line)) return "";
            string t = line.Trim().Replace('\n', ' ').Replace('\r', ' ');
            while (t.Contains("  ")) t = t.Replace("  ", " ");
            if (t.Length <= TalkMaxChars) return t;
            // Prefer the end of a sentence inside the limit, else a word boundary.
            int cut = -1;
            for (int i = TalkMaxChars - 1; i >= 40; i--)
                if (t[i] == '.' || t[i] == '!' || t[i] == '?') { cut = i + 1; break; }
            if (cut > 0) return t.Substring(0, cut);
            int sp = t.LastIndexOf(' ', TalkMaxChars - 1);
            if (sp < 40) sp = TalkMaxChars - 1;
            return t.Substring(0, sp).TrimEnd(',', ';', ':', ' ') + "…";
        }

        void BuildTalkBubble()
        {
            Transform parent = nearUserRig != null ? nearUserRig : transform;
            var go = new GameObject("WallTalkBubble");
            go.transform.SetParent(parent, false);
            _talkRoot = go.transform;
            Color paper = WallUi.Hex(0xF7F2E3), ink = WallUi.Hex(0x1F2530);
            _talkPlateR = WallUi.Plate(_talkRoot, "Plate", Vector3.zero, 0.05f, 0.02f, 0.004f, paper, WallUi.PaperMaterial != null ? WallUi.PaperMaterial : WallUi.PlateMaterial, ArtHelper());
            if (_talkPlateR != null) _talkPlateMf = _talkPlateR.GetComponent<MeshFilter>();
            _talkLabel = WallUi.MakeText(_talkRoot, "Words", font, new Vector3(0f, 0f, -0.0005f), new Vector2(TalkMaxWidthCm, 3f), TalkFontMm, ink, TextAlignmentOptions.Center);
            _talkLabel.overflowMode = TextOverflowModes.Ellipsis;
            // The tail: a small triangle under the plate, pointing down at the agent (visible from -Z like the plate).
            var tail = new GameObject("Tail");
            tail.transform.SetParent(_talkRoot, false);
            _talkTailMesh = new Mesh { name = "WallTalkTail" };
            _talkTailMesh.vertices = new[] { new Vector3(-TalkTail * 0.5f, TalkTail, 0f), new Vector3(TalkTail * 0.5f, TalkTail, 0f), Vector3.zero };
            _talkTailMesh.triangles = new[] { 0, 1, 2 };
            _talkTailMesh.normals = new[] { Vector3.back, Vector3.back, Vector3.back };
            _talkTailMesh.RecalculateBounds();
            tail.AddComponent<MeshFilter>().sharedMesh = _talkTailMesh;
            var mr = tail.AddComponent<MeshRenderer>();
            Material mat = _talkPlateR != null ? _talkPlateR.sharedMaterial : null;
            if (mat != null) mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            _talkTailR = mr;
            WallUi.TintPlate(mr, paper);
            go.SetActive(false);
        }

        void LayoutTalkBubble(string text)
        {
            _talkText = text;
            if (_talkLabel == null) return;
            _talkLabel.fontSize = TalkFontMm;
            _talkLabel.text = text;
            Vector2 pref = _talkLabel.GetPreferredValues(text, TalkMaxWidthCm, 0f);
            float wCm = Mathf.Clamp(pref.x + 0.05f, 2.2f, TalkMaxWidthCm);
            Vector2 pref2 = _talkLabel.GetPreferredValues(text, wCm, 0f);
            float lineCm = TalkFontMm * 0.125f;
            float hCm = Mathf.Clamp(pref2.y, lineCm, lineCm * 6f) + 0.15f;      // rc21: was 4 lines max and cut the last one
            _talkLabel.rectTransform.sizeDelta = new Vector2(wCm, hCm);
            float padX = 0.005f, padY = 0.004f;
            _talkW = wCm * 0.01f + padX * 2f;
            _talkH = hCm * 0.01f + padY * 2f;
            if (_talkPlateMesh != null) Destroy(_talkPlateMesh);
            _talkPlateMesh = WallUi.RoundedRect(_talkW, _talkH, 0.004f);
            if (_talkPlateMf != null) _talkPlateMf.sharedMesh = _talkPlateMesh;
            // Origin = tail tip; the plate sits above the tail.
            if (_talkPlateR != null) _talkPlateR.transform.localPosition = new Vector3(0f, TalkTail + _talkH * 0.5f, 0f);
            _talkLabel.transform.localPosition = new Vector3(0f, TalkTail + _talkH * 0.5f, -0.0005f);
        }

        void PlaceTalkBubble(Transform avatar, float height)
        {
            Transform eyeT = ResolveHead();
            Vector3 eye = eyeT != null ? eyeT.position : avatar.position + Vector3.back;
            Vector3 feet = avatar.position;
            Transform headBone = FindTalkHead(avatar);
            Vector3 top;
            if (headBone != null)
            {
                // Head bone sits about 0.93 of the height; the crown ~0.07 above it. For the portal visitor the height comes from the bone.
                float h = height > 1e-4f ? height : Mathf.Max(0.05f, (headBone.position.y - feet.y) / 0.92f);
                top = new Vector3(headBone.position.x, headBone.position.y + h * 0.10f, headBone.position.z);
            }
            else
            {
                float h = height > 1e-4f ? height : 0.3f;
                top = feet + Vector3.up * h;
            }
            // rc21 (Play capture): the chibi heads (ears, horns, antennae) reach well above the head-bone guess, so the head
            // covered the bubble's last line. Use the visible mesh top when it is higher.
            float meshTop = float.NegativeInfinity;
            foreach (var r in avatar.GetComponentsInChildren<Renderer>())
            {
                if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
                if (_talkRoot != null && r.transform.IsChildOf(_talkRoot)) continue;
                // Only the agent's own body: its skinned mesh and what rides on the head bone (ears, horns, hats, antennae);
                // never the portal column, pad or effects that may hang under the same root.
                bool body = r is SkinnedMeshRenderer || (headBone != null && r.transform.IsChildOf(headBone));
                if (!body) continue;
                if (r.bounds.max.y > meshTop) meshTop = r.bounds.max.y;
            }
            if (!float.IsNegativeInfinity(meshTop) && meshTop > top.y && meshTop - feet.y < 3f) top.y = meshTop;
            _talkTop = top;
            float agentH = Mathf.Max(0.05f, top.y - feet.y);

            // Scale for the viewing distance: at least 1, up to 2.8, so the text is ~6.5 mm at 0.6 m and bigger beyond.
            float d = Vector3.Distance(eye, top);
            float k = Mathf.Clamp(d / 0.55f, 1f, 2.8f);
            float pop = _talkK * _talkK * (3f - 2f * _talkK);
            _talkScale = k;

            Quaternion rot = WallUi.FacingEye(top, eye);
            Vector3 right = rot * Vector3.right;
            // Tail tip a little above the crown (clear of the head), the bubble stands on it.
            Vector3 tip = top + Vector3.up * (agentH * 0.06f + 0.012f * k);
            float halfW = _talkW * 0.5f * k, boxH = (_talkH + TalkTail) * k;

            // Keep clear of the visit card: if the bubble's box overlaps the card's box, slide it sideways away from the card.
            if (_visitCard != null && _visitCard.Visible)
            {
                Transform c = _visitCard.root;
                float cs = c.lossyScale.x;
                Vector3 cw = c.position - tip;
                float cardHalfW = _visitCard.Size.x * 0.5f * cs + 0.01f, cardHalfH = _visitCard.Size.y * 0.5f * cs + 0.01f;
                float dx = Vector3.Dot(cw, right);          // card centre relative to the bubble, along the bubble's right
                float dy = c.position.y - (tip.y + boxH * 0.5f);
                if (Mathf.Abs(dy) < cardHalfH + boxH * 0.5f && Mathf.Abs(dx) < cardHalfW + halfW)
                {
                    float need = cardHalfW + halfW - Mathf.Abs(dx);
                    tip += right * (dx > 0f ? -need : need);
                }
            }

            Transform parent = _talkRoot.parent;
            float ps = parent != null ? Mathf.Max(1e-5f, parent.lossyScale.x) : 1f;
            _talkRoot.SetPositionAndRotation(tip, rot);
            _talkRoot.localScale = Vector3.one * (k * pop / ps);
        }

        Transform FindTalkHead(Transform avatar)
        {
            return avatar == null ? null : _talkBobHead(avatar);
        }

        Transform _talkHeadCache, _talkHeadCacheFor;
        Transform _talkBobHead(Transform avatar)
        {
            if (_talkHeadCacheFor == avatar && _talkHeadCache != null) return _talkHeadCache;
            _talkHeadCacheFor = avatar; _talkHeadCache = null;
            foreach (var t in avatar.GetComponentsInChildren<Transform>(true))
                if (t.name == "Head" || t.name == "head") { _talkHeadCache = t; break; }
            return _talkHeadCache;
        }

        // ───────────── driver / probe evidence ─────────────

        /// <summary>Probe: the bubble's state in one line.</summary>
        public string TalkInfo()
        {
            string agent = TalkActiveAgent(out Transform avatar, out float h);
            Transform hb = avatar != null ? FindTalkHead(avatar) : null;
            bool vis = _talkRoot != null && _talkRoot.gameObject.activeSelf;
            string s = "talk agent=" + (agent ?? "none") + " bubble=" + (vis ? "UP" : "down") + " k=" + _talkK.ToString("F2") + " text='" + _talkText + "'";
            if (vis)
            {
                Transform e = ResolveHead();
                s += " tip@" + _talkRoot.position.ToString("F3") + " scale=" + _talkScale.ToString("F2") + " w=" + (_talkW * _talkScale * 100f).ToString("F1") + "cm"
                     + " fontMm=" + (TalkFontMm * _talkScale).ToString("F1");
                if (hb != null) s += " headBone@" + hb.position.ToString("F3") + " tipAboveHead=" + (_talkRoot.position.y - hb.position.y).ToString("F3");
                if (e != null) s += " dist=" + Vector3.Distance(e.position, _talkRoot.position).ToString("F2")
                                    + " facingDot=" + Vector3.Dot(-_talkRoot.forward, (e.position - _talkRoot.position).normalized).ToString("F2");
            }
            s += " bob=" + (_talkBob != null ? "str=" + _talkBob.Strength.ToString("F2") + " deg=" + _talkBob.LastAngle.ToString("F2") + " head=" + _talkBob.HasHead : "none")
                 + " speaking=" + (agent != null && MuseDesk.Voice.AgentSpeech.IsSpeaking(agent)) + " ttsSeen=" + _talkTtsSeen;
            return s;
        }

        /// <summary>Probe: speak a fake line as the agent that is with the user, through the same AgentSpeech path.</summary>
        public string TalkTest(string text)
        {
            string agent = TalkActiveAgent(out Transform avatar, out float h);
            if (agent == null) return "no agent with the user (perch one or open the portal first)";
            bool ok = MuseDesk.Voice.AgentSpeech.Say(agent, text, SpeechSpot());
            return "said as " + agent + " voice=" + ok;
        }
    }
}
