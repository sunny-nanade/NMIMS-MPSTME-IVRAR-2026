using NUnit.Framework;
using RescueSim.Core;

namespace RescueSim.Tests
{
    public class CampaignProgressTests
    {
        ICampaignStore _prev;

        [SetUp] public void SetUp() { _prev = CampaignProgress.Store; CampaignProgress.Store = new MemoryStore(); }
        [TearDown] public void TearDown() { CampaignProgress.Store = _prev; }

        [Test] public void OnlyFirstStageUnlockedInitially()
        {
            Assert.IsTrue(CampaignProgress.IsUnlocked(0));
            Assert.IsFalse(CampaignProgress.IsUnlocked(1));
            Assert.IsFalse(CampaignProgress.IsUnlocked(2));
        }

        [Test] public void CompletingStageUnlocksNext_ButNotTheOneAfter()
        {
            CampaignProgress.RecordResult(0, 80, 2, true);
            Assert.IsTrue(CampaignProgress.IsUnlocked(1));
            Assert.IsFalse(CampaignProgress.IsUnlocked(2));
        }

        [Test] public void FailedAttemptDoesNotUnlock()
        {
            CampaignProgress.RecordResult(0, 30, 0, false);
            Assert.IsFalse(CampaignProgress.IsUnlocked(1));
            Assert.IsFalse(CampaignProgress.IsCompleted(0));
        }

        [Test] public void BestScoreAndStarsNeverDecrease()
        {
            CampaignProgress.RecordResult(0, 90, 3, true);
            CampaignProgress.RecordResult(0, 50, 1, true);
            Assert.AreEqual(90, CampaignProgress.BestScore(0));
            Assert.AreEqual(3, CampaignProgress.BestStars(0));
        }

        [Test] public void InvalidStageIsRejected()
        {
            Assert.IsFalse(CampaignProgress.RecordResult(-1, 50, 1, true));
            Assert.IsFalse(CampaignProgress.RecordResult(99, 50, 1, true));
            Assert.IsFalse(CampaignProgress.IsUnlocked(99));
            Assert.IsFalse(CampaignProgress.IsUnlocked(-1));
            Assert.IsFalse(CampaignProgress.IsCompleted(99));
            Assert.AreEqual(0, CampaignProgress.BestScore(99));
            Assert.AreEqual(0, CampaignProgress.CompletedCount());
        }

        [Test] public void UnlockAllDemoModeUnlocksEverything_AndResetClearsIt()
        {
            CampaignProgress.UnlockAll = true;
            Assert.IsTrue(CampaignProgress.IsUnlocked(2));
            CampaignProgress.ResetAll();
            Assert.IsFalse(CampaignProgress.UnlockAll);
            Assert.IsFalse(CampaignProgress.IsUnlocked(1));
        }

        [Test] public void ResetAllClearsScoresAndStars()
        {
            CampaignProgress.RecordResult(0, 90, 3, true);
            CampaignProgress.ResetAll();
            Assert.AreEqual(0, CampaignProgress.TotalStars());
            Assert.AreEqual(0, CampaignProgress.CompletedCount());
        }

        [Test] public void CampaignCompleteOnlyWhenAllStagesDone()
        {
            for (int i = 0; i < StageCatalog.Count - 1; i++) CampaignProgress.RecordResult(i, 70, 2, true);
            Assert.IsFalse(CampaignProgress.CampaignComplete);
            CampaignProgress.RecordResult(StageCatalog.Count - 1, 70, 2, true);
            Assert.IsTrue(CampaignProgress.CampaignComplete);
            Assert.AreEqual(StageCatalog.Count * 2, CampaignProgress.TotalStars());
        }

        [Test] public void StageCatalogNavigation()
        {
            Assert.AreEqual(3, StageCatalog.Count);
            Assert.AreEqual("EarthquakeRescue", StageCatalog.SceneFor(1));
            Assert.IsNull(StageCatalog.SceneFor(7));
            Assert.AreEqual(1, StageCatalog.NextAfter(0));
            Assert.AreEqual(2, StageCatalog.NextAfter(1));
            Assert.AreEqual(-1, StageCatalog.NextAfter(2));
            Assert.AreEqual(-1, StageCatalog.NextAfter(-5));
            Assert.IsTrue(StageCatalog.IsFinal(2));
            Assert.IsFalse(StageCatalog.IsFinal(0));
            Assert.AreEqual(2, StageCatalog.IndexOf("FloodEvacuation"));
            Assert.AreEqual(-1, StageCatalog.IndexOf("Nope"));
        }

        [TestCase(100, true, 3)] [TestCase(85, true, 3)] [TestCase(84, true, 2)]
        [TestCase(60, true, 2)] [TestCase(59, true, 1)] [TestCase(0, true, 1)] [TestCase(95, false, 0)]
        public void StarThresholds(int score, bool done, int stars) => Assert.AreEqual(stars, StageScoring.Stars(score, done));

        [Test] public void ToHundredScalesAndClamps()
        {
            Assert.AreEqual(50, StageScoring.ToHundred(500, 1000));
            Assert.AreEqual(100, StageScoring.ToHundred(2000, 1000));
            Assert.AreEqual(0, StageScoring.ToHundred(-5, 1000));
            Assert.AreEqual(0, StageScoring.ToHundred(500, 0));
        }

        [Test] public void HazardFreeStageCanStillScoreFull()
        {
            var r = ScoreCalculator.Calculate(new ScoreInput
            {
                completed = true, timeRemaining = 900f, timeLimit = 900f,
                survivorsRescued = 2, survivorsTotal = 2, hazardsNeutralized = 0, hazardsTotal = 0
            }, ScoreWeights.Default);
            Assert.AreEqual(1000, r.total);
        }
    }
}
