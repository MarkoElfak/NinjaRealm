using System.Collections.Generic;
using System.Text;
using NinjaRealm.Combat;
using NinjaRealm.Core.Campaign;
using NinjaRealm.Core.Definitions;
using NinjaRealm.Core.Lineups;
using NinjaRealm.Core.Progression;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace NinjaRealm.Presentation.Battle
{
    /// <summary>
    /// The lineup screen: drag ninjas from the bench onto your half of the board, drag them
    /// around or off the board, drag weapons onto them, step through the campaign stages and
    /// start the fight. The bench and armory hold what the player owns. Drawn with UI Toolkit
    /// (UI/Resources/UI/Lineup.uxml); the dragging reads the Input System and asks the panel
    /// what is under the mouse.
    /// </summary>
    [RequireComponent(typeof(BattleController))]
    public sealed class LineupScreen : MonoBehaviour
    {
        const float DragThreshold = 6f;
        const float MessageSeconds = 3f;
        const float TooltipWidth = 420f;

        enum DragKind
        {
            None,
            NewNinja,
            MoveSlot,
            Weapon,
        }

        BattleController controller;
        readonly List<NinjaDefinition> roster = new List<NinjaDefinition>();
        readonly List<WeaponDefinition> armory = new List<WeaponDefinition>();
        readonly List<StatDefinition> shownStats = new List<StatDefinition>();

        DragKind drag;
        NinjaDefinition dragNinja;
        LineupSlot dragSlot;
        WeaponDefinition dragWeapon;
        Vector2 pressPosition;
        bool dragMoved;
        LineupSlot selected;
        float messageUntil;

        UIDocument document;
        VisualElement screen;
        VisualElement stagePanel;
        VisualElement infoPanel;
        Label lineupTitle;
        Label wallet;
        Button toVillage;
        Button fight;
        Button clear;
        Button rosterButton;
        VisualElement ninjaCards;
        Label armoryTitle;
        VisualElement weaponCards;
        VisualElement toast;
        Label toastText;
        Label tooltip;
        Label ghost;
        WorldLabels names;
        string shownState;

        void Awake()
        {
            controller = GetComponent<BattleController>();
        }

        void OnEnable()
        {
            if (document == null) document = GameUi.Create(transform, "Lineup", 5f);
            if (document == null) return;

            screen = document.rootVisualElement;
            stagePanel = screen.Q("stagePanel");
            infoPanel = screen.Q("infoPanel");
            lineupTitle = screen.Q<Label>("lineupTitle");
            wallet = screen.Q<Label>("wallet");
            toVillage = screen.Q<Button>("toVillage");
            fight = screen.Q<Button>("fight");
            clear = screen.Q<Button>("clear");
            rosterButton = screen.Q<Button>("roster");
            ninjaCards = screen.Q("ninjaCards");
            armoryTitle = screen.Q<Label>("armoryTitle");
            weaponCards = screen.Q("weaponCards");
            toast = screen.Q("toast");
            toastText = screen.Q<Label>("toastText");
            tooltip = screen.Q<Label>("tooltip");
            ghost = screen.Q<Label>("ghost");
            names = new WorldLabels(screen.Q("boardLabels"), "board-name");

            toVillage.clicked += () => controller.LeaveToVillage();
            fight.clicked += () =>
            {
                if (controller.CanFight) controller.StartFight();
            };
            clear.clicked += () =>
            {
                controller.Lineup?.Clear();
                selected = null;
            };
            rosterButton.clicked += () => controller.RosterOpen = true;
            shownState = null;
        }

        void Start()
        {
            if (!controller.Initialize()) return;
            var database = controller.Content;

            foreach (var id in RosterViews.ShownStatIds)
                if (database.TryGet<StatDefinition>(id, out var stat)) shownStats.Add(stat);

            RefreshLists();
            if (controller.Profile != null) controller.Profile.Changed += RefreshLists;
        }

        void OnDestroy()
        {
            if (controller != null && controller.Profile != null) controller.Profile.Changed -= RefreshLists;
        }

        /// <summary>Fills the bench and armory with what the player owns, or with all content without a save.</summary>
        void RefreshLists()
        {
            var database = controller.Content;
            var profile = controller.Profile;
            roster.Clear();
            armory.Clear();
            shownState = null;
            if (profile != null)
            {
                roster.AddRange(profile.OwnedNinjas());
                armory.AddRange(profile.OwnedWeapons());
                return;
            }

            roster.AddRange(database.GetAll<NinjaDefinition>());
            roster.Sort((a, b) => a.Rarity != b.Rarity ? a.Rarity.CompareTo(b.Rarity) : string.CompareOrdinal(a.Id, b.Id));
            armory.AddRange(database.GetAll<WeaponDefinition>());
            armory.Sort((a, b) => a.Tier != b.Tier ? a.Tier.CompareTo(b.Tier) : string.CompareOrdinal(a.Id, b.Id));
        }

        /// <summary>Copies of a weapon not yet held by a ninja in the lineup. Without a save there is one of each.</summary>
        int FreeCount(WeaponDefinition weapon)
        {
            var owned = controller.Profile != null ? controller.Profile.WeaponCount(weapon) : 1;
            return owned - controller.Lineup.EquippedCount(weapon);
        }

        bool Showing => controller.Phase == BattlePhase.Setup && controller.Lineup != null && !controller.RosterOpen;

        // ---------------------------------------------------------------- input

        void Update()
        {
            if (screen == null) return;
            if (!Showing)
            {
                drag = DragKind.None;
                GameUi.Show(screen, false);
                return;
            }

            GameUi.Show(screen, true);
            var lineup = controller.Lineup;
            if (selected != null && !Contains(lineup, selected)) selected = null;

            var keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard.enterKey.wasPressedThisFrame && controller.CanFight) controller.StartFight();
                if ((keyboard.deleteKey.wasPressedThisFrame || keyboard.backspaceKey.wasPressedThisFrame) && selected != null)
                {
                    lineup.Remove(selected);
                    selected = null;
                }
            }

            var mouse = Mouse.current;
            if (mouse != null && controller.Phase == BattlePhase.Setup) HandleMouse(mouse, lineup);
        }

        void HandleMouse(Mouse mouse, PlayerLineup lineup)
        {
            var mouseScreen = mouse.position.ReadValue();
            var under = GameUi.Under(document, mouseScreen);
            var camera = Camera.main;
            var hex = default(HexCoord);
            var overBoard = under == null && controller.Board.TryGetHex(camera, mouseScreen, out hex);

            if (mouse.leftButton.wasPressedThisFrame) Press(mouseScreen, under, overBoard, hex);

            if (drag == DragKind.None) return;
            if ((mouseScreen - pressPosition).magnitude > DragThreshold) dragMoved = true;

            if (drag == DragKind.MoveSlot && dragMoved && controller.Board.TryGetPoint(camera, mouseScreen, out var world))
                controller.ViewFor(dragSlot)?.Teleport(world + Vector3.up * 0.3f);

            if (overBoard && dragMoved) controller.Board.Highlight(hex, IsValidDrop(hex));
            else controller.Board.Highlight(null, false);

            if (!mouse.leftButton.wasReleasedThisFrame) return;
            Drop(overBoard, hex);
            drag = DragKind.None;
            controller.Board.Highlight(null, false);
            controller.RebuildPreview();
        }

        static bool Contains(PlayerLineup lineup, LineupSlot slot)
        {
            foreach (var s in lineup.Slots)
                if (s == slot) return true;
            return false;
        }

        void Press(Vector2 point, VisualElement under, bool overBoard, HexCoord hex)
        {
            var lineup = controller.Lineup;
            pressPosition = point;
            dragMoved = false;

            var ninja = GameUi.DataOf<NinjaDefinition>(under);
            if (ninja != null)
            {
                if (lineup.Contains(ninja))
                {
                    foreach (var slot in lineup.Slots)
                        if (slot.ninja == ninja) selected = slot;
                }
                else
                {
                    drag = DragKind.NewNinja;
                    dragNinja = ninja;
                }

                return;
            }

            var weapon = GameUi.DataOf<WeaponDefinition>(under);
            if (weapon != null)
            {
                if (FreeCount(weapon) <= 0)
                {
                    Say($"Every {weapon.DisplayName} you own is equipped. Remove one from a ninja first.");
                    return;
                }

                drag = DragKind.Weapon;
                dragWeapon = weapon;
                return;
            }

            if (!overBoard) return;

            var placed = controller.IsPlayerHex(hex, out var column, out var row) ? lineup.At(column, row) : null;
            if (placed != null)
            {
                drag = DragKind.MoveSlot;
                dragSlot = placed;
                selected = placed;
            }
            else
            {
                selected = null;
            }
        }

        bool IsValidDrop(HexCoord hex)
        {
            if (!controller.IsPlayerHex(hex, out var column, out var row)) return false;
            var lineup = controller.Lineup;
            switch (drag)
            {
                case DragKind.NewNinja:
                    return !lineup.IsFull || lineup.At(column, row) != null;
                case DragKind.MoveSlot:
                    return true;
                case DragKind.Weapon:
                    var slot = lineup.At(column, row);
                    return slot != null && slot.weapons.Count < LineupRules.MaxWeaponsPerNinja;
                default:
                    return false;
            }
        }

        void Drop(bool overBoard, HexCoord hex)
        {
            var lineup = controller.Lineup;
            var onPlayerHex = overBoard && controller.IsPlayerHex(hex, out _, out _);
            var column = hex.Column;
            var row = hex.Row;

            switch (drag)
            {
                case DragKind.NewNinja:
                {
                    if (!onPlayerHex)
                    {
                        if (overBoard) Say("Place ninjas on your half of the board, the near side.");
                        return;
                    }

                    var occupant = lineup.At(column, row);
                    if (occupant != null) lineup.Remove(occupant);
                    if (lineup.IsFull)
                    {
                        Say($"Your lineup is full ({lineup.MaxUnits}). Drag a ninja off the board first.");
                        return;
                    }

                    selected = lineup.Place(dragNinja, column, row);
                    break;
                }

                case DragKind.MoveSlot:
                    if (!dragMoved)
                    {
                        selected = dragSlot;
                    }
                    else if (onPlayerHex)
                    {
                        lineup.Move(dragSlot, column, row);
                    }
                    else if (!overBoard)
                    {
                        lineup.Remove(dragSlot);
                        selected = null;
                    }

                    break;

                case DragKind.Weapon:
                {
                    var slot = onPlayerHex ? lineup.At(column, row) : null;
                    if (slot == null)
                    {
                        if (dragMoved) Say("Drop weapons onto one of your ninjas.");
                        return;
                    }

                    if (!lineup.Equip(slot, dragWeapon))
                        Say($"A ninja can hold {LineupRules.MaxWeaponsPerNinja} weapons.");
                    else
                        selected = slot;
                    break;
                }
            }
        }

        void Say(string text)
        {
            if (toastText == null) return;
            toastText.text = text;
            messageUntil = Time.unscaledTime + MessageSeconds;
        }

        // ---------------------------------------------------------------- drawing

        void LateUpdate()
        {
            if (screen == null || !Showing) return;

            var state = State();
            if (state != shownState)
            {
                shownState = state;
                RefreshTopBar();
                RefreshStage();
                RefreshInfo();
                RefreshBench();
            }

            PlaceNames();
            GameUi.Show(toast, Time.unscaledTime < messageUntil);
            RefreshPointer();
        }

        /// <summary>Everything the panels show, as text, so they are only rebuilt when something changed.</summary>
        string State()
        {
            var text = new StringBuilder();
            var lineup = controller.Lineup;
            text.Append(controller.Stage != null ? controller.Stage.Id : "-").Append('|').Append(lineup.MaxUnits).Append('|');
            foreach (var slot in lineup.Slots)
            {
                text.Append(slot.ninja.Id).Append(slot == selected ? '*' : ' ').Append(slot.column).Append(',').Append(slot.rowFromBack);
                foreach (var weapon in slot.weapons) text.Append('+').Append(weapon.Id);
                text.Append(';');
            }

            text.Append('|').Append(roster.Count).Append('|').Append(armory.Count);
            var profile = controller.Profile;
            if (profile != null) text.Append('|').Append(WalletLine(profile));
            return text.ToString();
        }

        void RefreshTopBar()
        {
            var lineup = controller.Lineup;
            lineupTitle.text = $"Your lineup  {lineup.Slots.Count} / {lineup.MaxUnits}";
            fight.SetEnabled(controller.CanFight);
            clear.SetEnabled(lineup.Slots.Count > 0);
            GameUi.Show(toVillage, controller.CanLeaveToVillage);

            var profile = controller.Profile;
            GameUi.Show(rosterButton, profile != null);
            GameUi.Show(wallet, profile != null);
            if (profile != null) wallet.text = WalletLine(profile);
        }

        /// <summary>The clan level and the three balances, for the screens' top bars.</summary>
        public static string WalletLine(ProfileService profile)
        {
            var progression = profile.Progression;
            string Amount(ResourceDefinition resource) => resource == null ? "" :
                $"<color=#{PrototypeGui.Hex(resource.Color)}>{resource.DisplayName}</color> <b>{profile.Balance(resource)}</b>   ";
            return $"Clan <b>{profile.ClanLevel}</b> ({profile.ClanXp} / {profile.ClanXpToNext} XP)   " +
                   Amount(progression.Gold) + Amount(progression.Karma) + Amount(progression.Steel);
        }

        void RefreshStage()
        {
            stagePanel.Clear();
            var stage = controller.Stage;
            var campaign = controller.Campaign;
            if (stage == null || campaign == null)
            {
                stagePanel.Add(GameUi.Label("There are no campaign stages. Run Ninja Realm > Content > Create Prototype Content.", "note"));
                return;
            }

            var profile = controller.Profile;
            var chapter = campaign.ChapterOf(stage);
            if (chapter != null) stagePanel.Add(GameUi.Label($"Chapter {chapter.Number}: {chapter.DisplayName}", "small"));
            stagePanel.Add(GameUi.Label($"Stage {campaign.LabelOf(stage)}{KindText(stage.Kind)}", "title"));

            // Step through the stages that can be fought.
            var previous = campaign.Previous(stage);
            var next = campaign.Next(stage);
            var nav = GameUi.Element("stage-nav");
            var back = GameUi.Button("< Previous", () => controller.SelectStage(previous), "stage-nav__first");
            back.SetEnabled(controller.IsUnlocked(previous));
            nav.Add(back);
            var nextOpen = controller.IsUnlocked(next);
            var forward = GameUi.Button(next != null && !nextOpen ? "Next (locked)" : "Next >", () => controller.SelectStage(next));
            forward.SetEnabled(nextOpen);
            nav.Add(forward);
            stagePanel.Add(nav);

            var opponent = stage.Lineup;
            var card = GameUi.Element("card", "opponent");
            var top = GameUi.Element("opponent__top");
            top.Add(GameUi.Label(opponent != null ? opponent.DisplayName : "Nobody", "strong"));
            top.Add(GameUi.Label($"Lv {stage.Level}", "small"));
            card.Add(top);
            var bottom = GameUi.Element("opponent__bottom");
            var stars = profile != null ? profile.StarsOf(stage) : 0;
            bottom.Add(GameUi.Pips(stars, StageStars.Max));
            bottom.Add(GameUi.Label(stars > 0 ? $"Best: {stars} of {StageStars.Max} stars" : "Not cleared yet", "small"));
            card.Add(bottom);
            stagePanel.Add(card);

            if (opponent == null) return;
            stagePanel.Add(GameUi.Label(opponent.Description, "side-line"));

            if (profile != null)
            {
                var progression = profile.Progression;
                progression.FightPay(stage.RewardTier, true, out var gold, out var steel, out var xp);
                var pay = $"<b>Win:</b> {gold} Gold, {steel} Steel, {xp} clan XP, {progression.DropChance(stage.RewardTier) / 100}% weapon drop.";
                if (!profile.HasCleared(stage))
                    pay += $" <color=#{PrototypeGui.Hex(PrototypeGui.Gold)}>First clear: {FirstClearText(stage, progression)}.</color>";
                stagePanel.Add(GameUi.Label(pay, "side-line"));
            }

            stagePanel.Add(GameUi.Label("Stars: 1 for the win, 2 with half your team standing, 3 with nobody fallen.", "side-line", "muted"));
            AddTiles(stage);
            stagePanel.Add(GameUi.Label("Their synergies", "side-title"));
            AddSynergies(stagePanel, LineupSynergies.Count(opponent.Slots));
        }

        /// <summary>The stage's special tiles, once each, and which half they are on.</summary>
        void AddTiles(StageDefinition stage)
        {
            if (stage.Tiles.Count == 0) return;
            stagePanel.Add(GameUi.Label("Tiles", "side-title"));
            var seen = new List<TileDefinition>();
            foreach (var entry in stage.Tiles)
            {
                if (entry?.tile == null || seen.Contains(entry.tile)) continue;
                seen.Add(entry.tile);
                int mine = 0, theirs = 0;
                foreach (var other in stage.Tiles)
                    if (other?.tile == entry.tile)
                    {
                        if (other.team == 0) mine++;
                        else theirs++;
                    }

                var where = mine > 0 && theirs > 0 ? "both halves" : mine > 0 ? "your half" : "their half";
                var count = mine + theirs > 1 ? $" x{mine + theirs}" : "";
                stagePanel.Add(GameUi.Label(
                    $"<b><color=#{PrototypeGui.Hex(entry.tile.Color)}>{entry.tile.DisplayName}</color></b>{count} <color=#9ca3b0>({where})</color>  {entry.tile.DescribeAll()}",
                    "side-line"));
            }

            stagePanel.Add(GameUi.Label("A ninja that starts on a tile keeps its effect all fight.", "side-line", "muted"));
        }

        /// <summary>" Elite" or " Boss" in colour, or nothing for a normal stage.</summary>
        public static string KindText(StageKind kind) => kind switch
        {
            StageKind.Elite => "  <color=#c9a0ff>Elite</color>",
            StageKind.Boss => "  <color=#ff8066>Boss</color>",
            _ => "",
        };

        /// <summary>What a stage's first clear adds, such as "+200 Gold, a weapon, +20 Karma and Mei".</summary>
        public static string FirstClearText(StageDefinition stage, ProgressionDefinition progression)
        {
            var parts = new List<string> { $"+{progression.FirstWinGold(stage.RewardTier)} Gold", "a weapon" };
            if (stage.FirstClearKarma > 0) parts.Add($"+{stage.FirstClearKarma} Karma");
            if (stage.FirstClearSteel > 0) parts.Add($"+{stage.FirstClearSteel} Steel");
            if (stage.FirstClearNinja != null) parts.Add(stage.FirstClearNinja.DisplayName);
            return string.Join(", ", parts.GetRange(0, parts.Count - 1)) + " and " + parts[parts.Count - 1];
        }

        void RefreshInfo()
        {
            infoPanel.Clear();
            infoPanel.Add(GameUi.Label("Your synergies", "side-title"));
            AddSynergies(infoPanel, controller.Lineup.Synergies());
            infoPanel.Add(GameUi.Element("divider"));

            if (selected == null)
            {
                infoPanel.Add(GameUi.Label("Click one of your ninjas to see its stats and weapons.", "note"));
                return;
            }

            AddNinja(selected);
        }

        static void AddSynergies(VisualElement parent, List<TraitCount> traits)
        {
            if (traits.Count == 0)
            {
                parent.Add(GameUi.Label("None yet.", "small"));
                return;
            }

            foreach (var trait in traits)
            {
                var active = trait.ActiveTier;
                var thresholds = new StringBuilder();
                foreach (var tier in trait.Trait.Tiers)
                {
                    if (tier == null) continue;
                    if (thresholds.Length > 0) thresholds.Append(" / ");
                    thresholds.Append(trait.Count >= tier.requiredCount ? $"<b>{tier.requiredCount}</b>" : tier.requiredCount.ToString());
                }

                var row = GameUi.Element("synergy");
                row.EnableInClassList("synergy--inactive", active == null);
                var top = GameUi.Element("synergy__top");
                var dot = GameUi.Element("synergy__dot");
                dot.style.backgroundColor = active != null ? trait.Trait.Color : new Color(0.55f, 0.57f, 0.6f);
                top.Add(dot);
                top.Add(GameUi.Label($"<b>{trait.Trait.DisplayName}</b>  {trait.Count}   ({thresholds})", "synergy__name"));
                row.Add(top);
                if (active != null && !string.IsNullOrEmpty(active.description)) row.Add(GameUi.Label(active.description, "synergy__description"));
                parent.Add(row);
            }
        }

        void AddNinja(LineupSlot slot)
        {
            var ninja = slot.ninja;
            infoPanel.Add(RosterViews.NameLine(ninja));
            var rank = controller.RankOf(ninja);
            var profile = controller.Profile;
            var multiplier = profile != null ? profile.Progression.StatMultiplier(rank.Level, rank.Stars) : ProgressionDefinition.BasisPoints;
            if (profile != null) infoPanel.Add(GameUi.Label($"{RankText(rank)}  {profile.Progression.Belt(rank.Level)} belt", "small"));
            infoPanel.Add(GameUi.Label(RosterViews.ElementAndClass(ninja, true), "small"));

            var stats = GameUi.Element("stats");
            foreach (var stat in shownStats)
            {
                var value = ninja.GetBaseStat(stat);
                if (stat.Id == StatIds.Health || stat.Id == StatIds.AttackDamage) value = value * multiplier / ProgressionDefinition.BasisPoints;
                stats.Add(GameUi.Label($"<color=#9ca3b0>{stat.DisplayName}</color>  <b>{value:0.##}</b>", "stat"));
            }

            infoPanel.Add(stats);

            var tile = BoardTile.At(controller.Tiles, 0, slot.column, slot.rowFromBack);
            if (tile != null)
            {
                var effect = tile.DescribeFor(ninja);
                infoPanel.Add(GameUi.Label(
                    $"<b>On <color=#{PrototypeGui.Hex(tile.Color)}>{tile.DisplayName}</color></b>: {effect ?? "no effect on this ninja."}", "side-line"));
            }

            if (ninja.Skill != null)
            {
                infoPanel.Add(GameUi.Label(ninja.Skill.DisplayName, "skill-name"));
                infoPanel.Add(GameUi.Label(ninja.Skill.Description, "side-line", "muted"));
            }

            infoPanel.Add(GameUi.Label($"<b>Weapons</b>  {slot.weapons.Count} / {LineupRules.MaxWeaponsPerNinja}"));
            for (var i = 0; i < slot.weapons.Count; i++)
            {
                var index = i;
                var line = GameUi.Element("weapon-line");
                line.Add(GameUi.Label(slot.weapons[i].DisplayName, "small"));
                line.Add(GameUi.Button("x", () => controller.Lineup.Unequip(slot, index), "icon-button"));
                infoPanel.Add(line);
            }

            if (slot.weapons.Count == 0) infoPanel.Add(GameUi.Label("Drag a weapon from the armory onto this ninja.", "small", "wrap"));

            infoPanel.Add(GameUi.Button("Remove from lineup (Delete)", () =>
            {
                controller.Lineup.Remove(slot);
                selected = null;
            }, "remove"));
        }

        void RefreshBench()
        {
            var lineup = controller.Lineup;
            var profile = controller.Profile;
            ninjaCards.Clear();
            foreach (var ninja in roster)
            {
                var placed = lineup.Contains(ninja);
                var card = GameUi.Element("card", "bench-card", "bench-card--ninja");
                card.userData = ninja;
                card.EnableInClassList("bench-card--used", placed);
                card.EnableInClassList("bench-card--selected", selected != null && selected.ninja == ninja);
                card.style.borderLeftColor = RosterViews.ElementColor(ninja);
                card.Add(GameUi.Label(ninja.DisplayName, "bench-card__name"));
                card.Add(GameUi.Label(RosterViews.ElementAndClass(ninja), "small"));
                var detail = profile != null ? RankText(controller.RankOf(ninja)) : $"<color=#{PrototypeGui.Hex(RarityColor(ninja.Rarity))}>{ninja.Rarity}</color>";
                card.Add(GameUi.Label(placed ? "On the board" : detail, "small"));
                ninjaCards.Add(card);
            }

            armoryTitle.text = profile != null
                ? "<b>Armory</b>  the weapons you own, drag onto a ninja"
                : "<b>Armory</b>  one of each weapon, drag onto a ninja";
            weaponCards.Clear();
            foreach (var weapon in armory)
            {
                var free = FreeCount(weapon);
                var combined = weapon.Tier == WeaponTier.Combined;
                var card = GameUi.Element("card", "bench-card", "bench-card--weapon");
                card.userData = weapon;
                card.EnableInClassList("bench-card--used", free <= 0);
                card.EnableInClassList("bench-card--combined", combined && free > 0);
                card.Add(GameUi.Label(weapon.DisplayName, "bench-card__name"));
                var kind = combined ? "Combined" : "Component";
                card.Add(GameUi.Label(profile != null ? $"{kind}, {Mathf.Max(0, free)} free" : free <= 0 ? "Equipped" : kind, "small"));
                weaponCards.Add(card);
            }
        }

        /// <summary>A name over each ninja on the board.</summary>
        void PlaceNames()
        {
            names.Begin();
            var camera = Camera.main;
            foreach (var view in controller.PreviewViews)
                if (view != null) names.Place(view.BarsPosition, camera, view.DisplayName);
            names.End();
        }

        /// <summary>The tooltip over a hovered card and the label that follows a dragged ninja or weapon.</summary>
        void RefreshPointer()
        {
            var mouse = Mouse.current;
            var panel = screen.panel;
            if (mouse == null || panel == null)
            {
                GameUi.Show(tooltip, false);
                GameUi.Show(ghost, false);
                return;
            }

            var mouseScreen = mouse.position.ReadValue();
            var point = GameUi.ScreenToPanel(panel, mouseScreen);
            var dragging = dragMoved && (drag == DragKind.NewNinja || drag == DragKind.Weapon);
            GameUi.Show(ghost, dragging);
            if (dragging)
            {
                ghost.text = drag == DragKind.NewNinja ? dragNinja.DisplayName : dragWeapon.DisplayName;
                ghost.style.left = point.x + 12f;
                ghost.style.top = point.y + 8f;
            }

            string tip = null;
            var underPointer = GameUi.Under(document, mouseScreen);
            if (drag == DragKind.None)
            {
                var ninja = GameUi.DataOf<NinjaDefinition>(underPointer);
                var weapon = GameUi.DataOf<WeaponDefinition>(underPointer);
                if (ninja != null && ninja.Skill != null) tip = $"<b>{ninja.DisplayName}</b>: {ninja.Skill.DisplayName}. {ninja.Skill.Description}";
                else if (weapon != null) tip = $"<b>{weapon.DisplayName}</b>: {weapon.Description}";
            }

            // A special tile under the pointer: what it does, or what it would do to the ninja being dragged.
            if (tip == null && underPointer == null && controller.Phase == BattlePhase.Setup &&
                controller.Board.TryGetHex(Camera.main, mouseScreen, out var hex))
            {
                var tile = controller.TileAt(hex, out var half);
                var moving = !dragMoved ? null : drag == DragKind.NewNinja ? dragNinja : drag == DragKind.MoveSlot ? dragSlot?.ninja : null;
                if (tile != null) tip = TileTip(tile, half, moving);
            }

            GameUi.Show(tooltip, tip != null);
            if (tip == null) return;
            if (tooltip.text != tip) tooltip.text = tip;
            var width = screen.layout.width;
            tooltip.style.left = Mathf.Clamp(point.x - TooltipWidth / 2f, 10f, Mathf.Max(10f, width - TooltipWidth - 10f));
        }

        /// <summary>
        /// A tile's tooltip: its name and every part, or while a ninja is dragged over it, what it
        /// would do to that ninja.
        /// </summary>
        static string TileTip(TileDefinition tile, int half, NinjaDefinition ninja)
        {
            var name = $"<b><color=#{PrototypeGui.Hex(tile.Color)}>{tile.DisplayName}</color></b>";
            if (half == 1) return $"{name} (enemy half): {tile.DescribeAll()}";
            if (ninja == null) return $"{name}: {tile.DescribeAll()}. <color=#9ca3b0>{tile.Description}</color>";
            var effect = tile.DescribeFor(ninja);
            return effect != null ? $"<b>{ninja.DisplayName}</b> on {name}: {effect}" : $"<b>{ninja.DisplayName}</b> on {name}: no effect.";
        }

        /// <summary>A ninja's level and stars, such as "Lv 5  ★★".</summary>
        public static string RankText(NinjaRank rank) =>
            $"Lv {rank.Level}  <color=#{PrototypeGui.Hex(PrototypeGui.Gold)}>{new string('★', rank.Stars)}</color>";

        public static Color RarityColor(Rarity rarity)
        {
            switch (rarity)
            {
                case Rarity.Rare: return new Color(0.35f, 0.65f, 1f);
                case Rarity.Epic: return new Color(0.75f, 0.45f, 1f);
                case Rarity.Legendary: return PrototypeGui.Gold;
                default: return new Color(0.8f, 0.8f, 0.8f);
            }
        }
    }
}
