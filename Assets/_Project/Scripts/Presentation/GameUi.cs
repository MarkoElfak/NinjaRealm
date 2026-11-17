using UnityEngine;
using UnityEngine.UIElements;

namespace NinjaRealm.Presentation
{
    /// <summary>
    /// The game's UI Toolkit screens. Every screen shares one panel, scaled to a 900 pixel tall
    /// screen like the prototype IMGUI, and the theme in UI/Resources/UI/NinjaTheme.tss. The
    /// layouts live in UI/Resources/UI and are loaded by name, so a scene needs no setup.
    /// </summary>
    public static class GameUi
    {
        public const string ThemePath = "UI/NinjaTheme";
        public const int ReferenceHeight = 900;

        static PanelSettings panel;

        /// <summary>The shared panel, created the first time a screen asks for it.</summary>
        public static PanelSettings Panel
        {
            get
            {
                if (panel != null) return panel;
                panel = ScriptableObject.CreateInstance<PanelSettings>();
                panel.name = "Ninja Realm UI";
                panel.themeStyleSheet = Resources.Load<ThemeStyleSheet>(ThemePath);
                if (panel.themeStyleSheet == null) Debug.LogError($"[UI] No theme at Resources/{ThemePath}.tss.");
                panel.scaleMode = PanelScaleMode.ScaleWithScreenSize;
                panel.referenceResolution = new Vector2Int(1600, ReferenceHeight);
                panel.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
                panel.match = 1f;
                panel.sortingOrder = 10;
                return panel;
            }
        }

        /// <summary>
        /// Puts a UI Toolkit document from Resources/UI on a new child of the host and gives it
        /// back, or null when the layout is missing.
        /// </summary>
        public static UIDocument Create(Transform host, string layout)
        {
            var tree = Resources.Load<VisualTreeAsset>("UI/" + layout);
            if (tree == null)
            {
                Debug.LogError($"[UI] No layout at Resources/UI/{layout}.uxml.");
                return null;
            }

            var holder = new GameObject(layout + " UI");
            holder.SetActive(false);
            holder.transform.SetParent(host, false);
            var document = holder.AddComponent<UIDocument>();
            document.panelSettings = Panel;
            document.visualTreeAsset = tree;
            holder.SetActive(true);
            return document;
        }

        /// <summary>A layout from Resources/UI, instantiated to be put into a screen.</summary>
        public static VisualElement Instantiate(string layout)
        {
            var tree = Resources.Load<VisualTreeAsset>("UI/" + layout);
            if (tree == null)
            {
                Debug.LogError($"[UI] No layout at Resources/UI/{layout}.uxml.");
                return null;
            }

            var element = tree.Instantiate();
            element.AddToClassList("panel-layer");
            element.pickingMode = PickingMode.Ignore;
            return element;
        }

        /// <summary>Shows or hides an element with the theme's hidden class.</summary>
        public static void Show(VisualElement element, bool show) => element?.EnableInClassList("hidden", !show);

        /// <summary>A row of star pips, the first ones filled.</summary>
        public static VisualElement Pips(int count, int max)
        {
            var row = new VisualElement();
            row.AddToClassList("pips");
            for (var i = 0; i < max; i++)
            {
                var pip = new VisualElement();
                pip.AddToClassList("pip");
                pip.EnableInClassList("pip--on", i < count);
                row.Add(pip);
            }

            return row;
        }

        public static Label Label(string text, params string[] classes)
        {
            var label = new Label(text);
            foreach (var name in classes) label.AddToClassList(name);
            return label;
        }
    }
}
