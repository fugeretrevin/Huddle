using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using MuseDesk.Agents;

/// <summary>
/// Desk choreography layout: the three desks are spread so there is a walkable aisle between the Researcher's desk and
/// the Developer's, and every agent walk is an authored lane that keeps the agent's body clear of the furniture.
///
///   - ResearchStation, DevStation, QAStation and their nameplates move sideways (x only). Everything stays inside the
///     validated office footprint (1.0 x 0.7 m), so the Floor and Ceiling movement checks sweep the same volume as before.
///   - The Researcher walks out behind its desk, through the aisle, and offers the paper at the aisle mouth
///     (DeliveryZone), instead of going round the outside of the desk and standing half inside it.
///   - Handoffs get authored lanes on AgentHandoffController (stand point + waypoints): Researcher to Developer from
///     home and from the delivery spot, Developer to Tester.
///   - The book stack, the plant and the lamp leave the lanes and the widened desks.
///
/// Every position is data written into the scene here (waypoint transforms under WorkspaceRoot/DeskLanes); the runtime
/// scripts hold no positions. Numbers are WorkspaceRoot-local metres: +z toward the user, -x the user's right.
///
///   scripts\run_guarded.ps1 -File AgentScripts\DeskChoreographyLayoutV1.cs -Entry DeskChoreographyLayoutV1.Apply
///   scripts\run_guarded.ps1 -File AgentScripts\DeskChoreographyLayoutV1.cs -Entry DeskChoreographyLayoutV1.Revert
///
/// Idempotent. The first Apply writes the old values to AgentScripts/DeskChoreographyLayoutV1.record.txt (full float
/// precision); later Applies place everything from that record, and Revert puts every recorded value back exactly and
/// removes DeskLanes. Verify with scripts\drv.ps1 DeskClearanceDrive Report -Max 6000 (Edit or Play Mode).
/// </summary>
public static class DeskChoreographyLayoutV1
{
    const string LanesName = "DeskLanes";
    const string RecordPath = "AgentScripts/DeskChoreographyLayoutV1.record.txt";

    // ── the layout (data) ──────────────────────────────────────────────────────────────────────────────────────────
    // Shifts (metres along root x and z). Desk extents after the shift: Researcher -0.500..-0.224, aisle 0.200 wide,
    // Developer -0.024..0.226, Tester 0.239..0.489. The Researcher's station also comes 95 mm toward the user
    // (V1 25 mm, V2 another 70 mm): that widens the strip behind its desk to 0.194 m, so the lane behind it clears the desk
    // here AND the back rail of the Floor platform and the arch post of the Ceiling pod (which stand behind the office in
    // those modes). The Inbox tray and the decor cluster in front of the desk move with it.
    static readonly (string name, float dx, float dz)[] Shifts =
    {
        ("ResearchStation", -0.066f, 0.095f), ("Nameplate_Researcher", -0.066f, 0.095f),
        ("DevStation", 0.122f, 0f),           ("Nameplate_Developer", 0.122f, 0f),
        ("QAStation", 0.085f, 0f),            ("Nameplate_Tester", 0.085f, 0f),
        ("InboxTray", 0f, 0.070f),                // V2: moves with the Researcher's desk so the desk does not stand in the tray
    };

    // V2: objects that older records (V1) do not hold. They were not touched by V1, so on an applied scene their CURRENT
    // position is their old position; Apply records it once, before the first move.
    static readonly string[] V2Added = { "InboxTray" };

    // Objects placed at a root-local (x, z); their height and rotation are kept.
    static readonly (string name, float x, float z)[] Places =
    {
        ("DeliveryZone", -0.124f, 0.120f),       // the Researcher offers the paper at the aisle mouth, behind the factory line
        ("Decor_BookStack", -0.432f, 0.282f),    // a small cluster beside the Inbox tray, in front of the Researcher's desk (V2: +70 mm with the desk)
        ("Decor_MiniPlant", -0.462f, 0.332f),
        ("Decor_DeskLamp", -0.405f, 0.345f),     // (its old place at the Tester's end is now under the widened desk)
    };

