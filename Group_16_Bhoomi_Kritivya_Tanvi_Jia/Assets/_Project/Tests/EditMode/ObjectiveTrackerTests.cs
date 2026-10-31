using System.Collections.Generic;
using NUnit.Framework;
using RescueSim.Core;

namespace RescueSim.Tests
{
    public class ObjectiveTrackerTests
    {
        static MissionDefinition.ObjectiveDefinition Def(string id, bool required = true)
            => new MissionDefinition.ObjectiveDefinition { id = id, title = id, required = required };

        static ObjectiveTracker Make(params MissionDefinition.ObjectiveDefinition[] defs) => new ObjectiveTracker(defs);

        [Test] public void NullDefinitions_Throws() => Assert.Throws<System.ArgumentNullException>(() => new ObjectiveTracker(null));

        [Test]
        public void DuplicateAndEmptyIds_AreIgnored()
        {
            var t = Make(Def("a"), Def("a"), Def(""), Def(null), Def("b"));
            Assert.AreEqual(2, t.Objectives.Count);
        }

        [Test]
        public void Complete_IsIdempotentAndRaisesOnce()
        {
            var t = Make(Def("a"), Def("b"));
            int changed = 0; t.ObjectiveChanged += _ => changed++;
            Assert.IsTrue(t.Complete("a"));
            Assert.IsFalse(t.Complete("a"));
            Assert.AreEqual(1, changed);
            Assert.IsTrue(t.IsComplete("a"));
        }

        [Test]
        public void UnknownId_IsRejected()
        {
            var t = Make(Def("a"));
            Assert.IsFalse(t.Complete("zzz"));
            Assert.IsFalse(t.Fail("zzz"));
            Assert.IsFalse(t.IsComplete("zzz"));
        }

        [Test]
        public void AllRequiredComplete_FiresOnlyWhenRequiredDone_OptionalIgnored()
        {
            var t = Make(Def("a"), Def("b"), Def("opt", false));
            int fired = 0; t.AllRequiredComplete += () => fired++;
            t.Complete("a");
            Assert.AreEqual(0, fired);
            t.Complete("b");
            Assert.AreEqual(1, fired);
            Assert.IsTrue(t.AllRequiredDone());
        }

        [Test]
        public void Fail_OnlyFromPending()
        {
            var t = Make(Def("a"), Def("b"));
            Assert.IsTrue(t.Fail("a"));
            Assert.IsFalse(t.Fail("a"));
            t.Complete("b");
            Assert.IsFalse(t.Fail("b"));
            Assert.IsFalse(t.AllRequiredDone());
        }

        [Test]
        public void SetProgress_RaisesOnlyOnChange()
        {
            var t = Make(Def("a"));
            int n = 0; t.ObjectiveChanged += _ => n++;
            t.SetProgress("a", "1/3"); t.SetProgress("a", "1/3"); t.SetProgress("a", "2/3");
            Assert.AreEqual(2, n);
            t.TryGet("a", out var s);
            Assert.AreEqual("2/3", s.Progress);
        }

        [Test]
        public void EmptyTracker_IsNeverDone() => Assert.IsFalse(Make().AllRequiredDone());

        [Test]
        public void CompletedCount_Counts()
        {
            var t = Make(Def("a"), Def("b"), Def("c"));
            t.Complete("a"); t.Complete("c");
            Assert.AreEqual(2, t.CompletedCount());
        }
    }
}
