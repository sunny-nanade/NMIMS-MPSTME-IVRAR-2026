using System.Collections;
using System.Linq;
using NUnit.Framework;
using RescueSim.Core;
using RescueSim.Hazards;
using RescueSim.Survivors;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace RescueSim.Tests
{
    /// <summary>Loads the real shipped scenes and checks they start up cleanly. Runs with whatever XR runtime is active (none required).</summary>
    public class SceneSmokeTests
    {
        static IEnumerator Load(string scene)
        {
            var op = SceneManager.LoadSceneAsync(scene, LoadSceneMode.Single);
            Assert.IsNotNull(op, $"Scene '{scene}' is not in Build Settings");
            while (!op.isDone) yield return null;
            yield return null; yield return null;
        }

        static void AssertNoMissingScripts()
        {
            foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
                foreach (var c in root.GetComponentsInChildren<Component>(true))
                    Assert.IsNotNull(c, $"Missing script/component under '{root.name}'");
        }

        [UnityTest]
        public IEnumerator FactoryRescue_StartsCleanly_WithExpectedSetup()
        {
            LogAssert.ignoreFailingMessages = false;
            yield return Load("FactoryRescue");

            var missions = Object.FindObjectsByType<MissionController>(FindObjectsSortMode.None);
            Assert.AreEqual(1, missions.Length, "exactly one MissionController");
            var m = missions[0];
            Assert.IsNotNull(m.Mission, "MissionDefinition assigned");
            Assert.AreEqual(MissionState.Briefing, m.State);
            Assert.AreEqual(6, m.Objectives.Objectives.Count);
            Assert.GreaterOrEqual(m.HazardsTotal, 3);
            Assert.AreEqual(3, m.SurvivorsTotal);

            Assert.AreEqual(1, Object.FindObjectsByType<XROrigin>(FindObjectsSortMode.None).Length, "one XR rig");
            Assert.AreEqual(1, Object.FindObjectsByType<EventSystem>(FindObjectsSortMode.None).Length, "one EventSystem");
            Assert.IsNotNull(Camera.main, "tagged MainCamera");
            Assert.AreEqual(1, Object.FindObjectsByType<AudioListener>(FindObjectsSortMode.None).Length, "one AudioListener");
            AssertNoMissingScripts();
        }

        [UnityTest]
        public IEnumerator FactoryRescue_ProgrammaticFullRun_CompletesAndScores()
        {
            yield return Load("FactoryRescue");
            var m = Object.FindFirstObjectByType<MissionController>();
            m.BeginMission();
            Assert.AreEqual(MissionState.Active, m.State);

            foreach (var h in m.Hazards.ToList()) h.Neutralize();
            Assert.IsTrue(m.Objectives.IsComplete(ObjectiveIds.Power), "power");
            Assert.IsTrue(m.Objectives.IsComplete(ObjectiveIds.Gas), "gas");
            Assert.IsTrue(m.Objectives.IsComplete(ObjectiveIds.Fire), "fire");

            m.CompleteObjective(ObjectiveIds.Route);
            foreach (var s in m.Survivors.ToList()) s.ForceState(SurvivorState.Trapped);
            Assert.IsTrue(m.Objectives.IsComplete(ObjectiveIds.Locate));
            foreach (var s in m.Survivors.ToList()) s.ForceState(SurvivorState.Rescued);

            Assert.AreEqual(MissionState.Completed, m.State);
            Assert.IsTrue(m.LastResult.HasValue);
            Assert.GreaterOrEqual(m.LastResult.Value.total, 800);
        }

        [UnityTest]
        public IEnumerator FactoryRescue_ReloadingTwice_DoesNotDuplicateRig()
        {
            yield return Load("FactoryRescue");
            yield return Load("FactoryRescue");
            Assert.AreEqual(1, Object.FindObjectsByType<MissionController>(FindObjectsSortMode.None).Length);
            Assert.AreEqual(1, Object.FindObjectsByType<XROrigin>(FindObjectsSortMode.None).Length);
            Assert.AreEqual(MissionState.Briefing, Object.FindFirstObjectByType<MissionController>().State);
        }

        [UnityTest]
        public IEnumerator MainMenu_StartsCleanly()
        {
            yield return Load("MainMenu");
            Assert.AreEqual(1, Object.FindObjectsByType<XROrigin>(FindObjectsSortMode.None).Length);
            Assert.AreEqual(1, Object.FindObjectsByType<EventSystem>(FindObjectsSortMode.None).Length);
            Assert.IsNotNull(Camera.main);
            AssertNoMissingScripts();
        }
    }
}
