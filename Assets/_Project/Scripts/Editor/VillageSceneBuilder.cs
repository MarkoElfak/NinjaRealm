using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NinjaRealm.Core.Content;
using NinjaRealm.Presentation;
using NinjaRealm.Presentation.Village;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace NinjaRealm.Editor
{
    /// <summary>
    /// Creates the village hub scene, where the game starts: a stone plaza on grass, the six
    /// buildings round it (each a <see cref="VillageBuilding"/>), and houses, trees and lanterns
    /// from the Meshy art. A building without a Meshy model yet gets a placeholder in play mode,
    /// so run this again after adding a model. The layout is in <see cref="Layout"/>.
    /// </summary>
    public static class VillageSceneBuilder
    {
        public const string ScenePath = "Assets/Scenes/Village.unity";
        public const string ArtRoot = "Assets/_Project/Art/Village";
        const string GroundMaterialPath = ArtRoot + "/VillageGround.mat";
        const string PlazaMaterialPath = ArtRoot + "/VillagePlaza.mat";

        static readonly Color SkyColor = new Color(0.58f, 0.7f, 0.86f);

        /// <summary>One thing in the village. A building has a kind; decor has none.</summary>
        readonly struct Piece
        {
            public readonly string Model;
            public readonly float X;
            public readonly float Z;
            public readonly float Size;
            public readonly float Yaw;
            public readonly VillageBuildingKind? Building;

            public Piece(string model, float x, float z, float size, float yaw = 0f, VillageBuildingKind? building = null)
            {
                Model = model;
                X = x;
                Z = z;
                Size = size;
                Yaw = yaw;
                Building = building;
            }
        }

        /// <summary>
        /// The Meshy model each building uses: Buildings/Name/Name.fbx. The Blacksmith reuses the
        /// arena's Forge.
        /// </summary>
        public static string ModelOf(VillageBuildingKind kind) => kind switch
        {
            VillageBuildingKind.Blacksmith => "Forge",
            _ => kind.ToString(),
        };

        /// <summary>
        /// Where everything stands, in metres. The plaza is centred at (0, 2); the camera looks
        /// from -z, and a model faces the camera at yaw 0. The entrance (torii gate and lantern
        /// path) is at the front, the Command Hall at the back.
        /// </summary>
        static IEnumerable<Piece> Layout()
        {
            Piece Building(VillageBuildingKind kind, float x, float z, float size, float yaw) => new Piece(ModelOf(kind), x, z, size, yaw, kind);

            // The six buildings round the plaza.
            yield return Building(VillageBuildingKind.CommandHall, 0f, 12f, 7f, 0f);
            yield return Building(VillageBuildingKind.Dojo, -9.5f, 8f, 5.5f, 35f);
            yield return Building(VillageBuildingKind.Shrine, 9.5f, 8f, 5.5f, -35f);
            yield return Building(VillageBuildingKind.Blacksmith, -10.5f, -1.5f, 4.5f, 70f);
            yield return Building(VillageBuildingKind.Mint, 10.5f, -1.5f, 4.5f, -70f);
            yield return Building(VillageBuildingKind.SteelMine, -17f, 12f, 5f, 45f);

            // The entrance: a torii gate and a path of lanterns into the plaza.
            yield return new Piece("ToriiGate", 0f, -9f, 4.5f);
            foreach (var side in new[] { -1f, 1f })
            {
                yield return new Piece("StoneLantern", side * 2.2f, -3.5f, 1.4f);
                yield return new Piece("StoneLantern", side * 2.2f, -7f, 1.4f);
                yield return new Piece("Banner", side * 4f, 10f, 3.2f);
                yield return new Piece("Brazier", side * 5.5f, 4.5f, 1.1f);
            }

            // Houses and the watchtower round the edge.
            yield return new Piece("VillageHouse", 17f, 12f, 4f, -40f);
            yield return new Piece("VillageHouse", -6f, 19f, 4f, 10f);
            yield return new Piece("VillageHouse", 7f, 19.5f, 4f, -10f);
            yield return new Piece("VillageHouse", -18f, 1.5f, 4f, 80f);
            yield return new Piece("Watchtower", 18f, 0f, 6.5f, -60f);

            // Cherry trees by the plaza, pines further out, rocks to break up the grass.
            yield return new Piece("CherryTree", -5f, 6.5f, 3.4f);
            yield return new Piece("CherryTree", 5f, 6.5f, 3.4f, 120f);
            yield return new Piece("CherryTree", -6f, -5f, 3f, 60f);
            yield return new Piece("CherryTree", 6f, -5f, 3f, 200f);
            yield return new Piece("CherryTree", 13f, 16f, 3.6f, 90f);
            yield return new Piece("PineTree", -22f, 6f, 5f);
            yield return new Piece("PineTree", 22f, 7f, 5f, 90f);
            yield return new Piece("PineTree", -13f, 21f, 5.5f, 30f);
            yield return new Piece("PineTree", 14f, 23f, 5.5f, 200f);
            yield return new Piece("PineTree", 0f, 25f, 6f, 120f);
            yield return new Piece("PineTree", -22f, -7f, 4.5f, 300f);
            yield return new Piece("PineTree", 22f, -8f, 4.5f, 60f);
            yield return new Piece("Rocks", -14f, 6f, 1f);
            yield return new Piece("Rocks", 14f, 5f, 1.2f, 140f);
            yield return new Piece("Rocks", -19f, 16f, 1.4f, 250f);
            yield return new Piece("Rocks", 3.5f, -11f, 0.8f, 70f);
        }

        [MenuItem("Ninja Realm/Scenes/Create Village Scene")]
        public static void CreateVillageScene()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogError("[Village] Leave play mode first.");
                return;
            }

            if (File.Exists(ScenePath) && !Application.isBatchMode &&
                !EditorUtility.DisplayDialog("Create Village Scene", $"{ScenePath} already exists. Replace it?", "Replace", "Cancel"))
                return;

            if (FindDatabase() == null)
            {
                Debug.LogError("[Village] No ContentDatabase found. Run Ninja Realm > Content > Create Prototype Content first.");
                return;
            }

            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // Load assets after NewScene: opening a scene unloads assets loaded before it.
            var database = FindDatabase();

            var cameraObject = new GameObject("Main Camera") { tag = "MainCamera" };
            cameraObject.AddComponent<AudioListener>();
            var camera = cameraObject.AddComponent<Camera>();
            camera.fieldOfView = 40f;
            camera.nearClipPlane = 0.3f;
            camera.farClipPlane = 150f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = SkyColor;

            // The same warm late-afternoon sun as the battle.
            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.9f, 0.78f);
            sun.intensity = 1.4f;
            sun.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(50f, -35f, 0f);

            BattleSceneBuilder.ApplyAtmosphere();
            RenderSettings.fogColor = SkyColor;
            RenderSettings.fogStartDistance = 40f;
            RenderSettings.fogEndDistance = 95f;

            var bootstrap = new GameObject("Game Bootstrap").AddComponent<GameBootstrap>();
            Assign(bootstrap, "content", database);

            BuildGround();
            var missing = BuildLayout(out var placed);

            var village = new GameObject("Village");
            var controller = village.AddComponent<VillageController>();
            Assign(controller, "content", database);
            Assign(controller, "viewCamera", camera);
            controller.ApplyCamera();

            ContentDatabaseMenu.EnsureFolder(Path.GetDirectoryName(ScenePath)?.Replace('\\', '/'));
            EditorSceneManager.SaveScene(scene, ScenePath);
            PutScenesInBuild();

            Debug.Log($"[Village] Created {ScenePath} with {placed} models. Press Play and click a building." +
                      (missing.Count > 0 ? $" No model yet for {string.Join(", ", missing)}; buildings get a placeholder in play mode." : ""));
        }

        /// <summary>The village first (the game starts there), then the battle.</summary>
        static void PutScenesInBuild()
        {
            var scenes = EditorBuildSettings.scenes.Where(s => s.path != ScenePath && s.path != BattleSceneBuilder.ScenePath).ToList();
            scenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
            if (File.Exists(BattleSceneBuilder.ScenePath)) scenes.Insert(1, new EditorBuildSettingsScene(BattleSceneBuilder.ScenePath, true));
            else Debug.LogWarning("[Village] There is no battle scene yet. Run Ninja Realm > Scenes > Create Battle Scene, so the Command Hall can go to battle.");
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        /// <summary>Grass all round, a round stone plaza, and a stone path to the gate.</summary>
        static void BuildGround()
        {
            var ground = new GameObject("Ground").transform;
            var grass = SavedMaterial(GroundMaterialPath, BattleArenaSetup.GroundTexturePath, new Color(0.36f, 0.5f, 0.28f), 90f / 6f);
            var stone = SavedMaterial(PlazaMaterialPath, BattleArenaSetup.TileTexturePath, new Color(0.62f, 0.6f, 0.56f), 6f);

            Part(PrimitiveType.Cylinder, ground, "Grass", new Vector3(0f, -0.05f, 4f), new Vector3(90f, 0.05f, 90f), grass);
            Part(PrimitiveType.Cylinder, ground, "Plaza", new Vector3(0f, 0.01f, 2f), new Vector3(13f, 0.02f, 13f), stone);
            Part(PrimitiveType.Cube, ground, "Path", new Vector3(0f, 0.005f, -6.5f), new Vector3(3f, 0.02f, 7f), stone);
        }

        static void Part(PrimitiveType type, Transform parent, string name, Vector3 position, Vector3 scale, Material material)
        {
            var part = GameObject.CreatePrimitive(type);
            part.name = name;
            Object.DestroyImmediate(part.GetComponent<Collider>());
            part.transform.SetParent(parent, false);
            part.transform.localPosition = position;
            part.transform.localScale = scale;
            var renderer = part.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
        }

        /// <summary>A saved URP Lit material with a repeating texture, or a plain colour when the texture is missing.</summary>
        static Material SavedMaterial(string path, string texturePath, Color fallback, float repeats)
        {
            ContentDatabaseMenu.EnsureFolder(ArtRoot);
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }

            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
            var color = texture != null ? Color.white : fallback;
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color")) material.SetColor("_Color", color);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0.1f);
            if (material.HasProperty("_BaseMap"))
            {
                material.SetTexture("_BaseMap", texture);
                material.SetTextureScale("_BaseMap", Vector2.one * repeats);
            }

            material.mainTexture = texture;
            material.mainTextureScale = Vector2.one * repeats;
            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssetIfDirty(material);
            return material;
        }

        /// <summary>Places every piece. Buildings get their component (and a placeholder later if the model is missing). Returns the missing models.</summary>
        static List<string> BuildLayout(out int placed)
        {
            placed = 0;
            var missing = new List<string>();
            var buildings = new GameObject("Buildings").transform;
            var decor = new GameObject("Decor").transform;
            var counts = new Dictionary<string, int>();

            foreach (var piece in Layout())
            {
                var path = MeshyPlacement.ModelPath(piece.Model);
                var model = path != null ? AssetDatabase.LoadAssetAtPath<GameObject>(path) : null;
                if (model == null && !missing.Contains(piece.Model)) missing.Add(piece.Model);

                if (piece.Building is { } kind)
                {
                    Transform holder;
                    if (model != null)
                    {
                        holder = MeshyPlacement.Place(buildings, model, VillageBuilding.NameOf(kind), piece.X, piece.Z, piece.Size, piece.Yaw);
                        placed++;
                    }
                    else
                    {
                        holder = new GameObject(VillageBuilding.NameOf(kind)).transform;
                        holder.SetParent(buildings, false);
                        holder.localPosition = new Vector3(piece.X, 0f, piece.Z);
                        holder.localRotation = Quaternion.Euler(0f, piece.Yaw, 0f);
                    }

                    var building = holder.gameObject.AddComponent<VillageBuilding>();
                    building.SetKind(kind);
                    using (var serialized = new SerializedObject(building))
                    {
                        serialized.FindProperty("placeholderHeight").floatValue = piece.Size;
                        serialized.ApplyModifiedPropertiesWithoutUndo();
                    }

                    if (model != null) building.AddCollider();
                    continue;
                }

                if (model == null) continue;
                counts[piece.Model] = counts.TryGetValue(piece.Model, out var count) ? count + 1 : 1;
                MeshyPlacement.Place(decor, model, $"{piece.Model} {counts[piece.Model]}", piece.X, piece.Z, piece.Size, piece.Yaw);
                placed++;
            }

            return missing;
        }

        /// <summary>
        /// Command line: -executeMethod NinjaRealm.Editor.VillageSceneBuilder.CaptureVillage [-captureOut file.png].
        /// Opens the village scene, builds the placeholders, and saves what the camera sees.
        /// </summary>
        public static void CaptureVillage()
        {
            var output = CameraCapture.Argument("-captureOut", Path.GetFullPath("Logs/village_capture.png"));
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var controller = Object.FindAnyObjectByType<VillageController>();
            if (controller == null) throw new InvalidOperationException($"No VillageController in {ScenePath}.");
            if (!controller.Initialize()) throw new InvalidOperationException("[Village] The village scene has no content database.");
            controller.ApplyCamera();
            CameraCapture.Render(controller.ViewCamera, output, 1600, 900);
            Debug.Log($"[Village] Captured {output} with {controller.Buildings.Count} buildings.");
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }

        static void Assign(Object target, string field, Object value)
        {
            using var serialized = new SerializedObject(target);
            var property = serialized.FindProperty(field);
            if (property == null) throw new InvalidOperationException($"{target.GetType().Name} has no field '{field}'.");
            property.objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        static ContentDatabase FindDatabase()
        {
            var guid = AssetDatabase.FindAssets("t:" + nameof(ContentDatabase)).FirstOrDefault();
            return guid != null ? AssetDatabase.LoadAssetAtPath<ContentDatabase>(AssetDatabase.GUIDToAssetPath(guid)) : null;
        }
    }
}
