using System.Linq;
using System.Text;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using TMPro;
using MuseDesk.Agents;
using MuseDesk.Art;
using MuseDesk.Networking;
using MuseDesk.Workspace;

/// <summary>
/// Replaces the placeholder station cubes with the real Antigravity workstation FBX, aligning
/// each desk to its agent via the FBX's own AgentWorkPosition anchor, and mounts a lightweight
/// live monitor (WorkstationStatusDisplay) that mirrors the backend agent state. Adds a
/// TestingStationController driving the QA station's pass/fail lamps from real events. Idempotent.
/// </summary>
public static class BuildWorkstations
{
    const float S = 0.6f;   // workstation scale so a ~0.38m desk fits the 0.30m station spacing //TUNE

    struct Def { public string station, fbx, agentId, label; }
    static readonly Def[] Defs = new[]
    {
        new Def{ station="ResearchStation", fbx="Assets/MuseDesk/Art/Models/Workstations/Workstation_Research.fbx",  agentId="researcher", label="RESEARCH" },
        new Def{ station="DevStation",      fbx="Assets/MuseDesk/Art/Models/Workstations/Workstation_Developer.fbx", agentId="developer",  label="DEVELOP"  },
        new Def{ station="QAStation",       fbx="Assets/MuseDesk/Art/Models/Workstations/Workstation_Testing.fbx",   agentId="tester",     label="TEST"     },
    };

    public static string Run()
    {
        var scene = EditorSceneManager.OpenScene("Assets/MuseDesk/Scenes/MainMRScene.unity", OpenSceneMode.Single);
        var sb = new StringBuilder();
        var ws = GameObject.Find("WorkspaceRoot");
        if (ws == null) return "NO WorkspaceRoot";

        WorkstationVisualController qaVis = null;

        foreach (var d in Defs)
        {
            var st = ws.transform.Find(d.station);
            if (st == null) { sb.AppendLine(d.station + " MISSING"); continue; }

            // Remove placeholder props (keep agent, name tag, alert, drop zone).
            foreach (var n in new[] { "StationProp", "StationProp2", "Screen", "Workstation", "Monitor" })
            {
                var old = st.Find(n);
                if (old != null) Object.DestroyImmediate(old.gameObject);
            }

            var agent = st.GetComponentsInChildren<AgentView>(true).FirstOrDefault(a => a.agentId == d.agentId);
            Vector3 agentLocal = agent != null ? agent.transform.localPosition : Vector3.zero;

            // --- Workstation FBX ---
            var fbx = AssetDatabase.LoadMainAssetAtPath(d.fbx) as GameObject;
            if (fbx == null) { sb.AppendLine("NO FBX " + d.fbx); continue; }
            var deskGo = (GameObject)PrefabUtility.InstantiatePrefab(fbx);
            deskGo.name = "Workstation";
            deskGo.transform.SetParent(st, false);
            deskGo.transform.localScale = Vector3.one * S;
            deskGo.transform.localRotation = Quaternion.identity;

            var vis = deskGo.GetComponent<WorkstationVisualController>();
            if (vis == null) vis = deskGo.AddComponent<WorkstationVisualController>();
            vis.ResolveAnchorsAndParts();
            SetPrivateString(vis, "stationRole", d.agentId);

            // Align the desk so its AgentWorkPosition sits where the agent already stands
            // (keeps the validated agent position + home untouched).
            var workAnchor = FindChild(deskGo.transform, "AgentWorkPosition");
            if (workAnchor != null)
                deskGo.transform.localPosition = agentLocal - S * workAnchor.localPosition;
            else
                deskGo.transform.localPosition = agentLocal + new Vector3(0f, 0f, 0.09f); // fallback: in front

            if (d.station == "QAStation") qaVis = vis;

            // --- Live monitor ---
            BuildMonitor(st, agent, agentLocal, d.label, sb);

            EditorUtility.SetDirty(deskGo);
            sb.AppendLine($"{d.station}: desk placed (scale {S}), monitor wired to '{d.agentId}'.");
        }

        // --- Testing station lamp controller on AppServices ---
        var app = GameObject.Find("AppServices");
        var controller = Object.FindFirstObjectByType<SessionController>();
        if (app != null && qaVis != null)
        {
            var tsc = app.GetComponent<TestingStationController>();
            if (tsc == null) tsc = app.AddComponent<TestingStationController>();
            tsc.controller = controller;
            tsc.testingStation = qaVis;
            tsc.testerAgentId = "tester";
            EditorUtility.SetDirty(app);
            sb.AppendLine("TestingStationController wired (controller=" + (controller != null) + ").");
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        return sb.ToString();
    }

    static void BuildMonitor(Transform station, AgentView agent, Vector3 agentLocal, string label, StringBuilder sb)
    {
        var mon = new GameObject("Monitor");
        mon.transform.SetParent(station, false);
        // Above the agent's head, nudged toward the user side; FaceUser keeps it readable.
        mon.transform.localPosition = agentLocal + new Vector3(0f, 0.30f, 0.0f); //TUNE
        mon.AddComponent<FaceUser>();   // forward=true by default -> readable face at user

        // Dark backing quad.
        var back = GameObject.CreatePrimitive(PrimitiveType.Quad);
        back.name = "Back";
        Object.DestroyImmediate(back.GetComponent<Collider>());
        back.transform.SetParent(mon.transform, false);
        back.transform.localScale = new Vector3(0.14f, 0.07f, 1f); //TUNE
        var mr = back.GetComponent<Renderer>();
        var sh = Shader.Find("Universal Render Pipeline/Unlit");
        if (sh == null) sh = Shader.Find("Unlit/Color");
        var mat = new Material(sh); mat.color = new Color(0.08f, 0.09f, 0.12f, 1f);
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", mat.color);
        mr.sharedMaterial = mat;

        // Status text.
        var txtGo = new GameObject("Status");
        txtGo.transform.SetParent(mon.transform, false);
        txtGo.transform.localPosition = new Vector3(0f, 0f, -0.002f);
        txtGo.transform.localScale = Vector3.one * 0.008f; //TUNE
        var tmp = txtGo.AddComponent<TextMeshPro>();
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.fontSize = 6f;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        tmp.rectTransform.sizeDelta = new Vector2(16f, 8f);
        tmp.text = $"<b>{label}</b>\n<size=70%>Idle</size>";

        var disp = mon.AddComponent<WorkstationStatusDisplay>();
        disp.agent = agent;
        disp.stationLabel = label;
        disp.statusText = tmp;
    }

    static Transform FindChild(Transform root, string name)
    {
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
            if (t.name == name) return t;
        return null;
    }

    static void SetPrivateString(Object o, string field, string val)
    {
        var so = new SerializedObject(o);
        var p = so.FindProperty(field);
        if (p != null) { p.stringValue = val; so.ApplyModifiedPropertiesWithoutUndo(); }
    }
}
