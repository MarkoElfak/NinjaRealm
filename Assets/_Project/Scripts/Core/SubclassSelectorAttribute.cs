using UnityEngine;

namespace NinjaRealm.Core
{
    /// <summary>
    /// Put this next to [SerializeReference] on a field or list to get a dropdown in the
    /// inspector for picking which subclass to create (for example which effect type).
    /// </summary>
    public sealed class SubclassSelectorAttribute : PropertyAttribute
    {
    }
}
