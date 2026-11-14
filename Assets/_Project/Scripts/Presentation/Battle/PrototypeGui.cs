using UnityEngine;
using UnityEngine.InputSystem;

namespace NinjaRealm.Presentation.Battle
{
    /// <summary>
    /// Shared helpers for the prototype IMGUI screens: one scale so the layout fits any
    /// resolution (a 900 pixel tall reference), dark slate panels and text styles. The real UI
    /// replaces all of this in the art pass.
    /// </summary>
    public static class PrototypeGui
    {
        public const float ReferenceHeight = 900f;

        public static readonly Color PanelColor = new Color(0.1f, 0.12f, 0.16f, 0.88f);
        public static readonly Color CardColor = new Color(0.18f, 0.2f, 0.26f, 0.95f);
        public static readonly Color DisabledColor = new Color(0.12f, 0.13f, 0.15f, 0.7f);
        public static readonly Color BorderColor = new Color(1f, 1f, 1f, 0.15f);
        public static readonly Color Gold = new Color(1f, 0.82f, 0.35f);

        static GUIStyle label;
        static GUIStyle small;
        static GUIStyle title;
        static GUIStyle centered;
        static GUIStyle banner;
        static GUIStyle wrap;
        static GUIStyle nameStyle;

        public static float Scale => Mathf.Max(0.5f, Screen.height / ReferenceHeight);

        public static float Width => Screen.width / Scale;

        public static float Height => Screen.height / Scale;

        public static GUIStyle Label => label;

        public static GUIStyle Small => small;

        public static GUIStyle Title => title;

        public static GUIStyle Centered => centered;

        public static GUIStyle Banner => banner;

        public static GUIStyle Wrap => wrap;

        /// <summary>Small centred bold text for names above units.</summary>
        public static GUIStyle NameStyle => nameStyle;

        /// <summary>Call at the top of OnGUI: sets the scale and makes sure the styles exist.</summary>
        public static void Begin()
        {
            GUI.matrix = Matrix4x4.Scale(new Vector3(Scale, Scale, 1f));
            if (label != null) return;

            label = new GUIStyle(GUI.skin.label) { fontSize = 14, richText = true };
            label.normal.textColor = Color.white;
            small = new GUIStyle(label) { fontSize = 12 };
            small.normal.textColor = new Color(0.8f, 0.82f, 0.86f);
            title = new GUIStyle(label) { fontSize = 16, fontStyle = FontStyle.Bold };
            centered = new GUIStyle(label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, fontSize = 16 };
            banner = new GUIStyle(centered) { fontSize = 40 };
            wrap = new GUIStyle(small) { wordWrap = true };
            nameStyle = new GUIStyle(label) { alignment = TextAnchor.MiddleCenter, fontSize = 12 };
        }

        /// <summary>The mouse position in the scaled GUI space, read from the Input System.</summary>
        public static Vector2 MouseGuiPosition
        {
            get
            {
                var mouse = Mouse.current;
                if (mouse == null) return new Vector2(-1f, -1f);
                var screen = mouse.position.ReadValue();
                return new Vector2(screen.x / Scale, (Screen.height - screen.y) / Scale);
            }
        }

        /// <summary>The mouse position in screen pixels, for raycasts.</summary>
        public static Vector2 MouseScreenPosition => Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;

        public static void Panel(Rect rect) => Fill(rect, PanelColor, true);

        public static void Fill(Rect rect, Color color, bool border = false)
        {
            var previous = GUI.color;
            if (border)
            {
                GUI.color = BorderColor;
                GUI.DrawTexture(new Rect(rect.x - 1f, rect.y - 1f, rect.width + 2f, rect.height + 2f), Texture2D.whiteTexture);
            }

            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = previous;
        }

        /// <summary>A horizontal bar filled to a fraction, for health and damage.</summary>
        public static void Bar(Rect rect, float fraction, Color color)
        {
            Fill(rect, new Color(0f, 0f, 0f, 0.5f));
            Fill(new Rect(rect.x, rect.y, rect.width * Mathf.Clamp01(fraction), rect.height), color);
        }

        /// <summary>Small squares showing a level such as difficulty, filled up to count.</summary>
        public static void Pips(Vector2 position, int count, int max, Color color)
        {
            for (var i = 0; i < max; i++)
                Fill(new Rect(position.x + i * 11f, position.y, 8f, 8f), i < count ? color : new Color(1f, 1f, 1f, 0.15f));
        }

        public static string Hex(Color color) => ColorUtility.ToHtmlStringRGB(color);
    }
}
