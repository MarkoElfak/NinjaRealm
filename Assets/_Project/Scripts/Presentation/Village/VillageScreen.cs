using System;
using System.Collections.Generic;
using NinjaRealm.Core.Definitions;
using NinjaRealm.Core.Progression;
using NinjaRealm.Presentation.Battle;
using UnityEngine;
using UnityEngine.UIElements;

namespace NinjaRealm.Presentation.Village
{
    /// <summary>
    /// The village's UI, in UI Toolkit: the clan and wallet along the top, a name over each
    /// building and each plaza ninja, and the open panel. The Command Hall shows the campaign
    /// map (<see cref="CommandHallView"/>); the Dojo has two tabs, the day's recruits and a hall
    /// that shows every ninja with its model; the Shrine and Blacksmith show the roster's views
    /// (<see cref="RosterViews"/>); the Mint and Steel Mine collect their income; a ninja on the
    /// plaza opens its details. Layouts: UI/Resources/UI/VillageHud.uxml, BuildingPanel.uxml
    /// and CommandHall.uxml.
    /// </summary>
    [RequireComponent(typeof(VillageController))]
    public sealed class VillageScreen : MonoBehaviour
    {
        enum DojoTab
        {
            Recruits,
            AllNinjas,
        }

        enum ShrineTab
        {
            Ninjas,
            Runes,
        }

        const int HallColumns = 3;
        const float PreviewDegreesPerPixel = 0.6f;
        const float PreviewClickSlop = 6f;
        const float NameMarginX = 90f;
        const float NameTop = 106f;
        const float NameBottomMargin = 40f;

        VillageController controller;
        UIDocument document;
        VisualElement root;
        Label hint;
        Label villageTitle;
        VisualElement topBar;
        Label clanLevel;
        Label clanXp;
        VisualElement clanXpFill;
        VisualElement resources;
        VisualElement panelLayer;
        VisualElement worldNames;
        VisualElement speech;
        Label speechName;
        Label speechText;
        IVisualElementScheduledItem speechHide;
        readonly List<(ResourceDefinition resource, Label amount)> amounts = new List<(ResourceDefinition, Label)>();
        string shownWallet;
        VisualElement runeRail;
        string shownRunes;
        Label railKarma;
        Button railCollect;
        string shownRailKarma;
        float nextRailKarma;

        // Names over the village.
        readonly List<BuildingName> buildingNames = new List<BuildingName>();
        readonly List<PlazaName> plazaNames = new List<PlazaName>();

        // Panels.
        CommandHallView commandHall;
        VisualElement panel;
        VisualElement frame;
        Label panelTitle;
        Label panelSubtitle;
        VisualElement body;
        UiMessage message;
        RosterViews roster;
        DojoTab dojoTab;
        ShrineTab shrineTab;
        NinjaDefinition hallSelected;
        List<NinjaDefinition> allNinjas;
        IncomeView income;
        IVisualElementScheduledItem tick;

        /// <summary>True once the UI is up.</summary>
        public bool Ready { get; private set; }

        void Awake()
        {
            controller = GetComponent<VillageController>();
            roster = new RosterViews(() => controller.Profile, () => controller.Content, (ok, text) => message?.Show(ok, text), Rebuild);
        }

        void OnEnable()
        {
            if (document == null) document = GameUi.Create(transform, "VillageHud");
            if (document == null) return;

            root = document.rootVisualElement;
            hint = root.Q<Label>("hint");
            villageTitle = root.Q<Label>("villageTitle");
            topBar = root.Q("topBar");
            clanLevel = root.Q<Label>("clanLevel");
            clanXp = root.Q<Label>("clanXp");
            clanXpFill = root.Q("clanXpFill");
            resources = root.Q("resources");
            panelLayer = root.Q("panelLayer");
            worldNames = root.Q("worldNames");
            speech = root.Q("speech");
            runeRail = root.Q("runeRail");
            speechName = root.Q<Label>("speechName");
            speechText = root.Q<Label>("speechText");
            Ready = hint != null && villageTitle != null && clanLevel != null && resources != null && panelLayer != null && worldNames != null;
            if (!Ready)
            {
                Debug.LogError("[Village] VillageHud.uxml is missing an element the village needs.", this);
                return;
            }

            amounts.Clear();
            resources.Clear();
            shownWallet = null;
            shownRunes = null;
            panelLayer.Clear();
            worldNames.Clear();
            buildingNames.Clear();
            plazaNames.Clear();
            panel = null;
            commandHall = new CommandHallView(controller, panelLayer);
            runeRail?.RegisterCallback<ClickEvent>(OnRuneRailClicked);
            controller.OverUi = OverRuneRail;
            controller.OpenedChanged += OnOpenedChanged;
            controller.Spoke += OnSpoke;
            OnOpenedChanged(controller.Opened);
            if (speech != null) GameUi.Show(speech, false);
        }

