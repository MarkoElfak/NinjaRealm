using System;
using System.Collections.Generic;
using NinjaRealm.Core.Campaign;
using NinjaRealm.Core.Definitions;
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
    /// shows the campaign map and sends you to battle; a ninja on the plaza opens its details.
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
        ChapterDefinition mapChapter;
        StageDefinition mapStage;
        Vector2 hallScroll;
        List<NinjaDefinition> allNinjas;

        void Awake()
        {
            controller = GetComponent<VillageController>();
            panels = new RosterPanels(() => controller.Profile, () => controller.Content);
            controller.OpenedChanged += _ =>
            {
                panels.ResetScroll();

                // The campaign map opens on the stage picked for the next fight.
                mapChapter = null;
                mapStage = null;
            };
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

        // ---------------------------------------------------------------- the Command Hall and its campaign map

        /// <summary>Shows a chapter of the campaign map, for checking it from the editor menu.</summary>
        public void ShowChapter(int number)
        {
            var profile = controller.Profile;
            if (profile == null) return;
            foreach (var chapter in profile.Campaign.Chapters)
                if (chapter.Number == number) PickChapter(profile, chapter);
        }

        void PickChapter(ProfileService profile, ChapterDefinition chapter)
        {
            mapChapter = chapter;

            // The stage to fight next in this chapter: the first one not cleared that is open, else its first stage.
            mapStage = chapter.Stages.Count > 0 ? chapter.Stages[0] : null;
            foreach (var stage in chapter.Stages)
            {
                if (profile.HasCleared(stage) || !profile.IsUnlocked(stage)) continue;
                mapStage = stage;
                break;
            }
        }

        void DrawCommandHall(Rect area)
        {
            var profile = controller.Profile;
            if (profile == null)
            {
                GUI.Label(area, "<color=#9aa3b0>Press Play to see the campaign with your save.</color>", PrototypeGui.Centered);
                return;
            }

            var campaign = profile.Campaign;
            if (campaign.Stages.Count == 0)
            {
                GUI.Label(area, "<color=#ff9980>There are no campaign stages. Run Ninja Realm > Content > Create Prototype Content.</color>", PrototypeGui.Centered);
                return;
            }

            if (mapChapter == null || mapStage == null)
            {
                mapStage = profile.CurrentStage();
                mapChapter = campaign.ChapterOf(mapStage);
            }

            var y = area.y;

            // Chapters along the top.
            var chapters = campaign.Chapters;
            var tabWidth = (area.width - (chapters.Count - 1) * 8f) / Mathf.Max(1, chapters.Count);
            for (var i = 0; i < chapters.Count; i++)
            {
                var chapter = chapters[i];
                var rect = new Rect(area.x + i * (tabWidth + 8f), y, tabWidth, 30f);
                var open = profile.IsUnlocked(chapter);
                var text = open
                    ? $"{chapter.Number}. {chapter.DisplayName}   ★ {profile.ChapterStars(chapter)} / {chapter.MaxStars}"
                    : $"{chapter.Number}. {chapter.DisplayName}   (locked)";
                GUI.enabled = open;
                if (GUI.Button(rect, text) && chapter != mapChapter) PickChapter(profile, chapter);
                GUI.enabled = true;
                if (chapter == mapChapter) PrototypeGui.Fill(new Rect(rect.x, rect.yMax + 2f, rect.width, 3f), PrototypeGui.Gold);
            }

            y += 42f;
            GUI.Label(new Rect(area.x, y, area.width, 20f), $"<color=#9aa3b0>{mapChapter.Description}</color>", PrototypeGui.Small);
            y += 28f;

            // The chapter's stages, four to a row.
            const int columns = 4;
            const float cardHeight = 74f;
            var cardWidth = (area.width - (columns - 1) * 8f) / columns;
            var stages = mapChapter.Stages;
            for (var i = 0; i < stages.Count; i++)
            {
                var stage = stages[i];
                var rect = new Rect(area.x + i % columns * (cardWidth + 8f), y + i / columns * (cardHeight + 8f), cardWidth, cardHeight);
                DrawStageCard(profile, stage, rect);
            }

            y += Mathf.CeilToInt(stages.Count / (float)columns) * (cardHeight + 8f) + 6f;

            // The chapter's star chests.
            var chests = mapChapter.Chests;
            var stars = profile.ChapterStars(mapChapter);
            var chestWidth = (area.width - Mathf.Max(0, chests.Count - 1) * 8f) / Mathf.Max(1, chests.Count);
            for (var i = 0; i < chests.Count; i++)
                DrawChest(profile, mapChapter, chests[i], stars, new Rect(area.x + i * (chestWidth + 8f), y, chestWidth, 52f));
            if (chests.Count > 0) y += 64f;

            // The picked stage and the way to battle.
            var details = new Rect(area.x, y, area.width, area.yMax - y);
            PrototypeGui.Fill(details, new Color(1f, 1f, 1f, 0.04f));
            DrawStageDetails(profile, mapStage, new Rect(details.x + 12f, details.y + 8f, details.width - 24f, details.height - 16f));
        }

        void DrawStageCard(ProfileService profile, StageDefinition stage, Rect rect)
        {
            var open = profile.IsUnlocked(stage);
            var selected = stage == mapStage;
            PrototypeGui.Fill(rect, selected ? SelectedColor : open ? PrototypeGui.CardColor : PrototypeGui.DisabledColor, selected);
            if (open && GUI.Button(rect, GUIContent.none, GUIStyle.none)) mapStage = stage;

            var label = profile.Campaign.LabelOf(stage);
            var name = stage.Lineup != null ? stage.Lineup.DisplayName : "Nobody";
            var dim = open ? "" : "<color=#7a808c>";
            var dimEnd = open ? "" : "</color>";
            GUI.Label(new Rect(rect.x + 8f, rect.y + 4f, rect.width - 16f, 20f), $"{dim}<b>{label}</b>{dimEnd}{(open ? LineupScreen.KindText(stage.Kind) : "")}", PrototypeGui.Label);
            GUI.Label(new Rect(rect.xMax - 50f, rect.y + 6f, 44f, 16f), $"{dim}Lv {stage.Level}{dimEnd}", PrototypeGui.Small);
            GUI.Label(new Rect(rect.x + 8f, rect.y + 26f, rect.width - 16f, 18f), $"{dim}{name}{dimEnd}", PrototypeGui.Small);
            if (open) PrototypeGui.Pips(new Vector2(rect.x + 9f, rect.y + 54f), profile.StarsOf(stage), StageStars.Max, PrototypeGui.Gold);
            else GUI.Label(new Rect(rect.x + 8f, rect.y + 48f, rect.width - 16f, 18f), "<color=#7a808c>Locked</color>", PrototypeGui.Small);
        }

        void DrawChest(ProfileService profile, ChapterDefinition chapter, StarChest chest, int stars, Rect rect)
        {
            if (chest == null) return;
            var opened = profile.ChestOpened(chapter, chest);
            var ready = !opened && stars >= chest.stars;
            PrototypeGui.Fill(rect, ready ? SelectedColor : PrototypeGui.CardColor, ready);
            var gold = PrototypeGui.Hex(PrototypeGui.Gold);
            GUI.Label(new Rect(rect.x + 8f, rect.y + 4f, rect.width - 110f, 20f), $"<b>Chest</b>  <color=#{gold}>★ {chest.stars}</color>", PrototypeGui.Label);
            GUI.Label(new Rect(rect.x + 8f, rect.y + 26f, rect.width - 110f, 18f), profile.ChestContents(chest), PrototypeGui.Small);

            var button = new Rect(rect.xMax - 100f, rect.y + 11f, 92f, 30f);
            if (opened)
                GUI.Label(button, "<color=#9aa3b0>Opened</color>", PrototypeGui.Centered);
            else if (!ready)
                GUI.Label(button, $"<color=#9aa3b0>{stars} / {chest.stars}</color>", PrototypeGui.Centered);
            else if (GUI.Button(button, "Open"))
                panels.Show(profile.TryOpenChest(chapter, chest, out var message), message);
        }

        void DrawStageDetails(ProfileService profile, StageDefinition stage, Rect area)
        {
            if (stage == null) return;
            var campaign = profile.Campaign;
            var opponent = stage.Lineup;
            var textWidth = area.width - 260f;
            var y = area.y;
            GUI.Label(new Rect(area.x, y, textWidth, 24f),
                $"<b>Stage {campaign.LabelOf(stage)}: {(opponent != null ? opponent.DisplayName : "Nobody")}</b>{LineupScreen.KindText(stage.Kind)}   <color=#9aa3b0>Level {stage.Level}, {(opponent != null ? opponent.Slots.Count : 0)} ninjas</color>",
                PrototypeGui.Label);
            y += 26f;

            var text = opponent != null ? opponent.Description : "";
            var progression = profile.Progression;
            progression.FightPay(stage.RewardTier, true, out var gold, out var steel, out var xp);
            text += $"\n<b>Win:</b> {gold} Gold, {steel} Steel, {xp} clan XP, {progression.DropChance(stage.RewardTier) / 100}% weapon drop.";
            if (!profile.HasCleared(stage))
                text += $"\n<color=#{PrototypeGui.Hex(PrototypeGui.Gold)}>First clear: {LineupScreen.FirstClearText(stage, progression)}.</color>";
            GUI.Label(new Rect(area.x, y, textWidth, area.yMax - y), text, PrototypeGui.Wrap);

            // The button, with the team it will send.
            var button = new Rect(area.xMax - 240f, area.y + 4f, 240f, 44f);
            var canGo = GameScenes.CanLoad(GameScenes.Battle);
            GUI.enabled = canGo && profile.IsUnlocked(stage);
            if (GUI.Button(button, $"To battle: stage {campaign.LabelOf(stage)}"))
            {
                profile.SelectStage(stage);
                controller.GoToBattle();
            }

            GUI.enabled = true;
            var lineupCount = profile.Profile.lineup?.Count ?? 0;
            var note = canGo
                ? $"<color=#9aa3b0>Your lineup: {lineupCount} of {profile.TeamSize} ninjas. You set it up before the fight.</color>"
                : "<color=#ff9980>The battle scene is not in the build settings. Run Ninja Realm > Scenes > Create Village Scene.</color>";
            GUI.Label(new Rect(button.x, button.yMax + 6f, button.width, 54f), note, PrototypeGui.Wrap);
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

        /// <summary>Switches the Dojo's panel to the hall of all ninjas.</summary>
        public void ShowAllNinjas()
        {
            dojoTab = DojoTab.AllNinjas;
            hallScroll = Vector2.zero;
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
