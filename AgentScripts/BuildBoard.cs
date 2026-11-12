using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditorInternal;
using UnityEngine;
using TMPro;
using MuseDesk.Phase2;
using MuseDesk.Phase2.Board;
using MuseDesk.Phase2.Contracts;
using MuseDesk.Workspace;

/// <summary>
/// Editor builder that makes the Phase 2 task board VISIBLE and usable in the live scene. Run
/// <see cref="Run"/> from the Editor (it needs the real Unity + ISDK assemblies — it cannot run in a
/// bare worktree). It is ADDITIVE and IDEMPOTENT: re-running tears down the previous TaskBoard, its
/// card template, the station drop zones and the bootstrap, then rebuilds them.
///
/// What it constructs:
///   • A <b>TaskBoard</b> root under WorkspaceRoot (small upright cream panel + 4 region anchors:
///     Backlog / Working / Needs me / Completed) carrying a <see cref="BoardPresenter"/>.
///   • A <b>CardTemplate</b> (inactive) the presenter instantiates/pools: card quad + TMP title +
///     <see cref="TaskCardView"/> + <see cref="TaskDragController"/> + kinematic Rigidbody + trigger
///     BoxCollider, with the proven cube's Grabbable + HandGrabInteractable COPIED on (repointed to
///     the card), plus <see cref="ReRegisterInteractable"/> so each runtime clone re-registers with ISDK.
///   • A <see cref="StationDropZone"/> (trigger sphere) under each of the three agents.
///   • A <see cref="Phase2Bootstrap"/> on AppServices, wired + enableBoard = true.
///
/// ISDK types (Oculus.Interaction.*) are resolved by REFLECTION only (AgentScripts must not reference
/// them by name), exactly like FixPass3.PropGrab / AddGrabbablePaper. Phase 2 C# types live in the same
/// Assembly-CSharp this editor assembly already references, so they are used directly.
/// </summary>
public static class BuildBoard
{
    // ── board placement (WORLD space; assumes WorkspaceRoot is near identity at author time) ──
    // VERIFY: world position of the panel. If WorkspaceRoot is transformed at author time, adjust or
    // set this relative to the tray in the Editor.
    static readonly Vector3 BoardWorldPos = new Vector3(-0.2f, 1.05f, 0.4f);

    // Region anchor local X positions across the panel (4 columns), with labels above.
    static readonly (string key, string label, float x)[] Regions =
    {
        ("Backlog",  "Backlog",  -0.24f),
        ("Working",  "Working",  -0.08f),
        ("NeedsMe",  "Needs me",  0.08f),
        ("Completed","Completed", 0.24f),
    };

    static readonly (string agentGO, string agentId)[] Stations =
    {
        ("Agent_Researcher", Phase2AgentIds.Researcher),
        ("Agent_Developer",  Phase2AgentIds.Developer),
        ("Agent_Tester",     Phase2AgentIds.Tester),
    };

    static GameObject[] _all;

