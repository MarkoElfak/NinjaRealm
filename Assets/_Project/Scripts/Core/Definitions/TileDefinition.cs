using System;
using System.Collections.Generic;
using System.Text;
using NinjaRealm.Combat;
using NinjaRealm.Core.Content;
using NinjaRealm.Core.Definitions.Effects;
using UnityEngine;

namespace NinjaRealm.Core.Definitions
{
    /// <summary>Which rows of a team's half a tile may lie on.</summary>
    public enum TileRows
    {
        Any,

        /// <summary>The two back rows (rows 0 and 1 from the back).</summary>
        BackTwo,

        /// <summary>Only the front row.</summary>
        Front,
    }

    /// <summary>
    /// What a tile does to one kind of ninja. A ninja with any of the traits gets the effects;
    /// with no traits listed, every ninja does. A harmful part is shown in red.
    /// </summary>
    [Serializable]
    public sealed class TilePart
    {
        [Tooltip("Ninjas with any of these traits get this part. Empty means every ninja.")]
        public List<TraitDefinition> traits = new List<TraitDefinition>();

        [Tooltip("Shown in red as a drawback rather than in green as a bonus.")]
        public bool harmful;

        [SerializeReference, SubclassSelector, Tooltip("Passives the ninja carries for the whole fight. Use holders Self.")]
        public List<PassiveEffect> effects = new List<PassiveEffect>();

        [Tooltip("Shown instead of the effects' own text, for effects that can't describe themselves well (triggers).")]
        public string text;

        public TilePart()
        {
        }

        public TilePart(bool harmful, IEnumerable<TraitDefinition> traits, string text, params PassiveEffect[] effects)
        {
            this.harmful = harmful;
            this.traits = traits != null ? new List<TraitDefinition>(traits) : new List<TraitDefinition>();
            this.text = text;
            this.effects = new List<PassiveEffect>(effects);
        }

        public bool ForEveryone => traits.Count == 0;

        public bool AppliesTo(NinjaDefinition ninja)
        {
            if (ninja == null) return false;
            if (traits.Count == 0) return true;
            foreach (var trait in ninja.Traits)
                if (traits.Contains(trait)) return true;
            return false;
        }

        /// <summary>The effect text, such as "+20% Attack Damage".</summary>
        public string Describe()
        {
            if (!string.IsNullOrEmpty(text)) return text;
            var parts = new List<string>();
            foreach (var effect in effects)
                if (effect != null) parts.Add(effect.Describe());
            return string.Join(", ", parts);
        }

        /// <summary>Who gets it, such as "Fire" or "Mystic and Medic", or "Everyone".</summary>
        public string Who()
        {
            if (traits.Count == 0) return "Everyone";
            var names = new List<string>();
            foreach (var trait in traits)
                if (trait != null) names.Add(trait.DisplayName);
            return string.Join(" and ", names);
        }
    }

    /// <summary>
    /// A special hex a stage puts on the board. The ninja that starts the fight on it carries its
    /// effects for the whole fight, even after it moves off. Some tiles help one kind of ninja and
    /// hurt another; others give whoever stands there a bonus and a drawback together.
    /// </summary>
    [CreateAssetMenu(menuName = "Ninja Realm/Tile", fileName = "NewTile")]
    public sealed class TileDefinition : ContentDefinition
    {
        [SerializeField] Color color = Color.white;

        [SerializeField, TextArea] string description;

        [SerializeField, Tooltip("Where on a team's half a stage may put this tile.")]
        TileRows rows;

        [SerializeField] List<TilePart> parts = new List<TilePart>();

        public override string IdCategory => "tile";

        public Color Color => color;

        public string Description => description;

        public TileRows Rows => rows;

        public IReadOnlyList<TilePart> Parts => parts;

        /// <summary>True when every part applies to every ninja: a bonus and a drawback for whoever stands here.</summary>
        public bool IsTradeOff
        {
            get
            {
                foreach (var part in parts)
                    if (part != null && !part.ForEveryone) return false;
                return parts.Count > 0;
            }
        }

        /// <summary>True when the tile may lie on this row of a team's half (0 is the back row).</summary>
        public bool AllowsRow(int rowFromBack, int rowsPerTeam)
        {
            switch (rows)
            {
                case TileRows.BackTwo: return rowFromBack >= 0 && rowFromBack < 2;
                case TileRows.Front: return rowFromBack == rowsPerTeam - 1;
                default: return true;
            }
        }

        /// <summary>The parts this ninja gets, in order.</summary>
        public IEnumerable<TilePart> PartsFor(NinjaDefinition ninja)
        {
            foreach (var part in parts)
                if (part != null && part.AppliesTo(ninja)) yield return part;
        }

        /// <summary>Every passive this ninja carries from standing here, in order.</summary>
        public IEnumerable<PassiveEffect> PassivesFor(NinjaDefinition ninja)
        {
            foreach (var part in PartsFor(ninja))
                foreach (var effect in part.effects)
                    if (effect != null) yield return effect;
        }

        /// <summary>Bonuses minus drawbacks this ninja gets here: above 0 is good for it, below 0 bad.</summary>
        public int Score(NinjaDefinition ninja)
        {
            var score = 0;
            foreach (var part in PartsFor(ninja))
                score += part.harmful ? -1 : 1;
            return score;
        }

        /// <summary>What the tile does to this ninja as rich text, green bonuses and red drawbacks. Null when nothing.</summary>
        public string DescribeFor(NinjaDefinition ninja)
        {
            var text = new StringBuilder();
            foreach (var part in PartsFor(ninja))
            {
                if (text.Length > 0) text.Append(", ");
                text.Append(Colored(part, part.Describe()));
            }

            return text.Length > 0 ? text.ToString() : null;
        }

        /// <summary>Every part as rich text, such as "Fire: +20% Attack Damage. Earth: -20 Armor".</summary>
        public string DescribeAll()
        {
            var text = new StringBuilder();
            foreach (var part in parts)
            {
                if (part == null) continue;
                if (text.Length > 0) text.Append(". ");
                text.Append(part.Who()).Append(": ").Append(Colored(part, part.Describe()));
            }

            return text.ToString();
        }

        public const string BonusColor = "#7ee08a";
        public const string DrawbackColor = "#ff7a6b";

        static string Colored(TilePart part, string text) =>
            $"<color={(part.harmful ? DrawbackColor : BonusColor)}>{text}</color>";

        public override IEnumerable<string> ValidateSelf()
        {
            if (parts.Count == 0) yield return "has no parts.";

            for (var i = 0; i < parts.Count; i++)
            {
                var part = parts[i];
                var label = $"part {i + 1}";
                if (part == null)
                {
                    yield return $"{label} is empty.";
                    continue;
                }

                if (part.effects.Count == 0) yield return $"{label} has no effects.";
                foreach (var trait in part.traits)
                    if (trait == null) yield return $"{label} has an empty trait.";

                foreach (var message in EffectValidation.ValidateList(part.effects, label + " effect"))
                    yield return message;

                foreach (var effect in part.effects)
                    if (effect != null && effect.holders != EffectScope.Self)
                        yield return $"{label} has an effect not held by Self. A tile only affects the ninja standing on it.";
            }
        }

        internal void Configure(Color color, string description, TileRows rows, params TilePart[] parts)
        {
            this.color = color;
            this.description = description;
            this.rows = rows;
            this.parts = new List<TilePart>(parts);
        }
    }
}