        void OnRuneRailClicked(ClickEvent e)
        {
            // The Collect button does its own thing; the rest of the rail opens the Shrine.
            for (var target = e.target as VisualElement; target != null && target != runeRail; target = target.parent)
                if (target is Button) return;
            OpenShrineRunes();
        }

        /// <summary>True when the mouse is over the rune rail, so a click on it doesn't also open the building behind it.</summary>
        bool OverRuneRail(Vector2 screen)
        {
            if (runeRail == null || runeRail.ClassListContains("hidden")) return false;
            for (var e = GameUi.Under(document, screen); e != null; e = e.parent)
                if (e == runeRail) return true;
            return false;
        }

        void OnDisable()
        {
            runeRail?.UnregisterCallback<ClickEvent>(OnRuneRailClicked);
            if (controller != null) controller.OverUi = null;
            if (controller != null)
            {
                controller.OpenedChanged -= OnOpenedChanged;
                controller.Spoke -= OnSpoke;
            }

            speechHide?.Pause();
            tick?.Pause();
            Ready = false;
        }

        void Update()
        {
            if (!Ready) return;
            hint.text = controller.PanelOpen ? "Esc closes the panel." : "Click a building or a ninja. Drag to look around, scroll to zoom.";
            RefreshWallet();
            RefreshRuneRail();
        }

        // The camera moves in the controller's Update, so the names follow it here.
        void LateUpdate()
        {
            if (!Ready) return;
            var show = !controller.PanelOpen && !controller.Founding && root.panel != null;
            GameUi.Show(worldNames, show);
            if (!show) return;
            PlaceBuildingNames();
            PlacePlazaNames();
        }

        // ---------------------------------------------------------------- what a ninja says

        /// <summary>Shows a ninja's line along the bottom of the screen for a few seconds.</summary>
        void OnSpoke(NinjaDefinition ninja, string line, float seconds)
        {
            if (speech == null || speechName == null || speechText == null) return;
            speechName.text = ninja.DisplayName;
            speechName.style.color = RosterViews.ElementColor(ninja);
            speechText.text = $"\u201C{line}\u201D";
            GameUi.Show(speech, true);
            speechHide?.Pause();
            speechHide = speech.schedule.Execute(() => GameUi.Show(speech, false)).StartingIn((long)(seconds * 1000f));
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
            var village = profile.HomeVillage;
            var wallet = $"{profile.ClanName}/{village?.id}/{profile.ClanLevel}/{profile.ClanXp}/{profile.ClanXpToNext}";
            foreach (var (resource, _) in amounts) wallet += "/" + profile.Balance(resource);
            if (wallet == shownWallet) return;
            shownWallet = wallet;

            villageTitle.text = VillageTitle(profile);
            if (topBar != null)
            {
                // The top bar's stripe flies the home village's colour.
                if (village?.element != null) topBar.style.borderTopColor = village.element.Color;
                else topBar.style.borderTopColor = StyleKeyword.Null;
            }
            clanLevel.text = $"Clan {profile.ClanLevel}";
            clanXp.text = $"{profile.ClanXp} / {profile.ClanXpToNext} XP";
            var fraction = profile.ClanXpToNext > 0 ? Mathf.Clamp01(profile.ClanXp / (float)profile.ClanXpToNext) : 1f;
            clanXpFill.style.width = Length.Percent(fraction * 100f);
            foreach (var (resource, amount) in amounts) amount.text = profile.Balance(resource).ToString();
        }

        /// <summary>The clan and its home village, such as "Silent Moon of Mistwater", in the village's colour.</summary>
        public static string VillageTitle(ProfileService profile)
        {
            if (profile == null || string.IsNullOrEmpty(profile.ClanName)) return "Ninja Village";
            var village = profile.HomeVillage;
            if (village == null) return profile.ClanName;
            var colour = village.element != null ? Color.Lerp(village.element.Color, Color.white, 0.3f) : Color.white;
            return $"{profile.ClanName} <color=#{PrototypeGui.Hex(colour)}>of {village.displayName}</color>";
        }

