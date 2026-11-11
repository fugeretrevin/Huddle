using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Pass 4: arms to shoulder height, delivering-agent faces user, paper grab re-register,
/// rubber-band tuning (less early/jittery), bigger on-screen grab readout.
/// </summary>
public static class FixPass4
{
    static GameObject[] _all;
    static Type T(string n) => AppDomain.CurrentDomain.GetAssemblies()
        .SelectMany(a => { try { return a.GetTypes(); } catch { return Type.EmptyTypes; } })
        .FirstOrDefault(t => t.Name == n);
    static GameObject Find(string n) => _all.FirstOrDefault(g => g.name == n);

    public static string Run()
    {
        var scene = EditorSceneManager.GetActiveScene();
        _all = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true))
            .Select(t => t.gameObject).ToArray();
        var sb = new StringBuilder();

        Sect(sb, "ARMS", Arms);
        Sect(sb, "FACE_DELIVER", FaceDeliver);
        Sect(sb, "RUBBERBAND_TUNE", RubberTune);
        Sect(sb, "PAPER_REREGISTER", PaperReRegister);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        sb.AppendLine("SAVED.");
        return sb.ToString();
    }

    static void Sect(StringBuilder sb, string n, Func<string> f)
    { try { sb.AppendLine($"[{n}] " + f()); } catch (Exception e) { sb.AppendLine($"[{n}] ERROR {e.Message}"); } }

    static string Arms()
    {
        int n = 0;
        foreach (var agent in new[] { "Agent_Researcher", "Agent_Developer", "Agent_Tester" })
        {
            var a = Find(agent); if (a == null) continue;
            foreach (var arm in a.GetComponentsInChildren<Transform>(true).Where(t => t.name == "Arm_L" || t.name == "Arm_R"))
            {
                float x = arm.name == "Arm_L" ? -0.036f : 0.036f;
                arm.localPosition = new Vector3(x, 0.082f, 0.005f);   // up at the shoulders
                n++;
            }
        }
        return $"raised {n} arms to shoulder height (y 0.055->0.082)";
    }

    static string FaceDeliver()
    {
        var fType = T("FaceUserWhileDelivering");
        var amType = T("AgentMovement");
        if (fType == null) return "FaceUserWhileDelivering type missing";
        int n = 0;
        foreach (var agent in new[] { "Agent_Researcher", "Agent_Developer", "Agent_Tester" })
        {
            var a = Find(agent); if (a == null) continue;
            var comp = a.GetComponent(fType); if (comp == null) comp = a.AddComponent(fType);
            var mv = amType != null ? a.GetComponent(amType) : null;
            var f = fType.GetField("movement", BindingFlags.Instance | BindingFlags.Public);
            if (f != null && mv != null) f.SetValue(comp, mv);
            n++;
        }
        return $"added delivery-facing to {n} agents";
    }

    static string RubberTune()
    {
        var rbrType = T("RubberBandReturn");
        int n = 0;
        foreach (var agent in new[] { "Agent_Researcher", "Agent_Developer", "Agent_Tester" })
        {
            var a = Find(agent); if (a == null) continue;
            var sc = a.GetComponents<SphereCollider>().FirstOrDefault(c => c.isTrigger);
            if (sc != null) { sc.radius = 0.055f; sc.center = new Vector3(0f, 0.07f, 0f); }
            if (rbrType != null)
            {
                var comp = a.GetComponent(rbrType);
                if (comp != null)
                {
                    SetF(rbrType, comp, "maxOffset", 0.04f);
                    SetF(rbrType, comp, "pushStrength", 0.35f);
                    SetF(rbrType, comp, "springback", 8f);
                }
            }
            n++;
        }
        return $"tuned rubber-band on {n} agents (tighter zone, hold-then-return)";
    }

    static string PaperReRegister()
    {
        var paper = Find("DecisionDocument");
        if (paper == null) return "paper not found";
        var rrType = T("ReRegisterInteractable");
        if (rrType == null) return "ReRegisterInteractable type missing";
        var hgi = paper.GetComponents<Component>().FirstOrDefault(c => c != null && c.GetType().Name == "HandGrabInteractable") as Behaviour;
        var grab = paper.GetComponents<Component>().FirstOrDefault(c => c != null && c.GetType().Name == "Grabbable") as Behaviour;
        var list = new List<Behaviour>();
        if (hgi != null) list.Add(hgi);
        if (grab != null) list.Add(grab);
        var comp = paper.GetComponent(rrType); if (comp == null) comp = paper.AddComponent(rrType);
        var f = rrType.GetField("interactables", BindingFlags.Instance | BindingFlags.Public);
        if (f != null) f.SetValue(comp, list.ToArray());
        return $"added ReRegisterInteractable wired to {list.Count} components";
    }

    static string ReadoutFont()
    {
        var dt = Find("DiagnosticsText");
        if (dt == null) return "DiagnosticsText not found";
        var tmp = dt.GetComponents<Component>().FirstOrDefault(c => c != null && c.GetType().Name.StartsWith("TextMeshPro"));
        if (tmp == null) return "no TMP";
        var ty = tmp.GetType();
        var f = ty.GetField("m_fontSize", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy);
        if (f != null) f.SetValue(tmp, 0.9f);   // DiagnosticsText scale is 0.02 -> readable but not huge
        var auto = ty.GetProperty("enableAutoSizing"); if (auto != null) auto.SetValue(tmp, false);
        EditorUtility.SetDirty(tmp);
        return "enlarged diagnostic readout font";
    }

    static void SetF(Type ty, object o, string field, float val)
    {
        var f = ty.GetField(field, BindingFlags.Instance | BindingFlags.Public);
        if (f != null) f.SetValue(o, val);
    }
}
