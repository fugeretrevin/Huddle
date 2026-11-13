using System.Collections.Generic;
using System.Linq;
using System.Text;
using MuseDesk.Workspace;
using Oculus.Interaction;
using Oculus.Interaction.HandGrab;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class BuildDistanceGrab
{
    static GameObject FindInScene(string hierarchyPath)
    {
        var scene = EditorSceneManager.GetActiveScene();
        string[] parts = hierarchyPath.Split('/');
        foreach (var root in scene.GetRootGameObjects())
        {
            if (root.name != parts[0]) continue;
            Transform cur = root.transform;
            for (int i = 1; i < parts.Length && cur != null; i++)
                cur = cur.Find(parts[i]);
            if (cur != null) return cur.gameObject;
        }
        return null;
    }

    static string WireTarget(GameObject go)
    {
        if (go == null) return "MISSING GameObject";
        var hgi = go.GetComponent<HandGrabInteractable>();
        if (hgi == null) return $"{go.name}: MISSING HandGrabInteractable";

        var dgi = go.GetComponent<DistanceHandGrabInteractable>();
        bool added = false;
        if (dgi == null)
        {
            dgi = go.AddComponent<DistanceHandGrabInteractable>();
            added = true;
        }

        var rb = hgi.Rigidbody != null ? hgi.Rigidbody : go.GetComponent<Rigidbody>();
        var pointable = hgi.PointableElement != null ? hgi.PointableElement : go.GetComponent<Grabbable>();
        dgi.InjectAllDistanceHandGrabInteractable(hgi.SupportedGrabTypes, rb, hgi.PinchGrabRules, hgi.PalmGrabRules);
        if (hgi.HandGrabPoses != null && hgi.HandGrabPoses.Count > 0)
            dgi.InjectOptionalHandGrabPoses(new List<HandGrabPose>(hgi.HandGrabPoses));
        if (pointable != null)
            dgi.InjectOptionalPointableElement(pointable);

        EditorUtility.SetDirty(dgi);

        // Append cleanly to ReRegisterInteractable.interactables without duplicating
        var rereg = go.GetComponent<ReRegisterInteractable>();
        bool reregAdded = false;
        if (rereg != null)
        {
            var list = rereg.interactables != null ? rereg.interactables.ToList() : new List<Behaviour>();
            if (!list.Contains(hgi)) list.Add(hgi);
            if (!list.Contains(dgi))
            {
                list.Add(dgi);
                reregAdded = true;
            }
            rereg.interactables = list.ToArray();
            EditorUtility.SetDirty(rereg);
        }

        return $"{go.name}: added={added} rb={(rb != null ? rb.name : "null")} pointable={(pointable != null ? pointable.GetType().Name : "null")} reregAppended={reregAdded} reregCount={(rereg?.interactables?.Length ?? 0)}";
    }

    public static string Run()
    {
        var sb = new StringBuilder();
        sb.AppendLine(WireTarget(FindInScene("WorkspaceRoot/TaskBoard/CardTemplate")));
        sb.AppendLine(WireTarget(FindInScene("DecisionDocument")));

        var scene = EditorSceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(scene);
        bool saved = EditorSceneManager.SaveScene(scene);
        sb.AppendLine($"scene saved={saved}");
        return sb.ToString();
    }
}
