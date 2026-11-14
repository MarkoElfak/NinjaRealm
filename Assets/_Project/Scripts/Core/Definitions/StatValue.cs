using System;

namespace NinjaRealm.Core.Definitions
{
    /// <summary>A base value for one stat, as listed on a ninja.</summary>
    [Serializable]
    public struct StatValue
    {
        public StatDefinition stat;
        public float value;

        public StatValue(StatDefinition stat, float value)
        {
            this.stat = stat;
            this.value = value;
        }
    }
}
