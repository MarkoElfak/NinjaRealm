using System.Collections.Generic;
using NinjaRealm.Core.Content;
using UnityEngine;

namespace NinjaRealm.Core.Lineups
{
    /// <summary>
    /// A saved team: an enemy to fight in the prototype, and later bosses, tutorial fights or
    /// other players' defence lineups.
    /// </summary>
    [CreateAssetMenu(menuName = "Ninja Realm/Lineup", fileName = "NewLineup")]
    public sealed class LineupDefinition : ContentDefinition
    {
        [SerializeField, TextArea] string description;

        [SerializeField, Range(1, 5), Tooltip("Shown as stars when picking an opponent.")]
        int difficulty = 1;

        [SerializeField] List<LineupSlot> slots = new List<LineupSlot>();

        public override string IdCategory => "lineup";

        public string Description => description;

        public int Difficulty => difficulty;

        public IReadOnlyList<LineupSlot> Slots => slots;

        public override IEnumerable<string> ValidateSelf() => LineupRules.Validate(slots);

        internal void Configure(string description, int difficulty, IEnumerable<LineupSlot> slots)
        {
            this.description = description;
            this.difficulty = difficulty;
            this.slots = new List<LineupSlot>(slots);
        }
    }
}
