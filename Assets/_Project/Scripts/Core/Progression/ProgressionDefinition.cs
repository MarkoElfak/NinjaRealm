using System;
using System.Collections.Generic;
using NinjaRealm.Core.Content;
using NinjaRealm.Core.Definitions;
using UnityEngine;

namespace NinjaRealm.Core.Progression
{
    /// <summary>
    /// Every number behind the persistent roster: what a new save starts with, what levels cost,
    /// how stars and the Dojo work, and what fights pay. Levels, belts, stars and team sizes are
    /// lists, so a longer list is a balance change, not a code change. Chances and multipliers are
    /// in basis points (10000 = 100%).
    /// </summary>
    [CreateAssetMenu(menuName = "Ninja Realm/Progression", fileName = "Progression")]
    public sealed class ProgressionDefinition : ContentDefinition
    {
        public const int BasisPoints = 10000;

        [Serializable]
        public sealed class StarRank
        {
            [Tooltip("Shards it takes to reach this star from the one before. 0 for the first star.")]
            public int shards;

            [Tooltip("Health and attack damage at this star, in basis points (16000 = x1.6).")]
            public int statBasisPoints = BasisPoints;
        }

        [Serializable]
        public sealed class RarityOffer
        {
            public Rarity rarity;

            [Tooltip("Relative chance of this rarity in a Dojo offer.")]
            public int weight;

            [Tooltip("Gold to recruit a ninja of this rarity from the Dojo.")]
            public int price;

            [Tooltip("Can be bought directly from the list, at the direct buy price.")]
            public bool directBuy;
        }

        [Serializable]
        public sealed class TeamSizeStep
        {
            public int clanLevel = 1;
            public int teamSize = 3;
        }

        [Header("Resources")]
        [SerializeField] internal ResourceDefinition gold;
        [SerializeField] internal ResourceDefinition karma;
        [SerializeField] internal ResourceDefinition steel;

        [Header("New save")]
        [SerializeField] internal List<ResourceAmount> startingResources = new List<ResourceAmount>();
        [SerializeField] internal List<NinjaDefinition> startingNinjas = new List<NinjaDefinition>();
        [SerializeField] internal List<WeaponDefinition> startingWeapons = new List<WeaponDefinition>();
        [SerializeField] internal int rosterLimit = 20;

        [Header("Ninja levels")]
        [SerializeField, Tooltip("Karma to go from each level to the next. The level cap is one more than the length.")]
        internal List<int> levelCosts = new List<int>();

        [SerializeField, Tooltip("Health and attack damage added per level, in basis points (400 = 4%).")]
        internal int statGrowthPerLevel = 400;

        [SerializeField] internal int levelsPerBelt = 5;
        [SerializeField] internal List<string> beltNames = new List<string>();

        [Header("Stars")]
        [SerializeField] internal List<StarRank> stars = new List<StarRank>();

        [Header("Dojo")]
        [SerializeField] internal int dojoOfferCount = 4;
        [SerializeField] internal List<RarityOffer> rarities = new List<RarityOffer>();
        [SerializeField] internal int dojoRefreshCost = 50;
        [SerializeField, Tooltip("Direct buys cost this share of the Dojo price, in basis points (15000 = 150%).")]
        internal int directBuyPrice = 15000;

        [Header("Forging")]
        [SerializeField, Tooltip("Steel to forge a combined weapon from its two components.")]
        internal int forgeCost = 20;

        [Header("Fight rewards")]
        [SerializeField] internal int winGold = 60;
        [SerializeField] internal int winGoldPerDifficulty = 30;
        [SerializeField] internal int winSteelPerDifficulty = 5;
        [SerializeField] internal int winClanXp = 20;
        [SerializeField] internal int winClanXpPerDifficulty = 10;
        [SerializeField, Tooltip("Share of a win's Gold a loss or draw pays, in basis points.")]
        internal int lossGold = 2500;
        [SerializeField, Tooltip("Share of a win's clan XP a loss or draw pays, in basis points.")]
        internal int lossClanXp = 5000;
        [SerializeField] internal int firstWinGoldPerDifficulty = 200;
        [SerializeField, Tooltip("Chance a win drops a weapon component, in basis points.")]
        internal int dropChance = 3000;
        [SerializeField] internal int dropChancePerDifficulty = 1000;

