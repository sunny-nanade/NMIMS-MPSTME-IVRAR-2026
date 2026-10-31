using NUnit.Framework;
using RescueSim.Player;
using UnityEngine;

namespace RescueSim.Tests
{
    public class PlayerVitalsTests
    {
        GameObject _go; PlayerVitals _v;

        [SetUp] public void SetUp() { _go = new GameObject("vitals"); _v = _go.AddComponent<PlayerVitals>(); }
        [TearDown] public void TearDown() { Object.DestroyImmediate(_go); }

        [Test]
        public void AddInstant_AccumulatesAndTracksPeak()
        {
            _v.AddInstant(0.3f); _v.AddInstant(0.2f);
            Assert.AreEqual(0.5f, _v.Exposure, 1e-4f);
            Assert.AreEqual(0.5f, _v.PeakExposure, 1e-4f);
        }

        [Test]
        public void AddInstant_IgnoresNonPositive()
        {
            _v.AddInstant(0f); _v.AddInstant(-1f);
            Assert.AreEqual(0f, _v.Exposure);
        }

        [Test]
        public void Overcome_FiresExactlyOnce()
        {
            int n = 0; _v.Overcome += () => n++;
            _v.AddInstant(0.7f); _v.AddInstant(0.7f); _v.AddInstant(0.7f);
            Assert.AreEqual(1f, _v.Exposure);
            Assert.AreEqual(1, n);
        }

        [Test]
        public void ResetVitals_ClearsExposurePeakAndOvercome()
        {
            _v.AddInstant(1f);
            _v.ResetVitals();
            Assert.AreEqual(0f, _v.Exposure);
            Assert.AreEqual(0f, _v.PeakExposure);
            int n = 0; _v.Overcome += () => n++;
            _v.AddInstant(1f);
            Assert.AreEqual(1, n);
        }

        [Test]
        public void SampleRate_WithNoSources_IsZero() => Assert.AreEqual(0f, _v.SampleRate(Vector3.zero));

        [Test]
        public void RecoveryRate_ClampedNonNegative()
        {
            _v.RecoveryPerSecond = -5f;
            Assert.AreEqual(0f, _v.RecoveryPerSecond);
        }
    }

    public class GameSettingsTests
    {
        [SetUp] public void SetUp() => GameSettings.ResetToDefaults();
        [TearDown] public void TearDown() => GameSettings.ResetToDefaults();

        [Test]
        public void Defaults_AreComfortable()
        {
            Assert.IsTrue(GameSettings.SnapTurn);
            Assert.IsTrue(GameSettings.ComfortVignette);
            Assert.AreEqual(45f, GameSettings.SnapAngle);
            Assert.AreEqual(GameSettings.DefaultMoveSpeed, GameSettings.MoveSpeed);
        }

        [Test]
        public void Values_AreClamped()
        {
            GameSettings.MoveSpeed = 99f; Assert.AreEqual(GameSettings.MaxMoveSpeed, GameSettings.MoveSpeed);
            GameSettings.MoveSpeed = 0f; Assert.AreEqual(GameSettings.MinMoveSpeed, GameSettings.MoveSpeed);
            GameSettings.Volume = 7f; Assert.AreEqual(1f, GameSettings.Volume);
            GameSettings.Volume = -1f; Assert.AreEqual(0f, GameSettings.Volume);
            GameSettings.SnapAngle = 1f; Assert.AreEqual(15f, GameSettings.SnapAngle);
        }

        [Test]
        public void Changed_FiresOnSet_AndReset()
        {
            int n = 0; System.Action h = () => n++;
            GameSettings.Changed += h;
            try { GameSettings.SnapTurn = false; GameSettings.ResetToDefaults(); }
            finally { GameSettings.Changed -= h; }
            Assert.AreEqual(2, n);
            Assert.IsTrue(GameSettings.SnapTurn);
        }
    }
}
