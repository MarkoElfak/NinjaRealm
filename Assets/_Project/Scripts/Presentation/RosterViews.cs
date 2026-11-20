using System;
using System.Collections.Generic;
using NinjaRealm.Combat;
using NinjaRealm.Core.Content;
using NinjaRealm.Core.Definitions;
using NinjaRealm.Core.Progression;
using NinjaRealm.Presentation.Battle;
using UnityEngine;
using UnityEngine.UIElements;

namespace NinjaRealm.Presentation
{
    /// <summary>
    /// The roster's UI Toolkit views: your ninjas with their level ups and the runes (the Shrine), the Dojo's
    /// recruits, the weapons and forge (the Blacksmith), and one ninja's details. Each call
    /// builds a fresh element; after a button does something the view reports the result and
    /// asks its screen to rebuild it. Styles: UI/Styles/Panels.uss.
    /// </summary>
    public sealed class RosterViews
    {
        readonly Func<ProfileService> profile;
        readonly Func<ContentDatabase> content;
        readonly Action<bool, string> show;
        readonly Action changed;

        /// <param name="show">Shows the result of an action.</param>
        /// <param name="changed">Rebuilds the view after an action changed the save.</param>
        public RosterViews(Func<ProfileService> profile, Func<ContentDatabase> content, Action<bool, string> show, Action changed)
        {
            this.profile = profile;
            this.content = content;
            this.show = show;
            this.changed = changed;
        }

        ProfileService Profile => profile();

        void Did(bool succeeded, string result)
        {
            show?.Invoke(succeeded, result);
            changed?.Invoke();
        }

        // ---------------------------------------------------------------- the Shrine

        /// <summary>Your ninjas, with a level up button each.</summary>
        public VisualElement Ninjas()
        {
            var root = GameUi.Element("roster");
            var progression = Profile.Progression;
            var ninjas = Profile.OwnedNinjas();
            root.Add(GameUi.Label(
                $"<b>{ninjas.Count} / {progression.RosterLimit}</b> ninjas. Levels cost Karma; each adds 4% health and attack damage. Recruiting a ninja you own gives a shard toward its next star.",
                "note", "section-row"));

            var list = new ScrollView(ScrollViewMode.Vertical);
            list.AddToClassList("list");
            var health = Stat(StatIds.Health);
            var attack = Stat(StatIds.AttackDamage);
            foreach (var ninja in ninjas)
            {
                var owned = Profile.Find(ninja);
                var rank = owned.Rank;
                var row = Card(ninja, "ninja-row");

                var name = GameUi.Element("ninja-row__name");
                name.Add(NameLine(ninja));
                name.Add(GameUi.Label(ElementAndClass(ninja), "small"));
                row.Add(name);

                var standing = GameUi.Element("ninja-row__rank");
                standing.Add(GameUi.Label(LineupScreen.RankText(rank), "strong"));
                standing.Add(GameUi.Label($"{progression.Belt(rank.Level)} belt.  {ShardText(owned)}", "small"));
                row.Add(standing);

                var multiplier = progression.StatMultiplier(rank.Level, rank.Stars);
                var stats = GameUi.Element("ninja-row__stats");
                stats.Add(GameUi.Label($"Health <b>{Scaled(ninja, health, multiplier):0}</b>", "small"));
                stats.Add(GameUi.Label($"Attack <b>{Scaled(ninja, attack, multiplier):0}</b>", "small"));
                row.Add(stats);

                row.Add(GameUi.Element("spacer"));
                row.Add(LevelUp(ninja, owned.level, "ninja-row__action"));
                list.Add(row);
            }

            root.Add(list);
            return root;
        }

        /// <summary>A level up button for an owned ninja, or "Level cap".</summary>
        VisualElement LevelUp(NinjaDefinition ninja, int level, params string[] classes)
        {
            var progression = Profile.Progression;
            var cost = progression.LevelCost(level);
            if (cost < 0) return GameUi.Label("Level cap", "cap");

            var button = GameUi.Button($"Level up  ({cost} Karma)", () => Did(Profile.TryLevelUp(ninja, out var result), result), classes);
            button.SetEnabled(Profile.Balance(progression.Karma) >= cost);
            return button;
        }

