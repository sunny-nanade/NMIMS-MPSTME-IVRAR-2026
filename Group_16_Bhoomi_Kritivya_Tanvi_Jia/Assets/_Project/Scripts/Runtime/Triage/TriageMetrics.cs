using System;
using System.Collections.Generic;

namespace RescueSim.Triage
{
    /// <summary>Derived metrics, computed from raw records. Rates with a zero denominator report Has* = false (written as NA).</summary>
    public sealed class TriageSummary
    {
        public int total, classified, correct, trueRedTotal, trueRedUnderTriaged, invalidLatencyCount;
        public bool hasAccuracy, hasUnderTriageRate, hasLatency, isComplete;
        public double accuracy, underTriageRate, meanLatency, medianLatency, minLatency, maxLatency;
        /// <summary>[groundTruth, assigned] over Green, Yellow, Red, Black (index = category - 1).</summary>
        public int[,] confusion = new int[4, 4];
    }

    public static class TriageMetrics
    {
        /// <summary>
        /// Accuracy = correct / classified. Under-triage rate = true Red tagged Yellow or Green / true Red.
        /// Latency statistics use only records with a valid (non-negative) latency.
        /// </summary>
        public static TriageSummary Compute(IReadOnlyList<TriageRecord> records, int totalCasualties)
        {
            var s = new TriageSummary { total = totalCasualties };
            var lat = new List<double>();
            foreach (var r in records)
            {
                if (r.assigned == TriageCategory.None) continue;
                s.classified++;
                if (r.isCorrect) s.correct++;
                if (r.groundTruth == TriageCategory.Red)
                {
                    s.trueRedTotal++;
                    if (r.trueRedUnderTriaged) s.trueRedUnderTriaged++;
                }
                if (r.groundTruth != TriageCategory.None) s.confusion[(int)r.groundTruth - 1, (int)r.assigned - 1]++;
                if (r.latencyValid) lat.Add(r.latencySeconds); else s.invalidLatencyCount++;
            }
            s.hasAccuracy = s.classified > 0;
            s.accuracy = s.hasAccuracy ? (double)s.correct / s.classified : 0.0;
            s.hasUnderTriageRate = s.trueRedTotal > 0;
            s.underTriageRate = s.hasUnderTriageRate ? (double)s.trueRedUnderTriaged / s.trueRedTotal : 0.0;
            s.hasLatency = lat.Count > 0;
            if (s.hasLatency)
            {
                lat.Sort();
                double sum = 0; foreach (var x in lat) sum += x;
                s.meanLatency = sum / lat.Count; s.minLatency = lat[0]; s.maxLatency = lat[lat.Count - 1];
                int n = lat.Count;
                s.medianLatency = n % 2 == 1 ? lat[n / 2] : 0.5 * (lat[n / 2 - 1] + lat[n / 2]);
            }
            s.isComplete = totalCasualties > 0 && s.classified == totalCasualties;
            return s;
        }
    }
}
