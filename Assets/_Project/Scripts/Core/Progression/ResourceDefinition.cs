using System;
using NinjaRealm.Core.Content;
using UnityEngine;

namespace NinjaRealm.Core.Progression
{
    /// <summary>
    /// A currency the player earns and spends, such as Gold or Karma. Costs and rewards point at
    /// resources by ID, so a new resource is one new asset.
    /// </summary>
    [CreateAssetMenu(menuName = "Ninja Realm/Resource", fileName = "NewResource")]
    public sealed class ResourceDefinition : ContentDefinition
    {
        [SerializeField, TextArea] string description;
        [SerializeField] Color color = Color.white;
        [SerializeField] Sprite icon;

        public override string IdCategory => "resource";

        public string Description => description;

        public Color Color => color;

        public Sprite Icon => icon;

        internal void Configure(string description, Color color)
        {
            this.description = description;
            this.color = color;
        }
    }

    /// <summary>An amount of one resource: a cost, a reward or a starting balance.</summary>
    [Serializable]
    public sealed class ResourceAmount
    {
        public ResourceDefinition resource;
        public int amount;

        public ResourceAmount()
        {
        }

        public ResourceAmount(ResourceDefinition resource, int amount)
        {
            this.resource = resource;
            this.amount = amount;
        }

        public override string ToString() => $"{amount} {(resource != null ? resource.DisplayName : "?")}";
    }
}