    // Lane points (new transforms under WorkspaceRoot/DeskLanes), root-local (x, z).
    static readonly (string name, float x, float z)[] Points =
    {
        ("Res_Exit_1", -0.290f, 0.025f),             // a step back and aside, clear of the back of its own desk (a 45 degree corner: no stop)
        ("Res_Exit_2", -0.210f, 0.028f),             // on the strip behind the desk, centred between the desk back and the Ceiling pod's arch post (V2: no stop, two 45 degree corners)
        ("ResToDev_FromHome_2", -0.150f, 0.034f),   // keeps the walk to the Developer on the same strip, past the arch post (two gentle corners, no stop)
        ("ResToDev_FromDelivery_1", -0.122f, 0.050f),// back through the aisle after an approval (a 39 degree corner: no stop)
        ("ResToDev_Stand", -0.070f, -0.005f),        // beside the Developer, 0.19 m from it (V2: a little nearer the user than V1, clear of the arch post)
        ("DevToTester_Stand", 0.185f, -0.045f),      // beside the Tester, 0.21 m from it
    };

    const float FromDeliverySeconds = 1.1f;          // the short walk back through the aisle after an approval

    // ── record ────────────────────────────────────────────────────────────────────────────────────────────────────
    // A plain text file, one value per line, floats in round-trip form:
    //   pos|<object name>|<local x>|<local y>|<local z>
    //   exit|<waypoint name>,<waypoint name>
    //   speed|<maxRoutedSpeed>
    class Item { public string name; public float x, y, z; }
    class Record
    {
        public List<Item> positions = new List<Item>();
        public List<string> exitWaypoints = new List<string>();
        public float maxRoutedSpeed = 0.25f;
    }

    static string RecordFile => Path.Combine(Directory.GetParent(Application.dataPath).FullName, RecordPath);

    static string R(float v) => v.ToString("R", CultureInfo.InvariantCulture);

    static void SaveRecord(Record rec)
    {
        var sb = new StringBuilder();
        sb.Append("# Old values recorded by DeskChoreographyLayoutV1.Apply; Revert restores them. Local positions, metres.\n");
        foreach (var it in rec.positions) sb.Append("pos|" + it.name + "|" + R(it.x) + "|" + R(it.y) + "|" + R(it.z) + "\n");
        sb.Append("exit|" + string.Join(",", rec.exitWaypoints) + "\n");
        sb.Append("speed|" + R(rec.maxRoutedSpeed) + "\n");
        File.WriteAllText(RecordFile, sb.ToString());
    }

