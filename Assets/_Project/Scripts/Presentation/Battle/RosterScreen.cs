using System;
using NinjaRealm.Core.Progression;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace NinjaRealm.Presentation.Battle
{
    /// <summary>
    /// The roster screen, opened from the lineup screen: level up and star up your ninjas,
    /// recruit new ones in the Dojo, and forge weapons in the Armory, without going back to the
    /// village. It shows the same <see cref="RosterViews"/> as the village's Shrine, Dojo and
    /// Blacksmith, in the village's panel frame (UI/Resources/UI/BuildingPanel.uxml).
    /// </summary>
    [RequireComponent(typeof(BattleController))]
    public sealed class RosterScreen : MonoBehaviour
    {
        enum Tab
        {
            Ninjas,
            Dojo,
            Armory,
        }

        static readonly string[] TabNames = { "Ninjas", "Dojo", "Armory" };

        BattleController controller;
        RosterViews views;
        UIDocument document;
        VisualElement panel;
        Label subtitle;
        VisualElement body;
        UiMessage message;
        Tab tab;
        bool shownOpen;
        string shownWallet;

        void Awake()
        {
            controller = GetComponent<BattleController>();
            views = new RosterViews(() => controller.Profile, () => controller.Content, (ok, text) => message?.Show(ok, text), Rebuild);
        }

        ProfileService Profile => controller.Profile;

        void OnEnable()
        {
            if (document == null) document = GameUi.Create(transform, "BuildingPanel", 10f);
            if (document == null) return;

            var root = document.rootVisualElement;
            panel = root.Q("overlay");
            root.Q<Label>("title").text = "Roster";
            subtitle = root.Q<Label>("subtitle");
            body = root.Q("body");
            message = new UiMessage(root.Q<Label>("message"));
            root.Q("frame").AddToClassList("building-panel--tall");
            var close = root.Q<Button>("close");
            close.text = "Close (Tab)";
            close.clicked += () => SetOpen(false);
            shownOpen = false;
            GameUi.Show(panel, false);
        }

        void Update()
        {
            if (controller.Phase != BattlePhase.Setup || Profile == null)
            {
                controller.RosterOpen = false;
            }
            else
            {
                var keyboard = Keyboard.current;
                if (keyboard != null && keyboard.tabKey.wasPressedThisFrame) SetOpen(!controller.RosterOpen);
                else if (keyboard != null && controller.RosterOpen && keyboard.escapeKey.wasPressedThisFrame) SetOpen(false);
            }

            // The lineup screen's button opens the roster too, so follow the flag.
            var open = controller.RosterOpen;
            if (open != shownOpen)
            {
                shownOpen = open;
                GameUi.Show(panel, open);
                if (open)
                {
                    message?.Clear();
                    Rebuild();
                }
            }

            if (open) RefreshWallet();
        }

        void SetOpen(bool open)
        {
            if (controller.RosterOpen == open) return;
            controller.RosterOpen = open;
            if (open)
            {
                Profile.RefreshDojoIfNewDay(DateTime.Now);
            }
            else
            {
                // Levels and new weapons change what the lineup can hold.
                controller.FitLineupToProfile();
            }
        }

        void RefreshWallet()
        {
            var line = LineupScreen.WalletLine(Profile);
            if (line == shownWallet) return;
            shownWallet = line;
            subtitle.text = line;
        }

        void Rebuild()
        {
            if (body == null || Profile == null) return;
            body.Clear();

            var tabs = GameUi.Element("tabs");
            for (var i = 0; i < TabNames.Length; i++)
            {
                var index = (Tab)i;
                var button = GameUi.Button(TabNames[i], () =>
                {
                    if (tab == index) return;
                    tab = index;
                    Rebuild();
                }, "tab");
                button.EnableInClassList("tab--selected", tab == index);
                tabs.Add(button);
            }

            body.Add(tabs);
            body.Add(tab == Tab.Ninjas ? views.Ninjas() : tab == Tab.Dojo ? views.Recruits() : views.Armory());
        }
    }
}
