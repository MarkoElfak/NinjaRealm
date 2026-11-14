using System.Collections.Generic;
using NinjaRealm.Core.Definitions;

namespace NinjaRealm.Core.Lineups
{
    /// <summary>Board and lineup limits shared by the lineup screen, enemy lineups and validation.</summary>
    public static class LineupRules
    {
        public const int Columns = 7;

        /// <summary>Rows on each team's half.</summary>
        public const int RowsPerTeam = 4;

        public const int MaxUnits = 6;

        public const int MaxWeaponsPerNinja = 3;

        public static bool IsOnBoard(int column, int rowFromBack) =>
            column >= 0 && column < Columns && rowFromBack >= 0 && rowFromBack < RowsPerTeam;

        /// <summary>Problems with a lineup, one message per problem. Empty when the lineup can fight.</summary>
        public static IEnumerable<string> Validate(IReadOnlyList<LineupSlot> slots, int maxUnits = MaxUnits)
        {
            if (slots.Count == 0) yield return "has no ninjas.";
            if (slots.Count > maxUnits) yield return $"has {slots.Count} ninjas; the most is {maxUnits}.";

            var hexes = new HashSet<int>();
            for (var i = 0; i < slots.Count; i++)
            {
                var slot = slots[i];
                var label = $"slot {i + 1}";
                if (slot == null)
                {
                    yield return $"{label} is empty.";
                    continue;
                }

                if (slot.ninja == null) yield return $"{label} has no ninja.";
                if (!IsOnBoard(slot.column, slot.rowFromBack))
                    yield return $"{label} is off the board at column {slot.column}, row {slot.rowFromBack}.";
                else if (!hexes.Add(slot.column * 10 + slot.rowFromBack))
                    yield return $"{label} shares its hex with another ninja.";

                if (slot.weapons.Count > MaxWeaponsPerNinja)
                    yield return $"{label} holds {slot.weapons.Count} weapons; the most is {MaxWeaponsPerNinja}.";
                foreach (var weapon in slot.weapons)
                    if (weapon == null) yield return $"{label} has an empty weapon slot.";
            }
        }
    }

    /// <summary>A trait and how many different ninjas in a lineup have it.</summary>
    public readonly struct TraitCount
    {
        public readonly TraitDefinition Trait;
        public readonly int Count;

        public TraitCount(TraitDefinition trait, int count)
        {
            Trait = trait;
            Count = count;
        }

        /// <summary>The tier this count reaches, or null.</summary>
        public TraitTier ActiveTier => Trait.GetActiveTier(Count);

        /// <summary>The next tier to reach, or null when the highest is active.</summary>
        public TraitTier NextTier
        {
            get
            {
                foreach (var tier in Trait.Tiers)
                    if (tier != null && tier.requiredCount > Count) return tier;
                return null;
            }
        }
    }

    public static class LineupSynergies
    {
        /// <summary>
        /// Counts each trait once per different ninja, like the combat simulation does. Active
        /// traits come first, then by count, then by ID.
        /// </summary>
        public static List<TraitCount> Count(IEnumerable<LineupSlot> slots)
        {
            var ninjasByTrait = new Dictionary<TraitDefinition, HashSet<string>>();
            foreach (var slot in slots)
            {
                if (slot?.ninja == null) continue;
                foreach (var trait in slot.ninja.Traits)
                {
                    if (!ninjasByTrait.TryGetValue(trait, out var ninjas))
                        ninjasByTrait[trait] = ninjas = new HashSet<string>();
                    ninjas.Add(slot.ninja.Id);
                }
            }

            var result = new List<TraitCount>();
            foreach (var entry in ninjasByTrait)
                result.Add(new TraitCount(entry.Key, entry.Value.Count));

            result.Sort((a, b) =>
            {
                var active = (b.ActiveTier != null).CompareTo(a.ActiveTier != null);
                if (active != 0) return active;
                var count = b.Count.CompareTo(a.Count);
                return count != 0 ? count : string.CompareOrdinal(a.Trait.Id, b.Trait.Id);
            });
            return result;
        }
    }
}
