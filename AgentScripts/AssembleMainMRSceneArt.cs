using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using MuseDesk.Agents;
using MuseDesk.Art;
using MuseDesk.Diagrams;
using MuseDesk.Networking;
using MuseDesk.Phase2.Board;
using MuseDesk.Workspace;

public static class AssembleMainMRSceneArt
{
    const string ModelsRoot = "Assets/MuseDesk/Art/Models";
    const string MatsRoot   = "Assets/MuseDesk/Art/Materials";

    public static string Run()
    {
        var sb = new StringBuilder();
        var scene = EditorSceneManager.OpenScene("Assets/MuseDesk/Scenes/MainMRScene.unity", OpenSceneMode.Single);

        // Remove any temporary debug probe if present on AppServices so Scripts/ stays clean.
        var app = GameObject.Find("AppServices");
        if (app != null)
        {
            foreach (var c in app.GetComponents<Component>())
            {
                if (c != null && c.GetType().Name == "DeviceRenderProbe")
                {
                    UnityEngine.Object.DestroyImmediate(c);
                    sb.AppendLine("Removed temporary DeviceRenderProbe from AppServices.");
                }
            }
        }

        var ws = GameObject.Find("WorkspaceRoot");
        if (ws == null) return "ERROR: WorkspaceRoot not found.";

        var matMap = LoadAllMaterials();
        sb.AppendLine($"Loaded {matMap.Count} URP materials from {MatsRoot}.");

        // 1. Workstations (ResearchStation, DevStation, QAStation)
        AssembleWorkstations(ws.transform, app, matMap, sb);

        // 2. Reskin TaskBoard with TaskBoard_KanbanFrame + TaskCard_Grabbable + Badges/Markers/Links
        ReskinTaskBoard(ws.transform, matMap, sb);

        // 3. Place & orient SoftwareFactory nicely
        ConfigureSoftwareFactory(ws.transform, matMap, sb);

        // 4. Fix & populate Phase6 (Inbox/Outbox, ProjectShelf, Decorations, Architecture Diagram)
        AssemblePhase6AndProps(ws.transform, app, matMap, sb);

        // Apply URP materials across the entire WorkspaceRoot hierarchy
        ApplyMaterialsRecursively(ws, matMap);

        // 5. Capture honest Game-view renders (standard Camera frustum culling) from User POV (+Z looking toward -Z)
        CaptureVerificationViews(ws.transform, sb);

        // 6. Verify bounds on all placed props in WorkspaceRoot
        VerifyPlacedPropBounds(ws.transform, sb);

        EditorUtility.SetDirty(ws);
        if (app != null) EditorUtility.SetDirty(app);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();

        sb.AppendLine("MainMRScene.unity saved successfully.");
        return sb.ToString();
    }

    static Dictionary<string, Material> LoadAllMaterials()
    {
        var map = new Dictionary<string, Material>(StringComparer.OrdinalIgnoreCase);
        var guids = AssetDatabase.FindAssets("t:Material", new[] { MatsRoot });
        foreach (var g in guids)
        {
            string p = AssetDatabase.GUIDToAssetPath(g);
            var m = AssetDatabase.LoadAssetAtPath<Material>(p);
            if (m != null) map[m.name] = m;
        }
        return map;
    }

    static void ApplyMaterialsRecursively(GameObject root, Dictionary<string, Material> matMap)
    {
        if (root == null) return;
        foreach (var r in root.GetComponentsInChildren<Renderer>(true))
        {
            if (r.GetComponent<TextMeshPro>() != null) continue;
            var shared = r.sharedMaterials;
            bool changed = false;
            for (int i = 0; i < shared.Length; i++)
            {
                if (shared[i] == null) continue;
                string raw = shared[i].name;
                int dot = raw.IndexOf('.');
                if (dot > 0) raw = raw.Substring(0, dot);
                raw = raw.Replace(" (Instance)", "");
                if (matMap.TryGetValue(raw, out var urpMat) && shared[i] != urpMat)
                {
                    shared[i] = urpMat;
                    changed = true;
                }
            }
            if (changed) r.sharedMaterials = shared;
        }
    }

