using System;
using System.Collections.Generic;
using System.Text;
using NinjaRealm.Combat;
using NinjaRealm.Core.Content;
using NinjaRealm.Core.Definitions;
using NinjaRealm.Core.Fights;
using NinjaRealm.Core.Lineups;
using NinjaRealm.Core.Progression;

namespace NinjaRealm.Core.Campaign
{
    /// <summary>How one stage went for the walker.</summary>
    public sealed class StageWalk
    {
        public StageDefinition Stage;

        /// <summary>Fights on this stage until it was cleared, the win included.</summary>
        public int Tries;

        /// <summary>Fights on earlier stages for Gold and clan XP while stuck on this one.</summary>
        public int FarmFights;

        public bool Cleared;
        public int Stars;
        public int ClanLevel;
        public int TeamSize;

        /// <summary>The lineup's average ninja level, in tenths.</summary>
        public int TeamLevelTenths;

        /// <summary>The lineup's average ninja stars, in tenths.</summary>
        public int TeamStarsTenths;
    }

    public sealed class CampaignWalk
    {
        public readonly List<StageWalk> Stages = new List<StageWalk>();
        public int Fights;

        /// <summary>True when every stage was cleared before the walker ran out of fights.</summary>
        public bool Finished;

        /// <summary>Fights spent in a chapter: tries on its stages and the farming done while stuck on them.</summary>
        public int FightsIn(ChapterDefinition chapter)
        {
            var fights = 0;
            foreach (var walk in Stages)
                foreach (var stage in chapter.Stages)
                    if (stage == walk.Stage) fights += walk.Tries + walk.FarmFights;
            return fights;
        }
    }

    /// <summary>
    /// Plays the campaign headlessly with a simple, sensible player, to see how long each
    /// chapter takes. The player always fights the next stage with its strongest ninjas,
    /// opens star chests, lights altar slots and fills them with the cheapest runes, spends Karma
    /// on its lowest level, buys what the Dojo offers when it helps, forges what it can and hands
    /// out its weapons. After two losses in a row it fights the stage before
    /// for Gold and clan XP, then tries again. Every run with the same seed goes the same way.
    /// </summary>
    public sealed class CampaignWalker
    {
        // Front row first for the classes that fight up close, back row for the rest.
        static readonly int[] ColumnOrder = { 3, 2, 4, 1, 5, 0, 6 };

        readonly ContentDatabase content;
        readonly ProgressionDefinition progression;

        public CampaignWalker(ContentDatabase content)
        {
            this.content = content ?? throw new ArgumentNullException(nameof(content));
            progression = ProfileService.FindProgression(content);
        }

        /// <summary>The walker gives up after this many fights.</summary>
        public int MaxFights { get; set; } = 600;

        /// <summary>A day passes after this many fights: the Dojo has new offers and the Mint, the Steel Mine and the altar's Karma are collected.</summary>
        public int FightsPerDay { get; set; } = 10;

        public int Seed { get; set; } = 1;

        public CampaignWalk Run()
        {
            var walk = new CampaignWalk();
            if (progression == null) return walk;

            var service = new ProfileService(ProfileService.NewProfile(progression, Seed), content, progression);
            var campaign = service.Campaign;
            var day = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
            service.Clock = () => day;
            service.StartIncome(day);

            StageWalk current = null;
            var lossesInARow = 0;
            while (walk.Fights < MaxFights)
            {
                var target = service.NextUnclearedStage();
                if (target == null || service.HasCleared(target))
                {
                    walk.Finished = true;
                    break;
                }

                if (current == null || current.Stage != target)
                {
                    current = new StageWalk { Stage = target };
                    walk.Stages.Add(current);
                    lossesInARow = 0;
                }

                if (walk.Fights % FightsPerDay == 0)
                {
                    day = day.AddDays(1);
                    service.RefreshDojoIfNewDay(day);
                    foreach (var income in progression.Incomes)
                        if (income != null) service.TryCollectIncome(income.id, day, out _);
                    service.TryCollectAltarKarma(day, out _);
                }

                Spend(service);

                // Stuck: fight the stage before for its pay, then come back.
                var farm = lossesInARow >= 2 ? campaign.Previous(target) : null;
                var stage = farm ?? target;
                var lineup = BestLineup(service, stage);
                var fightSeed = unchecked(Seed * 7919 + walk.Fights);
                var stars = StageStars.For(Fight(service, lineup, stage, fightSeed));
                service.ApplyStageResult(stage, stars, fightSeed);
                walk.Fights++;

                if (farm != null)
                {
                    current.FarmFights++;
                    lossesInARow = 1;
                    continue;
                }

                current.Tries++;
                if (stars == 0)
                {
                    lossesInARow++;
                    continue;
                }

                current.Cleared = true;
                current.Stars = stars;
                current.ClanLevel = service.ClanLevel;
                current.TeamSize = lineup.Count;
                var levels = 0;
                var starSum = 0;
                foreach (var slot in lineup)
                {
                    var rank = service.RankOf(slot.ninja);
                    levels += rank.Level;
                    starSum += rank.Stars;
                }

                current.TeamLevelTenths = lineup.Count > 0 ? levels * 10 / lineup.Count : 0;
                current.TeamStarsTenths = lineup.Count > 0 ? starSum * 10 / lineup.Count : 0;
            }

            if (!walk.Finished)
            {
                var left = service.NextUnclearedStage();
                walk.Finished = left != null && service.HasCleared(left);
            }

            return walk;
        }

