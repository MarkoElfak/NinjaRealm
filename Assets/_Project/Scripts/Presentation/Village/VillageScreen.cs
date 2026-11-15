using NinjaRealm.Core.Lineups;
using NinjaRealm.Presentation.Battle;
using UnityEngine;

namespace NinjaRealm.Presentation.Village
{
    /// <summary>
    /// The village's prototype IMGUI: the clan and wallet along the top, a name over each
    /// building, and the open building's panel. The Dojo, Shrine and Blacksmith reuse the roster
    /// panels; the Command Hall shows the lineup and sends you to battle.
    /// </summary>
    [RequireComponent(typeof(VillageController))]
    public sealed class VillageScreen : MonoBehaviour
    {
        VillageController controller;
        RosterPanels panels;

        void Awake()
        {
            controller = GetComponent<VillageController>();
            panels = new RosterPanels(() => controller.Profile, () => controller.Content);
            controller.OpenedChanged += _ => panels.ResetScroll();
        }

        void OnGUI()
        {
            if (controller == null) return;
            PrototypeGui.Begin();

            if (controller.Opened == null) DrawNames();
            DrawTopBar();
            if (controller.Opened != null) DrawPanel(controller.Opened);
        }

        void DrawTopBar()
        {
            var bar = new Rect(PrototypeGui.Width / 2f - 300f, 10f, 600f, 58f);
            PrototypeGui.Panel(bar);
            GUI.Label(new Rect(bar.x + 14f, bar.y + 6f, 300f, 24f), "<b>Ninja Village</b>", PrototypeGui.Title);
            var hint = controller.Opened == null ? "Click a building. Drag to look around, scroll to zoom." : "Esc closes the panel.";
            GUI.Label(new Rect(bar.xMax - 330f, bar.y + 8f, 316f, 20f), $"<color=#9aa3b0>{hint}</color>", PrototypeGui.Small);

            var profile = controller.Profile;
            var line = profile != null ? LineupScreen.WalletLine(profile) : "No save outside play mode.";
            GUI.Label(new Rect(bar.x + 14f, bar.y + 32f, bar.width - 28f, 20f), line, PrototypeGui.Small);
        }

        void DrawNames()
        {
            var cam = controller.ViewCamera;
            if (cam == null) return;
            var scale = PrototypeGui.Scale;
            foreach (var building in controller.Buildings)
            {
                if (building == null) continue;
                var screen = cam.WorldToScreenPoint(building.LabelPosition);
                if (screen.z <= 0f) continue;
                var point = new Vector2(screen.x / scale, (Screen.height - screen.y) / scale);
                var hovered = building == controller.Hovered;
                var rect = new Rect(point.x - 80f, point.y - 26f, 160f, 24f);
                PrototypeGui.Fill(rect, hovered ? new Color(0.32f, 0.27f, 0.12f, 0.95f) : PrototypeGui.PanelColor, true);
                var name = building.IsOpen ? $"<b>{building.DisplayName}</b>" : $"<color=#9aa3b0>{building.DisplayName}</color>";
                GUI.Label(rect, name, PrototypeGui.NameStyle);

                if (!hovered) continue;
                var tip = new Rect(point.x - 150f, rect.yMax + 4f, 300f, 22f);
                PrototypeGui.Fill(tip, PrototypeGui.PanelColor);
                GUI.Label(tip, building.Description, PrototypeGui.NameStyle);
            }
        }

        void DrawPanel(VillageBuilding building)
        {
            var width = Mathf.Min(900f, PrototypeGui.Width - 40f);
            var height = Mathf.Min(700f, PrototypeGui.Height - 100f);
            var panel = new Rect((PrototypeGui.Width - width) / 2f, 80f + (PrototypeGui.Height - 80f - height) / 2f - 10f, width, height);
            PrototypeGui.Fill(new Rect(0f, 0f, PrototypeGui.Width, PrototypeGui.Height), new Color(0f, 0f, 0f, 0.3f));
            PrototypeGui.Panel(panel);
            PrototypeGui.Fill(new Rect(panel.x, panel.y, panel.width, 4f), VillageBuilding.AccentOf(building.Kind));

            var x = panel.x + 16f;
            var inner = panel.width - 32f;
            var y = panel.y + 14f;
            GUI.Label(new Rect(x, y, 300f, 26f), $"<b>{building.DisplayName}</b>", PrototypeGui.Title);
            GUI.Label(new Rect(x, y + 24f, inner - 140f, 20f), building.Description, PrototypeGui.Small);
            if (GUI.Button(new Rect(panel.xMax - 120f, y, 104f, 26f), "Close (Esc)"))
            {
                controller.Close();
                return;
            }

            y += 56f;
            var body = new Rect(x, y, inner, panel.yMax - y - 52f);
            var profile = controller.Profile;

            if (building.Kind == VillageBuildingKind.CommandHall)
            {
                DrawCommandHall(body);
            }
            else if (!building.IsOpen)
            {
                GUI.Label(body, "<color=#9aa3b0>Still being built. Come back in a later update.</color>", PrototypeGui.Centered);
            }
            else if (profile == null)
            {
                GUI.Label(body, "<color=#9aa3b0>Press Play to use the village with your save.</color>", PrototypeGui.Centered);
            }
            else
            {
                switch (building.Kind)
                {
                    case VillageBuildingKind.Dojo:
                        panels.DrawDojo(body);
                        break;
                    case VillageBuildingKind.Shrine:
                        panels.DrawNinjas(body);
                        break;
                    case VillageBuildingKind.Blacksmith:
                        panels.DrawArmory(body);
                        break;
                }
            }

            panels.DrawMessage(new Rect(x, panel.yMax - 40f, inner, 28f));
        }

        void DrawCommandHall(Rect area)
        {
            var profile = controller.Profile;
            var database = controller.Content;
            var y = area.y;
            if (profile != null)
            {
                GUI.Label(new Rect(area.x, y, area.width, 22f),
                    $"<b>Your clan</b>  {profile.OwnedNinjas().Count} ninjas, team size {profile.TeamSize}", PrototypeGui.Label);
                y += 30f;

                var opponentId = profile.Profile.opponent;
                LineupDefinition opponent = null;
                if (!string.IsNullOrEmpty(opponentId) && database != null) database.TryGet(opponentId, out opponent);
                var lineupCount = profile.Profile.lineup?.Count ?? 0;
                GUI.Label(new Rect(area.x, y, area.width, 22f),
                    $"Saved lineup: <b>{lineupCount}</b> ninjas.  Next opponent: <b>{(opponent != null ? opponent.DisplayName : "choose one in battle")}</b>",
                    PrototypeGui.Small);
                y += 30f;
            }

            GUI.Label(new Rect(area.x, y, area.width, 40f),
                "In battle you place your ninjas on the board, pick an opponent and fight. Wins pay Gold, Steel and clan XP, and can drop weapon components.",
                PrototypeGui.Wrap);
            y += 56f;

            var canGo = GameScenes.CanLoad(GameScenes.Battle);
            GUI.enabled = canGo;
            if (GUI.Button(new Rect(area.x + area.width / 2f - 120f, y, 240f, 44f), "To battle!")) controller.GoToBattle();
            GUI.enabled = true;
            if (!canGo)
                GUI.Label(new Rect(area.x, y + 52f, area.width, 20f),
                    "<color=#ff9980>The battle scene is not in the build settings. Run Ninja Realm > Scenes > Create Village Scene.</color>", PrototypeGui.Small);
        }
    }
}