        Label AddResource(ResourceDefinition resource)
        {
            var chip = GameUi.Element("resource");
            var dot = GameUi.Element("resource__dot");
            dot.style.backgroundColor = resource.Color;
            chip.Add(dot);
            chip.Add(GameUi.Label(resource.DisplayName, "resource__name"));
            var amount = GameUi.Label("0", "resource__amount");
            amount.style.color = resource.Color;
            chip.Add(amount);
            resources.Add(chip);
            return amount;
        }

        // ---------------------------------------------------------------- the rune rail

        /// <summary>
        /// The altar's runes down the left side of the village: each slot's rune with its rank and
        /// what it gives, the empty and unlit slots, and a click opens the Shrine on its runes.
        /// Hidden while a panel is open, during the new game screen, or without a save.
        /// </summary>
        void RefreshRuneRail()
        {
            if (runeRail == null) return;
            var profile = controller.Profile;
            var show = profile != null && profile.RuneSlotCount > 0 && !controller.PanelOpen && !controller.Founding;
            GameUi.Show(runeRail, show);
            if (!show) return;

            // Only rebuild when the altar, the ranks or the clan level changed.
            var state = $"{profile.ClanLevel}/{profile.RuneSlotsLit}";
            for (var i = 0; i < profile.RuneSlotsLit; i++)
            {
                var rune = profile.RuneInSlot(i);
                state += rune != null ? $"/{rune.Id}:{profile.RuneRankOf(rune)}" : "/-";
            }

            if (state != shownRunes)
            {
                shownRunes = state;
                BuildRuneRail(profile);
            }

            RefreshRailKarma(profile);
        }

        /// <summary>The altar's Karma at the foot of the rail: what is ready and how fast it fills. Updated twice a second.</summary>
        void RefreshRailKarma(ProfileService profile)
        {
            if (railKarma == null || railCollect == null) return;
            if (shownRailKarma != null && Time.unscaledTime < nextRailKarma) return;
            nextRailKarma = Time.unscaledTime + 0.5f;

            var perHour = profile.AltarKarmaPerHour;
            var ready = profile.PendingAltarKarma(DateTime.UtcNow);
            var text = perHour > 0 ? $"Karma  +{perHour} an hour" : "Set a rune to make Karma";
            var button = ready > 0 ? $"Collect {ready}" : "Filling";
            if (shownRailKarma == text + button) return;
            shownRailKarma = text + button;
            railKarma.text = text;
            railCollect.text = button;
            railCollect.SetEnabled(ready > 0);
        }

        void CollectAltarKarma()
        {
            var profile = controller.Profile;
            if (profile == null) return;
            profile.TryCollectAltarKarma(DateTime.UtcNow, out _);
            shownRailKarma = null;
        }

        void BuildRuneRail(ProfileService profile)
        {
            runeRail.Clear();
            var set = profile.SetRunes().Count;
            var header = GameUi.Element("rune-rail__header");
            header.Add(GameUi.Label("Runes", "rune-rail__title"));
            header.Add(GameUi.Label($"{set} / {profile.RuneSlotsLit}", "rune-rail__count"));
            runeRail.Add(header);

            for (var i = 0; i < profile.RuneSlotCount; i++)
            {
                var row = GameUi.Element("rune-rail__slot");
                var icon = GameUi.Element("rune-icon", "rune-rail__icon");
                var text = GameUi.Element("rune-rail__text");
                if (i < profile.RuneSlotsLit)
                {
                    var rune = profile.RuneInSlot(i);
                    if (rune != null)
                    {
                        var rank = profile.RuneRankOf(rune);
                        var texture = RuneArt.Icon(rune);
                        if (texture != null) icon.style.backgroundImage = new StyleBackground(texture);
                        row.style.borderLeftColor = RuneArt.RarityColor(rune.Rarity);
                        text.Add(GameUi.Label($"{rune.DisplayName} <color=#{PrototypeGui.Hex(RuneArt.RarityColor(rune.Rarity))}>{ProfileService.RomanRank(rank)}</color>", "rune-rail__name"));
                        text.Add(GameUi.Label(rune.Describe(rank), "rune-rail__effect"));
                    }
                    else
                    {
                        icon.AddToClassList("rune-rail__icon--empty");
                        icon.Add(GameUi.Label("+", "rune-rail__plus"));
                        text.Add(GameUi.Label("Empty slot", "rune-rail__name", "muted"));
                        text.Add(GameUi.Label("Set a rune at the Shrine", "rune-rail__effect"));
                    }
                }
                else
                {
                    var step = profile.Progression.RuneSlots[i];
                    row.AddToClassList("rune-rail__slot--locked");
                    icon.AddToClassList("rune-rail__icon--empty");
                    var ready = step == profile.NextRuneSlot && profile.ClanLevel >= step.clanLevel;
                    text.Add(GameUi.Label(ready ? "Ready to light" : "Unlit", "rune-rail__name", ready ? "gold-text" : "muted"));
                    text.Add(GameUi.Label(ready ? $"{step.karma} Karma at the Shrine" : $"Opens at clan {step.clanLevel}", "rune-rail__effect"));
                }

                row.Add(icon);
                row.Add(text);
                runeRail.Add(row);
            }

            var karma = GameUi.Element("rune-rail__karma");
            railKarma = GameUi.Label("", "rune-rail__karma-text");
            if (profile.Progression.Karma != null) railKarma.style.color = profile.Progression.Karma.Color;
            karma.Add(railKarma);
            railCollect = GameUi.Button("", CollectAltarKarma, "button--small", "rune-rail__collect");
            karma.Add(railCollect);
            runeRail.Add(karma);
            shownRailKarma = null;

            runeRail.Add(GameUi.Label("Click to open the Shrine", "rune-rail__hint"));
        }

