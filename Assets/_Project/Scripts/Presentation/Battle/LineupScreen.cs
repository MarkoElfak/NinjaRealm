using System.Collections.Generic;
using System.Text;
using NinjaRealm.Combat;
using NinjaRealm.Core.Campaign;
using NinjaRealm.Core.Definitions;
using NinjaRealm.Core.Lineups;
using NinjaRealm.Core.Progression;
using UnityEngine;
using UnityEngine.InputSystem;

namespace NinjaRealm.Presentation.Battle
{
    /// <summary>
    /// Prototype lineup screen: drag ninjas from the bench onto your half of the board, drag
    /// them around or off the board, drag weapons onto them, step through the campaign stages
    /// and start the fight. The bench and armory hold what the player owns. Drawn with IMGUI; input is read
    /// from the Input System.
    /// </summary>
    [RequireComponent(typeof(BattleController))]
    public sealed class LineupScreen : MonoBehaviour
    {
        const float DragThreshold = 6f;

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
        string message;
        float messageUntil;

        // Layout, in scaled GUI units. Rebuilt every frame from the screen size.
        Rect leftPanel, rightPanel, topBar, bottomPanel;
        readonly List<Rect> rosterCards = new List<Rect>();
        readonly List<Rect> armoryCards = new List<Rect>();

        void Awake()
        {
            controller = GetComponent<BattleController>();
        }

