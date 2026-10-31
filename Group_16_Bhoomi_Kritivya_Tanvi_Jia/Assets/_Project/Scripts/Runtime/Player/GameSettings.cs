using System;
using UnityEngine;

namespace RescueSim.Player
{
    /// <summary>Persisted player preferences (comfort + audio). Backed by PlayerPrefs; safe to call from any scene.</summary>
    public static class GameSettings
    {
        const string KeySnap = "rs.snapTurn";
        const string KeySnapAngle = "rs.snapAngle";
        const string KeyTurnSpeed = "rs.turnSpeed";
        const string KeyMoveSpeed = "rs.moveSpeed";
        const string KeyVignette = "rs.vignette";
        const string KeyVolume = "rs.volume";

        public const float MinMoveSpeed = 0.8f, MaxMoveSpeed = 3.0f;
        public const float DefaultMoveSpeed = 1.6f;

        public static event Action Changed;

        public static bool SnapTurn { get => Get(KeySnap, 1) == 1; set => Set(KeySnap, value ? 1 : 0); }
        public static float SnapAngle { get => GetF(KeySnapAngle, 45f); set => SetF(KeySnapAngle, Mathf.Clamp(value, 15f, 90f)); }
        public static float TurnSpeed { get => GetF(KeyTurnSpeed, 90f); set => SetF(KeyTurnSpeed, Mathf.Clamp(value, 30f, 180f)); }
        public static float MoveSpeed { get => GetF(KeyMoveSpeed, DefaultMoveSpeed); set => SetF(KeyMoveSpeed, Mathf.Clamp(value, MinMoveSpeed, MaxMoveSpeed)); }
        public static bool ComfortVignette { get => Get(KeyVignette, 1) == 1; set => Set(KeyVignette, value ? 1 : 0); }
        public static float Volume { get => GetF(KeyVolume, 0.8f); set => SetF(KeyVolume, Mathf.Clamp01(value)); }

        public static void ResetToDefaults()
        {
            foreach (var k in new[] { KeySnap, KeySnapAngle, KeyTurnSpeed, KeyMoveSpeed, KeyVignette, KeyVolume })
                PlayerPrefs.DeleteKey(k);
            PlayerPrefs.Save();
            Changed?.Invoke();
        }

        static int Get(string k, int d) => PlayerPrefs.GetInt(k, d);
        static float GetF(string k, float d) => PlayerPrefs.GetFloat(k, d);
        static void Set(string k, int v) { PlayerPrefs.SetInt(k, v); PlayerPrefs.Save(); Changed?.Invoke(); }
        static void SetF(string k, float v) { PlayerPrefs.SetFloat(k, v); PlayerPrefs.Save(); Changed?.Invoke(); }
    }
}