    static void AssembleWorkstations(Transform ws, GameObject app, Dictionary<string, Material> matMap, StringBuilder sb)
    {
        var specs = new[]
        {
            new { station = "ResearchStation", fbx = $"{ModelsRoot}/Workstations/Workstation_Research.fbx",  role = "researcher", scale = 0.72f },
            new { station = "DevStation",      fbx = $"{ModelsRoot}/Workstations/Workstation_Developer.fbx", role = "developer",  scale = 0.80f },
            new { station = "QAStation",       fbx = $"{ModelsRoot}/Workstations/Workstation_Testing.fbx",   role = "tester",     scale = 0.80f },
        };

        WorkstationVisualController qaVis = null;

        foreach (var sp in specs)
        {
            var st = ws.Find(sp.station);
            if (st == null) continue;

            // Keep Agent at its exact home position (0,0,0); replace/update Workstation child
            var oldDesk = st.Find("Workstation");
            if (oldDesk != null) UnityEngine.Object.DestroyImmediate(oldDesk.gameObject);

            var fbxAsset = AssetDatabase.LoadMainAssetAtPath(sp.fbx) as GameObject;
            if (fbxAsset == null) continue;

            var deskGo = (GameObject)PrefabUtility.InstantiatePrefab(fbxAsset);
            deskGo.name = "Workstation";
            deskGo.transform.SetParent(st, false);
            deskGo.transform.localScale = Vector3.one * sp.scale;
            // Rotate 180 deg around Y so the monitor screens, open bookshelf, whiteboard,
            // and testing chamber face +Z (toward the user)!
            deskGo.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            // Place desk at Z = +0.115m in front of the agent (at Z=0) so the monitors and desk
            // sit cleanly in front of the agent's body without being occluded by the torso,
            // while the agent's head and upper body rise right behind the desk!
            deskGo.transform.localPosition = new Vector3(0f, 0f, 0.115f);

            // Tuck the chair meshes behind the agent (in desk local space, +Z is toward -Z of station
            // because the desk is rotated 180 deg on Y) so the chair sits behind the agent instead of in front of the desk.
            foreach (var t in deskGo.GetComponentsInChildren<Transform>(true))
            {
                if (t.name.Contains("_Chair") || t.name == "AgentSeatPosition")
                {
                    Vector3 lp = t.localPosition;
                    if (lp.z < 0f)
                    {
                        lp.z = 0.21f + (-lp.z - 0.032f);
                        t.localPosition = lp;
                    }
                }
            }

            var vis = deskGo.GetComponent<WorkstationVisualController>();
            if (vis == null) vis = deskGo.AddComponent<WorkstationVisualController>();
            vis.ResolveAnchorsAndParts();
            var so = new SerializedObject(vis);
            var roleProp = so.FindProperty("stationRole");
            if (roleProp != null) { roleProp.stringValue = sp.role; so.ApplyModifiedPropertiesWithoutUndo(); }

            if (sp.station == "QAStation") qaVis = vis;

            // Ensure the floating live Monitor (WorkstationStatusDisplay) uses FaceUser(forward = false)
            // and is pre-rotated 180 deg around Y at edit time so its TextMeshPro text is un-mirrored
            // both in Edit mode and when viewed from +Z (the user's side).
            var mon = st.Find("Monitor");
            if (mon != null)
            {
                mon.localPosition = new Vector3(0f, 0.355f, 0.02f);
                mon.localRotation = Quaternion.Euler(0f, 180f, 0f);
                var fu = mon.GetComponent<FaceUser>();
                if (fu == null) fu = mon.gameObject.AddComponent<FaceUser>();
                fu.forward = false;
                var back = mon.Find("Back");
                if (back != null)
                {
                    back.localPosition = new Vector3(0f, 0f, 0.002f);
                    back.localScale = new Vector3(0.105f, 0.038f, 1f);
                }
                var status = mon.Find("Status");
                if (status != null)
                {
                    status.localPosition = new Vector3(0f, 0f, -0.002f);
                    status.localRotation = Quaternion.identity;
                }
                EditorUtility.SetDirty(mon.gameObject);
            }

            // Also pre-rotate NameTag and Alert to (0, 180, 0) at edit time to match FaceUser(forward = false)
            var agentView = st.GetComponentInChildren<AgentView>(true);
            if (agentView != null)
            {
                foreach (var childName in new[] { "NameTag", "Alert" })
                {
                    var ct = agentView.transform.Find(childName);
                    if (ct != null) ct.localRotation = Quaternion.Euler(0f, 180f, 0f);
                }
            }

            ApplyMaterialsRecursively(deskGo, matMap);
            EditorUtility.SetDirty(deskGo);
            sb.AppendLine($"Workstation '{sp.station}/Workstation' placed at {deskGo.transform.localPosition} rot=(0,180,0) scale={sp.scale}.");
        }

        // Preserve TestingStationController binding on AppServices
        if (app != null && qaVis != null)
        {
            var tsc = app.GetComponent<TestingStationController>();
            if (tsc != null)
            {
                tsc.testingStation = qaVis;
                EditorUtility.SetDirty(tsc);
                sb.AppendLine("Rebound TestingStationController.testingStation -> QAStation/Workstation.");
            }
        }
    }

