using System.Collections.Generic;
using UnityEngine;

namespace NinjaRealm.Core.Content
{
    /// <summary>
    /// Base class for every piece of game content: clans, ninjas, traits, weapons,
    /// resources, buildings and so on. Each one is a ScriptableObject asset with a
    /// stable ID. Code looks content up by ID through <see cref="ContentDatabase"/>
    /// and never lists content by name, so adding content never needs code changes.
    /// </summary>
    public abstract class ContentDefinition : ScriptableObject
    {
        [SerializeField, Tooltip("Stable ID such as ninja.kaito. Never change it after release: saves and server data point to it.")]
        string id;

        [SerializeField, Tooltip("Name shown to players. Falls back to the asset name when empty.")]
        string displayName;

        public string Id => id;

        public string DisplayName => string.IsNullOrEmpty(displayName) ? name : displayName;

        /// <summary>
        /// The category every ID of this type must start with, for example "ninja".
        /// Null allows any category.
        /// </summary>
        public virtual string IdCategory => null;

        /// <summary>
        /// Extra checks for a definition type, such as a recipe pointing at a missing
        /// component. Return one message per problem.
        /// </summary>
        public virtual IEnumerable<string> ValidateSelf()
        {
            yield break;
        }

        internal void SetId(string value) => id = value;

#if UNITY_EDITOR
        protected virtual void OnValidate()
        {
            if (id != null) id = id.Trim();
        }
#endif
    }
}