        /// <summary>Opens the Shrine's panel on its runes, from the rune rail.</summary>
        void OpenShrineRunes()
        {
            var shrine = controller.Find(VillageBuildingKind.Shrine);
            if (shrine == null) return;
            shrineTab = ShrineTab.Runes;
            controller.Open(shrine);
        }

        // ---------------------------------------------------------------- names over the village

        sealed class BuildingName
        {
            public VillageBuilding Building;
            public VisualElement Root;
            public Label Chip;
            public Label Tip;
            public string ChipText;
        }

        sealed class PlazaName
        {
            public NinjaDefinition Ninja;
            public VisualElement Root;
            public Label Level;
            public Label Hint;
        }

        /// <summary>A building's name plate over its roof, with what the Mint or Steel Mine has ready and, on hover, what it does.</summary>
        void PlaceBuildingNames()
        {
            var buildings = controller.Buildings;
            if (buildingNames.Count != buildings.Count) BuildBuildingNames();

            var cam = controller.ViewCamera;
            var profile = controller.Profile;
            var now = DateTime.UtcNow;
            var size = root.layout.size;
            foreach (var name in buildingNames)
            {
                var building = name.Building;
                if (!Place(name.Root, building != null ? building.LabelPosition : Vector3.zero, cam, building != null, true, size)) continue;

                var hovered = building == controller.Hovered;
                name.Root.EnableInClassList("world-name--hovered", hovered);
                GameUi.Show(name.Tip, hovered);

                // What the Mint, the Steel Mine or the Shrine's altar has ready, so the player sees it without opening it.
                var incomeDefinition = profile != null && building.IncomeId != null ? profile.Progression.Income(building.IncomeId) : null;
                var resource = incomeDefinition?.resource;
                var ready = resource != null ? profile.PendingIncome(building.IncomeId, now) : 0;
                if (profile != null && building.Kind == VillageBuildingKind.Shrine && profile.Progression.Karma != null)
                {
                    resource = profile.Progression.Karma;
                    ready = profile.PendingAltarKarma(now);
                }

                GameUi.Show(name.Chip, ready > 0);
                if (ready <= 0) continue;
                var chip = $"+{ready} {resource.DisplayName}";
                if (chip == name.ChipText) continue;
                name.ChipText = chip;
                name.Chip.text = chip;
                name.Chip.style.color = resource.Color;
            }
        }

        void BuildBuildingNames()
        {
            foreach (var name in buildingNames) name.Root.RemoveFromHierarchy();
            buildingNames.Clear();
            foreach (var building in controller.Buildings)
            {
                var element = GameUi.Element("world-name");
                element.EnableInClassList("world-name--closed", building != null && !building.IsOpen);
                element.Add(GameUi.Label(building != null ? building.DisplayName : "", "world-name__plate"));
                var chip = GameUi.Label("", "world-name__chip", "hidden");
                element.Add(chip);
                var tip = GameUi.Label(building != null ? building.Description : "", "world-name__tip", "hidden");
                element.Add(tip);
                GameUi.IgnorePicking(element);
                worldNames.Add(element);
                buildingNames.Add(new BuildingName { Building = building, Root = element, Chip = chip, Tip = tip });
            }
        }

