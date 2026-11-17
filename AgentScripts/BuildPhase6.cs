using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.Events;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using TMPro;
using MuseDesk.Networking;
using MuseDesk.Workspace;
using MuseDesk.Diagrams;

/// <summary>
/// Phase 6 assembly: document inbox/outbox, completed-project display shelf + progress awards,
/// decorations + layout persistence, focus mode (with a poke toggle), and the architecture
/// diagram system (with a poke toggle). Every dynamic element is wired to the real reducer;
/// decorations are cosmetic and persisted separately. Idempotent — removes prior builds first.
/// </summary>
public static class BuildPhase6
{
    const string M = "Assets/MuseDesk/Art/Models/";

    public static string Run()
    {
        var scene = EditorSceneManager.OpenScene("Assets/MuseDesk/Scenes/MainMRScene.unity", OpenSceneMode.Single);
        var sb = new StringBuilder();
        var ws = GameObject.Find("WorkspaceRoot");
        var app = GameObject.Find("AppServices");
        var controller = Object.FindAnyObjectByType<SessionController>();
        if (ws == null) return "NO WorkspaceRoot";

        var host = GetOrMake(ws.transform, "Phase6");   // container so re-runs are clean
        Object.DestroyImmediate(host.gameObject);
        host = GetOrMake(ws.transform, "Phase6");

        // ---------- Document inbox + outbox ----------
        var inbox = Place(M + "Documents/Doc_InboxTray.fbx", host, "InboxTray",
                          new Vector3(-0.12f, 0.02f, 0.26f), 1f);
        Place(M + "Documents/Doc_OutboxTray.fbx", host, "OutboxTray",
              new Vector3(0.12f, 0.02f, 0.26f), 1f);

        // Focus inbox readout (badge + log) floating above the inbox tray, faces the user.
        var inboxUi = new GameObject("InboxReadout");
        inboxUi.transform.SetParent(inbox.transform, false);
        inboxUi.transform.localPosition = new Vector3(0f, 0.12f, 0f);
        inboxUi.AddComponent<FaceUser>();
        var badge = MakeText(inboxUi.transform, "Badge", new Vector3(0f, 0.03f, 0f), 10f, "");
        badge.color = new Color(0.95f, 0.65f, 0.2f);
        var log = MakeText(inboxUi.transform, "Log", new Vector3(0f, -0.02f, 0f), 4.5f, "");
        log.color = new Color(0.8f, 0.82f, 0.86f);

        // ---------- Completed-project display shelf + awards ----------
        var shelf = Place(M + "Decorations/Decor_ProjectDisplayShelf.fbx", host, "ProjectShelf",
                          new Vector3(0f, 0.0f, -0.24f), 1f);
        var slots = new List<Transform>();
        for (int i = 0; i < 4; i++)
        {
            var s = new GameObject("Slot" + i).transform;
            s.SetParent(shelf.transform, false);
            s.localPosition = new Vector3(-0.045f + i * 0.03f, 0.075f, 0f);
            slots.Add(s);
        }
        var trophy = Place(M + "Decorations/Decor_Trophy.fbx", shelf.transform, "Award_Trophy",
                           new Vector3(0.06f, 0.075f, 0f), 1f);
        var plaque = Place(M + "Decorations/Decor_AwardPlaque.fbx", shelf.transform, "Award_Plaque",
                           new Vector3(-0.06f, 0.075f, 0f), 1f);
        trophy.SetActive(false); plaque.SetActive(false);

        var shelfCtrl = shelf.AddComponent<ProjectShelfController>();
        shelfCtrl.controller = controller;
        shelfCtrl.slots = slots;
        shelfCtrl.completedTokenPrefab = Load(M + "Documents/Collab_ArtifactToken.fbx");
        shelfCtrl.tokenScale = 0.8f;
        shelfCtrl.awards = new List<GameObject> { trophy, plaque };
        shelfCtrl.awardThresholds = new List<int> { 2, 4 };

        // ---------- Decorations ----------
        var decor = new List<Transform>();
        decor.Add(Place(M + "Decorations/Decor_MiniPlant.fbx",  host, "Decor_MiniPlant",  new Vector3(-0.42f, 0.02f, 0.16f), 1f).transform);
        decor.Add(Place(M + "Decorations/Decor_DeskLamp.fbx",   host, "Decor_DeskLamp",   new Vector3(0.42f, 0.02f, 0.14f), 1f).transform);
        decor.Add(Place(M + "Decorations/Decor_CoffeeMug.fbx",  host, "Decor_CoffeeMug",  new Vector3(-0.12f, 0.02f, 0.18f), 1f).transform);
        decor.Add(Place(M + "Decorations/Decor_BookStack.fbx",  host, "Decor_BookStack",  new Vector3(0.14f, 0.02f, 0.16f), 1f).transform);
        decor.Add(Place(M + "Decorations/Decor_MiniClock.fbx",  host, "Decor_MiniClock",  new Vector3(0.0f, 0.3f, -0.26f), 1f).transform);
        decor.Add(Place(M + "Decorations/Decor_PosterSign.fbx", host, "Decor_PosterSign", new Vector3(-0.3f, 0.32f, -0.26f), 1f).transform);

        // Layout persistence (visibility + transforms of decorations + awards).
        var store = ws.GetComponent<WorkspaceLayoutStore>();
        if (store == null) store = ws.AddComponent<WorkspaceLayoutStore>();
        store.tracked = new List<Transform>(decor) { trophy.transform, plaque.transform };

        // ---------- Focus mode ----------
        var focusIndicator = Place(M + "Decorations/Decor_StatusIndicator.fbx", inbox.transform, "FocusLamp",
                                   new Vector3(0.05f, 0.03f, 0f), 0.7f);
        focusIndicator.SetActive(false);
        var focus = (app != null ? app : ws).GetComponent<FocusModeController>();
        if (focus == null) focus = (app != null ? app : ws).AddComponent<FocusModeController>();
        focus.controller = controller;
        focus.inboxBadge = badge;
        focus.inboxLog = log;
        focus.focusIndicator = focusIndicator;
        focus.startFocused = false;

        var focusBtn = ClonePoke(scene, host, "FocusToggle", "Focus",
                                 new Vector3(-0.2f, 0.06f, 0.28f));
        WirePoke(focusBtn, focus, "Toggle", sb);

        // ---------- Architecture diagrams ----------
        var diagHost = new GameObject("ArchitectureDiagram");
        diagHost.transform.SetParent(host, false);
        diagHost.transform.localPosition = new Vector3(0f, 0.46f, -0.1f);
        diagHost.transform.localScale = Vector3.one * 0.5f;
        diagHost.AddComponent<FaceUser>();
        var content = new GameObject("Content").transform;
        content.SetParent(diagHost.transform, false);
        var diag = diagHost.AddComponent<ArchitectureDiagramController>();
        diag.contentRoot = content;
        diag.connectorArrowPrefab   = Load(M + "Diagrams/Diagram_ConnectorArrow.fbx");
        diag.labelPlaquePrefab      = Load(M + "Diagrams/Diagram_LabelPlaque.fbx");
        diag.selectionHighlightPrefab = Load(M + "Diagrams/Diagram_SelectionHighlight.fbx");
        diag.displayPlatformPrefab  = Load(M + "Diagrams/Diagram_DisplayPlatform.fbx");
        diag.nodePrefabs = new List<ArchitectureDiagramController.TypePrefab>
        {
            NP("frontend",   M + "Diagrams/Diagram_FrontendNode.fbx"),
            NP("apigateway", M + "Diagrams/Diagram_ApiGatewayNode.fbx"),
            NP("auth",       M + "Diagrams/Diagram_AuthNode.fbx"),
            NP("database",   M + "Diagrams/Diagram_DatabaseNode.fbx"),
            NP("server",     M + "Diagrams/Diagram_ServerNode.fbx"),
            NP("storage",    M + "Diagrams/Diagram_StorageNode.fbx"),
            NP("queue",      M + "Diagrams/Diagram_QueueNode.fbx"),
            NP("cloud",      M + "Diagrams/Diagram_CloudNode.fbx"),
            NP("process",    M + "Diagrams/Diagram_ProcessNode.fbx"),
        };
        diagHost.SetActive(false);   // shown on demand via the Explain poke button

        var diagBtn = ClonePoke(scene, host, "ExplainToggle", "Explain",
                                new Vector3(0.2f, 0.06f, 0.28f));
        WirePoke(diagBtn, diag, "ToggleExample", sb);

        if (app != null) EditorUtility.SetDirty(app);
        EditorUtility.SetDirty(ws);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);

