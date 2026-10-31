using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using RescueSim.Core;
using RescueSim.Hazards;
using RescueSim.Interaction;
using RescueSim.Player;
using RescueSim.Survivors;
using UnityEngine;
using UnityEngine.TestTools;

namespace RescueSim.Tests
{
    /// <summary>Mission logic with synthetic objects (no scene), exercising the real runtime classes.</summary>
    public class MissionFlowTests
    {
        readonly List<GameObject> _spawned = new List<GameObject>();
        MissionDefinition _def;

        static void SetPrivate(object target, string field, object value)
        {
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public;
            for (var t = target.GetType(); t != null; t = t.BaseType)   // private fields live on base classes too
            {
                var f = t.GetField(field, flags);
                if (f != null) { f.SetValue(target, value); return; }
            }
            Assert.Fail($"field {field} not found on {target.GetType().Name} or its base classes");
        }

        MissionDefinition MakeDef()
        {
            var d = ScriptableObject.CreateInstance<MissionDefinition>();
            d.timeLimitSeconds = 60f;
            d.objectives = new List<MissionDefinition.ObjectiveDefinition>
            {
                new MissionDefinition.ObjectiveDefinition { id = ObjectiveIds.Fire, title = "Fire", required = true },
                new MissionDefinition.ObjectiveDefinition { id = ObjectiveIds.Locate, title = "Locate", required = true },
                new MissionDefinition.ObjectiveDefinition { id = ObjectiveIds.Evacuate, title = "Evacuate", required = true },
            };
            return d;
        }

        FireHazard MakeFire(string objective)
        {
            var go = new GameObject("fire"); _spawned.Add(go);
            var f = go.AddComponent<FireHazard>();
            SetPrivate(f, "objectiveId", objective);
            return f;
        }

        Survivor MakeSurvivor()
        {
            var go = new GameObject("survivor"); _spawned.Add(go);
            go.SetActive(false);
            go.AddComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRSimpleInteractable>();
            go.AddComponent<UnityEngine.AI.NavMeshAgent>();
            var s = go.AddComponent<Survivor>();
            go.SetActive(true);
            return s;
        }

        MissionController MakeMission(List<Hazard> hz, List<Survivor> sv, out PlayerVitals vitals)
        {
            var vgo = new GameObject("vitals"); _spawned.Add(vgo);
            vitals = vgo.AddComponent<PlayerVitals>();
            var go = new GameObject("mission"); _spawned.Add(go);
            go.SetActive(false);
            var m = go.AddComponent<MissionController>();
            _def = MakeDef();
            m.Configure(_def, vitals, hz, sv);
            go.SetActive(true);
            return m;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var g in _spawned) if (g != null) Object.Destroy(g);
            _spawned.Clear();
            if (_def != null) Object.Destroy(_def);
            Time.timeScale = 1f;
        }

        [UnityTest]
        public IEnumerator Mission_StartsInBriefing_ThenActive_ThenPauseResume()
        {
            var fire = MakeFire(ObjectiveIds.Fire);
            var m = MakeMission(new List<Hazard> { fire }, new List<Survivor> { MakeSurvivor() }, out _);
            yield return null;
            Assert.AreEqual(MissionState.Briefing, m.State);
            m.BeginMission();
            Assert.AreEqual(MissionState.Active, m.State);
            m.Pause();
            Assert.AreEqual(MissionState.Paused, m.State);
            Assert.AreEqual(0f, Time.timeScale);
            m.Resume();
            Assert.AreEqual(MissionState.Active, m.State);
            Assert.AreEqual(1f, Time.timeScale);
        }

        [UnityTest]
        public IEnumerator FullRescue_CompletesMission_WithScore()
        {
            var fire = MakeFire(ObjectiveIds.Fire);
            var s1 = MakeSurvivor(); var s2 = MakeSurvivor();
            var m = MakeMission(new List<Hazard> { fire }, new List<Survivor> { s1, s2 }, out _);
            yield return null;
            m.BeginMission();

            fire.Neutralize();
            Assert.IsTrue(m.Objectives.IsComplete(ObjectiveIds.Fire));
            Assert.AreEqual(MissionState.Active, m.State, "Mission must not finish before survivors are out");

            s1.ForceState(SurvivorState.Trapped);
            s2.ForceState(SurvivorState.Trapped);
            Assert.IsTrue(m.Objectives.IsComplete(ObjectiveIds.Locate));
            s1.ForceState(SurvivorState.Rescued);
            Assert.AreEqual(MissionState.Active, m.State);
            s2.ForceState(SurvivorState.Rescued);

            Assert.AreEqual(MissionState.Completed, m.State);
            Assert.IsTrue(m.LastResult.HasValue);
            Assert.AreEqual("S", m.LastResult.Value.rank);
            Assert.AreEqual(2, m.SurvivorsRescued);
        }

