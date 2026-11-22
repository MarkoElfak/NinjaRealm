using System;
using System.Collections.Generic;
using NinjaRealm.Core.Definitions;
using UnityEngine;

namespace NinjaRealm.Core.Lineups
{
    /// <summary>
    /// A special tile on one hex of the board. Its place is seen from the owning half's side,
    /// like a <see cref="LineupSlot"/>: team 0 is the player's half, team 1 the enemy's, and row 0
    /// is that half's back row.
    /// </summary>
    [Serializable]
    public sealed class BoardTile
    {
        public TileDefinition tile;

        [Range(0, 1), Tooltip("0 is the player's half, 1 the enemy's.")]
        public int team;

        [Range(0, 6)] public int column = 3;

        [Range(0, 3), Tooltip("0 is the back row, 3 the front row.")]
        public int rowFromBack;

        public BoardTile()
        {
        }

        public BoardTile(TileDefinition tile, int team, int column, int rowFromBack)
        {
            this.tile = tile;
            this.team = team;
            this.column = column;
            this.rowFromBack = rowFromBack;
        }

        /// <summary>The tile at a place on a team's half, or null.</summary>
        public static TileDefinition At(IEnumerable<BoardTile> tiles, int team, int column, int rowFromBack)
        {
            if (tiles == null) return null;
            foreach (var entry in tiles)
                if (entry?.tile != null && entry.team == team && entry.column == column && entry.rowFromBack == rowFromBack)
                    return entry.tile;
            return null;
        }

        /// <summary>Problems with a stage's tiles, one message per problem. Empty when they are fine.</summary>
        public static IEnumerable<string> Validate(IReadOnlyList<BoardTile> tiles)
        {
            if (tiles == null) yield break;
            var hexes = new HashSet<int>();
            for (var i = 0; i < tiles.Count; i++)
            {
                var entry = tiles[i];
                var label = $"tile {i + 1}";
                if (entry == null || entry.tile == null)
                {
                    yield return $"{label} has no tile.";
                    continue;
                }

                if (entry.team != 0 && entry.team != 1)
                    yield return $"{label} ({entry.tile.DisplayName}) is on team {entry.team}; use 0 for the player's half or 1 for the enemy's.";
                if (!LineupRules.IsOnBoard(entry.column, entry.rowFromBack))
                    yield return $"{label} ({entry.tile.DisplayName}) is off the board at column {entry.column}, row {entry.rowFromBack}.";
                else if (!entry.tile.AllowsRow(entry.rowFromBack, LineupRules.RowsPerTeam))
                    yield return $"{label} ({entry.tile.DisplayName}) can't lie on row {entry.rowFromBack}; it needs {entry.tile.Rows}.";
                if (!hexes.Add(entry.team * 100 + entry.column * 10 + entry.rowFromBack))
                    yield return $"{label} ({entry.tile.DisplayName}) shares its hex with another tile.";
            }
        }
    }
}