    static Record LoadRecord()
    {
        if (!File.Exists(RecordFile)) return null;
        var rec = new Record();
        foreach (var raw in File.ReadAllLines(RecordFile))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line[0] == '#') continue;
            var f = line.Split('|');
            if (f[0] == "pos" && f.Length == 5)
                rec.positions.Add(new Item { name = f[1], x = float.Parse(f[2], CultureInfo.InvariantCulture), y = float.Parse(f[3], CultureInfo.InvariantCulture), z = float.Parse(f[4], CultureInfo.InvariantCulture) });
            else if (f[0] == "exit" && f.Length == 2)
                rec.exitWaypoints.AddRange(f[1].Split(',').Where(s => s.Length > 0));
            else if (f[0] == "speed" && f.Length == 2)
                rec.maxRoutedSpeed = float.Parse(f[1], CultureInfo.InvariantCulture);
        }
        return rec;
    }

    // ── scene helpers ─────────────────────────────────────────────────────────────────────────────────────────────
    static Transform Root() =>
        EditorSceneManager.GetActiveScene().GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true)).FirstOrDefault(t => t.name == "WorkspaceRoot");

    static Transform Under(Transform root, string name, StringBuilder sb)
    {
        var hits = root.GetComponentsInChildren<Transform>(true).Where(t => t != root && t.name == name).ToArray();
        if (hits.Length == 0) { sb.AppendLine("  MISSING under WorkspaceRoot: " + name); return null; }
        if (hits.Length > 1)
        {
            var active = hits.Where(t => t.gameObject.activeInHierarchy).ToArray();
            if (active.Length == 1) return active[0];
            sb.AppendLine("  AMBIGUOUS (" + hits.Length + " objects named " + name + "): the first is used");
        }
        return hits[0];
    }

    static void SetLocal(Transform t, Vector3 local)
    {
        Undo.RecordObject(t, "desk choreography layout");
        t.localPosition = local;
        EditorUtility.SetDirty(t);
        if (PrefabUtility.IsPartOfPrefabInstance(t)) PrefabUtility.RecordPrefabInstancePropertyModifications(t);
    }

    static string F(float v) => v.ToString("F3", CultureInfo.InvariantCulture);
    static string P(Vector3 v) => "(" + F(v.x) + ", " + F(v.y) + ", " + F(v.z) + ")";

    static AgentMovement ResearcherMove(Transform root) =>
        root.GetComponentsInChildren<AgentView>(true).Where(v => v.agentId == "researcher" && v.gameObject.activeInHierarchy)
            .Select(v => v.GetComponent<AgentMovement>()).FirstOrDefault(m => m != null);

    // ── Apply ─────────────────────────────────────────────────────────────────────────────────────────────────────
    public static string Apply()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return "DeskChoreographyLayoutV1: stop Play Mode first.\n";
        var scene = EditorSceneManager.GetActiveScene();
        var root = Root();
        if (root == null) return "DeskChoreographyLayoutV1: no WorkspaceRoot in " + scene.path + "; nothing changed.\n";
        var sb = new StringBuilder();
        sb.AppendLine("== DeskChoreographyLayoutV1.Apply ==");

        var mv = ResearcherMove(root);
        var handoff = UnityEngine.Object.FindObjectsByType<AgentHandoffController>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault();
        var registry = UnityEngine.Object.FindObjectsByType<AgentRegistry>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault();
        if (mv == null || handoff == null || registry == null)
            return sb.Append("  Researcher AgentMovement=" + (mv != null) + " AgentHandoffController=" + (handoff != null) + " AgentRegistry=" + (registry != null) + ": something is missing; nothing changed.\n").ToString();

        // Resolve every object first: nothing is moved unless all of them are there.
        var targets = new Dictionary<string, Transform>();
        foreach (var n in Shifts.Select(s => s.name).Concat(Places.Select(p => p.name)))
        {
            var t = Under(root, n, sb);
            if (t == null) return sb.Append("  nothing changed.\n").ToString();
            targets[n] = t;
        }
        if (registry.deliveryAnchor != targets["DeliveryZone"])
            sb.AppendLine("  NOTE: AgentRegistry.deliveryAnchor is " + (registry.deliveryAnchor != null ? registry.deliveryAnchor.name : "null") + ", not DeliveryZone; it is pointed at DeliveryZone");

        // The record: written from the scene when the layout is not applied yet, otherwise read back.
        var lanes = root.Find(LanesName);
        Record rec = LoadRecord();
        if (lanes == null)
        {
            rec = new Record();
            foreach (var kv in targets)
            {
                var lp = kv.Value.localPosition;
                rec.positions.Add(new Item { name = kv.Key, x = lp.x, y = lp.y, z = lp.z });
            }
            if (mv.exitWaypoints != null) foreach (var w in mv.exitWaypoints) rec.exitWaypoints.Add(w != null ? w.name : "");
            rec.maxRoutedSpeed = mv.maxRoutedSpeed;
            SaveRecord(rec);
            sb.AppendLine("  recorded the old values in " + RecordPath);
        }
        else if (rec == null)
            return sb.Append("  " + LanesName + " exists but " + RecordPath + " is missing: the old values are unknown, so nothing is changed. Restore the record file.\n").ToString();
        else sb.AppendLine("  already applied: positions are set again from " + RecordPath);

        // The record must hold every object this builder moves (read back from the file, not from memory):
        // without an old value an object would be shifted again on every run.
        rec = LoadRecord();
        var unrecorded = targets.Keys.Where(k => rec == null || rec.positions.All(i => i.name != k)).ToArray();
        // V2 top-up: an applied scene whose record predates V2 gets the V2-only objects added, at their current (untouched) position.
        if (unrecorded.Length > 0 && rec != null && unrecorded.All(k => V2Added.Contains(k)))
        {
            foreach (var k in unrecorded)
            {
                var lp = targets[k].localPosition;
                rec.positions.Add(new Item { name = k, x = lp.x, y = lp.y, z = lp.z });
                sb.AppendLine("  V2: recorded the old position of " + k + " " + P(lp));
            }
            SaveRecord(rec);
            rec = LoadRecord();
            unrecorded = targets.Keys.Where(k => rec == null || rec.positions.All(i => i.name != k)).ToArray();
        }
        if (unrecorded.Length > 0)
            return sb.Append("  " + RecordPath + " has no old value for: " + string.Join(", ", unrecorded) + ". Nothing changed.\n").ToString();

        Vector3 Old(string name)
        {
            var it = rec.positions.First(i => i.name == name);
            return new Vector3(it.x, it.y, it.z);
        }

        // 1. the desks and their nameplates: a shift from the recorded position.
        sb.AppendLine("-- moved (local position, old -> new; root-local position after) --");
        foreach (var (name, dx, dz) in Shifts)
        {
            var t = targets[name];
            Vector3 old = Old(name);
            // The shift is along the ROOT's axes, expressed in the object's parent space.
            Vector3 d = t.parent != null ? t.parent.InverseTransformVector(root.TransformVector(new Vector3(dx, 0f, dz))) : new Vector3(dx, 0f, dz);
            SetLocal(t, old + d);
            sb.AppendLine("  " + name + ": " + P(old) + " -> " + P(t.localPosition) + "   root-local " + P(root.InverseTransformPoint(t.position)));
        }

        // 2. the delivery spot and the decor: a root-local (x, z); height and rotation kept.
        foreach (var (name, x, z) in Places)
        {
            var t = targets[name];
            Vector3 old = Old(name);
            SetLocal(t, old);                                            // start from the recorded pose, then place
            Vector3 rl = root.InverseTransformPoint(t.position);
            Vector3 want = root.TransformPoint(new Vector3(x, rl.y, z));
            Undo.RecordObject(t, "desk choreography layout");
            t.position = want;
            EditorUtility.SetDirty(t);
            if (PrefabUtility.IsPartOfPrefabInstance(t)) PrefabUtility.RecordPrefabInstancePropertyModifications(t);
            sb.AppendLine("  " + name + ": " + P(old) + " -> " + P(t.localPosition) + "   root-local " + P(root.InverseTransformPoint(t.position)));
        }

        // 3. the lane points.
        if (lanes == null)
        {
            var go = new GameObject(LanesName);
            Undo.RegisterCreatedObjectUndo(go, LanesName);
            lanes = go.transform;
            lanes.SetParent(root, false);
        }
        lanes.localPosition = Vector3.zero; lanes.localRotation = Quaternion.identity; lanes.localScale = Vector3.one;
        lanes.gameObject.layer = root.gameObject.layer;
        var pt = new Dictionary<string, Transform>();
        sb.AppendLine("-- lane points (WorkspaceRoot/" + LanesName + ", root-local) --");
        foreach (var (name, x, z) in Points)
        {
            var t = lanes.Find(name);
            if (t == null) { var go = new GameObject(name); t = go.transform; t.SetParent(lanes, false); }
            t.localPosition = new Vector3(x, 0f, z); t.localRotation = Quaternion.identity; t.localScale = Vector3.one;
            t.gameObject.SetActive(true);
            EditorUtility.SetDirty(t);
            pt[name] = t;
            sb.AppendLine("  " + name + " (" + F(x) + ", " + F(z) + ")");
        }
        // Points this builder no longer lists are removed (an earlier run of an older table).
        foreach (var stale in lanes.Cast<Transform>().Where(c => !pt.ContainsKey(c.name)).ToArray())
        { sb.AppendLine("  removed stale point " + stale.name); UnityEngine.Object.DestroyImmediate(stale.gameObject); }

        // 4. the Researcher's delivery walk.
        Undo.RecordObject(mv, "desk choreography layout");
        mv.exitWaypoints = new[] { pt["Res_Exit_1"], pt["Res_Exit_2"] };
        EditorUtility.SetDirty(mv);
        if (PrefabUtility.IsPartOfPrefabInstance(mv)) PrefabUtility.RecordPrefabInstancePropertyModifications(mv);
        Undo.RecordObject(registry, "desk choreography layout");
        registry.deliveryAnchor = targets["DeliveryZone"];
        EditorUtility.SetDirty(registry);
        sb.AppendLine("-- wiring --");
        sb.AppendLine("  Researcher AgentMovement.exitWaypoints = Res_Exit_1, Res_Exit_2 (were " + (rec.exitWaypoints.Count > 0 ? string.Join(", ", rec.exitWaypoints) : "none") + "); maxRoutedSpeed " + F(mv.maxRoutedSpeed) + " unchanged");

        // 5. the handoff lanes.
        Undo.RecordObject(handoff, "desk choreography layout");
        handoff.lanes = new[]
        {
            new AgentHandoffController.Lane
            {
                sourceId = "researcher", targetId = "developer", stand = pt["ResToDev_Stand"],
                fromHome = new[] { pt["Res_Exit_1"], pt["ResToDev_FromHome_2"] },
                fromDelivery = new[] { pt["ResToDev_FromDelivery_1"] },
                fromDeliverySeconds = FromDeliverySeconds,
            },
            new AgentHandoffController.Lane
            {
                sourceId = "developer", targetId = "tester", stand = pt["DevToTester_Stand"],
                fromHome = new Transform[0], fromDelivery = new Transform[0], fromDeliverySeconds = 0f,
            },
        };
        EditorUtility.SetDirty(handoff);
        sb.AppendLine("  AgentHandoffController.lanes = researcher->developer (stand ResToDev_Stand; from home via Res_Exit_1, ResToDev_FromHome_2; from the delivery spot via ResToDev_FromDelivery_1, " + F(FromDeliverySeconds) + " s), developer->tester (stand DevToTester_Stand, straight)");
        sb.AppendLine("  stopDistance " + F(handoff.stopDistance) + " and backOffset " + F(handoff.backOffset) + " are left as they were (used only by a pair with no lane)");

        EditorSceneManager.MarkSceneDirty(scene);
        bool saved = EditorSceneManager.SaveOpenScenes();
        sb.AppendLine("  scene saved=" + saved);
        return sb.ToString();
    }

    // ── Revert ────────────────────────────────────────────────────────────────────────────────────────────────────
    public static string Revert()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return "DeskChoreographyLayoutV1: stop Play Mode first.\n";
        var scene = EditorSceneManager.GetActiveScene();
        var root = Root();
        if (root == null) return "DeskChoreographyLayoutV1.Revert: no WorkspaceRoot; nothing changed.\n";
        var sb = new StringBuilder();
        sb.AppendLine("== DeskChoreographyLayoutV1.Revert ==");
        var lanes = root.Find(LanesName);
        if (lanes == null) return sb.Append("  " + LanesName + " not found: the layout is not applied; nothing changed.\n").ToString();
        var rec = LoadRecord();
        if (rec == null) return sb.Append("  " + RecordPath + " is missing: the old values are unknown; nothing changed.\n").ToString();

        foreach (var it in rec.positions)
        {
            var t = Under(root, it.name, sb);
            if (t == null) continue;
            SetLocal(t, new Vector3(it.x, it.y, it.z));
            sb.AppendLine("  " + it.name + " -> " + P(t.localPosition));
        }

        var mv = ResearcherMove(root);
        if (mv != null)
        {
            Undo.RecordObject(mv, "desk choreography layout revert");
            mv.exitWaypoints = rec.exitWaypoints.Select(n => string.IsNullOrEmpty(n) ? null : Under(root, n, sb)).ToArray();
            mv.maxRoutedSpeed = rec.maxRoutedSpeed;
            EditorUtility.SetDirty(mv);
            if (PrefabUtility.IsPartOfPrefabInstance(mv)) PrefabUtility.RecordPrefabInstancePropertyModifications(mv);
            sb.AppendLine("  Researcher exitWaypoints = " + string.Join(", ", rec.exitWaypoints));
        }
        var handoff = UnityEngine.Object.FindObjectsByType<AgentHandoffController>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault();
        if (handoff != null)
        {
            Undo.RecordObject(handoff, "desk choreography layout revert");
            handoff.lanes = new AgentHandoffController.Lane[0];
            EditorUtility.SetDirty(handoff);
            sb.AppendLine("  AgentHandoffController.lanes cleared (straight handoffs, as before)");
        }
        UnityEngine.Object.DestroyImmediate(lanes.gameObject);
        sb.AppendLine("  removed WorkspaceRoot/" + LanesName);

        EditorSceneManager.MarkSceneDirty(scene);
        bool saved = EditorSceneManager.SaveOpenScenes();
        sb.AppendLine("  scene saved=" + saved);
        return sb.ToString();
    }
}
