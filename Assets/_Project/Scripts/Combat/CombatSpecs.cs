using System.Collections.Generic;

namespace NinjaRealm.Combat
{
    // Plain data describing a fight. Built from content assets by NinjaRealm.Core
    // (CombatSetupBuilder) or by hand in tests. All numbers are fixed-point (see Fixed).

    public sealed class FightSetup
    {
        public readonly List<CombatUnitSpec> Units = new List<CombatUnitSpec>();
        public readonly List<TraitSpec> Traits = new List<TraitSpec>();

        public int BoardColumns = 7;

        /// <summary>Rows for both teams together. Team 0 starts on the low rows, team 1 on the high rows.</summary>
        public int BoardRows = 8;

        public int OvertimeStartTicks = 30 * Fixed.TicksPerSecond;
        public int OvertimeStepTicks = 5 * Fixed.TicksPerSecond;
        public long OvertimeStepBonusPercent = 25 * Fixed.One;
        public int MaxTicks = 45 * Fixed.TicksPerSecond;

        /// <summary>Mana gained per basic attack.</summary>
        public long ManaPerAttack = 10 * Fixed.One;

        /// <summary>Most mana gained from a single hit taken.</summary>
        public long MaxManaFromHit = 15 * Fixed.One;
    }

    public sealed class CombatUnitSpec
    {
        public string DefinitionId;
        public int Team;
        public HexCoord Position;
        public readonly Dictionary<string, long> Stats = new Dictionary<string, long>();
        public readonly List<string> TraitIds = new List<string>();
        public SkillSpec Skill;

        /// <summary>Passives the unit carries itself, for example from weapons.</summary>
        public readonly List<PassiveSpec> Passives = new List<PassiveSpec>();
    }

    public sealed class TraitSpec
    {
        public string Id;
        public readonly List<TraitTierSpec> Tiers = new List<TraitTierSpec>();

        public TraitTierSpec GetActiveTier(int count)
        {
            TraitTierSpec active = null;
            foreach (var tier in Tiers)
                if (count >= tier.RequiredCount) active = tier;
            return active;
        }
    }

    public sealed class TraitTierSpec
    {
        public int RequiredCount;
        public readonly List<PassiveSpec> Effects = new List<PassiveSpec>();
    }

    public sealed class SkillSpec
    {
        public string Id;
        public int CastTicks;
        public readonly List<ActionSpec> Actions = new List<ActionSpec>();
    }

    public abstract class PassiveSpec
    {
        public EffectScope Holders;
    }

    public sealed class StatModifierSpec : PassiveSpec
    {
        public string StatId;
        public ModifierOperation Operation;
        public long Value;
    }

    public sealed class TriggeredSpec : PassiveSpec
    {
        public EffectTrigger Trigger;
        public int IntervalTicks;
        public long HealthThresholdPercent;
        public int EveryNth = 1;
        public bool OncePerFight;
        public readonly List<ActionSpec> Actions = new List<ActionSpec>();
    }

    public struct TargetSpec
    {
        public TargetKind Kind;
        public int Count;
        public int Radius;

        public TargetSpec(TargetKind kind, int count = 1, int radius = 1)
        {
            Kind = kind;
            Count = count;
            Radius = radius;
        }
    }

    public abstract class ActionSpec
    {
        public TargetSpec Target;
    }

    public sealed class DamageSpec : ActionSpec
    {
        public DamageType DamageType;
        public long BaseDamage;
        public long AttackDamagePercent;
        public bool ScalesWithAbilityPower;
    }

    public sealed class HealSpec : ActionSpec
    {
        public long PercentOfMaxHealth;
    }

    public sealed class ShieldSpec : ActionSpec
    {
        public long PercentOfMaxHealth;
        public int DurationTicks;
    }

    public sealed class StatusSpec : ActionSpec
    {
        public StatusKind Status;
        public long Value;
        public int DurationTicks;
    }

    public sealed class StatBuffSpec : ActionSpec
    {
        public string StatId;
        public ModifierOperation Operation;
        public long Value;
        public int DurationTicks;
    }
}