        // ---------------------------------------------------------------- the Shrine's runes

        /// <summary>The altar slot picked for setting a rune, or -1 for the first empty one.</summary>
        int runeSlot = -1;

        /// <summary>The altar's slots, what the set runes give, today's runes and your runes.</summary>
        public VisualElement Runes()
        {
            var save = Profile;
            var root = GameUi.Element("roster");
            if (runeSlot >= save.RuneSlotsLit) runeSlot = -1;
            var target = runeSlot >= 0 ? runeSlot : save.Profile.runeSlots.IndexOf(string.Empty);

            root.Add(Altar(save, target));
            root.Add(GameUi.Label(RuneBonusText(save), "note", "wrap", "rune-bonus"));

            var columns = GameUi.Element("columns", "rune-columns");
            columns.Add(RuneShop(save));
            columns.Add(OwnedRuneList(save, target));
            root.Add(columns);
            return root;
        }

        VisualElement Altar(ProfileService save, int target)
        {
            var altar = GameUi.Element("altar");
            var next = save.NextRuneSlot;
            for (var i = 0; i < save.RuneSlotCount; i++)
            {
                var index = i;
                var step = save.Progression.RuneSlots[i];
                VisualElement slot;
                if (i < save.RuneSlotsLit)
                {
                    var rune = save.RuneInSlot(i);
                    slot = GameUi.Element("rune-slot");
                    slot.EnableInClassList("rune-slot--picked", i == target);
                    if (rune != null)
                    {
                        slot.style.borderTopColor = slot.style.borderBottomColor = slot.style.borderLeftColor = slot.style.borderRightColor = RuneArt.RarityColor(rune.Rarity);
                        slot.Add(Icon(rune, "rune-slot__icon"));
                        slot.Add(GameUi.Label($"{rune.DisplayName}  {ProfileService.RomanRank(save.RuneRankOf(rune))}", "rune-slot__name"));
                        var remove = GameUi.Button("Take out", () =>
                        {
                            runeSlot = index;
                            if (save.ClearRuneSlot(index)) Did(true, $"{rune.DisplayName} is back with your runes.");
                        }, "button--small", "rune-slot__action");
                        slot.Add(remove);
                    }
                    else
                    {
                        slot.Add(GameUi.Label("+", "rune-slot__empty"));
                        slot.Add(GameUi.Label(i == target ? "Pick a rune below" : "Empty", "rune-slot__name", "muted"));
                    }

                    slot.RegisterCallback<ClickEvent>(_ =>
                    {
                        if (runeSlot == index) return;
                        runeSlot = index;
                        changed?.Invoke();
                    });
                }
                else
                {
                    slot = GameUi.Element("rune-slot", "rune-slot--locked");
                    slot.Add(GameUi.Label("Unlit", "rune-slot__empty", "muted"));
                    slot.Add(GameUi.Label($"Clan level {step.clanLevel}", "rune-slot__name", "muted"));
                    if (step == next && save.ClanLevel >= step.clanLevel)
                    {
                        var light = GameUi.Button(step.karma > 0 ? $"Light  ({step.karma} Karma)" : "Light",
                            () => Did(save.TryLightRuneSlot(out var result), result), "button--small", "rune-slot__action");
                        light.SetEnabled(save.CannotLightRuneSlot() == null);
                        slot.Add(light);
                    }
                }

                slot.EnableInClassList("rune-slot--last", i == save.RuneSlotCount - 1);
                altar.Add(slot);
            }

            return altar;
        }

