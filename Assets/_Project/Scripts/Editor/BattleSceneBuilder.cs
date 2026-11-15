using System;
using System.IO;
using System.Linq;
using NinjaRealm.Core.Content;
using NinjaRealm.Core.Lineups;
using NinjaRealm.Core.Prototype;
using NinjaRealm.Presentation;
using NinjaRealm.Presentation.Battle;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace NinjaRealm.Editor
{
    /// <summary>
    /// Creates the prototype battle scene, and renders a screenshot of a fight for checks run
    /// from the command line.
    /// </summary>
    public static class BattleSceneBuilder
    {
        public const string ScenePath = "Assets/Scenes/Battle.unity";

        [MenuItem("Ninja Realm/Scenes/Create Battle Scene")]
        public static void CreateBattleScene()
        {
            if (File.Exists(ScenePath) && !Application.isBatchMode &&
                !EditorUtility.DisplayDialog("Create Battle Scene", $"{ScenePath} already exists. Replace it?", "Replace", "Cancel"))
                return;

            if (FindDatabase() == null)
            {
                Debug.LogError("[Battle] No ContentDatabase found. Run Ninja Realm > Content > Create Prototype Content first.");
                return;
            }

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // Load the database after NewScene: opening a scene unloads assets loaded before it.
            var database = FindDatabase();

            // Angled board camera, like an auto battler: team 0 at the bottom of the screen.
            var cameraObject = new GameObject("Main Camera") { tag = "MainCamera" };
            var camera = cameraObject.AddComponent<Camera>();
            cameraObject.AddComponent<AudioListener>();
            camera.fieldOfView = 36f;
            camera.nearClipPlane = 0.3f;
            camera.farClipPlane = 100f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.55f, 0.68f, 0.84f);
            cameraObject.transform.position = new Vector3(0f, 12.5f, -12f);
            cameraObject.transform.LookAt(new Vector3(0f, 0f, -0.5f));

            // Warm late-afternoon sun with soft shadows.
            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.9f, 0.78f);
            sun.intensity = 1.4f;
            sun.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(50f, -35f, 0f);

            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.62f, 0.68f, 0.8f);
            RenderSettings.ambientEquatorColor = new Color(0.56f, 0.52f, 0.46f);
            RenderSettings.ambientGroundColor = new Color(0.3f, 0.28f, 0.25f);

            var bootstrap = new GameObject("Game Bootstrap").AddComponent<GameBootstrap>();
            Assign(bootstrap, "content", database);

            var battle = new GameObject("Battle");
            var board = new GameObject("Board").AddComponent<HexBoardView>();
            board.transform.SetParent(battle.transform, false);
            var playback = new GameObject("Units").AddComponent<BattlePlayback>();
            playback.transform.SetParent(battle.transform, false);

            var controller = battle.AddComponent<BattleController>();
            Assign(controller, "content", database);
            Assign(controller, "board", board);
            Assign(controller, "playback", playback);

            var hud = battle.AddComponent<BattleHud>();
            Assign(hud, "controller", controller);

            if (database == null || bootstrap == null)
                throw new InvalidOperationException("[Battle] The ContentDatabase could not be loaded into the new scene.");

            ContentDatabaseMenu.EnsureFolder(Path.GetDirectoryName(ScenePath)?.Replace('\\', '/'));
            EditorSceneManager.SaveScene(scene, ScenePath);

            // The battle scene is the first scene in builds until the village exists.
            var scenes = EditorBuildSettings.scenes.Where(s => s.path != ScenePath).ToList();
            scenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();

            Debug.Log($"[Battle] Created {ScenePath} using {AssetDatabase.GetAssetPath(database)}. Press Play to set up a lineup and fight.");
        }

        /// <summary>
        /// Command line: -executeMethod NinjaRealm.Editor.BattleSceneBuilder.CaptureBattle
        /// [-captureOut file.png] [-captureSeed 7] [-captureSeconds 6] [-captureOpponent lineup.ember_clan].
        /// Plays the first balance build against an enemy lineup in the battle scene without
        /// entering play mode and saves what the camera sees.
        /// </summary>
        public static void CaptureBattle()
        {
            var output = CameraCapture.Argument("-captureOut", Path.GetFullPath("Logs/battle_capture.png"));
            var seed = int.Parse(CameraCapture.Argument("-captureSeed", "7"));
            var seconds = float.Parse(CameraCapture.Argument("-captureSeconds", "6"), System.Globalization.CultureInfo.InvariantCulture);

            var opponentId = CameraCapture.Argument("-captureOpponent", "lineup.ember_clan");

            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var controller = Object.FindAnyObjectByType<BattleController>();
            if (controller == null) throw new InvalidOperationException($"No BattleController in {ScenePath}.");
            if (!controller.Initialize()) throw new InvalidOperationException("[Battle] The battle scene has no content database.");

            var database = controller.Content;
            var player = PrototypeContent.BalanceArchetypes(database)[0].Slots;
            var opponent = database.Get<LineupDefinition>(opponentId);
            controller.StartFight(player, opponent.Slots, seed);
            if (controller.Playback.Simulation == null)
                throw new InvalidOperationException("[Battle] The fight did not start. See the errors above.");

            const float frame = 1f / 30f;
            for (var time = 0f; time < seconds; time += frame)
                controller.Playback.Advance(frame);

            var camera = Camera.main;
            if (camera == null) throw new InvalidOperationException("The battle scene has no main camera.");

            CameraCapture.Render(camera, output, 1600, 900);

            var simulation = controller.Playback.Simulation;
            var alive = simulation.Units.Count(unit => unit.IsAlive);
            Debug.Log($"[Battle] Captured {output} at {controller.Playback.FightSeconds:0.0}s of seed {seed}: " +
                      $"{simulation.Units.Count} units, {alive} alive, {simulation.Events.Count} events.");

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }

        static void Assign(Object target, string field, Object value)
        {
            var serialized = new SerializedObject(target);
            var property = serialized.FindProperty(field);
            if (property == null) throw new InvalidOperationException($"{target.GetType().Name} has no field '{field}'.");
            property.objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            if (value != null && new SerializedObject(target).FindProperty(field).objectReferenceValue == null)
                throw new InvalidOperationException($"Could not assign {field} on {target.GetType().Name}.");
        }

        static ContentDatabase FindDatabase()
        {
            var guid = AssetDatabase.FindAssets("t:" + nameof(ContentDatabase)).FirstOrDefault();
            return guid != null ? AssetDatabase.LoadAssetAtPath<ContentDatabase>(AssetDatabase.GUIDToAssetPath(guid)) : null;
        }
    }
}
