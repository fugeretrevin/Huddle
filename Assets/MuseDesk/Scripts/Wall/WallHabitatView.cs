using System.Collections.Generic;
using UnityEngine;
using MuseDesk.Spatial.Wall;

namespace MuseDesk.Wall
{
    /// <summary>
    /// The visible Wall Habitat, built at run time from the SAME <see cref="HabitatLayout"/> the walkers and the
    /// validator use, so the drawn desks, partitions, shaft and railings are exactly the boxes the characters are
    /// proven to stay clear of. PROXY ART: labelled placeholder geometry in the design palette until the
    /// Antigravity modules arrive; <see cref="HabitatArtManifest"/> swaps a module for a real prefab by socket id.
    ///
    /// Avatars are VISUAL CLONES of the desk agents (mesh + Animator only; every script, collider and
    /// interactable stripped), so the habitat never runs a second agent. The Manager uses a tinted clone with a
    /// lavender cap until its own model ships. Each avatar's root is written only from its HabitatWalker.
    /// </summary>
    public sealed class WallHabitatView
    {
        public static readonly Color Paper = Hex(0xF7F2E3), Ink = Hex(0x1F2530), Teal = Hex(0x2FA39A), Ivory = Hex(0xEFE8DA),
            Wood = Hex(0xD9B98C), WoodDark = Hex(0xB89466), Lavender = Hex(0xA99BD1), Brass = Hex(0xC9A54A),
            Amber = Hex(0xE8A33C), Coral = Hex(0xE0654F), Green = Hex(0x5DBB7A), Grey = Hex(0x9AA0A8), Glass = Hex(0xBFD9D6);

        static Color Hex(int rgb) { return new Color(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f, 1f); }

        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        static readonly int ColorId = Shader.PropertyToID("_Color");
        static readonly int S_Idle = Animator.StringToHash("Idle_Breathing"), S_Walk = Animator.StringToHash("Walk_Loop"),
            S_Typing = Animator.StringToHash("Typing"), S_Reading = Animator.StringToHash("Reading"), S_Inspecting = Animator.StringToHash("Inspecting"),
            S_Wait = Animator.StringToHash("Waiting_For_Input"), S_Offer = Animator.StringToHash("Offer_Document"), S_Talking = Animator.StringToHash("Talking");

        public readonly Transform root;
        public readonly HabitatLayout layout;
        readonly Material _mat;
        readonly Mesh _cube;
        readonly MaterialPropertyBlock _mpb = new MaterialPropertyBlock();
        readonly Transform _cabin;
        readonly Transform[] _doorL, _doorR;
        readonly float _doorHalf;
        readonly Renderer _beacon;
        readonly Transform[] _avatar = new Transform[4];
        readonly Animator[] _anim = new Animator[4];
        readonly int[] _animState = new int[4];
        readonly Vector3[] _avatarScale = new Vector3[4];
        readonly Transform[] _prop = new Transform[4];
        readonly Renderer[] _propR = new Renderer[4];
        readonly Renderer[] _status = new Renderer[4];
        readonly Transform[] _selectRing = new Transform[4];
        readonly Renderer[] _selectRingR = new Renderer[4];
        readonly Transform[] _visitMarker = new Transform[4];
        readonly List<CardFlight> _cards = new List<CardFlight>();
        readonly Dictionary<int, int> _deskStack = new Dictionary<int, int>();
        float _beaconPhase;

        sealed class CardFlight { public Transform t; public Vector3 from, mid, to; public float time, duration; public int role; public bool landed; }

        /// <summary>Visual-only avatar for a role (null when no source mesh was available).</summary>
        public Transform Avatar(HabitatRole r) { return _avatar[(int)r]; }
        public float AvatarHeight => layout.agentHeight;

