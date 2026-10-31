using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using RescueSim.Core;
using RescueSim.Hazards;
using RescueSim.Interaction;
using RescueSim.Player;
using RescueSim.Survivors;
using RescueSim.Triage;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace RescueSim.Tests
{
    /// <summary>
    /// Carry / evacuation behaviour: synthetic objects for the state model and scoring, real scenes for configuration.
    /// Controller grip is represented by Survivor.HoldOverride in the synthetic tests (the XRI path is covered in
    /// CampaignPlaythroughTests through the rig's own interactor). Not a headset test.
    /// </summary>
    public class CarryEvacuationTests
    {
        readonly List<GameObject> _spawned = new List<GameObject>();
        MissionDefinition _def;
        Transform _head;
        ExitZone _zone;

        [SetUp]
        public void SetUp()
        {
            var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.name = "TestGround"; ground.transform.position = new Vector3(0, -0.5f, 0); ground.transform.localScale = new Vector3(100, 1, 100);
            _spawned.Add(ground);
            _head = new GameObject("TestHead").transform; _head.position = new Vector3(0, 1.6f, 0); _spawned.Add(_head.gameObject);
            var zg = new GameObject("TestZone"); zg.transform.position = new Vector3(0, 1.5f, 12f); _zone = zg.AddComponent<ExitZone>(); _zone.Size = new Vector3(6, 3, 6);
            _spawned.Add(zg);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var g in _spawned) if (g != null) Object.DestroyImmediate(g);
            _spawned.Clear();
            if (_def != null) Object.DestroyImmediate(_def);
            Time.timeScale = 1f;
        }

        Survivor Make(Vector3 pos)
        {
            var go = new GameObject("casualty"); _spawned.Add(go);
            go.SetActive(false);
            go.transform.position = pos;
            var col = go.AddComponent<BoxCollider>(); col.isTrigger = true; col.size = new Vector3(0.9f, 1.1f, 2f); col.center = new Vector3(0, 0.55f, -0.7f);
            go.AddComponent<XRSimpleInteractable>();
            go.AddComponent<UnityEngine.AI.NavMeshAgent>();
            var s = go.AddComponent<Survivor>();
            s.Configure(null, _head, _zone, null);
            go.SetActive(true);
            return s;
        }

        MissionController MakeMission(List<Survivor> sv)
        {
            var vgo = new GameObject("vitals"); _spawned.Add(vgo);
            var vitals = vgo.AddComponent<PlayerVitals>();
            var go = new GameObject("mission"); _spawned.Add(go);
            go.SetActive(false);
            var m = go.AddComponent<MissionController>();
            _def = ScriptableObject.CreateInstance<MissionDefinition>();
            _def.timeLimitSeconds = 60f;
            _def.objectives = new List<MissionDefinition.ObjectiveDefinition>
            {
                new MissionDefinition.ObjectiveDefinition { id = ObjectiveIds.Locate, title = "Locate", required = true },
                new MissionDefinition.ObjectiveDefinition { id = ObjectiveIds.Evacuate, title = "Evacuate", required = true },
            };
            m.Configure(_def, vitals, new List<Hazard>(), sv);
            go.SetActive(true);
            return m;
        }

        static string Prog(MissionController m) { m.Objectives.TryGet(ObjectiveIds.Evacuate, out var st); return st.Progress; }

        static IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }
        static IEnumerator Seconds(float s) { float t = 0f; while (t < s) { t += Time.deltaTime; yield return null; } }

        static void Carry(Survivor s)
        {
            s.ForceState(SurvivorState.Trapped);
            s.HoldOverride = true;
            s.ForceState(SurvivorState.Carried);
        }

        // 1
        [UnityTest]
        public IEnumerator Grab_EntersCarriedState_AndOnlyOneIsCarriedAtATime()
        {
            var a = Make(new Vector3(0, 0, 3)); var b = Make(new Vector3(2, 0, 3));
            Carry(a);
            yield return null;
            Assert.AreEqual(SurvivorState.Carried, a.State);
            Assert.AreSame(a, Survivor.Carrying);
            b.ForceState(SurvivorState.Trapped); b.HoldOverride = true;
            yield return Frames(3);
            Assert.AreEqual(SurvivorState.Trapped, b.State, "second casualty cannot be lifted while one is carried");
            Assert.AreSame(a, Survivor.Carrying);
        }

        // 2
        [UnityTest]
        public IEnumerator Carried_FollowsAnchor_SmoothlyAndStaysAboveGround()
        {
            var s = Make(new Vector3(4, 0, 4));
            Carry(s);
            yield return Seconds(2f);
            Vector3 expected = _head.position + Vector3.forward * 0.6f;
            Vector3 c = s.BodyCenter;
            Assert.Less(Vector2.Distance(new Vector2(c.x, c.z), new Vector2(expected.x, expected.z)), 0.25f, "body centre sits in front of the player");
            Assert.Greater(s.transform.position.y, -0.05f, "not below the floor");
            Assert.Less(c.y, _head.position.y, "carried below head height");
            // Moving the player moves the casualty without a jump larger than the max carry speed.
            Vector3 before = s.transform.position;
            _head.position += new Vector3(1f, 0, 0);
            yield return null;
            Assert.Less(Vector3.Distance(before, s.transform.position), 0.3f, "no teleport on the first frame");
            yield return Seconds(1.5f);
            Assert.Less(Mathf.Abs(s.BodyCenter.x - (_head.position.x)), 0.3f);
        }

        [UnityTest]
        public IEnumerator Carried_IsNotPushedThroughAWall()
        {
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube); _spawned.Add(wall);
            wall.transform.position = new Vector3(0, 1.5f, 0.9f); wall.transform.localScale = new Vector3(8, 3, 0.2f);
            var s = Make(new Vector3(0, 0, -3));
            Carry(s);
            yield return Seconds(1.5f);
            Assert.Less(s.BodyCenter.z, 0.8f, "casualty stays on the player's side of the wall");
        }

        // 3
        [UnityTest]
        public IEnumerator ReleaseOutsideZone_NoCredit_StaysRecarryable()
        {
            var s = Make(new Vector3(0, 0, 3));
            var m = MakeMission(new List<Survivor> { s });
            yield return null; m.BeginMission();
            int rescued = 0; s.Rescued += _ => rescued++;
            Carry(s);
            yield return Seconds(0.8f);
            s.HoldOverride = false;
            yield return Frames(2);
            Assert.AreEqual(SurvivorState.Released, s.State);
            Assert.IsNull(Survivor.Carrying);
            Assert.AreEqual(0, rescued); Assert.AreEqual(0, m.SurvivorsRescued);
            yield return Seconds(0.6f);
            Assert.IsTrue(s.gameObject.activeInHierarchy, "stays in the scene");
            Assert.Less(Mathf.Abs(s.transform.position.y), 0.05f, "settled on the ground, not falling through");
            // Picked up again and carried.
            s.HoldOverride = true;
            yield return Frames(3);
            Assert.AreEqual(SurvivorState.Carried, s.State);
        }

        // 4 + 5
        [UnityTest]
        public IEnumerator DeliverInsideZone_CreditsOnce_AndRescuedCannotBeScoredOrGrabbedAgain()
        {
            var s = Make(new Vector3(0, 0, 3));
            var m = MakeMission(new List<Survivor> { s });
            yield return null; m.BeginMission();
            int rescued = 0; s.Rescued += _ => rescued++;
            Carry(s);
            _head.position = new Vector3(0, 1.6f, 12f);
            yield return Seconds(3f);
            Assert.IsTrue(_zone.Contains(s.BodyCenter));
            Assert.IsTrue(_zone.CarrierInside, "zone shows the delivery glow");
            s.HoldOverride = false;
            yield return Frames(2);
            Assert.AreEqual(SurvivorState.Rescued, s.State);
            Assert.AreEqual(1, rescued); Assert.AreEqual(1, m.SurvivorsRescued);
            Assert.IsFalse(_zone.CarrierInside);
            // Attempts to change or re-score a rescued casualty.
            s.ForceState(SurvivorState.Rescued); s.ForceState(SurvivorState.Trapped); s.ForceState(SurvivorState.Released); s.ForceState(SurvivorState.Carried);
            s.HoldOverride = true;
            yield return Frames(3);
            Assert.AreEqual(SurvivorState.Rescued, s.State);
            Assert.IsNull(Survivor.Carrying);
            Assert.AreEqual(1, rescued); Assert.AreEqual(1, m.SurvivorsRescued);
            Assert.IsFalse(s.GetComponent<XRSimpleInteractable>().enabled, "grabbing is disabled once rescued");
        }

        [UnityTest]
        public IEnumerator EnteringZoneWithoutCasualty_GivesNoCredit()
        {
            var s = Make(new Vector3(0, 0, 3));
            var m = MakeMission(new List<Survivor> { s });
            yield return null; m.BeginMission();
            s.ForceState(SurvivorState.Trapped);
            _head.position = _zone.transform.position;       // the player alone in the zone
            yield return Seconds(0.5f);
            Assert.AreEqual(SurvivorState.Trapped, s.State);
            Assert.AreEqual(0, m.SurvivorsRescued);
        }

        // 6
        [UnityTest]
        public IEnumerator MultipleCasualties_UpdateObjectiveCount()
        {
            var a = Make(new Vector3(-2, 0, 3)); var b = Make(new Vector3(2, 0, 3));
            var m = MakeMission(new List<Survivor> { a, b });
            yield return null; m.BeginMission();
            foreach (var s in new[] { a, b })
            {
                Carry(s);
                _head.position = new Vector3(0, 1.6f, 12f);
                yield return Seconds(3f);
                s.HoldOverride = false;
                yield return Frames(2);
                Assert.AreEqual(SurvivorState.Rescued, s.State);
                _head.position = new Vector3(0, 1.6f, 0f);
                if (s == a) { Assert.AreEqual(1, m.SurvivorsRescued); Assert.AreEqual("1/2", Prog(m)); }
            }
            Assert.AreEqual(2, m.SurvivorsRescued);
            Assert.AreEqual("2/2", Prog(m));
            Assert.AreEqual(MissionState.Completed, m.State);
        }

        // 7
        [UnityTest]
        public IEnumerator Reset_RestoresInitialState_AndClearsCarry()
        {
            var s = Make(new Vector3(1, 0, 3));
            Vector3 home = s.transform.position;
            Carry(s);
            yield return Seconds(1f);
            s.ResetSurvivor();
            Assert.AreEqual(SurvivorState.Unfound, s.State, "back to the initial state straight after reset");
            Assert.IsNull(Survivor.Carrying, "no persistent carry after reset");
            Assert.Less(Vector3.Distance(s.transform.position, home), 0.01f);
            Assert.IsFalse(_zone.CarrierInside);
            Assert.IsTrue(s.GetComponent<XRSimpleInteractable>().enabled);
            Assert.AreEqual(1f, s.Health);
            // Rescued then reset can be rescued again in a fresh run.
            Carry(s);
            _head.position = new Vector3(0, 1.6f, 12f);
            yield return Seconds(3f);
            s.HoldOverride = false; yield return Frames(2);
            Assert.AreEqual(SurvivorState.Rescued, s.State);
            s.ResetSurvivor();
            Assert.IsTrue(s.State == SurvivorState.Unfound || s.State == SurvivorState.Trapped);
            Assert.IsTrue(s.GetComponent<XRSimpleInteractable>().enabled);
        }

        [UnityTest]
        public IEnumerator DestroyingCarriedCasualty_ClearsCarryGuard()
        {
            var s = Make(new Vector3(0, 0, 3));
            Carry(s);
            yield return null;
            Object.DestroyImmediate(s.gameObject);
            Assert.IsNull(Survivor.Carrying);
        }

        // 8
        [UnityTest]
        public IEnumerator Triage_SelectionAndTagging_UnchangedByCarrySystem()
        {
            var op = SceneManager.LoadSceneAsync(StageCatalog.TriageScene, LoadSceneMode.Single);
            while (!op.isDone) yield return null;
            yield return Frames(2);
            var m = Object.FindFirstObjectByType<STARTTriageSimulationManager>();
            Assert.IsNotNull(m);
            Assert.IsEmpty(Object.FindObjectsByType<Survivor>(FindObjectsSortMode.None), "triage uses no carryable rescue casualties");
            foreach (var c in Object.FindObjectsByType<CasualtySelectable>(FindObjectsSortMode.None))
                Assert.IsNull(c.GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable>(), "triage casualties are not grabbable");
            string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "carry_triage_" + System.Guid.NewGuid().ToString("N"));
            m.Logger.SetDirectoryOverride(dir);
            Assert.IsTrue(m.BeginSession());
            Assert.IsTrue(m.SelectCasualty(m.Casualties[0]));
            m.Assign(TriageCategory.Green);
            Assert.AreEqual(TriageCategory.Green, m.Casualties[0].Tag);
            m.Restart();
            if (System.IO.Directory.Exists(dir)) System.IO.Directory.Delete(dir, true);
        }

        // 9
        [UnityTest]
        public IEnumerator Factory_ExtinguisherAndDebris_StillGrabbableAndWired()
        {
            var op = SceneManager.LoadSceneAsync("FactoryRescue", LoadSceneMode.Single);
            while (!op.isDone) yield return null;
            yield return Frames(2);
            var ext = Object.FindFirstObjectByType<Extinguisher>();
            Assert.IsNotNull(ext); Assert.IsNotNull(ext.GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable>());
            var debris = Object.FindObjectsByType<DebrisItem>(FindObjectsSortMode.None);
            Assert.IsNotEmpty(debris);
            foreach (var d in debris) Assert.IsNotNull(d.GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable>());
        }

        // 10
        [UnityTest]
        public IEnumerator AllRescueStages_HaveCarryableCasualtiesAndSafeZones()
        {
            foreach (var scene in new[] { "FactoryRescue", "EarthquakeRescue", "FloodEvacuation" })
            {
                var op = SceneManager.LoadSceneAsync(scene, LoadSceneMode.Single);
                Assert.IsNotNull(op, scene + " in build settings");
                while (!op.isDone) yield return null;
                yield return Frames(2);
                var m = Object.FindFirstObjectByType<MissionController>();
                Assert.IsNotNull(m, scene);
                Assert.IsNotEmpty(m.Survivors, scene);
                foreach (var s in m.Survivors)
                {
                    Assert.IsTrue(s.CarryMode, $"{scene}/{s.Name} carry mode");
                    Assert.IsNotNull(s.Zone, $"{scene}/{s.Name} has a safe zone");
                    Assert.IsNotNull(s.GetComponent<XRSimpleInteractable>(), $"{scene}/{s.Name} interactable");
                    Assert.IsNotNull(s.GetComponent<Collider>(), $"{scene}/{s.Name} collider");
                    Assert.AreEqual(SurvivorState.Unfound, s.State);
                }
                var zones = Object.FindObjectsByType<ExitZone>(FindObjectsSortMode.None);
                Assert.IsNotEmpty(zones, scene);
                foreach (var z in zones)
                {
                    Assert.Greater(z.Size.x * z.Size.z, 4f, $"{scene}/{z.name} zone has area");
                    Assert.Greater(z.GetComponentsInChildren<MeshRenderer>().Length, 0, $"{scene}/{z.name} visible boundary");
                }
                Assert.IsNull(Survivor.Carrying, "no carry state carried across scene load");
            }
        }
    }
}
