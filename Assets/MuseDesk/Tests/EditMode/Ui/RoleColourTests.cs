using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using MuseDesk.Spatial.Ui;

namespace MuseDesk.Tests.EditMode
{
    /// <summary>Role colours (competition build): parity between OfficeTokens and the Core mirror, and distance from the state tokens.</summary>
    public class RoleColourTests
    {
        static string OfficeUiSource()
        {
            string p = Path.Combine(Application.dataPath, "MuseDesk/Scripts/Workspace/OfficeUi.cs");
            return File.ReadAllText(p);
        }

        static Color32 TokenFromSource(string name)
        {
            var m = Regex.Match(OfficeUiSource(), "\\b" + name + "\\s*=\\s*new Color32\\(0x([0-9A-Fa-f]{2}),\\s*0x([0-9A-Fa-f]{2}),\\s*0x([0-9A-Fa-f]{2})");
            Assert.IsTrue(m.Success, "token not found in OfficeUi.cs: " + name);
            return new Color32(System.Convert.ToByte(m.Groups[1].Value, 16), System.Convert.ToByte(m.Groups[2].Value, 16), System.Convert.ToByte(m.Groups[3].Value, 16), 255);
        }

        static void AreSame(Color32 a, Color32 b, string what)
        {
            Assert.AreEqual(a.r, b.r, what); Assert.AreEqual(a.g, b.g, what); Assert.AreEqual(a.b, b.b, what);
        }

        [Test]
        public void SpecifiedHexValues()
        {
            AreSame(PlanPalette.Of(PlanTone.RoleResearcher), new Color32(0xA9, 0xB8, 0xF0, 255), "researcher");
            AreSame(PlanPalette.Of(PlanTone.RoleDeveloper), new Color32(0xD2, 0xA6, 0xE8, 255), "developer");
            AreSame(PlanPalette.Of(PlanTone.RoleTester), new Color32(0xF0, 0xA9, 0xC4, 255), "tester");
            AreSame(PlanPalette.Of(PlanTone.RoleManager), new Color32(0x4B, 0x4F, 0x9C, 255), "manager");
        }

        [Test]
        public void CoreMirrorMatchesOfficeTokens()
        {
            AreSame(TokenFromSource("RoleResearcher"), PlanPalette.Of(PlanTone.RoleResearcher), "researcher");
            AreSame(TokenFromSource("RoleDeveloper"), PlanPalette.Of(PlanTone.RoleDeveloper), "developer");
            AreSame(TokenFromSource("RoleTester"), PlanPalette.Of(PlanTone.RoleTester), "tester");
            AreSame(TokenFromSource("RoleManager"), PlanPalette.Of(PlanTone.RoleManager), "manager");
        }

        [Test]
        public void MappingCoversAllRolesAndUnknownIsNeutral()
        {
            Assert.AreEqual(PlanTone.RoleResearcher, PlanPalette.RoleTone("researcher"));
            Assert.AreEqual(PlanTone.RoleDeveloper, PlanPalette.RoleTone("Developer"));
            Assert.AreEqual(PlanTone.RoleTester, PlanPalette.RoleTone(" TESTER "));
            Assert.AreEqual(PlanTone.RoleManager, PlanPalette.RoleTone("manager"));
            Assert.AreEqual(PlanTone.Neutral, PlanPalette.RoleTone("planner"));
            Assert.AreEqual(PlanTone.Neutral, PlanPalette.RoleTone(""));
            Assert.AreEqual(PlanTone.Neutral, PlanPalette.RoleTone(null));
            // The runtime mapping must exist in OfficeTokens with the same shape.
            string src = OfficeUiSource();
            StringAssert.Contains("RoleColor(string roleOrAgentId)", src);
            StringAssert.Contains("return Neutral;", src);
        }

        [Test]
        public void RoleColoursAreFarFromEveryStateToken()
        {
            PlanTone[] roles = { PlanTone.RoleResearcher, PlanTone.RoleDeveloper, PlanTone.RoleTester, PlanTone.RoleManager };
            PlanTone[] states = { PlanTone.Teal, PlanTone.Amber, PlanTone.Success, PlanTone.Coral, PlanTone.Lavender, PlanTone.Neutral, PlanTone.InfoBlue };
            foreach (var r in roles)
            {
                var rc = (Color)PlanPalette.Of(r);
                foreach (var s in states)
                {
                    var sc = (Color)PlanPalette.Of(s);
                    float d = Mathf.Sqrt((rc.r - sc.r) * (rc.r - sc.r) + (rc.g - sc.g) * (rc.g - sc.g) + (rc.b - sc.b) * (rc.b - sc.b));
                    Assert.Greater(d, 0.18f, r + " too close to " + s + " (d=" + d + ")");
                }
            }
        }

        [Test]
        public void RolesAreDistinctFromEachOther()
        {
            PlanTone[] roles = { PlanTone.RoleResearcher, PlanTone.RoleDeveloper, PlanTone.RoleTester, PlanTone.RoleManager };
            for (int i = 0; i < roles.Length; i++)
                for (int j = i + 1; j < roles.Length; j++)
                {
                    var a = (Color)PlanPalette.Of(roles[i]); var b = (Color)PlanPalette.Of(roles[j]);
                    float d = Mathf.Sqrt((a.r - b.r) * (a.r - b.r) + (a.g - b.g) * (a.g - b.g) + (a.b - b.b) * (a.b - b.b));
                    Assert.Greater(d, 0.15f, roles[i] + " vs " + roles[j]);
                }
        }
    }
}