        public WallHabitatView(Transform parent, HabitatLayout layout, Material baseMaterial, GameObject[] agentSources, HabitatArtManifest manifest)
        {
            this.layout = layout;
            _mat = baseMaterial;
            _cube = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            var go = new GameObject("WallHabitat_" + layout.kind);
            root = go.transform;
            root.SetParent(parent, false);

            var structure = new GameObject("Structure (proxy art)").transform;
            structure.SetParent(root, false);
            Vector3 size = layout.size;
            float back = 0.015f;

            // Static boxes, coloured by kind. These ARE the collision boxes.
            foreach (HabitatBox b in layout.boxes)
            {
                Color c = KindColor(b.kind);
                if (b.kind == HabitatBoxKind.ShaftFront || b.kind == HabitatBoxKind.ShaftWall) c = Ivory;
                Box(structure, b.id, b.center, b.half * 2f, c);
            }
            // Decorative, non-blocking detail: back panels per floor, posts, desk tops, monitors, lamps.
            foreach (HabitatFloor f in layout.floors)
            {
                Box(structure, "BackPanel_" + f.id, new Vector3((f.xMin + size.x * 0.5f) * 0.5f, f.floorY + f.Clearance * 0.5f, back + 0.002f),
                    new Vector3(size.x * 0.5f - f.xMin, f.Clearance, 0.004f), f.index == layout.ManagerFloor ? Lavender * 0.9f + Ivory * 0.1f : Paper);
                Box(structure, "Post_" + f.id, new Vector3(f.xMin + 0.004f, f.floorY + f.Clearance * 0.5f, size.z - 0.006f), new Vector3(0.008f, f.Clearance, 0.008f), WoodDark);
            }
            foreach (HabitatBox b in layout.boxes)
            {
                if (b.kind != HabitatBoxKind.Desk) continue;
                // A low laptop on each desk (a tall monitor would hide the character from the user's side).
                Box(structure, b.id + "_Laptop", new Vector3(b.center.x - b.half.x * 0.3f, b.Max.y + 0.002f, b.center.z), new Vector3(b.half.x * 0.9f, 0.003f, b.half.z * 1.2f), Ink);
                Box(structure, b.id + "_Lamp", new Vector3(b.Max.x - 0.01f, b.Max.y + 0.012f, b.Min.z + 0.008f), new Vector3(0.008f, 0.024f, 0.008f), Brass);
            }

            // Elevator cabin (moves) and landing doors (slide).
            _cabin = new GameObject("ElevatorCabin").transform;
            _cabin.SetParent(root, false);
            Vector3 ch = layout.cabinHalf;
            float wall = 0.004f;
            Box(_cabin, "CabinFloor", new Vector3(0, -0.003f, 0), new Vector3(ch.x * 2f, 0.006f, ch.z * 2f), Brass);
            Box(_cabin, "CabinRoof", new Vector3(0, ch.y * 2f + 0.003f, 0), new Vector3(ch.x * 2f, 0.006f, ch.z * 2f), Brass);
            Box(_cabin, "CabinSideL", new Vector3(-ch.x - wall * 0.5f, ch.y, 0), new Vector3(wall, ch.y * 2f, ch.z * 2f), Glass);
            Box(_cabin, "CabinSideR", new Vector3(ch.x + wall * 0.5f, ch.y, 0), new Vector3(wall, ch.y * 2f, ch.z * 2f), Glass);
            _doorHalf = layout.doorWidth * 0.25f;
            _doorL = new Transform[layout.floors.Count];
            _doorR = new Transform[layout.floors.Count];
            float zDoor = layout.Socket("ElevatorStop_Lower").position.z - 0.04f - 0.002f;
            foreach (HabitatBox b in layout.boxes) if (b.id == "ShaftFront_L") zDoor = b.center.z;
            for (int f = 0; f < layout.floors.Count; f++)
            {
                float y = layout.floors[f].floorY + layout.cabinHalf.y;
                _doorL[f] = Box(root, "Door_" + layout.floors[f].id + "_L", new Vector3(layout.elevatorX - _doorHalf, y, zDoor), new Vector3(_doorHalf * 2f, layout.cabinHalf.y * 2f, 0.004f), Glass).transform;
                _doorR[f] = Box(root, "Door_" + layout.floors[f].id + "_R", new Vector3(layout.elevatorX + _doorHalf, y, zDoor), new Vector3(_doorHalf * 2f, layout.cabinHalf.y * 2f, 0.004f), Glass).transform;
                // Stop indicator light above each door.
                Box(root, "StopLight_" + layout.floors[f].id, new Vector3(layout.elevatorX, layout.floors[f].floorY + layout.cabinHalf.y * 2f + 0.012f, zDoor + 0.003f), new Vector3(0.012f, 0.006f, 0.003f), Brass);
            }

            // Beacon, printer, delivery chute.
            HabitatSocket bs = layout.Socket("AttentionBeaconSocket");
            _beacon = Box(root, "AttentionBeacon", bs.position + new Vector3(0, 0.022f, 0), new Vector3(0.03f, 0.03f, 0.03f), Teal);
            Box(root, "BeaconPost", bs.position + new Vector3(0, 0.004f, 0), new Vector3(0.008f, 0.008f, 0.008f), Brass);
            HabitatSocket ps = layout.Socket("TaskPrinterSocket");
            Box(root, "TaskPrinter", ps.position + new Vector3(0, 0.02f, 0), new Vector3(0.07f, 0.04f, 0.05f), Brass);
            Box(root, "TaskPrinterSlot", ps.position + new Vector3(0, 0.03f, 0.026f), new Vector3(0.05f, 0.004f, 0.004f), Ink);

            // Status lights over each workstation (authoritative state colour).
            for (int r = 0; r < 4; r++)
            {
                HabitatSocket w = layout.Socket((HabitatRole)r + ".WorkPose");
                _status[r] = Box(root, "StatusLight_" + (HabitatRole)r, w.position + new Vector3(0, layout.agentHeight + 0.03f, 0), new Vector3(0.014f, 0.014f, 0.014f), Grey);
                _selectRing[r] = Box(root, "SelectRing_" + (HabitatRole)r, w.position + new Vector3(0, 0.002f, 0), new Vector3(0.075f, 0.003f, 0.075f), Teal).transform;
                _selectRingR[r] = _selectRing[r].GetComponent<Renderer>();
                _selectRing[r].gameObject.SetActive(false);
                _visitMarker[r] = Box(root, "OnVisitMarker_" + (HabitatRole)r, w.position + new Vector3(0, layout.agentHeight + 0.055f, 0), new Vector3(0.03f, 0.008f, 0.008f), Lavender).transform;
                _visitMarker[r].gameObject.SetActive(false);
            }

            // Avatars.
            for (int r = 0; r < 4; r++)
            {
                GameObject src = agentSources != null && agentSources.Length > 0 ? agentSources[r == 3 ? 1 : Mathf.Min(r, agentSources.Length - 1)] : null;
                Transform a = src != null ? CloneVisual(src, "Avatar_" + (HabitatRole)r, root, layout.agentHeight) : null;
                if (a == null) a = FallbackAvatar(root, (HabitatRole)r);
                if (r == (int)HabitatRole.Manager) DressManager(a);
                _avatar[r] = a;
                _avatarScale[r] = a.localScale;
                _anim[r] = a.GetComponentInChildren<Animator>();
                if (_anim[r] != null) { _anim[r].applyRootMotion = false; _anim[r].cullingMode = AnimatorCullingMode.CullUpdateTransforms; }
                _animState[r] = 0;
                _prop[r] = Box(a, "CarriedProp", Vector3.zero, Vector3.one, Paper).transform;
                _propR[r] = _prop[r].GetComponent<Renderer>();
                _prop[r].gameObject.SetActive(false);
            }

            if (manifest != null) manifest.ApplyTo(this);
        }

