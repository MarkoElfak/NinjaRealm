using System.Collections.Generic;
using System.Linq;
using NinjaRealm.Presentation.Battle;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace NinjaRealm.Editor
{
    /// <summary>
    /// Dresses the battle board. Builds <see cref="ArenaPrefabPath"/> from the Meshy props and
    /// buildings (fences, braziers, banners and lanterns round the board, a torii gate and the
    /// village behind it), then links the arena and the stone and grass textures to the board in
    /// the battle scene. A model that isn't there yet is left out. Edit the prefab by hand
    /// afterwards if you like, but running this again rebuilds it from <see cref="Layout"/>.
    /// </summary>
    public static class BattleArenaSetup
    {
        public const string ArtRoot = "Assets/_Project/Art/Arena";
        public const string ArenaPrefabPath = ArtRoot + "/BattleArena.prefab";
        public const string TileTexturePath = ArtRoot + "/StoneTile.png";
        public const string GroundTexturePath = ArtRoot + "/Grass.png";

        readonly struct Piece
        {
            public readonly string Model;
            public readonly float X;
            public readonly float Z;
            public readonly float Yaw;
            public readonly float Size;
            public readonly MeshyPlacement.Fit Fit;

            public Piece(string model, float x, float z, float size, float yaw = 0f, MeshyPlacement.Fit fit = MeshyPlacement.Fit.Height)
            {
                Model = model;
                X = x;
                Z = z;
                Yaw = yaw;
                Size = size;
                Fit = fit;
            }
        }

        /// <summary>
        /// Where everything stands, in metres, with the board's centre at the origin. The 7 by 8
        /// board reaches about 4.9 m to each side and 4.7 m front and back; the camera looks
        /// from -z, and a model faces the camera at yaw 0.
        /// </summary>
        static IEnumerable<Piece> Layout()
        {
            // Fences down both sides and along the back, with a gap for the gate.
            foreach (var side in new[] { -1f, 1f })
            {
                for (var z = -2.8f; z <= 3.7f; z += 1.6f) yield return new Piece("Fence", side * 5.7f, z, 1.6f, 90f, MeshyPlacement.Fit.Length);
                yield return new Piece("Fence", side * 4f, 5.6f, 1.6f, 0f, MeshyPlacement.Fit.Length);
                yield return new Piece("Fence", side * 2.4f, 5.6f, 1.6f, 0f, MeshyPlacement.Fit.Length);
            }

            // Braziers at the corners, banners by the gate and halfway down the sides, lanterns between.
            foreach (var side in new[] { -1f, 1f })
            {
                yield return new Piece("Brazier", side * 5.7f, 5.6f, 1.1f);
                yield return new Piece("Brazier", side * 5.7f, -4.4f, 1.1f);
                yield return new Piece("Banner", side * 1.9f, 5.9f, 3f);
                yield return new Piece("Banner", side * 6.5f, 0.8f, 3f, -side * 70f);
                yield return new Piece("StoneLantern", side * 6.5f, 3.2f, 1.4f, -side * 60f);
                yield return new Piece("StoneLantern", side * 6.5f, -2.2f, 1.4f, -side * 60f);
            }

            yield return new Piece("ToriiGate", 0f, 7.8f, 4f);

            // The village behind the arena.
            yield return new Piece("Forge", -5.6f, 11f, 4.5f, 15f);
            yield return new Piece("VillageHouse", 5.8f, 10.8f, 4f, -15f);
            yield return new Piece("VillageHouse", -11.5f, 8.5f, 4f, 35f);
            yield return new Piece("VillageHouse", 11.8f, 7f, 4f, -40f);
            yield return new Piece("Watchtower", 9.8f, 14f, 6.5f, -20f);

            // Trees and rocks to fill the gaps.
            yield return new Piece("CherryTree", -8.6f, 5.8f, 3.2f);
            yield return new Piece("CherryTree", 8.4f, 4.8f, 3f, 120f);
            yield return new Piece("CherryTree", 2.8f, 12f, 3.6f, 60f);
            yield return new Piece("PineTree", -2.2f, 13f, 4.5f);
            yield return new Piece("PineTree", -9.8f, 13.5f, 5f, 90f);
            yield return new Piece("PineTree", 13.5f, 11.5f, 5f, 200f);
            yield return new Piece("PineTree", -14f, 3f, 4.5f, 30f);
            yield return new Piece("PineTree", 14f, 1.5f, 4.5f, 300f);
            yield return new Piece("Rocks", -8.2f, 1.8f, 1f);
            yield return new Piece("Rocks", 8.6f, -1.6f, 1.2f, 140f);
            yield return new Piece("Rocks", -7.6f, -5.4f, 0.9f, 250f);
            yield return new Piece("Rocks", 4.6f, 8.4f, 0.8f, 70f);
        }

        [MenuItem("Ninja Realm/Art/Set Up Battle Arena")]
        public static void SetUp()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogError("[Arena] Leave play mode first.");
                return;
            }

            var arena = BuildArena(out var placed, out var missing);

            if (SceneManager.GetActiveScene().path != BattleSceneBuilder.ScenePath)
            {
                if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
                EditorSceneManager.OpenScene(BattleSceneBuilder.ScenePath);
            }

            var board = Object.FindFirstObjectByType<HexBoardView>();
            if (board == null)
            {
                Debug.LogError($"[Arena] {BattleSceneBuilder.ScenePath} has no board. Run Ninja Realm > Scenes > Create Battle Scene.");
                return;
            }

            LinkArt(board, arena);
            BattleSceneBuilder.ApplyCamera(Camera.main);
            BattleSceneBuilder.ApplyAtmosphere();

            var scene = SceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            Debug.Log($"[Arena] Built {ArenaPrefabPath} with {placed} pieces and linked it to the board in {BattleSceneBuilder.ScenePath}." +
                      (missing.Count > 0 ? $" No model yet for {string.Join(", ", missing)}." : ""));
        }

        /// <summary>Gives the board the arena and the tile and ground textures, where they exist.</summary>
        public static void LinkArt(HexBoardView board, GameObject arena = null)
        {
            using (var serialized = new SerializedObject(board))
            {
                serialized.FindProperty("tileTexture").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Texture2D>(TileTexturePath);
                serialized.FindProperty("groundTexture").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Texture2D>(GroundTexturePath);
                serialized.FindProperty("arena").objectReferenceValue = arena != null ? arena : AssetDatabase.LoadAssetAtPath<GameObject>(ArenaPrefabPath);
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        static GameObject BuildArena(out int placed, out List<string> missing)
        {
            placed = 0;
            missing = new List<string>();
            var root = new GameObject("Battle Arena");
            try
            {
                var counts = new Dictionary<string, int>();
                foreach (var piece in Layout())
                {
                    var path = MeshyPlacement.ModelPath(piece.Model);
                    if (path == null)
                    {
                        if (!missing.Contains(piece.Model)) missing.Add(piece.Model);
                        continue;
                    }

                    counts[piece.Model] = counts.TryGetValue(piece.Model, out var count) ? count + 1 : 1;
                    MeshyPlacement.Place(root.transform, AssetDatabase.LoadAssetAtPath<GameObject>(path), $"{piece.Model} {counts[piece.Model]}", piece.X, piece.Z, piece.Size, piece.Yaw, piece.Fit);
                    placed++;
                }

                ContentDatabaseMenu.EnsureFolder(ArtRoot);
                return PrefabUtility.SaveAsPrefabAsset(root, ArenaPrefabPath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }
    }
}
