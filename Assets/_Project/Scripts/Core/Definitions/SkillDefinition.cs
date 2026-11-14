using System.Collections.Generic;
using NinjaRealm.Core.Content;
using NinjaRealm.Core.Definitions.Effects;
using UnityEngine;

namespace NinjaRealm.Core.Definitions
{
    /// <summary>A ninja's active skill. It fires when the ninja's mana is full.</summary>
    [CreateAssetMenu(menuName = "Ninja Realm/Skill", fileName = "NewSkill")]
    public sealed class SkillDefinition : ContentDefinition
    {
        [SerializeField, TextArea] string description;

        [SerializeField, Min(0), Tooltip("Seconds the ninja is busy casting before the actions happen.")]
        float castTimeSeconds = 0.5f;

        [SerializeReference, SubclassSelector]
        List<ActionEffect> actions = new List<ActionEffect>();

        public override string IdCategory => "skill";

        public string Description => description;

        public float CastTimeSeconds => castTimeSeconds;

        public IReadOnlyList<ActionEffect> Actions => actions;

        public override IEnumerable<string> ValidateSelf()
        {
            if (actions.Count == 0)
                yield return "has no actions.";

            foreach (var message in EffectValidation.ValidateList(actions, "action"))
                yield return message;
        }

        internal void Configure(string description, float castTimeSeconds, params ActionEffect[] actions)
        {
            this.description = description;
            this.castTimeSeconds = castTimeSeconds;
            this.actions = new List<ActionEffect>(actions);
        }
    }
}