        /// <summary>A name, with the level under it, over each ninja on the plaza.</summary>
        void PlacePlazaNames()
        {
            var ninjas = controller.Ninjas;
            if (ninjas == null) return;
            if (!SameNinjas(ninjas)) BuildPlazaNames(ninjas);

            var cam = controller.ViewCamera;
            var profile = controller.Profile;
            var size = root.layout.size;
            for (var i = 0; i < plazaNames.Count; i++)
            {
                var name = plazaNames[i];
                var view = i < ninjas.Views.Count ? ninjas.Views[i] : null;
                if (!Place(name.Root, view != null ? ninjas.LabelPosition(i) : Vector3.zero, cam, view != null, false, size)) continue;

                var hovered = name.Ninja == controller.HoveredNinja;
                name.Root.EnableInClassList("plaza-name--hovered", hovered);
                GameUi.Show(name.Hint, hovered);
                GameUi.Show(name.Level, profile != null);
                if (profile != null) name.Level.text = $"Lv {profile.RankOf(name.Ninja).Level}";
            }
        }

        bool SameNinjas(VillageNinjas ninjas)
        {
            if (plazaNames.Count != ninjas.Ninjas.Count) return false;
            for (var i = 0; i < plazaNames.Count; i++)
                if (plazaNames[i].Ninja != ninjas.Ninjas[i]) return false;
            return true;
        }

        void BuildPlazaNames(VillageNinjas ninjas)
        {
            foreach (var name in plazaNames) name.Root.RemoveFromHierarchy();
            plazaNames.Clear();
            foreach (var ninja in ninjas.Ninjas)
            {
                var element = GameUi.Element("plaza-name");
                var hintLabel = GameUi.Label("Click for details", "plaza-name__hint", "hidden");
                element.Add(hintLabel);
                element.Add(GameUi.Label(ninja.DisplayName, "plaza-name__name"));
                var level = GameUi.Label("", "plaza-name__level");
                element.Add(level);
                GameUi.IgnorePicking(element);
                worldNames.Add(element);
                plazaNames.Add(new PlazaName { Ninja = ninja, Root = element, Level = level, Hint = hintLabel });
            }
        }

        /// <summary>
        /// Puts a name at a point of the world, or hides it when the point is behind the camera.
        /// Building names stay on screen and below the top bar when the roof is out of view.
        /// </summary>
        bool Place(VisualElement element, Vector3 world, Camera cam, bool exists, bool clamp, Vector2 size)
        {
            var visible = exists && cam != null && cam.WorldToScreenPoint(world).z > 0f;
            GameUi.Show(element, visible);
            if (!visible) return false;

            var point = RuntimePanelUtils.CameraTransformWorldToPanel(root.panel, world, cam);
            if (clamp && size.x > 0f && size.y > 0f)
            {
                point.x = Mathf.Clamp(point.x, NameMarginX, size.x - NameMarginX);
                point.y = Mathf.Clamp(point.y, NameTop, size.y - NameBottomMargin);
            }

            element.style.left = point.x;
            element.style.top = point.y;
            return true;
        }

        // ---------------------------------------------------------------- panels

        void OnOpenedChanged(VillageBuilding building)
        {
            if (!Ready) return;
            commandHall.Hide();
            GameUi.Show(panel, false);
            tick?.Pause();
            income = null;
            message?.Clear();

            if (building != null && building.Kind == VillageBuildingKind.CommandHall)
                commandHall.Show();
            else if (building != null)
                ShowBuilding(building);
            else if (controller.OpenedNinja != null)
                ShowNinja(controller.OpenedNinja);
        }

        /// <summary>Shows the shared panel frame with a title, a line under it and an accent along the top.</summary>
        void OpenFrame(string title, string subtitle, Color accent, string size)
        {
            if (panel == null)
            {
                panel = GameUi.Instantiate("BuildingPanel");
                if (panel == null) return;
                panelLayer.Add(panel);
                frame = panel.Q("frame");
                panelTitle = panel.Q<Label>("title");
                panelSubtitle = panel.Q<Label>("subtitle");
                body = panel.Q("body");
                message = new UiMessage(panel.Q<Label>("message"));
                panel.Q<Button>("close").clicked += controller.Close;
            }

            panelTitle.text = title;
            panelSubtitle.text = subtitle;
            frame.style.borderTopColor = accent;
            foreach (var name in new[] { "building-panel--tall", "building-panel--medium", "building-panel--narrow" })
                frame.EnableInClassList(name, name == size);
            message.Clear();
            GameUi.Show(panel, true);
        }