    public static string Run()
    {
        var scene = EditorSceneManager.GetActiveScene();
        _all = scene.GetRootGameObjects()
            .SelectMany(r => r.GetComponentsInChildren<Transform>(true))
            .Select(t => t.gameObject).ToArray();
        var sb = new StringBuilder();

        try
        {
            var ws = Find("WorkspaceRoot");
            if (ws == null) return "ERROR: WorkspaceRoot not found (run the scene builders first).";

            // ── source grab rig (proven cube) ──
            var cube = Find("[BuildingBlock] Grabbable Item");
            if (cube == null) return "ERROR: '[BuildingBlock] Grabbable Item' grab source not found.";
            var srcGrab = CompByName(cube, "Grabbable");
            var srcHgi = CompByName(cube, "HandGrabInteractable");
            if (srcGrab == null || srcHgi == null)
                return "ERROR: cube is missing Grabbable/HandGrabInteractable to copy from.";

            // ── idempotency: remove any prior board + drop zones ──
            var oldBoard = FindChild(ws.transform, "TaskBoard");
            if (oldBoard != null) UnityEngine.Object.DestroyImmediate(oldBoard.gameObject);
            foreach (var (agentGO, _) in Stations)
            {
                var a = Find(agentGO);
                if (a == null) continue;
                var oldZone = a.transform.Cast<Transform>().FirstOrDefault(t => t.name == "TaskDropZone");
                if (oldZone != null) UnityEngine.Object.DestroyImmediate(oldZone.gameObject);
            }

            var panelMat = LitMat(new Color(0.94f, 0.91f, 0.82f));   // cream panel
            var cardMat = LitMat(new Color(0.98f, 0.97f, 0.93f));   // near-white card face

            // ── TaskBoard root + backing panel ──
            var board = new GameObject("TaskBoard");
            board.transform.SetParent(ws.transform, worldPositionStays: false);
            board.transform.position = BoardWorldPos;            // VERIFY: see BoardWorldPos note
            board.transform.localRotation = Quaternion.identity; // VERIFY: rotate 180° on Y if the panel faces away from the user

            var backing = GameObject.CreatePrimitive(PrimitiveType.Quad);
            backing.name = "Backing";
            backing.transform.SetParent(board.transform, false);
            backing.transform.localPosition = new Vector3(0f, 0f, 0.012f); // behind the cards (readable face is -Z)
            backing.transform.localScale = new Vector3(0.62f, 0.42f, 1f);
            StripCollider(backing);
            backing.GetComponent<Renderer>().sharedMaterial = panelMat;

            // Title label
            MakeLabel(board.transform, "Title", "TASK BOARD", new Vector3(0f, 0.19f, -0.013f), 0.5f);

            // ── region anchors + labels ──
            var regionT = new Dictionary<string, Transform>();
            foreach (var (key, label, x) in Regions)
            {
                var r = new GameObject("Region_" + key);
                r.transform.SetParent(board.transform, false);
                r.transform.localPosition = new Vector3(x, 0.10f, -0.012f); // cards grow +X / -Y from here
                r.transform.localRotation = Quaternion.identity;
                regionT[key] = r.transform;
                MakeLabel(board.transform, "Label_" + key, label, new Vector3(x, 0.16f, -0.013f), 0.35f);
            }

            // ── BoardPresenter on the TaskBoard root ──
            var presenter = board.AddComponent<BoardPresenter>();
            presenter.backlogRegion = regionT["Backlog"];
            presenter.workingRegion = regionT["Working"];
            presenter.needsMeRegion = regionT["NeedsMe"];
            presenter.completedRegion = regionT["Completed"];
            presenter.columns = 1;                                // one narrow column per region
            presenter.cardSpacing = new Vector2(0.09f, 0.06f);

            // ── card template (inactive; the presenter Instantiates/pools from it) ──
            var template = BuildCardTemplate(board.transform, cardMat, srcGrab, srcHgi, sb);
            presenter.cardPrefab = template.GetComponent<TaskCardView>();

            EditorUtility.SetDirty(presenter);

            // ── station drop zones under each agent ──
            var zones = new List<StationDropZone>();
            foreach (var (agentGO, agentId) in Stations)
            {
                var a = Find(agentGO);
                if (a == null) { sb.AppendLine($"  WARN: agent '{agentGO}' not found; drop zone skipped"); continue; }
                var zoneGO = new GameObject("TaskDropZone");
                zoneGO.transform.SetParent(a.transform, false);
                // VERIFY: local offset — placed slightly above/in front of the agent so a dropped card
                // lands on the station. Tune per the real agent pivot.
                zoneGO.transform.localPosition = new Vector3(0f, 0.18f, 0.08f);
                var sc = zoneGO.AddComponent<SphereCollider>();  // add collider BEFORE StationDropZone (RequireComponent)
                sc.isTrigger = true;
                sc.radius = 0.12f;
                var zone = zoneGO.AddComponent<StationDropZone>();
                zone.agentId = agentId;
                zone.dropAnchor = zoneGO.transform;
                zones.Add(zone);
                EditorUtility.SetDirty(zone);
                sb.AppendLine($"  drop zone -> {agentGO} ({agentId})");
            }

            // ── Phase2Bootstrap on AppServices ──
            var services = Find("AppServices");
            if (services == null)
            {
                services = new GameObject("Phase2Services");
                sb.AppendLine("  AppServices not found; created 'Phase2Services' host GO");
            }
            var boot = services.GetComponent<Phase2Bootstrap>() ?? services.AddComponent<Phase2Bootstrap>();
            boot.enableBoard = true;
            boot.boardPresenter = presenter;
            boot.dropZones = zones.ToArray();
            EditorUtility.SetDirty(boot);

            sb.AppendLine($"TaskBoard built under WorkspaceRoot at {BoardWorldPos}; " +
                          $"regions={Regions.Length}, drop zones={zones.Count}.");

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            sb.AppendLine("SAVED scene + assets.");
            return sb.ToString();
        }
        catch (Exception e)
        {
            return sb.ToString() + $"\nERROR: {e.GetType().Name}: {e.Message}\n{e.StackTrace}";
        }
    }