        public Renderer Box(Transform parent, string name, Vector3 localCenter, Vector3 size, Color c)
        {
            var g = new GameObject(name);
            g.transform.SetParent(parent, false);
            g.transform.localPosition = localCenter;
            g.transform.localScale = size;
            g.AddComponent<MeshFilter>().sharedMesh = _cube;
            var mr = g.AddComponent<MeshRenderer>();
            mr.sharedMaterial = _mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            Tint(mr, c);
            return mr;
        }

        public void Tint(Renderer r, Color c)
        {
            if (r == null) return;
            r.GetPropertyBlock(_mpb);
            _mpb.SetColor(BaseColor, c);
            _mpb.SetColor(ColorId, c);
            r.SetPropertyBlock(_mpb);
        }

        static Color KindColor(HabitatBoxKind k)
        {
            switch (k)
            {
                case HabitatBoxKind.Backplate: return Ivory;
                case HabitatBoxKind.Deck: return Wood;
                case HabitatBoxKind.Roof: return WoodDark;
                case HabitatBoxKind.Desk: return WoodDark;
                case HabitatBoxKind.Partition: return Paper;
                case HabitatBoxKind.Railing: return WoodDark;
                case HabitatBoxKind.Furniture: return Wood;
                case HabitatBoxKind.EndWall: return WoodDark;
                default: return Ivory;
            }
        }

