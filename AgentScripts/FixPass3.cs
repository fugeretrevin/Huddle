using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// On-device feedback pass 3. Batches: eyes, button text, desk/floor collider, prop weight,
/// prop sticky-grab, agent rubber-band collision, paper grab widen + live grab readout.
/// Per-section try/catch so one failure doesn't abort the rest.
/// </summary>
public static class FixPass3
{
    static GameObject[] _all;
    static Type T(string n) => AppDomain.CurrentDomain.GetAssemblies()
        .SelectMany(a => { try { return a.GetTypes(); } catch { return Type.EmptyTypes; } })
        .FirstOrDefault(t => t.Name == n);

    public static string Run()
    {
        var scene = EditorSceneManager.GetActiveScene();
        _all = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true))
            .Select(t => t.gameObject).ToList().ToArray();
        var sb = new StringBuilder();

        Sect(sb, "EYES", Eyes);
        Sect(sb, "BUTTONS", Buttons);
        Sect(sb, "DESK", Desk);
        Sect(sb, "PROP_WEIGHT", PropWeight);
        Sect(sb, "PROP_GRAB", PropGrab);
        Sect(sb, "AGENT_RUBBERBAND", AgentRubberband);
        Sect(sb, "PAPER_GRAB", PaperGrab);

        return Save(scene, sb);
    }

    /// <summary>Re-run only the rubber-band section (the rest already applied). Idempotent.</summary>
    public static string RunRubberband()
    {
        var scene = EditorSceneManager.GetActiveScene();
        _all = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true))
            .Select(t => t.gameObject).ToList().ToArray();
        var sb = new StringBuilder();
        Sect(sb, "AGENT_RUBBERBAND", AgentRubberband);
        return Save(scene, sb);
    }

    static string Save(UnityEngine.SceneManagement.Scene scene, StringBuilder sb)
    {
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        sb.AppendLine("SAVED scene.");
        return sb.ToString();
    }

    static void Sect(StringBuilder sb, string name, Func<string> f)
    {
        try { sb.AppendLine($"[{name}] " + f()); }
        catch (Exception e) { sb.AppendLine($"[{name}] ERROR: {e.Message}"); }
    }

    static GameObject Find(string name) => _all.FirstOrDefault(g => g.name == name);
    static IEnumerable<GameObject> FindAll(string name) => _all.Where(g => g.name == name);

    // ---- A. Eyes: pull them back onto the head surface ----
    static string Eyes()
    {
        int n = 0;
        foreach (var agent in new[] { "Agent_Researcher", "Agent_Developer", "Agent_Tester" })
        {
            var a = Find(agent);
            if (a == null) continue;
            foreach (var eye in a.GetComponentsInChildren<Transform>(true).Where(t => t.name == "Eye_L" || t.name == "Eye_R"))
            {
                float x = eye.name == "Eye_L" ? -0.22f : 0.22f;
                eye.localPosition = new Vector3(x, 0.12f, 0.36f);
                eye.localScale = new Vector3(0.30f, 0.30f, 0.30f);
                n++;
            }
        }
        return $"repositioned {n} eyes onto the head surface (z 0.82->0.36)";
    }

    // ---- B. Button labels: bigger + legible ----
    static string Buttons()
    {
        int n = 0;
        foreach (var btn in new[] { "Btn_Approve", "Btn_Reject", "Btn_Ask_why", "Btn_Details" })
        {
            foreach (var b in FindAll(btn))
            {
                // enlarge the whole button a touch
                b.transform.localScale *= 1.35f;
                var label = b.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "Label");
                if (label == null) continue;
                var tmp = label.GetComponents<Component>().FirstOrDefault(c => c != null && c.GetType().Name.StartsWith("TextMeshPro"));
                if (tmp == null) continue;
                var ty = tmp.GetType();
                Set(ty, tmp, "m_fontSize", 13f);
                SetProp(ty, tmp, "enableAutoSizing", false);
                SetProp(ty, tmp, "enableWordWrapping", false);
                // overflowMode = Overflow (0)
                var om = ty.GetProperty("overflowMode");
                if (om != null) { try { om.SetValue(tmp, Enum.ToObject(om.PropertyType, 0)); } catch { } }
                EditorUtility.SetDirty(tmp);
                n++;
            }
        }
        return $"enlarged {n} button labels (font 13, buttons x1.35)";
    }

    // ---- C. Desk/floor collider: props rest, hands hit the desk, nothing falls forever ----
    static string Desk()
    {
        var ws = Find("WorkspaceRoot");
        var old = Find("DeskSurface");
        if (old != null) UnityEngine.Object.DestroyImmediate(old);
        var go = new GameObject("DeskSurface");
        if (ws != null) go.transform.SetParent(ws.transform, false);
        // top at ~y=1.00 world (station/desk base), wide, invisible.
        go.transform.position = new Vector3(0f, 0.98f, 0.45f);
        var bc = go.AddComponent<BoxCollider>();
        bc.size = new Vector3(2.2f, 0.04f, 2.2f);
        return $"added invisible DeskSurface collider (top ~y1.00) under {(ws ? ws.name : "scene")}";
    }

    // ---- D. Prop weight: resist pushing, settle instead of sliding forever ----
    static string PropWeight()
    {
        int n = 0;
        foreach (var name in new[] { "Prop_Block", "Prop_Mug" })
        {
            var p = Find(name);
            var rb = p ? p.GetComponent<Rigidbody>() : null;
            if (rb == null) continue;
            rb.mass = name == "Prop_Mug" ? 0.55f : 0.45f;
            rb.linearDamping = 0.6f;
            rb.angularDamping = 0.9f;
            rb.maxLinearVelocity = 2.5f;
            n++;
        }
        return $"tuned {n} props (heavier mass + damping + velocity clamp)";
    }

    // ---- E. Prop sticky-grab: copy the cube's Grabbable + HandGrabInteractable onto each prop ----
    static string PropGrab()
    {
        var cube = Find("[BuildingBlock] Grabbable Item");
        if (cube == null) return "cube source not found";
        var srcGrab = cube.GetComponents<Component>().FirstOrDefault(c => c != null && c.GetType().Name == "Grabbable");
        var srcHgi = cube.GetComponents<Component>().FirstOrDefault(c => c != null && c.GetType().Name == "HandGrabInteractable");
        if (srcGrab == null || srcHgi == null) return "cube grab components missing";
        var grbType = T("GrabbableRigidbody");

        int n = 0;
        foreach (var name in new[] { "Prop_Block", "Prop_Mug" })
        {
            var p = Find(name);
            if (p == null) continue;
            // already grabbable? skip
            if (p.GetComponents<Component>().Any(c => c != null && c.GetType().Name == "HandGrabInteractable")) continue;

            // Add a TRIGGER collider for the grab volume (physics uses the existing solid collider).
            var trig = p.AddComponent<BoxCollider>();
            trig.isTrigger = true;
            trig.size = Vector3.one * 1.15f;  // local; a bit larger than the prop for easy grabbing

            UnityEditorInternal.ComponentUtility.CopyComponent(srcGrab);
            UnityEditorInternal.ComponentUtility.PasteComponentAsNew(p);
            UnityEditorInternal.ComponentUtility.CopyComponent(srcHgi);
            UnityEditorInternal.ComponentUtility.PasteComponentAsNew(p);

            var newGrab = p.GetComponents<Component>().Last(c => c != null && c.GetType().Name == "Grabbable");
            var newHgi = p.GetComponents<Component>().Last(c => c != null && c.GetType().Name == "HandGrabInteractable");
            var rb = p.GetComponent<Rigidbody>();

            Repoint(newGrab, "_rigidbody", rb);
            Repoint(newHgi, "_pointableElement", newGrab);
            Repoint(newHgi, "<PointableElement>k__BackingField", null);
            Repoint(newHgi, "_rigidbody", rb);

            if (grbType != null)
            {
                var grb = p.GetComponent(grbType) ?? p.AddComponent(grbType);
                var f = grbType.GetField("grabbable", BindingFlags.Instance | BindingFlags.Public);
                if (f != null) f.SetValue(grb, newGrab);
            }
            n++;
        }
        return $"made {n} props grabbable (pinch/cup) + kinematic-on-hold";
    }

    // ---- F. Agent rubber-band: soft push + springback, yields to walk ----
    static string AgentRubberband()
    {
        var rbrType = T("RubberBandReturn");
        var amType = T("AgentMovement");
        if (rbrType == null) return "RubberBandReturn type not found";
        int n = 0;
        foreach (var agent in new[] { "Agent_Researcher", "Agent_Developer", "Agent_Tester" })
        {
            var a = Find(agent);
            if (a == null) continue;
            // kinematic rigidbody so trigger events fire against the solid hand capsules
            var rb = a.GetComponent<Rigidbody>();
            if (rb == null) rb = a.AddComponent<Rigidbody>();
            rb.isKinematic = true; rb.useGravity = false;
            // trigger sphere around the agent
            var sc = a.GetComponents<SphereCollider>().FirstOrDefault(c => c.isTrigger);
            if (sc == null) sc = a.AddComponent<SphereCollider>();
            sc.isTrigger = true; sc.radius = 0.09f; sc.center = new Vector3(0f, 0.08f, 0f);
            var comp = a.GetComponent(rbrType);
            if (comp == null) comp = a.AddComponent(rbrType);
            if (amType != null)
            {
                var mv = a.GetComponent(amType);
                var f = rbrType.GetField("movement", BindingFlags.Instance | BindingFlags.Public);
                if (f != null && mv != null) f.SetValue(comp, mv);
            }
            n++;
        }
        return $"added rubber-band collision to {n} agents";
    }

    // ---- G. Paper grab widen + live readout ----
    static string PaperGrab()
    {
        var sb = new StringBuilder();
        var paper = Find("DecisionDocument");
        if (paper != null)
        {
            var bc = paper.GetComponent<BoxCollider>();
            if (bc != null)
            {
                bc.isTrigger = true;
                bc.size = new Vector3(0.38f, 0.30f, 0.12f);  // a bit fatter -> easier to grab
                sb.Append("widened paper collider; ");
            }
            // faster lock so the placer stops fighting sooner
            var placer = paper.GetComponents<Component>().FirstOrDefault(c => c != null && c.GetType().Name == "PlaceInFrontOnStart");
            if (placer != null)
            {
                var f = placer.GetType().GetField("followSeconds", BindingFlags.Instance | BindingFlags.Public);
                if (f != null) f.SetValue(placer, 1.2f);
            }
        }
        // live grab readout on DiagnosticsText
        var dt = Find("DiagnosticsText");
        var grType = T("GrabReadout");
        if (dt != null && grType != null)
        {
            var tmp = dt.GetComponents<Component>().FirstOrDefault(c => c != null && c.GetType().Name.StartsWith("TextMeshPro"));
            var comp = dt.GetComponent(grType) ?? dt.AddComponent(grType);
            var f = grType.GetField("target", BindingFlags.Instance | BindingFlags.Public);
            if (f != null && tmp != null) f.SetValue(comp, tmp);
            sb.Append("added live GrabReadout to DiagnosticsText");
        }
        return sb.ToString();
    }

    // ---- helpers ----
    static void Set(Type ty, object o, string field, object val)
    {
        var f = ty.GetField(field, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy);
        if (f != null) f.SetValue(o, Convert.ChangeType(val, f.FieldType));
    }
    static void SetProp(Type ty, object o, string prop, object val)
    {
        var p = ty.GetProperty(prop);
        if (p != null && p.CanWrite) { try { p.SetValue(o, val); } catch { } }
    }
    static FieldInfo FindField(Type t, string name)
    {
        for (var c = t; c != null; c = c.BaseType)
        {
            var f = c.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (f != null) return f;
        }
        return null;
    }
    static void Repoint(Component c, string field, object val)
    {
        var f = FindField(c.GetType(), field);
        if (f != null) f.SetValue(c, val);
    }
}
