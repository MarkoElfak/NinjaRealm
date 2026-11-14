using System;
using System.Collections.Generic;
using NinjaRealm.Combat;
using UnityEngine;

namespace NinjaRealm.Core.Definitions.Effects
{
    /// <summary>
    /// Base for every effect building block. Effects are plain serialized data stored with
    /// [SerializeReference]; the combat simulation reads them and decides what they do.
    /// A new kind of effect is one new subclass here plus its handling in combat.
    /// </summary>
    [Serializable]
    public abstract class EffectData
    {
        /// <summary>Problems with this effect's settings, one message per problem.</summary>
        public virtual IEnumerable<string> Validate()
        {
            yield break;
        }

        /// <summary>Short text for tooltips and validation messages.</summary>
        public abstract string Describe();
    }

    /// <summary>An always-on or triggered effect owned by a trait, a weapon or a ninja.</summary>
    [Serializable]
    public abstract class PassiveEffect : EffectData
    {
        [Tooltip("Who carries this effect. Weapons use Self; traits usually use TraitMembers or Team.")]
        public EffectScope holders = EffectScope.Self;

        /// <summary>The plain combat data for this effect, with values in fixed-point.</summary>
        public abstract PassiveSpec ToSpec();
    }

    /// <summary>Something that happens once: damage, a heal, a shield, a status. Used by skills and triggers.</summary>
    [Serializable]
    public abstract class ActionEffect : EffectData
    {
        public TargetSelector target = new TargetSelector();

        /// <summary>The plain combat data for this action, with values in fixed-point.</summary>
        public abstract ActionSpec ToSpec();

        protected TargetSpec TargetToSpec() =>
            target != null ? new TargetSpec(target.kind, target.count, target.radius) : new TargetSpec(TargetKind.CurrentTarget);

        public override IEnumerable<string> Validate() => ValidateTarget();

        protected IEnumerable<string> ValidateTarget()
        {
            if (target == null)
            {
                yield return "has no target.";
                yield break;
            }

            foreach (var message in target.Validate())
                yield return message;
        }
    }

    [Serializable]
    public sealed class TargetSelector
    {
        public TargetKind kind = TargetKind.CurrentTarget;

        [Min(1), Tooltip("How many targets, for LowestHealthAllies and RandomEnemies.")]
        public int count = 1;

        [Min(0), Tooltip("Radius in hexes, for the InRadius kinds.")]
        public int radius = 1;

        public TargetSelector()
        {
        }

        public TargetSelector(TargetKind kind, int count = 1, int radius = 1)
        {
            this.kind = kind;
            this.count = count;
            this.radius = radius;
        }

        public IEnumerable<string> Validate()
        {
            if ((kind == TargetKind.LowestHealthAllies || kind == TargetKind.RandomEnemies) && count < 1)
                yield return $"targets {kind} with a count below 1.";
            if ((kind == TargetKind.EnemiesInRadiusOfTarget || kind == TargetKind.EnemiesInRadiusOfSelf) && radius < 1)
                yield return $"targets {kind} with a radius below 1.";
        }

        public string Describe()
        {
            switch (kind)
            {
                case TargetKind.LowestHealthAllies: return $"{count} lowest health {(count == 1 ? "ally" : "allies")}";
                case TargetKind.RandomEnemies: return $"{count} random {(count == 1 ? "enemy" : "enemies")}";
                case TargetKind.EnemiesInRadiusOfTarget: return $"enemies within {radius} hex of the target";
                case TargetKind.EnemiesInRadiusOfSelf: return $"enemies within {radius} hex";
                default: return kind.ToString();
            }
        }
    }

    public static class EffectValidation
    {
        /// <summary>Checks a list of effects for empty slots and invalid settings.</summary>
        public static IEnumerable<string> ValidateList<T>(IEnumerable<T> effects, string label) where T : EffectData
        {
            var index = 0;
            foreach (var effect in effects)
            {
                index++;
                if (effect == null)
                {
                    yield return $"{label} {index} is empty. Pick an effect type or remove it.";
                    continue;
                }

                foreach (var message in effect.Validate())
                    yield return $"{label} {index} ({effect.GetType().Name}) {message}";
            }
        }
    }
}
