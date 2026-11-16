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
        // State and UI colours ARE the app's design tokens (OfficeTokens), so teal / amber / green / coral / lavender
        // mean the same thing on the wall as on the desk board (audit C #1, #7). Only the treehouse materials
        // (ivory, woods, brass, glass) are Wall-specific.
        public static readonly Color Paper = Workspace.OfficeTokens.Paper, Ink = Workspace.OfficeTokens.Ink, Teal = Workspace.OfficeTokens.Teal,
            Lavender = Workspace.OfficeTokens.Lavender, Amber = Workspace.OfficeTokens.Amber, Coral = Workspace.OfficeTokens.Coral,
            Green = Workspace.OfficeTokens.Success,
            Ivory = Hex(0xEFE8DA), Wood = Hex(0xD9B98C), WoodDark = Hex(0xB89466), Brass = Hex(0xC9A54A),
            Grey = Hex(0x9AA0A8), Glass = Hex(0xBFD9D6);

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

        sealed class CardFlight { public Transform t; public Vector3 from, mid, to; public float time, duration; public int role; public bool landed; public bool hideOnLand; public string taskId; public Renderer r; public bool onDesk; }
        readonly Dictionary<string, CardFlight> _cardById = new Dictionary<string, CardFlight>();
        readonly Dictionary<int, int> _trayStack = new Dictionary<int, int>();
        readonly bool[] _propHidden = new bool[4];
        readonly bool[] _listening = new bool[4];
        readonly bool[] _riding = new bool[4];
        bool _managerHint;
        List<GameObject> _artRoots;
        MuseDesk.Art.WallHabitatElevatorVisualController _artLift;
        Transform _artPivot;
        float _artPivotRestY, _artOpen = -1f;
        int _artFloor = -1;

        /// <summary>True when real habitat art replaced the proxies (manifest useHabitatArt on and an entry applied).</summary>
        public bool UsesArt => _artRoots != null && _artRoots.Count > 0;
        public MuseDesk.Art.WallHabitatElevatorVisualController ArtElevator => _artLift;
        public IReadOnlyList<GameObject> ArtRoots => _artRoots;

        // art elevator: presentation only, posed from the REAL cabin and door state (animators are frozen, never self-played)

        void BindArtElevator()
        {
            if (_artRoots == null) return;
            foreach (var g in _artRoots)
            {
                if (g == null) continue;
                _artLift = g.GetComponentInChildren<MuseDesk.Art.WallHabitatElevatorVisualController>(true);
                if (_artLift != null) break;
            }
            if (_artLift == null) return;
            _artLift.ResolveReferences();
            _artPivot = _artLift.ElevatorCarPivot;
            if (_artPivot != null) _artPivotRestY = _artPivot.localPosition.y;
            if (_artLift.SpineAnimator != null) _artLift.SpineAnimator.speed = 0f;
            if (_artLift.CabAnimator != null) _artLift.CabAnimator.speed = 0f;
        }

        static readonly int H_DoorsIdle = Animator.StringToHash("ElevatorDoors_Idle_Closed"), H_DoorsLower = Animator.StringToHash("ElevatorDoors_Open_Lower"),
            H_DoorsMid = Animator.StringToHash("ElevatorDoors_Open_Mid"), H_DoorsMgr = Animator.StringToHash("ElevatorDoors_Open_Manager"),
            H_GateOpen = Animator.StringToHash("Cabin_Gate_Open");

        void DriveArtElevator(IHabitatElevator e, int hereFloor, float open)
        {
            if (_artLift == null) return;
            if (_artPivot != null)
            {
                Vector3 lp = _artPivot.localPosition;
                lp.y = _artPivotRestY + (e.CabinY - layout.floors[0].floorY);
                if (lp != _artPivot.localPosition) _artPivot.localPosition = lp;
            }
            if (hereFloor != _artFloor) { _artFloor = hereFloor; if (hereFloor >= 0) _artLift.HighlightFloorIndicator(ArtStopIndex(hereFloor)); }
            if (Mathf.Abs(open - _artOpen) < 1e-3f) return;
            _artOpen = open;
            Animator sp = _artLift.SpineAnimator, cab = _artLift.CabAnimator;
            if (sp != null)
            {
                int n = layout.floors.Count, hash = H_DoorsIdle;
                if (hereFloor >= 0 && open > 0f) hash = hereFloor == 0 ? H_DoorsLower : (hereFloor == n - 1 ? H_DoorsMgr : H_DoorsMid);
                if (sp.HasState(0, hash)) sp.Play(hash, 0, hash == H_DoorsIdle ? 0f : open);
            }
            if (cab != null && cab.HasState(0, H_GateOpen)) cab.Play(H_GateOpen, 0, open);
        }

        int ArtStopIndex(int floor) { int n = layout.floors.Count; return floor == 0 ? 0 : (floor == n - 1 ? 2 : 1); }

        /// <summary>The carried prop of a running act can be hidden (e.g. on the way UP to collect a card the agent does not hold yet).</summary>
        public void SetPropVisible(HabitatRole r, bool visible) { _propHidden[(int)r] = !visible; }
        /// <summary>Truthful per-agent Listening: only while the voice layer says this agent is the one being addressed.</summary>
        public void SetListening(HabitatRole r, bool on) { if (_listening[(int)r] != on) { _listening[(int)r] = on; _animState[(int)r] = 0; } }
        public bool IsListening(HabitatRole r) { return _listening[(int)r]; }
        /// <summary>A ring under the Manager while a free hand points at it (pinch opens the Manager paper).</summary>
        public void SetManagerHint(bool on) { _managerHint = on; }
        /// <summary>True while the avatar is a child of the cabin (it rides WITH the cabin, no pose of its own).</summary>
        public bool IsAttachedToCabin(HabitatRole r) { return _avatar[(int)r] != null && _avatar[(int)r].parent == _cabin; }
        public Transform Cabin => _cabin;

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
                    new Vector3(size.x * 0.5f - f.xMin, f.Clearance, 0.004f), f.index == layout.ManagerFloor ? Color.Lerp(Lavender, Ivory, 0.45f) : Paper);
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

            if (manifest != null) { _artRoots = manifest.ApplyTo(this); BindArtElevator(); }
            // After the manifest: a proxy it hid stays out of the merged mesh.
            if (BatchStaticStructure) BatchStructure(structure);
            _recordColors = false;
            _boxColor.Clear();
        }

        // ── static batching of the structure ──

        /// <summary>Merge the static proxy boxes into one mesh per colour. Rollback switch: set false before building.</summary>
        public static bool BatchStaticStructure = true;

        readonly Dictionary<Renderer, Color> _boxColor = new Dictionary<Renderer, Color>();
        readonly List<Object> _owned = new List<Object>();
        public int BatchedRenderers { get; private set; }
        public int BatchedSourceBoxes { get; private set; }

        /// <summary>
        /// Combine every ACTIVE box directly under <paramref name="structure"/> into one mesh per colour, each with its
        /// own material instance (no property block), so the static treehouse costs a handful of SRP-batched draws
        /// instead of one per box. The source GameObjects stay (empty, same names and transforms) so lookups by name
        /// still work; only their renderer and filter are removed. Moving parts (cabin, doors, lights, avatars) are not
        /// under the structure and are untouched. Collision truth is the layout data, so this changes nothing there.
        /// </summary>
        void BatchStructure(Transform structure)
        {
            var groups = new Dictionary<int, List<CombineInstance>>();
            var colors = new Dictionary<int, Color>();
            var merged = new List<Renderer>();
            Matrix4x4 toLocal = structure.worldToLocalMatrix;
            for (int i = 0; i < structure.childCount; i++)
            {
                Transform t = structure.GetChild(i);
                if (!t.gameObject.activeSelf) continue;
                var mr = t.GetComponent<MeshRenderer>();
                Color c;
                if (mr == null || !_boxColor.TryGetValue(mr, out c)) continue;
                Color32 c32 = c;
                int key = (c32.r << 16) | (c32.g << 8) | c32.b;
                List<CombineInstance> list;
                if (!groups.TryGetValue(key, out list)) { list = new List<CombineInstance>(); groups[key] = list; colors[key] = c; }
                list.Add(new CombineInstance { mesh = _cube, transform = toLocal * t.localToWorldMatrix });
                merged.Add(mr);
            }
            if (merged.Count < 2) return;

            // CombineMeshes needs CPU-readable sources; the built-in cube is not guaranteed readable in a player.
            Mesh unit = ReadableUnitCube();
            foreach (var list in groups.Values)
                for (int i = 0; i < list.Count; i++) { var ci = list[i]; ci.mesh = unit; list[i] = ci; }
            _owned.Add(unit);

            foreach (var kv in groups)
            {
                var mesh = new Mesh { name = "WallHabitat_Structure_" + kv.Key.ToString("X6") };
                if (kv.Value.Count * 24 > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                mesh.CombineMeshes(kv.Value.ToArray(), true, true);
                mesh.RecalculateBounds();
                mesh.UploadMeshData(true);
                var mat = new Material(_mat) { name = "WallHabitat_Proxy_" + kv.Key.ToString("X6") };
                if (mat.HasProperty(BaseColor)) mat.SetColor(BaseColor, colors[kv.Key]);
                if (mat.HasProperty(ColorId)) mat.SetColor(ColorId, colors[kv.Key]);
                var g = new GameObject("Batched_" + kv.Key.ToString("X6") + " (" + kv.Value.Count + " boxes)");
                g.transform.SetParent(structure, false);
                g.AddComponent<MeshFilter>().sharedMesh = mesh;
                var r = g.AddComponent<MeshRenderer>();
                r.sharedMaterial = mat;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
                _owned.Add(mesh);
                _owned.Add(mat);
            }
            for (int i = 0; i < merged.Count; i++)
            {
                _boxColor.Remove(merged[i]);
                var mf = merged[i].GetComponent<MeshFilter>();
                Object.Destroy(merged[i]);
                if (mf != null) Object.Destroy(mf);
            }
            BatchedSourceBoxes = merged.Count;
            BatchedRenderers = groups.Count;
        }

        /// <summary>A 1 m cube centred on the origin (24 vertices, flat normals), same shape as the built-in cube.</summary>
        static Mesh ReadableUnitCube()
        {
            var v = new List<Vector3>(24); var n = new List<Vector3>(24); var uv = new List<Vector2>(24); var tri = new List<int>(36);
            Vector3[] dirs = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
            foreach (Vector3 d in dirs)
            {
                Vector3 a = Mathf.Abs(d.y) > 0.5f ? Vector3.forward : Vector3.up;
                Vector3 u = Vector3.Cross(a, d) * 0.5f, w = a * 0.5f, c = d * 0.5f;
                int s = v.Count;
                v.Add(c - u - w); v.Add(c + u - w); v.Add(c + u + w); v.Add(c - u + w);
                for (int k = 0; k < 4; k++) n.Add(d);
                uv.Add(new Vector2(0, 0)); uv.Add(new Vector2(1, 0)); uv.Add(new Vector2(1, 1)); uv.Add(new Vector2(0, 1));
                // (u, w, d) is right-handed for every face, so this order is clockwise seen from outside (Unity front).
                tri.Add(s); tri.Add(s + 1); tri.Add(s + 2); tri.Add(s); tri.Add(s + 2); tri.Add(s + 3);
            }
            var m = new Mesh { name = "WallHabitat_UnitCube" };
            m.SetVertices(v); m.SetNormals(n); m.SetUVs(0, uv); m.SetTriangles(tri, 0);
            m.RecalculateBounds();
            return m;
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
            if (_recordColors) _boxColor[mr] = c;
            return mr;
        }

        bool _recordColors = true;   // only the constructor's structure boxes are batch candidates

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
            IHabitatElevator e = stage.lift;
            Vector3 cp = new Vector3(layout.elevatorX, e.CabinY, layout.elevatorZ);
            if (_cabin.localPosition != cp) _cabin.localPosition = cp;
            int artHere = -1; float artOpen = 0f;
            for (int f = 0; f < layout.floors.Count; f++)
            {
                bool here = e.CabinFloor == f && Mathf.Abs(e.CabinY - layout.floors[f].floorY) < 1e-3f;
                float open = here ? e.DoorOpenEased : 0f;
                if (here) { artHere = f; artOpen = open; }
                float y = layout.floors[f].floorY + layout.cabinHalf.y;
                float zd = _doorL[f].localPosition.z;
                _doorL[f].localPosition = new Vector3(layout.elevatorX - _doorHalf - open * _doorHalf * 1.9f, y, zd);
                _doorR[f].localPosition = new Vector3(layout.elevatorX + _doorHalf + open * _doorHalf * 1.9f, y, zd);
            }
            if (_artLift != null) DriveArtElevator(e, artHere, artOpen);

            for (int r = 0; r < 4; r++)
            {
                HabitatWalker w = stage.Walker((HabitatRole)r);
                Transform a = _avatar[r];
                if (a == null) continue;
                // A rider is attached to the cabin: it becomes the cabin's child and keeps a fixed local pose, so it cannot
                // drift against the cabin. The cabin has unit scale and no rotation under the habitat root, so re-parenting
                // with local values kept changes neither the avatar's size nor where it stands.
                bool riding = w.State == WalkerState.Riding;
                if (riding != _riding[r])
                {
                    _riding[r] = riding;
                    a.SetParent(riding ? _cabin : root, false);
                }
                a.localPosition = riding ? new Vector3(w.Position.x - cp.x, 0f, w.Position.z - cp.z) : w.Position;
                a.localRotation = Quaternion.Euler(0f, w.Yaw, 0f);
                float vis = w.Visibility;
                Vector3 s = _avatarScale[r] * Mathf.Max(0.001f, vis);
                if (a.localScale != s) a.localScale = s;
                SetAnim(r, w.Anim, (HabitatRole)r, w);
                MatchStride(r, w);

                HabitatAct act = stage.Running((HabitatRole)r);
                bool carry = act != null && !w.AtHome && w.State != WalkerState.SoftTransfer && !_propHidden[r];
                if (_prop[r].gameObject.activeSelf != carry) _prop[r].gameObject.SetActive(carry);
                if (carry)
                {
                    float h = layout.agentHeight;
                    float inv = 1f / Mathf.Max(1e-5f, a.lossyScale.y / Mathf.Max(1e-5f, root.lossyScale.y));
                    _prop[r].localPosition = new Vector3(0, h * 0.5f, h * 0.22f) * inv;
                    _prop[r].localScale = new Vector3(h * 0.32f, h * 0.24f, 0.004f) * inv;
                    Tint(_propR[r], act.prop == "warning" ? Amber : act.prop == "blueprint" ? Hex(0x6FB3E0) : act.prop == "receipt" ? Color.Lerp(Lavender, Paper, 0.55f) : Paper);
                }

                if (statusColors != null && r < statusColors.Length) Tint(_status[r], statusColors[r]);
                bool ring = selectArmed || (_managerHint && r == (int)HabitatRole.Manager);
                if (_selectRing[r].gameObject.activeSelf != ring) _selectRing[r].gameObject.SetActive(ring);
                if (ring)
                {
                    _selectRing[r].localPosition = w.Position + new Vector3(0, 0.002f, 0);
                    float pulse = 0.75f + 0.25f * Mathf.Sin(Time.unscaledTime * 4f + r);
                    Tint(_selectRingR[r], !selectArmed ? Lavender : hoveredRole == r ? Color.Lerp(Teal, Color.white, 0.45f) : Teal * pulse);
                }
                bool onVisit = away != null && r < away.Length && away[r];
                if (_visitMarker[r].gameObject.activeSelf != onVisit) _visitMarker[r].gameObject.SetActive(onVisit);
            }

            _beaconPhase += dt;
            float k = beaconPulse ? 0.7f + 0.3f * Mathf.Sin(_beaconPhase * 3.2f) : 1f;
            Tint(_beacon, beaconColor * k);

            for (int i = _cards.Count - 1; i >= 0; i--) StepCard(_cards[i], dt);
        }

        /// <summary>
        /// Foot-skate guard: while walking, the walk cycle plays at the walker's actual speed relative to its cruise speed
        /// (accelerating from rest plays a slow cycle, never a full stride on the spot). A turn in place keeps a slow
        /// stepping cycle. Every other state plays at normal speed.
        /// </summary>
        void MatchStride(int r, HabitatWalker w)
        {
            Animator an = _anim[r];
            if (an == null) return;
            float want = 1f;
            if (w.Anim == WalkerAnim.Walk)
                want = Mathf.Clamp(w.Speed / Mathf.Max(1e-4f, w.settings.walkSpeed), 0.35f, 1.15f);
            if (Mathf.Abs(an.speed - want) > 0.01f) an.speed = want;
        }

        // ── presentation driven by authoritative state ──

        static readonly int S_Thinking = Animator.StringToHash("Thinking"), S_Worried = Animator.StringToHash("Worried"),
            S_Writing = Animator.StringToHash("Writing"), S_Nodding = Animator.StringToHash("Nodding"),
            S_TurnL = Animator.StringToHash("Turn_Left"), S_TurnR = Animator.StringToHash("Turn_Right"), S_Listening = Animator.StringToHash("Listening");
        readonly float[] _worryUntil = new float[4];

        /// <summary>One restrained worried beat for a CONFIRMED failure (task.failed). Desk pose only; never moves the character.</summary>
        public void Worry(HabitatRole r, float seconds = 2f)
        {
            _worryUntil[(int)r] = Time.unscaledTime + seconds;
            _animState[(int)r] = 0;
        }
        readonly DeskActivity[] _activity = new DeskActivity[4];
        readonly float[] _oneShotUntil = new float[4];

        /// <summary>What the character's real task state says it is doing (see HabitatPresence). Desk pose only.</summary>
        public void SetActivity(HabitatRole r, DeskActivity a) { _activity[(int)r] = a; }

        /// <summary>One restrained acknowledgement (a nod) for a confirmed event, e.g. task.completed. Non-blocking:
        /// it plays only while the character is at its desk and never moves it.</summary>
        public void Acknowledge(HabitatRole r, float seconds = 1.2f)
        {
            _oneShotUntil[(int)r] = Time.unscaledTime + seconds;
            _animState[(int)r] = 0;   // force the cross-fade even if the desk clip did not change
        }

        int DeskClip(int r, HabitatRole role)
        {
            if (Time.unscaledTime < _oneShotUntil[r]) return S_Nodding;
            if (Time.unscaledTime < _worryUntil[r]) return S_Worried;
            if (_listening[r]) return S_Listening;
            switch (_activity[r])
            {
                case DeskActivity.Working:
                    return role == HabitatRole.Researcher ? S_Reading : role == HabitatRole.Tester ? S_Inspecting
                         : role == HabitatRole.Manager ? S_Writing : S_Typing;
                case DeskActivity.Waiting: return S_Wait;
                case DeskActivity.Blocked: return S_Thinking;
                case DeskActivity.Problem: return S_Worried;
                default: return S_Idle;
            }
        }

        void SetAnim(int r, WalkerAnim anim, HabitatRole role, HabitatWalker w)
        {
            Animator an = _anim[r];
            if (an == null || !an.isActiveAndEnabled || an.runtimeAnimatorController == null) return;
            int want;
            switch (anim)
            {
                case WalkerAnim.Walk:
                    // Standing still and turning: a turn-in-place step instead of walking on the spot.
                    want = w.Speed < 0.01f && Mathf.Abs(w.TurnRate) > 25f ? (w.TurnRate > 0f ? S_TurnR : S_TurnL) : S_Walk;
                    break;
                case WalkerAnim.Wait: case WalkerAnim.Ride: case WalkerAnim.Idle: want = S_Idle; break;
                case WalkerAnim.Present: want = S_Offer; break;
                default: want = DeskClip(r, role); break;
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
            if (u >= 1f)
            {
                c.landed = true;
                if (c.hideOnLand && c.t.gameObject.activeSelf) c.t.gameObject.SetActive(false);
            }
        }

        // ── Living Office: a committed card prints into the Manager's tray, is collected by its owner (who rides up for it),
        //    carried down and put on the owner's desk. Only ever for canonical, committed task ids. ──

        Vector3 TraySlot(int index)
        {
            HabitatSocket ps = layout.Socket("TaskPrinterSocket");
            return ps.position + new Vector3(0f, 0.043f + 0.0025f * Mathf.Min(index, 8), 0.004f);
        }

        Vector3 DeskSlot(HabitatRole owner, int index)
        {
            HabitatSocket desk = layout.Socket(owner + ".WorkPose");
            return desk.position + new Vector3(0.03f, 0.068f + 0.0025f * Mathf.Min(index, 6), 0.075f);
        }

        void Fly(CardFlight c, Vector3 to, float seconds, float lift, bool viaFront, bool hideOnLand)
        {
            if (!c.t.gameObject.activeSelf) c.t.gameObject.SetActive(true);
            c.from = c.t.localPosition; c.to = to;
            c.mid = new Vector3((c.from.x + to.x) * 0.5f, Mathf.Max(c.from.y, to.y) + lift, viaFront ? layout.size.z + 0.03f : (c.from.z + to.z) * 0.5f);
            c.time = 0f; c.duration = Mathf.Max(0.05f, seconds); c.landed = false; c.hideOnLand = hideOnLand;
        }

        /// <summary>The printer prints one card for a committed task: it slides out of the slot onto the tray.</summary>
        public void PrintToTray(string taskId, HabitatRole owner)
        {
            if (string.IsNullOrEmpty(taskId) || _cardById.ContainsKey(taskId)) return;
            HabitatSocket ps = layout.Socket("TaskPrinterSocket");
            var r = Box(root, "PlanCard_" + owner + "_" + taskId, ps.position + new Vector3(0, 0.03f, 0.03f), new Vector3(0.035f, 0.002f, 0.025f), Paper);
            int n;
            _trayStack.TryGetValue((int)owner, out n);
            int total = 0;
            foreach (var kv in _trayStack) total += kv.Value;
            _trayStack[(int)owner] = n + 1;
            var c = new CardFlight { t = r.transform, r = r, role = (int)owner, taskId = taskId };
            _cardById[taskId] = c;
            _cards.Add(c);
            Fly(c, TraySlot(total), 0.7f, 0.012f, false, false);
        }

        /// <summary>The owner stands at the Manager's desk: its tray cards go to its hands (then the carried prop shows them).</summary>
        public int CardsToAgent(HabitatRole owner, Vector3 agentLocalPos)
        {
            int moved = 0;
            foreach (var kv in _cardById)
            {
                CardFlight c = kv.Value;
                if (c.role != (int)owner || c.onDesk || c.hideOnLand) continue;
                Fly(c, agentLocalPos + new Vector3(0f, layout.agentHeight * 0.5f, 0.02f), 0.6f, 0.03f, false, true);
                moved++;
            }
            if (moved > 0) _trayStack[(int)owner] = 0;
            return moved;
        }

        /// <summary>The owner's cards land on its desk: from its hands when it carried them, else straight from the tray by the
        /// chute in front of the decks (the fallback when a pick-up trip could not run).</summary>
        public int CardsToDesk(HabitatRole owner, Vector3 agentLocalPos)
        {
            int moved = 0;
            foreach (var kv in _cardById)
            {
                CardFlight c = kv.Value;
                if (c.role != (int)owner || c.onDesk) continue;
                int count;
                _deskStack.TryGetValue((int)owner, out count);
                _deskStack[(int)owner] = count + 1;
                bool carried = c.hideOnLand;
                if (carried) { c.t.localPosition = agentLocalPos + new Vector3(0f, layout.agentHeight * 0.5f, 0.02f); }
                c.onDesk = true;
                Fly(c, DeskSlot(owner, count), carried ? 0.5f : 2.2f, carried ? 0.02f : 0.08f, !carried, false);
                moved++;
            }
            if (moved > 0) _trayStack[(int)owner] = 0;
            return moved;
        }

        /// <summary>The card's edge shows its REAL task state colour (the app's state palette).</summary>
        public void SetCardColor(string taskId, Color c)
        {
            CardFlight f;
            if (taskId != null && _cardById.TryGetValue(taskId, out f) && f.r != null) Tint(f.r, Color.Lerp(Paper, c, 0.55f));
        }

        public bool HasCard(string taskId) { return taskId != null && _cardById.ContainsKey(taskId); }
        public int CardsInTray(HabitatRole r) { int c; return _trayStack.TryGetValue((int)r, out c) ? c : 0; }

        /// <summary>Remove every printed card (session reset: the board they belonged to is gone).</summary>
        public void ClearCards()
        {
            for (int i = 0; i < _cards.Count; i++) if (_cards[i].t != null) Object.Destroy(_cards[i].t.gameObject);
            _cards.Clear(); _cardById.Clear(); _trayStack.Clear(); _deskStack.Clear();
        }

        /// <summary>Habitat-local position of a socket in world space (for things that leave the habitat, like the receipt).</summary>
        public Vector3 SocketWorld(string socketId, Vector3 localOffset)
        {
            HabitatSocket s;
            return root.TransformPoint((layout.TryGetSocket(socketId, out s) ? s.position : Vector3.zero) + localOffset);
        }

        public int CardsOnDesk(HabitatRole r) { int c; return _deskStack.TryGetValue((int)r, out c) ? c : 0; }

        public void Destroy()
        {
            if (root != null) Object.Destroy(root.gameObject);
            for (int i = 0; i < _owned.Count; i++) if (_owned[i] != null) Object.Destroy(_owned[i]);
            _owned.Clear();
        }
    }
}
