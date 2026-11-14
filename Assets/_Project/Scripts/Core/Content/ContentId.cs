using System.Text.RegularExpressions;

namespace NinjaRealm.Core.Content
{
    /// <summary>
    /// Rules for content IDs. An ID is lowercase "category.name", for example
    /// "ninja.kaito", "weapon.shadow_katana" or "trait.element.fire".
    /// IDs are what save files, recipes and server data point to, so they must
    /// never change once content has shipped.
    /// </summary>
    public static class ContentId
    {
        static readonly Regex Pattern =
            new Regex(@"^[a-z][a-z0-9]*(\.[a-z0-9]+(_[a-z0-9]+)*)+$", RegexOptions.Compiled);

        public static bool IsValid(string id) => !string.IsNullOrEmpty(id) && Pattern.IsMatch(id);

        /// <summary>The part before the first dot ("ninja" for "ninja.kaito"), or null if the ID is invalid.</summary>
        public static string Category(string id) => IsValid(id) ? id.Substring(0, id.IndexOf('.')) : null;
    }
}