        /// <summary>"Your runes give: +5.5% Attack Damage. Fire ninjas: +8% Attack Damage." or how to start.</summary>
        static string RuneBonusText(ProfileService save)
        {
            var set = save.SetRunes();
            if (set.Count == 0)
                return "Runes on the altar give every ninja in your lineup a bonus in every fight. Buy one below, then set it in a lit slot.";
            var parts = new List<string>();
            foreach (var entry in set) parts.Add($"<b>{entry.Rune.DisplayName}</b> {entry.Rune.Describe(entry.Rank)}");
            return "Your altar gives: " + string.Join(".  ", parts) + ".";
        }

        VisualElement RuneShop(ProfileService save)
        {
            var progression = save.Progression;
            var gold = save.Balance(progression.Gold);
            var column = GameUi.Element("column");

            var title = GameUi.Element("section-row", "column__title");
            title.Add(GameUi.Label("Today's runes", "section-title"));
            title.Add(GameUi.Label("  new every day", "note"));
            var refresh = GameUi.Button($"New runes  ({progression.ShrineRefreshCost} Gold)", () => Did(save.TryRefreshShrine(out var result), result), "button--small");
            refresh.SetEnabled(gold >= progression.ShrineRefreshCost);
            title.Add(refresh);
            column.Add(title);

            var list = new ScrollView(ScrollViewMode.Vertical);
            list.AddToClassList("list");
            var shown = new HashSet<RuneDefinition>();
            foreach (var rune in save.ShrineOffers())
            {
                if (rune == null)
                {
                    list.Add(GameUi.Label("Bought", "card", "rune-row", "rune-row--taken", "muted"));
                    continue;
                }

                shown.Add(rune);
                list.Add(ShopRow(save, rune));
            }

            var always = GameUi.Element("section-row", "rune-subtitle");
            always.Add(GameUi.Label("Always for sale", "section-title"));
            list.Add(always);
            foreach (var rune in save.RunesForSale())
                if (!shown.Contains(rune))
                    list.Add(ShopRow(save, rune));

            column.Add(list);
            return column;
        }

        VisualElement ShopRow(ProfileService save, RuneDefinition rune)
        {
            var row = RuneRow(rune);
            var text = GameUi.Element("rune-row__text");
            text.Add(RuneName(rune));
            text.Add(GameUi.Label(rune.Describe(1), "small", "wrap"));
            row.Add(text);

            if (save.OwnsRune(rune))
            {
                row.Add(GameUi.Label("Owned", "rune-row__action", "muted"));
                return row;
            }

            var price = save.RunePrice(rune);
            var karma = save.RuneKarmaPrice(rune);
            var label = karma > 0 ? $"Buy  ({price} Gold, {karma} Karma)" : $"Buy  ({price} Gold)";
            if (save.IsHomeSeal(rune)) label += "  home";
            var buy = GameUi.Button(label, () => Did(save.TryBuyRune(rune, out var result), result), "button--small", "rune-row__action");
            buy.SetEnabled(save.CannotBuyRune(rune) == null);
            row.Add(buy);
            return row;
        }

