using System;
using System.Collections.Generic;
using NinjaRealm.Combat;
using NinjaRealm.Core.Content;
using NinjaRealm.Core.Definitions.Effects;
using UnityEngine;

namespace NinjaRealm.Core.Definitions
{
    /// <summary>
    /// An element (Fire, Water...) or class (Vanguard, Medic...). When enough ninjas on the
    /// board share a trait, the highest tier they reach switches on.
    /// </summary>
    [CreateAssetMenu(menuName = "Ninja Realm/Trait", fileName = "NewTrait")]
    public sealed class TraitDefinition : ContentDefinition
    {
        [SerializeField] TraitKind kind;
        [SerializeField] Color color = Color.white;
        [SerializeField] Sprite icon;

        [SerializeField, Tooltip("Ordered from the lowest to the highest required count.")]
        List<TraitTier> tiers = new List<TraitTier>();

        public override string IdCategory => "trait";

        public TraitKind Kind => kind;

        public Color Color => color;

        public Sprite Icon => icon;

        public IReadOnlyList<TraitTier> Tiers => tiers;

        /// <summary>The highest tier reached with this many ninjas, or null if none is reached.</summary>
        public TraitTier GetActiveTier(int count)
        {
            TraitTier active = null;
            foreach (var tier in tiers)
            {
                if (tier != null && count >= tier.requiredCount) active = tier;
            }

            return active;
        }

        public override IEnumerable<string> ValidateSelf()
        {
            if (tiers.Count == 0)
            {
                yield return "has no tiers.";
                yield break;
            }

            var previous = 0;
            for (var i = 0; i < tiers.Count; i++)
            {
                var tier = tiers[i];
                if (tier == null)
                {
                    yield return $"tier {i + 1} is empty.";
                    continue;
                }

                if (tier.requiredCount < 1)
                    yield return $"tier {i + 1} needs a required count of at least 1.";
                else if (tier.requiredCount <= previous)
                    yield return $"tier {i + 1} needs more ninjas than the tier before it.";

                previous = Math.Max(previous, tier.requiredCount);

                if (tier.effects.Count == 0)
                    yield return $"tier {i + 1} has no effects.";

                foreach (var message in EffectValidation.ValidateList(tier.effects, $"tier {i + 1}"))
                    yield return message;

                foreach (var effect in tier.effects)
                {
                    if (effect != null && effect.holders == EffectScope.Self)
                        yield return $"tier {i + 1} has an effect held by Self. Use TraitMembers, AllAllies, OtherAllies or Team for trait effects.";
                }
            }
        }

        internal void Configure(TraitKind kind, Color color, IEnumerable<TraitTier> tiers)
        {
            this.kind = kind;
            this.color = color;
            this.tiers = new List<TraitTier>(tiers);
        }
    }

    [Serializable]
    public sealed class TraitTier
    {
        [Min(1)] public int requiredCount = 2;

        [TextArea] public string description;

        [SerializeReference, SubclassSelector]
        public List<PassiveEffect> effects = new List<PassiveEffect>();

        public TraitTier()
        {
        }

        public TraitTier(int requiredCount, string description, params PassiveEffect[] effects)
        {
            this.requiredCount = requiredCount;
            this.description = description;
            this.effects = new List<PassiveEffect>(effects);
        }
    }
}