    // ─────────────────────────── card template ───────────────────────────

    static GameObject BuildCardTemplate(Transform boardRoot, Material cardMat,
        Component srcGrab, Component srcHgi, StringBuilder sb)
    {
        // Root holds the interaction + view components; visuals are children.
        var card = new GameObject("CardTemplate");
        card.transform.SetParent(boardRoot, false);
        card.transform.localPosition = new Vector3(0f, -0.3f, 0f); // parked off the panel; it stays inactive anyway

        // ── cute layering (readable face is -Z; "behind" the reader is +Z) ──
        // 1) Soft drop-shadow / border backing: a slightly larger dark quad just BEHIND the face,
        //    exactly like AgentScripts/PaperLook.cs does for the decision paper.
        var shadow = GameObject.CreatePrimitive(PrimitiveType.Quad);
        shadow.name = "Shadow";
        shadow.transform.SetParent(card.transform, false);
        shadow.transform.localPosition = new Vector3(0.0015f, -0.0015f, 0.004f); // offset down-right + behind
        shadow.transform.localScale = new Vector3(0.090f, 0.058f, 1f);           // ~12% larger than the face
        StripCollider(shadow);
        shadow.GetComponent<Renderer>().sharedMaterial = LitMat(new Color(0.16f, 0.15f, 0.14f));

        // 2) Cream card face (~0.08 x 0.05) with a touch of emission so it glows gently in passthrough.
        var face = GameObject.CreatePrimitive(PrimitiveType.Quad);
        face.name = "Face";
        face.transform.SetParent(card.transform, false);
        face.transform.localScale = new Vector3(0.08f, 0.05f, 1f);
        StripCollider(face);
        // VERIFY: true rounded corners need a rounded sprite / 9-slice; the soft shadow + warm cream
        // reads as a friendly card with plain quads. Swap the face mesh for a rounded sprite to curve it.
        face.GetComponent<Renderer>().sharedMaterial = LitMat(new Color(0.98f, 0.97f, 0.93f), 0.10f);

        // 3) Role-colored header strip across the top of the face (tinted per card by CardRoleTint).
        var header = GameObject.CreatePrimitive(PrimitiveType.Quad);
        header.name = "HeaderStrip";
        header.transform.SetParent(card.transform, false);
        header.transform.localPosition = new Vector3(0f, 0.018f, -0.002f);   // top band, in front of the face
        header.transform.localScale = new Vector3(0.08f, 0.012f, 1f);
        StripCollider(header);
        var headerMat = LitMat(new Color(0.93f, 0.88f, 0.78f), 0.25f);        // neutral cream = unassigned default
        header.GetComponent<Renderer>().sharedMaterial = headerMat;

        // Bold, readable title on the face (below the header strip).
        var title = MakeLabel(card.transform, "Title", "Task", new Vector3(0f, -0.004f, -0.003f), 0.24f);
        title.rectTransform.sizeDelta = new Vector2(0.075f, 0.040f);
        title.fontStyle = FontStyles.Bold;

        // kinematic body (card never falls; moved by the hand grab).
        var rb = card.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;
        rb.interpolation = RigidbodyInterpolation.None;

        // trigger grab volume sized to the card.
        var bc = card.AddComponent<BoxCollider>();
        bc.isTrigger = true;
        bc.center = Vector3.zero;
        bc.size = new Vector3(0.08f, 0.05f, 0.02f);

        // Phase 2 view + drag state machine.
        var view = card.AddComponent<TaskCardView>();
        view.titleLabel = title;
        card.AddComponent<TaskDragController>();

        // ── copy the proven grab components and repoint to this card (mirror FixPass3.PropGrab) ──
        ComponentUtility.CopyComponent(srcGrab);
        ComponentUtility.PasteComponentAsNew(card);
        ComponentUtility.CopyComponent(srcHgi);
        ComponentUtility.PasteComponentAsNew(card);

        var newGrab = card.GetComponents<Component>().Last(c => c != null && c.GetType().Name == "Grabbable");
        var newHgi = card.GetComponents<Component>().Last(c => c != null && c.GetType().Name == "HandGrabInteractable");

        Repoint(newGrab, "_rigidbody", rb, sb);
        Repoint(newHgi, "_pointableElement", newGrab, sb);        // base-class field on PointerInteractable
        Repoint(newHgi, "<PointableElement>k__BackingField", null, sb);
        Repoint(newHgi, "_rigidbody", rb, sb);

        // TaskCardView reads grab state by reflection off this Grabbable (SelectingPointsCount).
        view.grabbable = newGrab;
        EditorUtility.SetDirty(view);

        // Cute per-card role tint: colors the header strip blue/green/amber by the card's assigned
        // agent (cream while unassigned). Clones inherit this (header is in the same hierarchy).
        var roleTint = card.AddComponent<CardRoleTint>();
        roleTint.headerStrip = header.GetComponent<Renderer>();
        EditorUtility.SetDirty(roleTint);

        // Re-register ISDK interactables after each runtime activation. On a clone, Unity remaps these
        // references (same hierarchy) to the clone's own components, so every pooled card re-registers.
        var rereg = card.AddComponent<ReRegisterInteractable>();
        rereg.interactables = new Behaviour[] { newGrab as Behaviour, newHgi as Behaviour };
        EditorUtility.SetDirty(rereg);

        sb.AppendLine($"  CardTemplate built (grab={newGrab != null} hgi={newHgi != null} " +
                      $"reReg={(newGrab is Behaviour && newHgi is Behaviour)})");

        card.SetActive(false);   // template stays hidden; presenter clones + activates
        return card;
    }

