using NinjaRealm.Core.Progression;
using NinjaRealm.Presentation.Battle;
using UnityEngine;

namespace NinjaRealm.Presentation.Village
{
    /// <summary>What a building in the village does when you open it.</summary>
    public enum VillageBuildingKind
    {
        /// <summary>Pick a lineup and an opponent, and go to battle.</summary>
        CommandHall,

        /// <summary>Recruit new ninjas, and see every ninja there is.</summary>
        Dojo,

        /// <summary>Level up ninjas with Karma, and buy and set runes.</summary>
        Shrine,

        /// <summary>Forge combined weapons.</summary>
        Blacksmith,

        /// <summary>Makes Gold over time, to collect.</summary>
        Mint,

        /// <summary>Makes Steel over time, to collect.</summary>
        SteelMine,
    }

    /// <summary>
    /// A building the player can click in the village. It sits on the building's root, with the
    /// Meshy model as a child. Without a model it builds a placeholder in code, so the village
    /// works before the art exists. It gets a box collider round the model for clicks.
    /// </summary>
    public sealed class VillageBuilding : MonoBehaviour
    {
        [SerializeField] VillageBuildingKind kind;

        [SerializeField, Min(1f), Tooltip("Height of the placeholder when there is no model.")]
        float placeholderHeight = 4.5f;

        Bounds bounds;
        bool measured;

        public VillageBuildingKind Kind => kind;

        public string DisplayName => NameOf(kind);

        public string Description => DescriptionOf(kind);

        /// <summary>False for a building that is not open yet. Every kind is open now.</summary>
        public bool IsOpen => IsOpenKind(kind);

        /// <summary>The income source this building collects from (see <see cref="IncomeIds"/>), or null.</summary>
        public string IncomeId => IncomeIdOf(kind);

        /// <summary>Where the name label goes: just above the top of the building.</summary>
        public Vector3 LabelPosition
        {
            get
            {
                Measure();
                return new Vector3(bounds.center.x, bounds.max.y + 0.4f, bounds.center.z);
            }
        }

        /// <summary>Sets the kind, for the scene builder and tests.</summary>
        public void SetKind(VillageBuildingKind value) => kind = value;

        void Awake() => EnsureBuilt();

        /// <summary>Builds the placeholder when there is no model, and the collider when there is none. Safe to call more than once.</summary>
        public void EnsureBuilt()
        {
            if (GetComponentInChildren<Renderer>() == null) BuildPlaceholder();
            measured = false;
            Measure();
            if (GetComponent<Collider>() == null) AddCollider();
        }

        void Measure()
        {
            if (measured) return;
            bounds = BoundsOf(gameObject);
            measured = true;
        }

        /// <summary>A box collider round the model, in the building's own space.</summary>
        public BoxCollider AddCollider()
        {
            var world = BoundsOf(gameObject);
            var box = gameObject.AddComponent<BoxCollider>();
            var scale = transform.lossyScale;
            box.center = transform.InverseTransformPoint(world.center);
            box.size = new Vector3(world.size.x / Mathf.Max(0.0001f, scale.x), world.size.y / Mathf.Max(0.0001f, scale.y),
                world.size.z / Mathf.Max(0.0001f, scale.z));
            return box;
        }

        /// <summary>The world bounds of every renderer under an object.</summary>
        public static Bounds BoundsOf(GameObject root)
        {
            var renderers = root.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return new Bounds(root.transform.position, Vector3.zero);
            var result = renderers[0].bounds;
            for (var i = 1; i < renderers.Length; i++) result.Encapsulate(renderers[i].bounds);
            return result;
        }

