using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Fractured_Final.Tests
{
    /// <summary>Edit-mode tests for the pure logic of the study (no play mode, no headset).</summary>
    public class LogicTests
    {
        static MazeLayout Load(string name)
        {
            var ta = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/_Project/Data/MazeLayout" + name + ".json");
            Assert.IsNotNull(ta, "layout " + name + " is missing");
            return MazeLayout.Parse(ta.text);
        }

        // ------------------------------------------------------------------ design
        [TestCase(0, "S-A then M-B")]
        [TestCase(1, "S-B then M-A")]
        [TestCase(2, "M-A then S-B")]
        [TestCase(3, "M-B then S-A")]
        [TestCase(4, "S-A then M-B")]
        [TestCase(7, "M-B then S-A")]
        [TestCase(102, "M-A then S-B")]
        public void Participant_number_mod_4_gives_the_specified_order(int participant, string expected)
        {
            Assert.AreEqual(expected, ConditionRandomizer.OrderLabel(participant));
        }

        [Test]
        public void Every_order_has_one_spatial_and_one_stereo_block_and_both_layouts()
        {
            for (int p = 0; p < 4; p++)
            {
                var b1 = ConditionRandomizer.Plan(p, 1); var b2 = ConditionRandomizer.Plan(p, 2);
                Assert.AreNotEqual(b1.condition, b2.condition);
                Assert.AreNotEqual(b1.layout, b2.layout);
            }
        }

        [Test]
        public void The_four_orders_balance_condition_against_layout_and_block_position()
        {
            int spatialFirst = 0, spatialA = 0;
            for (int p = 0; p < 4; p++)
            {
                if (ConditionRandomizer.Plan(p, 1).condition == GuidanceCondition.Spatial) spatialFirst++;
                for (int b = 1; b <= 2; b++) { var plan = ConditionRandomizer.Plan(p, b); if (plan.condition == GuidanceCondition.Spatial && plan.layout == 'A') spatialA++; }
            }
            Assert.AreEqual(2, spatialFirst);
            Assert.AreEqual(2, spatialA);
        }

        [Test]
        public void Participant_ids_with_letters_use_their_digits()
        {
            StudyConfig.Data.participant_id = "B7";
            Assert.AreEqual(7, StudyConfig.ParticipantNumber);
            StudyConfig.Data.participant_id = "1";
        }

        // ------------------------------------------------------------------ constants
        [Test]
        public void Decision_constants_hold()
        {
            Assert.AreEqual(1.65f, DS.EYE_HEIGHT, 1e-6f);
            Assert.AreEqual(72, DS.TARGET_FPS);
            Assert.AreEqual(11f, DS.EDITOR_FRAME_BUDGET_MS, 1e-6f);
            Assert.IsTrue(SetupController.ConsentText.StartsWith("This is a research study about guidance audio in a VR evacuation."));
            Assert.IsTrue(SetupController.ConsentText.EndsWith("tell the experimenter at once."));
        }

        // ------------------------------------------------------------------ maze
        [TestCase("A")]
        [TestCase("B")]
        public void Layouts_meet_the_specification(string name)
        {
            var l = Load(name);
            var m = l.Verify();
            Assert.AreEqual(18, m.steps, "shortest path steps (18 x 2 m = 36 m)");
            Assert.IsTrue(m.unique, "unique shortest path");
            Assert.AreEqual(6, m.junctions);
            Assert.AreEqual(3, m.deadEnds);
            Assert.GreaterOrEqual(m.misleading, 2);
            Assert.AreEqual(36f, l.Data.shortestMeters, 0.01f);
            Assert.AreEqual(6, l.Grid);
            Assert.AreEqual(2f, l.Cell, 1e-6f);
            Assert.AreEqual(19, l.Path.Count);
        }

        [Test]
        public void Layout_geometry_helpers_agree()
        {
            var l = Load("A");
            var c = new MazeCell(3, 2);
            Assert.IsTrue(l.TryCellOf(l.CellCenter(c), out var back));
            Assert.AreEqual(c, back);
            Assert.IsFalse(l.TryCellOf(new Vector3(-0.1f, 0, 1f), out _));
            Assert.AreEqual(0, l.PathIndex(l.Start));
            Assert.AreEqual(18, l.PathIndex(l.Exit));
        }

        [Test]
        public void The_two_layouts_are_different_mazes()
        {
            var a = Load("A"); var b = Load("B");
            Assert.AreNotEqual(string.Join(",", a.Data.cells), string.Join(",", b.Data.cells));
            Assert.AreNotEqual(a.Exit, b.Exit);
        }

        // ------------------------------------------------------------------ questionnaires and records
        [Test]
        public void Sus_scoring_follows_the_standard()
        {
            Assert.AreEqual(100f, QuestionnaireUI.ScoreSus(new[] { 5, 1, 5, 1, 5, 1, 5, 1, 5, 1 }), 1e-4f);
            Assert.AreEqual(0f, QuestionnaireUI.ScoreSus(new[] { 1, 5, 1, 5, 1, 5, 1, 5, 1, 5 }), 1e-4f);
            Assert.AreEqual(50f, QuestionnaireUI.ScoreSus(new[] { 3, 3, 3, 3, 3, 3, 3, 3, 3, 3 }), 1e-4f);
            Assert.AreEqual(10, QuestionnaireUI.SusItems.Length);
            Assert.AreEqual(6, QuestionnaireUI.TlxNames.Length);
        }

        [Test]
        public void Plaque_text_matches_the_specification()
        {
            Assert.AreEqual("MISSION LOG. Aethelgard, Deck 4 evacuation. Participant 12. Block 1 1:35. Block 2 2:10.", SaveLoad.Plaque("12", 95f, 130f));
            Assert.AreEqual("0:59", SaveLoad.FormatTime(59.9f));
            Assert.AreEqual("4:00", SaveLoad.FormatTime(240f));
        }

        // ------------------------------------------------------------------ ear check and audio
        [Test]
        public void Beacon_game_has_six_distinct_colours()
        {
            Assert.AreEqual(6, EarCheckController.ColorNames.Length);
            Assert.AreEqual(6, EarCheckController.Colors.Length);
            Assert.AreEqual(6, new System.Collections.Generic.HashSet<Color>(EarCheckController.Colors).Count);
        }

        [Test]
        public void Block_only_rose_lines_are_not_narrated_by_the_comms_director()
        {
            foreach (var id in new[] { "R201", "R205", "R210" }) Assert.IsTrue(NarrativeVoiceDirector.IsBlockOnly(id), id);
            foreach (var id in new[] { "R200", "R211", "R150", "H201", "R20" }) Assert.IsFalse(NarrativeVoiceDirector.IsBlockOnly(id), id);
        }

        [Test]
        public void Sfx_events_map_to_manifest_clips()
        {
            var assets = AssetDatabase.LoadAssetAtPath<DeepSleepAssets>("Assets/_Project/Resources/DeepSleepAssets.asset");
            Assert.IsNotNull(assets);
            assets.InvalidateCache();
            foreach (var kv in SfxDirector.ClipFor) Assert.IsTrue(assets.Has(kv.Value), kv.Key + " -> " + kv.Value);
        }

        [Test]
        public void Stereo_trim_curve_interpolates_and_falls_back()
        {
            var a = ScriptableObject.CreateInstance<DeepSleepAssets>();
            a.stereoGainTrimDb = -6f;
            Assert.AreEqual(-6f, a.StereoTrimAt(5f), 1e-4f);
            a.stereoTrimDistances = new[] { 1f, 11f }; a.stereoTrimDb = new[] { -8f, -4f };
            Assert.AreEqual(-8f, a.StereoTrimAt(0.5f), 1e-4f);
            Assert.AreEqual(-6f, a.StereoTrimAt(6f), 1e-4f);
            Assert.AreEqual(-4f, a.StereoTrimAt(40f), 1e-4f);
            Object.DestroyImmediate(a);
        }

        [Test]
        public void World_text_scale_gives_the_requested_glyph_height()
        {
            // TMP font size 1 is about 11 cm of glyph height; FontScale and Cm() compensate so Cm(m) draws about m metres
            float fontSize = WorldUI.Cm(0.05f) * WorldUI.FontScale;
            Assert.AreEqual(0.05f, fontSize * 0.112f, 0.006f);
        }
    }
}
