using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using RescueSim.Core;
using RescueSim.Triage;
using UnityEditor;

namespace RescueSim.Tests
{
    /// <summary>START evaluator, session, metrics, CSV export and scenario tests. Pure C#; no scene needed.</summary>
    public class TriageTests
    {
        const PerfusionCriterion Cap = PerfusionCriterion.CapillaryRefill;
        const PerfusionCriterion Pulse = PerfusionCriterion.RadialPulse;

        static VitalSigns V(bool walk = false, bool breathing = true, bool afterAirway = false, int rr = 18, float refill = 1.5f, bool pulse = true, bool commands = true)
            => new VitalSigns { canWalk = walk, isBreathing = breathing, breathingAfterAirwayReposition = afterAirway, respiratoryRate = rr, capillaryRefillSeconds = refill, hasRadialPulse = pulse, followsCommands = commands };

        sealed class Clock
        {
            public double t;
            public DateTime utc = new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);
            public double Now() => t;
            public DateTime Utc() => utc.AddSeconds(t);
        }

        static TriageSession NewSession(Clock clk, params (string id, VitalSigns v)[] cases)
            => new TriageSession(cases.Select(c => new CasualtyCase(c.id, c.v)), Cap, clk.Now, clk.Utc);

        static TriageSession Standard(Clock clk) => NewSession(clk,
            ("A", V(walk: true)), ("B", V(rr: 40)), ("C", V()), ("D", V(breathing: false)));

        // ---- START evaluator: every branch ---------------------------------------------------------

        [Test] public void Walking_IsGreen_EvenWithBadVitals()
        {
            var r = STARTEvaluator.Evaluate(V(walk: true, rr: 45, refill: 5f, commands: false, breathing: false), Cap);
            Assert.AreEqual(TriageCategory.Green, r.category); Assert.AreEqual(TriageBranch.Walking, r.branch);
        }

        [Test] public void Apnea_AirwayNotAttempted_IsRed()
        {
            var r = STARTEvaluator.Evaluate(V(breathing: false, afterAirway: true), Cap, airwayRepositioned: false);
            Assert.AreEqual(TriageCategory.Red, r.category); Assert.AreEqual(TriageBranch.ApneaAirwayNotAttempted, r.branch);
        }

        [Test] public void Apnea_RespondsToAirway_IsRed()
        {
            var r = STARTEvaluator.Evaluate(V(breathing: false, afterAirway: true), Cap);
            Assert.AreEqual(TriageCategory.Red, r.category); Assert.AreEqual(TriageBranch.ApneaRespondsToAirway, r.branch);
        }

        [Test] public void Apnea_NoResponseToAirway_IsBlack()
        {
            var r = STARTEvaluator.Evaluate(V(breathing: false, afterAirway: false), Cap);
            Assert.AreEqual(TriageCategory.Black, r.category); Assert.AreEqual(TriageBranch.ApneaNoResponseToAirway, r.branch);
        }

        [Test] public void RespiratoryRate_BoundaryAt30()
        {
            Assert.AreEqual(TriageCategory.Yellow, STARTEvaluator.Evaluate(V(rr: 30), Cap).category, "30/min is not above 30");
            var r = STARTEvaluator.Evaluate(V(rr: 31), Cap);
            Assert.AreEqual(TriageCategory.Red, r.category); Assert.AreEqual(TriageBranch.RespiratoryRateAbove30, r.branch);
        }

        [Test] public void CapillaryRefill_BoundaryAt2Seconds()
        {
            Assert.AreEqual(TriageCategory.Yellow, STARTEvaluator.Evaluate(V(refill: 2.0f), Cap).category, "exactly 2 s passes");
            var r = STARTEvaluator.Evaluate(V(refill: 2.01f), Cap);
            Assert.AreEqual(TriageCategory.Red, r.category); Assert.AreEqual(TriageBranch.PoorPerfusion, r.branch);
        }

        [Test] public void RadialPulse_Variant_UsesPulseNotRefill()
        {
            var noPulse = V(refill: 1.0f, pulse: false);
            Assert.AreEqual(TriageBranch.PoorPerfusion, STARTEvaluator.Evaluate(noPulse, Pulse).branch);
            Assert.AreEqual(TriageCategory.Yellow, STARTEvaluator.Evaluate(noPulse, Cap).category, "refill variant ignores the pulse");
            var slowRefill = V(refill: 3.5f, pulse: true);
            Assert.AreEqual(TriageCategory.Yellow, STARTEvaluator.Evaluate(slowRefill, Pulse).category, "pulse variant ignores refill");
            Assert.AreEqual(TriageCategory.Red, STARTEvaluator.Evaluate(slowRefill, Cap).category);
        }

