using System;
using NinjaRealm.Core.Progression;
using UnityEngine;
using UnityEngine.InputSystem;

namespace NinjaRealm.Presentation.Battle
{
    /// <summary>
    /// Prototype roster screen, opened from the lineup screen: level up and star up your ninjas,
    /// recruit new ones in the Dojo, and forge weapons in the Armory, without going back to the
    /// village. The panels are <see cref="RosterPanels"/>, shared with the village's Shrine, Dojo
    /// and Blacksmith. Drawn with IMGUI.
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
        RosterPanels panels;
        Tab tab;

        void Awake()
        {
            controller = GetComponent<BattleController>();
            panels = new RosterPanels(() => controller.Profile, () => controller.Content);
        }

        ProfileService Profile => controller.Profile;

        void Update()
        {
            if (controller.Phase != BattlePhase.Setup || Profile == null)
            {
                controller.RosterOpen = false;
                return;
            }

            var keyboard = Keyboard.current;
            if (keyboard == null) return;
            if (keyboard.tabKey.wasPressedThisFrame) SetOpen(!controller.RosterOpen);
            else if (controller.RosterOpen && keyboard.escapeKey.wasPressedThisFrame) SetOpen(false);
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

        // ---------------------------------------------------------------- drawing

        void OnGUI()
        {
            if (controller == null || !controller.RosterOpen || Profile == null || controller.Phase != BattlePhase.Setup) return;

            PrototypeGui.Begin();
            var width = Mathf.Min(900f, PrototypeGui.Width - 40f);
            var height = Mathf.Min(720f, PrototypeGui.Height - 40f);
            var panel = new Rect((PrototypeGui.Width - width) / 2f, (PrototypeGui.Height - height) / 2f, width, height);
            PrototypeGui.Fill(new Rect(0f, 0f, PrototypeGui.Width, PrototypeGui.Height), new Color(0f, 0f, 0f, 0.35f));
            PrototypeGui.Panel(panel);

            var x = panel.x + 16f;
            var inner = panel.width - 32f;
            var y = panel.y + 12f;
            GUI.Label(new Rect(x, y, 200f, 26f), "<b>Roster</b>", PrototypeGui.Title);
            if (GUI.Button(new Rect(panel.xMax - 120f, y, 104f, 26f), "Close (Tab)")) SetOpen(false);
            GUI.Label(new Rect(x + 90f, y + 4f, inner - 220f, 20f), LineupScreen.WalletLine(Profile), PrototypeGui.Small);
            y += 36f;

            for (var i = 0; i < TabNames.Length; i++)
            {
                var rect = new Rect(x + i * 128f, y, 120f, 28f);
                var active = (int)tab == i;
                PrototypeGui.Fill(rect, active ? new Color(0.32f, 0.27f, 0.12f, 0.95f) : PrototypeGui.CardColor, active);
                if (GUI.Button(rect, GUIContent.none, GUIStyle.none) && !active)
                {
                    tab = (Tab)i;
                    panels.ResetScroll();
                }

                GUI.Label(rect, TabNames[i], PrototypeGui.Centered);
            }

            y += 40f;
            var body = new Rect(x, y, inner, panel.yMax - y - 52f);
            switch (tab)
            {
                case Tab.Ninjas:
                    panels.DrawNinjas(body);
                    break;
                case Tab.Dojo:
                    panels.DrawDojo(body);
                    break;
                default:
                    panels.DrawArmory(body);
                    break;
            }

            panels.DrawMessage(new Rect(x, panel.yMax - 40f, inner, 28f));
        }
    }
}

