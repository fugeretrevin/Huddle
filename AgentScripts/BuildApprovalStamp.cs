using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using TMPro;
using MuseDesk.Documents;

/// <summary>
/// Adds the real Doc_ApprovalStamp prop + a stamped mark to the DecisionDocument and wires an
/// ApprovalStampController driven by the backend decision.resolved event. Idempotent.
/// </summary>
public static class BuildApprovalStamp
{
    const string StampFbx = "Assets/MuseDesk/Art/Models/Documents/Doc_ApprovalStamp.fbx";

    public static string Build(bool preview = false)
    {
        var scene = EditorSceneManager.OpenScene("Assets/MuseDesk/Scenes/MainMRScene.unity", OpenSceneMode.Single);
        var ddc = Object.FindObjectsByType<DocumentDeliveryController>(FindObjectsInactive.Include).FirstOrDefault();
        if (ddc == null || ddc.paperRoot == null) return "NO DocumentDeliveryController/paperRoot";
        var doc = ddc.paperRoot.transform;

        // Remove any previous build (idempotent)
        var oldStamp = doc.Find("ApprovalStamp");
        if (oldStamp) Object.DestroyImmediate(oldStamp.gameObject);
        var oldMark = doc.Find("StampMark");
        if (oldMark) Object.DestroyImmediate(oldMark.gameObject);

        // --- Stamp prop ---
        var fbx = AssetDatabase.LoadMainAssetAtPath(StampFbx) as GameObject;
        if (fbx == null) return "NO stamp FBX";
        var stamp = (GameObject)PrefabUtility.InstantiatePrefab(fbx);
        stamp.name = "ApprovalStamp";
        stamp.transform.SetParent(doc, false);
        stamp.transform.localScale = Vector3.one * 3.0f;              // ~12cm on the big readable card  //TUNE
        stamp.transform.localPosition = new Vector3(0.085f, 0.055f, -0.05f); // upper-right, toward user   //TUNE
        stamp.transform.localRotation = Quaternion.Euler(10f, 0f, 0f);

        // --- Stamped mark (tilted APPROVED/REJECTED text) ---
        var markGo = new GameObject("StampMark");
        markGo.transform.SetParent(doc, false);
        markGo.transform.localPosition = new Vector3(0.02f, -0.01f, -0.03f); // centred, in front of card  //TUNE
        markGo.transform.localRotation = Quaternion.Euler(0f, 0f, -12f);
        markGo.transform.localScale = Vector3.one * 0.03f;                   //TUNE
        var mark = markGo.AddComponent<TextMeshPro>();
        mark.text = "APPROVED";
        mark.fontSize = 8f;
        mark.fontStyle = FontStyles.Bold;
        mark.alignment = TextAlignmentOptions.Center;
        mark.color = new Color(0.22f, 0.76f, 0.45f);
        mark.textWrappingMode = TextWrappingModes.NoWrap;
        var rt = mark.rectTransform; rt.sizeDelta = new Vector2(3f, 1f);

        // --- Controller ---
        var ctrl = doc.GetComponent<ApprovalStampController>();
        if (ctrl == null) ctrl = doc.gameObject.AddComponent<ApprovalStampController>();
        ctrl.controller = ddc.controller;
        ctrl.stamp = stamp.transform;
        ctrl.mark = mark;
        ctrl.pressDepth = 0.06f;

        if (preview)
        {
            stamp.SetActive(true);
            markGo.SetActive(true);
            return "PREVIEW placed (not saved)";
        }

        // Shipping state: hidden until a decision resolves.
        stamp.SetActive(false);
        markGo.SetActive(false);
        EditorUtility.SetDirty(ctrl);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        return "ApprovalStamp wired + saved (hidden until resolved).";
    }

    public static string Preview() => Build(true);
}
