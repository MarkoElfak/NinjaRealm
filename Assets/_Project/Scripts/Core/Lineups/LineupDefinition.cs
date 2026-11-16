using System.Collections.Generic;
using NinjaRealm.Core.Content;
using UnityEngine;

namespace NinjaRealm.Core.Lineups
{
    /// <summary>
    /// A saved team: who stands where and with which weapons. Campaign stages fight one at a
    /// level of their own; later also tutorial fights or other players' defence lineups.
    /// </summary>
    [CreateAssetMenu(menuName = "Ninja Realm/Lineup", fileName = "NewLineup")]
    public sealed class LineupDefinition : ContentDefinition
    {
        [SerializeField, TextArea] string description;

        [SerializeField] List<LineupSlot> slots = new List<LineupSlot>();

        public override string IdCategory => "lineup";

        public string Description => description;

        public IReadOnlyList<LineupSlot> Slots => slots;

        public override IEnumerable<string> ValidateSelf() => LineupRules.Validate(slots);

        internal void Configure(string description, IEnumerable<LineupSlot> slots)
        {
            this.description = description;
            this.slots = new List<LineupSlot>(slots);
        }
    }
}
