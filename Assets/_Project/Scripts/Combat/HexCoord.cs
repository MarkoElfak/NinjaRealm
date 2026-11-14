using System;

namespace NinjaRealm.Combat
{
    /// <summary>
    /// A hex on the board in "odd-r" offset coordinates: odd rows are shifted half a hex right.
    /// </summary>
    public readonly struct HexCoord : IEquatable<HexCoord>
    {
        public readonly int Column;
        public readonly int Row;

        public HexCoord(int column, int row)
        {
            Column = column;
            Row = row;
        }

        static readonly int[,] EvenRowNeighbors = { { 1, 0 }, { 0, -1 }, { -1, -1 }, { -1, 0 }, { -1, 1 }, { 0, 1 } };
        static readonly int[,] OddRowNeighbors = { { 1, 0 }, { 1, -1 }, { 0, -1 }, { -1, 0 }, { 0, 1 }, { 1, 1 } };

        /// <summary>The 6 neighbors in a fixed order (east, then counter-clockwise), which keeps movement deterministic.</summary>
        public HexCoord Neighbor(int direction)
        {
            var table = (Row & 1) == 0 ? EvenRowNeighbors : OddRowNeighbors;
            return new HexCoord(Column + table[direction, 0], Row + table[direction, 1]);
        }

        public static int Distance(HexCoord a, HexCoord b)
        {
            a.ToCube(out var ax, out var ay, out var az);
            b.ToCube(out var bx, out var by, out var bz);
            return (Math.Abs(ax - bx) + Math.Abs(ay - by) + Math.Abs(az - bz)) / 2;
        }

        void ToCube(out int x, out int y, out int z)
        {
            x = Column - (Row - (Row & 1)) / 2;
            z = Row;
            y = -x - z;
        }

        public bool Equals(HexCoord other) => Column == other.Column && Row == other.Row;

        public override bool Equals(object obj) => obj is HexCoord other && Equals(other);

        public override int GetHashCode() => Column * 397 ^ Row;

        public static bool operator ==(HexCoord a, HexCoord b) => a.Equals(b);

        public static bool operator !=(HexCoord a, HexCoord b) => !a.Equals(b);

        public override string ToString() => $"({Column},{Row})";
    }
}
