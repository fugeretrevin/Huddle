using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using TMPro;
using MuseDesk.Phase2;
using MuseDesk.Phase2.Board;
using MuseDesk.Workspace;
using MuseDesk.Demo;

/// <summary>
/// Closes the remaining Phase 2 SCENE HOOKS (the only thing BoardPresenter/Phase2Bootstrap/
/// ArtifactViewer were waiting on). Idempotent + additive; operates on the CURRENTLY saved
/// MainMRScene so it does NOT disturb any on-head-tuned board placement (unlike re-running
/// BuildBoard, which resets the board to its authored world pose).
///
/// What it wires:
///   1. CARD DATA LABELS on the pooled CardTemplate — role / priority / state / id·rev and the
///      MANDATORY source label ("LIVE" / "SCRIPTED DEMO", invariant #1) plus a state-tinted status
///      dot. Only the title was wired before, so cards shipped without the required mode label.
///   2. CARD ACTION POKES on the CardTemplate — pause/resume + cycle-priority poke buttons, cloned
///      from the proven "[BuildingBlock] ISDK_PokeInteraction" and bound by reflection to the
///      template's OWN TaskCardView (persistent UnityEvent target remaps to each pooled clone). Their
///      PokeInteractable is appended to the card's ReRegisterInteractable so they re-register on each
///      pooled activation (same fix the grab path needed). NEEDS ON-HEAD VERIFICATION (poke/grab
///      coexistence on a tiny grabbable card) — trivially removable if it misbehaves; drag-to-station
///      still covers assign regardless.
///   3. "＋ New task" poke button near the board -> Phase2Bootstrap.CreateSampleTask.
///   4. ArtifactViewer PANEL — a FaceUser panel with root/title/body/page TMP targets and
///      Next / Prev / Close poke buttons -> ArtifactViewer.NextPage/PrevPage/Close. Auto-opens on a
///      real artifact.created from the backend.
///
/// ISDK types are touched by reflection only (this editor assembly must not reference Oculus.*),
/// mirroring BuildPhase6 / BuildBoard.
/// </summary>
public static class BuildSceneHooks
{
    static GameObject[] _all;

