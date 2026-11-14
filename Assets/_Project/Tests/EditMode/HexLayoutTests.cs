using NinjaRealm.Combat;
using NinjaRealm.Presentation.Battle;
using NUnit.Framework;
using UnityEngine;

namespace NinjaRealm.Tests
{
    public class HexLayoutTests
    {
        const int Columns = 7;
        const int Rows = 8;

        [TestCase(3, 3)]
        [TestCase(3, 4)]
        public void Neighbors_AreOneHexWidthApart(int column, int row)
        {
            var center = new HexCoord(column, row);
            var origin = HexLayout.ToWorld(center, Columns, Rows);
            for (var direction = 0; direction < 6; direction++)
            {
                var neighbor = HexLayout.ToWorld(center.Neighbor(direction), Columns, Rows);
                Assert.AreEqual(HexLayout.Width, Vector3.Distance(origin, neighbor), 0.0001f, $"direction {direction}");
            }
        }

        [Test]
        public void Board_IsCentredOnTheOrigin()
        {
            var first = HexLayout.ToWorld(new HexCoord(0, 0), Columns, Rows);
            var last = HexLayout.ToWorld(new HexCoord(Columns - 1, Rows - 1), Columns, Rows);
            Assert.AreEqual(0f, (first + last).x, 0.0001f);
            Assert.AreEqual(0f, (first + last).z, 0.0001f);
        }

        [Test]
        public void TeamZero_IsNearestTheCamera()
        {
            Assert.Less(HexLayout.ToWorld(new HexCoord(3, 0), Columns, Rows).z, 0f);
            Assert.Greater(HexLayout.ToWorld(new HexCoord(3, Rows - 1), Columns, Rows).z, 0f);
        }
    }
}
