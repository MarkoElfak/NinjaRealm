using System.Collections.Generic;
using NinjaRealm.Core.Content;
using NinjaRealm.Core.Definitions;
using NinjaRealm.Core.Lineups;
using NinjaRealm.Core.Progression;
using UnityEngine;

namespace NinjaRealm.Core.Campaign
{
    public enum StageKind
    {
        Normal,

        /// <summary>A harder fight in the middle of a chapter. Its first clear also pays Steel.</summary>
        Elite,

        /// <summary>The last fight of a chapter. Its first clear also pays a ninja.</summary>
        Boss,
    }

    /// <summary>
    /// One fight of the campaign: an enemy lineup, the level it fights at, and what the first
    /// clear pays. The lineup says who stands where; the stage says how strong they are, so one
    /// lineup can come back later at a higher level.
    /// </summary>
    [CreateAssetMenu(menuName = "Ninja Realm/Stage", fileName = "NewStage")]
    public sealed class StageDefinition : ContentDefinition
    {
        [SerializeField] LineupDefinition lineup;

        [SerializeField, Range(1, 60), Tooltip("The level every enemy ninja fights at.")]
        int level = 1;

        [SerializeField] StageKind kind;

        [SerializeField, Range(1, 5), Tooltip("Scales what every fight here pays: Gold, Steel, clan XP and the weapon drop chance.")]
        int rewardTier = 1;

        [SerializeField, Tooltip("Optional. This ninja of the lineup fights with the boss stars below.")]
        NinjaDefinition boss;

        [SerializeField, Range(1, 3)] int bossStars = 1;

        [Header("First clear, on top of the first-win Gold and weapon")]
        [SerializeField, Min(0)] int firstClearKarma;

        [SerializeField, Min(0)] int firstClearSteel;

        [SerializeField, Tooltip("Joins the clan on the first clear, or gives a shard when already owned.")]
        NinjaDefinition firstClearNinja;

        public override string IdCategory => "stage";

        public LineupDefinition Lineup => lineup;

        public int Level => level;

        public StageKind Kind => kind;

        public int RewardTier => rewardTier;

        public NinjaDefinition Boss => boss;

        public int BossStars => bossStars;

        public int FirstClearKarma => firstClearKarma;

        public int FirstClearSteel => firstClearSteel;

        public NinjaDefinition FirstClearNinja => firstClearNinja;

        /// <summary>The level and stars an enemy ninja fights at here.</summary>
        public NinjaRank RankOf(NinjaDefinition ninja) =>
            new NinjaRank(level, ninja != null && ninja == boss ? bossStars : 1);

        public override IEnumerable<string> ValidateSelf()
        {
            if (lineup == null)
            {
                yield return "It has no lineup to fight.";
                yield break;
            }

            if (boss == null) yield break;

            var found = false;
            foreach (var slot in lineup.Slots)
                if (slot != null && slot.ninja == boss) found = true;
            if (!found) yield return $"Its boss {boss.DisplayName} is not in the lineup {lineup.DisplayName}.";
        }

        internal void Configure(LineupDefinition lineup, int level, StageKind kind, int rewardTier)
        {
            this.lineup = lineup;
            this.level = level;
            this.kind = kind;
            this.rewardTier = rewardTier;
            boss = null;
            bossStars = 1;
            firstClearKarma = 0;
            firstClearSteel = 0;
            firstClearNinja = null;
        }

        internal void SetBoss(NinjaDefinition boss, int stars)
        {
            this.boss = boss;
            bossStars = stars;
        }

        internal void SetFirstClear(int karma, int steel, NinjaDefinition ninja)
        {
            firstClearKarma = karma;
            firstClearSteel = steel;
            firstClearNinja = ninja;
        }
    }
}
