using System;
using System.Collections.Generic;

namespace NinjaRealm.Core.Content
{
    public readonly struct ContentIssue
    {
        public readonly ContentDefinition Definition;
        public readonly string Message;

        public ContentIssue(ContentDefinition definition, string message)
        {
            Definition = definition;
            Message = message;
        }

        public override string ToString() => $"[Content] {Message}";
    }

    /// <summary>Checks a set of definitions for broken or conflicting IDs.</summary>
    public static class ContentValidation
    {
        public static List<ContentIssue> Validate(IEnumerable<ContentDefinition> definitions)
        {
            var issues = new List<ContentIssue>();
            var seen = new Dictionary<string, ContentDefinition>(StringComparer.Ordinal);

            foreach (var definition in definitions)
            {
                if (definition == null)
                {
                    issues.Add(new ContentIssue(null, "The database has an empty slot. Rebuild it from the Ninja Realm menu."));
                    continue;
                }

                if (!ContentId.IsValid(definition.Id))
                {
                    issues.Add(new ContentIssue(definition,
                        $"'{definition.name}' has an invalid ID '{definition.Id}'. Use lowercase category.name, for example ninja.kaito."));
                    continue;
                }

                var expected = definition.IdCategory;
                if (expected != null && ContentId.Category(definition.Id) != expected)
                {
                    issues.Add(new ContentIssue(definition,
                        $"'{definition.name}' has ID '{definition.Id}' but {definition.GetType().Name} IDs must start with '{expected}.'."));
                }

                if (seen.TryGetValue(definition.Id, out var other))
                {
                    issues.Add(new ContentIssue(definition,
                        $"'{definition.name}' and '{other.name}' share the ID '{definition.Id}'. IDs must be unique."));
                }
                else
                {
                    seen.Add(definition.Id, definition);
                }

                foreach (var message in definition.ValidateSelf())
                {
                    issues.Add(new ContentIssue(definition, $"'{definition.name}' ({definition.Id}): {message}"));
                }
            }

            return issues;
        }
    }
}