        VisualElement OwnedRuneList(ProfileService save, int target)
        {
            var column = GameUi.Element("column", "column--last");
            var title = GameUi.Element("section-row", "column__title");
            title.Add(GameUi.Label("Your runes", "section-title"));
            title.Add(GameUi.Label(target >= 0 ? $"  setting slot {target + 1}" : "  light a slot to set one", "note"));
            column.Add(title);

            var list = new ScrollView(ScrollViewMode.Vertical);
            list.AddToClassList("list");
            var owned = save.OwnedRunes();
            if (owned.Count == 0) list.Add(GameUi.Label("None yet. Common runes are always for sale; rarer ones show up in today's runes.", "note", "wrap"));
            foreach (var rune in owned)
            {
                var rank = save.RuneRankOf(rune);
                var row = RuneRow(rune);
                var text = GameUi.Element("rune-row__text");
                var name = RuneName(rune);
                name.Add(GameUi.Pips(rank, save.RuneMaxRank(rune)));
                text.Add(name);
                text.Add(GameUi.Label($"Rank {ProfileService.RomanRank(rank)}: {rune.Describe(rank)}", "small", "wrap"));
                var cost = save.RuneRankCost(rune);
                if (cost > 0) text.Add(GameUi.Label($"Rank {ProfileService.RomanRank(rank + 1)}: {rune.Describe(rank + 1)}", "small", "muted", "wrap"));
                row.Add(text);

                var actions = GameUi.Element("rune-row__buttons");
                var slot = save.SlotOfRune(rune);
                if (slot >= 0)
                {
                    actions.Add(GameUi.Label($"In slot {slot + 1}", "small", "rune-row__set"));
                }
                else
                {
                    var set = GameUi.Button("Set", () =>
                    {
                        var ok = save.TrySetRune(target, rune, out var result);
                        if (ok) runeSlot = -1;
                        Did(ok, result);
                    }, "button--small");
                    var problem = target >= 0 ? save.CannotSetRune(target, rune) : "Light an altar slot first.";
                    set.SetEnabled(problem == null);
                    set.tooltip = problem ?? string.Empty;
                    actions.Add(set);
                }

                if (cost > 0)
                {
                    var rankUp = GameUi.Button($"Rank up  ({cost} Gold)", () => Did(save.TryRankUpRune(rune, out var result), result), "button--small");
                    var problem = save.CannotRankUpRune(rune);
                    rankUp.SetEnabled(problem == null);
                    rankUp.tooltip = problem ?? string.Empty;
                    actions.Add(rankUp);
                }
                else
                {
                    actions.Add(GameUi.Label("Top rank", "small", "muted"));
                }

                var sell = GameUi.Button($"Sell  ({save.RuneSellValue(rune)})", () => Did(save.TrySellRune(rune, out var result), result), "button--small");
                actions.Add(sell);
                row.Add(actions);
                list.Add(row);
            }

            column.Add(list);
            return column;
        }

        /// <summary>A card with the rune's icon and its rarity colour down the left edge.</summary>
        static VisualElement RuneRow(RuneDefinition rune)
        {
            var row = GameUi.Element("card", "rune-row");
            row.style.borderLeftColor = RuneArt.RarityColor(rune.Rarity);
            row.Add(Icon(rune, "rune-row__icon"));
            return row;
        }

        static VisualElement RuneName(RuneDefinition rune)
        {
            var line = GameUi.Element("name-line");
            line.Add(GameUi.Label(rune.DisplayName, "strong"));
            var rarity = GameUi.Label(rune.Rarity.ToString(), "rarity");
            rarity.style.color = RuneArt.RarityColor(rune.Rarity);
            line.Add(rarity);
            line.tooltip = rune.Description;
            return line;
        }

        static VisualElement Icon(RuneDefinition rune, string kind)
        {
            var icon = GameUi.Element("rune-icon", kind);
            var texture = RuneArt.Icon(rune);
            if (texture != null) icon.style.backgroundImage = new StyleBackground(texture);
            else icon.style.backgroundColor = rune.Color;
            return icon;
        }

        // ---------------------------------------------------------------- one ninja

        /// <summary>The stats a ninja's details list, in order.</summary>
        public static readonly string[] ShownStatIds =
        {
            StatIds.Health, StatIds.AttackDamage, StatIds.AttackSpeed, StatIds.Range, StatIds.Armor, StatIds.MagicResist, StatIds.MaxMana,
        };

