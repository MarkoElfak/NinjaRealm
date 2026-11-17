using UnityEngine;

namespace NinjaRealm.Presentation.Battle
{
    /// <summary>
    /// Colour helpers left from the prototype IMGUI screens, for the rich text the UI Toolkit
    /// screens write: the gold of stars and rewards, and a colour as hex for a color tag.
    /// </summary>
    public static class PrototypeGui
    {
        public static readonly Color Gold = new Color(1f, 0.82f, 0.35f);

        public static string Hex(Color color) => ColorUtility.ToHtmlStringRGB(color);
    }
}
