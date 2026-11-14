using System.Collections.Generic;
using NinjaRealm.Core.Content;
using NinjaRealm.Core.Definitions.Effects;
using UnityEngine;

namespace NinjaRealm.Core.Definitions
{
    /// <summary>
    /// A weapon a ninja can hold. Components drop from battles; a combined weapon names the
    /// two components it is forged from and gets their effects plus its own.
    /// </summary>
    [CreateAssetMenu(menuName = "Ninja Realm/Weapon", fileName = "NewWeapon")]
    public sealed class WeaponDefinition : ContentDefinition
    {
        [SerializeField] WeaponTier tier;

        [SerializeField, Tooltip("Combined weapons only: the two components it is forged from.")]
        WeaponDefinition componentA;

        [SerializeField] WeaponDefinition componentB;

        [SerializeField, TextArea] string description;

        [SerializeReference, SubclassSelector, Tooltip("For combined weapons, only the extra effects on top of both components.")]
        List<PassiveEffect> effects = new List<PassiveEffect>();

        [SerializeField] Sprite icon;
        [SerializeField] GameObject modelPrefab;

        public override string IdCategory => "weapon";

        public WeaponTier Tier => tier;

        public WeaponDefinition ComponentA => componentA;

        public WeaponDefinition ComponentB => componentB;

        public string Description => description;

        public IReadOnlyList<PassiveEffect> Effects => effects;

        public Sprite Icon => icon;

        public GameObject ModelPrefab => modelPrefab;

        /// <summary>Every effect the holder gets: both components' effects, then this weapon's own.</summary>
        public IEnumerable<PassiveEffect> AllEffects()
        {
            if (tier == WeaponTier.Combined)
            {
                if (componentA != null)
                    foreach (var effect in componentA.effects) yield return effect;
                if (componentB != null)
                    foreach (var effect in componentB.effects) yield return effect;
            }

            foreach (var effect in effects) yield return effect;
        }

        public override IEnumerable<string> ValidateSelf()
        {
            if (tier == WeaponTier.Component)
            {
                if (componentA != null || componentB != null)
                    yield return "is a Component but lists components. Only Combined weapons are forged from components.";
            }
            else
            {
                if (componentA == null || componentB == null)
                    yield return "is Combined but is missing one of its two components.";
                if ((componentA != null && componentA.tier != WeaponTier.Component) ||
                    (componentB != null && componentB.tier != WeaponTier.Component))
                    yield return "is forged from a weapon that is not a Component.";
            }

            if (effects.Count == 0)
                yield return "has no effects.";

            foreach (var message in EffectValidation.ValidateList(effects, "effect"))
                yield return message;

            foreach (var effect in effects)
            {
                if (effect != null && effect.holders != EffectScope.Self)
                    yield return "has an effect not held by Self. Weapon effects apply to the ninja holding the weapon.";
            }
        }

        internal void Configure(WeaponTier tier, string description, WeaponDefinition componentA,
            WeaponDefinition componentB, params PassiveEffect[] effects)
        {
            this.tier = tier;
            this.description = description;
            this.componentA = componentA;
            this.componentB = componentB;
            this.effects = new List<PassiveEffect>(effects);
        }
    }
}