    static void ReskinTaskBoard(Transform ws, Dictionary<string, Material> matMap, StringBuilder sb)
    {
        var board = ws.Find("TaskBoard");
        if (board == null)
        {
            sb.AppendLine("WARN: TaskBoard not found under WorkspaceRoot.");
            return;
        }

        // Position TaskBoard centered behind the stations so it frames the diorama symmetrically,
        // and keep FaceUser(forward = false) so its readable face (-Z) points at the user (+Z).
        board.localPosition = new Vector3(0.0f, 0.23f, -0.22f);
        board.localRotation = Quaternion.Euler(0f, 180f, 0f); // At edit-time, -Z faces +Z (user); at runtime FaceUser(forward=false) maintains it!
        var fu = board.GetComponent<FaceUser>();
        if (fu == null) fu = board.gameObject.AddComponent<FaceUser>();
        fu.forward = false;

        // Disable the procedural flat Quad renderer on Backing (keep the GameObject intact)
        var backing = board.Find("Backing");
        if (backing != null)
        {
            var mr = backing.GetComponent<Renderer>();
            if (mr != null) mr.enabled = false;
        }

        // Adjust Title and Column Labels so they sit cleanly on the 3D KanbanFrame header plaques without overlapping
        var boardTitle = board.Find("Title");
        if (boardTitle != null)
        {
            boardTitle.localPosition = new Vector3(0f, 0.235f, -0.014f);
            var tmp = boardTitle.GetComponent<TextMeshPro>();
            if (tmp != null) { tmp.fontSize = 0.28f; tmp.color = new Color(0.96f, 0.94f, 0.88f); }
        }
        foreach (var colName in new[] { "Label_Backlog", "Label_Working", "Label_NeedsMe", "Label_Completed" })
        {
            var cl = board.Find(colName);
            if (cl != null)
            {
                Vector3 lp = cl.localPosition;
                lp.y = 0.165f;
                lp.z = -0.015f;
                cl.localPosition = lp;
                var tmp = cl.GetComponent<TextMeshPro>();
                if (tmp != null) { tmp.fontSize = 0.23f; tmp.color = Color.white; tmp.fontStyle = FontStyles.Bold; }
            }
        }

        // Remove any prior reskin children so this is idempotent
        foreach (var oldName in new[] { "KanbanFrame_Visual", "DependencyLink_1", "DependencyLink_2",
            "PreviewCard_Queued", "PreviewCard_Working", "PreviewCard_Blocked", "PreviewCard_Completed" })
        {
            var old = board.Find(oldName);
            if (old != null) UnityEngine.Object.DestroyImmediate(old.gameObject);
        }

        // Instantiate real TaskBoard_KanbanFrame.fbx as the 3D visual skin of TaskBoard
        var frameFbx = AssetDatabase.LoadMainAssetAtPath($"{ModelsRoot}/TaskBoard/TaskBoard_KanbanFrame.fbx") as GameObject;
        if (frameFbx != null)
        {
            var frameGo = (GameObject)PrefabUtility.InstantiatePrefab(frameFbx);
            frameGo.name = "KanbanFrame_Visual";
            frameGo.transform.SetParent(board, false);
            // TaskBoard's readable side is -Z; rotate KanbanFrame_Visual 180 deg around Y so its front faces -Z!
            frameGo.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            frameGo.transform.localScale = new Vector3(1.61f, 1.52f, 1.20f);
            frameGo.transform.localPosition = new Vector3(0f, -0.21f, 0.018f);
            ApplyMaterialsRecursively(frameGo, matMap);
        }

        // Add dependency links across the board
        var depFbx = AssetDatabase.LoadMainAssetAtPath($"{ModelsRoot}/TaskBoard/TaskBoard_DependencyLink.fbx") as GameObject;
        if (depFbx != null)
        {
            var dep1 = (GameObject)PrefabUtility.InstantiatePrefab(depFbx);
            dep1.name = "DependencyLink_1";
            dep1.transform.SetParent(board, false);
            dep1.transform.localPosition = new Vector3(-0.16f, 0.04f, -0.016f);
            dep1.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            dep1.transform.localScale = Vector3.one * 1.15f;

            var dep2 = (GameObject)PrefabUtility.InstantiatePrefab(depFbx);
            dep2.name = "DependencyLink_2";
            dep2.transform.SetParent(board, false);
            dep2.transform.localPosition = new Vector3(0.00f, 0.04f, -0.016f);
            dep2.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            dep2.transform.localScale = Vector3.one * 1.15f;
        }

        // Reskin CardTemplate in-place (preserving Grabbable, HandGrabInteractable, TaskCardView,
        // TaskDragController, CardRoleTint, ReRegisterInteractable, Rigidbody, BoxCollider, HeaderStrip, Title)
        var cardTemplate = board.Find("CardTemplate");
        var cardFbx   = AssetDatabase.LoadMainAssetAtPath($"{ModelsRoot}/TaskBoard/TaskCard_Grabbable.fbx") as GameObject;
        var prioFbx   = AssetDatabase.LoadMainAssetAtPath($"{ModelsRoot}/TaskBoard/TaskBoard_PriorityBadge.fbx") as GameObject;
        var statusFbx = AssetDatabase.LoadMainAssetAtPath($"{ModelsRoot}/TaskBoard/TaskBoard_StatusMarker.fbx") as GameObject;
        var ownerFbx  = AssetDatabase.LoadMainAssetAtPath($"{ModelsRoot}/TaskBoard/TaskBoard_OwnerIcon.fbx") as GameObject;

        if (cardTemplate != null && cardFbx != null)
        {
            foreach (var qName in new[] { "Shadow", "Face" })
            {
                var q = cardTemplate.Find(qName);
                if (q != null)
                {
                    var mr = q.GetComponent<Renderer>();
                    if (mr != null) mr.enabled = false;
                }
            }

            foreach (var oldVis in new[] { "TaskCard_Visual", "PriorityBadge_Visual", "StatusMarker_Visual", "OwnerIcon_Visual" })
            {
                var ov = cardTemplate.Find(oldVis);
                if (ov != null) UnityEngine.Object.DestroyImmediate(ov.gameObject);
            }

            var cVis = (GameObject)PrefabUtility.InstantiatePrefab(cardFbx);
            cVis.name = "TaskCard_Visual";
            cVis.transform.SetParent(cardTemplate, false);
            cVis.transform.localPosition = new Vector3(0f, 0f, 0.003f);
            cVis.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            cVis.transform.localScale = new Vector3(1.12f, 1.12f, 1.0f);

            if (prioFbx != null)
            {
                var pVis = (GameObject)PrefabUtility.InstantiatePrefab(prioFbx);
                pVis.name = "PriorityBadge_Visual";
                pVis.transform.SetParent(cardTemplate, false);
                pVis.transform.localPosition = new Vector3(0.030f, 0.018f, -0.004f);
                pVis.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
                pVis.transform.localScale = Vector3.one * 0.9f;
            }
            if (statusFbx != null)
            {
                var sVis = (GameObject)PrefabUtility.InstantiatePrefab(statusFbx);
                sVis.name = "StatusMarker_Visual";
                sVis.transform.SetParent(cardTemplate, false);
                sVis.transform.localPosition = new Vector3(-0.030f, -0.016f, -0.004f);
                sVis.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
                sVis.transform.localScale = Vector3.one * 0.9f;
            }
            if (ownerFbx != null)
            {
                var oVis = (GameObject)PrefabUtility.InstantiatePrefab(ownerFbx);
                oVis.name = "OwnerIcon_Visual";
                oVis.transform.SetParent(cardTemplate, false);
                oVis.transform.localPosition = new Vector3(0.030f, -0.016f, -0.004f);
                oVis.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
                oVis.transform.localScale = Vector3.one * 0.9f;
            }

            var header = cardTemplate.Find("HeaderStrip");
            if (header != null) header.localPosition = new Vector3(0f, 0.018f, -0.0035f);
            var title = cardTemplate.Find("Title");
            if (title != null) title.localPosition = new Vector3(0f, -0.003f, -0.0045f);

            ApplyMaterialsRecursively(cardTemplate.gameObject, matMap);
            EditorUtility.SetDirty(cardTemplate.gameObject);
        }

        // Also place 4 visible 3D TaskCard_Grabbable + badge/status/owner markers in the lower row of the board
        // so the board looks populated immediately in edit/game view as well.
        var previewCols = new[]
        {
            new { name = "PreviewCard_Queued",    x = -0.24f, y = -0.06f },
            new { name = "PreviewCard_Working",   x = -0.08f, y = -0.06f },
            new { name = "PreviewCard_Blocked",   x =  0.08f, y = -0.06f },
            new { name = "PreviewCard_Completed", x =  0.24f, y = -0.06f },
        };
        foreach (var pc in previewCols)
        {
            if (cardFbx == null) break;
            var cardGo = (GameObject)PrefabUtility.InstantiatePrefab(cardFbx);
            cardGo.name = pc.name;
            cardGo.transform.SetParent(board, false);
            cardGo.transform.localPosition = new Vector3(pc.x, pc.y, -0.010f);
            cardGo.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            cardGo.transform.localScale = Vector3.one * 1.05f;

            if (prioFbx != null)
            {
                var p = (GameObject)PrefabUtility.InstantiatePrefab(prioFbx);
                p.name = "PriorityBadge";
                p.transform.SetParent(cardGo.transform, false);
                p.transform.localPosition = new Vector3(-0.025f, 0.014f, 0.004f);
                p.transform.localRotation = Quaternion.identity;
                p.transform.localScale = Vector3.one * 0.85f;
            }
            if (statusFbx != null)
            {
                var s = (GameObject)PrefabUtility.InstantiatePrefab(statusFbx);
                s.name = "StatusMarker";
                s.transform.SetParent(cardGo.transform, false);
                s.transform.localPosition = new Vector3(0.025f, -0.013f, 0.004f);
                s.transform.localRotation = Quaternion.identity;
                s.transform.localScale = Vector3.one * 0.85f;
            }
            if (ownerFbx != null)
            {
                var o = (GameObject)PrefabUtility.InstantiatePrefab(ownerFbx);
                o.name = "OwnerIcon";
                o.transform.SetParent(cardGo.transform, false);
                o.transform.localPosition = new Vector3(-0.025f, -0.013f, 0.004f);
                o.transform.localRotation = Quaternion.identity;
                o.transform.localScale = Vector3.one * 0.85f;
            }
            ApplyMaterialsRecursively(cardGo, matMap);
        }

        EditorUtility.SetDirty(board.gameObject);
        sb.AppendLine("TaskBoard reskinned with TaskBoard_KanbanFrame, TaskCard_Grabbable, PriorityBadge, StatusMarker, OwnerIcon, and DependencyLinks.");
    }