        [Test] public void CannotFollowCommands_IsRed()
        {
            var r = STARTEvaluator.Evaluate(V(commands: false), Cap);
            Assert.AreEqual(TriageCategory.Red, r.category); Assert.AreEqual(TriageBranch.CannotFollowCommands, r.branch);
        }

        [Test] public void AllChecksPassed_IsYellow()
        {
            var r = STARTEvaluator.Evaluate(V(), Cap);
            Assert.AreEqual(TriageCategory.Yellow, r.category); Assert.AreEqual(TriageBranch.AllChecksPassed, r.branch);
        }

        [Test] public void BranchOrder_RespiratoryRateBeforePerfusionBeforeCommands()
        {
            Assert.AreEqual(TriageBranch.RespiratoryRateAbove30, STARTEvaluator.Evaluate(V(rr: 40, refill: 4f, commands: false), Cap).branch);
            Assert.AreEqual(TriageBranch.PoorPerfusion, STARTEvaluator.Evaluate(V(refill: 4f, commands: false), Cap).branch);
        }

        [Test] public void NullVitals_ReturnNone()
            => Assert.AreEqual(TriageCategory.None, STARTEvaluator.Evaluate(null, Cap).category);

        [Test] public void EveryCategoryIsReachable()
        {
            var seen = new HashSet<TriageCategory>
            {
                STARTEvaluator.GroundTruth(V(walk: true), Cap), STARTEvaluator.GroundTruth(V(), Cap),
                STARTEvaluator.GroundTruth(V(rr: 40), Cap), STARTEvaluator.GroundTruth(V(breathing: false), Cap)
            };
            CollectionAssert.AreEquivalent(new[] { TriageCategory.Green, TriageCategory.Yellow, TriageCategory.Red, TriageCategory.Black }, seen);
        }

        // ---- Session: classification, latency, under-triage -----------------------------------------

        [Test] public void Session_RejectsDuplicateOrEmptyIds()
        {
            var clk = new Clock();
            Assert.Throws<ArgumentException>(() => NewSession(clk, ("A", V()), ("A", V())));
            Assert.Throws<ArgumentException>(() => NewSession(clk, ("", V())));
        }

        [Test] public void Session_CannotAssignBeforeStart_OrWithoutSelection()
        {
            var clk = new Clock(); var s = Standard(clk);
            Assert.IsNull(s.Assign(TriageCategory.Red));
            Assert.IsFalse(s.BeginAssessment("A"));
            Assert.IsTrue(s.Start());
            Assert.IsNull(s.Assign(TriageCategory.Red), "nothing selected yet");
            Assert.IsTrue(s.BeginAssessment("A"));
            Assert.IsNull(s.Assign(TriageCategory.None));
        }

        [Test] public void Assign_CorrectAndIncorrect_AreFlagged()
        {
            var clk = new Clock(); var s = Standard(clk); s.Start();
            s.BeginAssessment("A"); var ok = s.Assign(TriageCategory.Green);
            s.BeginAssessment("C"); var bad = s.Assign(TriageCategory.Red);
            Assert.IsTrue(ok.isCorrect); Assert.IsFalse(bad.isCorrect);
            Assert.AreEqual(TriageCategory.Yellow, bad.groundTruth); Assert.AreEqual(TriageCategory.Red, bad.assigned);
            Assert.IsFalse(bad.trueRedUnderTriaged, "over-triage is not under-triage");
        }

        [Test] public void UnderTriage_OnlyForTrueRedTaggedYellowOrGreen()
        {
            foreach (var (tag, expected) in new[] { (TriageCategory.Yellow, true), (TriageCategory.Green, true), (TriageCategory.Black, false), (TriageCategory.Red, false) })
            {
                var clk = new Clock(); var s = Standard(clk); s.Start();
                s.BeginAssessment("B"); var r = s.Assign(tag);
                Assert.AreEqual(expected, r.trueRedUnderTriaged, "true Red tagged " + tag);
            }
        }