        sb.AppendLine($"Inbox/Outbox placed; shelf with {slots.Count} slots + 2 awards; " +
                      $"{decor.Count} decorations tracked; focus mode + diagram system wired; 2 poke toggles.");
        sb.AppendLine("controller bound: " + (controller != null));
        return sb.ToString();
    }

    // ---- helpers ----
    static Transform GetOrMake(Transform parent, string name)
    {
        var t = parent.Find(name);
        if (t != null) return t;
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        return go.transform;
    }

    static GameObject Load(string path) => AssetDatabase.LoadMainAssetAtPath(path) as GameObject;

    static GameObject Place(string fbx, Transform parent, string name, Vector3 localPos, float scale)
    {
        var asset = Load(fbx);
        GameObject go;
        if (asset != null) go = (GameObject)PrefabUtility.InstantiatePrefab(asset);
        else { go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.transform.localScale = Vector3.one * 0.03f; }
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localScale = Vector3.one * scale;
        return go;
    }

    static ArchitectureDiagramController.TypePrefab NP(string type, string fbx)
        => new ArchitectureDiagramController.TypePrefab { type = type, prefab = Load(fbx) };

    static TMP_Text MakeText(Transform parent, string name, Vector3 localPos, float size, string text)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localScale = Vector3.one * 0.008f;
        var t = go.AddComponent<TextMeshPro>();
        t.alignment = TextAlignmentOptions.Center;
        t.fontSize = size;
        t.textWrappingMode = TextWrappingModes.Normal;
        t.rectTransform.sizeDelta = new Vector2(16f, 10f);
        t.text = text;
        return t;
    }

    // Clone the known-good standalone poke button, relabel it, and place it.
    static GameObject ClonePoke(UnityEngine.SceneManagement.Scene scene, Transform parent, string name, string label, Vector3 localPos)
    {
        Transform template = null;
        foreach (var root in scene.GetRootGameObjects())
            if (root.name == "Poke Interaction")
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                    if (t.name == "[BuildingBlock] ISDK_PokeInteraction") { template = t; break; }
        if (template == null) { Debug.LogWarning("poke template not found"); return null; }

        var clone = Object.Instantiate(template.gameObject, parent);
        clone.name = name;
        clone.transform.localPosition = localPos;
        clone.transform.localScale = template.lossyScale;   // keep authored button size
        // Label floating above the button.
        var txt = MakeText(clone.transform, "ButtonLabel", new Vector3(0f, 0.03f, 0f), 6f, label);
        txt.color = Color.white;
        var fu = clone.AddComponent<FaceUser>();
        return clone;
    }

    // Bind the clone's InteractableUnityEventWrapper._whenSelect -> target.method().
    static void WirePoke(GameObject pokeClone, Object target, string method, StringBuilder sb)
    {
        if (pokeClone == null || target == null) { sb.AppendLine("WirePoke skipped (" + method + ")"); return; }
        Component wrapper = null;
        foreach (var c in pokeClone.GetComponents<Component>())
            if (c != null && c.GetType().Name == "InteractableUnityEventWrapper") { wrapper = c; break; }
        if (wrapper == null) { sb.AppendLine("no event wrapper on " + pokeClone.name); return; }

        var fi = wrapper.GetType().GetField("_whenSelect", BindingFlags.NonPublic | BindingFlags.Instance);
        var evt = fi != null ? fi.GetValue(wrapper) as UnityEvent : null;
        if (evt == null) { sb.AppendLine("no _whenSelect on " + pokeClone.name); return; }

        // Clear any inherited persistent calls, then add ours.
        for (int i = evt.GetPersistentEventCount() - 1; i >= 0; i--)
            UnityEventTools.RemovePersistentListener(evt, i);
        var action = (UnityAction)System.Delegate.CreateDelegate(typeof(UnityAction), target, method);
        UnityEventTools.AddVoidPersistentListener(evt, action);   // defaults to RuntimeOnly call state
        EditorUtility.SetDirty(wrapper);
        sb.AppendLine($"wired {pokeClone.name}._whenSelect -> {target.GetType().Name}.{method} (calls={evt.GetPersistentEventCount()})");
    }
}
