using System;
using System.Collections.Generic;
using UnityEngine;

namespace NinjaRealm.Core.Content
{
    /// <summary>
    /// The single place the game looks up content by ID. It holds a list of every
    /// <see cref="ContentDefinition"/> asset, filled by the editor menu
    /// Ninja Realm > Content > Rebuild Database.
    /// </summary>
    [CreateAssetMenu(menuName = "Ninja Realm/Content Database", fileName = "ContentDatabase")]
    public sealed class ContentDatabase : ScriptableObject
    {
        [SerializeField] List<ContentDefinition> definitions = new List<ContentDefinition>();

        Dictionary<string, ContentDefinition> byId;

        public IReadOnlyList<ContentDefinition> Definitions => definitions;

        public bool IsInitialized => byId != null;

        public int Count
        {
            get
            {
                EnsureInitialized();
                return byId.Count;
            }
        }

        /// <summary>Builds the ID lookup. Called lazily on first use; call it at boot to pay the cost up front.</summary>
        public void Initialize()
        {
            byId = new Dictionary<string, ContentDefinition>(definitions.Count, StringComparer.Ordinal);
            foreach (var definition in definitions)
            {
                if (definition == null || !ContentId.IsValid(definition.Id)) continue;

                if (byId.TryGetValue(definition.Id, out var existing))
                {
                    Debug.LogError($"[Content] Duplicate ID '{definition.Id}' on '{definition.name}'. Keeping '{existing.name}'.", definition);
                    continue;
                }

                byId.Add(definition.Id, definition);
            }
        }

        public bool Contains(string id)
        {
            EnsureInitialized();
            return id != null && byId.ContainsKey(id);
        }

        public bool TryGet<T>(string id, out T definition) where T : ContentDefinition
        {
            EnsureInitialized();
            if (id != null && byId.TryGetValue(id, out var found) && found is T typed)
            {
                definition = typed;
                return true;
            }

            definition = null;
            return false;
        }

        public T Get<T>(string id) where T : ContentDefinition
        {
            if (TryGet<T>(id, out var definition)) return definition;
            throw new KeyNotFoundException($"No {typeof(T).Name} with ID '{id}' in the content database.");
        }

        /// <summary>All definitions of a type, in database order, which is sorted by ID when rebuilt.</summary>
        public IEnumerable<T> GetAll<T>() where T : ContentDefinition
        {
            EnsureInitialized();
            foreach (var definition in definitions)
            {
                if (definition is T typed && byId.TryGetValue(typed.Id, out var registered) && registered == typed)
                    yield return typed;
            }
        }

        internal void SetDefinitions(IEnumerable<ContentDefinition> newDefinitions)
        {
            definitions = new List<ContentDefinition>(newDefinitions);
            byId = null;
        }

        void OnEnable() => byId = null;

        void EnsureInitialized()
        {
            if (byId == null) Initialize();
        }
    }
}
