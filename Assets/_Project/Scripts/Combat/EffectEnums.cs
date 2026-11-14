namespace NinjaRealm.Combat
{
    public enum EffectScope
    {
        /// <summary>The ninja that owns the effect, such as the one holding a weapon.</summary>
        Self,

        /// <summary>Every ninja on the team that has the trait.</summary>
        TraitMembers,

        /// <summary>Every ninja on the team.</summary>
        AllAllies,

        /// <summary>Every ninja on the team that does not have the trait.</summary>
        OtherAllies,

        /// <summary>The team as a whole, once, for example a periodic team heal.</summary>
        Team,
    }

    public enum ModifierOperation
    {
        /// <summary>Adds a flat amount, for example +20 armor.</summary>
        Add,

        /// <summary>Adds a percentage of the base value, for example +10% attack damage.</summary>
        AddPercent,
    }

    public enum EffectTrigger
    {
        FightStart,
        OnAttackHit,
        OnAbilityCast,
        Periodic,
        HealthBelowThreshold,
    }

    public enum TargetKind
    {
        Self,
        CurrentTarget,
        CurrentTargetAndAdjacent,
        AdjacentEnemies,
        EnemiesInRadiusOfTarget,
        EnemiesInRadiusOfSelf,
        LowestHealthAllies,
        AllAllies,
        RandomEnemies,
    }

    public enum DamageType
    {
        Physical,
        Magic,
        True,
    }

    public enum StatusKind
    {
        /// <summary>Cannot move, attack or cast.</summary>
        Stun,

        /// <summary>Enemies must attack the source.</summary>
        Taunt,

        /// <summary>Loses value% of max health over the duration.</summary>
        Burn,

        /// <summary>Attack speed reduced by value%.</summary>
        Slow,

        /// <summary>Healing received reduced by value%.</summary>
        HealingReduction,

        /// <summary>Damage taken reduced by value%.</summary>
        DamageReduction,
    }
}
