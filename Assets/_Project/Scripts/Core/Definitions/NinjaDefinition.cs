using System.Collections.Generic;
using NinjaRealm.Core.Content;
using UnityEngine;

namespace NinjaRealm.Core.Definitions
{
    /// <summary>A recruitable ninja: one element, one class, base stats and a skill.</summary>
    [CreateAssetMenu(menuName = "Ninja Realm/Ninja", fileName = "NewNinja")]
    public sealed class NinjaDefinition : ContentDefinition
    {
        [SerializeField] Rarity rarity;
        [SerializeField] TraitDefinition element;
        [SerializeField] TraitDefinition ninjaClass;
        [SerializeField] SkillDefinition skill;

        [SerializeField, Tooltip("Only stats that differ from the stat's default need to be listed.")]
        List<StatValue> baseStats = new List<StatValue>();

        [SerializeField] Sprite portrait;
        [SerializeField] GameObject modelPrefab;

        public override string IdCategory => "ninja";

        public Rarity Rarity => rarity;

        public TraitDefinition Element => element;

        public TraitDefinition Class => ninjaClass;

        public SkillDefinition Skill => skill;

        public IReadOnlyList<StatValue> BaseStats => baseStats;

        public Sprite Portrait => portrait;

        public GameObject ModelPrefab => modelPrefab;

        /// <summary>All traits this ninja counts toward, in a fixed order.</summary>
        public IEnumerable<TraitDefinition> Traits
        {
            get
            {
                if (element != null) yield return element;
                if (ninjaClass != null) yield return ninjaClass;
            }
        }

        /// <summary>The listed base value for a stat, or the stat's default.</summary>
        public float GetBaseStat(StatDefinition stat)
        {
            foreach (var entry in baseStats)
            {
                if (entry.stat == stat) return entry.value;
            }

            return stat != null ? stat.DefaultValue : 0f;
        }

        public override IEnumerable<string> ValidateSelf()
        {
            if (element == null)
                yield return "has no element.";
            else if (element.Kind != TraitKind.Element)
                yield return $"element '{element.Id}' is a {element.Kind} trait, not an Element.";

            if (ninjaClass == null)
                yield return "has no class.";
            else if (ninjaClass.Kind != TraitKind.Class)
                yield return $"class '{ninjaClass.Id}' is a {ninjaClass.Kind} trait, not a Class.";

            if (skill == null)
                yield return "has no skill.";

            var seen = new HashSet<StatDefinition>();
            foreach (var entry in baseStats)
            {
                if (entry.stat == null)
                    yield return "has a base stat with no stat picked.";
                else if (!seen.Add(entry.stat))
                    yield return $"lists stat '{entry.stat.Id}' more than once.";
            }
        }

        internal void Configure(Rarity rarity, TraitDefinition element, TraitDefinition ninjaClass,
            SkillDefinition skill, IEnumerable<StatValue> baseStats)
        {
            this.rarity = rarity;
            this.element = element;
            this.ninjaClass = ninjaClass;
            this.skill = skill;
            this.baseStats = new List<StatValue>(baseStats);
        }
    }
}
