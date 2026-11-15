using NinjaRealm.Presentation.Battle;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace NinjaRealm.Tests
{
    /// <summary>The board builds its tiles and ground, and uses the arena art when it is linked.</summary>
    public class HexBoardViewTests
    {
        GameObject boardObject;
        GameObject arena;
        Texture2D texture;

        [SetUp]
        public void CreateBoard()
        {
            boardObject = new GameObject("Board");
        }

        [TearDown]
        public void DestroyObjects()
        {
            Object.DestroyImmediate(boardObject);
            if (arena != null) Object.DestroyImmediate(arena);
            if (texture != null) Object.DestroyImmediate(texture);
        }

        HexBoardView Board() => boardObject.AddComponent<HexBoardView>();

        static Transform Tile(HexBoardView board, int column, int row) => board.transform.Find($"Hex {column},{row}");

        [Test]
        public void WithoutArt_BuildsPlainTilesAndGround()
        {
            var board = Board();
            board.Build(7, 8);

            Assert.IsNotNull(Tile(board, 0, 0));
            Assert.IsNotNull(Tile(board, 6, 7));
            Assert.IsNotNull(board.transform.Find("Ground"));
            Assert.IsNull(board.Arena);
            Assert.IsNull(Tile(board, 3, 3).GetComponent<MeshRenderer>().sharedMaterial.mainTexture);
        }

        [Test]
        public void WithArt_TexturesTilesAndGroundAndPlacesTheArena()
        {
            var board = Board();
            arena = new GameObject("Test Arena");
            GameObject.CreatePrimitive(PrimitiveType.Cube).transform.SetParent(arena.transform, false);
            texture = new Texture2D(4, 4);
            using (var serialized = new SerializedObject(board))
            {
                serialized.FindProperty("tileTexture").objectReferenceValue = texture;
                serialized.FindProperty("groundTexture").objectReferenceValue = texture;
                serialized.FindProperty("arena").objectReferenceValue = arena;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }

            board.Build(7, 8);
            board.Build(7, 8);

            Assert.AreSame(texture, Tile(board, 3, 3).GetComponent<MeshRenderer>().sharedMaterial.mainTexture);
            var ground = board.transform.Find("Ground").GetComponent<MeshRenderer>().sharedMaterial;
            Assert.AreSame(texture, ground.mainTexture);
            Assert.Greater(ground.mainTextureScale.x, 1f, "the grass repeats");

            Assert.IsNotNull(board.Arena);
            Assert.AreSame(board.transform, board.Arena.transform.parent);
            Assert.AreEqual(1, CountChildren(board.transform, "Arena"), "a rebuild replaces the arena");
        }

        [Test]
        public void Highlight_KeepsTheTileTexture()
        {
            var board = Board();
            texture = new Texture2D(4, 4);
            using (var serialized = new SerializedObject(board))
            {
                serialized.FindProperty("tileTexture").objectReferenceValue = texture;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }

            board.Build(7, 8);
            var hex = new NinjaRealm.Combat.HexCoord(2, 1);
            var renderer = Tile(board, 2, 1).GetComponent<MeshRenderer>();
            var normal = renderer.sharedMaterial;

            board.Highlight(hex, true);
            Assert.AreNotSame(normal, renderer.sharedMaterial);
            Assert.AreSame(texture, renderer.sharedMaterial.mainTexture);

            board.Highlight(null, true);
            Assert.AreSame(normal, renderer.sharedMaterial);
        }

        [Test]
        public void HexTile_HasTextureCoordinates()
        {
            var mesh = PrototypeVisuals.HexTile();
            Assert.AreEqual(mesh.vertexCount, mesh.uv.Length);
        }

        static int CountChildren(Transform parent, string name)
        {
            var count = 0;
            foreach (Transform child in parent)
                if (child.name == name) count++;
            return count;
        }
    }
}