        void ShowBuilding(VillageBuilding building)
        {
            var size = building.IncomeId != null ? "building-panel--narrow" : "building-panel--tall";
            OpenFrame(building.DisplayName, building.Description, VillageBuilding.AccentOf(building.Kind), size);
            Rebuild();
        }

        void ShowNinja(NinjaDefinition ninja)
        {
            OpenFrame("Your ninja", "Level it up here or at the Shrine.", RosterViews.ElementColor(ninja), "building-panel--medium");
            Rebuild();
        }

        /// <summary>Builds the open panel's content again, keeping its lists where they were scrolled to.</summary>
        void Rebuild()
        {
            if (body == null) return;
            var offsets = new List<Vector2>();
            body.Query<ScrollView>().ForEach(view => offsets.Add(view.scrollOffset));
            body.Clear();
            tick?.Pause();
            income = null;

            var building = controller.Opened;
            var profile = controller.Profile;
            if (building == null && controller.OpenedNinja != null)
                body.Add(NinjaBody(controller.OpenedNinja));
            else if (building == null)
                return;
            else if (building.Kind == VillageBuildingKind.Dojo)
                body.Add(DojoBody());
            else if (!building.IsOpen)
                body.Add(Note("Still being built. Come back in a later update."));
            else if (profile == null)
                body.Add(Note("Press Play to use the village with your save."));
            else if (building.IncomeId != null)
                body.Add(IncomeBody(building));
            else if (building.Kind == VillageBuildingKind.Shrine)
                body.Add(ShrineBody());
            else if (building.Kind == VillageBuildingKind.Blacksmith)
                body.Add(roster.Armory());

            var restored = 0;
            body.Query<ScrollView>().ForEach(view =>
            {
                if (restored < offsets.Count) GameUi.RestoreScroll(view, offsets[restored]);
                restored++;
            });
        }

        static Label Note(string text) => GameUi.Label(text, "panel-note", "wrap");

        // ---------------------------------------------------------------- the Shrine

        /// <summary>The Shrine's two tabs: level ups for your ninjas, and the runes on the altar.</summary>
        VisualElement ShrineBody()
        {
            var content = GameUi.Element("roster");
            var profile = controller.Profile;
            var tabs = GameUi.Element("tabs");
            tabs.Add(ShrineTabButton("Ninjas", ShrineTab.Ninjas));
            tabs.Add(ShrineTabButton($"Runes  ({profile.SetRunes().Count} / {profile.RuneSlotsLit})", ShrineTab.Runes));
            content.Add(tabs);
            content.Add(shrineTab == ShrineTab.Runes ? roster.Runes() : roster.Ninjas());
            return content;
        }

        Button ShrineTabButton(string text, ShrineTab tab)
        {
            var button = GameUi.Button(text, () =>
            {
                if (shrineTab == tab) return;
                shrineTab = tab;
                Rebuild();
            }, "tab");
            button.EnableInClassList("tab--selected", shrineTab == tab);
            return button;
        }

        /// <summary>Opens the Shrine's panel on its runes.</summary>
        public void ShowRunes()
        {
            shrineTab = ShrineTab.Runes;
            if (controller.Opened != null && controller.Opened.Kind == VillageBuildingKind.Shrine) Rebuild();
        }

        // ---------------------------------------------------------------- the Dojo and its hall

        VisualElement DojoBody()
        {
            var content = GameUi.Element("roster");
            var profile = controller.Profile;
            var total = AllNinjas().Count;
            var hallName = profile != null ? $"All ninjas  ({profile.OwnedNinjas().Count} / {total})" : $"All ninjas  ({total})";

            var tabs = GameUi.Element("tabs");
            tabs.Add(Tab("Recruits", DojoTab.Recruits));
            tabs.Add(Tab(hallName, DojoTab.AllNinjas));
            content.Add(tabs);

            if (dojoTab == DojoTab.AllNinjas)
                content.Add(Hall());
            else if (profile == null)
                content.Add(Note("Press Play to recruit with your save."));
            else
                content.Add(roster.Recruits());
            return content;
        }