    public static string Run()
    {
        var scene = EditorSceneManager.OpenScene("Assets/MuseDesk/Scenes/MainMRScene.unity", OpenSceneMode.Single);
        var sb = new StringBuilder();
        try
        {
            _all = scene.GetRootGameObjects()
                .SelectMany(r => r.GetComponentsInChildren<Transform>(true))
                .Select(t => t.gameObject).ToArray();

            var ws = Find("WorkspaceRoot");
            if (ws == null) return "ERROR: WorkspaceRoot not found.";

            var boot = UnityEngine.Object.FindAnyObjectByType<Phase2Bootstrap>();
            if (boot == null) return "ERROR: Phase2Bootstrap not found (run BuildBoard first).";
            var presenter = boot.boardPresenter;
            if (presenter == null) return "ERROR: Phase2Bootstrap.boardPresenter not assigned (run BuildBoard first).";
            var template = presenter.cardPrefab != null ? presenter.cardPrefab.gameObject : null;
            if (template == null) return "ERROR: BoardPresenter.cardPrefab (CardTemplate) missing (run BuildBoard first).";
            var board = presenter.gameObject;

            // Poke template present in the scene?
            if (FindPokeTemplate(scene) == null)
                sb.AppendLine("WARN: '[BuildingBlock] ISDK_PokeInteraction' template not found — poke buttons will be skipped.");

            // ── 1 + 2: augment the card template ──
            AugmentCardTemplate(scene, template, sb);

            // ── 3: + New task button ──
            BuildNewTaskButton(scene, board.transform, boot, sb);

            // ── 4: artifact viewer panel ──
            BuildArtifactPanel(scene, ws.transform, board.transform, boot, sb);

            // ── 5: offline demo auto-director (attended-judge defaults: self-starts the scripted
            //     flow, leaves the decision for the human; inert in live mode) ──
            var services = Find("AppServices");
            if (services != null)
            {
                if (services.GetComponent<DemoDirector>() == null) services.AddComponent<DemoDirector>();
                EditorUtility.SetDirty(services);
                sb.AppendLine("  DemoDirector on AppServices (defaults: human-in-the-loop, no auto-approve).");
            }
            else sb.AppendLine("  WARN: AppServices not found — DemoDirector not added.");

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

    // ─────────────────────────── 1 + 2: card template ───────────────────────────

    static void AugmentCardTemplate(UnityEngine.SceneManagement.Scene scene, GameObject card, StringBuilder sb)
    {
        var view = card.GetComponent<TaskCardView>();
        if (view == null) { sb.AppendLine("  card: no TaskCardView — skipped"); return; }

        // Idempotency: clear anything we previously added.
        foreach (var n in new[] { "SH_Role", "SH_Priority", "SH_State", "SH_Source", "SH_IdRev",
                                   "SH_StatusDot", "SH_PokePauseResume", "SH_PokePriority" })
        {
            var old = card.transform.Find(n);
            if (old != null) UnityEngine.Object.DestroyImmediate(old.gameObject);
        }

        const float zf = -0.004f; // just in front of the readable (−Z) face
        // id·rev (tiny, top), role (left) / priority (right), state (mid), SOURCE (bottom, bold).
        var idRev = MakeLabel(card.transform, "SH_IdRev", "t · r0", new Vector3(0f, 0.0225f, zf), 0.10f,
                              new Color(0.35f, 0.34f, 0.32f), false);
        var role = MakeLabel(card.transform, "SH_Role", "Unassigned", new Vector3(-0.024f, 0.0125f, zf), 0.12f,
                             new Color(0.12f, 0.11f, 0.10f), false);
        role.alignment = TextAlignmentOptions.Left;
        var prio = MakeLabel(card.transform, "SH_Priority", "P1", new Vector3(0.030f, 0.0125f, zf), 0.12f,
                             new Color(0.12f, 0.11f, 0.10f), true);
        prio.alignment = TextAlignmentOptions.Right;
        var state = MakeLabel(card.transform, "SH_State", "Queued", new Vector3(0f, -0.011f, zf), 0.12f,
                              new Color(0.18f, 0.17f, 0.16f), false);
        var source = MakeLabel(card.transform, "SH_Source", "SCRIPTED DEMO", new Vector3(0f, -0.0215f, zf), 0.13f,
                               new Color(0.85f, 0.30f, 0.20f), true);

        // State-tinted status dot (top-left corner).
        var dot = GameObject.CreatePrimitive(PrimitiveType.Quad);
        dot.name = "SH_StatusDot";
        dot.transform.SetParent(card.transform, false);
        dot.transform.localPosition = new Vector3(-0.036f, 0.0225f, zf);
        dot.transform.localScale = new Vector3(0.006f, 0.006f, 1f);
        StripCollider(dot);
        dot.GetComponent<Renderer>().sharedMaterial = LitMat(new Color(0.75f, 0.75f, 0.78f), 0.2f);

        // Assign the TaskCardView fields (Render() will drive them from reducer data).
        view.idRevisionLabel = idRev;
        view.roleLabel = role;
        view.priorityLabel = prio;
        view.stateLabel = state;
        view.sourceLabel = source;
        view.statusMarker = dot.GetComponent<Renderer>();
        EditorUtility.SetDirty(view);
        sb.AppendLine("  card labels wired (role/priority/state/id·rev/SOURCE + status dot).");

        // ── action pokes bound to the template's own TaskCardView (remaps per pooled clone) ──
        var pokeInteractables = new List<Behaviour>();
        var pause = ClonePoke(scene, card.transform, "SH_PokePauseResume", "Pause",
                              new Vector3(0.066f, 0.010f, zf), 0.35f);
        WirePoke(pause, view, "PokePauseResume", sb, pokeInteractables);
        var prioBtn = ClonePoke(scene, card.transform, "SH_PokePriority", "Prio",
                                new Vector3(0.066f, -0.010f, zf), 0.35f);
        WirePoke(prioBtn, view, "PokeCyclePriority", sb, pokeInteractables);

        // Make the pooled clone re-register the poke interactables on activation (inactive-at-init
        // ISDK interactables otherwise silently fail to register — same lesson as the grab path).
        if (pokeInteractables.Count > 0)
        {
            var rereg = card.GetComponent<ReRegisterInteractable>();
            if (rereg == null) rereg = card.AddComponent<ReRegisterInteractable>();
            var merged = new List<Behaviour>();
            if (rereg.interactables != null) merged.AddRange(rereg.interactables.Where(b => b != null));
            foreach (var b in pokeInteractables) if (b != null && !merged.Contains(b)) merged.Add(b);
            rereg.interactables = merged.ToArray();
            EditorUtility.SetDirty(rereg);
            sb.AppendLine($"  card pokes re-register wired ({pokeInteractables.Count} poke interactable(s)).");
        }
    }

    // ─────────────────────────── 3: + New task ───────────────────────────

    static void BuildNewTaskButton(UnityEngine.SceneManagement.Scene scene, Transform board,
                                   Phase2Bootstrap boot, StringBuilder sb)
    {
        var old = board.Find("NewTaskButton");
        if (old != null) UnityEngine.Object.DestroyImmediate(old.gameObject);

        // Below the board panel, reachable.
        var btn = ClonePoke(scene, board, "NewTaskButton", "＋ New task",
                            new Vector3(-0.24f, -0.17f, -0.012f), 1f);
        WirePoke(btn, boot, "CreateSampleTask", sb, null);
    }

    // ─────────────────────────── 4: ArtifactViewer panel ───────────────────────────

    static void BuildArtifactPanel(UnityEngine.SceneManagement.Scene scene, Transform ws, Transform board,
                                   Phase2Bootstrap boot, StringBuilder sb)
    {
        var old = ws.Find("ArtifactPanel");
        if (old != null) UnityEngine.Object.DestroyImmediate(old.gameObject);

        // Beside the board (to its right in world space), faces the user (readable face −Z).
        var panel = new GameObject("ArtifactPanel");
        panel.transform.SetParent(ws, false);
        panel.transform.position = board.position + new Vector3(0.75f, 0f, 0f);
        panel.transform.localRotation = Quaternion.identity;
        AddFaceUser(panel);

        var viewer = panel.AddComponent<ArtifactViewer>();
        viewer.phase2 = boot;
        viewer.autoOpenOnCreate = true;

        // Root: shown only while an artifact is open (ArtifactViewer disables it at OnEnable).
        var root = new GameObject("Root");
        root.transform.SetParent(panel.transform, false);

        var backing = GameObject.CreatePrimitive(PrimitiveType.Quad);
        backing.name = "Backing";
        backing.transform.SetParent(root.transform, false);
        backing.transform.localPosition = new Vector3(0f, 0f, 0.006f);
        backing.transform.localScale = new Vector3(0.5f, 0.6f, 1f);
        StripCollider(backing);
        backing.GetComponent<Renderer>().sharedMaterial = LitMat(new Color(0.97f, 0.96f, 0.92f), 0.06f);

        var title = MakeLabel(root.transform, "Title", "Artifact", new Vector3(0f, 0.255f, -0.004f), 0.5f,
                              new Color(0.10f, 0.10f, 0.12f), true);
        title.rectTransform.sizeDelta = new Vector2(0.46f, 0.08f);
        var body = MakeLabel(root.transform, "Body", "", new Vector3(0f, -0.01f, -0.004f), 0.26f,
                             new Color(0.12f, 0.12f, 0.14f), false);
        body.alignment = TextAlignmentOptions.TopLeft;
        body.rectTransform.sizeDelta = new Vector2(0.46f, 0.46f);
        body.textWrappingMode = TextWrappingModes.Normal;
        var page = MakeLabel(root.transform, "Page", "1 / 1", new Vector3(0f, -0.275f, -0.004f), 0.3f,
                             new Color(0.30f, 0.30f, 0.34f), false);

        viewer.root = root;
        viewer.titleLabel = title;
        viewer.bodyText = body;
        viewer.pageLabel = page;
        EditorUtility.SetDirty(viewer);

        // Nav pokes under the panel (always active; panel root is what toggles).
        var prev = ClonePoke(scene, panel.transform, "PrevPoke", "◀ Prev", new Vector3(-0.17f, -0.34f, -0.006f), 0.8f);
        WirePoke(prev, viewer, "PrevPage", sb, null);
        var next = ClonePoke(scene, panel.transform, "NextPoke", "Next ▶", new Vector3(0.17f, -0.34f, -0.006f), 0.8f);
        WirePoke(next, viewer, "NextPage", sb, null);
        var close = ClonePoke(scene, panel.transform, "ClosePoke", "Close", new Vector3(0f, -0.34f, -0.006f), 0.8f);
        WirePoke(close, viewer, "Close", sb, null);

        root.SetActive(false); // viewer re-asserts this at runtime; keeps the authored scene tidy
        sb.AppendLine("  ArtifactViewer panel built (root/title/body/page + Prev/Next/Close pokes).");
    }

    // ─────────────────────────── helpers ───────────────────────────

    static GameObject Find(string name) => _all.FirstOrDefault(g => g != null && g.name == name);

    static Transform FindPokeTemplate(UnityEngine.SceneManagement.Scene scene)
    {
        foreach (var root in scene.GetRootGameObjects())
            if (root.name == "Poke Interaction")
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                    if (t.name == "[BuildingBlock] ISDK_PokeInteraction") return t;
        return null;
    }

    static void StripCollider(GameObject go)
    {
        var c = go.GetComponent<Collider>();
        if (c != null) UnityEngine.Object.DestroyImmediate(c);
    }

    static void AddFaceUser(GameObject go)
    {
        // FaceUser lives in the default assembly; add by type if present (reflection-free: it is a
        // MuseDesk runtime type this editor assembly can see). forward=false => readable −Z toward user.
        var t = typeof(MuseDesk.Workspace.ReRegisterInteractable).Assembly.GetType("FaceUser")
                ?? AppDomain.CurrentDomain.GetAssemblies()
                    .Select(a => a.GetType("FaceUser")).FirstOrDefault(x => x != null);
        if (t == null) return;
        var comp = go.GetComponent(t) ?? go.AddComponent(t);
        var f = t.GetField("forward");
        if (f != null) f.SetValue(comp, false);
    }

    static Material LitMat(Color c, float emission = 0f)
    {
        var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        var m = new Material(shader);
        m.color = c;
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        if (emission > 0f && m.HasProperty("_EmissionColor"))
        {
            m.EnableKeyword("_EMISSION");
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            m.SetColor("_EmissionColor", c * emission);
        }
        return m;
    }

    static TextMeshPro MakeLabel(Transform parent, string name, string text, Vector3 localPos,
                                 float fontSize, Color color, bool bold)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localRotation = Quaternion.identity;
        var tmp = go.AddComponent<TextMeshPro>();
        tmp.text = text;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.enableAutoSizing = false;
        tmp.fontSize = fontSize;
        tmp.color = color;
        if (bold) tmp.fontStyle = FontStyles.Bold;
        tmp.rectTransform.sizeDelta = new Vector2(0.09f, 0.03f);
        return tmp;
    }

    // Clone the known-good standalone poke button, relabel + place + scale it.
    static GameObject ClonePoke(UnityEngine.SceneManagement.Scene scene, Transform parent, string name,
                                string label, Vector3 localPos, float scaleMul)
    {
        var template = FindPokeTemplate(scene);
        if (template == null) return null;

        var clone = UnityEngine.Object.Instantiate(template.gameObject, parent);
        clone.name = name;
        clone.transform.localPosition = localPos;
        clone.transform.localScale = template.lossyScale * scaleMul;
        var txt = MakeLabel(clone.transform, "ButtonLabel", label, new Vector3(0f, 0.03f, 0f), 6f, Color.white, true);
        txt.transform.localScale = Vector3.one; // label independent of the button's authored scale
        AddFaceUser(clone);
        return clone;
    }

    // Bind clone's InteractableUnityEventWrapper._whenSelect -> target.method(). Collect the poke
    // interactable(s) for re-registration when pokeInteractables != null.
    static void WirePoke(GameObject pokeClone, UnityEngine.Object target, string method, StringBuilder sb,
                         List<Behaviour> pokeInteractables)
    {
        if (pokeClone == null || target == null) { sb.AppendLine("  WirePoke skipped (" + method + ")"); return; }
        Component wrapper = null;
        foreach (var c in pokeClone.GetComponents<Component>())
            if (c != null && c.GetType().Name == "InteractableUnityEventWrapper") { wrapper = c; break; }
        if (wrapper == null) { sb.AppendLine("  no event wrapper on " + pokeClone.name); return; }

        var fi = wrapper.GetType().GetField("_whenSelect", BindingFlags.NonPublic | BindingFlags.Instance);
        var evt = fi != null ? fi.GetValue(wrapper) as UnityEvent : null;
        if (evt == null) { sb.AppendLine("  no _whenSelect on " + pokeClone.name); return; }

        for (int i = evt.GetPersistentEventCount() - 1; i >= 0; i--)
            UnityEventTools.RemovePersistentListener(evt, i);
        var action = (UnityAction)Delegate.CreateDelegate(typeof(UnityAction), target, method);
        UnityEventTools.AddVoidPersistentListener(evt, action);
        EditorUtility.SetDirty(wrapper);
        sb.AppendLine($"  wired {pokeClone.name}._whenSelect -> {target.GetType().Name}.{method}");

        if (pokeInteractables != null)
            foreach (var c in pokeClone.GetComponentsInChildren<Component>(true))
                if (c is Behaviour b && c.GetType().Name == "PokeInteractable")
                    pokeInteractables.Add(b);
    }
}