        /// <summary>
        /// One ninja in full: rarity, element and class, its rank or how to get it, its stats and
        /// its skill. With <paramref name="levelUp"/> an owned ninja also gets a level up button.
        /// Works without a save, with base stats.
        /// </summary>
        public VisualElement Details(NinjaDefinition ninja, bool levelUp)
        {
            var root = GameUi.Element("ninja-details");
            root.Add(NameLine(ninja, "ninja-name--big"));
            root.Add(GameUi.Label(ElementAndClass(ninja, true), "small"));

            var save = Profile;
            var owned = save?.Find(ninja);
            var multiplier = ProgressionDefinition.BasisPoints;
            if (owned != null)
            {
                var progression = save.Progression;
                var rank = owned.Rank;
                multiplier = progression.StatMultiplier(rank.Level, rank.Stars);
                root.Add(GameUi.Label($"{LineupScreen.RankText(rank)}   {progression.Belt(rank.Level)} belt   {ShardText(owned)}", "ninja-details__rank"));
            }
            else if (save != null)
            {
                root.Add(GameUi.Label(HowToGet(ninja), "ninja-details__rank", "wrap"));
            }

            var stats = GameUi.Element("stats");
            foreach (var id in ShownStatIds)
            {
                var stat = Stat(id);
                if (stat == null) continue;
                var value = id == StatIds.Health || id == StatIds.AttackDamage ? Scaled(ninja, stat, multiplier) : ninja.GetBaseStat(stat);
                stats.Add(GameUi.Label($"<color=#9ca3b0>{stat.DisplayName}</color>  <b>{value:0.##}</b>", "stat"));
            }

            root.Add(stats);

            if (ninja.Skill != null)
            {
                root.Add(GameUi.Label(ninja.Skill.DisplayName, "skill-name"));
                root.Add(GameUi.Label(ninja.Skill.Description, "note"));
            }

            if (!levelUp || owned == null) return root;
            root.Add(LevelUp(ninja, owned.level, "level-up"));
            if (save.Progression.LevelCost(owned.level) >= 0) root.Add(GameUi.Label("Each level adds 4% health and attack damage.", "small"));
            return root;
        }

        /// <summary>Where a ninja the player doesn't own comes from, with the prices.</summary>
        string HowToGet(NinjaDefinition ninja)
        {
            var progression = Profile.Progression;
            var recruit = progression.RecruitPrice(ninja.Rarity);
            var direct = progression.DirectBuyPrice(ninja.Rarity);
            const string missing = "<color=#9ca3b0>Not recruited yet.</color>";
            if (recruit <= 0) return missing;
            return direct > 0
                ? $"{missing} It can show up in the Dojo's daily recruits for {recruit} Gold, or you can buy it there directly for {direct} Gold."
                : $"{missing} It can show up in the Dojo's daily recruits for {recruit} Gold.";
        }

        // ---------------------------------------------------------------- the Dojo

