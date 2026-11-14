using System;
using System.Collections.Generic;
using NinjaRealm.Combat;
using UnityEngine;

namespace NinjaRealm.Core.Definitions.Effects
{
    /// <summary>Changes a stat for as long as the effect is active, for example +20 armor.</summary>
    [Serializable]
    public sealed class StatModifierEffect : PassiveEffect
    {
        public StatDefinition stat;
        public ModifierOperation operation;
        public float value;

        public StatModifierEffect()
        {
        }

        public StatModifierEffect(StatDefinition stat, ModifierOperation operation, float value, EffectScope holders = EffectScope.Self)
        {
            this.stat = stat;
            this.operation = operation;
            this.value = value;
            this.holders = holders;
        }

        public override IEnumerable<string> Validate()
        {
            if (stat == null) yield return "has no stat picked.";
            if (Mathf.Approximately(value, 0f)) yield return "has a value of 0.";
        }

        public override string Describe()
        {
            var name = stat != null ? stat.DisplayName : "?";
            var percent = operation == ModifierOperation.AddPercent || (stat != null && stat.IsPercentage) ? "%" : "";
            return $"{value:+0.##;-0.##}{percent} {name}";
        }

        public override PassiveSpec ToSpec() => new StatModifierSpec
        {
            Holders = holders,
            StatId = stat != null ? stat.Id : null,
            Operation = operation,
            Value = Fixed.FromFloat(value),
        };
    }

    /// <summary>Runs actions when something happens: fight start, an attack hit, a timer, low health.</summary>
    [Serializable]
    public sealed class TriggeredEffect : PassiveEffect
    {
        public EffectTrigger trigger;

        [Min(0), Tooltip("Periodic only: seconds between runs.")]
        public float intervalSeconds = 4f;

        [Range(1, 99), Tooltip("HealthBelowThreshold only: runs when health drops below this percentage.")]
        public float healthThresholdPercent = 40f;

        [Min(1), Tooltip("Runs on every Nth trigger, for example 3 for every third attack.")]
        public int everyNth = 1;

        [Tooltip("Runs at most once per fight.")]
        public bool oncePerFight;

        [SerializeReference, SubclassSelector]
        public List<ActionEffect> actions = new List<ActionEffect>();

        public TriggeredEffect()
        {
        }

        public TriggeredEffect(EffectTrigger trigger, EffectScope holders, params ActionEffect[] actions)
        {
            this.trigger = trigger;
            this.holders = holders;
            this.actions = new List<ActionEffect>(actions);
        }

        public override IEnumerable<string> Validate()
        {
            if (actions.Count == 0) yield return "has no actions.";
            if (trigger == EffectTrigger.Periodic && intervalSeconds <= 0f) yield return "is Periodic but its interval is 0.";
            if (everyNth < 1) yield return "has everyNth below 1.";

            foreach (var message in EffectValidation.ValidateList(actions, "action"))
                yield return message;
        }

        public override string Describe()
        {
            switch (trigger)
            {
                case EffectTrigger.Periodic: return $"Every {intervalSeconds:0.##}s: {actions.Count} action(s)";
                case EffectTrigger.HealthBelowThreshold: return $"Below {healthThresholdPercent:0}% health: {actions.Count} action(s)";
                default: return $"{trigger}: {actions.Count} action(s)";
            }
        }

        public override PassiveSpec ToSpec()
        {
            var spec = new TriggeredSpec
            {
                Holders = holders,
                Trigger = trigger,
                IntervalTicks = Fixed.SecondsToTicks(intervalSeconds),
                HealthThresholdPercent = Fixed.FromFloat(healthThresholdPercent),
                EveryNth = Math.Max(1, everyNth),
                OncePerFight = oncePerFight,
            };
            foreach (var action in actions)
                if (action != null) spec.Actions.Add(action.ToSpec());
            return spec;
        }
    }
}
