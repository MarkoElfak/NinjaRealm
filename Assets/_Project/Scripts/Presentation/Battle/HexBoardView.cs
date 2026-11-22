using NinjaRealm.Combat;
using UnityEngine;

namespace NinjaRealm.Presentation.Battle
{
    /// <summary>
    /// Builds the hex board: stone tiles, a slightly different tint for each team's half, the
    /// ground, and the arena around it. The art (textures and the arena prefab) is linked by
    /// Ninja Realm > Art > Set Up Battle Arena; without it the board uses plain colours.
    /// </summary>
    public sealed class HexBoardView : MonoBehaviour
    {
        [SerializeField] Color tileColor = new Color(0.62f, 0.6f, 0.56f);
        [SerializeField] Color tileColorAlt = new Color(0.56f, 0.54f, 0.5f);
        [SerializeField] Color groundColor = new Color(0.36f, 0.5f, 0.28f);
        [SerializeField, Range(0f, 0.3f)] float teamTint = 0.12f;

        [Header("Art")]
        [SerializeField] Texture2D tileTexture;
        [SerializeField] Texture2D groundTexture;
        [SerializeField, Min(0.5f)] float groundTextureMetres = 6f;
        [SerializeField, Min(1f)] float groundRadius = 45f;
        [SerializeField] GameObject arena;

        // Tints for textured tiles: the texture carries the stone colour.
        static readonly Color TexturedTile = new Color(1f, 1f, 1f);
        static readonly Color TexturedTileAlt = new Color(0.9f, 0.9f, 0.88f);

        [SerializeField] Color validColor = new Color(0.45f, 0.85f, 0.45f);
        [SerializeField] Color invalidColor = new Color(0.9f, 0.35f, 0.3f);

        readonly System.Collections.Generic.Dictionary<HexCoord, MeshRenderer> tiles =
            new System.Collections.Generic.Dictionary<HexCoord, MeshRenderer>();
        readonly System.Collections.Generic.Dictionary<HexCoord, Material> tileMaterials =
            new System.Collections.Generic.Dictionary<HexCoord, Material>();
        readonly System.Collections.Generic.Dictionary<HexCoord, Material> baseMaterials =
            new System.Collections.Generic.Dictionary<HexCoord, Material>();
        readonly System.Collections.Generic.List<GameObject> tileMarks = new System.Collections.Generic.List<GameObject>();
        HexCoord? highlighted;
        Material groundMaterial;

        /// <summary>How far a special tile's stone takes on the tile's colour.</summary>
        [SerializeField, Range(0f, 1f)] float specialTint = 0.45f;

        public int Columns { get; private set; } = 7;

        /// <summary>The arena placed around the board, if one is linked.</summary>
        public GameObject Arena { get; private set; }

        public int Rows { get; private set; } = 8;

        public Vector3 ToWorld(HexCoord hex) => transform.TransformPoint(HexLayout.ToWorld(hex, Columns, Rows));

        /// <summary>The hex under a screen point, if the point is over the board.</summary>
        public bool TryGetHex(Camera viewCamera, Vector2 screenPoint, out HexCoord hex)
        {
            hex = default;
            if (viewCamera == null) return false;

            var ray = viewCamera.ScreenPointToRay(screenPoint);
            var plane = new Plane(transform.up, transform.position);
            if (!plane.Raycast(ray, out var distance)) return false;

            var point = ray.GetPoint(distance);
            var best = float.MaxValue;
            foreach (var tile in tiles)
            {
                var d = (ToWorld(tile.Key) - point).sqrMagnitude;
                if (d < best)
                {
                    best = d;
                    hex = tile.Key;
                }
            }

            return best <= HexLayout.Radius * HexLayout.Radius;
        }

        /// <summary>The point on the board plane under a screen point, for dragging.</summary>
        public bool TryGetPoint(Camera viewCamera, Vector2 screenPoint, out Vector3 point)
        {
            point = default;
            if (viewCamera == null) return false;
            var ray = viewCamera.ScreenPointToRay(screenPoint);
            var plane = new Plane(transform.up, transform.position);
            if (!plane.Raycast(ray, out var distance)) return false;
            point = ray.GetPoint(distance);
            return true;
        }

        /// <summary>Colours one hex as a valid or invalid drop target. Pass null to clear.</summary>
        public void Highlight(HexCoord? hex, bool valid)
        {
            if (highlighted.HasValue && tiles.TryGetValue(highlighted.Value, out var previous))
                previous.sharedMaterial = tileMaterials[highlighted.Value];

            highlighted = hex;
            if (hex.HasValue && tiles.TryGetValue(hex.Value, out var renderer))
                renderer.sharedMaterial = PrototypeVisuals.Lit(valid ? validColor : invalidColor, tileTexture);
        }

