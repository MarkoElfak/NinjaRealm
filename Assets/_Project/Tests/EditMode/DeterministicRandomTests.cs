using System;
using NinjaRealm.Combat;
using NUnit.Framework;

namespace NinjaRealm.Tests
{
    public class DeterministicRandomTests
    {
        [Test]
        public void SameSeed_GivesKnownSequence()
        {
            // Golden values: if these change, every saved replay and server check breaks.
            var random = new DeterministicRandom(42);
            Assert.AreEqual(204391854u, random.NextUInt());
            Assert.AreEqual(1829846404u, random.NextUInt());
            Assert.AreEqual(4021786942u, random.NextUInt());
        }

        [Test]
        public void SameSeed_GivesSameSequence()
        {
            var a = new DeterministicRandom(12345);
            var b = new DeterministicRandom(12345);
            for (var i = 0; i < 1000; i++)
                Assert.AreEqual(a.NextUInt(), b.NextUInt());
        }

        [Test]
        public void DifferentSeeds_GiveDifferentSequences()
        {
            var a = new DeterministicRandom(1);
            var b = new DeterministicRandom(2);
            var same = 0;
            for (var i = 0; i < 100; i++)
                if (a.NextUInt() == b.NextUInt()) same++;
            Assert.Less(same, 3);
        }

        [Test]
        public void Range_StaysInBoundsAndHitsBothEnds()
        {
            var random = new DeterministicRandom(7);
            bool sawMin = false, sawMax = false;
            for (var i = 0; i < 10000; i++)
            {
                var value = random.Range(-3, 4);
                Assert.That(value, Is.InRange(-3, 3));
                sawMin |= value == -3;
                sawMax |= value == 3;
            }

            Assert.IsTrue(sawMin && sawMax);
        }

        [Test]
        public void Range_HandlesFullIntSpan()
        {
            var random = new DeterministicRandom(9);
            for (var i = 0; i < 1000; i++)
                Assert.Less(random.Range(int.MinValue, int.MaxValue), int.MaxValue);
        }

        [Test]
        public void Range_ThrowsWhenEmpty()
        {
            var random = new DeterministicRandom(1);
            Assert.Throws<ArgumentOutOfRangeException>(() => random.Range(5, 5));
        }

        [Test]
        public void RollBasisPoints_RespectsEdgesAndRate()
        {
            var random = new DeterministicRandom(99);
            Assert.IsFalse(random.RollBasisPoints(0));
            Assert.IsTrue(random.RollBasisPoints(10000));

            const int rolls = 100000;
            var hits = 0;
            for (var i = 0; i < rolls; i++)
                if (random.RollBasisPoints(2500)) hits++;

            Assert.That(hits / (double)rolls, Is.InRange(0.24, 0.26));
        }
    }
}
