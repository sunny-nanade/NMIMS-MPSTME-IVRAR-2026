using System.Collections;
using System.Linq;
using NUnit.Framework;
using RescueSim.Core;
using RescueSim.Hands;
using RescueSim.Survivors;
using RescueSim.Triage;
using RescueSim.UI;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace RescueSim.Tests
{
    /// <summary>
    /// Human casualty models in every scene, and the hand-input path (pinch grab, latch, release, hand-over) driven through a
    /// SIMULATED hand source. These exercise the same code real hand tracking feeds, but they are not a headset test.
    /// </summary>
    public class HumanAndHandTests
    {
        MissionController _m;
        HandTrackingInput _hands;
        SimulatedHandSource _src;
        Survivor _s;
        XROrigin _origin;
        int _started, _ended;
        string _dir;

        [SetUp] public void SetUp() { _dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "hand_play_" + System.Guid.NewGuid().ToString("N")); }
        [TearDown] public void TearDown() { Time.timeScale = 1f; if (System.IO.Directory.Exists(_dir)) System.IO.Directory.Delete(_dir, true); }

        static IEnumerator Load(string scene)
        {
            var op = SceneManager.LoadSceneAsync(scene, LoadSceneMode.Single);
            Assert.IsNotNull(op, $"Scene '{scene}' is not in Build Settings");
            while (!op.isDone) yield return null;
            yield return null; yield return null; yield return null;
        }

        static IEnumerator Seconds(float s) { float t = 0f; while (t < s) { t += Time.deltaTime; yield return null; } }

        // ---- every scene uses the human models -------------------------------------------------------------------------

        static void AssertHuman(Survivor s)
        {
            var app = s.GetComponent<CasualtyAppearance>();
            Assert.IsNotNull(app, s.Name + ": CasualtyAppearance missing");
            Assert.IsTrue(app.HasHumanBody, s.Name + ": no human body");
            Assert.IsNotEmpty(app.Avatar, s.Name + ": avatar not recorded");
            var visual = s.transform.Find("Visual");
            Assert.IsNotNull(visual);
            foreach (var mf in visual.GetComponentsInChildren<MeshFilter>(true))
                Assert.That(mf.sharedMesh == null || !new[] { "Capsule", "Sphere", "Cube", "Cylinder" }.Contains(mf.sharedMesh.name), s.Name + ": primitive placeholder left in the body");
            int tris = visual.GetComponentsInChildren<SkinnedMeshRenderer>(true).Sum(r => { int n = 0; for (int i = 0; i < r.sharedMesh.subMeshCount; i++) n += (int)r.sharedMesh.GetIndexCount(i) / 3; return n; });
            Assert.Less(tris, 12000, s.Name + ": too many triangles for Quest 2");
            Assert.IsEmpty(visual.GetComponentsInChildren<Collider>(true), s.Name + ": body mesh must not add colliders");
            Assert.IsEmpty(visual.GetComponentsInChildren<Animator>(true), s.Name + ": animator would fight the carry pose");
        }

        [UnityTest]
        public IEnumerator Stages_UseHumanModels_AndKeepCasualtyNames([ValueSource(nameof(StageCases))] string[] c)
        {
            yield return Load(c[0]);
            var m = Object.FindFirstObjectByType<MissionController>();
            CollectionAssert.AreEquivalent(c.Skip(1).ToArray(), m.Survivors.Select(s => s.Name).ToArray(), "casualty identities preserved");
            foreach (var s in m.Survivors) AssertHuman(s);
            var avatars = m.Survivors.Select(s => s.GetComponent<CasualtyAppearance>().Avatar).Distinct().Count();
            Assert.Greater(avatars, 1, "casualties should not all wear the same avatar");
        }

        static readonly string[][] StageCases =
        {
            new[] { "FactoryRescue", "Dana", "Marcus", "Priya" },
            new[] { "EarthquakeRescue", "Amira", "Ben" },
            new[] { "FloodEvacuation", "Lena", "Omar" },
        };

        [UnityTest]
        public IEnumerator Triage_UsesHumanModels_KeepsIds_AndHasNoCarrying()
        {
            yield return Load(StageCatalog.TriageScene);
            var mgr = Object.FindFirstObjectByType<STARTTriageSimulationManager>();
            Assert.AreEqual(10, mgr.Casualties.Count);
            CollectionAssert.AreEqual(Enumerable.Range(1, 10).Select(i => "C" + i.ToString("00")), mgr.Casualties.Select(c => c.Id));
            foreach (var c in mgr.Casualties)
            {
                var app = c.GetComponent<CasualtyAppearance>();
                Assert.IsNotNull(app, c.Id); Assert.IsTrue(app.HasHumanBody, c.Id);
                foreach (var mf in c.GetComponentsInChildren<MeshFilter>(true).Where(f => f.name == "Torso" || f.name == "Head" || f.name == "Vest"))
                    Assert.Fail(c.Id + ": primitive placeholder '" + mf.name + "' left in the body");
            }
            Assert.Greater(mgr.Casualties.Select(c => c.GetComponent<CasualtyAppearance>().Avatar).Distinct().Count(), 4, "variety");
            Assert.IsEmpty(Object.FindObjectsByType<Survivor>(FindObjectsSortMode.None), "Triage Study must not be a carrying exercise");
        }

        // ---- hand input on the real Earthquake scene ---------------------------------------------------------------------

        IEnumerator SetupHands()
        {
            yield return Load("FloodEvacuation");
            _m = Object.FindFirstObjectByType<MissionController>();
            var ui = Object.FindFirstObjectByType<MissionUI>();
            ui.Briefing.GetComponentsInChildren<Button>(true).First().onClick.Invoke();
            Assert.AreEqual(MissionState.Active, _m.State);
            _hands = Object.FindFirstObjectByType<HandTrackingInput>();
            Assert.IsNotNull(_hands, "HandTrackingInput should be added to the rig automatically");
            _src = new SimulatedHandSource();
            _hands.Source = _src;
            _hands.ControllersTrackedOverride = false;
            _origin = Object.FindFirstObjectByType<XROrigin>();
            _s = _m.Survivors.First(x => x.Name == "Lena");
            _m.Objectives.Complete(ObjectiveIds.Equip);               // Lena may only be moved once the rescue buoy is collected
            _started = _ended = 0;
            _s.CarryStarted += _ => _started++;
            _s.CarryEnded += (_, __) => _ended++;
            MoveHead(_s.transform.position + new Vector3(2.4f, 1.65f, 1.0f), _s.BodyCenter);
            yield return Seconds(0.3f);
        }

        void MoveHead(Vector3 world, Vector3 lookAt)
        {
            Vector3 d = lookAt - world; d.y = 0f;
            if (d.sqrMagnitude > 0.01f) _origin.transform.rotation = Quaternion.LookRotation(d.normalized, Vector3.up);
            _origin.MoveCameraToWorldLocation(world);
        }

        void Hand(bool right, Vector3 at, bool pinch) => _src.Set(right, at, pinch ? 0.01f : 0.12f);

        IEnumerator HoldUntil(System.Func<bool> cond, float timeout, System.Action each = null)
        {
            float t = 0f;
            while (t < timeout && !cond()) { each?.Invoke(); t += Time.deltaTime; yield return null; }
        }

        IEnumerator GrabWithHand()
        {
            Vector3 p = _s.BodyCenter;
            Hand(true, p, false);                                   // hand arrives open, then pinches (a pinch must be a fresh gesture)
            yield return Seconds(0.5f);
            Hand(true, p, true);
            var trace = new System.Text.StringBuilder(); float tt = 0f, nx = 0f;
            yield return HoldUntil(() => _s.State == SurvivorState.Carried, 5f, () =>
            {
                Hand(true, _s.BodyCenter, true); tt += Time.deltaTime;
                if (tt >= nx) { nx += 0.5f; var r = _hands.RightInteractor; trace.Append($"[{tt:0.0}s h={r.hasHover} s={r.hasSelection} a={_s.AssessProgress:0.00} on={(r.firstInteractableSelected as Component)?.name} hov=[{string.Join(",", r.interactablesHovered.Select(h => (h as Component)?.name))}] own={_s.Owner != null} st={_s.State}] "); }
            });
            var ia = _hands.RightInteractor;
            Assert.AreEqual(SurvivorState.Carried, _s.State, "pinching at the casualty lifts them | active=" + _hands.RightHandActive + " pinching=" + _hands.RightPinching +
                " hover=" + ia.hasHover + " sel=" + ia.hasSelection + " iaPos=" + ia.transform.position + " body=" + _s.BodyCenter + " col=" + _s.GetComponent<Collider>().bounds +
                " near=" + (ia.nearInteractionCaster == null ? "null" : ia.nearInteractionCaster.GetType().Name + "/" + (ia.nearInteractionCaster is UnityEngine.XR.Interaction.Toolkit.Interactors.Casters.SphereInteractionCaster sc ? sc.physicsTriggerInteraction + "/mask" + sc.physicsLayerMask.value + "/r" + sc.castRadius : "")) + " ov=[" + string.Join(",", Physics.OverlapSphere(ia.transform.position, 0.1f, ~0, QueryTriggerInteraction.Collide).Select(c => c.name)) + "] reg=" + (ia.interactionManager.TryGetInteractableForCollider(_s.GetComponent<Collider>(), out var regI) ? "yes" : "NO") + " en=" + _s.GetComponent<XRBaseInteractable>().enabled + " ncol=" + _s.GetComponent<XRBaseInteractable>().colliders.Count + " org=" + ia.nearInteractionCaster.effectiveCastOrigin.position + " layerS=" + _s.gameObject.layer + " ilS=" + _s.GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRBaseInteractable>().interactionLayers.value + " ilI=" + ia.interactionLayers.value + " trace=" + trace + " colTrig=" + _s.GetComponent<Collider>().isTrigger + " state=" + _s.State);
        }

        [UnityTest]
        public IEnumerator Hand_Pinch_GrabsAndCarries_ThroughTheRigsInteractor()
        {
            yield return SetupHands();
            yield return GrabWithHand();
            Assert.AreSame(_s, Survivor.Carrying);
            Assert.AreSame(_hands.RightInteractor, _s.Owner, "the same interactor that controllers use owns the casualty");
            Assert.IsTrue(_hands.RightHandActive);
            Assert.AreEqual(1, _started);
            // Carried casualty follows the player, as with controllers.
            MoveHead(_origin.Camera.transform.position + _origin.transform.forward * 1.5f, _origin.Camera.transform.position + _origin.transform.forward * 5f);
            Hand(true, _origin.Camera.transform.position + _origin.transform.forward * 0.6f + Vector3.down * 0.5f, false);
            yield return Seconds(1.5f);
            Assert.AreEqual(SurvivorState.Carried, _s.State);
            Assert.Less(Vector3.Distance(new Vector3(_s.BodyCenter.x, 0, _s.BodyCenter.z), new Vector3(_origin.Camera.transform.position.x, 0, _origin.Camera.transform.position.z)), 1.2f);
        }

        [UnityTest]
        public IEnumerator Hand_Carry_StaysLatched_WhenFingersRelax()
        {
            yield return SetupHands();
            yield return GrabWithHand();
            Hand(true, _s.BodyCenter, false);                       // open hand
            yield return Seconds(2f);
            Assert.AreEqual(SurvivorState.Carried, _s.State, "relaxing the fingers must not drop the casualty");
            Assert.IsTrue(_hands.RightLatched);
            Assert.AreEqual(0, _ended);
        }

        [UnityTest]
        public IEnumerator Hand_NewPinch_ReleasesOutsideZone_NoCredit_AndCasualtyCanBeRecovered()
        {
            yield return SetupHands();
            yield return GrabWithHand();
            Hand(true, _s.BodyCenter, false);
            yield return Seconds(1.6f);
            Hand(true, _s.BodyCenter, true);                        // deliberate second pinch = put down
            yield return HoldUntil(() => _s.State == SurvivorState.Released, 2f);
            Assert.AreEqual(SurvivorState.Released, _s.State, "released outside the zone");
            Assert.AreEqual(0, _m.SurvivorsRescued, "no rescue credit outside the safe zone");
            Assert.AreEqual(1, _ended);
            // Recover: open the hand, then pinch again.
            Hand(true, _s.BodyCenter, false);
            yield return Seconds(1.2f);
            Hand(true, _s.BodyCenter, true);
            yield return HoldUntil(() => _s.State == SurvivorState.Carried, 5f, () => Hand(true, _s.BodyCenter, true));
            Assert.AreEqual(SurvivorState.Carried, _s.State, "a dropped casualty can be picked up again");
            Assert.AreEqual(2, _started);
            Assert.AreEqual(0, _m.SurvivorsRescued);
        }

        [UnityTest]
        public IEnumerator Hand_Delivery_InsideSafeZone_CreditsExactlyOnce()
        {
            yield return SetupHands();
            yield return GrabWithHand();
            var zone = _s.Zone;
            Vector3 zc = zone.transform.position; zc.y += 0.5f;   // the Flood safe zone is raised; stand inside its volume
            MoveHead(zc, zc + Vector3.forward * 3f);
            Hand(true, zc + Vector3.down * 0.5f, false);
            yield return HoldUntil(() => zone.Contains(_s.BodyCenter), 30f);
            Assert.IsTrue(zone.Contains(_s.BodyCenter), "casualty reached the zone | body=" + _s.BodyCenter + " zone=" + zone.transform.position + " head=" + _origin.Camera.transform.position + " state=" + _s.State);
            yield return Seconds(1.2f);
            Hand(true, zc + Vector3.down * 0.5f, true);             // pinch again to put down
            yield return HoldUntil(() => _s.State == SurvivorState.Rescued, 3f);
            Assert.AreEqual(SurvivorState.Rescued, _s.State);
            Assert.AreEqual(1, _m.SurvivorsRescued);
            // Pinching again (and again) does not score or re-grab.
            for (int i = 0; i < 4; i++)
            {
                Hand(true, _s.BodyCenter, false); yield return Seconds(0.5f);
                Hand(true, _s.BodyCenter, true); yield return Seconds(0.5f);
            }
            Assert.AreEqual(1, _m.SurvivorsRescued, "a rescued casualty cannot be scored again");
            Assert.AreEqual(SurvivorState.Rescued, _s.State);
            Assert.IsNull(Survivor.Carrying);
        }

        [UnityTest]
        public IEnumerator Hand_TrackingLoss_ShorterThanGrace_DoesNotDrop()
        {
            yield return SetupHands();
            yield return GrabWithHand();
            _src.Lose(true);
            yield return Seconds(0.6f);
            Assert.AreEqual(SurvivorState.Carried, _s.State);
            Hand(true, _s.BodyCenter, false);
            yield return Seconds(0.5f);
            Assert.AreEqual(SurvivorState.Carried, _s.State);
            Assert.AreEqual(1, _started); Assert.AreEqual(0, _ended);
        }

        [UnityTest]
        public IEnumerator SwitchingToControllers_KeepsTheCasualty_ThenReleasesAfterGrace_WithoutDuplicates()
        {
            yield return SetupHands();
            yield return GrabWithHand();
            _hands.ControllersTrackedOverride = true;                // player picks up controllers; hand no longer drives the interactor
            yield return Seconds(1.0f);
            Assert.AreEqual(SurvivorState.Carried, _s.State, "hand-over: casualty stays held");
            Assert.IsTrue(_s.InHandover);
            Assert.AreEqual(1, _started, "no duplicate carry event"); Assert.AreEqual(0, _ended);
            Assert.AreSame(_s, Survivor.Carrying);
            yield return Seconds(2.6f);                              // nobody takes over: normal release (recoverable)
            Assert.AreEqual(SurvivorState.Released, _s.State);
            Assert.AreEqual(0, _m.SurvivorsRescued);
        }

        [UnityTest]
        public IEnumerator SwitchingBackToHands_DuringHandover_ResumesTheSameCarry()
        {
            yield return SetupHands();
            yield return GrabWithHand();
            _hands.ControllersTrackedOverride = true;
            yield return Seconds(0.8f);
            Assert.IsTrue(_s.InHandover);
            _hands.ControllersTrackedOverride = false;               // hands again
            Hand(true, _s.BodyCenter, true);
            yield return HoldUntil(() => !_s.InHandover && _s.Owner != null, 3f, () => Hand(true, _s.BodyCenter, true));
            Assert.AreEqual(SurvivorState.Carried, _s.State);
            Assert.AreSame(_hands.RightInteractor, _s.Owner);
            Assert.AreEqual(1, _started, "the carry was resumed, not restarted"); Assert.AreEqual(0, _ended);
        }

        [UnityTest]
        public IEnumerator BothHandsPinching_StillOneOwner_AndOneCarry()
        {
            yield return SetupHands();
            Hand(true, _s.BodyCenter, false); Hand(false, _s.BodyCenter, false);
            yield return Seconds(0.5f);
            Hand(true, _s.BodyCenter, true); Hand(false, _s.BodyCenter, true);
            yield return HoldUntil(() => _s.State == SurvivorState.Carried, 5f, () => { Hand(true, _s.BodyCenter, true); Hand(false, _s.BodyCenter, true); });
            Assert.AreEqual(SurvivorState.Carried, _s.State);
            Assert.IsNotNull(_s.Owner);
            Assert.AreEqual(1, _started);
            yield return Seconds(1f);
            Assert.AreEqual(1, _started, "second hand cannot start a second carry or take the casualty");
        }

        [UnityTest]
        public IEnumerator Hand_Restart_ResetsCasualtyAndInputState()
        {
            yield return SetupHands();
            yield return GrabWithHand();
            _s.ResetSurvivor();
            Assert.AreEqual(SurvivorState.Unfound, _s.State);
            Assert.IsNull(Survivor.Carrying);
            Assert.IsFalse(_s.InHandover);
            Assert.IsNull(_s.Owner);
        }

        // ---- Triage: hands select, never carry --------------------------------------------------------------------------

        [UnityTest]
        public IEnumerator Triage_HandPinch_SelectsACasualty_WithoutMovingIt()
        {
            yield return Load(StageCatalog.TriageScene);
            var mgr = Object.FindFirstObjectByType<STARTTriageSimulationManager>();
            mgr.Logger.SetDirectoryOverride(_dir);
            Assert.IsTrue(mgr.BeginSession());
            yield return null;
            var hands = Object.FindFirstObjectByType<HandTrackingInput>();
            Assert.IsNotNull(hands);
            var src = new SimulatedHandSource(); hands.Source = src; hands.ControllersTrackedOverride = false;
            var origin = Object.FindFirstObjectByType<XROrigin>();
            var c = mgr.Casualties[3];
            Vector3 start = c.transform.position;
            Vector3 target = c.GetComponent<Collider>().bounds.center;
            Vector3 head = target + new Vector3(0, 0, -2.2f); head.y = 1.65f;
            origin.transform.rotation = Quaternion.LookRotation(Vector3.forward);
            origin.MoveCameraToWorldLocation(head);
            yield return Seconds(0.3f);
            src.Set(true, target, 0.01f);
            yield return HoldUntil(() => mgr.Active == c, 4f, () => src.Set(true, target, 0.01f));
            Assert.AreSame(c, mgr.Active, "pinch selects the casualty for assessment");
            src.Set(true, target, 0.12f);
            yield return Seconds(2f);
            Assert.Less(Vector3.Distance(start, c.transform.position), 0.01f, "selecting never lifts or moves a casualty");
            Assert.IsEmpty(Object.FindObjectsByType<Survivor>(FindObjectsSortMode.None));
        }
    }
}
