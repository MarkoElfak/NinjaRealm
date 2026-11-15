using System;
using System.Collections.Generic;
using NinjaRealm.Core.Definitions;
using NinjaRealm.Core.Lineups;
using NinjaRealm.Core.Progression;
using NinjaRealm.Presentation.Battle;
using UnityEngine;

namespace NinjaRealm.Presentation.Village
{
    /// <summary>
    /// The village's prototype IMGUI: the clan and wallet along the top, a name over each
    /// building and each plaza ninja, and the open panel. The Dojo has two tabs, the day's
    /// recruits and a hall that shows every ninja with its model; the Shrine and Blacksmith
    /// reuse the roster panels; the Mint and Steel Mine collect their income; the Command Hall
    /// sends you to battle; a ninja on the plaza opens its details.
    /// </summary>
    [RequireComponent(typeof(VillageController))]
    public sealed class VillageScreen : MonoBehaviour
    {
        enum DojoTab
        {
            Recruits,
            AllNinjas,
        }

        const int HallColumns = 3;
        const int HallRowsShown = 3;
        const float HallCardHeight = 56f;
        const float HallGap = 8f;

        static readonly Color SelectedColor = new Color(0.32f, 0.27f, 0.12f, 0.95f);
        static readonly Color StageColor = new Color(0.11f, 0.13f, 0.18f, 1f);

        VillageController controller;
        RosterPanels panels;
        DojoTab dojoTab;
        NinjaDefinition hallSelected;
        Vector2 hallScroll;
        List<NinjaDefinition> allNinjas;

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

            if (!controller.PanelOpen)
            {
                DrawNinjaNames();
                DrawNames();
            }

            DrawTopBar();
            if (controller.Opened != null) DrawPanel(controller.Opened);
            else if (controller.OpenedNinja != null) DrawNinjaPanel(controller.OpenedNinja);
        }

        void DrawTopBar()
        {
            var bar = new Rect(PrototypeGui.Width / 2f - 330f, 10f, 660f, 58f);
            PrototypeGui.Panel(bar);
            GUI.Label(new Rect(bar.x + 14f, bar.y + 6f, 260f, 24f), "<b>Ninja Village</b>", PrototypeGui.Title);
            var hint = controller.PanelOpen ? "Esc closes the panel." : "Click a building or a ninja. Drag to look around, scroll to zoom.";
            GUI.Label(new Rect(bar.xMax - 400f, bar.y + 8f, 386f, 20f), $"<color=#9aa3b0>{hint}</color>", PrototypeGui.Small);

            var profile = controller.Profile;
            var line = profile != null ? LineupScreen.WalletLine(profile) : "No save outside play mode.";
            GUI.Label(new Rect(bar.x + 14f, bar.y + 32f, bar.width - 28f, 20f), line, PrototypeGui.Small);
        }

        // ---------------------------------------------------------------- names over the village

        /// <summary>A name, with the level under it, over each ninja on the plaza.</summary>
        void DrawNinjaNames()
        {
            var cam = controller.ViewCamera;
            var ninjas = controller.Ninjas;
            if (cam == null || ninjas == null) return;
            var scale = PrototypeGui.Scale;
            var profile = controller.Profile;
            for (var i = 0; i < ninjas.Ninjas.Count; i++)
            {
                if (ninjas.Views[i] == null) continue;
                var screen = cam.WorldToScreenPoint(ninjas.LabelPosition(i));
                if (screen.z <= 0f) continue;
                var point = new Vector2(screen.x / scale, (Screen.height - screen.y) / scale);
                var ninja = ninjas.Ninjas[i];
                var hovered = ninja == controller.HoveredNinja;
                if (hovered) PrototypeGui.Fill(new Rect(point.x - 62f, point.y - 54f, 124f, 56f), SelectedColor, true);

                GUI.Label(new Rect(point.x - 70f, point.y - 34f, 140f, 18f), $"<b>{ninja.DisplayName}</b>", PrototypeGui.NameStyle);
                if (profile != null)
                {
                    var level = $"<color=#{PrototypeGui.Hex(PrototypeGui.Gold)}>Lv {profile.RankOf(ninja).Level}</color>";
                    GUI.Label(new Rect(point.x - 70f, point.y - 18f, 140f, 18f), level, PrototypeGui.NameStyle);
                }

                if (hovered) GUI.Label(new Rect(point.x - 70f, point.y - 52f, 140f, 18f), "<color=#9aa3b0>Click for details</color>", PrototypeGui.NameStyle);
            }
        }