        [Header("Clan")]
        [SerializeField, Tooltip("Clan XP to the next clan level is this times the current level.")]
        internal int clanXpPerLevel = 100;
        [SerializeField] internal int clanLevelKarma = 50;
        [SerializeField] internal int clanLevelKarmaPerLevel = 10;
        [SerializeField, Tooltip("From which clan level each team size applies, lowest first.")]
        internal List<TeamSizeStep> teamSizes = new List<TeamSizeStep>();

        [Header("Income")]
        [SerializeField, Tooltip("Buildings that make a resource over time, such as the Mint.")]
        internal List<IncomeSource> incomes = new List<IncomeSource>();

        public override string IdCategory => "progression";

        public ResourceDefinition Gold => gold;

        public ResourceDefinition Karma => karma;

        public ResourceDefinition Steel => steel;

        public IReadOnlyList<ResourceAmount> StartingResources => startingResources;

        public IReadOnlyList<NinjaDefinition> StartingNinjas => startingNinjas;

        public IReadOnlyList<WeaponDefinition> StartingWeapons => startingWeapons;

        public int RosterLimit => rosterLimit;

        public int MaxLevel => levelCosts.Count + 1;

        public int MaxStars => Math.Max(1, stars.Count);

        public int DojoOfferCount => dojoOfferCount;

        public IReadOnlyList<RarityOffer> Rarities => rarities;

        public int DojoRefreshCost => dojoRefreshCost;

        public int ForgeCost => forgeCost;

        public IReadOnlyList<IncomeSource> Incomes => incomes;

        /// <summary>The income source with this ID (see <see cref="IncomeIds"/>), or null.</summary>
        public IncomeSource Income(string id)
        {
            foreach (var income in incomes)
                if (income != null && income.id == id) return income;
            return null;
        }

        /// <summary>Karma to raise a ninja from this level to the next, or -1 at the cap.</summary>
        public int LevelCost(int level) => level >= 1 && level < MaxLevel ? levelCosts[level - 1] : -1;

        /// <summary>The belt for a level, such as White or Black, 1st dan.</summary>
        public string Belt(int level)
        {
            if (beltNames.Count == 0) return string.Empty;
            var index = Math.Max(0, level - 1) / Math.Max(1, levelsPerBelt);
            return beltNames[Math.Min(index, beltNames.Count - 1)];
        }

        /// <summary>Shards needed to go from this star to the next, or -1 at the top.</summary>
        public int ShardsForNextStar(int star) => star >= 1 && star < stars.Count ? stars[star].shards : -1;

        /// <summary>Health and attack damage of a ninja at this level and star, in basis points of its base stats.</summary>
        public int StatMultiplier(int level, int star)
        {
            var levelPart = (long)BasisPoints + (long)statGrowthPerLevel * (Math.Max(1, level) - 1);
            var starPart = stars.Count == 0 ? BasisPoints : stars[Math.Max(0, Math.Min(star, stars.Count) - 1)].statBasisPoints;
            return (int)(levelPart * starPart / BasisPoints);
        }

        public RarityOffer OfferFor(Rarity rarity)
        {
            foreach (var offer in rarities)
                if (offer != null && offer.rarity == rarity) return offer;
            return null;
        }

        /// <summary>Gold to recruit a ninja from the Dojo, or -1 if its rarity has no price.</summary>
        public int RecruitPrice(Rarity rarity) => OfferFor(rarity)?.price ?? -1;

        /// <summary>Gold to buy a ninja directly, or -1 if its rarity can't be bought that way.</summary>
        public int DirectBuyPrice(Rarity rarity)
        {
            var offer = OfferFor(rarity);
            return offer != null && offer.directBuy ? (int)((long)offer.price * directBuyPrice / BasisPoints) : -1;
        }

