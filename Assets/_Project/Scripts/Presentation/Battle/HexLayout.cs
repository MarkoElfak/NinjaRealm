using NinjaRealm.Combat;
using UnityEngine;

namespace NinjaRealm.Presentation.Battle
{
    /// <summary>
    /// Converts board hexes to world positions. Hexes are pointy-top in "odd-r" layout, the
    /// same coordinates the combat simulation uses. Row 0 (team 0's back row) is nearest the
    /// camera, and the board is centred on the world origin.
    /// </summary>
    public static class HexLayout
    {
        /// <summary>Centre to corner, in metres. A ninja is about 1.2 m tall.</summary>
        public const float Radius = 0.75f;

        public static readonly float Width = Radius * Mathf.Sqrt(3f);
        public const float RowSpacing = Radius * 1.5f;

        public static Vector3 ToWorld(HexCoord hex, int columns, int rows)
        {
            var x = Width * (hex.Column + 0.5f * (hex.Row & 1));
            var z = RowSpacing * hex.Row;
            var centreX = Width * (columns - 1 + 0.5f) * 0.5f;
            var centreZ = RowSpacing * (rows - 1) * 0.5f;
            return new Vector3(x - centreX, 0f, z - centreZ);
        }

        /// <summary>The six corners of a pointy-top hex around the origin, counter-clockwise from the right.</summary>
        public static Vector3 Corner(int index, float radius)
        {
            var angle = Mathf.Deg2Rad * (60f * index - 30f);
            return new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
        }
    }
}
