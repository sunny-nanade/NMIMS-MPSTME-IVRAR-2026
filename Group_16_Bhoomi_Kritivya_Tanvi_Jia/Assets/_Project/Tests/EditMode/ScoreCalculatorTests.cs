using NUnit.Framework;
using RescueSim.Core;

namespace RescueSim.Tests
{
    public class ScoreCalculatorTests
    {
        static ScoreInput Perfect() => new ScoreInput
        {
            completed = true, timeRemaining = 480f, timeLimit = 480f,
            survivorsRescued = 3, survivorsTotal = 3, hazardsNeutralized = 3, hazardsTotal = 3,
            peakExposure = 0f, mistakes = 0
        };

        [Test]
        public void PerfectRun_ScoresMaxAndRankS()
        {
            var r = ScoreCalculator.Calculate(Perfect(), ScoreWeights.Default);
            Assert.AreEqual(1000, r.total);
            Assert.AreEqual("S", r.rank);
            Assert.AreEqual(500, r.survivorPoints);
            Assert.AreEqual(300, r.hazardPoints);
            Assert.AreEqual(200, r.timeBonus);
        }

        [Test]
        public void FailedMission_HasNoTimeBonusAndIsIncomplete()
        {
            var i = Perfect(); i.completed = false;
            var r = ScoreCalculator.Calculate(i, ScoreWeights.Default);
            Assert.AreEqual(0, r.timeBonus);
            Assert.AreEqual("INCOMPLETE", r.rank);
        }

        [Test]
        public void Mistakes_ReduceScoreLinearly()
        {
            var i = Perfect(); i.mistakes = 4;
            var r = ScoreCalculator.Calculate(i, ScoreWeights.Default);
            Assert.AreEqual(100, r.mistakePenalty);
            Assert.AreEqual(900, r.total);
        }

        [Test]
        public void Exposure_PenaltyScalesWithPeak()
        {
            var i = Perfect(); i.peakExposure = 0.5f;
            var r = ScoreCalculator.Calculate(i, ScoreWeights.Default);
            Assert.AreEqual(75, r.exposurePenalty);
            Assert.AreEqual(925, r.total);
        }

        [Test]
        public void Exposure_AboveOneIsClamped()
        {
            var i = Perfect(); i.peakExposure = 5f;
            Assert.AreEqual(150, ScoreCalculator.Calculate(i, ScoreWeights.Default).exposurePenalty);
        }

        [Test]
        public void Total_NeverNegative()
        {
            var i = new ScoreInput { completed = true, timeLimit = 100f, survivorsTotal = 3, hazardsTotal = 3, peakExposure = 1f, mistakes = 50 };
            Assert.AreEqual(0, ScoreCalculator.Calculate(i, ScoreWeights.Default).total);
        }

        [Test]
        public void ZeroTotals_DoNotDivideByZero()
        {
            var r = ScoreCalculator.Calculate(new ScoreInput { completed = true }, ScoreWeights.Default);
            // No survivors/time limit: nothing to divide by. A hazard-free stage that is completed still earns the hazard share.
            Assert.AreEqual(0, r.survivorPoints);
            Assert.AreEqual(0, r.timeBonus);
            Assert.AreEqual(r.hazardPoints, r.total);
            Assert.AreEqual(0, ScoreCalculator.Calculate(new ScoreInput { completed = false }, ScoreWeights.Default).hazardPoints);
        }

        [Test]
        public void PartialRescue_GivesProportionalPoints()
        {
            var i = Perfect(); i.survivorsRescued = 1;
            Assert.AreEqual(167, ScoreCalculator.Calculate(i, ScoreWeights.Default).survivorPoints);
        }

        [TestCase(1000, "S")] [TestCase(900, "S")] [TestCase(899, "A")] [TestCase(750, "A")]
        [TestCase(749, "B")] [TestCase(550, "B")] [TestCase(549, "C")] [TestCase(350, "C")]
        [TestCase(349, "D")] [TestCase(0, "D")]
        public void RankThresholds(int total, string expected)
            => Assert.AreEqual(expected, ScoreCalculator.RankFor(total, true));
    }
}