        [UnityTest]
        public IEnumerator AllSurvivorsLost_DoesNotComplete()
        {
            var fire = MakeFire(ObjectiveIds.Fire);
            var s = MakeSurvivor();
            var m = MakeMission(new List<Hazard> { fire }, new List<Survivor> { s }, out _);
            yield return null;
            m.BeginMission();
            fire.Neutralize();
            s.ForceState(SurvivorState.Trapped);
            s.ForceState(SurvivorState.Lost);
            Assert.AreNotEqual(MissionState.Completed, m.State);
            Assert.AreEqual(1, m.SurvivorsLost);
        }

        [UnityTest]
        public IEnumerator PlayerOvercome_FailsMission()
        {
            var m = MakeMission(new List<Hazard> { MakeFire(ObjectiveIds.Fire) }, new List<Survivor>(), out var vitals);
            yield return null;
            m.BeginMission();
            vitals.AddInstant(1f);
            Assert.AreEqual(MissionState.Failed, m.State);
            Assert.AreEqual("INCOMPLETE", m.LastResult.Value.rank);
            Assert.IsNotEmpty(m.FailureReason);
        }

        [UnityTest]
        public IEnumerator Timeout_FailsMission()
        {
            var m = MakeMission(new List<Hazard>(), new List<Survivor>(), out _);
            yield return null;
            m.BeginMission();
            SetPrivate(m, "<TimeRemaining>k__BackingField", 0.05f);
            yield return new WaitForSeconds(0.3f);
            Assert.AreEqual(MissionState.Failed, m.State);
        }

        [UnityTest]
        public IEnumerator Mistakes_OnlyCountWhileActive()
        {
            var m = MakeMission(new List<Hazard>(), new List<Survivor>(), out _);
            yield return null;
            m.ReportMistake("early");
            Assert.AreEqual(0, m.Mistakes);
            m.BeginMission();
            m.ReportMistake("now");
            Assert.AreEqual(1, m.Mistakes);
        }

        [UnityTest]
        public IEnumerator Fire_ExtinguishedBySpray_AndRegrowsIfAbandoned()
        {
            var fire = MakeFire(ObjectiveIds.Fire);
            var m = MakeMission(new List<Hazard> { fire }, new List<Survivor>(), out _);
            yield return null;
            m.BeginMission();
            fire.ApplyExtinguisher(2f);
            Assert.AreEqual(0.5f, fire.Intensity, 0.01f);
            Assert.IsTrue(fire.IsActive);
            yield return new WaitForSeconds(0.5f);
            Assert.Greater(fire.Intensity, 0.5f, "fire should slowly regrow");
            fire.ApplyExtinguisher(10f);
            Assert.IsFalse(fire.IsActive);
            Assert.AreEqual(0f, fire.Intensity);
        }

        [UnityTest]
        public IEnumerator PassageMonitor_CompletesRouteWhenDebrisLeaves()
        {
            var def = MakeDef();
            def.objectives.Add(new MissionDefinition.ObjectiveDefinition { id = ObjectiveIds.Route, title = "Route", required = true });
            var vgo = new GameObject("v"); _spawned.Add(vgo); var vit = vgo.AddComponent<PlayerVitals>();
            var mgo = new GameObject("m"); _spawned.Add(mgo); mgo.SetActive(false);
            var m = mgo.AddComponent<MissionController>();
            m.Configure(def, vit, new List<Hazard>(), new List<Survivor>());
            mgo.SetActive(true);
            yield return null;
            m.BeginMission();

            var debris = new GameObject("debris"); _spawned.Add(debris);
            debris.SetActive(false);
            debris.AddComponent<Rigidbody>();
            debris.AddComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable>();
            var item = debris.AddComponent<DebrisItem>();
            debris.SetActive(true);
            debris.GetComponent<Rigidbody>().isKinematic = true;

            var pgo = new GameObject("passage"); _spawned.Add(pgo);
            var pm = pgo.AddComponent<PassageMonitor>();
            pm.Configure(m, new[] { item });

            pm.Evaluate();
            Assert.AreEqual(1, pm.ItemsInside);
            Assert.IsFalse(pm.IsCleared);
            Assert.IsFalse(m.Objectives.IsComplete(ObjectiveIds.Route));

            debris.transform.position = new Vector3(50f, 0f, 0f);
            pm.Evaluate();
            Assert.IsTrue(pm.IsCleared);
            Assert.IsTrue(m.Objectives.IsComplete(ObjectiveIds.Route));
            Object.Destroy(def);
        }

        [UnityTest]
        public IEnumerator Breaker_Cut_IsIdempotent_AndRaisesEvent()
        {
            var go = new GameObject("breaker"); _spawned.Add(go);
            go.SetActive(false);
            go.AddComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRSimpleInteractable>();
            var b = go.AddComponent<MainBreaker>();
            go.SetActive(true);
            yield return null;
            int n = 0; b.PowerCut += () => n++;
            Assert.IsTrue(b.IsLive);
            b.Cut(); b.Cut();
            Assert.IsFalse(b.IsLive);
            Assert.AreEqual(1, n);
        }
    }
}
