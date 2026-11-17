using TMPro;
using UnityEngine;
using UnityEngine.Rendering;

namespace MuseDesk.Agents
{
    /// <summary>
    /// Creates (or finds) the objects the interaction pass needs: one speech bubble and one status
    /// chip per character under WorkspaceRoot/AgentTalk, and the poke trigger volume on each
    /// character. One code path for the Editor builder (AgentScripts/BuildAgentInteractionV1, which
    /// then saves the scene) and for the runtime fallback in AgentAttention (a scene the builder
    /// has not been run on still works). Every method is idempotent: it finds by name first.
    ///
    /// Nothing here is parented under a rig bone (the armature is scaled by 100). The signs live
    /// under WorkspaceRoot and are placed above the head by world position each frame.
    /// </summary>
    public static class AgentTalkRig
    {
        public const string RootName = "AgentTalk";
        public const string PokeVolumeName = "PokeVolume";
        const int IgnoreRaycastLayer = 2;

        public static Transform EnsureRoot(Transform workspaceRoot)
        {
            Transform t = workspaceRoot != null ? workspaceRoot.Find(RootName) : null;
            if (t == null)
            {
                var go = new GameObject(RootName);
                t = go.transform;
                t.SetParent(workspaceRoot, false);
            }
            t.localPosition = Vector3.zero;
            t.localRotation = Quaternion.identity;
            t.localScale = Vector3.one;
            return t;
        }

        static Transform Child(Transform parent, string name, out bool created)
        {
            Transform t = parent.Find(name);
            created = t == null;
            if (created)
            {
                var go = new GameObject(name);
                t = go.transform;
                t.SetParent(parent, false);
            }
            return t;
        }

        static MeshRenderer PlateRenderer(Transform t, Material mat)
        {
            if (t.GetComponent<MeshFilter>() == null) t.gameObject.AddComponent<MeshFilter>();
            MeshRenderer mr = t.GetComponent<MeshRenderer>();
            if (mr == null) mr = t.gameObject.AddComponent<MeshRenderer>();
            if (mat != null) mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.lightProbeUsage = LightProbeUsage.Off;
            mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return mr;
        }

        static TextMeshPro Label(Transform parent, string name, TMP_FontAsset font, Color color)
        {
            Transform t = Child(parent, name, out _);
            t.localRotation = Quaternion.identity;
            t.localScale = Vector3.one * 0.01f;          // 1 font unit = 1 mm, 1 rect unit = 1 cm
            TextMeshPro tmp = t.GetComponent<TextMeshPro>();
            if (tmp == null)
            {
                // Adding TMP swaps the Transform for a RectTransform, destroying the old reference.
                tmp = t.gameObject.AddComponent<TextMeshPro>();
                t = tmp.transform;
                t.localRotation = Quaternion.identity;
                t.localScale = Vector3.one * 0.01f;
            }
            if (font != null) tmp.font = font;
            tmp.color = color;
            tmp.raycastTarget = false;
            tmp.overflowMode = TextOverflowModes.Ellipsis;
            MeshRenderer mr = t.GetComponent<MeshRenderer>();
            if (mr != null) { mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = false; }
            return tmp;
        }

        /// <summary>The speech bubble for <paramref name="role"/> ("Researcher" ...). Saved hidden.</summary>
        public static AgentSpeechBubble EnsureBubble(Transform talkRoot, string role, Material paper, TMP_FontAsset font)
        {
            Transform t = Child(talkRoot, "Bubble_" + role, out _);
            AgentSpeechBubble bubble = t.GetComponent<AgentSpeechBubble>();
            if (bubble == null) bubble = t.gameObject.AddComponent<AgentSpeechBubble>();

            Transform plate = Child(t, "BubblePlate", out _);
            plate.localPosition = Vector3.zero;
            plate.localRotation = Quaternion.identity;
            plate.localScale = Vector3.one;
            bubble.plateRenderer = PlateRenderer(plate, paper);
            bubble.plate = plate.GetComponent<MeshFilter>();

            TextMeshPro tmp = Label(t, "BubbleText", font, MuseDesk.Workspace.OfficeTokens.Ink);
            tmp.fontSize = bubble.fontMm;
            tmp.enableAutoSizing = false;
            tmp.fontStyle = FontStyles.Normal;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.textWrappingMode = TextWrappingModes.Normal;
            tmp.rectTransform.sizeDelta = new Vector2(bubble.maxTextWidthCm, 2.6f);
            tmp.transform.localPosition = new Vector3(0f, bubble.tailHeight + 0.016f, -bubble.textProud);
            if (string.IsNullOrEmpty(tmp.text)) tmp.text = " ";
            bubble.label = tmp;

            bubble.plateRenderer.enabled = false;
            tmp.enabled = false;
            return bubble;
        }

