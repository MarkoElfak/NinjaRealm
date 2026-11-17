using System;
using System.Collections.Generic;
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
        public static UIDocument Create(Transform host, string layout, float sortingOrder = 0f)
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
            document.sortingOrder = sortingOrder;
            holder.SetActive(true);

            // Only the screen's own panels catch the mouse; the rest of the screen is the 3D world.
            document.rootVisualElement.pickingMode = PickingMode.Ignore;
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

        /// <summary>An empty element with these classes.</summary>
        public static VisualElement Element(params string[] classes)
        {
            var element = new VisualElement();
            foreach (var name in classes) element.AddToClassList(name);
            return element;
        }

        public static Button Button(string text, Action clicked, params string[] classes)
        {
            var button = new Button(clicked) { text = text };
            foreach (var name in classes) button.AddToClassList(name);
            return button;
        }

        /// <summary>A point on the screen, in pixels from the bottom left, in a panel's coordinates.</summary>
        public static Vector2 ScreenToPanel(IPanel panel, Vector2 screen) =>
            RuntimePanelUtils.ScreenToPanel(panel, new Vector2(screen.x, Screen.height - screen.y));

        /// <summary>
        /// The UI element under a point on the screen, in pixels from the bottom left, or null
        /// when the point is over the 3D world. Every screen shares one panel, so this finds the
        /// elements of all of them.
        /// </summary>
        public static VisualElement Under(UIDocument document, Vector2 screen)
        {
            var panel = document != null && document.rootVisualElement != null ? document.rootVisualElement.panel : null;
            return panel != null ? panel.Pick(ScreenToPanel(panel, screen)) : null;
        }

        /// <summary>The data a card carries in its userData, looked up from an element inside it.</summary>
        public static T DataOf<T>(VisualElement element) where T : class
        {
            for (var e = element; e != null; e = e.parent)
                if (e.userData is T data) return data;
            return null;
        }

        /// <summary>Lets clicks pass through an element and everything in it, for labels over the 3D world.</summary>
        public static void IgnorePicking(VisualElement element)
        {
            element.pickingMode = PickingMode.Ignore;
            foreach (var child in element.Children()) IgnorePicking(child);
        }

        /// <summary>
        /// Scrolls a freshly built scroll view to an offset once its content has a size, so a
        /// list rebuilt after a button press stays where the player was.
        /// </summary>
        public static void RestoreScroll(ScrollView view, Vector2 offset)
        {
            if (offset == Vector2.zero) return;
            EventCallback<GeometryChangedEvent> once = null;
            once = _ =>
            {
                view.contentContainer.UnregisterCallback(once);
                view.scrollOffset = offset;
            };
            view.contentContainer.RegisterCallback(once);
        }
    }

    /// <summary>
    /// Labels that follow points of the 3D world, such as names over ninjas and damage
    /// numbers. Each frame: <see cref="Begin"/>, one <see cref="Place"/> per label, then
    /// <see cref="End"/>, which hides the labels not placed. Labels are reused.
    /// </summary>
    public sealed class WorldLabels
    {
        readonly VisualElement layer;
        readonly string className;
        readonly List<Label> labels = new List<Label>();
        int used;

        public WorldLabels(VisualElement layer, string className)
        {
            this.layer = layer;
            this.className = className;
        }

        public void Begin() => used = 0;

        /// <summary>Puts a label over a world point. Null when the point is behind the camera.</summary>
        public Label Place(Vector3 world, Camera camera, string text)
        {
            if (layer == null || layer.panel == null || camera == null || camera.WorldToScreenPoint(world).z <= 0f) return null;
            if (used == labels.Count)
            {
                var created = GameUi.Label("", className);
                created.pickingMode = PickingMode.Ignore;
                layer.Add(created);
                labels.Add(created);
            }

            var label = labels[used++];
            var point = RuntimePanelUtils.CameraTransformWorldToPanel(layer.panel, world, camera);
            label.style.left = point.x;
            label.style.top = point.y;
            if (label.text != text) label.text = text;
            GameUi.Show(label, true);
            return label;
        }

        public void End()
        {
            for (var i = used; i < labels.Count; i++) GameUi.Show(labels[i], false);
        }
    }

    /// <summary>A label that shows the result of the last action for a few seconds, red when it failed.</summary>
    public sealed class UiMessage
    {
        const long ShowMilliseconds = 4000;

        readonly Label label;
        IVisualElementScheduledItem clear;

        public UiMessage(Label label) => this.label = label;

        public void Show(bool succeeded, string text)
        {
            if (label == null) return;
            label.text = text;
            label.EnableInClassList("danger-text", !succeeded);
            clear?.Pause();
            clear = label.schedule.Execute(Clear).StartingIn(ShowMilliseconds);
        }

        public void Clear()
        {
            if (label != null) label.text = "";
        }
    }
}
