using System.Text;
using UnityEngine;
using MuseDesk.Workspace;

/// <summary>Checks AgentNameplate.TiltToward (the plate's readable face, i.e. -forward, turns toward the viewer, clamped)
/// and the live Manager paper footer words. unity command script run --file AgentScripts/NameplateCheck.cs --entry NameplateCheck.Run</summary>
public static class NameplateCheck
{
    static int _fail;
    static void Ok(StringBuilder sb, string name, bool ok, string detail) { if (!ok) _fail++; sb.AppendLine((ok ? "PASS " : "FAIL ") + name + "  " + detail); }

    public static string Run()
    {
        _fail = 0;
        var sb = new StringBuilder();
        Quaternion rest = Quaternion.Euler(0, 180, 0);       // forward -Z: readable from +Z
        // Viewer straight in front (+Z), level: stays at rest.
        Quaternion q = AgentNameplate.TiltToward(rest, new Vector3(0, 0, 1), 60, 35);
        Ok(sb, "front viewer keeps rest", Quaternion.Angle(q, rest) < 0.5f, q.eulerAngles.ToString("F1"));
        // Viewer 30 deg to the right (+X): the readable face (-forward) points at the viewer.
        Vector3 to = Quaternion.Euler(0, 30, 0) * Vector3.forward;
        q = AgentNameplate.TiltToward(rest, to, 60, 35);
        Ok(sb, "face follows a viewer 30 deg right", Vector3.Angle(-(q * Vector3.forward), to) < 0.5f, (-(q * Vector3.forward)).ToString("F2"));
        // Viewer far to the side (100 deg): clamped to 60.
        to = Quaternion.Euler(0, 100, 0) * Vector3.forward;
        q = AgentNameplate.TiltToward(rest, to, 60, 35);
        float yawOff = Mathf.Abs(Mathf.DeltaAngle(180, q.eulerAngles.y));
        Ok(sb, "side viewer clamped to maxYaw", Mathf.Abs(yawOff - 60) < 0.5f, yawOff.ToString("F1"));
        // Viewer above (looking down at the desk at 30 deg): the face tips up toward them.
        to = new Vector3(0, 0.5f, 0.866f);
        q = AgentNameplate.TiltToward(rest, to, 60, 35);
        Vector3 face = -(q * Vector3.forward);
        Ok(sb, "face tips up to a viewer above", face.y > 0.45f && face.y < 0.55f, face.ToString("F2"));
        // Ceiling: viewer far below (pod overhead): clamped pitch.
        to = new Vector3(0, -0.95f, 0.3f);
        q = AgentNameplate.TiltToward(rest, to, 60, 35);
        face = -(q * Vector3.forward);
        Ok(sb, "viewer below clamped to maxPitch", Mathf.Abs(Mathf.Asin(-face.y) * Mathf.Rad2Deg - 35) < 0.6f, face.ToString("F2"));
        // Viewer behind the plate: never flips round, stays within the clamp.
        q = AgentNameplate.TiltToward(rest, new Vector3(0.05f, 0, -1), 60, 35);
        yawOff = Mathf.Abs(Mathf.DeltaAngle(180, q.eulerAngles.y));
        Ok(sb, "viewer behind: within clamp", yawOff <= 60.5f, yawOff.ToString("F1"));

        Ok(sb, "footer sandbox", MuseDesk.Wall.WallHabitatController.PlanFooter("sandbox") == "LIVE model · code + tests really run in a sandbox", MuseDesk.Wall.WallHabitatController.PlanFooter("sandbox"));
        Ok(sb, "footer text only", MuseDesk.Wall.WallHabitatController.PlanFooter("") == "LIVE model · text only, no code tools", MuseDesk.Wall.WallHabitatController.PlanFooter(""));
        sb.Insert(0, (_fail == 0 ? "ALL PASS" : _fail + " FAILED") + "\n");
        return sb.ToString();
    }
}
