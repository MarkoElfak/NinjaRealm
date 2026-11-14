using System;

namespace NinjaRealm.Combat
{
    /// <summary>
    /// Fixed-point helpers. The simulation stores every number as an integer count of
    /// thousandths ("milli") of the authored value: 650 health is 650000, 25% crit chance
    /// is 25000, 0.75 attacks per second is 750. Integer math keeps fights identical on
    /// every machine.
    /// </summary>
    public static class Fixed
    {
        public const long One = 1000;

        /// <summary>100% expressed in milli-percent.</summary>
        public const long Hundred = 100 * One;

        public const int TicksPerSecond = 20;

        public static long FromFloat(float value) => (long)Math.Round(value * One, MidpointRounding.AwayFromZero);

        public static long FromInt(int value) => value * One;

        public static float ToFloat(long value) => value / (float)One;

        public static int SecondsToTicks(float seconds) =>
            (int)Math.Round(seconds * TicksPerSecond, MidpointRounding.AwayFromZero);

        /// <summary>amount x percent%, where percent is in milli-percent (25000 = 25%).</summary>
        public static long Percent(long amount, long percentMilli) => amount * percentMilli / Hundred;

        /// <summary>amount x (100% + bonus%), where bonus is in milli-percent.</summary>
        public static long AddPercent(long amount, long bonusMilli) => amount * (Hundred + bonusMilli) / Hundred;

        /// <summary>A milli-percent chance converted to basis points for DeterministicRandom.</summary>
        public static int ToBasisPoints(long percentMilli) => (int)Math.Max(0, Math.Min(10000, percentMilli / 10));
    }
}
