using System.Collections.Generic;
using System.Text;
using NinjaRealm.Combat;
using NinjaRealm.Core.Definitions;
using NinjaRealm.Core.Lineups;
using UnityEngine;
using UnityEngine.InputSystem;

namespace NinjaRealm.Presentation.Battle
{
    /// <summary>
    /// Prototype lineup screen: drag ninjas from the bench onto your half of the board, drag
    /// them around or off the board, drag weapons onto them, pick an opponent and start the
    /// fight. Drawn with IMGUI; input is read from the Input System.
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
            var database = controller.Content;
            if (database == null) return;

            roster.AddRange(database.GetAll<NinjaDefinition>());
            roster.Sort((a, b) => a.Rarity != b.Rarity ? a.Rarity.CompareTo(b.Rarity) : string.CompareOrdinal(a.Id, b.Id));
            armory.AddRange(database.GetAll<WeaponDefinition>());
            armory.Sort((a, b) => a.Tier != b.Tier ? a.Tier.CompareTo(b.Tier) : string.CompareOrdinal(a.Id, b.Id));

            foreach (var id in new[] { StatIds.Health, StatIds.AttackDamage, StatIds.AttackSpeed, StatIds.Range, StatIds.Armor, StatIds.MagicResist, StatIds.MaxMana })
                if (database.TryGet<StatDefinition>(id, out var stat)) shownStats.Add(stat);
        }

        void Layout()
        {
            var width = PrototypeGui.Width;
            var height = PrototypeGui.Height;
            leftPanel = new Rect(10f, 10f, 250f, height - 210f);
            rightPanel = new Rect(width - 280f, 10f, 270f, height - 210f);
            topBar = new Rect(width / 2f - 230f, 10f, 460f, 74f);
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
            if (controller.Phase != BattlePhase.Setup || controller.Lineup == null)
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
                if (lineup.IsEquipped(weapon))
                {
                    Say($"{weapon.DisplayName} is already equipped. Remove it from that ninja first.");
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
            if (controller == null || controller.Phase != BattlePhase.Setup || controller.Lineup == null) return;

            PrototypeGui.Begin();
            Layout();

            DrawNames();
            DrawOpponents();
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
            GUI.Label(new Rect(topBar.x + 14f, topBar.y + 34f, 300f, 36f),
                "Drag ninjas onto the near half. Drag one off the board to remove it.", PrototypeGui.Wrap);

            GUI.enabled = controller.CanFight;
            if (GUI.Button(new Rect(topBar.xMax - 130f, topBar.y + 10f, 118f, 34f), "Fight! (Enter)")) controller.StartFight();
            GUI.enabled = lineup.Slots.Count > 0;
            if (GUI.Button(new Rect(topBar.xMax - 130f, topBar.y + 48f, 118f, 20f), "Clear lineup"))
            {
                lineup.Clear();
                selected = null;
            }

            GUI.enabled = true;
        }

        void DrawOpponents()
        {
            PrototypeGui.Panel(leftPanel);
            var x = leftPanel.x + 12f;
            var y = leftPanel.y + 10f;
            var width = leftPanel.width - 24f;
            GUI.Label(new Rect(x, y, width, 22f), "Opponent", PrototypeGui.Title);
            y += 28f;

            foreach (var opponent in controller.Opponents)
            {
                var rect = new Rect(x, y, width, 36f);
                var isSelected = opponent == controller.Opponent;
                PrototypeGui.Fill(rect, isSelected ? new Color(0.32f, 0.27f, 0.12f, 0.95f) : PrototypeGui.CardColor, isSelected);
                if (GUI.Button(rect, GUIContent.none, GUIStyle.none)) controller.SelectOpponent(opponent);
                GUI.Label(new Rect(rect.x + 8f, rect.y + 2f, rect.width - 16f, 20f), opponent.DisplayName, PrototypeGui.Label);
                PrototypeGui.Pips(new Vector2(rect.x + 9f, rect.y + 23f), opponent.Difficulty, 5, PrototypeGui.Gold);
                y += 40f;
            }

            if (controller.Opponent == null) return;

            y += 6f;
            var description = controller.Opponent.Description;
            var descriptionHeight = PrototypeGui.Wrap.CalcHeight(new GUIContent(description), width);
            GUI.Label(new Rect(x, y, width, descriptionHeight), description, PrototypeGui.Wrap);
            y += descriptionHeight + 10f;

            GUI.Label(new Rect(x, y, width, 22f), "Their synergies", PrototypeGui.Title);
            y += 26f;
            DrawSynergies(LineupSynergies.Count(controller.Opponent.Slots), x, y, width, leftPanel.yMax - 8f);
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
            GUI.Label(new Rect(x, y, width, 18f),
                $"<color=#{PrototypeGui.Hex(ninja.Element != null ? ninja.Element.Color : Color.grey)}>{ninja.Element?.DisplayName}</color>  {ninja.Class?.DisplayName}",
                PrototypeGui.Small);
            y += 22f;

            for (var i = 0; i < shownStats.Count; i++)
            {
                var stat = shownStats[i];
                var column = i % 2;
                var rect = new Rect(x + column * (width / 2f), y + (i / 2) * 17f, width / 2f, 17f);
                GUI.Label(rect, $"{stat.DisplayName}: <b>{ninja.GetBaseStat(stat):0.##}</b>", PrototypeGui.Small);
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
            GUI.Label(new Rect(bottomPanel.x + 12f, bottomPanel.y + 98f, 600f, 20f), "<b>Armory</b>  one of each weapon, drag onto a ninja", PrototypeGui.Label);

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
                GUI.Label(new Rect(rect.x + 10f, rect.y + 37f, rect.width - 12f, 16f),
                    placed ? "On the board" : $"<color=#{PrototypeGui.Hex(RarityColor(ninja.Rarity))}>{ninja.Rarity}</color>", PrototypeGui.Small);

                if (rect.Contains(point) && ninja.Skill != null)
                    tooltip = $"<b>{ninja.DisplayName}</b>: {ninja.Skill.DisplayName}. {ninja.Skill.Description}";
            }

            for (var i = 0; i < armory.Count && i < armoryCards.Count; i++)
            {
                var weapon = armory[i];
                var rect = armoryCards[i];
                var used = lineup.IsEquipped(weapon);
                var combined = weapon.Tier == WeaponTier.Combined;
                PrototypeGui.Fill(rect, used ? PrototypeGui.DisabledColor : combined ? new Color(0.26f, 0.22f, 0.14f, 0.95f) : PrototypeGui.CardColor, true);
                GUI.Label(new Rect(rect.x + 8f, rect.y + 4f, rect.width - 10f, 20f), $"<b>{weapon.DisplayName}</b>", PrototypeGui.Label);
                GUI.Label(new Rect(rect.x + 8f, rect.y + 23f, rect.width - 10f, 16f), used ? "Equipped" : combined ? "Combined" : "Component", PrototypeGui.Small);

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

        static Color RarityColor(Rarity rarity)
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
