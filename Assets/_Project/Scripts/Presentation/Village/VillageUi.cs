using System.Collections.Generic;
using NinjaRealm.Core.Campaign;
using NinjaRealm.Core.Progression;
using NinjaRealm.Presentation.Battle;
using UnityEngine;
using UnityEngine.UIElements;

namespace NinjaRealm.Presentation.Village
{
    /// <summary>
    /// The village's UI Toolkit screens: the clan and wallet along the top, and the Command
    /// Hall's campaign map. The other panels are still drawn by <see cref="VillageScreen"/>.
    /// Layouts: UI/Resources/UI/VillageHud.uxml and CommandHall.uxml.
    /// </summary>
    [RequireComponent(typeof(VillageController))]
    public sealed class VillageUi : MonoBehaviour
    {
        const int StagesPerRow = 4;

        VillageController controller;
        UIDocument document;
        Label hint;
        Label clanLevel;
        Label clanXp;
        VisualElement clanXpFill;
        VisualElement resources;
        VisualElement panelLayer;
        readonly List<(ResourceDefinition resource, Label amount)> amounts = new List<(ResourceDefinition, Label)>();
        string shownWallet;

        // The Command Hall.
        VisualElement hall;
        VisualElement chapterTabs;
        Label chapterDescription;
        VisualElement stages;
        VisualElement chests;
        Label stageTitle;
        Label stageKind;
        Label stageInfo;
        Label stageDescription;
        Label stagePay;
        Label stageFirstClear;
        Button toBattle;
        Label lineupNote;
        Label message;
        ChapterDefinition mapChapter;
        StageDefinition mapStage;

        /// <summary>True once the screens are up, so the IMGUI leaves the top bar and the Command Hall to them.</summary>
        public bool Ready { get; private set; }

        void Awake() => controller = GetComponent<VillageController>();

        void OnEnable()
        {
            if (document == null) document = GameUi.Create(transform, "VillageHud");
            if (document == null) return;

            var root = document.rootVisualElement;
            hint = root.Q<Label>("hint");
            clanLevel = root.Q<Label>("clanLevel");
            clanXp = root.Q<Label>("clanXp");
            clanXpFill = root.Q("clanXpFill");
            resources = root.Q("resources");
            panelLayer = root.Q("panelLayer");
            amounts.Clear();
            resources.Clear();
            shownWallet = null;
            hall = null;
            Ready = hint != null && clanLevel != null && resources != null && panelLayer != null;
            controller.OpenedChanged += OnOpenedChanged;
            OnOpenedChanged(controller.Opened);
        }

        void OnDisable()
        {
            controller.OpenedChanged -= OnOpenedChanged;
            Ready = false;
        }

        void Update()
        {
            if (!Ready) return;
            hint.text = controller.PanelOpen ? "Esc closes the panel." : "Click a building or a ninja. Drag to look around, scroll to zoom.";
            RefreshWallet();
        }

        // ---------------------------------------------------------------- top bar

        void RefreshWallet()
        {
            var profile = controller.Profile;
            if (profile == null)
            {
                if (shownWallet == "") return;
                shownWallet = "";
                clanLevel.text = "No save";
                clanXp.text = "Outside play mode";
                clanXpFill.style.width = Length.Percent(0f);
                return;
            }

            var progression = profile.Progression;
            if (amounts.Count == 0)
                foreach (var resource in new[] { progression.Gold, progression.Karma, progression.Steel })
                    if (resource != null) amounts.Add((resource, AddResource(resource)));

            // Only touch the labels when something changed.
            var wallet = $"{profile.ClanLevel}/{profile.ClanXp}/{profile.ClanXpToNext}";
            foreach (var (resource, _) in amounts) wallet += "/" + profile.Balance(resource);
            if (wallet == shownWallet) return;
            shownWallet = wallet;

            clanLevel.text = $"Clan {profile.ClanLevel}";
            clanXp.text = $"{profile.ClanXp} / {profile.ClanXpToNext} XP";
            var fraction = profile.ClanXpToNext > 0 ? Mathf.Clamp01(profile.ClanXp / (float)profile.ClanXpToNext) : 1f;
            clanXpFill.style.width = Length.Percent(fraction * 100f);
            foreach (var (resource, amount) in amounts) amount.text = profile.Balance(resource).ToString();
        }

        Label AddResource(ResourceDefinition resource)
        {
            var chip = new VisualElement();
            chip.AddToClassList("resource");
            var dot = new VisualElement();
            dot.AddToClassList("resource__dot");
            dot.style.backgroundColor = resource.Color;
            chip.Add(dot);
            chip.Add(GameUi.Label(resource.DisplayName, "resource__name"));
            var amount = GameUi.Label("0", "resource__amount");
            amount.style.color = resource.Color;
            chip.Add(amount);
            resources.Add(chip);
            return amount;
        }

        // ---------------------------------------------------------------- panels

