using System;
using System.Collections.Generic;
using UnityEngine;

namespace Fractured_Final
{
    /// <summary>Controller haptics, routed through InputService.</summary>
    public static class Haptics
    {
        public static void Pulse(Hand h, float amplitude, float seconds)
        {
            if (InputService.Exists) InputService.Instance.Vibrate(h, amplitude, seconds);
        }
    }
}
