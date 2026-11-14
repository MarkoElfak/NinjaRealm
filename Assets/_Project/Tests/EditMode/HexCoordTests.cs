using System.Collections.Generic;
using NinjaRealm.Combat;
using NUnit.Framework;

namespace NinjaRealm.Tests
{
    public class HexCoordTests
    {
        [Test]
        public void Distance_ToSelfIsZero()
        {
            Assert.AreEqual(0, HexCoord.Distance(new HexCoord(3, 4), new HexCoord(3, 4)));
        }

        [TestCase(2, 2)]
        [TestCase(2, 3)]
        public void Neighbors_AreSixDistinctHexesAtDistanceOne(int column, int row)
        {
            var center = new HexCoord(column, row);
            var seen = new HashSet<HexCoord>();
            for (var direction = 0; direction < 6; direction++)
            {
                var neighbor = center.Neighbor(direction);
                Assert.AreEqual(1, HexCoord.Distance(center, neighbor), $"direction {direction}");
                Assert.IsTrue(seen.Add(neighbor), $"direction {direction} repeats a hex");
            }
        }

        [TestCase(2, 2)]
        [TestCase(2, 3)]
        public void Neighbor_OppositeDirectionLeadsBack(int column, int row)
        {
            var center = new HexCoord(column, row);
            for (var direction = 0; direction < 6; direction++)
                Assert.AreEqual(center, center.Neighbor(direction).Neighbor((direction + 3) % 6));
        }

        [Test]
        public void Distance_MatchesKnownValues()
        {
            Assert.AreEqual(3, HexCoord.Distance(new HexCoord(0, 0), new HexCoord(3, 0)));
            Assert.AreEqual(2, HexCoord.Distance(new HexCoord(0, 0), new HexCoord(0, 2)));
            Assert.AreEqual(2, HexCoord.Distance(new HexCoord(0, 0), new HexCoord(1, 2)));
            Assert.AreEqual(7, HexCoord.Distance(new HexCoord(3, 0), new HexCoord(3, 7)));
            Assert.AreEqual(HexCoord.Distance(new HexCoord(1, 5), new HexCoord(6, 2)), HexCoord.Distance(new HexCoord(6, 2), new HexCoord(1, 5)));
        }
    }
}