        CombatResult Fight(ProfileService service, List<LineupSlot> lineup, StageDefinition stage, int seed)
        {
            var builder = CombatSetupBuilder.FromDatabase(content);
            builder.AddLineup(0, lineup, service.RankOf, service.RunePassives);
            builder.AddLineup(1, stage.Lineup.Slots, stage.RankOf);
            builder.AddTiles(stage.Tiles);
            return new CombatSimulation(builder.Build(), (ulong)(uint)seed).Run();
        }

        // ---------------------------------------------------------------- the player's choices

        void Spend(ProfileService service)
        {
            // Star chests are opened as soon as the stars are there.
            foreach (var chapter in service.Campaign.Chapters)
                foreach (var chest in chapter.Chests)
                    service.TryOpenChest(chapter, chest, out _);

            // Recruits: a new ninja while the team has room for one, otherwise shards for the ninjas that fight.
            var team = TopNinjas(service);
            var offers = service.DojoOffers();
            for (var pass = 0; pass < 2; pass++)
                for (var i = 0; i < offers.Count; i++)
                {
                    var offer = offers[i];
                    if (offer == null) continue;
                    var wanted = pass == 0
                        ? !service.Owns(offer) && service.OwnedNinjas().Count < LineupRules.MaxUnits
                        : team.Contains(offer);
                    if (wanted && service.TryRecruitOffer(i, out _)) offers[i] = null;
                }

            // A team with an empty place buys the cheapest ninja it can.
            if (service.OwnedNinjas().Count < service.TeamSize)
                foreach (var ninja in service.DirectBuyNinjas())
                    if (!service.Owns(ninja) && service.TryBuyDirect(ninja, out _)) break;

            // Runes make Karma: light the next altar slot when it opens, then fill empty slots with
            // the cheapest rune for sale that fits.
            while (service.TryLightRuneSlot(out _))
            {
            }

            foreach (var rune in service.RunesForSale())
                if (!service.OwnsRune(rune) && service.FitsOnAltar(rune) && service.CannotBuyRune(rune) == null)
                    service.TryBuyRune(rune, out _);

            // Karma: always the lowest level in the team.
            team = TopNinjas(service);
            while (team.Count > 0)
            {
                NinjaDefinition lowest = null;
                foreach (var ninja in team)
                    if (service.LevelUpCost(ninja) > 0 && (lowest == null || service.RankOf(ninja).Level < service.RankOf(lowest).Level))
                        lowest = ninja;
                if (lowest == null || !service.TryLevelUp(lowest, out _)) break;
            }

            foreach (var recipe in service.Recipes())
                while (service.TryForge(recipe, out _))
                {
                }
        }

        /// <summary>The strongest ninjas the team has room for, strongest first.</summary>
        List<NinjaDefinition> TopNinjas(ProfileService service)
        {
            var owned = service.OwnedNinjas();
            owned.Sort((a, b) =>
            {
                var power = Power(service, b).CompareTo(Power(service, a));
                return power != 0 ? power : string.CompareOrdinal(a.Id, b.Id);
            });
            if (owned.Count > service.TeamSize) owned.RemoveRange(service.TeamSize, owned.Count - service.TeamSize);
            return owned;
        }

        long Power(ProfileService service, NinjaDefinition ninja)
        {
            var rank = service.RankOf(ninja);
            return (long)progression.StatMultiplier(rank.Level, rank.Stars) * (10 + (int)ninja.Rarity);
        }

        static bool FightsUpClose(NinjaDefinition ninja)
        {
            var id = ninja.Class != null ? ninja.Class.Id : "";
            return id.EndsWith("vanguard", StringComparison.Ordinal) || id.EndsWith("blademaster", StringComparison.Ordinal);
        }

