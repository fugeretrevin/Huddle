using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;

namespace MuseDesk.Tests.AgentInteraction
{
    /// <summary>
    /// Guard: a reimport once rewrote Agent_*.fbx.meta and 32 clips lost loopTime. A looping clip that does not
    /// loop freezes on its last pose, which looks like a hang. These read the importer settings of the committed
    /// metas; the list mirrors MuseDeskArtPipeline.LoopingCharacterClips (the core set).
    /// </summary>
    public class AgentClipLoopFlagTests
    {
        const string CharDir = "Assets/MuseDesk/Art/Models/Characters/";

        static readonly string[] MustLoop =
        {
            "Idle_Breathing", "Walk_Loop", "Typing", "Reading", "Inspecting", "Waiting_For_Input", "Thinking",
            "Idle_Fidget", "Happy_Idle", "Walk_Carry_Paper", "Seated_Idle", "Talking", "Listening",
            "Writing", "Using_Workstation", "Hold_Document", "Blocked"
        };

        static readonly string[] Characters =
        {
            CharDir + "Agent_Researcher.fbx", CharDir + "Agent_Developer.fbx", CharDir + "Agent_Tester.fbx"
        };

        const string Manager = "Assets/MuseDesk/Art/Models/WallHabitat/Agent_Manager.fbx";

        static Dictionary<string, bool> LoopFlags(string path)
        {
            var imp = AssetImporter.GetAtPath(path) as ModelImporter;
            Assert.IsNotNull(imp, "no ModelImporter at " + path);
            var map = new Dictionary<string, bool>();
            foreach (var c in imp.clipAnimations)
            {
                string n = c.name;
                int bar = n.LastIndexOf('|');
                if (bar >= 0 && bar < n.Length - 1) n = n.Substring(bar + 1);
                map[n] = c.loopTime;
            }
            return map;
        }

        static void AssertLoops(string path, bool required)
        {
            var flags = LoopFlags(path);
            var bad = new List<string>();
            foreach (var clip in MustLoop)
            {
                if (!flags.TryGetValue(clip, out bool loop)) { if (required) bad.Add(clip + " (missing)"); }
                else if (!loop) bad.Add(clip);
            }
            Assert.IsEmpty(bad, path + ": clips lost loopTime: " + string.Join(", ", bad));
        }

        [TestCase(0)] [TestCase(1)] [TestCase(2)]
        public void DeskAgentLoopingClipsKeepLoopTime(int index) => AssertLoops(Characters[index], true);

        [Test]
        public void ManagerLoopingClipsKeepLoopTime_IfPresent()
        {
            if (AssetImporter.GetAtPath(Manager) == null) Assert.Ignore("Agent_Manager.fbx not present");
            AssertLoops(Manager, false);
        }

        [Test]
        public void DeskAgentsHaveTheCoreLoopingClips()
        {
            foreach (var p in Characters)
            {
                var flags = LoopFlags(p);
                foreach (var clip in new[] { "Idle_Breathing", "Walk_Loop", "Typing", "Reading", "Inspecting", "Waiting_For_Input", "Thinking" })
                    Assert.IsTrue(flags.ContainsKey(clip), p + " has no clip " + clip);
            }
        }
    }
}
