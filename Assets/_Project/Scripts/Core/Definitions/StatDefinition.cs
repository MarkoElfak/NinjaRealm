using UnityEngine;
using NinjaRealm.Core.Content;

namespace NinjaRealm.Core.Definitions
{
    /// <summary>
    /// One combat stat such as health or armor. Stats are content, so a new stat is a new
    /// asset; the combat simulation decides which stats it reads.
    /// </summary>
    [CreateAssetMenu(menuName = "Ninja Realm/Stat", fileName = "NewStat")]
    public sealed class StatDefinition : ContentDefinition
    {
        [SerializeField, Tooltip("Value a ninja has when its definition does not list this stat.")]
        float defaultValue;

        [SerializeField, Tooltip("Shown with a % sign in the UI, for example crit chance.")]
        bool isPercentage;

        [SerializeField, TextArea]
        string description;

        public override string IdCategory => "stat";

        public float DefaultValue => defaultValue;

        public bool IsPercentage => isPercentage;

        public string Description => description;

        internal void Configure(float defaultValue, bool isPercentage, string description)
        {
            this.defaultValue = defaultValue;
            this.isPercentage = isPercentage;
            this.description = description;
        }
    }
}
