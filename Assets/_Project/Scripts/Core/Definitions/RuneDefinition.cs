using System;
using System.Collections.Generic;
using NinjaRealm.Core.Content;
using NinjaRealm.Core.Definitions.Effects;
using UnityEngine;

namespace NinjaRealm.Core.Definitions
{
    /// <summary>The five kinds of rune, for the Shrine's lists and the stone's glyph colour.</summary>
    public enum RuneFamily
    {
        /// <summary>Damage: attack damage, attack speed, crits.</summary>
        Blade,

        /// <summary>Survival: health, armor, magic resist, dodge.</summary>
        Shell,

        /// <summary>Skills: mana, ability damage, healing, omnivamp.</summary>
        Spirit,

        /// <summary>A bonus for the ninjas of one element only.</summary>
        Seal,

        /// <summary>Economy: Gold and clan XP from fights, building income.</summary>
        Fortune,
    }

    /// <summary>What a rune gives at one rank. Combat effects go on every ninja the rune covers; the rest are economy bonuses.</summary>
    [Serializable]
    public sealed class RuneRank
    {
        [SerializeReference, SubclassSelector, Tooltip("Passives every covered ninja carries in a fight. Use holders Self.")]
        public List<PassiveEffect> effects = new List<PassiveEffect>();

        [Tooltip("Extra Gold from a fight's normal pay, in basis points (500 = +5%). First win Gold is not raised.")]
        public int fightGold;

        [Tooltip("Extra clan XP from fights, in basis points.")]
        public int clanXp;

        [Tooltip("Extra per-hour output of the Mint and the Steel Mine, in basis points.")]
        public int income;

        [Tooltip("Shown instead of the effects' own text, for effects that can't describe themselves well (triggers).")]
        public string text;

        public RuneRank()
        {
        }

        public RuneRank(params PassiveEffect[] effects)
        {
            this.effects = new List<PassiveEffect>(effects);
        }

        public bool IsEmpty => effects.Count == 0 && fightGold <= 0 && clanXp <= 0 && income <= 0;
    }

    /// <summary>
    /// A rune the Shrine sells. Set into one of the altar's slots, it gives every ninja in the
    /// lineup (or, for a Seal, every ninja of its element) its current rank's passives. Ranks are
    /// a list, rank I first, so a rune with more or fewer ranks is a content change.
    /// The icon is loaded by name from Resources/RuneIcons (see <see cref="IconName"/>) unless one is linked.
    /// </summary>
    [CreateAssetMenu(menuName = "Ninja Realm/Rune", fileName = "NewRune")]
    public sealed class RuneDefinition : ContentDefinition
    {
        public const int MaxRanks = 5;

        [SerializeField] Rarity rarity;
        [SerializeField] RuneFamily family;

        [SerializeField, Tooltip("Seals only: the element whose ninjas the rune covers. Empty for every other family.")]
        TraitDefinition element;

        [SerializeField, TextArea] string description;

        [SerializeField, Tooltip("The glyph's glow, on the altar stone and in the Shrine.")]
        Color color = Color.white;

        [SerializeField, Tooltip("Optional. Without one, the icon comes from Resources/RuneIcons by the rune's name.")]
        Sprite icon;

        [SerializeField, Tooltip("Rank I first.")]
        List<RuneRank> ranks = new List<RuneRank>();

        public override string IdCategory => "rune";

        public Rarity Rarity => rarity;

        public RuneFamily Family => family;

        /// <summary>The element a Seal covers, or null when the rune covers every ninja.</summary>
        public TraitDefinition Element => element;

        public string Description => description;

        public Color Color => color;

        public Sprite Icon => icon;

        /// <summary>The icon's file name in Resources/RuneIcons: the ID without "rune.", such as ember_fang.</summary>
        public string IconName => Id != null && Id.StartsWith("rune.", StringComparison.Ordinal) ? Id.Substring(5) : Id;

        public IReadOnlyList<RuneRank> Ranks => ranks;

        public int MaxRank => ranks.Count;

        /// <summary>The rank, 1 to <see cref="MaxRank"/>, or null.</summary>
        public RuneRank Rank(int rank) => rank >= 1 && rank <= ranks.Count ? ranks[rank - 1] : null;

        /// <summary>True when the rune's bonus applies to this ninja.</summary>
        public bool Covers(NinjaDefinition ninja) => ninja != null && (element == null || ninja.Element == element);

        /// <summary>The IDs of every stat this rune changes, at any rank. Two set runes may not share one.</summary>
        public HashSet<string> RaisedStats()
        {
            var stats = new HashSet<string>();
            foreach (var rank in ranks)
                if (rank != null)
                    foreach (var effect in rank.effects)
                        if (effect is StatModifierEffect modifier && modifier.stat != null) stats.Add(modifier.stat.Id);
            return stats;
        }

        /// <summary>What a rank gives, such as "+3% Attack Damage" or "Fire ninjas: +5% Attack Damage".</summary>
        public string Describe(int rank)
        {
            var data = Rank(rank);
            if (data == null) return string.Empty;

            string text;
            if (!string.IsNullOrEmpty(data.text))
            {
                text = data.text;
            }
            else
            {
                var parts = new List<string>();
                foreach (var effect in data.effects)
                    if (effect != null) parts.Add(effect.Describe());
                if (data.fightGold > 0) parts.Add($"+{Percent(data.fightGold)}% Gold from fights");
                if (data.clanXp > 0) parts.Add($"+{Percent(data.clanXp)}% clan XP from fights");
                if (data.income > 0) parts.Add($"+{Percent(data.income)}% Mint and Steel Mine output");
                text = string.Join(", ", parts);
            }

            return element != null ? $"{element.DisplayName} ninjas: {text}" : text;
        }

        static string Percent(int basisPoints) => (basisPoints / 100f).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);

        public override IEnumerable<string> ValidateSelf()
        {
            if (ranks.Count == 0 || ranks.Count > MaxRanks)
                yield return $"has {ranks.Count} ranks; a rune has 1 to {MaxRanks}.";

            for (var i = 0; i < ranks.Count; i++)
            {
                var rank = ranks[i];
                if (rank == null || rank.IsEmpty)
                {
                    yield return $"rank {i + 1} gives nothing.";
                    continue;
                }

                if (rank.fightGold < 0 || rank.clanXp < 0 || rank.income < 0)
                    yield return $"rank {i + 1} has a negative economy bonus.";

                foreach (var message in EffectValidation.ValidateList(rank.effects, $"rank {i + 1} effect"))
                    yield return message;
            }

            if (family == RuneFamily.Seal && element == null)
                yield return "is a Seal but covers no element.";
            if (family != RuneFamily.Seal && element != null)
                yield return "covers an element but isn't a Seal.";
            if (element != null && element.Kind != TraitKind.Element)
                yield return $"covers '{element.Id}', which is a {element.Kind} trait, not an Element.";
        }

        internal void Configure(Rarity rarity, RuneFamily family, TraitDefinition element, Color color, string description, IEnumerable<RuneRank> ranks)
        {
            this.rarity = rarity;
            this.family = family;
            this.element = element;
            this.color = color;
            this.description = description;
            this.ranks = new List<RuneRank>(ranks);
        }
    }
}
