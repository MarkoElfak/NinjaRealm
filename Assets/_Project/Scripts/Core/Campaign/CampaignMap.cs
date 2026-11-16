using System.Collections.Generic;
using NinjaRealm.Combat;
using NinjaRealm.Core.Content;

namespace NinjaRealm.Core.Campaign
{
    /// <summary>
    /// The campaign in play order: chapters by number, and every stage one after another.
    /// Built once from the content.
    /// </summary>
    public sealed class CampaignMap
    {
        readonly List<ChapterDefinition> chapters = new List<ChapterDefinition>();
        readonly List<StageDefinition> stages = new List<StageDefinition>();
        readonly Dictionary<StageDefinition, int> indexOf = new Dictionary<StageDefinition, int>();
        readonly Dictionary<StageDefinition, ChapterDefinition> chapterOf = new Dictionary<StageDefinition, ChapterDefinition>();
        readonly Dictionary<StageDefinition, string> labelOf = new Dictionary<StageDefinition, string>();

        public CampaignMap(ContentDatabase content)
        {
            if (content == null) return;

            chapters.AddRange(content.GetAll<ChapterDefinition>());
            chapters.Sort((a, b) => a.Number != b.Number ? a.Number.CompareTo(b.Number) : string.CompareOrdinal(a.Id, b.Id));

            foreach (var chapter in chapters)
            {
                var place = 0;
                foreach (var stage in chapter.Stages)
                {
                    place++;

                    // A stage belongs to the first chapter that lists it.
                    if (stage == null || indexOf.ContainsKey(stage)) continue;
                    indexOf[stage] = stages.Count;
                    chapterOf[stage] = chapter;
                    labelOf[stage] = $"{chapter.Number}-{place}";
                    stages.Add(stage);
                }
            }
        }

        public IReadOnlyList<ChapterDefinition> Chapters => chapters;

        /// <summary>Every stage of every chapter, in the order they are fought.</summary>
        public IReadOnlyList<StageDefinition> Stages => stages;

        public bool Contains(StageDefinition stage) => stage != null && indexOf.ContainsKey(stage);

        /// <summary>The stage's place in <see cref="Stages"/>, or -1 when it isn't in the campaign.</summary>
        public int IndexOf(StageDefinition stage) => stage != null && indexOf.TryGetValue(stage, out var index) ? index : -1;

        public ChapterDefinition ChapterOf(StageDefinition stage) =>
            stage != null && chapterOf.TryGetValue(stage, out var chapter) ? chapter : null;

        /// <summary>Chapter and place, such as "2-4" for the fourth stage of chapter 2.</summary>
        public string LabelOf(StageDefinition stage) =>
            stage != null && labelOf.TryGetValue(stage, out var label) ? label : "?";

        /// <summary>The stage fought before this one, or null for the first.</summary>
        public StageDefinition Previous(StageDefinition stage)
        {
            var index = IndexOf(stage);
            return index > 0 ? stages[index - 1] : null;
        }

        /// <summary>The stage fought after this one, or null for the last.</summary>
        public StageDefinition Next(StageDefinition stage)
        {
            var index = IndexOf(stage);
            return index >= 0 && index + 1 < stages.Count ? stages[index + 1] : null;
        }
    }

    /// <summary>How many stars a fight earns: one for the win, two with at least half the team standing, three with nobody fallen.</summary>
    public static class StageStars
    {
        public const int Max = ChapterDefinition.StarsPerStage;

        public static int For(bool won, int fielded, int standing)
        {
            if (!won) return 0;
            if (fielded <= 0 || standing >= fielded) return Max;
            return standing * 2 >= fielded ? 2 : 1;
        }

        /// <summary>The stars a finished fight earns for one team.</summary>
        public static int For(CombatResult result, int team = 0)
        {
            if (result == null) return 0;

            var fielded = 0;
            var standing = 0;
            foreach (var unit in result.Units)
            {
                if (unit.Team != team) continue;
                fielded++;
                if (unit.Survived) standing++;
            }

            return For(result.WinningTeam == team, fielded, standing);
        }
    }
}