        [Test] public void Latency_IsEndMinusFirstSelection_AndSurvivesSwitching()
        {
            var clk = new Clock(); var s = Standard(clk); s.Start();
            clk.t = 1; s.BeginAssessment("A");
            clk.t = 2; s.BeginAssessment("B");
            clk.t = 3; s.BeginAssessment("A");
            clk.t = 10; var r = s.Assign(TriageCategory.Green);
            Assert.AreEqual(9.0, r.latencySeconds, 1e-9);
            Assert.AreEqual(1.0, r.startSeconds, 1e-9); Assert.AreEqual(10.0, r.endSeconds, 1e-9);
            Assert.IsTrue(r.latencyValid);
        }

        [Test] public void Latency_NegativeClockStep_IsMarkedInvalid()
        {
            var clk = new Clock(); var s = Standard(clk); s.Start();
            clk.t = 5; s.BeginAssessment("A"); clk.t = 3;
            var r = s.Assign(TriageCategory.Green);
            Assert.IsFalse(r.latencyValid); Assert.AreEqual(0.0, r.latencySeconds);
            var sum = TriageMetrics.Compute(s.Records, s.Cases.Count);
            Assert.AreEqual(1, sum.invalidLatencyCount); Assert.IsFalse(sum.hasLatency);
        }

        [Test] public void Tags_AreFinal_AndCompletionIsDetected()
        {
            var clk = new Clock(); var s = Standard(clk); s.Start();
            s.BeginAssessment("A"); s.Assign(TriageCategory.Green);
            Assert.IsFalse(s.BeginAssessment("A"), "already tagged");
            Assert.IsNull(s.Assign(TriageCategory.Red), "no active casualty after tagging");
            foreach (var id in new[] { "B", "C", "D" }) { s.BeginAssessment(id); s.Assign(TriageCategory.Red); }
            Assert.AreEqual(TriageSession.SessionState.Complete, s.State);
            Assert.AreEqual(4, s.Records.Count);
            Assert.IsNull(s.Assign(TriageCategory.Red));
        }

        [Test] public void Examine_RecordsOnlyWhatWasExamined()
        {
            var clk = new Clock(); var s = Standard(clk); s.Start();
            Assert.IsNull(s.Examine(ExamKind.Walking), "no active casualty");
            s.BeginAssessment("B");
            Assert.IsNotNull(s.Examine(ExamKind.RespiratoryRate));
            var r = s.Assign(TriageCategory.Red);
            Assert.AreEqual(40, r.observedRespiratoryRate);
            Assert.IsNull(r.observedCapillaryRefill); Assert.IsNull(r.observedCanWalk);
            Assert.AreEqual("RespiratoryRate", r.inputsExamined);
        }

        [Test] public void Airway_FlagFollowsTheExamination()
        {
            var clk = new Clock(); var s = Standard(clk); s.Start();
            s.BeginAssessment("D"); s.Examine(ExamKind.Breathing); s.Examine(ExamKind.Airway);
            Assert.IsTrue(s.Assign(TriageCategory.Black).airwayRepositioned);
        }

        // ---- Session: participant, reset, repeats ---------------------------------------------------

        [TestCase("P01", true)] [TestCase("P123", true)] [TestCase("TEST", true)]
        [TestCase("P1", false)] [TestCase("Jane Doe", false)] [TestCase("", false)] [TestCase(null, false)] [TestCase("p01", false)]
        public void ParticipantId_MustBeAnonymised(string id, bool valid) => Assert.AreEqual(valid, TriageSession.IsValidParticipantId(id));

        [Test] public void SetParticipant_RejectedWhileRunningOrInvalid()
        {
            var clk = new Clock(); var s = Standard(clk);
            Assert.IsFalse(s.SetParticipant("Jane Doe", DataProvenance.HumanParticipant));
            Assert.AreEqual(DataProvenance.SoftwareTest, s.Provenance, "default provenance is never human");
            Assert.IsTrue(s.SetParticipant("P01", DataProvenance.HumanParticipant));
            s.Start();
            Assert.IsFalse(s.SetParticipant("P02", DataProvenance.Pilot));
            Assert.AreEqual("P01", s.ParticipantId);
        }

        [Test] public void Reset_ClearsData_AndIssuesANewSessionId()
        {
            var clk = new Clock(); var s = Standard(clk); s.Start();
            string first = s.SessionId;
            s.BeginAssessment("A"); s.Assign(TriageCategory.Green);
            s.Reset();
            Assert.AreEqual(0, s.Records.Count); Assert.AreEqual(TriageSession.SessionState.NotStarted, s.State);
            Assert.AreNotEqual(first, s.SessionId);
            Assert.AreEqual("S20261001T120000-01", first); Assert.AreEqual("S20261001T120000-02", s.SessionId);
            Assert.IsTrue(s.Start());
            Assert.IsFalse(s.IsTagged("A"));
        }

