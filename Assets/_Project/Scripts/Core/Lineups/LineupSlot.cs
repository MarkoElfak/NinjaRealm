using System;
using System.Collections.Generic;
using NinjaRealm.Core.Definitions;
using UnityEngine;

namespace NinjaRealm.Core.Lineups
{
    /// <summary>
    /// One ninja in a lineup, with its place on its team's half of the board and its weapons.
    /// Positions are seen from the team's own side: row 0 is the back row.
    /// </summary>
    [Serializable]
    public sealed class LineupSlot
    {
        public NinjaDefinition ninja;

        [Range(0, 6)] public int column = 3;

        [Range(0, 3), Tooltip("0 is the back row, 3 the front row.")]
        public int rowFromBack = 3;

        public List<WeaponDefinition> weapons = new List<WeaponDefinition>();

        public LineupSlot()
        {
        }

        public LineupSlot(NinjaDefinition ninja, int column, int rowFromBack, params WeaponDefinition[] weapons)
        {
            this.ninja = ninja;
            this.column = column;
            this.rowFromBack = rowFromBack;
            this.weapons = new List<WeaponDefinition>(weapons);
        }

        public LineupSlot Clone() => new LineupSlot(ninja, column, rowFromBack, weapons.ToArray());
    }

    /// <summary>One lineup slot in a save file: content IDs instead of asset references.</summary>
    [Serializable]
    public sealed class SavedLineupSlot
    {
        public string ninja;
        public int column;
        public int row;
        public List<string> weapons = new List<string>();
    }
}