        /// <summary>The day's four recruits and the direct buy list.</summary>
        public VisualElement Recruits()
        {
            var root = GameUi.Element("roster");
            var progression = Profile.Progression;
            var gold = Profile.Balance(progression.Gold);

            var header = GameUi.Element("section-row");
            header.Add(GameUi.Label("Four recruits, new every day. A ninja you already own becomes a shard toward its next star.", "note"));
            var refresh = GameUi.Button($"New recruits  ({progression.DojoRefreshCost} Gold)",
                () => Did(Profile.TryRefreshDojo(out var result), result), "button--small");
            refresh.SetEnabled(gold >= progression.DojoRefreshCost);
            header.Add(refresh);
            root.Add(header);

            var cards = GameUi.Element("recruits");
            var offers = Profile.DojoOffers();
            for (var i = 0; i < offers.Count; i++)
            {
                var ninja = offers[i];
                var last = i == offers.Count - 1;
                if (ninja == null)
                {
                    var taken = GameUi.Element("card", "recruit", "recruit--taken");
                    taken.EnableInClassList("recruit--last", last);
                    taken.Add(GameUi.Label("Recruited", "muted"));
                    cards.Add(taken);
                    continue;
                }

                var card = GameUi.Element("card", "recruit");
                card.EnableInClassList("recruit--last", last);
                card.style.borderTopColor = ElementColor(ninja);
                card.Add(GameUi.Label(ninja.DisplayName, "ninja-name"));
                card.Add(GameUi.Label($"{RarityText(ninja)}  {ElementAndClass(ninja)}", "recruit__line"));
                card.Add(GameUi.Label(OwnedText(ninja), "recruit__line"));
                if (ninja.Skill != null) card.Add(GameUi.Label($"Skill: {ninja.Skill.DisplayName}", "recruit__line", "muted"));
                card.Add(GameUi.Element("spacer"));

                var index = i;
                var price = progression.RecruitPrice(ninja.Rarity);
                var recruit = GameUi.Button($"Recruit  ({price} Gold)", () => Did(Profile.TryRecruitOffer(index, out var result), result));
                recruit.SetEnabled(Profile.CannotGain(ninja) == null && gold >= price);
                card.Add(recruit);
                cards.Add(card);
            }

            root.Add(cards);

            var buyTitle = GameUi.Element("section-row");
            buyTitle.Add(GameUi.Label("Buy directly", "section-title"));
            buyTitle.Add(GameUi.Label("  Common and Rare ninjas, at a higher price", "note"));
            root.Add(buyTitle);

            var list = new ScrollView(ScrollViewMode.Vertical);
            list.AddToClassList("list");
            foreach (var ninja in Profile.DirectBuyNinjas())
            {
                var row = GameUi.Element("card", "buy-row");
                row.Add(GameUi.Label($"<b>{ninja.DisplayName}</b>  {RarityText(ninja)}  <color=#9ca3b0>{ElementAndClass(ninja)}</color>", "buy-row__name"));
                row.Add(GameUi.Label(OwnedText(ninja), "buy-row__owned"));

                var price = progression.DirectBuyPrice(ninja.Rarity);
                var buy = GameUi.Button($"Buy  ({price} Gold)", () => Did(Profile.TryBuyDirect(ninja, out var result), result), "button--small", "buy-row__action");
                buy.SetEnabled(Profile.CannotGain(ninja) == null && gold >= price);
                row.Add(buy);
                list.Add(row);
            }

            root.Add(list);
            return root;
        }

        /// <summary>What getting this ninja would do: a new ninja, a shard, or nothing.</summary>
        string OwnedText(NinjaDefinition ninja)
        {
            var owned = Profile.Find(ninja);
            if (owned == null)
                return Profile.RosterFull ? "<color=#ff9980>Roster full</color>" : "<color=#99ff99>New</color>";
            var next = Profile.Progression.ShardsForNextStar(owned.stars);
            return next > 0 ? $"Owned: +1 shard ({owned.shards} / {next})" : "Owned, most stars";
        }

        // ---------------------------------------------------------------- the Blacksmith

        /// <summary>Your weapons and the forge, side by side.</summary>
        public VisualElement Armory()
        {
            var progression = Profile.Progression;
            var root = GameUi.Element("columns");

            var weapons = GameUi.Element("column");
            var weaponsTitle = GameUi.Element("section-row", "column__title");
            weaponsTitle.Add(GameUi.Label("Your weapons", "section-title"));
            weaponsTitle.Add(GameUi.Label("  wins can drop components", "note"));
            weapons.Add(weaponsTitle);
            var weaponList = new ScrollView(ScrollViewMode.Vertical);
            weaponList.AddToClassList("list");
            var owned = Profile.OwnedWeapons();
            if (owned.Count == 0) weaponList.Add(GameUi.Label("None yet. Win fights to find components.", "note"));
            foreach (var weapon in owned)
            {
                var card = GameUi.Element("card", "weapon");
                card.EnableInClassList("weapon--combined", weapon.Tier == WeaponTier.Combined);
                var top = GameUi.Element("weapon__top");
                top.Add(GameUi.Label(weapon.DisplayName, "strong"));
                top.Add(GameUi.Label($"x {Profile.WeaponCount(weapon)}", "strong"));
                card.Add(top);
                card.Add(GameUi.Label(weapon.Description, "note"));
                weaponList.Add(card);
            }

            weapons.Add(weaponList);
            root.Add(weapons);

            var forge = GameUi.Element("column", "column--last");
            var forgeTitle = GameUi.Element("section-row", "column__title");
            forgeTitle.Add(GameUi.Label("Forge", "section-title"));
            forgeTitle.Add(GameUi.Label($"  two components and {progression.ForgeCost} Steel", "note"));
            forge.Add(forgeTitle);
            var recipeList = new ScrollView(ScrollViewMode.Vertical);
            recipeList.AddToClassList("list");
            foreach (var recipe in Profile.Recipes())
            {
                var card = GameUi.Element("card", "recipe");
                var text = GameUi.Element("recipe__text");
                text.Add(GameUi.Label($"<b>{recipe.DisplayName}</b>  x {Profile.WeaponCount(recipe)}"));
                var pair = recipe.ComponentA == recipe.ComponentB ? 2 : 1;
                text.Add(GameUi.Label($"{Have(recipe.ComponentA, pair)} + {Have(recipe.ComponentB, pair)}", "small"));
                text.Add(GameUi.Label(recipe.Description, "note"));
                card.Add(text);

                var button = GameUi.Button("Forge", () => Did(Profile.TryForge(recipe, out var result), result), "recipe__action");
                button.SetEnabled(Profile.CannotForge(recipe) == null);
                card.Add(button);
                recipeList.Add(card);
            }

            forge.Add(recipeList);
            root.Add(forge);
            return root;
        }