        [Test] public void RepeatedRuns_ProduceIdenticalGroundTruth_AndIndependentRecords()
        {
            var clk = new Clock(); var s = new TriageSession(TriageScenario.Create(), Cap, clk.Now, clk.Utc);
            var runs = new List<List<string>>();
            for (int run = 0; run < 3; run++)
            {
                s.Start();
                foreach (var c in s.Cases) { clk.t += 1; s.BeginAssessment(c.id); clk.t += 2; s.Assign(STARTEvaluator.GroundTruth(c.vitals, Cap)); }
                runs.Add(s.Records.Select(r => r.casualtyId + ":" + r.groundTruth).ToList());
                Assert.AreEqual(10, s.Records.Count);
                s.Reset();
            }
            CollectionAssert.AreEqual(runs[0], runs[1]); CollectionAssert.AreEqual(runs[1], runs[2]);
        }

        // ---- Metrics ----------------------------------------------------------------------------------

        [Test] public void Metrics_ZeroRecords_HaveNoAccuracyRateOrLatency()
        {
            var m = TriageMetrics.Compute(new List<TriageRecord>(), 10);
            Assert.IsFalse(m.hasAccuracy); Assert.IsFalse(m.hasUnderTriageRate); Assert.IsFalse(m.hasLatency); Assert.IsFalse(m.isComplete);
            Assert.AreEqual(0, m.classified);
        }

        [Test] public void Metrics_ZeroTrueRed_UnderTriageIsUndefined_NotZero()
        {
            var clk = new Clock(); var s = Standard(clk); s.Start();
            s.BeginAssessment("A"); s.Assign(TriageCategory.Green);
            var m = TriageMetrics.Compute(s.Records, 4);
            Assert.AreEqual(0, m.trueRedTotal); Assert.IsFalse(m.hasUnderTriageRate);
            Assert.IsTrue(m.hasAccuracy); Assert.AreEqual(1.0, m.accuracy, 1e-9);
            StringAssert.Contains(",NA,", TriageCsv.SummaryToCsv(s, m));
        }

        [Test] public void Metrics_AccuracyUnderTriageAndLatency_AreCorrect()
        {
            var clk = new Clock();
            var s = NewSession(clk, ("R1", V(rr: 40)), ("R2", V(rr: 40)), ("R3", V(rr: 40)), ("R4", V(rr: 40)), ("Y", V()));
            s.Start();
            var plan = new[] { ("R1", TriageCategory.Red, 2.0), ("R2", TriageCategory.Yellow, 4.0), ("R3", TriageCategory.Green, 6.0), ("R4", TriageCategory.Red, 8.0), ("Y", TriageCategory.Red, 10.0) };
            foreach (var (id, tag, secs) in plan) { s.BeginAssessment(id); clk.t += secs; s.Assign(tag); }
            var m = TriageMetrics.Compute(s.Records, 5);
            Assert.AreEqual(5, m.classified); Assert.AreEqual(2, m.correct); Assert.AreEqual(0.4, m.accuracy, 1e-9);
            Assert.AreEqual(4, m.trueRedTotal); Assert.AreEqual(2, m.trueRedUnderTriaged); Assert.AreEqual(0.5, m.underTriageRate, 1e-9);
            Assert.AreEqual(6.0, m.meanLatency, 1e-9); Assert.AreEqual(6.0, m.medianLatency, 1e-9);
            Assert.AreEqual(2.0, m.minLatency, 1e-9); Assert.AreEqual(10.0, m.maxLatency, 1e-9);
            Assert.IsTrue(m.isComplete);
            Assert.AreEqual(2, m.confusion[2, 2]); Assert.AreEqual(1, m.confusion[2, 1]); Assert.AreEqual(1, m.confusion[2, 0]); Assert.AreEqual(1, m.confusion[1, 2]);
        }

        [Test] public void Metrics_MedianOfEvenCount_IsMeanOfMiddleTwo()
        {
            var recs = new List<TriageRecord>();
            foreach (var l in new[] { 1.0, 2.0, 3.0, 10.0 })
                recs.Add(new TriageRecord { casualtyId = "X", assigned = TriageCategory.Green, groundTruth = TriageCategory.Green, isCorrect = true, latencyValid = true, latencySeconds = l });
            Assert.AreEqual(2.5, TriageMetrics.Compute(recs, 4).medianLatency, 1e-9);
        }

