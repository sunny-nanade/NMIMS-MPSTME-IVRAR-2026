using System.Linq;
using NUnit.Framework;
using RescueSim.Hands;
using RescueSim.Survivors;
using UnityEditor;
using UnityEngine;
using UnityEngine.XR.OpenXR;

namespace RescueSim.Tests
{
    /// <summary>Pure-logic tests for the hand input layer plus checks of the character assets and XR configuration.</summary>
    public class HandAndCharacterTests
    {
        static readonly string[] Avatars =
        {
            "Male_Adult_01", "Female_Adult_01", "Male_Adult_05", "Female_Adult_04",
            "Male_Adult_10", "Female_Adult_08", "Construction_Male_01", "Construction_Female_01",
        };

        // ---- PinchDetector -------------------------------------------------------------------------------------------

        [Test]
        public void Pinch_StartsBelowOnThreshold_AndStaysInHysteresisBand()
        {
            var p = new PinchDetector();
            p.Update(true, 0.06f, 0.016f); Assert.IsFalse(p.Pinching);
            p.Update(true, 0.02f, 0.016f); Assert.IsTrue(p.Pinching); Assert.IsTrue(p.PressedThisFrame);
            p.Update(true, 0.035f, 1f);    Assert.IsTrue(p.Pinching, "between on and off thresholds the pinch holds, however long");
            Assert.IsFalse(p.PressedThisFrame);
        }

        [Test]
        public void Pinch_NeedsDebouncedOpenHand_ToRelease()
        {
            var p = new PinchDetector();
            p.Update(true, 0.01f, 0.016f);
            p.Update(true, 0.08f, 0.05f); Assert.IsTrue(p.Pinching, "a flicker shorter than the debounce does not release");
            p.Update(true, 0.01f, 0.016f); Assert.IsTrue(p.Pinching);
            p.Update(true, 0.08f, 0.05f); p.Update(true, 0.08f, 0.05f); p.Update(true, 0.08f, 0.05f);
            Assert.IsFalse(p.Pinching); Assert.IsTrue(p.ReleasedThisFrame, "release fires once, after the debounce");
            p.Update(true, 0.08f, 0.05f); Assert.IsFalse(p.ReleasedThisFrame);
        }

        [Test]
        public void Pinch_SurvivesShortTrackingLoss_ButNotALongOne()
        {
            var p = new PinchDetector();
            p.Update(true, 0.01f, 0.016f);
            p.Update(false, 1f, 0.2f); Assert.IsTrue(p.Pinching, "0.2 s without tracking keeps the pinch");
            p.Update(true, 0.01f, 0.016f); Assert.IsTrue(p.Pinching);
            p.Update(false, 1f, 0.2f); p.Update(false, 1f, 0.2f);
            Assert.IsFalse(p.Pinching, "0.4 s without tracking ends it");
        }

        // ---- CarryLatch ----------------------------------------------------------------------------------------------

        [Test]
        public void Latch_KeepsHolding_WhileFingersRelax()
        {
            var l = new CarryLatch();
            Assert.IsTrue(l.Update(true, true, true, 0.016f), "lifting with the pinch held");
            for (int i = 0; i < 100; i++) Assert.IsTrue(l.Update(true, false, false, 0.05f), "open hand while carrying stays latched");
            Assert.IsTrue(l.Latched);
        }

        [Test]
        public void Latch_IgnoresPinchRightAfterLift_AndReleasesOnADeliberateSecondPinch()
        {
            var l = new CarryLatch();
            l.Update(true, true, true, 0.016f);
            Assert.IsTrue(l.Update(true, true, true, 0.1f), "a second 'press' within the first moments is ignored");
            Assert.IsFalse(l.ReleaseRequested);
            for (int i = 0; i < 30; i++) l.Update(true, false, false, 0.05f);          // hand opens for 1.5 s
            bool hold = l.Update(true, true, true, 0.016f);                            // deliberate new pinch
            Assert.IsTrue(l.ReleaseRequested); Assert.IsFalse(hold, "release drops the hold at once");
            Assert.IsFalse(l.Latched);
        }