    // ─────────────────────────── helpers ───────────────────────────

    static GameObject Find(string name) => _all.FirstOrDefault(g => g != null && g.name == name);

    static Transform FindChild(Transform parent, string name)
        => parent.Cast<Transform>().FirstOrDefault(t => t.name == name);

    static Component CompByName(GameObject go, string simpleTypeName)
        => go.GetComponents<Component>().FirstOrDefault(c => c != null && c.GetType().Name == simpleTypeName);

    static void StripCollider(GameObject go)
    {
        var c = go.GetComponent<Collider>();
        if (c != null) UnityEngine.Object.DestroyImmediate(c);
    }

    static Material LitMat(Color c, float emission = 0f)
    {
        var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        var m = new Material(shader);
        m.color = c;
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        // A touch of emission keeps the card readable in passthrough / low light.
        if (emission > 0f && m.HasProperty("_EmissionColor"))
        {
            m.EnableKeyword("_EMISSION");
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            m.SetColor("_EmissionColor", c * emission);
        }
        return m;
    }

    static TextMeshPro MakeLabel(Transform parent, string name, string text, Vector3 localPos, float fontSize)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localRotation = Quaternion.identity;
        var tmp = go.AddComponent<TextMeshPro>();   // 3D (MeshRenderer) TMP; derives TMP_Text
        tmp.text = text;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.enableAutoSizing = false;
        tmp.fontSize = fontSize;                    // VERIFY: world-space TMP font size — tune for legibility
        tmp.color = new Color(0.12f, 0.11f, 0.10f);
        tmp.rectTransform.sizeDelta = new Vector2(0.3f, 0.1f);
        return tmp;
    }

    // Set a field that may be declared on a base class (walks the hierarchy), like AddGrabbablePaper.
    static void Repoint(Component c, string field, object value, StringBuilder sb)
    {
        if (c == null) { sb.AppendLine($"  (null component; skipped {field})"); return; }
        for (var t = c.GetType(); t != null; t = t.BaseType)
        {
            var f = t.GetField(field, BindingFlags.Instance | BindingFlags.Public |
                                      BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (f != null) { f.SetValue(c, value); return; }
        }
        sb.AppendLine($"  WARN: {c.GetType().Name}.{field} not found");
    }
}
