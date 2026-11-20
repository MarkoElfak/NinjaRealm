using System.Collections.Generic;
using NinjaRealm.Core.Definitions;
using UnityEngine;

namespace NinjaRealm.Presentation
{
    /// <summary>
    /// The runes' icons. A rune's linked icon wins; otherwise it comes from
    /// Resources/RuneIcons/&lt;name&gt; (Art/Runes), where the name is the ID without "rune.".
    /// Tools/RuneIcons/make_rune_icons.py draws those files.
    /// </summary>
    public static class RuneArt
    {
        public const string Folder = "RuneIcons";

        static readonly Dictionary<string, Texture2D> Cache = new Dictionary<string, Texture2D>();

        /// <summary>The rune's icon, or null when it has none.</summary>
        public static Texture2D Icon(RuneDefinition rune)
        {
            if (rune == null) return null;
            if (rune.Icon != null) return rune.Icon.texture;

            var name = rune.IconName;
            if (string.IsNullOrEmpty(name)) return null;
            if (Cache.TryGetValue(name, out var texture) && texture != null) return texture;
            texture = Resources.Load<Texture2D>($"{Folder}/{name}");
            Cache[name] = texture;
            return texture;
        }

        /// <summary>The colour of a rarity's rim, the same as the icons'.</summary>
        public static Color RarityColor(Rarity rarity) => rarity switch
        {
            Rarity.Rare => new Color(0.29f, 0.64f, 1f),
            Rarity.Epic => new Color(0.69f, 0.38f, 1f),
            Rarity.Legendary => new Color(1f, 0.72f, 0.19f),
            _ => new Color(0.61f, 0.64f, 0.69f),
        };
    }
}