    static void ConfigureSoftwareFactory(Transform ws, Dictionary<string, Material> matMap, StringBuilder sb)
    {
        var factory = ws.Find("SoftwareFactory");
        if (factory == null)
        {
            sb.AppendLine("WARN: SoftwareFactory not found under WorkspaceRoot.");
            return;
        }

        // Place SoftwareFactory neatly across the front of the workspace, rotated 180 deg on Y
        // so its front windows, conveyor, and status lights face +Z (toward the user).
        factory.localPosition = new Vector3(0f, 0f, 0.25f);
        factory.localRotation = Quaternion.Euler(0f, 180f, 0f);
        factory.localScale = Vector3.one * 0.68f;

        var fvc = factory.GetComponent<SoftwareFactoryVisualController>();
        if (fvc != null) fvc.ResolveReferences();

        ApplyMaterialsRecursively(factory.gameObject, matMap);
        EditorUtility.SetDirty(factory.gameObject);
        sb.AppendLine($"SoftwareFactory positioned at {factory.localPosition} rot=(0,180,0) scale=0.68.");
    }

    static void AssemblePhase6AndProps(Transform ws, GameObject app, Dictionary<string, Material> matMap, StringBuilder sb)
    {
        var phase6 = ws.Find("Phase6");
        if (phase6 == null)
        {
            sb.AppendLine("WARN: Phase6 container not found under WorkspaceRoot.");
            return;
        }

        // 1. InboxTray & OutboxTray (fix rotation from 270.02 X to upright, facing +Z user)
        var inbox = phase6.Find("InboxTray");
        if (inbox != null)
        {
            inbox.localPosition = new Vector3(-0.32f, 0.0f, 0.25f);
            inbox.localRotation = Quaternion.Euler(0f, 180f, 0f);
            inbox.localScale = Vector3.one;

            var readout = inbox.Find("InboxReadout");
            if (readout != null)
            {
                readout.localPosition = new Vector3(0f, 0.11f, 0f);
                readout.localRotation = Quaternion.Euler(0f, 180f, 0f);
                var fu = readout.GetComponent<FaceUser>();
                if (fu == null) fu = readout.gameObject.AddComponent<FaceUser>();
                fu.forward = false; // Un-mirror TMP text when facing +Z user
            }

            var focusLamp = inbox.Find("FocusLamp");
            if (focusLamp != null)
            {
                focusLamp.localPosition = new Vector3(0.055f, 0.0f, 0f);
                focusLamp.localRotation = Quaternion.identity;
                focusLamp.localScale = Vector3.one * 0.75f;
            }
            EditorUtility.SetDirty(inbox.gameObject);
        }

        var outbox = phase6.Find("OutboxTray");
        if (outbox != null)
        {
            outbox.localPosition = new Vector3(0.32f, 0.0f, 0.25f);
            outbox.localRotation = Quaternion.Euler(0f, 180f, 0f);
            outbox.localScale = Vector3.one;
            EditorUtility.SetDirty(outbox.gameObject);
        }

        // 2. ProjectShelf & Awards (fix rotation from 270.02 X to upright, facing +Z user)
        var shelf = phase6.Find("ProjectShelf");
        Transform trophyTx = null, plaqueTx = null;
        if (shelf != null)
        {
            shelf.localPosition = new Vector3(0.38f, 0.0f, -0.15f);
            shelf.localRotation = Quaternion.Euler(0f, 180f, 0f);
            shelf.localScale = Vector3.one * 1.15f;

            var trophy = shelf.Find("Award_Trophy");
            if (trophy != null)
            {
                trophy.localPosition = new Vector3(0.045f, 0.088f, 0f);
                trophy.localRotation = Quaternion.identity;
                trophy.localScale = Vector3.one;
                trophyTx = trophy;
            }
            var plaque = shelf.Find("Award_Plaque");
            if (plaque != null)
            {
                plaque.localPosition = new Vector3(-0.045f, 0.088f, 0f);
                plaque.localRotation = Quaternion.identity;
                plaque.localScale = Vector3.one;
                plaqueTx = plaque;
            }
            EditorUtility.SetDirty(shelf.gameObject);
        }

        // 3. Fix existing 6 decorations under Phase6 (upright rotations + tasteful positions)
        SetTransform(phase6.Find("Decor_MiniPlant"),  new Vector3(-0.44f, 0.0f,  0.08f), new Vector3(0f, 180f, 0f), 1.15f);
        SetTransform(phase6.Find("Decor_DeskLamp"),   new Vector3( 0.44f, 0.0f,  0.08f), new Vector3(0f, 155f, 0f), 1.10f);
        SetTransform(phase6.Find("Decor_CoffeeMug"),  new Vector3(-0.16f, 0.0f,  0.14f), new Vector3(0f, 140f, 0f), 1.15f);
        SetTransform(phase6.Find("Decor_BookStack"),  new Vector3(-0.44f, 0.0f, -0.05f), new Vector3(0f, 165f, 0f), 1.15f);
        SetTransform(phase6.Find("Decor_MiniClock"),  new Vector3( 0.14f, 0.0f, -0.17f), new Vector3(0f, 180f, 0f), 1.20f);
        SetTransform(phase6.Find("Decor_PosterSign"), new Vector3( 0.34f, 0.22f, -0.19f), new Vector3(0f, 180f, 0f), 1.25f);

        // 4. Fix FocusToggle and ExplainToggle button labels so they are un-mirrored
        foreach (var btnName in new[] { "FocusToggle", "ExplainToggle" })
        {
            var btn = phase6.Find(btnName);
            if (btn == null) continue;
            var lbl = btn.Find("ButtonLabel");
            if (lbl != null)
            {
                // Since btn has FaceUser(forward = true) so the poke surface faces the user (+Z),
                // rotating ButtonLabel 180 deg around Y makes its readable face (-Z of TMP) point along +Z of btn!
                lbl.localRotation = Quaternion.Euler(0f, 180f, 0f);
                lbl.localPosition = new Vector3(0f, 0.65f, 0f);
            }
        }
        SetTransform(phase6.Find("FocusToggle"),   new Vector3(-0.42f, 0.05f, 0.22f), Vector3.zero, 0.05f);
        SetTransform(phase6.Find("ExplainToggle"), new Vector3( 0.42f, 0.05f, 0.22f), Vector3.zero, 0.05f);

        // 5. Fix ArchitectureDiagram FaceUser so its runtime labels are un-mirrored
        var archDiag = phase6.Find("ArchitectureDiagram");
        if (archDiag != null)
        {
            archDiag.localPosition = new Vector3(0f, 0.38f, -0.05f);
            var fu = archDiag.GetComponent<FaceUser>();
            if (fu == null) fu = archDiag.gameObject.AddComponent<FaceUser>();
            fu.forward = false;
            EditorUtility.SetDirty(archDiag.gameObject);
        }

        // 6. Add additional rich props, documents, decorations, and visible spatial architecture diagram layout
        var extraContainer = phase6.Find("OfficeProps_Collection");
        if (extraContainer != null) UnityEngine.Object.DestroyImmediate(extraContainer.gameObject);
        var extraGo = new GameObject("OfficeProps_Collection");
        extraGo.transform.SetParent(phase6, false);
        var extra = extraGo.transform;

        var addedDecor = new List<Transform>();

        // Additional Decorations
        addedDecor.Add(PlaceFbx($"{ModelsRoot}/Decorations/Decor_FilingCabinet.fbx", extra, "Decor_FilingCabinet", new Vector3(-0.46f, 0.0f, -0.17f), new Vector3(0f, 165f, 0f), 1.10f));
        addedDecor.Add(PlaceFbx($"{ModelsRoot}/Decorations/Decor_Toolbox.fbx",       extra, "Decor_Toolbox",       new Vector3( 0.16f, 0.0f,  0.14f), new Vector3(0f, 195f, 0f), 1.10f));
        addedDecor.Add(PlaceFbx($"{ModelsRoot}/Decorations/Decor_SmallShelf.fbx",    extra, "Decor_SmallShelf",    new Vector3( 0.46f, 0.0f, -0.05f), new Vector3(0f, 180f, 0f), 1.10f));
        addedDecor.Add(PlaceFbx($"{ModelsRoot}/Decorations/Decor_Trophy.fbx",        extra, "Decor_Trophy_Display",new Vector3( 0.46f, 0.070f,-0.05f), new Vector3(0f, 180f, 0f), 1.00f));
        addedDecor.Add(PlaceFbx($"{ModelsRoot}/Decorations/Decor_AwardPlaque.fbx",   extra, "Decor_Plaque_Display",new Vector3( 0.46f, 0.036f,-0.05f), new Vector3(0f, 180f, 0f), 1.00f));
        addedDecor.Add(PlaceFbx($"{ModelsRoot}/Decorations/Decor_ProgressAward.fbx", extra, "Decor_ProgressAward", new Vector3( 0.25f, 0.0f, -0.17f), new Vector3(0f, 180f, 0f), 1.10f));
        addedDecor.Add(PlaceFbx($"{ModelsRoot}/Decorations/Decor_DeskMat.fbx",       extra, "Decor_DeskMat",       new Vector3( 0.00f, 0.001f, 0.07f), new Vector3(0f, 180f, 0f), 1.15f));

        // Document & Approval Props around Inbox / Outbox / Archive
        PlaceFbx($"{ModelsRoot}/Documents/Doc_ApprovalPaper.fbx",    extra, "Doc_ApprovalPaper",    new Vector3(-0.30f, 0.006f, 0.22f), new Vector3(0f, 180f, 0f), 1.0f);
        PlaceFbx($"{ModelsRoot}/Documents/Doc_ApprovalStamp.fbx",    extra, "Doc_ApprovalStamp",    new Vector3(-0.23f, 0.0f,   0.23f), new Vector3(0f, 165f, 0f), 1.1f);
        PlaceFbx($"{ModelsRoot}/Documents/Doc_SignaturePen.fbx",     extra, "Doc_SignaturePen",     new Vector3(-0.20f, 0.0f,   0.22f), new Vector3(0f, 150f, 0f), 1.1f);
        PlaceFbx($"{ModelsRoot}/Documents/Doc_ResearchDocument.fbx", extra, "Doc_ResearchDocument", new Vector3( 0.30f, 0.006f, 0.22f), new Vector3(0f, 180f, 0f), 1.0f);
        PlaceFbx($"{ModelsRoot}/Documents/Doc_TestReport.fbx",       extra, "Doc_TestReport",       new Vector3( 0.30f, 0.010f, 0.22f), new Vector3(0f, 192f, 0f), 1.0f);
        PlaceFbx($"{ModelsRoot}/Documents/Doc_RolledBlueprint.fbx",  extra, "Doc_RolledBlueprint",  new Vector3(-0.38f, 0.0f,   0.16f), new Vector3(0f, 160f, 0f), 1.1f);
        PlaceFbx($"{ModelsRoot}/Documents/Doc_Clipboard.fbx",        extra, "Doc_Clipboard",        new Vector3( 0.22f, 0.0f,   0.22f), new Vector3(0f, 185f, 0f), 1.0f);
        PlaceFbx($"{ModelsRoot}/Documents/Doc_DocumentArchive.fbx",  extra, "Doc_DocumentArchive",  new Vector3( 0.44f, 0.0f,  -0.17f), new Vector3(0f, 180f, 0f), 1.1f);
        PlaceFbx($"{ModelsRoot}/Documents/Collab_PackageBox.fbx",    extra, "Collab_PackageBox",    new Vector3(-0.24f, 0.0f,   0.15f), new Vector3(0f, 170f, 0f), 1.0f);

        // Visible Spatial Architecture Diagram Platform on the desk (Frontend -> API -> Auth -> Database)
        var diagDesk = new GameObject("SpatialDiagram_DeskLayout");
        diagDesk.transform.SetParent(extra, false);
        diagDesk.transform.localPosition = new Vector3(0.06f, 0.0f, -0.17f);
        diagDesk.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
        diagDesk.transform.localScale = Vector3.one * 0.62f;

        PlaceFbx($"{ModelsRoot}/Diagrams/Diagram_DisplayPlatform.fbx", diagDesk.transform, "Diagram_DisplayPlatform", Vector3.zero, Vector3.zero, 1.0f);
        PlaceFbx($"{ModelsRoot}/Diagrams/Diagram_FrontendNode.fbx",    diagDesk.transform, "Diagram_FrontendNode",    new Vector3(-0.12f, 0.016f, 0f), Vector3.zero, 1.0f);
        PlaceFbx($"{ModelsRoot}/Diagrams/Diagram_ConnectorArrow.fbx",  diagDesk.transform, "Diagram_ConnectorArrow_1",new Vector3(-0.08f, 0.018f, 0f), Vector3.zero, 1.0f);
        PlaceFbx($"{ModelsRoot}/Diagrams/Diagram_ApiGatewayNode.fbx",  diagDesk.transform, "Diagram_ApiGatewayNode",  new Vector3(-0.04f, 0.016f, 0f), Vector3.zero, 1.0f);
        PlaceFbx($"{ModelsRoot}/Diagrams/Diagram_ConnectorArrow.fbx",  diagDesk.transform, "Diagram_ConnectorArrow_2",new Vector3( 0.00f, 0.018f, 0f), Vector3.zero, 1.0f);
        PlaceFbx($"{ModelsRoot}/Diagrams/Diagram_AuthNode.fbx",        diagDesk.transform, "Diagram_AuthNode",        new Vector3( 0.04f, 0.016f, 0f), Vector3.zero, 1.0f);
        PlaceFbx($"{ModelsRoot}/Diagrams/Diagram_ConnectorArrow.fbx",  diagDesk.transform, "Diagram_ConnectorArrow_3",new Vector3( 0.08f, 0.018f, 0f), Vector3.zero, 1.0f);
        PlaceFbx($"{ModelsRoot}/Diagrams/Diagram_DatabaseNode.fbx",    diagDesk.transform, "Diagram_DatabaseNode",    new Vector3( 0.12f, 0.016f, 0f), Vector3.zero, 1.0f);
        PlaceFbx($"{ModelsRoot}/Diagrams/Diagram_LabelPlaque.fbx",     diagDesk.transform, "Diagram_LabelPlaque",     new Vector3( 0.00f, 0.012f, -0.055f), Vector3.zero, 1.0f);

        // Update WorkspaceLayoutStore.tracked with all decorations + awards
        var store = ws.GetComponent<WorkspaceLayoutStore>();
        if (store != null)
        {
            var tracked = new List<Transform>();
            foreach (var n in new[] { "Decor_MiniPlant", "Decor_DeskLamp", "Decor_CoffeeMug", "Decor_BookStack", "Decor_MiniClock", "Decor_PosterSign" })
            {
                var t = phase6.Find(n);
                if (t != null) tracked.Add(t);
            }
            foreach (var t in addedDecor)
                if (t != null) tracked.Add(t);
            if (trophyTx != null) tracked.Add(trophyTx);
            if (plaqueTx != null) tracked.Add(plaqueTx);
            store.tracked = tracked;
            EditorUtility.SetDirty(store);
        }

        sb.AppendLine("Phase6 props, Inbox/Outbox, ProjectShelf, Decorations, Document props, and SpatialDiagram_DeskLayout assembled.");
    }