        /// <summary>Clan XP to go from this clan level to the next.</summary>
        public int ClanXpToNext(int clanLevel) => clanXpPerLevel * Math.Max(1, clanLevel);

        /// <summary>Karma paid for reaching this clan level.</summary>
        public int ClanLevelKarma(int newClanLevel) => clanLevelKarma + clanLevelKarmaPerLevel * newClanLevel;

        /// <summary>How many ninjas a lineup can hold at this clan level.</summary>
        public int TeamSize(int clanLevel)
        {
            var size = teamSizes.Count > 0 ? teamSizes[0].teamSize : 1;
            foreach (var step in teamSizes)
                if (step != null && clanLevel >= step.clanLevel) size = step.teamSize;
            return size;
        }

        /// <summary>Gold, Steel and clan XP for a fight. The difficulty is the stage's reward tier, 1 to 5.</summary>
        public void FightPay(int difficulty, bool won, out int goldPaid, out int steelPaid, out int clanXp)
        {
            goldPaid = winGold + winGoldPerDifficulty * difficulty;
            steelPaid = winSteelPerDifficulty * difficulty;
            clanXp = winClanXp + winClanXpPerDifficulty * difficulty;
            if (won) return;

            goldPaid = (int)((long)goldPaid * lossGold / BasisPoints);
            steelPaid = 0;
            clanXp = (int)((long)clanXp * lossClanXp / BasisPoints);
        }

        public int FirstWinGold(int difficulty) => firstWinGoldPerDifficulty * difficulty;

        /// <summary>Chance a win drops a component, in basis points.</summary>
        public int DropChance(int difficulty) => Math.Min(BasisPoints, dropChance + dropChancePerDifficulty * difficulty);

        public override IEnumerable<string> ValidateSelf()
        {
            if (gold == null || karma == null || steel == null)
                yield return "is missing one of its resources (gold, karma, steel).";
            if (startingNinjas.Count == 0)
                yield return "starts a new save with no ninjas.";
            if (startingNinjas.Contains(null) || startingWeapons.Contains(null))
                yield return "has an empty starting ninja or weapon.";
            foreach (var amount in startingResources)
                if (amount?.resource == null) yield return "has a starting resource with no resource picked.";
            if (rosterLimit < startingNinjas.Count)
                yield return "has a roster limit smaller than the starting roster.";

            if (levelCosts.Count == 0)
                yield return "has no level costs.";
            foreach (var cost in levelCosts)
                if (cost <= 0)
                {
                    yield return "has a level cost that isn't positive.";
                    break;
                }

            if (beltNames.Count == 0)
                yield return "has no belts.";

            if (stars.Count == 0)
                yield return "has no stars.";
            for (var i = 1; i < stars.Count; i++)
                if (stars[i] == null || stars[i].shards <= 0)
                    yield return $"star {i + 1} needs no shards.";

            if (rarities.Count == 0)
                yield return "has no Dojo rarities.";
            foreach (var offer in rarities)
                if (offer == null || offer.weight < 0 || offer.price <= 0)
                    yield return "has a Dojo rarity with a negative weight or no price.";

            var incomeIds = new HashSet<string>();
            foreach (var income in incomes)
            {
                if (income == null || string.IsNullOrEmpty(income.id))
                    yield return "has an income source with no ID.";
                else if (!incomeIds.Add(income.id))
                    yield return $"lists income source '{income.id}' more than once.";
                else if (income.resource == null || income.perHour <= 0 || income.storageHours <= 0)
                    yield return $"income source '{income.id}' needs a resource, an hourly amount and storage hours.";
            }

            if (teamSizes.Count == 0)
                yield return "has no team sizes.";
            for (var i = 1; i < teamSizes.Count; i++)
                if (teamSizes[i] == null || teamSizes[i - 1] == null || teamSizes[i].clanLevel <= teamSizes[i - 1].clanLevel)
                    yield return "lists team sizes out of clan level order.";
        }
    }
}