        /// <summary>A chunky house in the art style's colours: stone base, red walls, a dark blue roof and a coloured sign.</summary>
        void BuildPlaceholder()
        {
            var h = placeholderHeight;
            var root = new GameObject("Placeholder").transform;
            root.SetParent(transform, false);

            var stone = PrototypeVisuals.Lit(new Color(0.55f, 0.53f, 0.5f));
            var wood = PrototypeVisuals.Lit(new Color(0.62f, 0.2f, 0.16f));
            var roof = PrototypeVisuals.Lit(new Color(0.16f, 0.22f, 0.36f));
            var sign = PrototypeVisuals.Lit(AccentOf(kind));

            PrototypeVisuals.Part(PrimitiveType.Cube, root, new Vector3(0f, h * 0.06f, 0f), new Vector3(h * 0.9f, h * 0.12f, h * 0.75f), stone, "Base");
            PrototypeVisuals.Part(PrimitiveType.Cube, root, new Vector3(0f, h * 0.36f, 0f), new Vector3(h * 0.7f, h * 0.5f, h * 0.55f), wood, "Walls");
            // A two-tier roof with wide eaves, like a small pagoda.
            PrototypeVisuals.Part(PrimitiveType.Cube, root, new Vector3(0f, h * 0.64f, 0f), new Vector3(h * 0.95f, h * 0.08f, h * 0.8f), roof, "Eaves");
            PrototypeVisuals.Part(PrimitiveType.Cube, root, new Vector3(0f, h * 0.75f, 0f), new Vector3(h * 0.5f, h * 0.14f, h * 0.4f), wood, "Upper Walls");
            PrototypeVisuals.Part(PrimitiveType.Cube, root, new Vector3(0f, h * 0.86f, 0f), new Vector3(h * 0.7f, h * 0.08f, h * 0.58f), roof, "Upper Eaves");
            PrototypeVisuals.Part(PrimitiveType.Cube, root, new Vector3(0f, h * 0.95f, 0f), new Vector3(h * 0.3f, h * 0.1f, h * 0.22f), roof, "Ridge");
            PrototypeVisuals.Part(PrimitiveType.Cube, root, new Vector3(0f, h * 0.5f, -h * 0.28f), new Vector3(h * 0.3f, h * 0.12f, 0.05f), sign, "Sign");
        }

        public static bool IsOpenKind(VillageBuildingKind kind) => true;

        public static string IncomeIdOf(VillageBuildingKind kind) => kind switch
        {
            VillageBuildingKind.Mint => IncomeIds.Mint,
            VillageBuildingKind.SteelMine => IncomeIds.SteelMine,
            _ => null,
        };

        public static string NameOf(VillageBuildingKind kind) => kind switch
        {
            VillageBuildingKind.CommandHall => "Command Hall",
            VillageBuildingKind.SteelMine => "Steel Mine",
            _ => kind.ToString(),
        };

        public static string DescriptionOf(VillageBuildingKind kind) => kind switch
        {
            VillageBuildingKind.CommandHall => "Fight the campaign: pick a stage, set up your lineup and win its stars.",
            VillageBuildingKind.Dojo => "Recruit new ninjas, four new every day, and see every ninja there is.",
            VillageBuildingKind.Shrine => "Level up your ninjas with Karma, and set runes on the altar for passive bonuses.",
            VillageBuildingKind.Blacksmith => "Forge combined weapons from two components and Steel.",
            VillageBuildingKind.Mint => "Mints Gold while you are away. Come back to collect it.",
            VillageBuildingKind.SteelMine => "Digs Steel while you are away. Come back to collect it.",
            _ => "",
        };

        /// <summary>The placeholder sign's colour, so the buildings are easy to tell apart.</summary>
        public static Color AccentOf(VillageBuildingKind kind) => kind switch
        {
            VillageBuildingKind.CommandHall => new Color(0.85f, 0.25f, 0.2f),
            VillageBuildingKind.Dojo => new Color(0.95f, 0.6f, 0.2f),
            VillageBuildingKind.Shrine => new Color(0.65f, 0.45f, 0.95f),
            VillageBuildingKind.Blacksmith => new Color(0.55f, 0.55f, 0.6f),
            VillageBuildingKind.Mint => PrototypeGui.Gold,
            _ => new Color(0.45f, 0.6f, 0.75f),
        };
    }
}
