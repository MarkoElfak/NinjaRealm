using System;

namespace NinjaRealm.Core.Progression
{
    /// <summary>IDs of the income sources the village's buildings use.</summary>
    public static class IncomeIds
    {
        public const string Mint = "mint";
        public const string SteelMine = "steel_mine";
    }

    /// <summary>
    /// Something that makes a resource while the player is away, such as the Mint. It fills up
    /// to a few hours of work and waits to be collected. Part of <see cref="ProgressionDefinition"/>.
    /// </summary>
    [Serializable]
    public sealed class IncomeSource
    {
        public const int SecondsPerHour = 3600;

        public string id;
        public ResourceDefinition resource;

        /// <summary>Made per hour at clan level 1.</summary>
        public int perHour = 1;

        /// <summary>Added to the hourly amount for every clan level above 1.</summary>
        public int perHourPerClanLevel;

        /// <summary>It stops filling after this many hours without a collection.</summary>
        public int storageHours = 8;

        /// <summary>Made per hour at this clan level.</summary>
        public int PerHour(int clanLevel) => Math.Max(0, perHour + perHourPerClanLevel * (Math.Max(1, clanLevel) - 1));

        /// <summary>The most it holds at this clan level.</summary>
        public int Capacity(int clanLevel) => PerHour(clanLevel) * Math.Max(1, storageHours);

        /// <summary>Seconds of work it can store.</summary>
        public long StorageSeconds => (long)Math.Max(1, storageHours) * SecondsPerHour;

        /// <summary>What this many seconds of work makes at this clan level, up to the capacity.</summary>
        public int Made(long seconds, int clanLevel)
        {
            var worked = Math.Max(0L, Math.Min(seconds, StorageSeconds));
            return (int)(worked * PerHour(clanLevel) / SecondsPerHour);
        }
    }

    /// <summary>When an income source was last emptied, in the save file.</summary>
    [Serializable]
    public sealed class IncomeTimer
    {
        public string id;

        /// <summary>Seconds since 1970 (UTC) from which the source has been filling.</summary>
        public long collectedAt;
    }
}