        [Test] public void Metrics_IncompleteSession_IsNotComplete()
        {
            var clk = new Clock(); var s = Standard(clk); s.Start();
            s.BeginAssessment("A"); s.Assign(TriageCategory.Green);
            var m = TriageMetrics.Compute(s.Records, 4);
            Assert.IsFalse(m.isComplete); Assert.AreEqual(1, m.classified); Assert.AreEqual(4, m.total);
        }

        // ---- CSV ----------------------------------------------------------------------------------------

        static List<string> SplitCsvLine(string line)
        {
            var f = new List<string>(); var sb = new System.Text.StringBuilder(); bool q = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (q) { if (c == '"') { if (i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; } else q = false; } else sb.Append(c); }
                else if (c == '"') q = true;
                else if (c == ',') { f.Add(sb.ToString()); sb.Clear(); }
                else sb.Append(c);
            }
            f.Add(sb.ToString()); return f;
        }

        static TriageSession FullSession(Clock clk)
        {
            var s = new TriageSession(TriageScenario.Create(), Cap, clk.Now, clk.Utc);
            s.SetParticipant("P07", DataProvenance.Pilot); s.Start();
            foreach (var c in s.Cases) { clk.t += 1.5; s.BeginAssessment(c.id); s.Examine(ExamKind.Walking); clk.t += 3.25; s.Assign(STARTEvaluator.GroundTruth(c.vitals, Cap)); }
            return s;
        }

        [Test] public void Csv_Headers_AreTheDocumentedColumns()
        {
            Assert.AreEqual(26, TriageCsv.RecordColumns.Length); Assert.AreEqual(17, TriageCsv.SummaryColumns.Length);
            var expected = new[] { "session_id", "data_provenance", "participant_id", "protocol_perfusion", "casualty_id", "ground_truth", "assigned", "is_correct",
                "true_red_under_triaged", "expected_branch", "assessment_start_utc", "assessment_end_utc", "assessment_start_s", "assessment_end_s", "assessment_latency_s",
                "latency_valid", "hazard_level", "inputs_examined", "airway_repositioned", "obs_can_walk", "obs_breathing", "obs_breathing_after_airway", "obs_resp_rate",
                "obs_cap_refill_s", "obs_radial_pulse", "obs_follows_commands" };
            CollectionAssert.AreEqual(expected, TriageCsv.RecordColumns);
            CollectionAssert.AllItemsAreUnique(TriageCsv.RecordColumns); CollectionAssert.AllItemsAreUnique(TriageCsv.SummaryColumns);
        }

        [Test] public void Csv_EveryRow_HasEveryColumn_AndNoPii()
        {
            var clk = new Clock(); var s = FullSession(clk);
            var lines = TriageCsv.RecordsToCsv(s.Records).TrimEnd('\n').Split('\n');
            Assert.AreEqual(11, lines.Length, "header + 10 records");
            Assert.AreEqual(TriageCsv.RecordColumns.Length, SplitCsvLine(lines[0]).Count);
            for (int i = 1; i < lines.Length; i++)
            {
                var f = SplitCsvLine(lines[i]);
                Assert.AreEqual(26, f.Count, "row " + i);
                Assert.AreEqual("Pilot", f[1]); Assert.AreEqual("P07", f[2]); Assert.AreEqual("CapillaryRefill", f[3]);
                StringAssert.EndsWith("Z", f[10]);
                Assert.AreEqual("3.25", f[14]);
            }
        }

        [Test] public void Csv_Escape_QuotesCommasNewlinesAndFormulas()
        {
            Assert.AreEqual("plain", TriageCsv.Escape("plain"));
            Assert.AreEqual("\"a,b\"", TriageCsv.Escape("a,b"));
            Assert.AreEqual("\"say \"\"hi\"\"\"", TriageCsv.Escape("say \"hi\""));
            Assert.AreEqual("\"line1\nline2\"", TriageCsv.Escape("line1\nline2"));
            Assert.AreEqual("'=SUM(A1)", TriageCsv.Escape("=SUM(A1)"));
            Assert.AreEqual("'@x", TriageCsv.Escape("@x")); Assert.AreEqual("'+1", TriageCsv.Escape("+1")); Assert.AreEqual("'-1", TriageCsv.Escape("-1"));
            Assert.AreEqual("", TriageCsv.Escape(null));
        }

        [Test] public void Csv_Summary_HasHeaderAndOneCompleteRow()
        {
            var clk = new Clock(); var s = FullSession(clk);
            var m = TriageMetrics.Compute(s.Records, 10);
            var lines = TriageCsv.SummaryToCsv(s, m).TrimEnd('\n').Split('\n');
            Assert.AreEqual(2, lines.Length);
            Assert.AreEqual(17, SplitCsvLine(lines[1]).Count);
            var f = SplitCsvLine(lines[1]);
            Assert.AreEqual("10", f[4]); Assert.AreEqual("10", f[5]); Assert.AreEqual("true", f[6]); Assert.AreEqual("1", f[8]); Assert.AreEqual("0", f[11]);
        }

        [Test] public void Csv_Confusion_HasHeaderAndFourRows()
        {
            var clk = new Clock(); var s = FullSession(clk);
            var lines = TriageCsv.ConfusionToCsv(TriageMetrics.Compute(s.Records, 10)).TrimEnd('\n').Split('\n');
            Assert.AreEqual("ground_truth\\assigned,Green,Yellow,Red,Black", lines[0]);
            Assert.AreEqual(5, lines.Length);
            Assert.AreEqual("Green,3,0,0,0", lines[1]); Assert.AreEqual("Yellow,0,2,0,0", lines[2]);
            Assert.AreEqual("Red,0,0,4,0", lines[3]); Assert.AreEqual("Black,0,0,0,1", lines[4]);
        }

        [Test] public void Csv_Latency_IsWrittenInSecondsWithInvariantCulture()
        {
            var clk = new Clock(); var s = NewSession(clk, ("A", V(walk: true))); s.Start();
            s.BeginAssessment("A"); clk.t += 3.25; s.Assign(TriageCategory.Green);
            var f = SplitCsvLine(TriageCsv.RecordsToCsv(s.Records).Split('\n')[1]);
            Assert.AreEqual("3.25", f[14]); Assert.AreEqual("true", f[15]);
        }

        [Test] public void Export_WritesThreeFiles_AndReportsFailure()
        {
            var clk = new Clock(); var s = FullSession(clk);
            string dir = Path.Combine(Path.GetTempPath(), "triage_test_" + Guid.NewGuid().ToString("N"));
            try
            {
                Assert.IsTrue(TriageTelemetryLogger.WriteSession(s, dir, out var rec, out var sum, out var conf, out var err), err);
                Assert.IsTrue(File.Exists(rec) && File.Exists(sum) && File.Exists(conf));
                StringAssert.StartsWith(string.Join(",", TriageCsv.RecordColumns), File.ReadAllText(rec));
                StringAssert.EndsWith("_records.csv", rec);
                Assert.AreEqual(11, File.ReadAllLines(rec).Length);
            }
            finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }

            string blocker = Path.Combine(Path.GetTempPath(), "triage_block_" + Guid.NewGuid().ToString("N"));
            File.WriteAllText(blocker, "x");
            try
            {
                Assert.IsFalse(TriageTelemetryLogger.WriteSession(s, Path.Combine(blocker, "sub"), out _, out _, out _, out var err2));
                Assert.IsNotEmpty(err2);
                Assert.IsFalse(TriageTelemetryLogger.WriteSession(null, blocker, out _, out _, out _, out var err3)); Assert.IsNotEmpty(err3);
            }
            finally { File.Delete(blocker); }
        }

        [Test] public void Export_PartialSession_IsWrittenAndMarkedIncomplete()
        {
            var clk = new Clock(); var s = Standard(clk); s.Start();
            s.BeginAssessment("A"); s.Assign(TriageCategory.Green);
            string dir = Path.Combine(Path.GetTempPath(), "triage_part_" + Guid.NewGuid().ToString("N"));
            try
            {
                Assert.IsTrue(TriageTelemetryLogger.WriteSession(s, dir, out _, out var sum, out _, out _));
                var f = SplitCsvLine(File.ReadAllLines(sum)[1]);
                Assert.AreEqual("4", f[4]); Assert.AreEqual("1", f[5]); Assert.AreEqual("false", f[6]);
            }
            finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
        }

        [Test] public void Variants_AreRecordedSeparately_AndObserveDifferentInputs()
        {
            foreach (var (crit, label) in new[] { (Cap, "CapillaryRefill"), (Pulse, "RadialPulse") })
            {
                var clk = new Clock();
                var s = new TriageSession(new[] { new CasualtyCase("A", V(refill: 3f, pulse: false)) }, crit, clk.Now, clk.Utc);
                s.Start(); s.BeginAssessment("A"); s.Examine(ExamKind.Perfusion); clk.t += 2; var r = s.Assign(TriageCategory.Red);
                var f = SplitCsvLine(TriageCsv.RecordsToCsv(s.Records).Split('\n')[1]);
                Assert.AreEqual(label, f[3]);
                Assert.AreEqual(crit == Cap ? "3" : "", f[23], "obs_cap_refill_s only for the refill variant");
                Assert.AreEqual(crit == Pulse ? "false" : "", f[24], "obs_radial_pulse only for the pulse variant");
                Assert.AreEqual(crit, r.protocol);
                StringAssert.Contains(crit == Cap ? "refill" : "pulse", STARTEvaluator.Label(ExamKind.Perfusion, crit).ToLowerInvariant());
            }
        }

        [Test] public void Examine_DoesNotLeakOtherFindings()
        {
            var clk = new Clock(); var s = Standard(clk); s.Start(); s.BeginAssessment("B");
            var txt = s.Examine(ExamKind.Walking);
            StringAssert.DoesNotContain("40", txt); StringAssert.DoesNotContain("breaths", txt);
        }

        // ---- Scenario and campaign compatibility ------------------------------------------------------

        [Test] public void Scenario_HasTenUniqueCasualties_WithKnownMix()
        {
            var cases = TriageScenario.Create();
            Assert.AreEqual(TriageScenario.CasualtyCount, cases.Count); Assert.AreEqual(10, cases.Count);
            CollectionAssert.AllItemsAreUnique(cases.Select(c => c.id));
            var counts = cases.GroupBy(c => STARTEvaluator.GroundTruth(c.vitals, Cap)).ToDictionary(g => g.Key, g => g.Count());
            Assert.AreEqual(3, counts[TriageCategory.Green]); Assert.AreEqual(2, counts[TriageCategory.Yellow]);
            Assert.AreEqual(4, counts[TriageCategory.Red]); Assert.AreEqual(1, counts[TriageCategory.Black]);
        }

        [Test] public void Scenario_GroundTruthIsSameUnderBothPerfusionCriteria()
        {
            foreach (var c in TriageScenario.Create())
                Assert.AreEqual(STARTEvaluator.GroundTruth(c.vitals, Cap), STARTEvaluator.GroundTruth(c.vitals, Pulse), c.id);
        }

        [Test] public void Scenario_CoversEveryNonWalkingBranch()
        {
            var branches = TriageScenario.Create().Select(c => STARTEvaluator.Evaluate(c.vitals, Cap).branch).ToList();
            foreach (var b in new[] { TriageBranch.Walking, TriageBranch.AllChecksPassed, TriageBranch.RespiratoryRateAbove30, TriageBranch.PoorPerfusion,
                                      TriageBranch.CannotFollowCommands, TriageBranch.ApneaRespondsToAirway, TriageBranch.ApneaNoResponseToAirway })
                CollectionAssert.Contains(branches, b);
        }

        [Test] public void Scenario_CreateReturnsFreshObjects_SoSessionsDoNotShareState()
        {
            var a = TriageScenario.Create(); var b = TriageScenario.Create();
            Assert.AreNotSame(a[0], b[0]); Assert.AreNotSame(a[0].vitals, b[0].vitals);
        }

        [Test] public void Campaign_IsUnchanged_AndTriageIsRegisteredSeparately()
        {
            Assert.AreEqual(3, StageCatalog.Count);
            CollectionAssert.DoesNotContain(StageCatalog.Scenes, StageCatalog.TriageScene);
            Assert.AreEqual("TriageSimulation", StageCatalog.TriageScene);
            var scenes = EditorBuildSettings.scenes.Where(x => x.enabled).Select(x => Path.GetFileNameWithoutExtension(x.path)).ToList();
            Assert.AreEqual("MainMenu", scenes[0]);
            foreach (var n in new[] { "FactoryRescue", "EarthquakeRescue", "FloodEvacuation", "TriageSimulation" }) CollectionAssert.Contains(scenes, n);
        }
    }
}
