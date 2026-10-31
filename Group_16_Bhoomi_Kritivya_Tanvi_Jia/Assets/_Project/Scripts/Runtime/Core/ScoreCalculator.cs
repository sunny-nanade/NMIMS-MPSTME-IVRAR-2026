using System;
using UnityEngine;

namespace RescueSim.Core
{
    [Serializable]
    public struct ScoreWeights
    {
        public int survivorPoints;
        public int hazardPoints;
        public int timeBonusPoints;
        public int maxExposurePenalty;
        public int mistakePenalty;
        public int maxScore;

        public static ScoreWeights Default => new ScoreWeights
        {
            survivorPoints = 500,
            hazardPoints = 300,
            timeBonusPoints = 200,
            maxExposurePenalty = 150,
            mistakePenalty = 25,
            maxScore = 1000
        };
    }

    public struct ScoreInput
    {
        public bool completed;
        public float timeRemaining;
        public float timeLimit;
        public int survivorsRescued;
        public int survivorsTotal;
        public int hazardsNeutralized;
        public int hazardsTotal;
        /// <summary>Highest player exposure reached, 0..1.</summary>
        public float peakExposure;
        public int mistakes;
    }

    public struct ScoreResult
    {
        public bool completed;
        public int survivorPoints;
        public int hazardPoints;
        public int timeBonus;
        public int exposurePenalty;
        public int mistakePenalty;
        public int total;
        public string rank;
    }

    /// <summary>Pure scoring maths with no scene dependencies, so it can be unit tested.</summary>
    public static class ScoreCalculator
    {
        public static ScoreResult Calculate(ScoreInput input, ScoreWeights w)
        {
            var r = new ScoreResult { completed = input.completed };

            float survivorFraction = input.survivorsTotal > 0 ? Mathf.Clamp01((float)input.survivorsRescued / input.survivorsTotal) : 0f;
            // Stages without hazards award the hazard share automatically so every stage can reach a full score.
            float hazardFraction = input.hazardsTotal > 0 ? Mathf.Clamp01((float)input.hazardsNeutralized / input.hazardsTotal) : (input.completed ? 1f : 0f);
            float timeFraction = input.timeLimit > 0f ? Mathf.Clamp01(input.timeRemaining / input.timeLimit) : 0f;

            r.survivorPoints = Mathf.RoundToInt(w.survivorPoints * survivorFraction);
            r.hazardPoints = Mathf.RoundToInt(w.hazardPoints * hazardFraction);
            r.timeBonus = input.completed ? Mathf.RoundToInt(w.timeBonusPoints * timeFraction) : 0;
            r.exposurePenalty = Mathf.RoundToInt(w.maxExposurePenalty * Mathf.Clamp01(input.peakExposure));
            r.mistakePenalty = Mathf.Max(0, input.mistakes) * w.mistakePenalty;

            int raw = r.survivorPoints + r.hazardPoints + r.timeBonus - r.exposurePenalty - r.mistakePenalty;
            r.total = Mathf.Clamp(raw, 0, w.maxScore);
            r.rank = RankFor(r.total, input.completed);
            return r;
        }

        public static string RankFor(int total, bool completed)
        {
            if (!completed) return "INCOMPLETE";
            if (total >= 900) return "S";
            if (total >= 750) return "A";
            if (total >= 550) return "B";
            if (total >= 350) return "C";
            return "D";
        }
    }
}
