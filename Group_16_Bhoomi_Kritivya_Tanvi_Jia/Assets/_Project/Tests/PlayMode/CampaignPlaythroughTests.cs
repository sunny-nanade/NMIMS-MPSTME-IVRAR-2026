using System.Collections;
using System.Linq;
using NUnit.Framework;
using RescueSim.Core;
using RescueSim.Interaction;
using RescueSim.Survivors;
using RescueSim.UI;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace RescueSim.Tests
{
    /// <summary>
    /// Plays the two new stages end to end through the real components: UI button, head movement through zones,
    /// XR Interaction Toolkit select events from the rig's own right-hand interactor (grab / hold-to-assist),
    /// NavMesh-following survivors, the exit zone, the results panel and the Next Stage transition.
    /// Not a physical headset test; the controller input is synthesised through XRInteractionManager.
    /// </summary>
    public class CampaignPlaythroughTests
    {
        ICampaignStore _prev;
        [SetUp] public void SetUp() { _prev = CampaignProgress.Store; CampaignProgress.Store = new MemoryStore(); }
        [TearDown] public void TearDown() { Time.timeScale = 1f; CampaignProgress.Store = _prev; }

        static IEnumerator Load(string scene)
        {
            var op = SceneManager.LoadSceneAsync(scene, LoadSceneMode.Single);
            Assert.IsNotNull(op, $"Scene '{scene}' is not in Build Settings");
            while (!op.isDone) yield return null;
            yield return null; yield return null;
        }

        static IEnumerator Seconds(float s) { float t = 0f; while (t < s) { t += Time.deltaTime; yield return null; } }

        static void MoveHeadTo(Vector3 world, Vector3? lookAt = null)
        {
            var origin = Object.FindFirstObjectByType<XROrigin>();
            if (lookAt.HasValue)
            {
                Vector3 d = lookAt.Value - world; d.y = 0f;
                if (d.sqrMagnitude > 0.01f) origin.transform.rotation = Quaternion.LookRotation(d.normalized, Vector3.up);
            }
            origin.MoveCameraToWorldLocation(world);
        }

        static NearFarInteractor RightHand()
        {
            var all = Object.FindObjectsByType<NearFarInteractor>(FindObjectsSortMode.None);
            Assert.IsNotEmpty(all, "rig has no NearFarInteractor");
            return all.FirstOrDefault(i => i.name.Contains("Right")) ?? all[0];
        }

        static IEnumerator Select(NearFarInteractor hand, XRBaseInteractable target, float holdSeconds)
        {
            var mgr = hand.interactionManager;
            hand.transform.position = target.transform.position;
            mgr.SelectEnter((IXRSelectInteractor)hand, (IXRSelectInteractable)target);
            float t = 0f;
            while (t < holdSeconds)
            {
                if (!target.isSelected) mgr.SelectEnter((IXRSelectInteractor)hand, (IXRSelectInteractable)target);   // keep "gripping"
                t += Time.deltaTime; yield return null;
            }
        }

        static void Release(NearFarInteractor hand, XRBaseInteractable target)
        {
            if (target.isSelected) hand.interactionManager.SelectExit((IXRSelectInteractor)hand, (IXRSelectInteractable)target);
        }

        static MissionUI BeginMission(out MissionController m)
        {
            m = Object.FindFirstObjectByType<MissionController>();
            var ui = Object.FindFirstObjectByType<MissionUI>();
            Assert.AreEqual(MissionState.Briefing, m.State);
            Assert.IsTrue(ui.Briefing.IsVisible, "briefing is shown first");
            var begin = ui.Briefing.GetComponentsInChildren<Button>(true).First();
            begin.onClick.Invoke();
            Assert.AreEqual(MissionState.Active, m.State);
            return ui;
        }

        static IEnumerator DoZones(MissionController m)
        {
            for (int pass = 0; pass < 3; pass++)
            {
                foreach (var z in Object.FindObjectsByType<ZoneObjective>(FindObjectsSortMode.None).OrderBy(z => z.name))
                {
                    if (z.Reached) continue;
                    MoveHeadTo(z.transform.position);
                    yield return Seconds(0.6f);
                }
            }
            foreach (var z in Object.FindObjectsByType<ZoneObjective>(FindObjectsSortMode.None)) Assert.IsTrue(z.Reached, $"zone '{z.name}' never completed");
        }

        static IEnumerator DoPickups(NearFarInteractor hand)
        {
            foreach (var p in Object.FindObjectsByType<PickupObjective>(FindObjectsSortMode.None))
            {
                var g = p.GetComponent<XRGrabInteractable>();
                MoveHeadTo(p.transform.position - Vector3.forward * 1f + Vector3.down * 0f);
                yield return Select(hand, g, 0.3f);
                Release(hand, g);
                yield return Seconds(0.3f);
                Assert.IsTrue(p.PickedUp, $"pickup '{p.name}' did not register a grab");
            }
        }

        static IEnumerator DoDebris(NearFarInteractor hand)
        {
            var monitor = Object.FindFirstObjectByType<PassageMonitor>();
            if (monitor == null) yield break;
            foreach (var d in Object.FindObjectsByType<DebrisItem>(FindObjectsSortMode.None))
            {
                var g = d.GetComponent<XRGrabInteractable>();
                if (g == null || !monitor.Contains(d.transform.position)) continue;
                MoveHeadTo(d.transform.position - Vector3.forward * 1.2f);
                yield return Select(hand, g, 0.2f);
                Assert.IsTrue(g.isSelected, "debris block can be grabbed");
                hand.transform.position += Vector3.right * 14f;   // carry it out of the corridor
                yield return Seconds(0.5f);
                Release(hand, g);
                yield return Seconds(0.3f);
            }
            monitor.Evaluate();
            Assert.IsTrue(monitor.IsCleared, "passage reported cleared after moving the blocks");
        }

        static IEnumerator DoSurvivors(MissionController m, NearFarInteractor hand)
        {
            foreach (var s in m.Survivors)
            {
                Vector3 p = s.transform.position;
                MoveHeadTo(p + new Vector3(0f, 0f, -1.8f), p);
                float t = 0f; while (s.State == SurvivorState.Unfound && t < 3f) { t += Time.deltaTime; yield return null; }
                Assert.AreEqual(SurvivorState.Trapped, s.State, $"{s.Name} should be found when the player looks at them");
                var si = s.GetComponent<XRSimpleInteractable>();
                yield return Select(hand, si, 4f);   // check takes ~1.5 s, then the grip keeps holding: lifted
                Assert.AreEqual(SurvivorState.Carried, s.State, $"{s.Name} should be lifted after being checked and held");
                Assert.AreSame(s, Survivor.Carrying);
                var zone = s.Zone != null ? s.Zone : Object.FindFirstObjectByType<ExitZone>();
                MoveHeadTo(zone.transform.position);                    // carry them into the safe zone
                float tc = 0f; while (!zone.Contains(s.BodyCenter) && tc < 25f) { tc += Time.deltaTime; yield return null; }
                Assert.IsTrue(zone.Contains(s.BodyCenter), $"{s.Name} was carried into the safe zone");
                Release(hand, si);                                      // deliberate release inside the zone
                yield return null; yield return null;
                Assert.AreEqual(SurvivorState.Rescued, s.State, $"{s.Name} is evacuated when released inside the zone");
            }
        }

        static IEnumerator DoExit(MissionController m)
        {
            var exit = Object.FindFirstObjectByType<ExitZone>();
            Assert.IsNotNull(exit, "scene has an evacuation zone");
            MoveHeadTo(exit.transform.position);
            float t = 0f;
            while (m.State == MissionState.Active && t < 90f) { t += Time.deltaTime; yield return null; }
            Assert.AreEqual(MissionState.Completed, m.State, "mission completes once everyone reached the zone");
        }

        static void AssertResults(MissionUI ui, int stage)
        {
            Assert.IsTrue(ui.Results.IsVisible, "results panel shown");
            Assert.That(ui.LastScore100, Is.InRange(1, 100));
            Assert.That(ui.LastStars, Is.InRange(1, 3));
            Assert.IsTrue(CampaignProgress.IsCompleted(stage));
            Assert.AreEqual(ui.LastStars, CampaignProgress.BestStars(stage));
        }

        static IEnumerator DoFactoryHazards(MissionController m, NearFarInteractor hand)
        {
            // Breaker: select the lever (one press cuts power).
            var breaker = Object.FindFirstObjectByType<MainBreaker>();
            Assert.IsNotNull(breaker);
            MoveHeadTo(breaker.transform.position - breaker.transform.forward * 1.2f, breaker.transform.position);
            var bi = breaker.GetComponent<XRSimpleInteractable>();
            yield return Select(hand, bi, 0.3f); Release(hand, bi);
            Assert.IsFalse(breaker.IsLive, "breaker lever cuts the power");
            yield return Seconds(0.3f);
            Assert.IsTrue(m.Objectives.IsComplete(ObjectiveIds.Power));

            // Gas valve: hold the grip until the wheel has turned fully closed.
            var valve = Object.FindFirstObjectByType<GasValve>();
            Assert.IsNotNull(valve);
            MoveHeadTo(valve.transform.position - valve.transform.forward * 1.2f, valve.transform.position);
            var vi = valve.GetComponent<XRSimpleInteractable>();
            yield return Select(hand, vi, 5f); Release(hand, vi);
            Assert.IsTrue(valve.IsClosed, "gas valve closes while held");
            Assert.IsTrue(m.Objectives.IsComplete(ObjectiveIds.Gas));

            // Extinguisher: grab it, aim at every fire and spray (trigger is applied through the public SprayStep hook).
            var ext = Object.FindFirstObjectByType<Extinguisher>();
            Assert.IsNotNull(ext);
            var eg = ext.GetComponent<XRGrabInteractable>();
            MoveHeadTo(ext.transform.position - Vector3.forward * 1f);
            yield return Select(hand, eg, 0.3f);
            Assert.IsTrue(eg.isSelected, "extinguisher can be grabbed");
            var nozzleField = typeof(Extinguisher).GetField("nozzle", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var nozzle = (nozzleField?.GetValue(ext) as Transform) ?? ext.transform;
            foreach (var h in m.Hazards.OfType<RescueSim.Hazards.FireHazard>())
            {
                Vector3 dir = Vector3.ProjectOnPlane(h.transform.forward, Vector3.up);
                if (dir.sqrMagnitude < 0.01f) dir = Vector3.forward;
                nozzle.position = h.transform.position - dir.normalized * 1.5f;
                nozzle.rotation = Quaternion.LookRotation((h.transform.position - nozzle.position).normalized);
                float t = 0f;
                while (h.IsActive && t < 12f) { ext.SprayStep(0.25f); t += 0.25f; yield return null; }
                Assert.IsFalse(h.IsActive, $"fire '{h.name}' extinguished");
            }
            Release(hand, eg);
            yield return Seconds(0.3f);
            Assert.IsTrue(m.Objectives.IsComplete(ObjectiveIds.Fire));
        }

        [UnityTest]
        public IEnumerator Stage1_Factory_FullPlaythrough_ThenNextStage()
        {
            yield return Load("FactoryRescue");
            var ui = BeginMission(out var m);
            Time.timeScale = 3f;
            var hand = RightHand();
            yield return DoFactoryHazards(m, hand);
            yield return DoDebris(hand);   // collapsed passage that blocks two survivors
            Assert.IsTrue(m.Objectives.IsComplete(ObjectiveIds.Route));
            yield return DoSurvivors(m, hand);
            yield return DoExit(m);
            AssertResults(ui, 0);
            Assert.IsTrue(CampaignProgress.IsUnlocked(1), "completing stage 1 unlocks stage 2");
            ui.OnNext();
            float t = 0f; while (SceneManager.GetActiveScene().name != "EarthquakeRescue" && t < 15f) { t += Time.unscaledDeltaTime; yield return null; }
            Assert.AreEqual("EarthquakeRescue", SceneManager.GetActiveScene().name);
        }

        [UnityTest]
        public IEnumerator Stage2_Earthquake_FullPlaythrough_ThenNextStage()
        {
            yield return Load("EarthquakeRescue");
            var ui = BeginMission(out var m);
            Time.timeScale = 3f;
            var hand = RightHand();
            yield return DoZones(m);
            yield return DoDebris(hand);
            Assert.IsTrue(m.Objectives.IsComplete(ObjectiveIds.Route));
            yield return DoSurvivors(m, hand);
            Assert.IsTrue(m.Objectives.IsComplete(ObjectiveIds.Locate));
            yield return DoExit(m);
            AssertResults(ui, 1);
            Assert.IsTrue(CampaignProgress.IsUnlocked(2), "completing stage 2 unlocks stage 3");

            ui.OnNext();
            float t = 0f; while (SceneManager.GetActiveScene().name != "FloodEvacuation" && t < 15f) { t += Time.unscaledDeltaTime; yield return null; }
            Assert.AreEqual("FloodEvacuation", SceneManager.GetActiveScene().name);
        }

        [UnityTest]
        public IEnumerator Stage3_Flood_FullPlaythrough_ThenCampaignComplete()
        {
            yield return Load("FloodEvacuation");
            var ui = BeginMission(out var m);
            Time.timeScale = 3f;
            var hand = RightHand();
            yield return DoZones(m);
            yield return DoPickups(hand);
            Assert.IsTrue(m.Objectives.IsComplete(ObjectiveIds.Equip));
            yield return DoSurvivors(m, hand);
            yield return DoExit(m);
            AssertResults(ui, 2);

            ui.OnNext();   // final stage: campaign-complete panel instead of another scene
            yield return null;
            Assert.IsTrue(ui.Campaign.IsVisible, "campaign complete panel after the last stage");
            Assert.AreEqual("FloodEvacuation", SceneManager.GetActiveScene().name);
        }

        [UnityTest]
        public IEnumerator Retry_ReloadsStageInBriefing_AndMenuReturns()
        {
            yield return Load("EarthquakeRescue");
            var ui = BeginMission(out var m);
            ui.Restart();
            // Wait for the game's own reload (fade + async load) to produce a fresh mission; never start a second load ourselves.
            float t = 0f;
            MissionController m2 = null;
            while (t < 20f)
            {
                t += Time.unscaledDeltaTime; yield return null;
                m2 = Object.FindFirstObjectByType<MissionController>();
                if (m2 != null && m2 != m) break;
            }
            Assert.IsNotNull(m2);
            Assert.AreNotSame(m, m2, "stage was reloaded");
            yield return null; yield return null;
            Assert.AreEqual(MissionState.Briefing, m2.State, "retry returns to the briefing");
            Assert.AreEqual("EarthquakeRescue", SceneManager.GetActiveScene().name);

            Object.FindFirstObjectByType<MissionUI>().GoToMenu();
            t = 0f; while (SceneManager.GetActiveScene().name != StageCatalog.MenuScene && t < 20f) { t += Time.unscaledDeltaTime; yield return null; }
            Assert.AreEqual(StageCatalog.MenuScene, SceneManager.GetActiveScene().name);
        }
    }
}
