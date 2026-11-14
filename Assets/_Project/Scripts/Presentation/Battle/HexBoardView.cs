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

        public int Columns { get; private set; } = 7;

        public int Rows { get; private set; } = 8;

        public Vector3 ToWorld(HexCoord hex) => transform.TransformPoint(HexLayout.ToWorld(hex, Columns, Rows));

        public void Build(int columns, int rows)
        {
            Columns = columns;
            Rows = rows;

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
