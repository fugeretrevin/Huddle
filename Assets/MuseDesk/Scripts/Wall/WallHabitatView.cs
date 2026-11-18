using System.Collections.Generic;
using TMPro;
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

        sealed class CardFlight
        {
            public Transform t; public Vector3 from, mid, to; public float time, duration; public int role; public bool landed; public bool hideOnLand; public string taskId; public Renderer r; public bool onDesk;
            // Paper tube (rc18): rise into the inlet rolled up, travel the tube, pop out at the desk and unroll onto the stack.
            public bool tube, tubeStarted; public float tubeT; public Vector3 a, b, c1, c2, d, slot, fullScale;
        }
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

        // Owner rc13: "when you grab an agent or ask to meet with them it makes a copy, so there's one next to you and one
        // still working, which is weird". While its projection (desk visitor or portal visitor) is out, the habitat avatar is
        // not drawn: the agent is visibly AWAY from its desk (the station keeps its visit marker), and comes back into view
        // when the projection is gone. Its walker, task and animator keep running; only drawing stops (forceRenderingOff,
        // which nothing else in the project uses, so it is put back exactly).
        readonly bool[] _hidden = new bool[4];
        readonly Renderer[][] _avatarRenderers = new Renderer[4][];

        public bool IsHidden(HabitatRole r) { return _hidden[(int)r]; }

        /// <summary>Seconds of the teleport-in (the avatar grows back at its desk after Send back / Return).</summary>
        public const float ArriveSeconds = 0.9f;
        readonly float[] _arriveT = { 99f, 99f, 99f, 99f };
        /// <summary>Start the teleport-in for this role: it is drawn from nothing up to full size over <see cref="ArriveSeconds"/>.</summary>
        public void PlayArrive(HabitatRole role) { int r = (int)role; if (r >= 0 && r < 4) _arriveT[r] = 0f; }
        public bool Arriving(HabitatRole role) { int r = (int)role; return r >= 0 && r < 4 && _arriveT[r] < ArriveSeconds; }

        public void SetHidden(HabitatRole role, bool hidden)
        {
            int r = (int)role;
            if (r < 0 || r >= 4 || _hidden[r] == hidden) return;
            Transform a = _avatar[r];
            if (a == null) { _hidden[r] = false; return; }
            _hidden[r] = hidden;
            if (_avatarRenderers[r] == null || hidden) _avatarRenderers[r] = a.GetComponentsInChildren<Renderer>(true);
            foreach (var rr in _avatarRenderers[r]) if (rr != null) rr.forceRenderingOff = hidden;
        }
        public float AvatarHeight => layout.agentHeight;

        /// <summary>True when the Manager avatar is the dedicated Agent_Manager model (not the dressed Developer placeholder).</summary>
        public bool ManagerIsDedicated { get; private set; }

        public WallHabitatView(Transform parent, HabitatLayout layout, Material baseMaterial, GameObject[] agentSources, HabitatArtManifest manifest, GameObject managerSource = null)
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
                // rc20 (owner, worn rc19: "the blocks above the agents' heads are kinda weird"): the state light is a small
                // round lamp on the back wall above the desk (a brass plate and a coloured glass dome), not a cube in the air.
                Vector3 lamp = new Vector3(w.position.x, w.position.y + layout.agentHeight + 0.03f, LampZ);
                Disc(root, "StatusLamp_Plate_" + (HabitatRole)r, lamp - Vector3.forward * 0.0015f, Vector3.forward, 0.0105f, 0.003f, new Color(0.78f, 0.62f, 0.3f));
                _status[r] = Disc(root, "StatusLight_" + (HabitatRole)r, lamp + Vector3.forward * 0.002f, Vector3.forward, 0.0075f, 0.0045f, Grey);
                _selectRing[r] = Box(root, "SelectRing_" + (HabitatRole)r, w.position + new Vector3(0, 0.002f, 0), new Vector3(0.075f, 0.003f, 0.075f), Teal).transform;
                _selectRingR[r] = _selectRing[r].GetComponent<Renderer>();
                _selectRing[r].gameObject.SetActive(false);
                _visitMarker[r] = Box(root, "OnVisitMarker_" + (HabitatRole)r, new Vector3(w.position.x, w.position.y + layout.agentHeight + 0.052f, LampZ), new Vector3(0.03f, 0.007f, 0.004f), Lavender).transform;
                _visitMarker[r].gameObject.SetActive(false);
            }

            // Avatars.
            for (int r = 0; r < 4; r++)
            {
                bool isMgr = r == (int)HabitatRole.Manager;
                Transform a = null;
                // The Manager: the dedicated model when it is wired (same rig and state names as the walkers).
                if (isMgr && managerSource != null)
                {
                    a = CloneVisual(managerSource, "Avatar_" + (HabitatRole)r, root, layout.agentHeight);
                    ManagerIsDedicated = a != null;
                }
                if (a == null)
                {
                    GameObject src = agentSources != null && agentSources.Length > 0 ? agentSources[r == 3 ? 1 : Mathf.Min(r, agentSources.Length - 1)] : null;
                    a = src != null ? CloneVisual(src, "Avatar_" + (HabitatRole)r, root, layout.agentHeight) : null;
                }
                if (a == null) a = FallbackAvatar(root, (HabitatRole)r);
                if (isMgr && !ManagerIsDedicated) DressManager(a);
                _avatar[r] = a;
                _avatarScale[r] = a.localScale;
                _anim[r] = a.GetComponentInChildren<Animator>();
                if (_anim[r] != null) { _anim[r].applyRootMotion = false; _anim[r].cullingMode = AnimatorCullingMode.CullUpdateTransforms; }
                _animState[r] = 0;
                ResolveCarryBones(r, a);
                _prop[r] = Box(a, "CarriedProp", Vector3.zero, Vector3.one, Paper).transform;
                _propR[r] = _prop[r].GetComponent<Renderer>();
                _prop[r].gameObject.SetActive(false);
            }

            if (manifest != null) { _artRoots = manifest.ApplyTo(this); BindArtElevator(); OpenManagerSideWall(); FixMonitorScreens(); }
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

        /// <summary>For drivers: triangles removed from the Manager floor's right-hand end wall (0 = not opened).</summary>
        public int ManagerWallTrianglesCut { get; private set; }

        /// <summary>
        /// rc22 (owner, worn rc21: "get rid of the right wall on the third floor of the wall mode so we can see through to the
        /// manager from our desk if it's to the left of us"). The Manager floor's structure is one combined mesh; its right-hand
        /// end wall (seen from the front: the habitat's -X end) is cut out of a runtime copy: every triangle lying in the thin
        /// slab at that end, above the floor and in front of the back panel. The floor slab, the back wall, the roof and the
        /// other floors are untouched.
        /// </summary>
        void OpenManagerSideWall()
        {
            if (_artRoots == null) return;
            MeshFilter mf = null;
            foreach (var g in _artRoots)
            {
                if (g == null) continue;
                foreach (var f in g.GetComponentsInChildren<MeshFilter>(true))
                    if (f.name == "WallHabitat_ManagerLookout_Structure") { mf = f; break; }
                if (mf != null) break;
            }
            if (mf == null || mf.sharedMesh == null || !mf.sharedMesh.isReadable) return;
            Mesh src = mf.sharedMesh;
            Vector3[] v = src.vertices;
            var hv = new Vector3[v.Length];
            float minX = float.MaxValue;
            for (int i = 0; i < v.Length; i++) { hv[i] = root.InverseTransformPoint(mf.transform.TransformPoint(v[i])); if (hv[i].x < minX) minX = hv[i].x; }
            float floorTop = 0.148f;
            for (int f = 0; f < layout.floors.Count; f++) if (layout.floors[f].id == "Manager") floorTop = layout.floors[f].floorY - 0.002f;
            Mesh m = Object.Instantiate(src);
            m.name = src.name + "_OpenRight";
            int cut = 0;
            for (int s = 0; s < m.subMeshCount; s++)
            {
                int[] t = m.GetTriangles(s);
                var keep = new List<int>(t.Length);
                for (int i = 0; i < t.Length; i += 3)
                {
                    Vector3 a = hv[t[i]], b = hv[t[i + 1]], c = hv[t[i + 2]];
                    bool inSlab = Mathf.Max(a.x, Mathf.Max(b.x, c.x)) < minX + 0.025f
                               && Mathf.Min(a.y, Mathf.Min(b.y, c.y)) > floorTop
                               && Mathf.Max(a.z, Mathf.Max(b.z, c.z)) > 0.03f;
                    if (inSlab) { cut++; continue; }
                    keep.Add(t[i]); keep.Add(t[i + 1]); keep.Add(t[i + 2]);
                }
                m.SetTriangles(keep, s, false);
            }
            if (cut == 0) { Object.Destroy(m); return; }
            m.RecalculateBounds();
            mf.sharedMesh = m;
            ManagerWallTrianglesCut = cut;
        }

        /// <summary>For drivers: triangles moved off the glowing screen material (the back and edges of the desk monitors).</summary>
        public int ScreenTrianglesFixed { get; private set; }

        /// <summary>For drivers: the monitor renderers fixed, with their kept/moved triangle counts.</summary>
        public readonly List<KeyValuePair<Renderer, string>> ScreenFixLog = new List<KeyValuePair<Renderer, string>>();

        /// <summary>
        /// rc23 (owner, worn rc21: "monitors clip, and the developer workstation's screen is visible from both sides").
        /// The art's monitor screens are thin boxes with the glowing screen material on every face. The agent stands behind the
        /// desk (habitat-local smaller z) facing the user (+Z, Yaw 0) so the monitor's screen faces -Z; the user sees its back.
        /// On a runtime mesh copy (the asset is never touched) only the triangles whose normal points toward -Z (the agent) keep
        /// the screen material, nudged 0.3 mm that way off the bezel; the back and the edges go to a second submesh in the
        /// station's charcoal material. Unreadable meshes are skipped. Runs once at build.
        /// </summary>
        void FixMonitorScreens()
        {
            if (_artRoots == null) return;
            Vector3 towardAgentWorld = root.TransformDirection(Vector3.back).normalized;
            foreach (var g in _artRoots)
            {
                if (g == null) continue;
                foreach (var mr in g.GetComponentsInChildren<MeshRenderer>(true))
                {
                    if (!mr.name.EndsWith("Screen", System.StringComparison.Ordinal)) continue;
                    var mf = mr.GetComponent<MeshFilter>();
                    Material screen = mr.sharedMaterial;
                    if (mf == null || mf.sharedMesh == null || screen == null || mr.sharedMaterials.Length != 1) continue;
                    if (screen.name.IndexOf("Screen", System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                    Mesh src = mf.sharedMesh;
                    if (!src.isReadable || src.subMeshCount != 1 || src.name.EndsWith("_FrontOnly")) continue;
                    Material dark = null;
                    if (mr.transform.parent != null)
                        foreach (var sib in mr.transform.parent.GetComponentsInChildren<MeshRenderer>(true))
                            if (sib.sharedMaterial != null && sib.sharedMaterial.name.IndexOf("Charcoal", System.StringComparison.OrdinalIgnoreCase) >= 0) { dark = sib.sharedMaterial; break; }
                    if (dark == null) continue;

                    Transform t = mf.transform;
                    Vector3[] v = src.vertices; int[] tri = src.triangles;
                    var front = new List<int>(tri.Length); var rest = new List<int>(tri.Length);
                    for (int i = 0; i < tri.Length; i += 3)
                    {
                        Vector3 a = t.TransformPoint(v[tri[i]]), b = t.TransformPoint(v[tri[i + 1]]), c = t.TransformPoint(v[tri[i + 2]]);
                        Vector3 n = Vector3.Cross(b - a, c - a);
                        var dst = n.sqrMagnitude > 1e-16f && Vector3.Dot(n.normalized, towardAgentWorld) > 0.5f ? front : rest;
                        dst.Add(tri[i]); dst.Add(tri[i + 1]); dst.Add(tri[i + 2]);
                    }
                    if (front.Count == 0 || rest.Count == 0) continue;

                    // Nudge only the vertices the screen face alone uses, so the back and edges stay where they are.
                    var usedByRest = new HashSet<int>(rest);
                    var moved = new HashSet<int>();
                    Vector3 nudge = t.InverseTransformVector(towardAgentWorld * 0.0003f);
                    for (int i = 0; i < front.Count; i++) if (!usedByRest.Contains(front[i])) moved.Add(front[i]);
                    foreach (int i in moved) v[i] += nudge;

                    Mesh m = Object.Instantiate(src);
                    m.name = src.name + "_FrontOnly";
                    m.vertices = v;
                    m.subMeshCount = 2;
                    m.SetTriangles(front, 0, false);
                    m.SetTriangles(rest, 1, false);
                    m.RecalculateBounds();
                    mf.sharedMesh = m;
                    mr.sharedMaterials = new[] { screen, dark };
                    ScreenTrianglesFixed += rest.Count / 3;
                    ScreenFixLog.Add(new KeyValuePair<Renderer, string>(mr, "screenTris=" + front.Count / 3 + " movedToDark=" + rest.Count / 3 + " nudgedVerts=" + moved.Count + " dark=" + dark.name));
                }
            }
        }

        /// <summary>Habitat-local z of things mounted on the back wall (the back panels end at z 0.015-0.019).</summary>
        const float LampZ = 0.0225f;

        static Mesh _cylinder;

        /// <summary>A unit cylinder (radius 0.5, height 1, along Y) with caps; built once.</summary>
        static Mesh UnitCylinder()
        {
            if (_cylinder != null) return _cylinder;
            const int sides = 20;
            var v = new List<Vector3>(); var n = new List<Vector3>(); var tri = new List<int>();
            for (int cap = 0; cap < 2; cap++)
            {
                float y = cap == 0 ? -0.5f : 0.5f; Vector3 nn = cap == 0 ? Vector3.down : Vector3.up;
                int c0 = v.Count; v.Add(new Vector3(0, y, 0)); n.Add(nn);
                for (int i = 0; i <= sides; i++) { float a = i * Mathf.PI * 2f / sides; v.Add(new Vector3(Mathf.Cos(a) * 0.5f, y, Mathf.Sin(a) * 0.5f)); n.Add(nn); }
                for (int i = 0; i < sides; i++)
                {
                    if (cap == 1) { tri.Add(c0); tri.Add(c0 + i + 2); tri.Add(c0 + i + 1); }
                    else { tri.Add(c0); tri.Add(c0 + i + 1); tri.Add(c0 + i + 2); }
                }
            }
            int s0 = v.Count;
            for (int i = 0; i <= sides; i++)
            {
                float a = i * Mathf.PI * 2f / sides; Vector3 d = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                v.Add(d * 0.5f + Vector3.down * 0.5f); n.Add(d);
                v.Add(d * 0.5f + Vector3.up * 0.5f); n.Add(d);
            }
            for (int i = 0; i < sides; i++)
            {
                int a = s0 + i * 2, b = a + 2;
                tri.Add(a); tri.Add(a + 1); tri.Add(b + 1);
                tri.Add(a); tri.Add(b + 1); tri.Add(b);
            }
            _cylinder = new Mesh { name = "WallHabitat_UnitCylinder" };
            _cylinder.SetVertices(v); _cylinder.SetNormals(n); _cylinder.SetTriangles(tri, 0);
            _cylinder.RecalculateBounds();
            return _cylinder;
        }

        /// <summary>A round disc / short cylinder of <paramref name="radius"/> and <paramref name="thickness"/> whose axis is
        /// <paramref name="axis"/> (habitat-local), centred on <paramref name="localCenter"/>.</summary>
        public Renderer Disc(Transform parent, string name, Vector3 localCenter, Vector3 axis, float radius, float thickness, Color c)
        {
            var g = new GameObject(name);
            g.transform.SetParent(parent, false);
            g.transform.localPosition = localCenter;
            g.transform.localRotation = Quaternion.FromToRotation(Vector3.up, axis.sqrMagnitude > 1e-8f ? axis.normalized : Vector3.up);
            g.transform.localScale = new Vector3(radius * 2f, thickness, radius * 2f);
            g.AddComponent<MeshFilter>().sharedMesh = UnitCylinder();
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
            // A clone is meant to be seen even if its source is hidden while away (SetHidden).
            foreach (var r in clone.GetComponentsInChildren<Renderer>(true)) r.forceRenderingOff = false;
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
            Color body = r == HabitatRole.Researcher ? Workspace.OfficeTokens.RoleResearcher : r == HabitatRole.Developer ? Workspace.OfficeTokens.RoleDeveloper : r == HabitatRole.Tester ? Workspace.OfficeTokens.RoleTester : Workspace.OfficeTokens.RoleManager;
            Box(a, "Body", new Vector3(0, h * 0.35f, 0), new Vector3(h * 0.38f, h * 0.7f, h * 0.3f), body);
            Box(a, "Head", new Vector3(0, h * 0.82f, 0), new Vector3(h * 0.34f, h * 0.3f, h * 0.3f), Paper);
            Box(a, "Eyes", new Vector3(0, h * 0.84f, h * 0.152f), new Vector3(h * 0.2f, h * 0.05f, 0.002f), Ink);
            return a;
        }

        void DressManager(Transform a)
        {
            float h = layout.agentHeight;
            // Fallback Manager (only when managerSource is not wired): the Developer mesh with a lavender cap and a brass badge.
            // (A colour tint on the character's toon material rendered translucent in Play Mode: not used.)
            var cap = Box(a, "ManagerCap", Vector3.zero, Vector3.one, Workspace.OfficeTokens.RoleManager).transform;
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
                // Teleport-in at the desk (Send back): the avatar grows out of its light column instead of popping in.
                if (_arriveT[r] < ArriveSeconds)
                {
                    _arriveT[r] += dt;
                    float ak = Mathf.Clamp01(_arriveT[r] / ArriveSeconds);
                    vis *= ak < 0.25f ? 0f : 1f - Mathf.Pow(1f - (ak - 0.25f) / 0.75f, 3f);   // hold briefly in the light, then ease out
                }
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
                    _prop[r].localScale = new Vector3(h * 0.32f, h * 0.24f, 0.004f) * inv;
                    PoseCarried(r, a, w, dt);
                    Tint(_propR[r], act.prop == "warning" ? Amber : act.prop == "blueprint" ? Hex(0x6FB3E0) : act.prop == "receipt" ? Color.Lerp(Lavender, Paper, 0.55f) : Paper);
                }
                TickLiftCue(r, w);

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
            StepAllMail(dt);
            StepLabels(stage, dt);
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

        // ── the carried paper is held IN THE HAND (rc21; owner, worn rc20: "the paper isn't attached to their hands fully, so
        //    when they move up/down or side to side the paper stays the same orientation"). It used to sit at a fixed point in
        //    front of the avatar root, so it ignored the arms and the body's bob. Now it rides the right hand bone with a fixed
        //    grip offset (position AND rotation), blended 80 % hand / 20 % chest so a swinging arm reads as held, not flailing.
        //    The grip is measured once per avatar from the carry walk itself (the mean hand pose over half a second of
        //    Walk_Carry_Paper), so it needs no knowledge of the rig's bone axes. Clamps keep the sheet in front of the body
        //    (never through it) and within 40 degrees of upright. No bone found (placeholder avatar): the old fixed pose. ──

        public const float CarryHandWeight = 0.8f, CarryTiltLimit = 40f, CarryBaseTilt = -20f, CarryForwardMin = 0.2f;
        public const float CarryCalibrateSeconds = 0.5f, CarryBlendSeconds = 0.25f, CarryAnyWalkAfter = 2.5f;
        static readonly string[] HandNames = { "RightHand_Attach", "TaskCardHold", "Hand_R", "hand_r", "Hand.R", "hand.R", "RightHand", "mixamorig:RightHand" };
        static readonly string[] OtherHandNames = { "LeftHand_Attach", "Hand_L", "hand_l", "Hand.L", "hand.L", "LeftHand", "mixamorig:LeftHand" };
        static readonly string[] ChestNames = { "Chest", "chest", "Spine2", "mixamorig:Spine2", "Spine1", "Spine", "spine" };
        readonly Transform[] _gripHand = new Transform[4], _gripOther = new Transform[4], _gripChest = new Transform[4];
        readonly bool[] _gripReady = new bool[4];
        readonly float[] _gripSampleT = new float[4], _gripWaitT = new float[4], _gripBlend = new float[4];
        readonly int[] _gripSamples = new int[4];
        readonly Vector3[] _gripSumHand = new Vector3[4], _gripSumOther = new Vector3[4], _gripSumChest = new Vector3[4];
        readonly Vector4[] _gripSumHandQ = new Vector4[4], _gripSumChestQ = new Vector4[4];
        readonly Vector3[] _gripHandPos = new Vector3[4], _gripChestPos = new Vector3[4];
        readonly Quaternion[] _gripHandRot = new Quaternion[4], _gripChestRot = new Quaternion[4];

        /// <summary>For drivers: the hand bone the paper rides (null = old fixed pose) and whether its grip is measured.</summary>
        public string CarryHandName(HabitatRole r) { Transform t = _gripHand[(int)r]; return t != null ? t.name : null; }
        public bool CarryGripReady(HabitatRole r) { return _gripReady[(int)r]; }
        /// <summary>For drivers: the carried paper's transform (active only while carrying).</summary>
        public Transform CarriedProp(HabitatRole r) { return _prop[(int)r]; }

        void ResolveCarryBones(int r, Transform a)
        {
            _gripHand[r] = FindNamed(a, HandNames);
            _gripOther[r] = FindNamed(a, OtherHandNames);
            _gripChest[r] = FindNamed(a, ChestNames);
        }

        static Transform FindNamed(Transform root, string[] names)
        {
            if (root == null) return null;
            Transform[] all = root.GetComponentsInChildren<Transform>(true);
            for (int n = 0; n < names.Length; n++)
                for (int i = 0; i < all.Length; i++)
                    if (all[i] != root && all[i].name == names[n]) return all[i];
            return null;
        }

        static void AddQ(ref Vector4 sum, Quaternion q)
        {
            var v = new Vector4(q.x, q.y, q.z, q.w);
            if (Vector4.Dot(sum, v) < 0f) v = -v;     // same hemisphere: a mean of nearby rotations
            sum += v;
        }

        static Quaternion MeanQ(Vector4 sum)
        {
            if (sum.sqrMagnitude < 1e-12f) return Quaternion.identity;
            Vector4 n = sum.normalized;
            return new Quaternion(n.x, n.y, n.z, n.w);
        }

        /// <summary>The carry walk is playing steadily (not cross-fading, at cruise): the hand pose is a good grip sample.</summary>
        bool CarryPoseSettled(int r, HabitatWalker w)
        {
            if (w.Anim != WalkerAnim.Walk || w.Speed < 0.6f * w.settings.walkSpeed) return false;
            Animator an = _anim[r];
            if (an == null || !an.isActiveAndEnabled || an.runtimeAnimatorController == null) return true;
            if (an.IsInTransition(0)) return false;
            int cur = an.GetCurrentAnimatorStateInfo(0).shortNameHash;
            if (cur == S_WalkCarry) return true;
            // A controller without the carry clip (or one that never switched to it) samples the plain walk.
            return cur == S_Walk && (!an.HasState(0, S_WalkCarry) || _gripWaitT[r] > CarryAnyWalkAfter);
        }

        void PoseCarried(int r, Transform a, HabitatWalker w, float dt)
        {
            Transform prop = _prop[r];
            // Avatar-local units: positions below are in the avatar's own space (InverseTransformPoint), so they do not
            // change with the arrive/soft-transfer scale.
            float hl = layout.agentHeight / Mathf.Max(1e-5f, _avatarScale[r].y);
            Vector3 basePos = new Vector3(0f, hl * 0.5f, hl * 0.22f);
            Quaternion baseRot = Quaternion.Euler(CarryBaseTilt, 0f, 0f);      // upright, the face tipped up a little
            Transform hand = _gripHand[r];
            if (hand == null) { prop.localPosition = basePos; prop.localRotation = baseRot; return; }

            Quaternion toA = Quaternion.Inverse(a.rotation);
            Vector3 hp = a.InverseTransformPoint(hand.position);
            Quaternion hq = toA * hand.rotation;
            Transform chest = _gripChest[r];
            Vector3 cp = chest != null ? a.InverseTransformPoint(chest.position) : new Vector3(0f, hl * 0.5f, 0f);
            Quaternion cq = chest != null ? toA * chest.rotation : Quaternion.identity;

            if (!_gripReady[r])
            {
                if (w.Anim == WalkerAnim.Walk) _gripWaitT[r] += dt;
                if (CarryPoseSettled(r, w))
                {
                    _gripSamples[r]++;
                    _gripSampleT[r] += dt;
                    _gripSumHand[r] += hp; _gripSumChest[r] += cp;
                    if (_gripOther[r] != null) _gripSumOther[r] += a.InverseTransformPoint(_gripOther[r].position);
                    AddQ(ref _gripSumHandQ[r], hq); AddQ(ref _gripSumChestQ[r], cq);
                    if (_gripSampleT[r] >= CarryCalibrateSeconds && _gripSamples[r] >= 3) CalibrateGrip(r, hl, baseRot);
                }
                prop.localPosition = basePos; prop.localRotation = baseRot;
                return;
            }

            Vector3 ph = hp + hq * _gripHandPos[r];
            Quaternion qh = hq * _gripHandRot[r];
            Vector3 pc = cp + cq * _gripChestPos[r];
            Quaternion qc = cq * _gripChestRot[r];
            Vector3 p = Vector3.Lerp(pc, ph, CarryHandWeight);
            Quaternion q = Quaternion.Slerp(qc, qh, CarryHandWeight);
            float ang = Quaternion.Angle(baseRot, q);
            if (ang > CarryTiltLimit) q = Quaternion.Slerp(baseRot, q, CarryTiltLimit / ang);
            float minZ = cp.z + CarryForwardMin * hl;                       // in front of the body, never through it
            if (p.z < minZ) p.z = minZ;
            p.y = Mathf.Clamp(p.y, 0.22f * hl, 0.85f * hl);
            _gripBlend[r] = Mathf.MoveTowards(_gripBlend[r], 1f, dt / CarryBlendSeconds);
            float k = _gripBlend[r];
            prop.localPosition = Vector3.Lerp(basePos, p, k);
            prop.localRotation = Quaternion.Slerp(baseRot, q, k);
        }

        /// <summary>Measure the grip from the mean carry pose: the sheet sits just above and in front of the hands (between
        /// them when both hands hold it), upright; that offset is then fixed in the hand's and the chest's own frames.</summary>
        void CalibrateGrip(int r, float hl, Quaternion baseRot)
        {
            float n = Mathf.Max(1, _gripSamples[r]);
            Vector3 mh = _gripSumHand[r] / n, mc = _gripSumChest[r] / n;
            Quaternion qh = MeanQ(_gripSumHandQ[r]), qc = MeanQ(_gripSumChestQ[r]);
            Vector3 hold = mh;
            if (_gripOther[r] != null)
            {
                Vector3 mo = _gripSumOther[r] / n;
                if ((mo - mh).magnitude < 0.35f * hl) hold = (mh + mo) * 0.5f;   // both hands on the sheet
            }
            Vector3 want = hold + new Vector3(0f, 0.05f * hl, 0.03f * hl);
            want.z = Mathf.Max(want.z, mc.z + CarryForwardMin * hl);
            want.y = Mathf.Clamp(want.y, 0.3f * hl, 0.75f * hl);
            Quaternion ih = Quaternion.Inverse(qh), ic = Quaternion.Inverse(qc);
            _gripHandPos[r] = ih * (want - mh); _gripHandRot[r] = ih * baseRot;
            _gripChestPos[r] = ic * (want - mc); _gripChestRot[r] = ic * baseRot;
            _gripReady[r] = true;
            _gripBlend[r] = 0f;
        }

        // ── presentation driven by authoritative state ──

        static readonly int S_Thinking = Animator.StringToHash("Thinking"), S_Worried = Animator.StringToHash("Worried"),
            S_Writing = Animator.StringToHash("Writing"), S_Nodding = Animator.StringToHash("Nodding"),
            S_TurnL = Animator.StringToHash("Turn_Left"), S_TurnR = Animator.StringToHash("Turn_Right"), S_Listening = Animator.StringToHash("Listening"),
            S_MgrLookout = Animator.StringToHash("Manager_Lookout_Idle"), S_MgrReview = Animator.StringToHash("Manager_Review_Plan"),
            S_WalkCarry = Animator.StringToHash("Walk_Carry_Paper");
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
                    if (role == HabitatRole.Manager && ManagerIsDedicated) return S_MgrReview;
                    return role == HabitatRole.Researcher ? S_Reading : role == HabitatRole.Tester ? S_Inspecting
                         : role == HabitatRole.Manager ? S_Writing : S_Typing;
                case DeskActivity.Waiting: return S_Wait;
                case DeskActivity.Blocked: return S_Thinking;
                case DeskActivity.Problem: return S_Worried;
                default: return role == HabitatRole.Manager && ManagerIsDedicated ? S_MgrLookout : S_Idle;
            }
        }

        static readonly int P_MoveSpeed = Animator.StringToHash("MoveSpeed"), P_Carrying = Animator.StringToHash("IsCarryingPaper");
        readonly sbyte[] _hasParams = new sbyte[4];   // 0 unknown, 1 yes, -1 no

        /// <summary>
        /// The shared agent controllers carry their own locomotion transitions (Idle -> Walk_Loop when MoveSpeed &gt; 0.02,
        /// Walk_Loop -> Idle when MoveSpeed &lt; 0.015, Walk_Loop &lt;-&gt; Walk_Carry_Paper on IsCarryingPaper). The desk agents feed
        /// those parameters from AgentMovement; a habitat clone has no movement script, so the view must feed them too.
        /// Without this, every walk cross-fades in and the controller sends it straight back to idle (rc9: "the legs walk
        /// for a second, then slide").
        /// </summary>
        void FeedLocomotion(int r, Animator an, HabitatWalker w, bool carrying)
        {
            if (_hasParams[r] == 0)
            {
                bool m = false, c = false;
                foreach (var p in an.parameters) { if (p.nameHash == P_MoveSpeed) m = true; else if (p.nameHash == P_Carrying) c = true; }
                _hasParams[r] = (sbyte)(m && c ? 1 : m ? 2 : -1);
            }
            if (_hasParams[r] < 0) return;
            bool walking = w.Anim == WalkerAnim.Walk && w.Speed >= 0.01f;
            an.SetFloat(P_MoveSpeed, walking ? 1f : 0f);
            if (_hasParams[r] == 1) an.SetBool(P_Carrying, walking && carrying);
        }

        void SetAnim(int r, WalkerAnim anim, HabitatRole role, HabitatWalker w)
        {
            Animator an = _anim[r];
            if (an == null || !an.isActiveAndEnabled || an.runtimeAnimatorController == null) return;
            FeedLocomotion(r, an, w, _prop[r] != null && _prop[r].gameObject.activeSelf);
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
            if (!an.HasState(0, want)) want = an.HasState(0, S_Idle) ? S_Idle : 0;
            if (want == 0) return;
            if (_animState[r] == want)
            {
                // Re-assert when the controller's own transitions moved off the wanted clip (never fight a running fade).
                // Walking may legitimately be Walk_Carry_Paper (IsCarryingPaper); a one-shot turn may end on idle.
                if (an.IsInTransition(0)) return;
                int cur = an.GetCurrentAnimatorStateInfo(0).shortNameHash;
                if (cur == want || (want == S_Walk && cur == S_WalkCarry) || want == S_TurnL || want == S_TurnR) return;
            }
            _animState[r] = want;
            an.CrossFadeInFixedTime(want, 0.2f, 0);
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
            if (c.tube)
            {
                c.tubeT += dt;
                if (c.tubeT >= 0f)
                {
                    if (!c.tubeStarted) { c.tubeStarted = true; c.a = c.t.localPosition; }
                    StepTube(c);
                    return;
                }
                // Still waiting for its turn: finish the print flight into the tray first.
            }
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

        // ── Paper tubes (owner, worn rc17: the Manager's cards went out "one by one" and slowly; "have papers come down to them
        //    through little paper tubes"). One glass tube per owner from the printer to above its desk, in front of the decks.
        //    A card rises into the inlet rolling up, rides the tube, pops out over the desk and unrolls onto the stack.
        //    Presentation only: the card is the same committed task card; nothing here changes task state. ──

        /// <summary>Transparent glow material for the tube glass (set by the controller; null = a faint opaque tint).</summary>
        public Material tubeMaterial;
        public const float TubeRise = 0.35f, TubeTravel = 1.25f, TubeDrop = 0.4f;
        public const float TubeRadius = 0.0055f;
        readonly Transform[] _tube = new Transform[3];
        readonly Vector3[][] _tubePts = new Vector3[3][];
        readonly float[][] _tubeLen = new float[3][];

        /// <summary>The ceiling above a habitat-local point (the floor it stands on), or the point + 0.12 when unknown.</summary>
        float CeilingAbove(Vector3 p)
        {
            for (int f = 0; f < layout.floors.Count; f++)
                if (p.y >= layout.floors[f].floorY - 0.01f && p.y < layout.floors[f].ceilingY) return layout.floors[f].ceilingY;
            return p.y + 0.12f;
        }

        /// <summary>
        /// A pneumatic-tube route, like a real office's: up out of the printer, back to the BACK wall (owner, rc18 worn: "they
        /// should go over the back wall, not in front"), straight down the back wall, along it under the ceiling of the owner's
        /// floor, then forward under that ceiling to above its desk and down onto the desk (beside the stack, never in front of
        /// the agent's face). Nothing crosses in front of the decks. Corners are rounded. Habitat-local points.
        /// </summary>
        Vector3[] TubeRoute(HabitatRole owner)
        {
            int r = (int)owner;
            float lane = (r - 1) * 0.013f;                                   // three tubes side by side, never through each other
            float back = TubeBackZ + 0.007f * r;                             // on the back wall, a little apart
            Vector3 tray = TraySlot(0);
            Vector3 slot = DeskSlot(owner, 0);
            Vector3 outlet = slot + new Vector3(0.028f, 0.028f, -0.012f);    // beside and just above the stack
            float run = CeilingAbove(slot) - 0.016f - 0.007f * r;            // tucked under the ceiling, stacked a little
            float top = tray.y + 0.075f;
            var poly = new List<Vector3>
            {
                tray + new Vector3(lane, 0.035f, 0f),                         // inlet above the tray
                new Vector3(tray.x + lane, top, tray.z),
                new Vector3(tray.x + lane, top, back),
                new Vector3(tray.x + lane, run, back),
                new Vector3(outlet.x, run, back),
                new Vector3(outlet.x, run, outlet.z),
                outlet,
            };
            return Rounded(poly, 0.03f, 6);
        }

        /// <summary>Habitat-local z of the tubes on the back wall: just in front of the back panels (z 0.015-0.019).</summary>
        public const float TubeBackZ = 0.028f;

        /// <summary>Round every inner corner of a polyline with a quadratic arc of up to <paramref name="radius"/>.</summary>
        static Vector3[] Rounded(List<Vector3> p, float radius, int steps)
        {
            var o = new List<Vector3> { p[0] };
            for (int i = 1; i < p.Count - 1; i++)
            {
                Vector3 a = p[i - 1], c = p[i], b = p[i + 1];
                float ra = Mathf.Min(radius, (c - a).magnitude * 0.45f), rb = Mathf.Min(radius, (b - c).magnitude * 0.45f);
                Vector3 s = c + (a - c).normalized * ra, e = c + (b - c).normalized * rb;
                for (int k = 0; k <= steps; k++)
                {
                    float t = k / (float)steps, u = 1f - t;
                    o.Add(u * u * s + 2f * u * t * c + t * t * e);
                }
            }
            o.Add(p[p.Count - 1]);
            return o.ToArray();
        }

        static float[] Cumulative(Vector3[] pts)
        {
            var len = new float[pts.Length];
            for (int i = 1; i < pts.Length; i++) len[i] = len[i - 1] + (pts[i] - pts[i - 1]).magnitude;
            return len;
        }

        /// <summary>The point at fraction <paramref name="u"/> of the tube's length, and its direction of travel.</summary>
        static Vector3 AlongTube(Vector3[] pts, float[] len, float u, out Vector3 dir)
        {
            float target = Mathf.Clamp01(u) * len[len.Length - 1];
            int i = 1;
            while (i < len.Length - 1 && len[i] < target) i++;
            float seg = Mathf.Max(1e-6f, len[i] - len[i - 1]);
            dir = (pts[i] - pts[i - 1]).normalized;
            return Vector3.Lerp(pts[i - 1], pts[i], (target - len[i - 1]) / seg);
        }

        /// <summary>Build the three tubes (once; again after the habitat is rebuilt). Safe to call every frame.</summary>
        public void EnsureTubes()
        {
            for (int r = 0; r < 3; r++)
            {
                if (_tube[r] != null) continue;
                Vector3[] pts = TubeRoute((HabitatRole)r);
                _tubePts[r] = pts;
                _tubeLen[r] = Cumulative(pts);
                var go = new GameObject("CardTube_" + (HabitatRole)r);
                go.transform.SetParent(root, false);
                go.AddComponent<MeshFilter>().sharedMesh = TubeMesh(pts, TubeRadius, 10);
                var mr = go.AddComponent<MeshRenderer>();
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mr.receiveShadows = false;
                if (tubeMaterial != null) mr.sharedMaterial = tubeMaterial;
                else { mr.sharedMaterial = _mat; Tint(mr, Color.Lerp(Teal, Color.white, 0.7f)); }
                _tube[r] = go.transform;
                // Brass collars at the inlet and the outlet so the ends read as fittings, not cut glass.
                Color brass = new Color(0.78f, 0.62f, 0.3f);
                Box(go.transform, "Collar_In", pts[0], new Vector3(TubeRadius * 2.8f, 0.005f, TubeRadius * 2.8f), brass);
                Box(go.transform, "Collar_Out", pts[pts.Length - 1], new Vector3(TubeRadius * 2.8f, 0.005f, TubeRadius * 2.8f), brass);
                PassThroughCollars(go.transform, pts, brass);
            }
        }

        /// <summary>For drivers: brass collars placed where the tubes pass through a slab or a wall.</summary>
        public int TubeCollars { get; private set; }

        /// <summary>
        /// rc20 (owner, worn rc19: "the pipes shouldn't go through the floors and walls, add holes for them"): wherever a tube
        /// passes through a floor slab, the roof, a partition or a wall, a brass flange sits on each face it crosses, with a
        /// dark ring inside it (the hole), so the tube reads as fitted through the slab, not cut into it.
        /// </summary>
        void PassThroughCollars(Transform parent, Vector3[] pts, Color brass)
        {
            Color hole = new Color(0.10f, 0.08f, 0.07f);
            var hits = new List<Vector3>(); var dirs = new List<Vector3>();
            // The solid things a tube can pass: the layout's decks, roof, partitions and walls, plus each floor's ceiling slab
            // across the floor's full width (the art's ceilings span wider than the layout's walkable decks).
            var solids = new List<KeyValuePair<Vector3, Vector3>>();
            float back = 0.015f, depth = layout.size.z;
            for (int b = 0; b < layout.boxes.Count; b++)
            {
                HabitatBox box = layout.boxes[b];
                if (box.kind == HabitatBoxKind.Deck || box.kind == HabitatBoxKind.Roof || box.kind == HabitatBoxKind.EndWall
                    || box.kind == HabitatBoxKind.ShaftWall)
                    solids.Add(new KeyValuePair<Vector3, Vector3>(box.Min, box.Max));
                else if (box.kind == HabitatBoxKind.Partition)
                {
                    // In the art the bay walls run up to the bay ceiling (the layout's partition is only the walk blocker).
                    Vector3 mx = box.Max;
                    if (box.floor >= 0 && box.floor < layout.floors.Count) mx.y = Mathf.Max(mx.y, layout.floors[box.floor].ceilingY);
                    solids.Add(new KeyValuePair<Vector3, Vector3>(box.Min, mx));
                }
            }
            for (int f = 0; f < layout.floors.Count; f++)
            {
                // Calibrated against the art (screenshots, rc20): the lower floor's cubicle bays have their own ceiling
                // just BELOW the layout's ceiling line; the upper floors' ceilings are the slab ABOVE it.
                HabitatFloor fl = layout.floors[f];
                float y0 = f == 0 ? fl.ceilingY - 0.021f : fl.ceilingY, y1 = f == 0 ? fl.ceilingY : fl.ceilingY + 0.02f;
                solids.Add(new KeyValuePair<Vector3, Vector3>(new Vector3(fl.xMin, y0, back), new Vector3(fl.xMax, y1, depth)));
            }
            for (int b = 0; b < solids.Count; b++)
            {
                Vector3 mn = solids[b].Key, mx = solids[b].Value;
                for (int i = 1; i < pts.Length; i++)
                {
                    Vector3 a = pts[i - 1], d = pts[i] - pts[i - 1];
                    float t0, t1;
                    if (!SegmentBox(a, d, mn, mx, out t0, out t1)) continue;
                    Vector3 dir = d.normalized;
                    if (t0 > 0f) { hits.Add(a + d * t0); dirs.Add(dir); }     // enters through a face on this segment
                    if (t1 < 1f) { hits.Add(a + d * t1); dirs.Add(dir); }     // leaves through a face on this segment
                }
            }
            for (int k = 0; k < hits.Count; k++)
            {
                // Two hits closer than a flange are one crossing seen from two boxes that touch.
                bool dup = false;
                for (int j = 0; j < k && !dup; j++) dup = (hits[j] - hits[k]).sqrMagnitude < 0.006f * 0.006f;
                if (dup) continue;
                Disc(parent, "PassCollar_" + k, hits[k], dirs[k], TubeRadius * 1.5f, 0.0032f, brass);
                Disc(parent, "PassHole_" + k, hits[k], dirs[k], TubeRadius * 1.22f, 0.004f, hole);
                TubeCollars++;
            }
        }

        /// <summary>Slab test: the part [t0, t1] of segment a + d*t (t in 0..1) inside the box. False when it misses.</summary>
        static bool SegmentBox(Vector3 a, Vector3 d, Vector3 mn, Vector3 mx, out float t0, out float t1)
        {
            t0 = 0f; t1 = 1f;
            for (int ax = 0; ax < 3; ax++)
            {
                float o = a[ax], v = d[ax];
                if (Mathf.Abs(v) < 1e-7f) { if (o < mn[ax] || o > mx[ax]) return false; continue; }
                float u0 = (mn[ax] - o) / v, u1 = (mx[ax] - o) / v;
                if (u0 > u1) { float s = u0; u0 = u1; u1 = s; }
                t0 = Mathf.Max(t0, u0); t1 = Mathf.Min(t1, u1);
                if (t0 > t1) return false;
            }
            return t1 - t0 > 1e-5f || (t0 > 0f && t0 < 1f);
        }

        static Mesh TubeMesh(Vector3[] pts, float radius, int sides)
        {
            int rings = pts.Length;
            var v = new Vector3[rings * (sides + 1)];
            var col = new Color[v.Length];
            var tris = new int[(rings - 1) * sides * 6];
            Vector3 prevN = Vector3.right;
            for (int i = 0; i < rings; i++)
            {
                Vector3 tan = (i < rings - 1 ? pts[i + 1] - pts[i] : pts[i] - pts[i - 1]).normalized;
                Vector3 n = Vector3.Cross(tan, Vector3.Cross(prevN, tan)).normalized;   // parallel transport: no twisting
                if (n.sqrMagnitude < 1e-6f) n = Vector3.Cross(tan, Vector3.up).normalized;
                prevN = n;
                Vector3 bi = Vector3.Cross(tan, n);
                for (int s = 0; s <= sides; s++)
                {
                    float a = s / (float)sides * Mathf.PI * 2f;
                    v[i * (sides + 1) + s] = pts[i] + (n * Mathf.Cos(a) + bi * Mathf.Sin(a)) * radius;
                    col[i * (sides + 1) + s] = Color.white;
                }
            }
            int t = 0;
            for (int i = 0; i < rings - 1; i++)
                for (int s = 0; s < sides; s++)
                {
                    int a0 = i * (sides + 1) + s, a1 = a0 + 1, b0 = a0 + sides + 1, b1 = b0 + 1;
                    tris[t++] = a0; tris[t++] = b0; tris[t++] = a1; tris[t++] = a1; tris[t++] = b0; tris[t++] = b1;
                }
            var m = new Mesh { name = "CardTube", vertices = v, colors = col, triangles = tris };
            m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }

        /// <summary>Send every tray card of this owner down its tube to its desk (staggered by <paramref name="stagger"/> s
        /// per card, after <paramref name="delay"/> s). Returns how many went.</summary>
        public int SendByTube(HabitatRole owner, float delay, float stagger = 0.25f)
        {
            int r = (int)owner;
            if (r < 0 || r >= 3) return 0;
            EnsureTubes();
            int moved = 0;
            foreach (var kv in _cardById)
            {
                CardFlight c = kv.Value;
                if (c.role != r || c.onDesk || c.hideOnLand) continue;
                int count;
                _deskStack.TryGetValue(r, out count);
                _deskStack[r] = count + 1;
                c.onDesk = true;
                if (!c.t.gameObject.activeSelf) c.t.gameObject.SetActive(true);
                Vector3[] route = _tubePts[r];
                c.b = route[0]; c.d = route[route.Length - 1];
                c.a = c.t.localPosition;
                c.slot = DeskSlot(owner, count);
                if (c.fullScale == Vector3.zero) c.fullScale = new Vector3(0.035f, 0.002f, 0.025f);   // the printed card's size
                c.tube = true; c.tubeStarted = false; c.hideOnLand = false;
                c.tubeT = -(delay + stagger * moved);
                moved++;
            }
            if (moved > 0) _trayStack[r] = 0;
            return moved;
        }

        // ── Papers going UP (owner, rc18 worn: "when the agents are bringing up their papers they should use the tubes too; the
        //    elevator should only be used for a question, otherwise it's slow"). A sheet lifts off the agent's desk, rolls up,
        //    rides its tube up to the Manager's tray and unrolls there; for an agent on another floor it goes on down that
        //    agent's tube (the Manager's printer is the hub, like a real pneumatic station). Presentation only: the event it
        //    shows already happened; nothing here changes task state. The sheet fades after it lands. ──

        sealed class TubeMail
        {
            public Transform t; public Renderer r;
            public int[] legs; public bool[] up;
            public Vector3 start, end; public float time; public bool spent;
            public HabitatRole to; public bool acked, landed;
        }
        readonly List<TubeMail> _mail = new List<TubeMail>();
        public const float MailHold = 1.4f, MailFade = 0.35f;
        /// <summary>For drivers: sheets sent up a tube so far, and in flight now.</summary>
        public int MailSent { get; private set; }
        public int MailInFlight => _mail.Count;

        /// <summary>Send a sheet from <paramref name="from"/>'s desk up its tube to the Manager (<paramref name="to"/> =
        /// Manager), or through the Manager's hub on down to another agent's desk. False when there is no tube for it.</summary>
        public bool SendUpTube(HabitatRole from, HabitatRole to, Color tint) { return SendUpTube(from, to, tint, null); }

        /// <summary>Same, with a small label (rc21) that rides along in front of the tube and waits at the destination for
        /// <see cref="LabelHold"/> s, e.g. "Researcher → Manager: Findings: login options". Null/empty: no label.</summary>
        public bool SendUpTube(HabitatRole from, HabitatRole to, Color tint, string label)
        {
            int f = (int)from, g = (int)to;
            if (f < 0 || f >= 3 || f == g) return false;              // the Manager has no tube of its own: its papers print
            EnsureTubes();
            if (_tubePts[f] == null) return false;
            bool relay = g >= 0 && g < 3;
            if (relay && _tubePts[g] == null) return false;
            var r = Box(root, "TubeMail_" + from + "_to_" + to, DeskSlot(from, CardsOnDesk(from)), new Vector3(0.035f, 0.002f, 0.025f), Color.Lerp(Paper, tint, 0.45f));
            var m = new TubeMail
            {
                t = r.transform, r = r,
                legs = relay ? new[] { f, g } : new[] { f },
                up = relay ? new[] { true, false } : new[] { true },
                start = r.transform.localPosition,
                end = relay ? DeskSlot(to, CardsOnDesk(to)) + Vector3.up * 0.003f : TraySlot(CardsInTrayTotal()) + Vector3.up * 0.003f,
                to = relay ? to : HabitatRole.Manager,
            };
            _mail.Add(m);
            MailSent++;
            if (!string.IsNullOrEmpty(label)) ShowTubeLabel(m, label, tint == Paper ? Teal : tint);
            return true;
        }

        int CardsInTrayTotal() { int n = 0; foreach (var kv in _trayStack) n += kv.Value; return n; }

        Vector3 LegEnd(int leg, bool up, bool atStart)
        {
            Vector3[] p = _tubePts[leg];
            // A tube runs tray (index 0) -> desk (last). Up = desk -> tray.
            bool first = up ? !atStart : atStart;
            return first ? p[0] : p[p.Length - 1];
        }

        void StepMail(TubeMail m, float dt)
        {
            if (m.t == null) { m.spent = true; return; }
            m.time += dt;
            float t = m.time;
            Vector3 full = new Vector3(0.035f, 0.002f, 0.025f), rolled = new Vector3(0.011f, 0.011f, 0.025f * 0.95f);
            if (t < TubeRise)
            {
                // Lifts off the desk into the outlet, rolling up.
                float k = Mathf.SmoothStep(0f, 1f, t / TubeRise);
                m.t.localPosition = Vector3.Lerp(m.start, LegEnd(m.legs[0], m.up[0], true), k);
                m.t.localScale = Vector3.Lerp(full, rolled, Mathf.Clamp01(k * 1.6f));
                m.t.localRotation = Quaternion.Slerp(Quaternion.identity, Quaternion.LookRotation(Vector3.up, Vector3.forward), k);
                return;
            }
            t -= TubeRise;
            for (int i = 0; i < m.legs.Length; i++)
            {
                if (t < TubeTravel)
                {
                    float u = t / TubeTravel;
                    float e = u < 0.5f ? 4f * u * u * u : 1f - Mathf.Pow(-2f * u + 2f, 3f) * 0.5f;
                    Vector3 dir;
                    Vector3 p = AlongTube(_tubePts[m.legs[i]], _tubeLen[m.legs[i]], m.up[i] ? 1f - e : e, out dir);
                    if (m.up[i]) dir = -dir;
                    m.t.localPosition = p;
                    m.t.localScale = rolled;
                    if (dir.sqrMagnitude > 1e-8f) m.t.localRotation = Quaternion.LookRotation(dir, Mathf.Abs(dir.y) > 0.95f ? Vector3.forward : Vector3.up);
                    return;
                }
                t -= TubeTravel;
            }
            int last = m.legs.Length - 1;
            Vector3 exit = LegEnd(m.legs[last], m.up[last], false);
            float q = Mathf.Clamp01(t / TubeDrop);
            if (!m.acked) { m.acked = true; Acknowledge(m.to, 0.9f); }       // rc21: whoever it is for nods as it pops out
            if (q < 1f)
            {
                // Pops out, unrolls and settles onto the tray (or the other desk).
                float drop = q * q * (3f - 2f * q);
                m.t.localPosition = Vector3.Lerp(exit, m.end, drop) + Vector3.up * (Mathf.Sin(q * Mathf.PI) * 0.01f);
                m.t.localScale = Vector3.Lerp(rolled, full, Mathf.Clamp01(q * 1.8f));
                m.t.localRotation = Quaternion.Slerp(m.t.localRotation, Quaternion.identity, Mathf.Clamp01(q * 2f));
                return;
            }
            t -= TubeDrop;
            m.landed = true;
            m.t.localPosition = m.end; m.t.localRotation = Quaternion.identity;
            if (t < MailHold) { m.t.localScale = full; return; }
            float f = Mathf.Clamp01((t - MailHold) / MailFade);
            m.t.localScale = Vector3.Lerp(full, new Vector3(full.x, full.y, full.z) * 0.2f, f);
            if (f >= 1f) { Object.Destroy(m.t.gameObject); m.spent = true; }
        }

        // ── Little labels (rc21; owner, worn rc20: "there should be little indicators of what the papers say as they are being
        //    pushed through the pipes, maybe small popups at the pipes"). A small dark rounded label in front of the habitat,
        //    tucked under the ceiling of the floor the sheet is on (above every head, never over a face), follows the sheet
        //    along its tube, waits at the destination for LabelHold s, then shrinks away. At most MaxTubeLabels at once (the
        //    oldest goes). The same label shows an agent's question while it walks out to you, and "Waiting for the lift"
        //    while a rider waits for the cabin. Text comes only from the real event (agent names, the task's own title). ──

        enum LabelKind { Tube = 0, Question = 1, Lift = 2 }

        sealed class Label
        {
            public LabelKind kind; public Transform t; public TMP_Text text;
            public TubeMail mail; public int role = -1, slot;
            public float age, held, fade = -1f, maxAge, width;
            public bool placed, leftHome; public Vector3 pos;
        }

        readonly List<Label> _labels = new List<Label>();
        /// <summary>Font and unlit plate material for the labels (set by the controller; null = Inter fallback / lit box).</summary>
        public TMP_FontAsset labelFont;
        public Material labelPlateMaterial;
        public const int MaxTubeLabels = 3;
        public const float LabelHold = 2f, LabelIn = 0.18f, LabelOut = 0.35f, LabelFontMm = 5.2f, LabelH = 0.019f, LabelMaxCm = 15f;
        /// <summary>For drivers: labels created so far, alive now (tube ones), and the last label's text.</summary>
        public int LabelsShown { get; private set; }
        public int TubeLabelsLive { get { int n = 0; for (int i = 0; i < _labels.Count; i++) if (_labels[i].kind == LabelKind.Tube) n++; return n; } }
        public int LabelsLive => _labels.Count;
        public string LastLabel { get; private set; }

        float RootScale => Mathf.Max(1e-4f, root.lossyScale.y);

        Label MakeLabel(LabelKind kind, string name, string text, Color accent)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root, false);
            go.transform.localScale = Vector3.zero;                         // grows in on the first step
            var l = new Label { kind = kind, t = go.transform };
            TMP_Text tx = WallUi.MakeText(go.transform, "Text", labelFont, new Vector3(0.002f, 0f, -0.0016f), new Vector2(LabelMaxCm, 2f), LabelFontMm, Paper, TextAlignmentOptions.Center);
            tx.textWrappingMode = TextWrappingModes.NoWrap;
            tx.overflowMode = TextOverflowModes.Ellipsis;
            if (text.IndexOf('→') >= 0 && (tx.font == null || !tx.font.HasCharacter('→', true, true))) text = text.Replace("→", "->");
            tx.text = text;
            float wCm = Mathf.Clamp(tx.GetPreferredValues(text).x + 0.2f, 3f, LabelMaxCm);
            tx.rectTransform.sizeDelta = new Vector2(wCm, 2f);
            l.text = tx;
            l.width = wCm * 0.01f + 0.014f;
            WallUi.Plate(go.transform, "Plate", Vector3.zero, l.width, LabelH, LabelH * 0.45f, Ink, labelPlateMaterial, this);
            // A small accent dot on the left in the paper's colour (teal result, coral problem, lavender question).
            WallUi.Plate(go.transform, "Accent", new Vector3(-l.width * 0.5f + 0.0055f, 0f, -0.0012f), 0.0045f, 0.0045f, 0.00225f, accent, labelPlateMaterial, this);
            _labels.Add(l);
            LabelsShown++;
            LastLabel = text;
            return l;
        }

        void KillLabel(int i)
        {
            if (_labels[i].t != null) Object.Destroy(_labels[i].t.gameObject);
            _labels.RemoveAt(i);
        }

        void ShowTubeLabel(TubeMail m, string text, Color accent)
        {
            // Never more than MaxTubeLabels: the oldest tube label goes at once (labels are oldest first in the list).
            int live = TubeLabelsLive;
            for (int i = 0; i < _labels.Count && live >= MaxTubeLabels; i++)
                if (_labels[i].kind == LabelKind.Tube) { KillLabel(i); i--; live--; }
            var used = new bool[MaxTubeLabels];
            for (int i = 0; i < _labels.Count; i++) if (_labels[i].kind == LabelKind.Tube && _labels[i].slot < MaxTubeLabels) used[_labels[i].slot] = true;
            int slot = 0;
            while (slot < MaxTubeLabels - 1 && used[slot]) slot++;
            Label l = MakeLabel(LabelKind.Tube, "TubeLabel_" + m.t.name, text, accent);
            l.mail = m; l.slot = slot; l.maxAge = 20f;
        }

        /// <summary>A label over an agent's head while it walks out with a question for the user; it goes once the agent is
        /// back at its desk (or after <paramref name="maxSeconds"/>). One per agent.</summary>
        public void ShowQuestionLabel(HabitatRole role, string text, float maxSeconds = 30f)
        {
            int r = (int)role;
            if (r < 0 || r >= 4 || string.IsNullOrEmpty(text)) return;
            for (int i = _labels.Count - 1; i >= 0; i--) if (_labels[i].kind == LabelKind.Question && _labels[i].role == r) KillLabel(i);
            Label l = MakeLabel(LabelKind.Question, "QuestionLabel_" + role, text, Lavender);
            l.role = r; l.maxAge = maxSeconds;
        }

        /// <summary>"Waiting for the lift" over a rider that has been waiting for the cabin for a moment (rc21: a wait has a
        /// visible reason instead of an agent standing still at the doors).</summary>
        void TickLiftCue(int r, HabitatWalker w)
        {
            if (w.State != WalkerState.WaitingElevator || w.WaitSeconds < 0.6f) return;
            for (int i = 0; i < _labels.Count; i++) if (_labels[i].kind == LabelKind.Lift && _labels[i].role == r) return;
            Label l = MakeLabel(LabelKind.Lift, "LiftLabel_" + (HabitatRole)r, "Waiting for the lift", Brass);
            l.role = r; l.slot = 1; l.maxAge = 600f;
        }

        /// <summary>The floor whose storey contains habitat-local height <paramref name="y"/>.</summary>
        int FloorAt(float y)
        {
            int f = 0;
            for (int i = 0; i < layout.floors.Count; i++) if (y >= layout.floors[i].floorY - 0.01f) f = i;
            return f;
        }

        /// <summary>Where a tube label sits for a sheet at <paramref name="at"/>: same x, in front of the habitat, just under
        /// the ceiling of that floor (the lower bays' own ceiling is 2.1 cm lower, as the tube collars found), stacked down by
        /// slot but never lower than a head plus a margin.</summary>
        Vector3 TubeLabelSpot(Vector3 at, Label l)
        {
            float rs = RootScale, half = LabelH * 0.5f / rs, hw = l.width * 0.5f / rs;
            HabitatFloor fl = layout.floors[FloorAt(at.y)];
            float top = fl.ceilingY - 0.006f - half - (fl.index == 0 ? 0.021f : 0f);
            float y = Mathf.Max(top - l.slot * (2f * half + 0.004f / rs), fl.floorY + layout.agentHeight + 0.012f + half);
            float x = Mathf.Clamp(at.x, -layout.size.x * 0.5f + hw, layout.size.x * 0.5f - hw);
            return new Vector3(x, y, layout.size.z + 0.008f + 0.002f * l.slot);
        }

        void StepLabels(HabitatStage stage, float dt)
        {
            if (_labels.Count == 0) return;
            Camera cam = Camera.main;
            float rs = RootScale, half = LabelH * 0.5f / rs;
            for (int i = _labels.Count - 1; i >= 0; i--)
            {
                Label l = _labels[i];
                if (l.t == null) { _labels.RemoveAt(i); continue; }
                l.age += dt;
                Vector3 target;
                bool keep;
                if (l.kind == LabelKind.Tube)
                {
                    TubeMail m = l.mail;
                    bool flying = m != null && !m.spent && !m.landed && m.t != null;
                    if (!flying) l.held += dt;
                    target = TubeLabelSpot(flying ? m.t.localPosition : (m != null ? m.end : l.pos), l);
                    keep = l.held < LabelHold;
                }
                else
                {
                    HabitatWalker w = stage != null ? stage.Walker((HabitatRole)l.role) : null;
                    Transform a = _avatar[l.role];
                    Vector3 feet = a != null ? root.InverseTransformPoint(a.position) : (w != null ? w.Position : Vector3.zero);
                    target = feet + new Vector3(0f, layout.agentHeight + 0.014f + half + l.slot * (2f * half + 0.003f / rs), 0.01f);
                    if (l.kind == LabelKind.Lift) keep = w != null && w.State == WalkerState.WaitingElevator;
                    else
                    {
                        if (w != null && !w.AtHome) l.leftHome = true;
                        keep = w != null && !(l.leftHome && w.AtHome) && !(!l.leftHome && l.age > 12f);
                    }
                }
                if (l.age > l.maxAge) keep = false;
                if (!keep && l.fade < 0f) l.fade = 0f;

                l.pos = l.placed ? Vector3.Lerp(l.pos, target, 1f - Mathf.Exp(-dt * 12f)) : target;
                l.placed = true;
                l.t.localPosition = l.pos;
                if (cam != null) l.t.rotation = WallUi.FacingEye(l.t.position, cam.transform.position);
                else l.t.localRotation = Quaternion.Euler(0f, 180f, 0f);   // TMP reads from local -Z; the user is at +Z

                float s, alpha;
                if (l.fade >= 0f)
                {
                    l.fade += dt;
                    float k = Mathf.Clamp01(l.fade / LabelOut);
                    if (k >= 1f) { KillLabel(i); continue; }
                    s = 1f - k * k; alpha = 1f - k;
                }
                else
                {
                    float k = Mathf.Clamp01(l.age / LabelIn), e = 1f - (1f - k) * (1f - k) * (1f - k);
                    s = 0.6f + 0.4f * e; alpha = e;
                }
                l.t.localScale = Vector3.one * (s / rs);                    // absolute size whatever the habitat's scale
                if (l.text != null) l.text.alpha = alpha;
            }
        }

        void StepAllMail(float dt)
        {
            for (int i = _mail.Count - 1; i >= 0; i--)
            {
                StepMail(_mail[i], dt);
                if (_mail[i].spent) _mail.RemoveAt(i);
            }
        }

        void StepTube(CardFlight c)
        {
            Vector3 rolled = new Vector3(0.011f, 0.011f, c.fullScale.z * 0.95f);
            float t = c.tubeT;
            if (t < TubeRise)
            {
                // Up from the tray into the inlet, rolling up as it goes.
                float k = Mathf.SmoothStep(0f, 1f, t / TubeRise);
                c.t.localPosition = Vector3.Lerp(c.a, c.b, k);
                c.t.localScale = Vector3.Lerp(c.fullScale, rolled, Mathf.Clamp01(k * 1.6f));
                c.t.localRotation = Quaternion.Slerp(Quaternion.identity, Quaternion.LookRotation(Vector3.up, Vector3.forward), k);
                return;
            }
            t -= TubeRise;
            if (t < TubeTravel)
            {
                // Through the tube: quick in the middle (InOutCubic), the rolled card pointing along the glass.
                float u = t / TubeTravel;
                float e = u < 0.5f ? 4f * u * u * u : 1f - Mathf.Pow(-2f * u + 2f, 3f) * 0.5f;
                Vector3 dir;
                Vector3 p = AlongTube(_tubePts[c.role], _tubeLen[c.role], e, out dir);
                c.t.localPosition = p;
                c.t.localScale = rolled;
                if (dir.sqrMagnitude > 1e-8f) c.t.localRotation = Quaternion.LookRotation(dir, Mathf.Abs(dir.y) > 0.95f ? Vector3.forward : Vector3.up);
                return;
            }
            t -= TubeTravel;
            float q = Mathf.Clamp01(t / TubeDrop);
            // Pops out of the outlet, unrolls and drops onto the stack with a little settle.
            float drop = q * q * (3f - 2f * q);
            Vector3 pos = Vector3.Lerp(c.d, c.slot, drop) + Vector3.up * (Mathf.Sin(q * Mathf.PI) * 0.012f);
            c.t.localPosition = pos;
            c.t.localScale = Vector3.Lerp(rolled, c.fullScale, Mathf.Clamp01(q * 1.8f));
            c.t.localRotation = Quaternion.Slerp(c.t.localRotation, Quaternion.identity, Mathf.Clamp01(q * 2f));
            if (q >= 1f)
            {
                c.t.localPosition = c.slot; c.t.localScale = c.fullScale; c.t.localRotation = Quaternion.identity;
                c.landed = true; c.tube = false;
            }
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
            for (int i = 0; i < _mail.Count; i++) if (_mail[i].t != null) Object.Destroy(_mail[i].t.gameObject);
            _mail.Clear();
            for (int i = _labels.Count - 1; i >= 0; i--)
                if (_labels[i].kind == LabelKind.Tube) { if (_labels[i].t != null) Object.Destroy(_labels[i].t.gameObject); _labels.RemoveAt(i); }
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