    static void SetTransform(Transform t, Vector3 pos, Vector3 euler, float scale)
    {
        if (t == null) return;
        t.localPosition = pos;
        t.localRotation = Quaternion.Euler(euler);
        t.localScale = Vector3.one * scale;
        EditorUtility.SetDirty(t.gameObject);
    }

    static Transform PlaceFbx(string path, Transform parent, string name, Vector3 localPos, Vector3 localEuler, float scale)
    {
        var asset = AssetDatabase.LoadMainAssetAtPath(path) as GameObject;
        if (asset == null) return null;
        var go = (GameObject)PrefabUtility.InstantiatePrefab(asset);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localRotation = Quaternion.Euler(localEuler);
        go.transform.localScale = Vector3.one * scale;
        return go.transform;
    }

    static void CaptureVerificationViews(Transform ws, StringBuilder sb)
    {
        // User sits on the +Z side of WorkspaceRoot looking toward WorkspaceRoot (-Z direction).
        Vector3 wsPos = ws.position;

        // 1. Full Office User POV
        RenderView(
            wsPos + new Vector3(0.0f, 0.36f, 0.78f),
            wsPos + new Vector3(0.0f, 0.11f, 0.00f),
            42f,
            "Screenshots/ArtPreviews/GameView_MainMRScene_FullOffice.png",
            1600, 1000);

        // 2. Developer Workstation & Factory Close-Up
        RenderView(
            wsPos + new Vector3(0.04f, 0.22f, 0.42f),
            wsPos + new Vector3(0.00f, 0.10f, 0.08f),
            36f,
            "Screenshots/ArtPreviews/GameView_MainMRScene_DeskCloseUp.png",
            1600, 1000);

        // 3. TaskBoard, Shelf, Diagrams & Decorations View
        RenderView(
            wsPos + new Vector3(-0.08f, 0.32f, 0.55f),
            wsPos + new Vector3(-0.05f, 0.16f, -0.15f),
            40f,
            "Screenshots/ArtPreviews/GameView_MainMRScene_TaskBoardAndProps.png",
            1600, 1000);

        sb.AppendLine("Captured 3 Game-view verification renders under Screenshots/ArtPreviews/.");
    }

