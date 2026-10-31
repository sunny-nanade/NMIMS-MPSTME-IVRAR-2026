using System;
using System.Collections;
using Oculus.Interaction;
using TMPro;
using UnityEngine;
using UnityEngine.Events;

namespace CSER
{
    /// Hardware OTP token: a fresh 6-digit code every 30 s.
    public class AuthToken : MonoBehaviour
    {
        public static AuthToken I { get; private set; }
        public TMP_Text display;
        public TMP_Text countdown;
        const float Period = 30f;
        int seed;

        // clear the static when the scene unloads, so later scenes never call into a destroyed instance
        void OnDestroy() { if (I == this) I = null; }

        void Awake()
        {
            I = this;
            seed = UnityEngine.Random.Range(1000, 999999);
        }

        int Step => Mathf.FloorToInt(Time.time / Period);
        int CodeFor(int step) => new System.Random(seed * 31 + step).Next(0, 1000000);

        public string Current => CodeFor(Step).ToString("000000");

        public bool IsValid(string code)
        {
            code = code.Replace(" ", "");
            return code == CodeFor(Step).ToString("000000") || code == CodeFor(Step - 1).ToString("000000");
        }

        void Update()
        {
            if (display) display.text = Current.Substring(0, 3) + " " + Current.Substring(3);
            if (countdown) countdown.text = new string('|', Mathf.CeilToInt((Period - Time.time % Period) / 3f));
        }
    }
}
