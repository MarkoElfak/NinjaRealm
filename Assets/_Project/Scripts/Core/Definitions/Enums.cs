namespace NinjaRealm.Core.Definitions
{
    public enum Rarity
    {
        Common,
        Rare,
        Epic,
        Legendary,
    }

    /// <summary>Every ninja has exactly one trait of each kind.</summary>
    public enum TraitKind
    {
        Element,
        Class,
    }

    public enum WeaponTier
    {
        /// <summary>A basic component that drops from battles.</summary>
        Component,

        /// <summary>Forged from two components.</summary>
        Combined,
    }
}