        Button Tab(string text, DojoTab tab)
        {
            var button = GameUi.Button(text, () =>
            {
                if (dojoTab == tab) return;
                dojoTab = tab;
                if (tab != DojoTab.AllNinjas && controller.Preview != null) controller.Preview.Hide();
                Rebuild();
            }, "tab");
            button.EnableInClassList("tab--selected", dojoTab == tab);
            return button;
        }

        /// <summary>Switches the Dojo's panel to the hall of all ninjas.</summary>
        public void ShowAllNinjas()
        {
            dojoTab = DojoTab.AllNinjas;
            if (controller.Opened != null && controller.Opened.Kind == VillageBuildingKind.Dojo) Rebuild();
        }

        /// <summary>Every ninja in the content, sorted once.</summary>
        List<NinjaDefinition> AllNinjas() => allNinjas ??= controller.AllNinjas();

        /// <summary>
        /// The hall of all ninjas: the picked ninja's model on the left, a card for every ninja on
        /// the right (owned or not), and the picked ninja's details under the cards.
        /// </summary>
        VisualElement Hall()
        {
            var ninjas = AllNinjas();
            if (ninjas.Count == 0) return Note("The content has no ninjas.");
            if (hallSelected == null || !ninjas.Contains(hallSelected)) hallSelected = ninjas[0];
            var profile = controller.Profile;

            var layout = GameUi.Element("hall-layout");
            var left = GameUi.Element("preview-column");
            left.Add(Preview(hallSelected));
            if (profile != null)
            {
                var owned = profile.Owns(hallSelected);
                left.Add(GameUi.Label(owned ? "<color=#99ff99>In your clan</color>" : "Not recruited yet", "preview-caption", owned ? "strong" : "muted"));
            }

            left.Add(GameUi.Label("Drag the ninja to turn it, click it to hear it.", "preview-caption", "small"));
            layout.Add(left);

            var right = GameUi.Element("hall-right");
            var grid = new ScrollView(ScrollViewMode.Vertical);
            grid.AddToClassList("hall-grid");
            VisualElement row = null;
            for (var i = 0; i < ninjas.Count; i++)
            {
                if (i % HallColumns == 0)
                {
                    row = GameUi.Element("hall-row");
                    grid.Add(row);
                }

                var last = i % HallColumns == HallColumns - 1 || i == ninjas.Count - 1;
                row.Add(HallCard(ninjas[i], last));
            }

            right.Add(grid);
            right.Add(roster.Details(hallSelected, false));
            layout.Add(right);
            return layout;
        }

        Button HallCard(NinjaDefinition ninja, bool last)
        {
            var profile = controller.Profile;
            var card = new Button(() =>
            {
                controller.Speak(ninja);
                if (ninja == hallSelected) return;
                hallSelected = ninja;
                Rebuild();
            });
            card.AddToClassList("hall-card");
            card.EnableInClassList("hall-card--last", last);
            card.EnableInClassList("hall-card--selected", ninja == hallSelected);
            card.style.borderLeftColor = RosterViews.ElementColor(ninja);
            card.Add(RosterViews.NameLine(ninja));

            var owned = profile?.Find(ninja);
            var second = owned != null ? LineupScreen.RankText(owned.Rank)
                : profile != null ? "Not recruited"
                : RosterViews.ElementAndClass(ninja);
            card.Add(GameUi.Label(second, owned != null ? "strong" : "small"));
            return card;
        }

        /// <summary>
        /// The ninja's model, filmed on the preview stage. Dragging across it turns the ninja, and
        /// a click makes it say something.
        /// </summary>
        VisualElement Preview(NinjaDefinition ninja)
        {
            var view = GameUi.Element("preview");
            var stage = controller.Preview;
            if (stage == null) return view;

            stage.Show(ninja);
            if (stage.Texture is RenderTexture texture) view.style.backgroundImage = new StyleBackground(Background.FromRenderTexture(texture));

            var dragged = 0f;
            view.RegisterCallback<PointerDownEvent>(e =>
            {
                dragged = 0f;
                view.CapturePointer(e.pointerId);
            });
            view.RegisterCallback<PointerMoveEvent>(e =>
            {
                if (!view.HasPointerCapture(e.pointerId)) return;
                dragged += Mathf.Abs(e.deltaPosition.x) + Mathf.Abs(e.deltaPosition.y);
                stage.Turn(-e.deltaPosition.x * PreviewDegreesPerPixel);
            });
            view.RegisterCallback<PointerUpEvent>(e =>
            {
                if (!view.HasPointerCapture(e.pointerId)) return;
                view.ReleasePointer(e.pointerId);
                if (dragged < PreviewClickSlop) controller.Speak(ninja);
            });

            // The stage camera draws into the texture every frame; keep the panel showing the new picture.
            tick = view.schedule.Execute(view.MarkDirtyRepaint).Every(33);
            return view;
        }