        /// <summary>A component's name and count, green when the player has enough of it.</summary>
        string Have(WeaponDefinition component, int needed)
        {
            var count = Profile.WeaponCount(component);
            var color = count >= needed ? "99ff99" : "9ca3b0";
            return $"<color=#{color}>{component.DisplayName} ({count})</color>";
        }

        // ---------------------------------------------------------------- shared bits

        /// <summary>A card with the ninja's element colour down its left edge.</summary>
        static VisualElement Card(NinjaDefinition ninja, string kind)
        {
            var card = GameUi.Element("card", kind);
            card.style.borderLeftColor = ElementColor(ninja);
            return card;
        }

        /// <summary>The ninja's name and its rarity, side by side.</summary>
        public static VisualElement NameLine(NinjaDefinition ninja, string nameClass = null)
        {
            var line = GameUi.Element("name-line");
            var name = GameUi.Label(ninja.DisplayName, "ninja-name");
            if (nameClass != null) name.AddToClassList(nameClass);
            line.Add(name);
            var rarity = GameUi.Label(ninja.Rarity.ToString(), "rarity");
            rarity.style.color = LineupScreen.RarityColor(ninja.Rarity);
            line.Add(rarity);
            return line;
        }

        public static Color ElementColor(NinjaDefinition ninja) => ninja.Element != null ? ninja.Element.Color : Color.grey;

        static string RarityText(NinjaDefinition ninja) =>
            $"<color=#{PrototypeGui.Hex(LineupScreen.RarityColor(ninja.Rarity))}>{ninja.Rarity}</color>";

        /// <summary>"Fire Vanguard", with the element in its colour when asked.</summary>
        public static string ElementAndClass(NinjaDefinition ninja, bool colored = false)
        {
            var element = ninja.Element != null ? ninja.Element.DisplayName : "";
            if (colored && ninja.Element != null) element = $"<color=#{PrototypeGui.Hex(ninja.Element.Color)}>{element}</color>";
            return $"{element} {(ninja.Class != null ? ninja.Class.DisplayName : "")}".Trim();
        }

        string ShardText(OwnedNinja owned)
        {
            var next = Profile.Progression.ShardsForNextStar(owned.stars);
            return next > 0 ? $"{owned.shards} / {next} shards to {owned.stars + 1} stars" : "Most stars";
        }

        static float Scaled(NinjaDefinition ninja, StatDefinition stat, int multiplier) =>
            stat == null ? 0f : ninja.GetBaseStat(stat) * multiplier / ProgressionDefinition.BasisPoints;

        StatDefinition Stat(string id)
        {
            var database = content();
            return database != null && database.TryGet<StatDefinition>(id, out var stat) ? stat : null;
        }
    }
}
