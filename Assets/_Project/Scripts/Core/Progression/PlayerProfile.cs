using System;
using System.Collections.Generic;
using NinjaRealm.Core.Lineups;

namespace NinjaRealm.Core.Progression
{
    /// <summary>A ninja's level and stars, which scale its health and attack damage in a fight.</summary>
    public readonly struct NinjaRank : IEquatable<NinjaRank>
    {
        public static readonly NinjaRank Base = new NinjaRank(1, 1);

        public readonly int Level;
        public readonly int Stars;

        public NinjaRank(int level, int stars)
        {
            Level = Math.Max(1, level);
            Stars = Math.Max(1, stars);
        }

        public bool Equals(NinjaRank other) => Level == other.Level && Stars == other.Stars;

        public override bool Equals(object obj) => obj is NinjaRank other && Equals(other);

        public override int GetHashCode() => Level * 31 + Stars;

        public override string ToString() => $"level {Level}, {Stars} star{(Stars == 1 ? "" : "s")}";
    }

    /// <summary>
    /// Everything the save file holds, as plain data with content IDs. <see cref="ProfileService"/>
    /// changes it and <see cref="SaveFile"/> reads and writes it.
    /// </summary>
    [Serializable]
    public sealed class PlayerProfile
    {
        public int version;
        public Wallet wallet = new Wallet();
        public List<OwnedNinja> ninjas = new List<OwnedNinja>();
        public List<OwnedWeapon> weapons = new List<OwnedWeapon>();
        public int clanLevel = 1;
        public int clanXp;
        public List<SavedLineupSlot> lineup = new List<SavedLineupSlot>();

        /// <summary>The campaign stage picked for the next fight.</summary>
        public string stage = string.Empty;

        /// <summary>The best stars on every campaign stage cleared so far.</summary>
        public List<StageProgress> stages = new List<StageProgress>();

        /// <summary>The star chests opened so far.</summary>
        public List<OpenedChest> chests = new List<OpenedChest>();

        /// <summary>
        /// Only in saves from before the campaign (version 1): the enemy lineups beaten at least
        /// once. Loading turns them into cleared stages and empties the list.
        /// </summary>
        public List<string> beatenLineups = new List<string>();

        /// <summary>The Dojo's current offers by ninja ID. An empty entry was already recruited.</summary>
        public List<string> dojoOffers = new List<string>();

        /// <summary>The day the Dojo offers were rolled, as yyyy-MM-dd.</summary>
        public string dojoDate = string.Empty;

        /// <summary>Seed and roll count for the Dojo's random offers.</summary>
        public long randomSeed;

        public int randomRolls;

        /// <summary>When each income source (the Mint, the Steel Mine) was last emptied.</summary>
        public List<IncomeTimer> incomes = new List<IncomeTimer>();
    }

    [Serializable]
    public sealed class OwnedNinja
    {
        public string id;
        public int level = 1;
        public int stars = 1;

        /// <summary>Shards toward the next star.</summary>
        public int shards;

        public NinjaRank Rank => new NinjaRank(level, stars);
    }

    /// <summary>A cleared campaign stage and the most stars won on it.</summary>
    [Serializable]
    public sealed class StageProgress
    {
        public string id;
        public int stars;
    }

    /// <summary>An opened star chest: the chapter it belongs to and the stars it needed.</summary>
    [Serializable]
    public sealed class OpenedChest
    {
        public string chapter;
        public int stars;
    }

    [Serializable]
    public sealed class OwnedWeapon
    {
        public string id;
        public int count;
    }

    /// <summary>Resource balances by resource ID. Every cost and reward goes through here.</summary>
    [Serializable]
    public sealed class Wallet
    {
        [Serializable]
        public sealed class Balance
        {
            public string id;
            public int amount;
        }

        public List<Balance> balances = new List<Balance>();

        public int Get(string resourceId)
        {
            foreach (var balance in balances)
                if (balance != null && balance.id == resourceId) return balance.amount;
            return 0;
        }

        public int Get(ResourceDefinition resource) => resource != null ? Get(resource.Id) : 0;

        /// <summary>Adds an amount, which may be negative. Balances never go below zero.</summary>
        public void Add(string resourceId, int amount)
        {
            if (string.IsNullOrEmpty(resourceId) || amount == 0) return;
            foreach (var balance in balances)
            {
                if (balance == null || balance.id != resourceId) continue;
                balance.amount = (int)Math.Max(0L, Math.Min(int.MaxValue, (long)balance.amount + amount));
                return;
            }

            if (amount > 0) balances.Add(new Balance { id = resourceId, amount = amount });
        }

        public void Add(ResourceDefinition resource, int amount)
        {
            if (resource != null) Add(resource.Id, amount);
        }

        public bool CanAfford(ResourceDefinition resource, int amount) => amount <= 0 || Get(resource) >= amount;

        /// <summary>Takes the amount if the balance covers it.</summary>
        public bool TrySpend(ResourceDefinition resource, int amount)
        {
            if (amount <= 0) return true;
            if (resource == null || Get(resource) < amount) return false;
            Add(resource, -amount);
            return true;
        }
    }
}
