using System;
using System.Collections.Generic;
using NinjaRealm.Combat;
using UnityEngine;

namespace NinjaRealm.Core.Definitions.Effects
{
    /// <summary>Deals damage: baseDamage plus a percentage of attack damage, optionally scaled by ability power.</summary>
    [Serializable]
    public sealed class DamageAction : ActionEffect
    {
        public DamageType damageType = DamageType.Physical;

        [Min(0)] public float baseDamage;

        [Min(0), Tooltip("Percentage of the caster's attack damage, for example 180.")]
        public float attackDamagePercent;

        [Tooltip("Multiply the damage by ability power / 100.")]
        public bool scalesWithAbilityPower;

        public DamageAction()
        {
        }

        public DamageAction(TargetSelector target, DamageType damageType, float baseDamage, float attackDamagePercent, bool scalesWithAbilityPower)
        {
            this.target = target;
            this.damageType = damageType;
            this.baseDamage = baseDamage;
            this.attackDamagePercent = attackDamagePercent;
            this.scalesWithAbilityPower = scalesWithAbilityPower;
        }

        public override IEnumerable<string> Validate()
        {
            foreach (var message in ValidateTarget()) yield return message;
            if (baseDamage <= 0f && attackDamagePercent <= 0f) yield return "deals no damage.";
        }

        public override string Describe()
        {
            var amount = attackDamagePercent > 0f ? $"{attackDamagePercent:0}% attack damage" : $"{baseDamage:0}";
            if (attackDamagePercent > 0f && baseDamage > 0f) amount = $"{baseDamage:0} + {attackDamagePercent:0}% attack damage";
            return $"{amount} {damageType} damage to {target?.Describe()}";
        }

        public override ActionSpec ToSpec() => new DamageSpec
        {
            Target = TargetToSpec(),
            DamageType = damageType,
            BaseDamage = Fixed.FromFloat(baseDamage),
            AttackDamagePercent = Fixed.FromFloat(attackDamagePercent),
            ScalesWithAbilityPower = scalesWithAbilityPower,
        };
    }

    /// <summary>Heals a percentage of each target's max health.</summary>
    [Serializable]
    public sealed class HealAction : ActionEffect
    {
        [Min(0)] public float percentOfMaxHealth;

        public HealAction()
        {
        }

        public HealAction(TargetSelector target, float percentOfMaxHealth)
        {
            this.target = target;
            this.percentOfMaxHealth = percentOfMaxHealth;
        }

        public override IEnumerable<string> Validate()
        {
            foreach (var message in ValidateTarget()) yield return message;
            if (percentOfMaxHealth <= 0f) yield return "heals 0%.";
        }

        public override string Describe() => $"Heal {target?.Describe()} for {percentOfMaxHealth:0.#}% max health";

        public override ActionSpec ToSpec() => new HealSpec
        {
            Target = TargetToSpec(),
            PercentOfMaxHealth = Fixed.FromFloat(percentOfMaxHealth),
        };
    }

    /// <summary>Gives each target a shield worth a percentage of their max health.</summary>
    [Serializable]
    public sealed class ShieldAction : ActionEffect
    {
        [Min(0)] public float percentOfMaxHealth;
        [Min(0)] public float durationSeconds = 4f;

        public ShieldAction()
        {
        }

        public ShieldAction(TargetSelector target, float percentOfMaxHealth, float durationSeconds)
        {
            this.target = target;
            this.percentOfMaxHealth = percentOfMaxHealth;
            this.durationSeconds = durationSeconds;
        }

        public override IEnumerable<string> Validate()
        {
            foreach (var message in ValidateTarget()) yield return message;
            if (percentOfMaxHealth <= 0f) yield return "shields 0%.";
            if (durationSeconds <= 0f) yield return "has a duration of 0.";
        }

        public override string Describe() => $"Shield {target?.Describe()} for {percentOfMaxHealth:0.#}% max health, {durationSeconds:0.#}s";

        public override ActionSpec ToSpec() => new ShieldSpec
        {
            Target = TargetToSpec(),
            PercentOfMaxHealth = Fixed.FromFloat(percentOfMaxHealth),
            DurationTicks = Fixed.SecondsToTicks(durationSeconds),
        };
    }

    /// <summary>Applies a status such as stun, burn or slow.</summary>
    [Serializable]
    public sealed class ApplyStatusAction : ActionEffect
    {
        public StatusKind status;

        [Tooltip("Burn: % max health over the duration. Slow, HealingReduction, DamageReduction: %. Unused for Stun and Taunt.")]
        public float value;

        [Min(0)] public float durationSeconds = 2f;

        public ApplyStatusAction()
        {
        }

        public ApplyStatusAction(TargetSelector target, StatusKind status, float value, float durationSeconds)
        {
            this.target = target;
            this.status = status;
            this.value = value;
            this.durationSeconds = durationSeconds;
        }

        public static bool NeedsValue(StatusKind status) => status != StatusKind.Stun && status != StatusKind.Taunt;

        public override IEnumerable<string> Validate()
        {
            foreach (var message in ValidateTarget()) yield return message;
            if (durationSeconds <= 0f) yield return "has a duration of 0.";
            if (NeedsValue(status) && value <= 0f) yield return $"applies {status} with a value of 0.";
        }

        public override string Describe() =>
            NeedsValue(status)
                ? $"{status} {value:0.#}% on {target?.Describe()} for {durationSeconds:0.#}s"
                : $"{status} {target?.Describe()} for {durationSeconds:0.#}s";

        public override ActionSpec ToSpec() => new StatusSpec
        {
            Target = TargetToSpec(),
            Status = status,
            Value = Fixed.FromFloat(value),
            DurationTicks = Fixed.SecondsToTicks(durationSeconds),
        };
    }

    /// <summary>Changes a stat for a limited time.</summary>
    [Serializable]
    public sealed class StatBuffAction : ActionEffect
    {
        public StatDefinition stat;
        public ModifierOperation operation;
        public float value;
        [Min(0)] public float durationSeconds = 4f;

        public StatBuffAction()
        {
        }

        public StatBuffAction(TargetSelector target, StatDefinition stat, ModifierOperation operation, float value, float durationSeconds)
        {
            this.target = target;
            this.stat = stat;
            this.operation = operation;
            this.value = value;
            this.durationSeconds = durationSeconds;
        }

        public override IEnumerable<string> Validate()
        {
            foreach (var message in ValidateTarget()) yield return message;
            if (stat == null) yield return "has no stat picked.";
            if (Mathf.Approximately(value, 0f)) yield return "has a value of 0.";
            if (durationSeconds <= 0f) yield return "has a duration of 0.";
        }

        public override string Describe()
        {
            var percent = operation == ModifierOperation.AddPercent ? "%" : "";
            return $"{value:+0.##;-0.##}{percent} {(stat != null ? stat.DisplayName : "?")} to {target?.Describe()} for {durationSeconds:0.#}s";
        }

        public override ActionSpec ToSpec() => new StatBuffSpec
        {
            Target = TargetToSpec(),
            StatId = stat != null ? stat.Id : null,
            Operation = operation,
            Value = Fixed.FromFloat(value),
            DurationTicks = Fixed.SecondsToTicks(durationSeconds),
        };
    }
}