        // ── avatars: visual clones only ──

        /// <summary>
        /// A copy of an agent's mesh and Animator with every script, collider, rigidbody, audio source and text
        /// removed BEFORE it is ever active (so no Awake/OnEnable of any agent logic runs), scaled to
        /// <paramref name="height"/>. Never a second worker. Also used for the visit projection and the portal.
        /// </summary>
        public static Transform CloneVisual(GameObject source, string name, Transform parent, float height)
        {
            if (source == null) return null;
            float srcH = 0f;
            var smrs = source.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            if (smrs.Length > 0)
            {
                Bounds b = smrs[0].bounds;
                for (int i = 1; i < smrs.Length; i++) b.Encapsulate(smrs[i].bounds);
                srcH = b.size.y;
            }
            var holder = new GameObject(name + "_holder");
            holder.SetActive(false);
            holder.transform.SetParent(parent, false);
            GameObject clone = Object.Instantiate(source, holder.transform);
            clone.name = name;
            StripToVisual(clone);
            clone.transform.SetParent(parent, false);
            Object.Destroy(holder);
            clone.transform.localPosition = Vector3.zero;
            clone.transform.localRotation = Quaternion.identity;
            float parentScale = parent != null ? parent.lossyScale.y : 1f;
            float factor = srcH > 1e-4f ? (height * parentScale) / srcH : 1f;
            Vector3 srcWorld = source.transform.lossyScale;
            clone.transform.localScale = srcWorld * factor / Mathf.Max(1e-5f, parentScale);
            clone.SetActive(true);
            return clone.transform;
        }

