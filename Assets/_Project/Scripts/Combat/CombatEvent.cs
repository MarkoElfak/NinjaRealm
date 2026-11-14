namespace NinjaRealm.Combat
{
    public enum CombatEventType
    {
        FightStart,
        Move,
        Attack,
        Dodge,
        Damage,
        Heal,
        Shield,
        CastStart,
        Cast,
        StatusApplied,
        Death,
        TraitActivated,
        Overtime,
        FightEnd,
    }

    /// <summary>
    /// One thing that happened in a fight. Presentation plays these back; the damage log reads them.
    /// Source and Target are unit indexes, or -1 when not used.
    /// </summary>
    public readonly struct CombatEvent
    {
        public readonly int Tick;
        public readonly CombatEventType Type;
        public readonly int Source;
        public readonly int Target;

        /// <summary>Damage, heal or shield amount (fixed-point), or the winning team for FightEnd.</summary>
        public readonly long Amount;

        /// <summary>Extra detail: the hex for Move (column * 100 + row), the StatusKind, for Damage 1 on a crit and 2 for damage over time (burn), the trait tier count.</summary>
        public readonly int Detail;

        /// <summary>A content ID for events that name something, such as the trait for TraitActivated or the skill for Cast.</summary>
        public readonly string Id;

        public CombatEvent(int tick, CombatEventType type, int source, int target, long amount = 0, int detail = 0, string id = null)
        {
            Tick = tick;
            Type = type;
            Source = source;
            Target = target;
            Amount = amount;
            Detail = detail;
            Id = id;
        }

        public override string ToString() => $"{Tick}:{Type} {Source}->{Target} {Amount} {Detail} {Id}";
    }
}
