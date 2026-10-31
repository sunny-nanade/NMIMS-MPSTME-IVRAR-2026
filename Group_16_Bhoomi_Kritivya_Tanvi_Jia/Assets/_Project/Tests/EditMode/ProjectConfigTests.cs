using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using RescueSim.Core;
using UnityEditor;

namespace RescueSim.Tests
{
    /// <summary>Validates authored data and project configuration (no Play Mode needed).</summary>
    public class ProjectConfigTests
    {
        const string MissionPath = "Assets/_Project/Data/Mission_NightShiftFactoryFire.asset";

        [Test]
        public void MissionAsset_HasAllCoreObjectivesWithUniqueIds()
        {
            var m = AssetDatabase.LoadAssetAtPath<MissionDefinition>(MissionPath);
            Assert.IsNotNull(m, "Mission asset missing: run Rescue Sim > Build > Everything");
            var ids = m.objectives.Select(o => o.id).ToList();
            CollectionAssert.AllItemsAreUnique(ids);
            foreach (var id in new[] { ObjectiveIds.Power, ObjectiveIds.Gas, ObjectiveIds.Fire, ObjectiveIds.Route, ObjectiveIds.Locate, ObjectiveIds.Evacuate })
                CollectionAssert.Contains(ids, id);
            Assert.GreaterOrEqual(m.timeLimitSeconds, 30f);
            Assert.IsNotEmpty(m.briefing);
            Assert.IsTrue(m.objectives.Any(o => o.required));
        }

        [Test]
        public void BuildSettings_MenuFirst_ThenMission()
        {
            var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => System.IO.Path.GetFileNameWithoutExtension(s.path)).ToList();
            Assert.GreaterOrEqual(scenes.Count, 2);
            Assert.AreEqual("MainMenu", scenes[0]);
            CollectionAssert.Contains(scenes, "FactoryRescue");
        }

        [Test]
        public void Android_IsConfiguredForQuest()
        {
            Assert.AreEqual(UnityEditor.AndroidArchitecture.ARM64, PlayerSettings.Android.targetArchitectures & UnityEditor.AndroidArchitecture.ARM64);
            Assert.GreaterOrEqual((int)PlayerSettings.Android.minSdkVersion, 29, "Quest 3 requires Android 10+ (API 29)");
            Assert.IsFalse(PlayerSettings.GetUseDefaultGraphicsAPIs(BuildTarget.Android), "Android should use an explicit graphics API list");
            var apis = PlayerSettings.GetGraphicsAPIs(BuildTarget.Android);
            CollectionAssert.Contains(apis, UnityEngine.Rendering.GraphicsDeviceType.Vulkan, "Quest builds should use Vulkan");
        }
    }
}
