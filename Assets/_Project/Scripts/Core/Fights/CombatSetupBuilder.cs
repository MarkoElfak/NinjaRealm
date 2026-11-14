using System;
using System.Collections.Generic;
using NinjaRealm.Combat;
using NinjaRealm.Core.Content;
using NinjaRealm.Core.Definitions;

namespace NinjaRealm.Core.Fights
{
    /// <summary>
    /// Turns content assets (ninjas, weapons, traits, stats) into a FightSetup for the
    /// combat simulation. This is the only place that converts authored float values into
    /// the simulation's fixed-point numbers.
    /// </summary>
    public sealed class CombatSetupBuilder
    {
        readonly List<StatDefinition> stats;
        readonly List<Entry> entries = new List<Entry>();

        /// <param name="stats">Every stat a unit should carry. Stats a ninja does not list use the stat's default.</param>
        public CombatSetupBuilder(IEnumerable<StatDefinition> stats)
        {
            this.stats = new List<StatDefinition>();
            foreach (var stat in stats)
                if (stat != null) this.stats.Add(stat);
            this.stats.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
        }

        public static CombatSetupBuilder FromDatabase(ContentDatabase database) =>
            new CombatSetupBuilder(database.GetAll<StatDefinition>());

        public int BoardColumns { get; set; } = 7;

        public int BoardRows { get; set; } = 8;

        /// <summary>Adds a ninja at a board position. Weapons are optional.</summary>
        public CombatSetupBuilder Add(int team, NinjaDefinition ninja, HexCoord position, params WeaponDefinition[] weapons)
        {
            if (ninja == null) throw new ArgumentNullException(nameof(ninja));
            if (team != 0 && team != 1) throw new ArgumentOutOfRangeException(nameof(team), "Team must be 0 or 1.");
            entries.Add(new Entry(team, ninja, position, weapons ?? Array.Empty<WeaponDefinition>()));
            return this;
        }

        /// <summary>
        /// Converts a position on a team's own half into a board position. Row 0 is the team's
        /// back row and column 0 is its left edge; team 1's half is flipped so both teams face each other.
        /// </summary>
        public HexCoord TeamPosition(int team, int column, int rowFromBack)
        {
            var half = BoardRows / 2;
            if (rowFromBack < 0 || rowFromBack >= half)
                throw new ArgumentOutOfRangeException(nameof(rowFromBack), $"Each team has {half} rows.");
            if (column < 0 || column >= BoardColumns)
                throw new ArgumentOutOfRangeException(nameof(column), $"The board has {BoardColumns} columns.");
            return team == 0 ? new HexCoord(column, rowFromBack) : new HexCoord(column, BoardRows - 1 - rowFromBack);
        }

        public FightSetup Build()
        {
            var setup = new FightSetup { BoardColumns = BoardColumns, BoardRows = BoardRows };
            var traits = new SortedDictionary<string, TraitDefinition>(StringComparer.Ordinal);

            foreach (var entry in entries)
            {
                setup.Units.Add(BuildUnit(entry));
                foreach (var trait in entry.Ninja.Traits)
                    traits[trait.Id] = trait;
            }

            foreach (var trait in traits.Values)
                setup.Traits.Add(BuildTrait(trait));

            return setup;
        }

        CombatUnitSpec BuildUnit(Entry entry)
        {
            var ninja = entry.Ninja;
            var unit = new CombatUnitSpec
            {
                DefinitionId = ninja.Id,
                Team = entry.Team,
                Position = entry.Position,
                Skill = BuildSkill(ninja.Skill),
            };

            foreach (var stat in stats)
                unit.Stats[stat.Id] = Fixed.FromFloat(ninja.GetBaseStat(stat));

            // Stats the ninja lists that were not in the stat list still count.
            foreach (var entryStat in ninja.BaseStats)
                if (entryStat.stat != null && !unit.Stats.ContainsKey(entryStat.stat.Id))
                    unit.Stats[entryStat.stat.Id] = Fixed.FromFloat(entryStat.value);

            foreach (var trait in ninja.Traits)
                unit.TraitIds.Add(trait.Id);

            foreach (var weapon in entry.Weapons)
            {
                if (weapon == null) continue;
                foreach (var effect in weapon.AllEffects())
                    if (effect != null) unit.Passives.Add(effect.ToSpec());
            }

            return unit;
        }

        static SkillSpec BuildSkill(SkillDefinition skill)
        {
            if (skill == null) return null;

            var spec = new SkillSpec { Id = skill.Id, CastTicks = Fixed.SecondsToTicks(skill.CastTimeSeconds) };
            foreach (var action in skill.Actions)
                if (action != null) spec.Actions.Add(action.ToSpec());
            return spec;
        }

        static TraitSpec BuildTrait(TraitDefinition trait)
        {
            var spec = new TraitSpec { Id = trait.Id };
            foreach (var tier in trait.Tiers)
            {
                if (tier == null) continue;
                var tierSpec = new TraitTierSpec { RequiredCount = tier.requiredCount };
                foreach (var effect in tier.effects)
                    if (effect != null) tierSpec.Effects.Add(effect.ToSpec());
                spec.Tiers.Add(tierSpec);
            }

            return spec;
        }

        readonly struct Entry
        {
            public readonly int Team;
            public readonly NinjaDefinition Ninja;
            public readonly HexCoord Position;
            public readonly WeaponDefinition[] Weapons;

            public Entry(int team, NinjaDefinition ninja, HexCoord position, WeaponDefinition[] weapons)
            {
                Team = team;
                Ninja = ninja;
                Position = position;
                Weapons = weapons;
            }
        }
    }
}