        void DrawNames()
        {
            var cam = controller.ViewCamera;
            if (cam == null) return;
            var scale = PrototypeGui.Scale;
            var profile = controller.Profile;
            var now = DateTime.UtcNow;
            foreach (var building in controller.Buildings)
            {
                if (building == null) continue;
                var screen = cam.WorldToScreenPoint(building.LabelPosition);
                if (screen.z <= 0f) continue;
                // Keep the name on screen and below the top bar when the roof is out of view.
                var point = new Vector2(
                    Mathf.Clamp(screen.x / scale, 90f, PrototypeGui.Width - 90f),
                    Mathf.Clamp((Screen.height - screen.y) / scale, 106f, PrototypeGui.Height - 40f));
                var hovered = building == controller.Hovered;
                var rect = new Rect(point.x - 80f, point.y - 26f, 160f, 24f);
                PrototypeGui.Fill(rect, hovered ? SelectedColor : PrototypeGui.PanelColor, true);
                var name = building.IsOpen ? $"<b>{building.DisplayName}</b>" : $"<color=#9aa3b0>{building.DisplayName}</color>";
                GUI.Label(rect, name, PrototypeGui.NameStyle);

                // What the Mint or the Steel Mine has ready, so the player sees it without opening it.
                var below = rect.yMax + 4f;
                var income = profile != null && building.IncomeId != null ? profile.Progression.Income(building.IncomeId) : null;
                var ready = income != null ? profile.PendingIncome(building.IncomeId, now) : 0;
                if (ready > 0 && income.resource != null)
                {
                    var chip = new Rect(point.x - 60f, below, 120f, 20f);
                    PrototypeGui.Fill(chip, PrototypeGui.PanelColor);
                    GUI.Label(chip, $"<color=#{PrototypeGui.Hex(income.resource.Color)}><b>+{ready}</b> {income.resource.DisplayName}</color>", PrototypeGui.NameStyle);
                    below = chip.yMax + 4f;
                }

                if (!hovered) continue;
                var tip = new Rect(point.x - 180f, below, 360f, 22f);
                PrototypeGui.Fill(tip, PrototypeGui.PanelColor);
                GUI.Label(tip, building.Description, PrototypeGui.NameStyle);
            }
        }

        // ---------------------------------------------------------------- panels

        /// <summary>
        /// Dims the village and draws a centred panel with a title, a line under it and a close
        /// button. Gives back the area for the panel's content and the line for messages.
        /// Returns false when the player closed it.
        /// </summary>
        bool PanelFrame(float width, float height, string title, string description, Color accent, out Rect body, out Rect footer)
        {
            width = Mathf.Min(width, PrototypeGui.Width - 40f);
            height = Mathf.Min(height, PrototypeGui.Height - 100f);
            var panel = new Rect((PrototypeGui.Width - width) / 2f, 80f + (PrototypeGui.Height - 80f - height) / 2f - 10f, width, height);
            PrototypeGui.Fill(new Rect(0f, 0f, PrototypeGui.Width, PrototypeGui.Height), new Color(0f, 0f, 0f, 0.3f));
            PrototypeGui.Panel(panel);
            PrototypeGui.Fill(new Rect(panel.x, panel.y, panel.width, 4f), accent);

            var x = panel.x + 16f;
            var inner = panel.width - 32f;
            var y = panel.y + 14f;
            GUI.Label(new Rect(x, y, 300f, 26f), $"<b>{title}</b>", PrototypeGui.Title);
            GUI.Label(new Rect(x, y + 24f, inner - 140f, 20f), description, PrototypeGui.Small);

            y += 56f;
            body = new Rect(x, y, inner, panel.yMax - y - 52f);
            footer = new Rect(x, panel.yMax - 40f, inner, 28f);
            if (!GUI.Button(new Rect(panel.xMax - 120f, panel.y + 14f, 104f, 26f), "Close (Esc)")) return true;
            controller.Close();
            return false;
        }

