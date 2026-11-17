using System;
using NinjaRealm.Core.Definitions;

namespace NinjaRealm.Core.Progression
{
    /// <summary>
    /// A village a new clan can call home, picked once on the new game screen. It belongs to an
    /// element: its ninjas show up more often in the Dojo, and the village flies its colour.
    /// Part of <see cref="ProgressionDefinition"/>.
    /// </summary>
    [Serializable]
    public sealed class HomeVillage
    {
        public string id;
        public string displayName;
        public TraitDefinition element;

        /// <summary>One or two sentences for the new game screen.</summary>
        public string description;

        /// <summary>
        /// How likely a Dojo offer of the home element is, against any other ninja of the same
        /// rarity, in basis points (20000 = twice as likely).
        /// </summary>
        public int dojoAffinity = 20000;

        /// <summary>True when a ninja belongs to this village's element.</summary>
        public bool IsHome(NinjaDefinition ninja) => ninja != null && element != null && ninja.Element == element;
    }
}
