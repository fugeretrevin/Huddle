using System.Linq;
using System.Text;
using MuseDesk.Agents;
using MuseDesk.Networking;
using MuseDesk.Phase2;
using MuseDesk.Workspace;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

public static class BuildAgentTouchSummary
{
    public static string Run()
    {
        var sb = new StringBuilder();
        var scene = EditorSceneManager.GetActiveScene();
        var wsRoot = GameObject.Find("WorkspaceRoot")?.transform;
        if (wsRoot == null) throw new System.Exception("WorkspaceRoot not found.");

        var controller = Object.FindAnyObjectByType<SessionController>(FindObjectsInactive.Include);
        var board = Object.FindAnyObjectByType<Phase2Bootstrap>(FindObjectsInactive.Include);
        var plateMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/MuseDesk/Art/Materials/Mat_CharcoalVisor.mat");

        var targets = new (string station, string agent)[]
        {
            ("ResearchStation", "Agent_Researcher"),
            ("DevStation",      "Agent_Developer"),
            ("QAStation",       "Agent_Tester"),
        };

        foreach (var (stationName, agentName) in targets)
        {
            var agent = wsRoot.Find($"{stationName}/{agentName}");
            if (agent == null)
            {
                sb.AppendLine($"SKIP {agentName}: not found");
                continue;
            }

            var view = agent.GetComponent<AgentView>();
            var head = agent.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "Head");
            var nameTag = agent.Find("NameTag");
            var nameTmp = nameTag != null ? nameTag.GetComponent<TMP_Text>() : null;
            var nameFace = nameTag != null ? nameTag.GetComponent<FaceUser>() : null;

            // Create or reuse TouchSummaryCard idempotently
            var cardT = agent.Find("TouchSummaryCard");
            GameObject cardGo;
            bool createdCard = false;
            if (cardT == null)
            {
                cardGo = new GameObject("TouchSummaryCard");
                cardT = cardGo.transform;
                cardT.SetParent(agent, false);
                createdCard = true;
            }
            else
            {
                cardGo = cardT.gameObject;
            }

            cardT.localPosition = new Vector3(0f, 0.255f, 0.045f);
            cardT.localRotation = Quaternion.identity;
            cardT.localScale = Vector3.one;

            var face = cardGo.GetComponent<FaceUser>() ?? cardGo.AddComponent<FaceUser>();
            face.forward = nameFace != null ? nameFace.forward : false;
            face.turnSpeed = 10f;

            // Backing plate facing -Z
            var plateT = cardT.Find("Plate");
            if (plateT == null)
            {
                var plate = GameObject.CreatePrimitive(PrimitiveType.Quad);
                plate.name = "Plate";
                var col = plate.GetComponent<Collider>();
                if (col != null) Object.DestroyImmediate(col);
                plateT = plate.transform;
                plateT.SetParent(cardT, false);
            }
            plateT.localPosition = new Vector3(0f, 0f, 0.002f);
            plateT.localRotation = Quaternion.identity;
            plateT.localScale = new Vector3(0.175f, 0.068f, 1f);
            var mr = plateT.GetComponent<MeshRenderer>();
            if (mr != null)
            {
                if (plateMat != null) mr.sharedMaterial = plateMat;
                mr.shadowCastingMode = ShadowCastingMode.Off;
                mr.receiveShadows = false;
            }

            // TMP readout facing -Z
            var textT = cardT.Find("Text");
            if (textT == null)
            {
                var textGo = new GameObject("Text");
                textT = textGo.transform;
                textT.SetParent(cardT, false);
            }
            textT.localPosition = new Vector3(0f, 0f, -0.002f);
            textT.localRotation = Quaternion.identity;
            textT.localScale = Vector3.one * 0.01f;

            var tmp = textT.GetComponent<TextMeshPro>() ?? textT.gameObject.AddComponent<TextMeshPro>();
            if (nameTmp != null && nameTmp.font != null) tmp.font = nameTmp.font;
            tmp.rectTransform.sizeDelta = new Vector2(16.0f, 5.8f);
            tmp.enableAutoSizing = true;
            tmp.fontSizeMin = 0.75f;
            tmp.fontSizeMax = 1.45f;
            tmp.fontSize = 1.35f;
            tmp.textWrappingMode = TextWrappingModes.Normal;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = new Color(0.95f, 0.96f, 0.98f, 1f);
            tmp.text = agentName;

            // Hidden by default; AgentTouchSummary activates on dwell/linger
            cardGo.SetActive(false);

            var summary = agent.GetComponent<AgentTouchSummary>() ?? agent.gameObject.AddComponent<AgentTouchSummary>();
            summary.view = view;
            summary.controller = controller;
            summary.board = board;
            summary.touchPoint = head != null ? head : agent;
            summary.cardRoot = cardGo;
            summary.cardText = tmp;
            EditorUtility.SetDirty(summary);
            EditorUtility.SetDirty(cardGo);

            sb.AppendLine($"{agentName}: createdCard={createdCard} touchPoint={summary.touchPoint.name} controller={(controller != null)} board={(board != null)}");
        }

        EditorSceneManager.MarkSceneDirty(scene);
        bool saved = EditorSceneManager.SaveScene(scene);
        sb.AppendLine($"scene saved={saved}");
        return sb.ToString();
    }
}