        void Start()
        {
            if (!controller.Initialize()) return;
            var database = controller.Content;

            foreach (var id in new[] { StatIds.Health, StatIds.AttackDamage, StatIds.AttackSpeed, StatIds.Range, StatIds.Armor, StatIds.MagicResist, StatIds.MaxMana })
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

        void Layout()
        {
            var width = PrototypeGui.Width;
            var height = PrototypeGui.Height;
            leftPanel = new Rect(10f, 10f, 250f, height - 210f);
            rightPanel = new Rect(width - 280f, 10f, 270f, height - 210f);
            topBar = new Rect(width / 2f - 290f, 10f, 580f, controller.Profile != null ? 100f : 74f);
            bottomPanel = new Rect(10f, height - 190f, width - 20f, 180f);

            rosterCards.Clear();
            var cardWidth = Mathf.Min(150f, (bottomPanel.width - 20f) / Mathf.Max(1, roster.Count) - 6f);
            for (var i = 0; i < roster.Count; i++)
                rosterCards.Add(new Rect(bottomPanel.x + 10f + i * (cardWidth + 6f), bottomPanel.y + 30f, cardWidth, 56f));

            armoryCards.Clear();
            var weaponWidth = Mathf.Min(150f, (bottomPanel.width - 20f) / Mathf.Max(1, armory.Count) - 6f);
            for (var i = 0; i < armory.Count; i++)
                armoryCards.Add(new Rect(bottomPanel.x + 10f + i * (weaponWidth + 6f), bottomPanel.y + 122f, weaponWidth, 46f));
        }

        bool OverPanel(Vector2 point) =>
            leftPanel.Contains(point) || rightPanel.Contains(point) || topBar.Contains(point) || bottomPanel.Contains(point);

        static int IndexAt(List<Rect> rects, Vector2 point)
        {
            for (var i = 0; i < rects.Count; i++)
                if (rects[i].Contains(point)) return i;
            return -1;
        }

        // ---------------------------------------------------------------- input

        void Update()
        {
            if (controller.Phase != BattlePhase.Setup || controller.Lineup == null || controller.RosterOpen)
            {
                drag = DragKind.None;
                return;
            }

            Layout();
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
            if (mouse == null) return;

            var point = PrototypeGui.MouseGuiPosition;
            var camera = Camera.main;
            var hex = default(HexCoord);
            var overBoard = !OverPanel(point) && controller.Board.TryGetHex(camera, PrototypeGui.MouseScreenPosition, out hex);

            if (mouse.leftButton.wasPressedThisFrame) Press(point, overBoard, hex);

            if (drag != DragKind.None)
            {
                if ((point - pressPosition).magnitude > DragThreshold) dragMoved = true;

                if (drag == DragKind.MoveSlot && dragMoved && controller.Board.TryGetPoint(camera, PrototypeGui.MouseScreenPosition, out var world))
                    controller.ViewFor(dragSlot)?.Teleport(world + Vector3.up * 0.3f);

                if (overBoard && dragMoved) controller.Board.Highlight(hex, IsValidDrop(hex));
                else controller.Board.Highlight(null, false);

                if (mouse.leftButton.wasReleasedThisFrame)
                {
                    Drop(point, overBoard, hex);
                    drag = DragKind.None;
                    controller.Board.Highlight(null, false);
                    controller.RebuildPreview();
                }
            }
        }

        static bool Contains(PlayerLineup lineup, LineupSlot slot)
        {
            foreach (var s in lineup.Slots)
                if (s == slot) return true;
            return false;
        }

        void Press(Vector2 point, bool overBoard, HexCoord hex)
        {
            var lineup = controller.Lineup;
            pressPosition = point;
            dragMoved = false;

            var card = IndexAt(rosterCards, point);
            if (card >= 0)
            {
                var ninja = roster[card];
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

            var weaponCard = IndexAt(armoryCards, point);
            if (weaponCard >= 0)
            {
                var weapon = armory[weaponCard];
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

        void Drop(Vector2 point, bool overBoard, HexCoord hex)
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
            message = text;
            messageUntil = Time.unscaledTime + 3f;
        }

        // ---------------------------------------------------------------- drawing

        void OnGUI()
        {
            if (controller == null || controller.Phase != BattlePhase.Setup || controller.Lineup == null || controller.RosterOpen) return;

            PrototypeGui.Begin();
            Layout();

            DrawNames();
            DrawStage();
            DrawRight();
            DrawTopBar();
            DrawBottom();
            DrawDragGhost();

            if (!string.IsNullOrEmpty(message) && Time.unscaledTime < messageUntil)
            {
                var rect = new Rect(PrototypeGui.Width / 2f - 260f, bottomPanel.y - 44f, 520f, 32f);
                PrototypeGui.Panel(rect);
                GUI.Label(rect, message, PrototypeGui.Centered);
            }
        }

        void DrawNames()
        {
            var camera = Camera.main;
            if (camera == null) return;
            var scale = PrototypeGui.Scale;
            foreach (var view in controller.PreviewViews)
            {
                if (view == null) continue;
                var screen = camera.WorldToScreenPoint(view.BarsPosition);
                if (screen.z <= 0f) continue;
                var point = new Vector2(screen.x / scale, (Screen.height - screen.y) / scale);
                GUI.Label(new Rect(point.x - 60f, point.y - 30f, 120f, 18f), $"<b>{view.DisplayName}</b>", PrototypeGui.NameStyle);
            }
        }

        void DrawTopBar()
        {
            var lineup = controller.Lineup;
            PrototypeGui.Panel(topBar);
            GUI.Label(new Rect(topBar.x + 14f, topBar.y + 8f, 220f, 24f), $"Your lineup  {lineup.Slots.Count} / {lineup.MaxUnits}", PrototypeGui.Title);
            GUI.Label(new Rect(topBar.x + 14f, topBar.y + 34f, 290f, 36f),
                "Drag ninjas onto the near half. Drag one off the board to remove it.", PrototypeGui.Wrap);

            // The way back to the village, next to Fight so it is easy to find.
            if (controller.CanLeaveToVillage && GUI.Button(new Rect(topBar.xMax - 262f, topBar.y + 10f, 124f, 34f), "Back to village")) controller.LeaveToVillage();

            GUI.enabled = controller.CanFight;
            if (GUI.Button(new Rect(topBar.xMax - 130f, topBar.y + 10f, 118f, 34f), "Fight! (Enter)")) controller.StartFight();
            GUI.enabled = lineup.Slots.Count > 0;
            if (GUI.Button(new Rect(topBar.xMax - 130f, topBar.y + 48f, 118f, 20f), "Clear lineup"))
            {
                lineup.Clear();
                selected = null;
            }

            GUI.enabled = true;

            var profile = controller.Profile;
            if (profile == null) return;

            if (GUI.Button(new Rect(topBar.xMax - 130f, topBar.y + 72f, 118f, 22f), "Roster (Tab)")) controller.RosterOpen = true;
            GUI.Label(new Rect(topBar.x + 14f, topBar.y + 74f, topBar.width - 160f, 20f), WalletLine(profile), PrototypeGui.Small);
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

        void DrawStage()
        {
            PrototypeGui.Panel(leftPanel);
            var x = leftPanel.x + 12f;
            var y = leftPanel.y + 10f;
            var width = leftPanel.width - 24f;

            var stage = controller.Stage;
            var campaign = controller.Campaign;
            if (stage == null || campaign == null)
            {
                GUI.Label(new Rect(x, y, width, 60f), "There are no campaign stages. Run Ninja Realm > Content > Create Prototype Content.", PrototypeGui.Wrap);
                return;
            }

            var profile = controller.Profile;
            var chapter = campaign.ChapterOf(stage);
            if (chapter != null)
            {
                GUI.Label(new Rect(x, y, width, 18f), $"<color=#9aa3b0>Chapter {chapter.Number}: {chapter.DisplayName}</color>", PrototypeGui.Small);
                y += 20f;
            }

            GUI.Label(new Rect(x, y, width, 22f), $"Stage {campaign.LabelOf(stage)}{KindText(stage.Kind)}", PrototypeGui.Title);
            y += 28f;

            // Step through the stages that can be fought.
            var previous = campaign.Previous(stage);
            var next = campaign.Next(stage);
            var half = (width - 6f) / 2f;
            GUI.enabled = controller.IsUnlocked(previous);
            if (GUI.Button(new Rect(x, y, half, 24f), "< Previous")) controller.SelectStage(previous);
            GUI.enabled = controller.IsUnlocked(next);
            if (GUI.Button(new Rect(x + half + 6f, y, half, 24f), next != null && !GUI.enabled ? "Next (locked)" : "Next >")) controller.SelectStage(next);
            GUI.enabled = true;
            y += 32f;

            var opponent = stage.Lineup;
            PrototypeGui.Fill(new Rect(x, y, width, 46f), PrototypeGui.CardColor, true);
            GUI.Label(new Rect(x + 8f, y + 3f, width - 60f, 20f), $"<b>{(opponent != null ? opponent.DisplayName : "Nobody")}</b>", PrototypeGui.Label);
            GUI.Label(new Rect(x + width - 52f, y + 5f, 46f, 16f), $"Lv {stage.Level}", PrototypeGui.Small);
            var stars = profile != null ? profile.StarsOf(stage) : 0;
            PrototypeGui.Pips(new Vector2(x + 9f, y + 29f), stars, StageStars.Max, PrototypeGui.Gold);
            GUI.Label(new Rect(x + 60f, y + 24f, width - 68f, 16f), stars > 0 ? $"Best: {stars} of {StageStars.Max} stars" : "Not cleared yet", PrototypeGui.Small);
            y += 54f;

            if (opponent == null) return;

            var description = opponent.Description;
            var descriptionHeight = PrototypeGui.Wrap.CalcHeight(new GUIContent(description), width);
            GUI.Label(new Rect(x, y, width, descriptionHeight), description, PrototypeGui.Wrap);
            y += descriptionHeight + 6f;

            if (profile != null)
            {
                var progression = profile.Progression;
                progression.FightPay(stage.RewardTier, true, out var gold, out var steel, out var xp);
                var pay = $"<b>Win:</b> {gold} Gold, {steel} Steel, {xp} clan XP, {progression.DropChance(stage.RewardTier) / 100}% weapon drop.";
                if (!profile.HasCleared(stage))
                    pay += $" <color=#{PrototypeGui.Hex(PrototypeGui.Gold)}>First clear: {FirstClearText(stage, progression)}.</color>";
                var payHeight = PrototypeGui.Wrap.CalcHeight(new GUIContent(pay), width);
                GUI.Label(new Rect(x, y, width, payHeight), pay, PrototypeGui.Wrap);
                y += payHeight + 6f;
            }

            const string starRules = "<color=#9aa3b0>Stars: 1 for the win, 2 with half your team standing, 3 with nobody fallen.</color>";
            var rulesHeight = PrototypeGui.Wrap.CalcHeight(new GUIContent(starRules), width);
            GUI.Label(new Rect(x, y, width, rulesHeight), starRules, PrototypeGui.Wrap);
            y += rulesHeight + 10f;

            GUI.Label(new Rect(x, y, width, 22f), "Their synergies", PrototypeGui.Title);
            y += 26f;
            DrawSynergies(LineupSynergies.Count(opponent.Slots), x, y, width, leftPanel.yMax - 8f);
        }

        /// <summary>" (Elite)" or " (Boss)" in colour, or nothing for a normal stage.</summary>
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

        void DrawRight()
        {
            PrototypeGui.Panel(rightPanel);
            var x = rightPanel.x + 12f;
            var y = rightPanel.y + 10f;
            var width = rightPanel.width - 24f;

            GUI.Label(new Rect(x, y, width, 22f), "Your synergies", PrototypeGui.Title);
            y += 26f;
            y = DrawSynergies(controller.Lineup.Synergies(), x, y, width, rightPanel.y + 300f);

            y = Mathf.Max(y, rightPanel.y + 230f) + 8f;
            PrototypeGui.Fill(new Rect(x, y, width, 1f), PrototypeGui.BorderColor);
            y += 10f;

            if (selected == null)
            {
                GUI.Label(new Rect(x, y, width, 60f), "Click one of your ninjas to see its stats and weapons.", PrototypeGui.Wrap);
                return;
            }

            DrawNinja(selected, x, y, width);
        }

        float DrawSynergies(List<TraitCount> traits, float x, float y, float width, float maxY)
        {
            if (traits.Count == 0)
            {
                GUI.Label(new Rect(x, y, width, 20f), "None yet.", PrototypeGui.Small);
                return y + 22f;
            }

            foreach (var trait in traits)
            {
                if (y > maxY - 20f) break;

                var active = trait.ActiveTier;
                var thresholds = new StringBuilder();
                foreach (var tier in trait.Trait.Tiers)
                {
                    if (tier == null) continue;
                    if (thresholds.Length > 0) thresholds.Append(" / ");
                    var reached = trait.Count >= tier.requiredCount;
                    thresholds.Append(reached ? $"<b>{tier.requiredCount}</b>" : tier.requiredCount.ToString());
                }

                var color = active != null ? trait.Trait.Color : new Color(0.55f, 0.57f, 0.6f);
                PrototypeGui.Fill(new Rect(x, y + 3f, 12f, 12f), color);
                GUI.Label(new Rect(x + 18f, y, width - 18f, 20f),
                    $"<color=#{PrototypeGui.Hex(active != null ? Color.white : new Color(0.65f, 0.67f, 0.7f))}><b>{trait.Trait.DisplayName}</b>  {trait.Count}   ({thresholds})</color>",
                    PrototypeGui.Label);
                y += 20f;

                if (active != null && !string.IsNullOrEmpty(active.description))
                {
                    var height = PrototypeGui.Wrap.CalcHeight(new GUIContent(active.description), width - 18f);
                    GUI.Label(new Rect(x + 18f, y, width - 18f, height), active.description, PrototypeGui.Wrap);
                    y += height + 4f;
                }
            }

            return y;
        }

        void DrawNinja(LineupSlot slot, float x, float y, float width)
        {
            var ninja = slot.ninja;
            GUI.Label(new Rect(x, y, width, 22f), $"<b>{ninja.DisplayName}</b>  <color=#{PrototypeGui.Hex(RarityColor(ninja.Rarity))}>{ninja.Rarity}</color>", PrototypeGui.Title);
            y += 22f;
            var rank = controller.RankOf(ninja);
            var multiplier = controller.Profile != null ? controller.Profile.Progression.StatMultiplier(rank.Level, rank.Stars) : ProgressionDefinition.BasisPoints;
            if (controller.Profile != null)
            {
                GUI.Label(new Rect(x, y, width, 18f), $"{RankText(rank)}  {controller.Profile.Progression.Belt(rank.Level)} belt", PrototypeGui.Small);
                y += 18f;
            }
            GUI.Label(new Rect(x, y, width, 18f),
                $"<color=#{PrototypeGui.Hex(ninja.Element != null ? ninja.Element.Color : Color.grey)}>{ninja.Element?.DisplayName}</color>  {ninja.Class?.DisplayName}",
                PrototypeGui.Small);
            y += 22f;

            for (var i = 0; i < shownStats.Count; i++)
            {
                var stat = shownStats[i];
                var column = i % 2;
                var rect = new Rect(x + column * (width / 2f), y + (i / 2) * 17f, width / 2f, 17f);
                var value = ninja.GetBaseStat(stat);
                if (stat.Id == StatIds.Health || stat.Id == StatIds.AttackDamage) value = value * multiplier / ProgressionDefinition.BasisPoints;
                GUI.Label(rect, $"{stat.DisplayName}: <b>{value:0.##}</b>", PrototypeGui.Small);
            }

            y += ((shownStats.Count + 1) / 2) * 17f + 6f;

            if (ninja.Skill != null)
            {
                GUI.Label(new Rect(x, y, width, 18f), $"<b>{ninja.Skill.DisplayName}</b>", PrototypeGui.Label);
                y += 18f;
                var height = PrototypeGui.Wrap.CalcHeight(new GUIContent(ninja.Skill.Description), width);
                GUI.Label(new Rect(x, y, width, height), ninja.Skill.Description, PrototypeGui.Wrap);
                y += height + 6f;
            }

            GUI.Label(new Rect(x, y, width, 18f), $"<b>Weapons</b>  {slot.weapons.Count} / {LineupRules.MaxWeaponsPerNinja}", PrototypeGui.Label);
            y += 20f;
            for (var i = 0; i < slot.weapons.Count; i++)
            {
                var weapon = slot.weapons[i];
                GUI.Label(new Rect(x, y, width - 28f, 18f), weapon.DisplayName, PrototypeGui.Small);
                if (GUI.Button(new Rect(x + width - 24f, y, 22f, 18f), "x"))
                {
                    controller.Lineup.Unequip(slot, i);
                    break;
                }

                y += 20f;
            }

            if (slot.weapons.Count == 0)
            {
                GUI.Label(new Rect(x, y, width, 18f), "Drag a weapon from the armory onto this ninja.", PrototypeGui.Small);
                y += 20f;
            }

            if (GUI.Button(new Rect(x, Mathf.Min(y + 6f, rightPanel.yMax - 30f), width, 22f), "Remove from lineup (Delete)"))
            {
                controller.Lineup.Remove(slot);
                selected = null;
            }
        }

        void DrawBottom()
        {
            var lineup = controller.Lineup;
            PrototypeGui.Panel(bottomPanel);
            GUI.Label(new Rect(bottomPanel.x + 12f, bottomPanel.y + 6f, 600f, 20f), "<b>Ninjas</b>  drag onto your half", PrototypeGui.Label);
            GUI.Label(new Rect(bottomPanel.x + 12f, bottomPanel.y + 98f, 600f, 20f), controller.Profile != null ? "<b>Armory</b>  the weapons you own, drag onto a ninja" : "<b>Armory</b>  one of each weapon, drag onto a ninja", PrototypeGui.Label);

            var point = PrototypeGui.MouseGuiPosition;
            string tooltip = null;

            for (var i = 0; i < roster.Count && i < rosterCards.Count; i++)
            {
                var ninja = roster[i];
                var rect = rosterCards[i];
                var placed = lineup.Contains(ninja);
                PrototypeGui.Fill(rect, placed ? PrototypeGui.DisabledColor : PrototypeGui.CardColor, true);
                PrototypeGui.Fill(new Rect(rect.x, rect.y, 4f, rect.height), ninja.Element != null ? ninja.Element.Color : Color.grey);
                GUI.Label(new Rect(rect.x + 10f, rect.y + 4f, rect.width - 12f, 20f),
                    $"<b>{ninja.DisplayName}</b>", PrototypeGui.Label);
                GUI.Label(new Rect(rect.x + 10f, rect.y + 22f, rect.width - 12f, 16f), $"{ninja.Element?.DisplayName} {ninja.Class?.DisplayName}", PrototypeGui.Small);
                var detail = controller.Profile != null ? RankText(controller.RankOf(ninja)) : $"<color=#{PrototypeGui.Hex(RarityColor(ninja.Rarity))}>{ninja.Rarity}</color>";
                GUI.Label(new Rect(rect.x + 10f, rect.y + 37f, rect.width - 12f, 16f), placed ? "On the board" : detail, PrototypeGui.Small);

                if (rect.Contains(point) && ninja.Skill != null)
                    tooltip = $"<b>{ninja.DisplayName}</b>: {ninja.Skill.DisplayName}. {ninja.Skill.Description}";
            }

            for (var i = 0; i < armory.Count && i < armoryCards.Count; i++)
            {
                var weapon = armory[i];
                var rect = armoryCards[i];
                var free = FreeCount(weapon);
                var used = free <= 0;
                var combined = weapon.Tier == WeaponTier.Combined;
                PrototypeGui.Fill(rect, used ? PrototypeGui.DisabledColor : combined ? new Color(0.26f, 0.22f, 0.14f, 0.95f) : PrototypeGui.CardColor, true);
                GUI.Label(new Rect(rect.x + 8f, rect.y + 4f, rect.width - 10f, 20f), $"<b>{weapon.DisplayName}</b>", PrototypeGui.Label);
                var kind = combined ? "Combined" : "Component";
                var state = controller.Profile != null ? $"{kind}, {Mathf.Max(0, free)} free" : used ? "Equipped" : kind;
                GUI.Label(new Rect(rect.x + 8f, rect.y + 23f, rect.width - 10f, 16f), state, PrototypeGui.Small);

                if (rect.Contains(point))
                    tooltip = $"<b>{weapon.DisplayName}</b>: {weapon.Description}";
            }

            if (tooltip != null && drag == DragKind.None)
            {
                var width = 420f;
                var height = PrototypeGui.Wrap.CalcHeight(new GUIContent(tooltip), width - 16f) + 12f;
                var rect = new Rect(Mathf.Clamp(point.x - width / 2f, 10f, PrototypeGui.Width - width - 10f), bottomPanel.y - height - 6f, width, height);
                PrototypeGui.Panel(rect);
                GUI.Label(new Rect(rect.x + 8f, rect.y + 6f, width - 16f, height - 12f), tooltip, PrototypeGui.Wrap);
            }
        }

        void DrawDragGhost()
        {
            if (!dragMoved || (drag != DragKind.NewNinja && drag != DragKind.Weapon)) return;

            var point = PrototypeGui.MouseGuiPosition;
            var text = drag == DragKind.NewNinja ? dragNinja.DisplayName : dragWeapon.DisplayName;
            var rect = new Rect(point.x + 12f, point.y + 8f, 130f, 26f);
            PrototypeGui.Fill(rect, new Color(0.15f, 0.17f, 0.22f, 0.9f), true);
            GUI.Label(rect, text, PrototypeGui.Centered);
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
