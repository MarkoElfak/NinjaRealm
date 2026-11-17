using System.Text;

namespace NinjaRealm.Core.Progression
{
    /// <summary>The rules for a clan's name: 3 to 16 letters, digits, spaces, hyphens or apostrophes.</summary>
    public static class ClanName
    {
        public const int MinLength = 3;
        public const int MaxLength = 16;

        /// <summary>The name with its ends trimmed and runs of spaces made single.</summary>
        public static string Clean(string name)
        {
            if (string.IsNullOrEmpty(name)) return string.Empty;
            var cleaned = new StringBuilder(name.Length);
            var space = false;
            foreach (var c in name.Trim())
            {
                if (char.IsWhiteSpace(c))
                {
                    space = true;
                    continue;
                }

                if (space && cleaned.Length > 0) cleaned.Append(' ');
                space = false;
                cleaned.Append(c);
            }

            return cleaned.ToString();
        }

        /// <summary>Why a cleaned name can't be used, or null when it can.</summary>
        public static string Problem(string name)
        {
            name = Clean(name);
            if (name.Length < MinLength) return $"A clan name needs at least {MinLength} characters.";
            if (name.Length > MaxLength) return $"A clan name can have at most {MaxLength} characters.";
            var letters = 0;
            foreach (var c in name)
            {
                if (char.IsLetter(c)) letters++;
                else if (!char.IsDigit(c) && c != ' ' && c != '-' && c != '\'')
                    return "Use only letters, digits, spaces, hyphens and apostrophes.";
            }

            return letters == 0 ? "A clan name needs at least one letter." : null;
        }
    }
}