    static void RenderView(Vector3 camPos, Vector3 lookTarget, float fov, string relPath, int w, int h)
    {
        var camGo = new GameObject("__TempVerifyCamera");
        var cam = camGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.14f, 0.16f, 0.20f, 1f);
        cam.fieldOfView = fov;
        cam.nearClipPlane = 0.02f;
        cam.farClipPlane = 20f;
        camGo.transform.position = camPos;
        camGo.transform.LookAt(lookTarget);

        var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
        var prevActive = RenderTexture.active;
        cam.targetTexture = rt;
        cam.Render();
        RenderTexture.active = rt;

        var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        tex.Apply();

        cam.targetTexture = null;
        RenderTexture.active = prevActive;
        UnityEngine.Object.DestroyImmediate(rt);
        UnityEngine.Object.DestroyImmediate(camGo);

        byte[] png = ImageConversion.EncodeToPNG(tex);
        UnityEngine.Object.DestroyImmediate(tex);

        string full = Path.Combine(Directory.GetCurrentDirectory(), relPath);
        Directory.CreateDirectory(Path.GetDirectoryName(full));
        File.WriteAllBytes(full, png);
    }

    static void VerifyPlacedPropBounds(Transform ws, StringBuilder sb)
    {
        string[] checkPaths =
        {
            "ResearchStation/Workstation",
            "DevStation/Workstation",
            "QAStation/Workstation",
            "TaskBoard/KanbanFrame_Visual",
            "TaskBoard/PreviewCard_Queued",
            "SoftwareFactory",
            "Phase6/InboxTray",
            "Phase6/OutboxTray",
            "Phase6/ProjectShelf",
            "Phase6/Decor_MiniPlant",
            "Phase6/Decor_DeskLamp",
            "Phase6/OfficeProps_Collection/SpatialDiagram_DeskLayout",
        };

        sb.AppendLine("\n== Placed Prop World Bounds Verification ==");
        foreach (var cp in checkPaths)
        {
            var t = ws.Find(cp);
            if (t == null) { sb.AppendLine($"  {cp}: MISSING"); continue; }
            var rs = t.GetComponentsInChildren<Renderer>(true).Where(r => r.enabled).ToArray();
            if (rs.Length == 0) { sb.AppendLine($"  {cp}: 0 enabled renderers"); continue; }
            Bounds b = rs[0].bounds;
            for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
            sb.AppendLine($"  {cp,-54} renderers={rs.Length,2} worldSize=({b.size.x:0.###}, {b.size.y:0.###}, {b.size.z:0.###})m");
        }
    }
}