        void DrawPanel(VillageBuilding building)
        {
            var income = building.IncomeId != null;
            if (!PanelFrame(income ? 620f : 900f, income ? 400f : 700f, building.DisplayName, building.Description,
                    VillageBuilding.AccentOf(building.Kind), out var body, out var footer)) return;

            var profile = controller.Profile;
            if (building.Kind == VillageBuildingKind.CommandHall)
                DrawCommandHall(body);
            else if (building.Kind == VillageBuildingKind.Dojo)
                DrawDojo(body);
            else if (!building.IsOpen)
                GUI.Label(body, "<color=#9aa3b0>Still being built. Come back in a later update.</color>", PrototypeGui.Centered);
            else if (profile == null)
                GUI.Label(body, "<color=#9aa3b0>Press Play to use the village with your save.</color>", PrototypeGui.Centered);
            else if (income)
                DrawIncome(building, body);
            else if (building.Kind == VillageBuildingKind.Shrine)
                panels.DrawNinjas(body);
            else if (building.Kind == VillageBuildingKind.Blacksmith)
                panels.DrawArmory(body);

            panels.DrawMessage(footer);
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

        // ---------------------------------------------------------------- the Dojo and its hall

        void DrawDojo(Rect area)
        {
            var profile = controller.Profile;
            var total = AllNinjas().Count;
            var hallName = profile != null ? $"All ninjas  ({profile.OwnedNinjas().Count} / {total})" : $"All ninjas  ({total})";
            DrawTab(new Rect(area.x, area.y, 150f, 28f), "Recruits", DojoTab.Recruits);
            DrawTab(new Rect(area.x + 158f, area.y, 190f, 28f), hallName, DojoTab.AllNinjas);

            var content = new Rect(area.x, area.y + 42f, area.width, area.height - 42f);
            if (dojoTab == DojoTab.AllNinjas)
                DrawHall(content);
            else if (profile == null)
                GUI.Label(content, "<color=#9aa3b0>Press Play to recruit with your save.</color>", PrototypeGui.Centered);
            else
                panels.DrawDojo(content);
        }

        void DrawTab(Rect rect, string text, DojoTab tab)
        {
            var selected = dojoTab == tab;
            if (GUI.Button(rect, text) && !selected)
            {
                dojoTab = tab;
                panels.ResetScroll();
                hallScroll = Vector2.zero;
                if (tab != DojoTab.AllNinjas && controller.Preview != null) controller.Preview.Hide();
            }

            if (selected) PrototypeGui.Fill(new Rect(rect.x, rect.yMax + 2f, rect.width, 3f), PrototypeGui.Gold);
        }

        /// <summary>Every ninja in the content, sorted once.</summary>
        List<NinjaDefinition> AllNinjas() => allNinjas ??= controller.AllNinjas();

        /// <summary>
        /// The hall of all ninjas: the picked ninja's model on the left, a card for every ninja on
        /// the right (owned or not), and the picked ninja's details under the cards.
        /// </summary>
        void DrawHall(Rect area)
        {
            var ninjas = AllNinjas();
            if (ninjas.Count == 0)
            {
                GUI.Label(area, "<color=#9aa3b0>The content has no ninjas.</color>", PrototypeGui.Centered);
                return;
            }

            if (hallSelected == null || !ninjas.Contains(hallSelected)) hallSelected = ninjas[0];
            var profile = controller.Profile;

            const float previewWidth = 300f;
            var preview = new Rect(area.x, area.y, previewWidth, previewWidth * NinjaPreview.TextureHeight / NinjaPreview.TextureWidth);
            DrawPreview(hallSelected, preview);
            var status = profile == null ? "" : profile.Owns(hallSelected) ? "<color=#99ff99>In your clan</color>" : "<color=#9aa3b0>Not recruited yet</color>";
            GUI.Label(new Rect(preview.x, preview.yMax + 6f, preview.width, 18f), status, PrototypeGui.NameStyle);
            GUI.Label(new Rect(preview.x, preview.yMax + 26f, preview.width, 18f), "<color=#9aa3b0>Drag the ninja to turn it.</color>", PrototypeGui.NameStyle);

            var x = preview.xMax + 16f;
            var width = area.xMax - x;
            var rows = (ninjas.Count + HallColumns - 1) / HallColumns;
            var scrolls = rows > HallRowsShown;
            var grid = new Rect(x, area.y, width, Mathf.Min(rows, HallRowsShown) * (HallCardHeight + HallGap));
            var gridWidth = scrolls ? width - 18f : width;
            var cardWidth = (gridWidth - (HallColumns - 1) * HallGap) / HallColumns;

            hallScroll = GUI.BeginScrollView(grid, hallScroll, new Rect(0f, 0f, gridWidth, rows * (HallCardHeight + HallGap) - HallGap));
            for (var i = 0; i < ninjas.Count; i++)
            {
                var card = new Rect(i % HallColumns * (cardWidth + HallGap), i / HallColumns * (HallCardHeight + HallGap), cardWidth, HallCardHeight);
                DrawHallCard(ninjas[i], card);
            }

            GUI.EndScrollView();

            panels.DrawNinjaDetails(hallSelected, new Rect(x, grid.yMax + 10f, width, area.yMax - grid.yMax - 10f), false);
        }

        void DrawHallCard(NinjaDefinition ninja, Rect card)
        {
            var profile = controller.Profile;
            var owned = profile?.Find(ninja);
            PrototypeGui.Fill(card, ninja == hallSelected ? SelectedColor : PrototypeGui.CardColor, true);
            PrototypeGui.Fill(new Rect(card.x, card.y, 4f, card.height), ninja.Element != null ? ninja.Element.Color : Color.grey);

            GUI.Label(new Rect(card.x + 12f, card.y + 6f, card.width - 16f, 20f),
                $"<b>{ninja.DisplayName}</b>  <color=#{PrototypeGui.Hex(LineupScreen.RarityColor(ninja.Rarity))}>{ninja.Rarity}</color>", PrototypeGui.Label);
            var second = owned != null ? LineupScreen.RankText(owned.Rank)
                : profile != null ? "<color=#9aa3b0>Not recruited</color>"
                : $"{ninja.Element?.DisplayName} {ninja.Class?.DisplayName}";
            GUI.Label(new Rect(card.x + 12f, card.y + 28f, card.width - 16f, 18f), second, PrototypeGui.Small);

            if (GUI.Button(card, GUIContent.none, GUIStyle.none)) hallSelected = ninja;
        }

        /// <summary>The ninja's model, filmed on the preview stage. Dragging across it turns the ninja.</summary>
        void DrawPreview(NinjaDefinition ninja, Rect rect)
        {
            PrototypeGui.Fill(rect, StageColor, true);
            var preview = controller.Preview;
            if (preview == null) return;

            preview.Show(ninja);
            if (preview.Texture != null) GUI.DrawTexture(rect, preview.Texture, ScaleMode.ScaleToFit, false);

            var current = Event.current;
            if (current.type != EventType.MouseDrag || !rect.Contains(current.mousePosition)) return;
            preview.Turn(-current.delta.x * 0.6f);
            current.Use();
        }

        // ---------------------------------------------------------------- a ninja from the plaza

        void DrawNinjaPanel(NinjaDefinition ninja)
        {
            var accent = ninja.Element != null ? ninja.Element.Color : Color.grey;
            if (!PanelFrame(700f, 540f, "Your ninja", "Level it up here or at the Shrine.", accent, out var body, out var footer)) return;

            const float previewWidth = 280f;
            var preview = new Rect(body.x, body.y, previewWidth, previewWidth * NinjaPreview.TextureHeight / NinjaPreview.TextureWidth);
            DrawPreview(ninja, preview);
            GUI.Label(new Rect(preview.x, preview.yMax + 6f, preview.width, 18f), "<color=#9aa3b0>Drag the ninja to turn it.</color>", PrototypeGui.NameStyle);

            var x = preview.xMax + 16f;
            panels.DrawNinjaDetails(ninja, new Rect(x, body.y, body.xMax - x, body.height), true);
            panels.DrawMessage(footer);
        }

        // ---------------------------------------------------------------- the Mint and the Steel Mine

        void DrawIncome(VillageBuilding building, Rect area)
        {
            var profile = controller.Profile;
            var id = building.IncomeId;
            var income = profile.Progression.Income(id);
            if (income == null || income.resource == null)
            {
                GUI.Label(area, "<color=#ff9980>The Progression asset has no income for this building.</color>", PrototypeGui.Centered);
                return;
            }

            var now = DateTime.UtcNow;
            var ready = profile.PendingIncome(id, now);
            var capacity = profile.IncomeCapacity(id);
            var toFull = profile.IncomeTimeToFull(id, now);
            var resource = income.resource.DisplayName;
            var hex = PrototypeGui.Hex(income.resource.Color);

            var y = area.y + 8f;
            GUI.Label(new Rect(area.x, y, area.width, 24f),
                $"Makes <color=#{hex}>{profile.IncomePerHour(id)} {resource}</color> an hour and holds up to {capacity}.", PrototypeGui.Centered);
            y += 28f;
            GUI.Label(new Rect(area.x, y, area.width, 18f),
                $"<color=#9aa3b0>A higher clan level makes more. Your clan is level {profile.ClanLevel}.</color>", PrototypeGui.NameStyle);
            y += 40f;

            var bar = new Rect(area.x + 40f, y, area.width - 80f, 26f);
            PrototypeGui.Bar(bar, capacity > 0 ? ready / (float)capacity : 0f, income.resource.Color);
            y += 34f;
            GUI.Label(new Rect(area.x, y, area.width, 22f), $"<color=#{hex}>{ready} / {capacity} {resource}</color> ready", PrototypeGui.Centered);
            y += 24f;
            var full = toFull > TimeSpan.Zero ? $"Full in {Duration(toFull)}." : "Full. Collect it to start again.";
            GUI.Label(new Rect(area.x, y, area.width, 18f), $"<color=#9aa3b0>{full}</color>", PrototypeGui.NameStyle);
            y += 34f;

            GUI.enabled = ready > 0;
            var label = ready > 0 ? $"Collect {ready} {resource}" : "Nothing to collect yet";
            if (GUI.Button(new Rect(area.x + area.width / 2f - 120f, y, 240f, 44f), label))
                panels.Show(profile.TryCollectIncome(id, DateTime.UtcNow, out var result), result);
            GUI.enabled = true;
        }

        /// <summary>A waiting time in words, such as "2 h 15 min".</summary>
        public static string Duration(TimeSpan time)
        {
            if (time.TotalMinutes < 1d) return "less than a minute";
            var hours = (int)time.TotalHours;
            return hours > 0 ? $"{hours} h {time.Minutes} min" : $"{time.Minutes} min";
        }
    }
}
