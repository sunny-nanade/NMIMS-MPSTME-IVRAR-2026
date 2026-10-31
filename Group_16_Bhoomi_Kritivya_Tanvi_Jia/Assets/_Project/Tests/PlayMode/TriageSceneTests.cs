using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using RescueSim.Core;
using RescueSim.Triage;
using RescueSim.UI;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace RescueSim.Tests
{
    /// <summary>Loads TriageSimulation and drives the real manager, UI and telemetry logger.</summary>
    public class TriageSceneTests
    {
        string _dir;

        [SetUp] public void SetUp() { _dir = Path.Combine(Path.GetTempPath(), "triage_play_" + System.Guid.NewGuid().ToString("N")); }
        [TearDown] public void TearDown() { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); }

        static IEnumerator Load(string scene)
        {
            var op = SceneManager.LoadSceneAsync(scene, LoadSceneMode.Single);
            Assert.IsNotNull(op, $"Scene '{scene}' is not in Build Settings");
            while (!op.isDone) yield return null;
            yield return null; yield return null;
        }

        IEnumerator LoadTriage(System.Action<STARTTriageSimulationManager, TriageUI> done)
        {
            yield return Load(StageCatalog.TriageScene);
            var m = Object.FindFirstObjectByType<STARTTriageSimulationManager>();
            var ui = Object.FindFirstObjectByType<TriageUI>();
            Assert.IsNotNull(m, "manager"); Assert.IsNotNull(ui, "ui");
            m.Logger.SetDirectoryOverride(_dir);
            done(m, ui);
        }

        static Button FindButton(string label) =>
            Object.FindObjectsByType<Button>(FindObjectsInactive.Include, FindObjectsSortMode.None).First(b => b.name == "Button_" + label);

        [UnityTest]
        public IEnumerator Scene_LoadsClean_WithTenIdentifiableCasualties()
        {
            STARTTriageSimulationManager m = null; TriageUI ui = null;
            yield return LoadTriage((a, b) => { m = a; ui = b; });
            foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
                foreach (var c in root.GetComponentsInChildren<Component>(true)) Assert.IsNotNull(c, $"Missing script under '{root.name}'");
            Assert.AreEqual(1, Object.FindObjectsByType<XROrigin>(FindObjectsSortMode.None).Length, "one XR rig");
            Assert.AreEqual(1, Object.FindObjectsByType<EventSystem>(FindObjectsSortMode.None).Length, "one EventSystem");
            Assert.AreEqual(1, Object.FindObjectsByType<STARTTriageSimulationManager>(FindObjectsSortMode.None).Length);
            Assert.AreEqual(10, m.Casualties.Count);
            CollectionAssert.AreEqual(Enumerable.Range(1, 10).Select(i => "C" + i.ToString("00")), m.Casualties.Select(c => c.Id));
            foreach (var c in m.Casualties)
            {
                Assert.IsNotNull(c.GetComponent<CasualtySelectable>(), c.Id);
                Assert.IsNotNull(c.GetComponent<XRSimpleInteractable>(), c.Id);
                Assert.IsNotNull(c.GetComponent<Collider>(), c.Id);
            }
            Assert.IsTrue(ui.IsStartVisible); Assert.IsFalse(ui.IsAssessVisible); Assert.IsFalse(ui.IsResultsVisible);
            Assert.IsFalse(m.IsRunning);
        }

        [UnityTest]
        public IEnumerator FullPlaythrough_ExportsCsv_AndShowsResults()
        {
            STARTTriageSimulationManager m = null; TriageUI ui = null;
            yield return LoadTriage((a, b) => { m = a; ui = b; });
            Assert.IsTrue(m.SetParticipant("P01", DataProvenance.Pilot));
            Assert.IsTrue(m.BeginSession());
            Assert.IsTrue(ui.IsAssessVisible); Assert.IsFalse(ui.IsStartVisible);
            foreach (var c in m.Casualties)
            {
                Assert.IsTrue(m.SelectCasualty(c), c.Id + " should be within reach of the start position");
                foreach (ExamKind k in System.Enum.GetValues(typeof(ExamKind))) Assert.IsNotNull(m.Examine(k));
                var rec = m.Assign(STARTEvaluator.GroundTruth(c.Vitals, m.Perfusion));
                Assert.IsTrue(rec.isCorrect, c.Id);
                Assert.AreEqual(rec.groundTruth, c.Tag);
                yield return null;
            }
            Assert.IsTrue(m.IsComplete);
            Assert.IsTrue(ui.IsResultsVisible);
            StringAssert.Contains("100", ui.SummaryMessage);
            Assert.AreEqual(1, m.Logger.ExportCount);
            Assert.IsTrue(File.Exists(m.Logger.LastRecordsPath)); Assert.IsTrue(File.Exists(m.Logger.LastSummaryPath)); Assert.IsTrue(File.Exists(m.Logger.LastConfusionPath));
            var lines = File.ReadAllLines(m.Logger.LastRecordsPath);
            Assert.AreEqual(11, lines.Length);
            StringAssert.Contains("Pilot", lines[1]); StringAssert.Contains("P01", lines[1]);
        }

        [UnityTest]
        public IEnumerator Restart_ExportsPartialData_ThenClearsEverything()
        {
            STARTTriageSimulationManager m = null; TriageUI ui = null;
            yield return LoadTriage((a, b) => { m = a; ui = b; });
            m.BeginSession(); string firstId = m.Session.SessionId;
            m.SelectCasualty(m.Casualties[0]); m.Assign(TriageCategory.Green);
            m.SelectCasualty(m.Casualties[3]); m.Assign(TriageCategory.Red);
            Assert.AreEqual(TriageCategory.Red, m.Casualties[3].Tag);
            m.Restart();
            Assert.AreEqual(1, m.Logger.ExportCount, "partial session is exported");
            StringAssert.Contains("false", File.ReadAllLines(m.Logger.LastSummaryPath)[1], "session_complete = false");
            Assert.IsTrue(ui.IsStartVisible); Assert.IsFalse(m.IsRunning);
            Assert.AreEqual(0, m.Session.TaggedCount);
            Assert.AreNotEqual(firstId, m.Session.SessionId);
            Assert.AreEqual(TriageCategory.None, m.Casualties[0].Tag); Assert.AreEqual(TriageCategory.None, m.Casualties[3].Tag);
            Assert.IsTrue(m.BeginSession(), "can run again after a restart");
        }

        [UnityTest]
        public IEnumerator Ui_BeginButton_StartsSession_AndWrongTagGivesFeedback()
        {
            STARTTriageSimulationManager m = null; TriageUI ui = null;
            yield return LoadTriage((a, b) => { m = a; ui = b; });
            FindButton("Begin Exercise").onClick.Invoke();
            Assert.IsTrue(m.IsRunning); Assert.AreEqual("TEST", m.Session.ParticipantId); Assert.AreEqual(DataProvenance.SoftwareTest, m.Session.Provenance);
            Assert.IsTrue(m.SelectCasualty(m.Casualties[5]));      // C06: true Red (respiratory rate)
            FindButton("YELLOW").onClick.Invoke();
            StringAssert.Contains("START says Red", ui.FeedbackMessage);
            Assert.AreEqual(TriageCategory.Yellow, m.Casualties[5].Tag);
            Assert.IsTrue(m.Session.Records[0].trueRedUnderTriaged);
        }

        [UnityTest]
        public IEnumerator Selection_RequiresBegin_AndRefusesRetag()
        {
            STARTTriageSimulationManager m = null; TriageUI ui = null;
            yield return LoadTriage((a, b) => { m = a; ui = b; });
            Assert.IsFalse(m.SelectCasualty(m.Casualties[0]), "not started");
            m.BeginSession();
            Assert.IsTrue(m.SelectCasualty(m.Casualties[0])); m.Assign(TriageCategory.Green);
            Assert.IsFalse(m.SelectCasualty(m.Casualties[0]), "already tagged");
            Assert.IsNull(m.Assign(TriageCategory.Red));
        }

        [UnityTest]
        public IEnumerator Selecting_ARedCasualty_DoesNotRevealFindings()
        {
            STARTTriageSimulationManager m = null; TriageUI ui = null;
            yield return LoadTriage((a, b) => { m = a; ui = b; });
            m.BeginSession();
            var c = m.Casualties[5];   // C06: respiratory rate 36, "breathing very fast" in the scenario data
            Assert.IsTrue(m.SelectCasualty(c));
            var texts = Object.FindObjectsByType<TMPro.TMP_Text>(FindObjectsInactive.Include, FindObjectsSortMode.None).Select(t => t.text).ToList();
            Assert.IsFalse(texts.Any(t => t.Contains(c.Vitals.observation)), "scenario observation text must not be shown");
            Assert.IsFalse(texts.Any(t => t.Contains("36")), "respiratory rate must not appear before the check");
            m.Examine(ExamKind.RespiratoryRate);
            texts = Object.FindObjectsByType<TMPro.TMP_Text>(FindObjectsInactive.Include, FindObjectsSortMode.None).Select(t => t.text).ToList();
            Assert.IsTrue(texts.Any(t => t.Contains("36 breaths/min")), "shown after the check");
        }

        [UnityTest]
        public IEnumerator HazardEscalation_StepsUp_WhileRunning_AndResets()
        {
            STARTTriageSimulationManager m = null; TriageUI ui = null;
            yield return LoadTriage((a, b) => { m = a; ui = b; });
            var hz = Object.FindFirstObjectByType<TriageHazardEscalation>();
            typeof(TriageHazardEscalation).GetField("secondsPerLevel", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).SetValue(hz, 1f);
            var smoke = Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None).First(p => p.name == "Smoke");
            float baseRate = smoke.emission.rateOverTimeMultiplier;
            Assert.AreEqual(0, hz.Level);
            m.BeginSession();
            float t0 = Time.time;
            while (Time.time - t0 < 2.4f) yield return null;
            Assert.GreaterOrEqual(hz.Level, 2, "level rises with elapsed time");
            Assert.Greater(smoke.emission.rateOverTimeMultiplier, baseRate, "smoke emission grows");
            Assert.AreEqual(hz.Level, m.Session.Records.Count == 0 ? m.HazardLevel : -1);
            m.Restart();
            Assert.AreEqual(0, hz.Level);
            Assert.AreEqual(baseRate, smoke.emission.rateOverTimeMultiplier, 1e-3f);
        }

        [UnityTest]
        public IEnumerator MainMenu_HasTriageButton_AndStillThreeStages()
        {
            yield return Load(StageCatalog.MenuScene);
            var menu = Object.FindFirstObjectByType<MainMenuController>();
            Assert.AreEqual(3, menu.Cards.Count);
            Assert.IsNotNull(FindButton("Triage Study"));
            Assert.AreEqual(1, Object.FindObjectsByType<EventSystem>(FindObjectsSortMode.None).Length);
        }
    }
}
