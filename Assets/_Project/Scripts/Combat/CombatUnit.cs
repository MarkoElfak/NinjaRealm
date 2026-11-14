using System.Collections.Generic;

namespace NinjaRealm.Combat
{
    /// <summary>A ninja's live state during a fight. Read it for display; only the simulation changes it.</summary>
    public sealed class CombatUnit
    {
        public readonly int Index;
        public readonly int Team;
        public readonly string DefinitionId;
        public readonly IReadOnlyList<string> TraitIds;
        public readonly SkillSpec Skill;

        public HexCoord Position { get; internal set; }
        internal HexCoord PreviousPosition;
        public long Health { get; internal set; }
        public long MaxHealth { get; internal set; }
        public long Mana { get; internal set; }
        public int TargetIndex { get; internal set; } = -1;
        public bool IsCasting => CastTicksLeft >= 0;
        public bool IsAlive => Health > 0;

        // Totals for the result screen and damage log.
        public long DamageDealt { get; internal set; }
        public long DamageTaken { get; internal set; }
        public long HealingDone { get; internal set; }
        public long ShieldingDone { get; internal set; }

        internal readonly Dictionary<string, long> BaseStats;
        internal readonly List<StatModifier> Modifiers = new List<StatModifier>();
        internal readonly List<ActiveStatus> Statuses = new List<ActiveStatus>();
        internal readonly List<ActiveShield> Shields = new List<ActiveShield>();
        internal int AttackCooldown;
        internal int MoveCooldown;
        internal int CastTicksLeft = -1;

        internal CombatUnit(int index, CombatUnitSpec spec)
        {
            Index = index;
            Team = spec.Team;
            DefinitionId = spec.DefinitionId;
            TraitIds = spec.TraitIds;
            Skill = spec.Skill;
            Position = spec.Position;
            BaseStats = new Dictionary<string, long>(spec.Stats);
        }

        public bool HasTrait(string traitId)
        {
            foreach (var id in TraitIds)
                if (id == traitId) return true;
            return false;
        }

        /// <summary>Current value of a stat: base x (100% + percent bonuses) + flat bonuses.</summary>
        public long GetStat(string statId)
        {
            BaseStats.TryGetValue(statId, out var baseValue);
            long percent = 0, flat = 0;
            foreach (var modifier in Modifiers)
            {
                if (modifier.StatId != statId) continue;
                if (modifier.Operation == ModifierOperation.AddPercent) percent += modifier.Value;
                else flat += modifier.Value;
            }

            return Fixed.AddPercent(baseValue, percent) + flat;
        }

        public long ShieldTotal
        {
            get
            {
                long total = 0;
                foreach (var shield in Shields) total += shield.Amount;
                return total;
            }
        }

        public bool HasStatus(StatusKind kind)
        {
            foreach (var status in Statuses)
                if (status.Kind == kind) return true;
            return false;
        }

        /// <summary>The strongest value of a status, or 0.</summary>
        internal long StatusValue(StatusKind kind)
        {
            long best = 0;
            foreach (var status in Statuses)
                if (status.Kind == kind && status.Value > best) best = status.Value;
            return best;
        }
    }

    internal struct StatModifier
    {
        public string StatId;
        public ModifierOperation Operation;
        public long Value;

        /// <summary>Tick at which it ends, or -1 for the whole fight.</summary>
        public int EndTick;
    }

    internal sealed class ActiveStatus
    {
        public StatusKind Kind;
        public long Value;
        public int EndTick;
        public int Source;
        public long DamagePerTick;
    }

    internal sealed class ActiveShield
    {
        public long Amount;
        public int EndTick;
    }
}
