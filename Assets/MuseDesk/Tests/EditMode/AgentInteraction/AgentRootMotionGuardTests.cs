using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace MuseDesk.Tests.AgentInteraction
{
    /// <summary>
    /// Guard: all agent displacement is script-driven, so no agent Animator may apply root motion. The desk agents also
    /// force it off at runtime (AgentMovement.Awake); this test catches a rebuilt prefab that ships it on.
    /// </summary>
    public class AgentRootMotionGuardTests
    {
        // Desk agents MainMRScene instantiates, the art-pipeline desk agents, and the wall Manager.
        static readonly string[] Prefabs =
        {
            "Assets/MuseDesk/Prefabs/Agents/Agent_Researcher.prefab",
            "Assets/MuseDesk/Prefabs/Agents/Agent_Developer.prefab",
            "Assets/MuseDesk/Prefabs/Agents/Agent_Tester.prefab",
            "Assets/MuseDesk/Art/Prefabs/Characters/Agent_Researcher.prefab",
            "Assets/MuseDesk/Art/Prefabs/Characters/Agent_Developer.prefab",
            "Assets/MuseDesk/Art/Prefabs/Characters/Agent_Tester.prefab",
            "Assets/MuseDesk/Art/Prefabs/WallHabitat/Agent_Manager.prefab",
        };

        [TestCaseSource(nameof(Prefabs))]
        public void AgentPrefabAnimatorsDoNotApplyRootMotion(string path)
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (go == null) Assert.Ignore("prefab not present: " + path);
            var bad = new List<string>();
            foreach (var a in go.GetComponentsInChildren<Animator>(true))
                if (a.applyRootMotion) bad.Add(a.name);
            Assert.IsEmpty(bad, path + ": Animators with applyRootMotion on: " + string.Join(", ", bad));
        }
    }
}