        public static void StripToVisual(GameObject clone)
        {
            // Text objects go entirely (their meshes would show stale words).
            var texts = clone.GetComponentsInChildren<TMPro.TMP_Text>(true);
            for (int i = 0; i < texts.Length; i++) if (texts[i] != null && texts[i].gameObject != clone) Object.DestroyImmediate(texts[i].gameObject);
            for (int pass = 0; pass < 4; pass++)
            {
                var behaviours = clone.GetComponentsInChildren<MonoBehaviour>(true);
                if (behaviours.Length == 0) break;
                for (int i = behaviours.Length - 1; i >= 0; i--)
                    if (behaviours[i] != null) { try { Object.DestroyImmediate(behaviours[i]); } catch (System.Exception) { } }
            }
            foreach (var c in clone.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
            foreach (var rb in clone.GetComponentsInChildren<Rigidbody>(true)) Object.DestroyImmediate(rb);
            foreach (var a in clone.GetComponentsInChildren<AudioSource>(true)) Object.DestroyImmediate(a);
            foreach (var c in clone.GetComponentsInChildren<Canvas>(true)) Object.DestroyImmediate(c.gameObject);
        }

        Transform FallbackAvatar(Transform parent, HabitatRole r)
        {
            // A capsule-ish block avatar (no mesh source available): body, head, eyes. Clearly placeholder.
            var a = new GameObject("Avatar_" + r + " (placeholder)").transform;
            a.SetParent(parent, false);
            float h = layout.agentHeight;
            Color body = r == HabitatRole.Researcher ? Teal : r == HabitatRole.Developer ? Hex(0x5B8DEF) : r == HabitatRole.Tester ? Amber : Lavender;
            Box(a, "Body", new Vector3(0, h * 0.35f, 0), new Vector3(h * 0.38f, h * 0.7f, h * 0.3f), body);
            Box(a, "Head", new Vector3(0, h * 0.82f, 0), new Vector3(h * 0.34f, h * 0.3f, h * 0.3f), Paper);
            Box(a, "Eyes", new Vector3(0, h * 0.84f, h * 0.152f), new Vector3(h * 0.2f, h * 0.05f, 0.002f), Ink);
            return a;
        }

        void DressManager(Transform a)
        {
            float h = layout.agentHeight;
            // Placeholder Manager (until Antigravity's model): the Developer mesh with a lavender cap and a brass badge.
            // (A colour tint on the character's toon material rendered translucent in Play Mode: not used.)
            var cap = Box(a, "ManagerCap", Vector3.zero, Vector3.one, Lavender).transform;
            cap.SetParent(a, false);
            float s = a.lossyScale.y > 1e-5f ? a.lossyScale.y : 1f;
            float ps = root.lossyScale.y;
            cap.localPosition = new Vector3(0, h * 1.02f, 0) * ps / s;
            cap.localScale = new Vector3(h * 0.42f, h * 0.06f, h * 0.42f) * ps / s;
            var badge = Box(a, "ManagerBadge", Vector3.zero, Vector3.one, Brass).transform;
            badge.localPosition = new Vector3(0, h * 0.55f, h * 0.16f) * ps / s;
            badge.localScale = new Vector3(h * 0.08f, h * 0.08f, 0.004f) * ps / s;
        }

        // ── per frame ──

        /// <summary>Write everything that moves from the stage (pure logic) and the presentation state.</summary>
        public void Apply(HabitatStage stage, float dt, bool selectArmed, int hoveredRole, bool[] away, Color[] statusColors, Color beaconColor, bool beaconPulse)
        {
            HabitatElevatorMachine e = stage.elevator;
            Vector3 cp = new Vector3(layout.elevatorX, e.CabinY, layout.elevatorZ);
            if (_cabin.localPosition != cp) _cabin.localPosition = cp;
            for (int f = 0; f < layout.floors.Count; f++)
            {
                bool here = e.CabinFloor == f && Mathf.Abs(e.CabinY - layout.floors[f].floorY) < 1e-3f;
                float open = here ? e.DoorOpen01 : 0f;
                float y = layout.floors[f].floorY + layout.cabinHalf.y;
                float zd = _doorL[f].localPosition.z;
                _doorL[f].localPosition = new Vector3(layout.elevatorX - _doorHalf - open * _doorHalf * 1.9f, y, zd);
                _doorR[f].localPosition = new Vector3(layout.elevatorX + _doorHalf + open * _doorHalf * 1.9f, y, zd);
            }

            for (int r = 0; r < 4; r++)
            {
                HabitatWalker w = stage.Walker((HabitatRole)r);
                Transform a = _avatar[r];
                if (a == null) continue;
                a.localPosition = w.Position;
                a.localRotation = Quaternion.Euler(0f, w.Yaw, 0f);
                float vis = w.Visibility;
                Vector3 s = _avatarScale[r] * Mathf.Max(0.001f, vis);
                if (a.localScale != s) a.localScale = s;
                SetAnim(r, w.Anim, (HabitatRole)r);

                HabitatAct act = stage.Running((HabitatRole)r);
                bool carry = act != null && !w.AtHome && w.State != WalkerState.SoftTransfer;
                if (_prop[r].gameObject.activeSelf != carry) _prop[r].gameObject.SetActive(carry);
                if (carry)
                {
                    float h = layout.agentHeight;
                    float inv = 1f / Mathf.Max(1e-5f, a.lossyScale.y / Mathf.Max(1e-5f, root.lossyScale.y));
                    _prop[r].localPosition = new Vector3(0, h * 0.5f, h * 0.22f) * inv;
                    _prop[r].localScale = new Vector3(h * 0.32f, h * 0.24f, 0.004f) * inv;
                    Tint(_propR[r], act.prop == "warning" ? Amber : act.prop == "blueprint" ? Hex(0x6FB3E0) : Paper);
                }

                if (statusColors != null && r < statusColors.Length) Tint(_status[r], statusColors[r]);
                bool ring = selectArmed && r != (int)HabitatRole.Manager || (selectArmed && r == (int)HabitatRole.Manager);
                if (_selectRing[r].gameObject.activeSelf != ring) _selectRing[r].gameObject.SetActive(ring);
                if (ring)
                {
                    _selectRing[r].localPosition = w.Position + new Vector3(0, 0.002f, 0);
                    float pulse = 0.75f + 0.25f * Mathf.Sin(Time.unscaledTime * 4f + r);
                    Tint(_selectRingR[r], hoveredRole == r ? Color.Lerp(Teal, Color.white, 0.45f) : Teal * pulse);
                }
                bool onVisit = away != null && r < away.Length && away[r];
                if (_visitMarker[r].gameObject.activeSelf != onVisit) _visitMarker[r].gameObject.SetActive(onVisit);
            }

            _beaconPhase += dt;
            float k = beaconPulse ? 0.7f + 0.3f * Mathf.Sin(_beaconPhase * 3.2f) : 1f;
            Tint(_beacon, beaconColor * k);

            for (int i = _cards.Count - 1; i >= 0; i--) StepCard(_cards[i], dt);
        }

        void SetAnim(int r, WalkerAnim anim, HabitatRole role)
        {
            Animator an = _anim[r];
            if (an == null || !an.isActiveAndEnabled || an.runtimeAnimatorController == null) return;
            int want;
            switch (anim)
            {
                case WalkerAnim.Walk: want = S_Walk; break;
                case WalkerAnim.Wait: case WalkerAnim.Ride: case WalkerAnim.Idle: want = S_Idle; break;
                case WalkerAnim.Present: want = S_Offer; break;
                default: want = role == HabitatRole.Researcher ? S_Reading : role == HabitatRole.Tester ? S_Inspecting : role == HabitatRole.Manager ? S_Talking : S_Typing; break;
            }
            if (_animState[r] == want) return;
            if (!an.HasState(0, want)) want = an.HasState(0, S_Idle) ? S_Idle : 0;
            _animState[r] = want;
            if (want != 0) an.CrossFadeInFixedTime(want, 0.2f, 0);
        }

        // ── printed task cards (only ever for canonical, committed task ids) ──

        public void PrintCard(HabitatRole owner)
        {
            HabitatSocket ps = layout.Socket("TaskPrinterSocket");
            HabitatSocket desk = layout.Socket(owner + ".WorkPose");
            var t = Box(root, "PrintedCard_" + owner, ps.position + new Vector3(0, 0.03f, 0.03f), new Vector3(0.035f, 0.002f, 0.025f), Paper).transform;
            int count;
            _deskStack.TryGetValue((int)owner, out count);
            _deskStack[(int)owner] = count + 1;
            Vector3 to = desk.position + new Vector3(0.03f, 0.068f + 0.0025f * Mathf.Min(count, 6), 0.075f);
            Vector3 from = t.localPosition;
            Vector3 mid = new Vector3((from.x + to.x) * 0.5f, Mathf.Max(from.y, to.y) + 0.08f, layout.size.z + 0.03f);   // out in front of the decks, never through them
            _cards.Add(new CardFlight { t = t, from = from, mid = mid, to = to, duration = 2.2f, role = (int)owner });
        }

        void StepCard(CardFlight c, float dt)
        {
            if (c.landed) return;
            c.time += dt;
            float u = Mathf.Clamp01(c.time / c.duration);
            float e = u * u * (3f - 2f * u);
            Vector3 a = Vector3.Lerp(c.from, c.mid, e), b = Vector3.Lerp(c.mid, c.to, e);
            c.t.localPosition = Vector3.Lerp(a, b, e);
            if (u >= 1f) c.landed = true;
        }

        public int CardsOnDesk(HabitatRole r) { int c; return _deskStack.TryGetValue((int)r, out c) ? c : 0; }

        public void Destroy()
        {
            if (root != null) Object.Destroy(root.gameObject);
        }
    }
}