        /// <summary>
        /// Marks special tiles: the stone takes on the tile's colour and a coloured inlay sits in
        /// its middle. Replaces the tiles shown before; pass nothing to clear them.
        /// </summary>
        public void ShowTiles(System.Collections.Generic.IEnumerable<(HexCoord hex, Color color)> special)
        {
            foreach (var mark in tileMarks)
                if (mark != null) PrototypeVisuals.DestroySafe(mark);
            tileMarks.Clear();
            foreach (var entry in baseMaterials)
            {
                tileMaterials[entry.Key] = entry.Value;
                if (tiles.TryGetValue(entry.Key, out var renderer)) renderer.sharedMaterial = entry.Value;
            }

            if (special == null) return;
            var mesh = PrototypeVisuals.HexTile();
            foreach (var (hex, color) in special)
            {
                if (!tiles.TryGetValue(hex, out var renderer) || !baseMaterials.TryGetValue(hex, out var baseMaterial)) continue;

                var stone = Color.Lerp(PrototypeVisuals.ColorOf(baseMaterial), color, specialTint);
                var material = PrototypeVisuals.Lit(stone, tileTexture);
                tileMaterials[hex] = material;
                if (highlighted != hex) renderer.sharedMaterial = material;

                var inlay = new GameObject($"Tile mark {hex.Column},{hex.Row}");
                inlay.transform.SetParent(renderer.transform, false);
                inlay.transform.localPosition = new Vector3(0f, 0.015f, 0f);
                inlay.transform.localScale = new Vector3(0.55f, 0.1f, 0.55f);
                inlay.AddComponent<MeshFilter>().sharedMesh = mesh;
                var inlayRenderer = inlay.AddComponent<MeshRenderer>();
                inlayRenderer.sharedMaterial = PrototypeVisuals.Unlit(Color.Lerp(color, Color.white, 0.15f));
                inlayRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                tileMarks.Add(inlay);
            }
        }

        public void Build(int columns, int rows)
        {
            Columns = columns;
            Rows = rows;
            tiles.Clear();
            tileMaterials.Clear();
            baseMaterials.Clear();
            tileMarks.Clear();
            highlighted = null;

            for (var i = transform.childCount - 1; i >= 0; i--)
                PrototypeVisuals.DestroySafe(transform.GetChild(i).gameObject);

            var mesh = PrototypeVisuals.HexTile();
            for (var row = 0; row < rows; row++)
            {
                var team = row < rows / 2 ? 0 : 1;
                for (var column = 0; column < columns; column++)
                {
                    var tile = new GameObject($"Hex {column},{row}");
                    tile.transform.SetParent(transform, false);
                    tile.transform.localPosition = HexLayout.ToWorld(new HexCoord(column, row), columns, rows);

                    // Turning a hex by a sixth of a circle keeps its shape, and varies the stone.
                    tile.transform.localRotation = Quaternion.Euler(0f, 60f * ((column * 5 + row * 3) % 6), 0f);
                    tile.AddComponent<MeshFilter>().sharedMesh = mesh;

                    var even = (column + row) % 2 == 0;
                    var baseColor = tileTexture != null ? (even ? TexturedTile : TexturedTileAlt) : even ? tileColor : tileColorAlt;
                    var color = Color.Lerp(baseColor, PrototypeVisuals.TeamColor(team), teamTint);
                    var renderer = tile.AddComponent<MeshRenderer>();
                    renderer.sharedMaterial = PrototypeVisuals.Lit(color, tileTexture);
                    var hex = new HexCoord(column, row);
                    tiles[hex] = renderer;
                    tileMaterials[hex] = renderer.sharedMaterial;
                    baseMaterials[hex] = renderer.sharedMaterial;
                }
            }

            BuildGround(columns, rows);

            Arena = null;
            if (arena != null)
            {
                Arena = Instantiate(arena, transform, false);
                Arena.name = "Arena";
            }
        }

        void BuildGround(int columns, int rows)
        {
            var width = HexLayout.Width * (columns + 0.5f);
            var depth = HexLayout.RowSpacing * rows;
            var groundSize = Mathf.Max(Mathf.Max(width, depth) * 2.2f, groundRadius * 2f);

            Material material;
            if (groundTexture != null)
            {
                // A cylinder's cap maps the texture once across its diameter, so repeat it.
                if (groundMaterial == null)
                {
                    groundMaterial = PrototypeVisuals.NewLit(Color.white, groundTexture);
                    groundMaterial.name = "Ground";
                }

                var repeats = Vector2.one * (groundSize / groundTextureMetres);
                groundMaterial.mainTextureScale = repeats;
                if (groundMaterial.HasProperty("_BaseMap")) groundMaterial.SetTextureScale("_BaseMap", repeats);
                material = groundMaterial;
            }
            else
            {
                material = PrototypeVisuals.Lit(groundColor);
            }

            PrototypeVisuals.Part(PrimitiveType.Cylinder, transform, new Vector3(0f, -0.3f, 0f),
                new Vector3(groundSize, 0.05f, groundSize), material, "Ground");
        }
    }
}