        /// <summary>The gaze chip for <paramref name="role"/>. Saved hidden.</summary>
        public static AgentStatusChip EnsureChip(Transform talkRoot, string role, Material plateMat, TMP_FontAsset font)
        {
            Transform t = Child(talkRoot, "Chip_" + role, out _);
            AgentStatusChip chip = t.GetComponent<AgentStatusChip>();
            if (chip == null) chip = t.gameObject.AddComponent<AgentStatusChip>();

            Transform plate = Child(t, "ChipPlate", out _);
            plate.localPosition = Vector3.zero;
            plate.localRotation = Quaternion.identity;
            plate.localScale = Vector3.one;
            chip.plateRenderer = PlateRenderer(plate, plateMat);
            chip.plate = plate.GetComponent<MeshFilter>();

            Transform strip = Child(t, "ChipStrip", out _);
            strip.localPosition = new Vector3(-chip.width * 0.5f + 0.0058f, 0.0045f, -0.0002f);
            strip.localRotation = Quaternion.identity;
            strip.localScale = Vector3.one;
            chip.strip = PlateRenderer(strip, plateMat);

            float left = -chip.width * 0.5f + 0.0098f, right = chip.width * 0.5f - 0.004f;
            TextMeshPro tmp = Label(t, "ChipText", font, MuseDesk.Workspace.OfficeTokens.OnPlate);
            tmp.fontSize = 7.5f;
            tmp.enableAutoSizing = true;
            tmp.fontSizeMax = 7.5f;
            tmp.fontSizeMin = 6.3f;
            tmp.fontStyle = FontStyles.Normal;
            tmp.alignment = TextAlignmentOptions.Left;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.rectTransform.sizeDelta = new Vector2((right - left) * 100f, (chip.height - 0.004f) * 100f);
            tmp.transform.localPosition = new Vector3((left + right) * 0.5f, chip.height * 0.5f, -0.0005f);
            if (string.IsNullOrEmpty(tmp.text)) tmp.text = role;
            chip.label = tmp;

            chip.plateRenderer.enabled = false;
            chip.strip.enabled = false;
            tmp.enabled = false;
            return chip;
        }

        /// <summary>
        /// The poke volume: a trigger capsule around the character's head and body, a child of the
        /// agent root (not of a bone), on the Ignore Raycast layer so no ray or grab query sees it.
        /// Sized from the rig as it stands. It is only ever asked "is this fingertip inside".
        /// </summary>
        public static CapsuleCollider EnsurePokeVolume(AgentPresence presence)
        {
            if (presence == null) return null;
            Transform root = presence.transform;
            Transform t = Child(root, PokeVolumeName, out _);
            t.gameObject.layer = IgnoreRaycastLayer;
            t.localPosition = Vector3.zero;
            t.localRotation = Quaternion.identity;
            t.localScale = Vector3.one;
            CapsuleCollider c = t.GetComponent<CapsuleCollider>();
            if (c == null) c = t.gameObject.AddComponent<CapsuleCollider>();
            c.isTrigger = true;
            c.direction = 1;

            // Height of the top of the head in the agent root's own units.
            float topLocal = root.InverseTransformPoint(presence.TopPoint).y;
            if (topLocal < 0.01f) topLocal = 0.22f;
            float h = topLocal * 1.06f;
            c.height = h;
            c.radius = Mathf.Min(h * 0.27f, h * 0.5f);
            c.center = new Vector3(0f, h * 0.5f, 0f);
            presence.pokeVolume = c;
            return c;
        }
    }
}
