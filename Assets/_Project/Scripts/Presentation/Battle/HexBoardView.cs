using NinjaRealm.Combat;
using UnityEngine;

namespace NinjaRealm.Presentation.Battle
{
    /// <summary>Builds the hex board: stone tiles, a slightly different tint for each team's half, and the ground.</summary>
    public sealed class HexBoardView : MonoBehaviour
    {
        [SerializeField] Color tileColor = new Color(0.62f, 0.6f, 0.56f);
        [SerializeField] Color tileColorAlt = new Color(0.56f, 0.54f, 0.5f);
        [SerializeField] Color groundColor = new Color(0.36f, 0.5f, 0.28f);
        [SerializeField, Range(0f, 0.3f)] float teamTint = 0.12f;

        [SerializeField] Color validColor = new Color(0.45f, 0.85f, 0.45f);
        [SerializeField] Color invalidColor = new Color(0.9f, 0.35f, 0.3f);

        readonly System.Collections.Generic.Dictionary<HexCoord, MeshRenderer> tiles =
            new System.Collections.Generic.Dictionary<HexCoord, MeshRenderer>();
        readonly System.Collections.Generic.Dictionary<HexCoord, Material> tileMaterials =
            new System.Collections.Generic.Dictionary<HexCoord, Material>();
        HexCoord? highlighted;

        public int Columns { get; private set; } = 7;

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
                renderer.sharedMaterial = PrototypeVisuals.Lit(valid ? validColor : invalidColor);
        }

        public void Build(int columns, int rows)
        {
            Columns = columns;
            Rows = rows;
            tiles.Clear();
            tileMaterials.Clear();
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
                    tile.AddComponent<MeshFilter>().sharedMesh = mesh;

                    var baseColor = (column + row) % 2 == 0 ? tileColor : tileColorAlt;
                    var color = Color.Lerp(baseColor, PrototypeVisuals.TeamColor(team), teamTint);
                    var renderer = tile.AddComponent<MeshRenderer>();
                    renderer.sharedMaterial = PrototypeVisuals.Lit(color);
                    var hex = new HexCoord(column, row);
                    tiles[hex] = renderer;
                    tileMaterials[hex] = renderer.sharedMaterial;
                }
            }

            var width = HexLayout.Width * (columns + 0.5f);
            var depth = HexLayout.RowSpacing * rows;
            var groundSize = Mathf.Max(width, depth) * 2.2f;
            PrototypeVisuals.Part(PrimitiveType.Cylinder, transform, new Vector3(0f, -0.3f, 0f),
                new Vector3(groundSize, 0.05f, groundSize), PrototypeVisuals.Lit(groundColor), "Ground");
        }
    }
}