        List<LineupSlot> BestLineup(ProfileService service, StageDefinition stage = null)
        {
            var team = TopNinjas(service);

            // Vanguards take the middle of the front row, so they are placed first.
            team.Sort((a, b) =>
            {
                var vanguard = IsVanguard(b).CompareTo(IsVanguard(a));
                return vanguard != 0 ? vanguard : string.CompareOrdinal(a.Id, b.Id);
            });

            var slots = new List<LineupSlot>();
            var front = 0;
            var back = 0;
            var anyUpClose = false;
            foreach (var ninja in team) anyUpClose |= FightsUpClose(ninja);
            foreach (var ninja in team)
            {
                // Without anyone for the front row, everyone stands there.
                var upClose = FightsUpClose(ninja) || !anyUpClose;
                var column = ColumnOrder[(upClose ? front++ : back++) % ColumnOrder.Length];
                slots.Add(new LineupSlot(ninja, column, upClose ? LineupRules.RowsPerTeam - 1 : 0));
            }

            UseTiles(slots, stage);

            // Weapons: combined ones first, dealt round the team.
            var weapons = service.OwnedWeapons();
            weapons.Sort((a, b) => a.Tier != b.Tier ? b.Tier.CompareTo(a.Tier) : string.CompareOrdinal(a.Id, b.Id));
            var next = 0;
            foreach (var weapon in weapons)
                for (var copy = 0; copy < service.WeaponCount(weapon) && slots.Count > 0; copy++)
                {
                    var tried = 0;
                    while (tried < slots.Count && slots[next % slots.Count].weapons.Count >= LineupRules.MaxWeaponsPerNinja)
                    {
                        next++;
                        tried++;
                    }

                    if (tried == slots.Count) return slots;
                    slots[next++ % slots.Count].weapons.Add(weapon);
                }

            return slots;
        }

        /// <summary>
        /// Moves ninjas for the stage's tiles on the player's half: off a tile that only hurts them,
        /// and the ninja a tile helps most onto it. Trade-off tiles (a bonus and a drawback together)
        /// are left alone. Deterministic: tiles in stage order, ties to the earlier slot.
        /// </summary>
        internal static void UseTiles(List<LineupSlot> slots, StageDefinition stage)
        {
            if (stage == null || stage.Tiles.Count == 0) return;
            var tiles = stage.Tiles;
            int ScoreAt(LineupSlot slot) => BoardTile.At(tiles, 0, slot.column, slot.rowFromBack)?.Score(slot.ninja) ?? 0;

            LineupSlot At(int column, int row)
            {
                foreach (var slot in slots)
                    if (slot.column == column && slot.rowFromBack == row) return slot;
                return null;
            }

            // Step off tiles that hurt, to a free plain hex in the same row.
            foreach (var slot in slots)
            {
                if (ScoreAt(slot) >= 0) continue;
                foreach (var column in ColumnOrder)
                    if (At(column, slot.rowFromBack) == null && BoardTile.At(tiles, 0, column, slot.rowFromBack) == null)
                    {
                        slot.column = column;
                        break;
                    }
            }

            // The ninja a tile helps most takes it, unless it already stands on a tile that helps it.
            foreach (var entry in tiles)
            {
                if (entry?.tile == null || entry.team != 0) continue;
                var standing = At(entry.column, entry.rowFromBack);
                if (standing != null && entry.tile.Score(standing.ninja) > 0) continue;

                LineupSlot best = null;
                var bestScore = 0;
                foreach (var slot in slots)
                {
                    var score = entry.tile.Score(slot.ninja);
                    if (score > bestScore && ScoreAt(slot) <= 0)
                    {
                        best = slot;
                        bestScore = score;
                    }
                }

                if (best == null) continue;
                if (standing != null)
                {
                    standing.column = best.column;
                    standing.rowFromBack = best.rowFromBack;
                }

                best.column = entry.column;
                best.rowFromBack = entry.rowFromBack;
            }
        }

        static bool IsVanguard(NinjaDefinition ninja) =>
            ninja.Class != null && ninja.Class.Id.EndsWith("vanguard", StringComparison.Ordinal);

        // ---------------------------------------------------------------- report

        /// <summary>The walk as a Markdown table: one row per stage and a total per chapter.</summary>
        public string Report(CampaignWalk walk)
        {
            var campaign = new CampaignMap(content);
            var text = new StringBuilder();
            text.AppendLine($"Campaign walk, seed {Seed}: {walk.Fights} fights, {(walk.Finished ? "finished" : "not finished")}.");
            text.AppendLine();
            text.AppendLine("| Stage | Enemy | Level | Tries | Farm fights | Stars | Clan | Team | Avg level | Avg stars |");
            text.AppendLine("| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |");
            foreach (var stage in walk.Stages)
            {
                var name = stage.Stage.Lineup != null ? stage.Stage.Lineup.DisplayName : "?";
                text.Append($"| {campaign.LabelOf(stage.Stage)} | {name} | {stage.Stage.Level} | {stage.Tries} | {stage.FarmFights} | ");
                text.AppendLine(stage.Cleared
                    ? $"{stage.Stars} | {stage.ClanLevel} | {stage.TeamSize} | {Tenths(stage.TeamLevelTenths)} | {Tenths(stage.TeamStarsTenths)} |"
                    : "not cleared | | | | |");
            }

            text.AppendLine();
            foreach (var chapter in campaign.Chapters)
                text.AppendLine($"* Chapter {chapter.Number}, {chapter.DisplayName}: {walk.FightsIn(chapter)} fights.");
            return text.ToString();
        }

        static string Tenths(int value) => $"{value / 10}.{value % 10}";
    }
}