        // ---------------------------------------------------------------- a ninja from the plaza

        VisualElement NinjaBody(NinjaDefinition ninja)
        {
            var layout = GameUi.Element("hall-layout");
            var left = GameUi.Element("preview-column");
            left.Add(Preview(ninja));
            left.Add(GameUi.Label("Drag the ninja to turn it, click it to hear it.", "preview-caption", "small"));
            layout.Add(left);

            var right = GameUi.Element("hall-right");
            right.Add(roster.Details(ninja, true));
            layout.Add(right);
            return layout;
        }

        // ---------------------------------------------------------------- the Mint and the Steel Mine

        sealed class IncomeView
        {
            public string Id;
            public ResourceDefinition Resource;
            public VisualElement Fill;
            public Label Ready;
            public Label Full;
            public Button Collect;
        }

        VisualElement IncomeBody(VillageBuilding building)
        {
            var profile = controller.Profile;
            var id = building.IncomeId;
            var definition = profile.Progression.Income(id);
            if (definition == null || definition.resource == null)
                return GameUi.Label("The Progression asset has no income for this building.", "panel-note", "danger-text");

            var resource = definition.resource;
            var hex = PrototypeGui.Hex(resource.Color);
            var content = GameUi.Element("income");
            content.Add(GameUi.Label(
                $"Makes <color=#{hex}><b>{profile.IncomePerHour(id)} {resource.DisplayName}</b></color> an hour and holds up to {profile.IncomeCapacity(id)}.",
                "income__line", "income__big"));
            content.Add(GameUi.Label($"A higher clan level makes more. Your clan is level {profile.ClanLevel}.", "income__line", "note"));

            var bar = GameUi.Element("bar", "income__bar");
            var fill = GameUi.Element("bar__fill");
            fill.style.backgroundColor = resource.Color;
            bar.Add(fill);
            content.Add(bar);

            var ready = GameUi.Label("", "income__ready");
            ready.style.color = resource.Color;
            content.Add(ready);
            var full = GameUi.Label("", "income__line", "note");
            content.Add(full);

            var collect = GameUi.Button("", () =>
            {
                var collected = profile.TryCollectIncome(id, DateTime.UtcNow, out var result);
                message.Show(collected, result);
                RefreshIncome();
            }, "button--primary", "income__collect");
            content.Add(collect);

            income = new IncomeView { Id = id, Resource = resource, Fill = fill, Ready = ready, Full = full, Collect = collect };
            RefreshIncome();
            tick = content.schedule.Execute(RefreshIncome).Every(1000);
            return content;
        }

        /// <summary>The income fills over time, so its numbers update every second while the panel is open.</summary>
        void RefreshIncome()
        {
            var profile = controller.Profile;
            if (income == null || profile == null) return;
            var now = DateTime.UtcNow;
            var ready = profile.PendingIncome(income.Id, now);
            var capacity = profile.IncomeCapacity(income.Id);
            var toFull = profile.IncomeTimeToFull(income.Id, now);
            var name = income.Resource.DisplayName;

            income.Fill.style.width = Length.Percent(capacity > 0 ? Mathf.Clamp01(ready / (float)capacity) * 100f : 0f);
            income.Ready.text = $"{ready} / {capacity} {name} ready";
            income.Full.text = toFull > TimeSpan.Zero ? $"Full in {Duration(toFull)}." : "Full. Collect it to start again.";
            income.Collect.text = ready > 0 ? $"Collect {ready} {name}" : "Nothing to collect yet";
            income.Collect.SetEnabled(ready > 0);
        }

        /// <summary>A waiting time in words, such as "2 h 15 min".</summary>
        public static string Duration(TimeSpan time)
        {
            if (time.TotalMinutes < 1d) return "less than a minute";
            var hours = (int)time.TotalHours;
            return hours > 0 ? $"{hours} h {time.Minutes} min" : $"{time.Minutes} min";
        }

        // ---------------------------------------------------------------- editor menu

        /// <summary>Shows a chapter of the open campaign map, for checking it from the editor menu.</summary>
        public void ShowChapter(int number)
        {
            if (Ready) commandHall.ShowChapter(number);
        }
    }
}