        void OnOpenedChanged(VillageBuilding building)
        {
            if (!Ready) return;
            var commandHall = building != null && building.Kind == VillageBuildingKind.CommandHall;
            if (commandHall)
            {
                // The campaign map opens on the stage picked for the next fight.
                mapChapter = null;
                mapStage = null;
                ShowCommandHall();
            }
            else if (hall != null)
            {
                GameUi.Show(hall, false);
            }
        }

        /// <summary>True when this building's panel is drawn here rather than by the IMGUI.</summary>
        public bool Draws(VillageBuilding building) => Ready && building != null && building.Kind == VillageBuildingKind.CommandHall;

        // ---------------------------------------------------------------- the Command Hall

        /// <summary>Shows a chapter of the open campaign map, for checking it from the editor menu.</summary>
        public void ShowChapter(int number)
        {
            var profile = controller.Profile;
            if (profile == null || hall == null) return;
            foreach (var chapter in profile.Campaign.Chapters)
                if (chapter.Number == number) PickChapter(profile, chapter);
            RefreshHall();
        }

        void ShowCommandHall()
        {
            if (hall == null)
            {
                hall = GameUi.Instantiate("CommandHall");
                if (hall == null) return;
                panelLayer.Add(hall);
                chapterTabs = hall.Q("chapterTabs");
                chapterDescription = hall.Q<Label>("chapterDescription");
                stages = hall.Q("stages");
                chests = hall.Q("chests");
                stageTitle = hall.Q<Label>("stageTitle");
                stageKind = hall.Q<Label>("stageKind");
                stageInfo = hall.Q<Label>("stageInfo");
                stageDescription = hall.Q<Label>("stageDescription");
                stagePay = hall.Q<Label>("stagePay");
                stageFirstClear = hall.Q<Label>("stageFirstClear");
                toBattle = hall.Q<Button>("toBattle");
                lineupNote = hall.Q<Label>("lineupNote");
                message = hall.Q<Label>("message");
                hall.Q<Button>("close").clicked += controller.Close;
                toBattle.clicked += GoToBattle;
            }

            message.text = "";
            GameUi.Show(hall, true);
            RefreshHall();
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

        void RefreshHall()
        {
            if (hall == null) return;
            var profile = controller.Profile;
            chapterTabs.Clear();
            stages.Clear();
            chests.Clear();

            var campaign = profile?.Campaign;
            if (campaign == null || campaign.Stages.Count == 0)
            {
                chapterDescription.text = profile == null
                    ? "Press Play to see the campaign with your save."
                    : "There are no campaign stages. Run Ninja Realm > Content > Create Prototype Content.";
                GameUi.Show(hall.Q("details"), false);
                return;
            }

            GameUi.Show(hall.Q("details"), true);
            if (mapChapter == null || mapStage == null)
            {
                mapStage = profile.CurrentStage();
                mapChapter = campaign.ChapterOf(mapStage);
            }

            // Chapters along the top.
            var chapters = campaign.Chapters;
            for (var i = 0; i < chapters.Count; i++)
            {
                var chapter = chapters[i];
                var open = profile.IsUnlocked(chapter);
                var tab = new Button(() =>
                {
                    if (chapter == mapChapter) return;
                    PickChapter(profile, chapter);
                    RefreshHall();
                });
                tab.AddToClassList("chapter-tab");
                tab.EnableInClassList("chapter-tab--last", i == chapters.Count - 1);
                tab.EnableInClassList("chapter-tab--selected", chapter == mapChapter);
                tab.SetEnabled(open);
                tab.Add(GameUi.Label($"{chapter.Number}. {chapter.DisplayName}", "chapter-tab__name"));
                tab.Add(GameUi.Label(open ? $"{profile.ChapterStars(chapter)} / {chapter.MaxStars} stars" : "Locked", "chapter-tab__stars"));
                chapterTabs.Add(tab);
            }

            chapterDescription.text = mapChapter.Description;

            // The chapter's stages, four to a row.
            VisualElement row = null;
            var chapterStages = mapChapter.Stages;
            for (var i = 0; i < chapterStages.Count; i++)
            {
                if (i % StagesPerRow == 0)
                {
                    row = new VisualElement();
                    row.AddToClassList("stage-row");
                    stages.Add(row);
                }

                var last = i % StagesPerRow == StagesPerRow - 1 || i == chapterStages.Count - 1;
                row.Add(StageCard(profile, chapterStages[i], last));
            }

            // The chapter's star chests.
            var stars = profile.ChapterStars(mapChapter);
            for (var i = 0; i < mapChapter.Chests.Count; i++)
                chests.Add(Chest(profile, mapChapter, mapChapter.Chests[i], stars, i == mapChapter.Chests.Count - 1));
            GameUi.Show(chests, mapChapter.Chests.Count > 0);

            RefreshDetails(profile);
        }

        VisualElement StageCard(ProfileService profile, StageDefinition stage, bool last)
        {
            var open = profile.IsUnlocked(stage);
            var card = new Button(() =>
            {
                mapStage = stage;
                RefreshHall();
            });
            card.AddToClassList("stage-card");
            card.EnableInClassList("stage-card--last", last);
            card.EnableInClassList("stage-card--selected", stage == mapStage);
            card.SetEnabled(open);

            var top = new VisualElement();
            top.AddToClassList("stage-card__top");
            var labelRow = new VisualElement();
            labelRow.AddToClassList("stage-card__label-row");
            labelRow.Add(GameUi.Label(profile.Campaign.LabelOf(stage), "stage-card__label"));
            if (open && stage.Kind != StageKind.Normal) labelRow.Add(Kind(stage.Kind));
            top.Add(labelRow);
            top.Add(GameUi.Label($"Lv {stage.Level}", "stage-card__level"));
            card.Add(top);

            card.Add(GameUi.Label(stage.Lineup != null ? stage.Lineup.DisplayName : "Nobody", "stage-card__name"));
            card.Add(open ? GameUi.Pips(profile.StarsOf(stage), StageStars.Max) : GameUi.Label("Locked", "stage-card__locked"));
            return card;
        }

        static Label Kind(StageKind kind)
        {
            var label = GameUi.Label(kind == StageKind.Boss ? "Boss" : "Elite", "kind");
            label.AddToClassList(kind == StageKind.Boss ? "kind--boss" : "kind--elite");
            return label;
        }

        VisualElement Chest(ProfileService profile, ChapterDefinition chapter, StarChest chest, int stars, bool last)
        {
            var box = new VisualElement();
            box.AddToClassList("chest");
            box.EnableInClassList("chest--last", last);
            if (chest == null) return box;

            var opened = profile.ChestOpened(chapter, chest);
            var ready = !opened && stars >= chest.stars;
            box.EnableInClassList("chest--ready", ready);

            var text = new VisualElement();
            text.AddToClassList("chest__text");
            text.Add(GameUi.Label($"Chest at {chest.stars} stars", "chest__title"));
            text.Add(GameUi.Label(profile.ChestContents(chest), "chest__contents"));
            box.Add(text);

            if (ready)
            {
                var open = new Button(() =>
                {
                    profile.TryOpenChest(chapter, chest, out var result);
                    RefreshHall();
                    message.text = result;
                }) { text = "Open" };
                open.AddToClassList("button--primary");
                open.AddToClassList("button--small");
                box.Add(open);
            }
            else
            {
                box.Add(GameUi.Label(opened ? "Opened" : $"{stars} / {chest.stars}", "chest__state"));
            }

            return box;
        }

        void RefreshDetails(ProfileService profile)
        {
            var stage = mapStage;
            if (stage == null) return;
            var campaign = profile.Campaign;
            var opponent = stage.Lineup;
            stageTitle.text = $"Stage {campaign.LabelOf(stage)}: {(opponent != null ? opponent.DisplayName : "Nobody")}";
            GameUi.Show(stageKind, stage.Kind != StageKind.Normal);
            stageKind.text = stage.Kind == StageKind.Boss ? "Boss" : "Elite";
            stageKind.EnableInClassList("kind--boss", stage.Kind == StageKind.Boss);
            stageKind.EnableInClassList("kind--elite", stage.Kind == StageKind.Elite);
            stageInfo.text = $"Level {stage.Level}, {(opponent != null ? opponent.Slots.Count : 0)} ninjas";
            stageDescription.text = opponent != null ? opponent.Description : "";

            var progression = profile.Progression;
            progression.FightPay(stage.RewardTier, true, out var gold, out var steel, out var xp);
            stagePay.text = $"A win pays {gold} Gold, {steel} Steel and {xp} clan XP, with a {progression.DropChance(stage.RewardTier) / 100}% chance of a weapon.";
            var firstClear = !profile.HasCleared(stage);
            GameUi.Show(stageFirstClear, firstClear);
            if (firstClear) stageFirstClear.text = $"First clear: {LineupScreen.FirstClearText(stage, progression)}.";

            var canGo = GameScenes.CanLoad(GameScenes.Battle);
            toBattle.text = $"To battle: stage {campaign.LabelOf(stage)}";
            toBattle.SetEnabled(canGo && profile.IsUnlocked(stage));
            var lineupCount = profile.Profile.lineup?.Count ?? 0;
            lineupNote.text = canGo
                ? $"Your lineup: {lineupCount} of {profile.TeamSize} ninjas. You set it up before the fight."
                : "The battle scene is not in the build settings. Run Ninja Realm > Scenes > Create Village Scene.";
            lineupNote.EnableInClassList("danger-text", !canGo);
        }

        void GoToBattle()
        {
            var profile = controller.Profile;
            if (profile == null || mapStage == null || !profile.IsUnlocked(mapStage)) return;
            profile.SelectStage(mapStage);
            controller.GoToBattle();
        }
    }
}
