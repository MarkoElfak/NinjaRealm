using System;
using System.Collections.Generic;
using NinjaRealm.Core.Content;
using NinjaRealm.Core.Definitions;
using UnityEngine;

namespace NinjaRealm.Core.Campaign
{
    /// <summary>A reward for collecting stars in a chapter. It opens once.</summary>
    [Serializable]
    public sealed class StarChest
    {
        [Min(1), Tooltip("Stars needed in this chapter to open the chest.")]
        public int stars = 1;

        [Min(0)] public int gold;
        [Min(0)] public int steel;

        [Tooltip("Optional weapon inside the chest.")]
        public WeaponDefinition weapon;

        public bool IsEmpty => gold <= 0 && steel <= 0 && weapon == null;
    }

    /// <summary>
    /// A chapter of the campaign: its stages in the order they are fought, and the chests its
    /// stars open. Chapters are played in the order of their numbers.
    /// </summary>
    [CreateAssetMenu(menuName = "Ninja Realm/Chapter", fileName = "NewChapter")]
    public sealed class ChapterDefinition : ContentDefinition
    {
        public const int StarsPerStage = 3;

        [SerializeField, Min(1), Tooltip("Chapters are played in this order.")]
        int number = 1;

        [SerializeField, TextArea] string description;

        [SerializeField] List<StageDefinition> stages = new List<StageDefinition>();

        [SerializeField] List<StarChest> chests = new List<StarChest>();

        public override string IdCategory => "chapter";

        public int Number => number;

        public string Description => description;

        public IReadOnlyList<StageDefinition> Stages => stages;

        public IReadOnlyList<StarChest> Chests => chests;

        /// <summary>The most stars the chapter can give.</summary>
        public int MaxStars => stages.Count * StarsPerStage;

        public override IEnumerable<string> ValidateSelf()
        {
            if (stages.Count == 0) yield return "It has no stages.";

            var seen = new HashSet<StageDefinition>();
            for (var i = 0; i < stages.Count; i++)
            {
                if (stages[i] == null) yield return $"Stage {i + 1} is empty.";
                else if (!seen.Add(stages[i])) yield return $"Stage {i + 1} ({stages[i].Id}) is in the chapter twice.";
            }

            var last = 0;
            for (var i = 0; i < chests.Count; i++)
            {
                var chest = chests[i];
                if (chest == null)
                {
                    yield return $"Chest {i + 1} is empty.";
                    continue;
                }

                if (chest.IsEmpty) yield return $"Chest {i + 1} has nothing in it.";
                if (chest.stars <= last) yield return $"Chest {i + 1} needs {chest.stars} stars, which is not more than the chest before it.";
                if (chest.stars > MaxStars) yield return $"Chest {i + 1} needs {chest.stars} stars but the chapter only has {MaxStars}.";
                last = chest.stars;
            }
        }

        internal void Configure(int number, string description, IEnumerable<StageDefinition> stages, IEnumerable<StarChest> chests)
        {
            this.number = number;
            this.description = description;
            this.stages = new List<StageDefinition>(stages);
            this.chests = new List<StarChest>(chests);
        }
    }
}