        [Test]
        public void Latch_AfterRelease_NeedsOpenHandBeforeANewGrab()
        {
            var l = new CarryLatch();
            l.Update(true, true, true, 0.016f);
            for (int i = 0; i < 30; i++) l.Update(true, false, false, 0.05f);
            l.Update(true, true, true, 0.016f);                                         // release pinch
            Assert.IsFalse(l.Update(false, true, false, 0.9f), "still pinching after the release: no instant re-grab");
            Assert.IsFalse(l.Update(false, true, false, 0.9f));
            Assert.IsFalse(l.Update(false, false, false, 0.05f), "hand opens");
            Assert.IsTrue(l.Update(false, true, true, 0.05f), "a fresh pinch grabs again");
        }

        [Test]
        public void Latch_ResetsWhenCarryEndsOtherwise()
        {
            var l = new CarryLatch();
            l.Update(true, true, true, 0.016f);
            Assert.IsTrue(l.Latched);
            l.Update(false, false, false, 0.016f);
            Assert.IsFalse(l.Latched, "carry ended some other way (e.g. controller release)");
        }

        // ---- Simulated source ----------------------------------------------------------------------------------------

        [Test]
        public void SimulatedSource_ReportsPinchGapAndTracking()
        {
            var s = new SimulatedHandSource();
            Assert.IsFalse(s.TryGetHand(true, out _));
            s.Set(true, new Vector3(1, 1, 1), 0.02f);
            Assert.IsTrue(s.TryGetHand(true, out var h));
            Assert.AreEqual(0.02f, h.PinchDistance, 1e-4f);
            s.Lose(true);
            Assert.IsFalse(s.TryGetHand(true, out _));
        }

        // ---- Character assets ----------------------------------------------------------------------------------------

        [Test]
        public void EveryAvatar_HasAHumanPrefab_WithOneSkinnedBodyAndNoColliders()
        {
            foreach (var a in Avatars)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/_Project/Prefabs/Characters/Human_{a}.prefab");
                Assert.IsNotNull(prefab, $"{a}: run Rescue Sim > Characters > Rebuild human prefabs");
                var skins = prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                Assert.AreEqual(1, skins.Length, a);
                Assert.IsEmpty(prefab.GetComponentsInChildren<Collider>(true), $"{a}: visual only, no colliders");
                Assert.IsEmpty(prefab.GetComponentsInChildren<Animator>(true), $"{a}: no animator competing with carrying");
                int tris = skins[0].sharedMesh.triangles.Length / 3;
                Assert.Less(tris, 12000, $"{a}: {tris} triangles is too heavy for Quest 2");
                foreach (var m in skins[0].sharedMaterials) Assert.IsNotNull(m, $"{a}: missing material");
            }
        }

        [Test]
        public void AvatarLicense_IsShippedWithTheAssets()
        {
            var lic = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/_Project/Art/Characters/Rocketbox/LICENSE-Rocketbox-MIT.txt");
            Assert.IsNotNull(lic);
            StringAssert.Contains("MIT License", lic.text);
            StringAssert.Contains("Microsoft-Rocketbox", lic.text);
        }

        [Test]
        public void Appearance_VariantIsStable_AndSpreadsAcrossNames()
        {
            Assert.AreEqual(CasualtyAppearance.VariantFor("C01"), CasualtyAppearance.VariantFor("C01"));
            var set = Enumerable.Range(1, 10).Select(i => CasualtyAppearance.VariantFor("C" + i.ToString("00"))).Distinct().Count();
            Assert.GreaterOrEqual(set, 4, "ten triage casualties should not all look the same");
        }

        // ---- XR configuration ----------------------------------------------------------------------------------------

        [Test]
        public void QuestBuild_EnablesOpenXrHandTracking_AndTheMetaAimExtension()
        {
            var s = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Android);
            Assert.IsNotNull(s);
            var hands = s.GetFeature<UnityEngine.XR.Hands.OpenXR.HandTracking>();
            Assert.IsNotNull(hands, "Hand Tracking Subsystem feature missing");
            Assert.IsTrue(hands.enabled, "Hand Tracking Subsystem must be enabled for Quest (run Rescue Sim > Configure XR and Android)");
            var aim = s.GetFeature<UnityEngine.XR.Hands.OpenXR.MetaHandTrackingAim>();
            Assert.IsNotNull(aim);
            Assert.IsTrue(aim.enabled, "Meta Hand Tracking Aim should be enabled");
        }
    }
}
