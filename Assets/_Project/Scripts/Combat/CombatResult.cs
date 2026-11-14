using System.Collections.Generic;

namespace NinjaRealm.Combat
{
    public sealed class CombatResult
    {
        /// <summary>0 or 1 for the winning team, -1 for a draw.</summary>
        public int WinningTeam;

        public int DurationTicks;

        /// <summary>True when the fight ran out of time and was decided by remaining health.</summary>
        public bool DecidedByTimeout;

        public readonly List<UnitSummary> Units = new List<UnitSummary>();

        public float DurationSeconds => DurationTicks / (float)Fixed.TicksPerSecond;
    }

    public readonly struct UnitSummary
    {
        public readonly int Index;
        public readonly int Team;
        public readonly string DefinitionId;
        public readonly bool Survived;
        public readonly long Health;
        public readonly long MaxHealth;
        public readonly long DamageDealt;
        public readonly long DamageTaken;
        public readonly long HealingDone;
        public readonly long ShieldingDone;

        internal UnitSummary(CombatUnit unit)
        {
            Index = unit.Index;
            Team = unit.Team;
            DefinitionId = unit.DefinitionId;
            Survived = unit.IsAlive;
            Health = unit.Health;
            MaxHealth = unit.MaxHealth;
            DamageDealt = unit.DamageDealt;
            DamageTaken = unit.DamageTaken;
            HealingDone = unit.HealingDone;
            ShieldingDone = unit.ShieldingDone;
        }
    }
}
