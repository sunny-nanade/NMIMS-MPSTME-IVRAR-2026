using System.Collections;
using System.Linq;
using NUnit.Framework;
using RescueSim.Core;
using RescueSim.UI;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace RescueSim.Tests
{
    /// <summary>Loads each shipped scene and runs the campaign flow through the real components.</summary>
    public class CampaignSceneTests
    {
        ICampaignStore _prev;

        [SetUp] public void SetUp() { _prev = CampaignProgress.Store; CampaignProgress.Store = new MemoryStore(); }
        [TearDown] public void TearDown() { CampaignProgress.Store = _prev; }

        static IEnumerator Load(string scene)
        {
            var op = SceneManager.LoadSceneAsync(scene, LoadSceneMode.Single);
            Assert.IsNotNull(op, $"Scene '{scene}' is not in Build Settings");
            while (!op.isDone) yield return null;
            yield return null; yield return null;
        }

        static void AssertClean()
        {
            foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
                foreach (var c in root.GetComponentsInChildren<Component>(true))
                    Assert.IsNotNull(c, $"Missing script under '{root.name}'");
            Assert.AreEqual(1, Object.FindObjectsByType<XROrigin>(FindObjectsSortMode.None).Length, "one XR rig");
            Assert.AreEqual(1, Object.FindObjectsByType<EventSystem>(FindObjectsSortMode.None).Length, "one EventSystem");
            Assert.AreEqual(1, Object.FindObjectsByType<MissionController>(FindObjectsSortMode.None).Length, "one mission");
        }

        [UnityTest]
        public IEnumerator AllStageScenes_Load_WithValidStageConfig([ValueSource(nameof(Stages))] int stage)
        {
            yield return Load(StageCatalog.SceneFor(stage));
            AssertClean();
            var m = Object.FindFirstObjectByType<MissionController>();
            Assert.AreEqual(stage, m.Mission.stageIndex);
            Assert.AreEqual(MissionState.Briefing, m.State);
            Assert.That(m.Objectives.Objectives.Count, Is.InRange(3, 6));
            Assert.GreaterOrEqual(m.SurvivorsTotal, 2);
            var ui = Object.FindFirstObjectByType<MissionUI>();
            Assert.IsNotNull(ui);
            Assert.IsTrue(ui.HasValidStage);
        }
        static int[] Stages = { 0, 1, 2 };

        [UnityTest]
        public IEnumerator MainMenu_ShowsLocks_AndSelectStageRules()
        {
            yield return Load(StageCatalog.MenuScene);
            Assert.AreEqual(1, Object.FindObjectsByType<EventSystem>(FindObjectsSortMode.None).Length);
            var menu = Object.FindFirstObjectByType<MainMenuController>();
            Assert.IsNotNull(menu);
            Assert.AreEqual(3, menu.Cards.Count);
            Assert.IsFalse(menu.SelectStage(1), "locked stage must not load");
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex(@"Invalid stage index -1"));
            Assert.IsFalse(menu.SelectStage(-1));
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex(@"Invalid stage index 42"));
            Assert.IsFalse(menu.SelectStage(42));
            yield return null;
            Assert.AreEqual(StageCatalog.MenuScene, SceneManager.GetActiveScene().name);
        }

        [UnityTest]
        public IEnumerator MainMenu_UnlockedStage_Loads()
        {
            CampaignProgress.RecordResult(0, 70, 2, true);
            yield return Load(StageCatalog.MenuScene);
            var menu = Object.FindFirstObjectByType<MainMenuController>();
            Assert.IsTrue(menu.SelectStage(1));
            float t = 0; while (SceneManager.GetActiveScene().name != "EarthquakeRescue" && t < 10f) { t += Time.unscaledDeltaTime; yield return null; }
            Assert.AreEqual("EarthquakeRescue", SceneManager.GetActiveScene().name);
        }

        [UnityTest]
        public IEnumerator Earthquake_ObjectivesComplete_StageCompletes_AndRecordsProgress()
        {
            yield return Load("EarthquakeRescue");
            var m = Object.FindFirstObjectByType<MissionController>();
            m.BeginMission();
            Assert.AreEqual(MissionState.Active, m.State);
            foreach (var o in m.Objectives.Objectives.ToList())
                if (o.Id != ObjectiveIds.Locate && o.Id != ObjectiveIds.Evacuate) m.CompleteObjective(o.Id);
            // Survivors resolved through their own interaction path.
            foreach (var s in m.Survivors) { s.ForceState(RescueSim.Survivors.SurvivorState.Trapped); s.ForceState(RescueSim.Survivors.SurvivorState.Rescued); }
            m.CompleteObjective(ObjectiveIds.Locate);
            m.CompleteObjective(ObjectiveIds.Evacuate);
            yield return null; yield return null;
            Assert.AreEqual(MissionState.Completed, m.State);
            Assert.IsTrue(CampaignProgress.IsCompleted(1));
            Assert.IsTrue(CampaignProgress.IsUnlocked(2));
            var ui = Object.FindFirstObjectByType<MissionUI>();
            Assert.That(ui.LastStars, Is.InRange(1, 3));
            Assert.That(ui.LastScore100, Is.InRange(1, 100));
        }
    }
}
