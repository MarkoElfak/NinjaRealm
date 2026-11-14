using System;
using System.Collections.Generic;
using NinjaRealm.Core.Content;
using NinjaRealm.Core.Definitions;
using UnityEngine;

namespace NinjaRealm.Core.Lineups
{
    /// <summary>
    /// The player's lineup while they edit it: place, move, swap and remove ninjas, and equip
    /// weapons, within the lineup rules. Each ninja can be on the board once.
    /// </summary>
    public sealed class PlayerLineup
    {
        readonly List<LineupSlot> slots = new List<LineupSlot>();

        public PlayerLineup(int maxUnits = LineupRules.MaxUnits)
        {
            MaxUnits = maxUnits;
        }

        public int MaxUnits { get; }

        public IReadOnlyList<LineupSlot> Slots => slots;

        public bool IsFull => slots.Count >= MaxUnits;

        public event Action Changed;

        public LineupSlot At(int column, int rowFromBack)
        {
            foreach (var slot in slots)
                if (slot.column == column && slot.rowFromBack == rowFromBack) return slot;
            return null;
        }

        public bool Contains(NinjaDefinition ninja)
        {
            foreach (var slot in slots)
                if (slot.ninja == ninja) return true;
            return false;
        }

        /// <summary>Puts a ninja on an empty hex. Fails when the lineup is full, the ninja is already placed, or the hex is taken.</summary>
        public LineupSlot Place(NinjaDefinition ninja, int column, int rowFromBack)
        {
            if (ninja == null || IsFull || Contains(ninja) || !LineupRules.IsOnBoard(column, rowFromBack) || At(column, rowFromBack) != null)
                return null;

            var slot = new LineupSlot(ninja, column, rowFromBack);
            slots.Add(slot);
            Changed?.Invoke();
            return slot;
        }

        /// <summary>Moves a placed ninja. If the hex is taken, the two ninjas swap places.</summary>
        public bool Move(LineupSlot slot, int column, int rowFromBack)
        {
            if (slot == null || !slots.Contains(slot) || !LineupRules.IsOnBoard(column, rowFromBack)) return false;

            var other = At(column, rowFromBack);
            if (other == slot) return true;
            if (other != null)
            {
                other.column = slot.column;
                other.rowFromBack = slot.rowFromBack;
            }

            slot.column = column;
            slot.rowFromBack = rowFromBack;
            Changed?.Invoke();
            return true;
        }

        public bool Remove(LineupSlot slot)
        {
            if (!slots.Remove(slot)) return false;
            Changed?.Invoke();
            return true;
        }

        /// <summary>True when any ninja in the lineup holds this weapon.</summary>
        public bool IsEquipped(WeaponDefinition weapon)
        {
            foreach (var slot in slots)
                if (slot.weapons.Contains(weapon)) return true;
            return false;
        }

        public bool Equip(LineupSlot slot, WeaponDefinition weapon)
        {
            if (slot == null || weapon == null || !slots.Contains(slot) || slot.weapons.Count >= LineupRules.MaxWeaponsPerNinja)
                return false;

            slot.weapons.Add(weapon);
            Changed?.Invoke();
            return true;
        }

        public bool Unequip(LineupSlot slot, int weaponIndex)
        {
            if (slot == null || weaponIndex < 0 || weaponIndex >= slot.weapons.Count) return false;
            slot.weapons.RemoveAt(weaponIndex);
            Changed?.Invoke();
            return true;
        }

        public void Clear()
        {
            slots.Clear();
            Changed?.Invoke();
        }

        /// <summary>Replaces the lineup with copies of other slots, skipping any that break the rules.</summary>
        public void SetSlots(IEnumerable<LineupSlot> source)
        {
            slots.Clear();
            foreach (var slot in source)
            {
                if (slot?.ninja == null) continue;
                var placed = Place(slot.ninja, slot.column, slot.rowFromBack);
                if (placed == null) continue;
                foreach (var weapon in slot.weapons)
                    Equip(placed, weapon);
            }

            Changed?.Invoke();
        }

        public List<TraitCount> Synergies() => LineupSynergies.Count(slots);

        // ---------------------------------------------------------------- saving

        [Serializable]
        sealed class SavedLineup
        {
            public List<SavedSlot> slots = new List<SavedSlot>();
        }

        [Serializable]
        sealed class SavedSlot
        {
            public string ninja;
            public int column;
            public int row;
            public List<string> weapons = new List<string>();
        }

        /// <summary>The lineup as JSON with content IDs, for saving.</summary>
        public string ToJson()
        {
            var saved = new SavedLineup();
            foreach (var slot in slots)
            {
                var entry = new SavedSlot { ninja = slot.ninja.Id, column = slot.column, row = slot.rowFromBack };
                foreach (var weapon in slot.weapons) entry.weapons.Add(weapon.Id);
                saved.slots.Add(entry);
            }

            return JsonUtility.ToJson(saved);
        }

        /// <summary>Loads a lineup saved with ToJson. Unknown IDs are skipped, so old saves keep working.</summary>
        public bool LoadJson(string json, ContentDatabase database)
        {
            if (string.IsNullOrEmpty(json) || database == null) return false;

            SavedLineup saved;
            try
            {
                saved = JsonUtility.FromJson<SavedLineup>(json);
            }
            catch (ArgumentException)
            {
                return false;
            }

            if (saved?.slots == null) return false;

            var loaded = new List<LineupSlot>();
            foreach (var entry in saved.slots)
            {
                if (entry == null || !database.TryGet<NinjaDefinition>(entry.ninja, out var ninja)) continue;
                var slot = new LineupSlot(ninja, entry.column, entry.row);
                if (entry.weapons != null)
                    foreach (var id in entry.weapons)
                        if (database.TryGet<WeaponDefinition>(id, out var weapon)) slot.weapons.Add(weapon);
                loaded.Add(slot);
            }

            SetSlots(loaded);
            return true;
        }
    }
}
