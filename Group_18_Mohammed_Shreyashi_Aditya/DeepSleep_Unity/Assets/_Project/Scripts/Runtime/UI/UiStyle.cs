using UnityEngine;

namespace Fractured_Final
{
    /// <summary>One look for every world-space panel (Setup, briefing, goal card, TLX, SUS, oxygen display, posters): dark navy panel, cyan frame, light body text.</summary>
    public static class UiStyle
    {
        public static readonly Color Back = new Color(0.03f, 0.05f, 0.08f);
        public static readonly Color Frame = new Color(0.2f, 0.45f, 0.6f);
        public static readonly Color Accent = new Color(0.3f, 0.85f, 1f);
        public static readonly Color Body = new Color(0.82f, 0.9f, 0.96f);
        public static readonly Color Muted = new Color(0.55f, 0.7f, 0.8f);
        public static readonly Color Button = new Color(0.12f, 0.25f, 0.35f);

        /// <summary>A navy panel with a cyan frame behind it, centred on localPos. Returns the panel.</summary>
        public static GameObject Panel(Transform parent, string name, Vector2 size, Vector3 localPos, float frameWidth = 0.025f)
        {
            WorldUI.Panel(parent, name + "Frame", size + new Vector2(frameWidth * 2f, frameWidth * 2f), Frame, localPos + new Vector3(0, 0, 0.004f));
            return WorldUI.Panel(parent, name, size, Back, localPos);
        }
    }
}
