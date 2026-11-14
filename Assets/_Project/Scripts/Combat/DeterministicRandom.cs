using System;

namespace NinjaRealm.Combat
{
    /// <summary>
    /// Seeded random numbers for the combat simulation (xoshiro128**, seeded with SplitMix64).
    /// The same seed gives the same sequence on every platform, which lets the server
    /// replay and verify fights. Combat code must never use UnityEngine.Random or System.Random.
    /// </summary>
    public sealed class DeterministicRandom
    {
        /// <summary>100% in basis points. Chances in the sim are integers so results never depend on float rounding.</summary>
        public const int BasisPointsPerHundredPercent = 10000;

        uint s0, s1, s2, s3;

        public DeterministicRandom(ulong seed)
        {
            var x = seed;
            s0 = (uint)SplitMix64(ref x);
            s1 = (uint)SplitMix64(ref x);
            s2 = (uint)SplitMix64(ref x);
            s3 = (uint)SplitMix64(ref x);
            if ((s0 | s1 | s2 | s3) == 0) s0 = 1;
        }

        public uint NextUInt()
        {
            unchecked
            {
                var result = RotateLeft(s1 * 5, 7) * 9;
                var t = s1 << 9;
                s2 ^= s0;
                s3 ^= s1;
                s1 ^= s2;
                s0 ^= s3;
                s2 ^= t;
                s3 = RotateLeft(s3, 11);
                return result;
            }
        }

        /// <summary>A uniform integer in [minInclusive, maxExclusive), without modulo bias.</summary>
        public int Range(int minInclusive, int maxExclusive)
        {
            if (maxExclusive <= minInclusive)
                throw new ArgumentOutOfRangeException(nameof(maxExclusive), "maxExclusive must be greater than minInclusive.");

            unchecked
            {
                var range = (uint)(maxExclusive - minInclusive);
                var m = (ulong)NextUInt() * range;
                var low = (uint)m;
                if (low < range)
                {
                    var threshold = (0u - range) % range;
                    while (low < threshold)
                    {
                        m = (ulong)NextUInt() * range;
                        low = (uint)m;
                    }
                }

                return minInclusive + (int)(m >> 32);
            }
        }

        /// <summary>True with the given chance in basis points, where 2500 means 25%.</summary>
        public bool RollBasisPoints(int chance)
        {
            if (chance <= 0) return false;
            if (chance >= BasisPointsPerHundredPercent) return true;
            return Range(0, BasisPointsPerHundredPercent) < chance;
        }

        static ulong SplitMix64(ref ulong x)
        {
            unchecked
            {
                x += 0x9E3779B97F4A7C15UL;
                var z = x;
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                return z ^ (z >> 31);
            }
        }

        static uint RotateLeft(uint value, int count) => (value << count) | (value >> (32 - count));
    }
}
