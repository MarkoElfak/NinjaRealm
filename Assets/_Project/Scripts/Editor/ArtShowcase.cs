using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace NinjaRealm.Editor
{
    /// <summary>
    /// Builds a scene that lines up every Meshy model under <see cref="MeshyArtImporter.Root"/> at
    /// game scale, in the warm board lighting, to judge new art side by side.
    /// </summary>
    public static class ArtShowcase
    {
        public const string ScenePath = MeshyArtImporter.Root + "/MeshyShowcase.unity";

        // Heights in metres by folder, from docs/ART_STYLE.md (a ninja is about 1.2 m).
        static readonly Dictionary<string, float> HeightByFolder = new Dictionary<string, float>
        {
            { "Characters", 1.2f },
            { "Props", 3f },
            { "Buildings", 4.5f },
        };

        [MenuItem("Ninja Realm/Art/Open Meshy Showcase")]
        public static void OpenShowcase()
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            var modelPaths = AssetDatabase.FindAssets("t:Model", new[] { MeshyArtImporter.Root })
                .Select(AssetDatabase.GUIDToAssetPath)
                .OrderBy(path => path, System.StringComparer.Ordinal)
                .ToList();
            if (modelPaths.Count == 0)
            {
                Debug.LogWarning($"[Meshy] No models under {MeshyArtImporter.Root}. Save Meshy downloads there as Folder/Name/Name.fbx.");
                return;
            }

            // Make sure the models use the current Meshy import settings.
            foreach (var path in modelPaths) AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            CreateLighting();

            var front = new List<GameObject>();
            var back = new List<GameObject>();
            foreach (var path in modelPaths)
            {
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (model == null) continue;

                var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
                var folder = CategoryOf(path);
                FitToHeight(instance, HeightByFolder.TryGetValue(folder, out var height) ? height : 1.5f);
                (folder == "Characters" ? front : back).Add(instance);
            }

            // Characters in front, everything else in a row behind them.
            LayOutRow(front, 0f, 0.8f);
            LayOutRow(back, 4.5f, 1.5f);

            var all = front.Concat(back).ToList();
            CreateGround(all);
            var camera = CreateCamera(all);

            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath) ?? ".");
            EditorSceneManager.SaveScene(scene, ScenePath);

            Selection.objects = all.Cast<Object>().ToArray();
            SceneView.lastActiveSceneView?.AlignViewToObject(camera.transform);
            Debug.Log($"[Meshy] Showcase with {all.Count} models: {string.Join(", ", all.Select(go => go.name))}.");
        }

        /// <summary>Command line: -executeMethod NinjaRealm.Editor.ArtShowcase.CaptureShowcase [-captureOut file.png].</summary>
        public static void CaptureShowcase()
        {
            OpenShowcase();
            var output = CameraCapture.Argument("-captureOut", Path.GetFullPath("Logs/meshy_showcase.png"));
            CameraCapture.Render(Camera.main, output, 1600, 900);
            Debug.Log($"[Meshy] Captured {output}.");
        }

        static string CategoryOf(string path)
        {
            var relative = path.Substring(MeshyArtImporter.Root.Length + 1);
            var slash = relative.IndexOf('/');
            return slash > 0 ? relative.Substring(0, slash) : "";
        }

        static Bounds BoundsOf(GameObject instance)
        {
            var renderers = instance.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return new Bounds(instance.transform.position, Vector3.one);
            var bounds = renderers[0].bounds;
            foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
            return bounds;
        }

        /// <summary>Scales a model to a height in metres and turns it to face the camera.</summary>
        static void FitToHeight(GameObject instance, float height)
        {
            // Meshy models face +Z; the showcase camera looks along +Z, so turn them around.
            instance.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
            var bounds = BoundsOf(instance);
            if (bounds.size.y > 0.0001f) instance.transform.localScale *= height / bounds.size.y;
        }

        static void LayOutRow(List<GameObject> row, float z, float gap)
        {
            var widths = row.Select(go => BoundsOf(go).size.x).ToList();
            var total = widths.Sum() + gap * Mathf.Max(0, row.Count - 1);
            var x = -total / 2f;
            for (var i = 0; i < row.Count; i++)
            {
                var bounds = BoundsOf(row[i]);
                var offset = new Vector3(x + widths[i] / 2f - bounds.center.x, -bounds.min.y, z - bounds.center.z);
                row[i].transform.position += offset;
                x += widths[i] + gap;
            }
        }

        static void CreateLighting()
        {
            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.88f, 0.74f);
            sun.intensity = 1.4f;
            sun.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(40f, -35f, 0f);

            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.62f, 0.68f, 0.8f);
            RenderSettings.ambientEquatorColor = new Color(0.56f, 0.52f, 0.46f);
            RenderSettings.ambientGroundColor = new Color(0.3f, 0.28f, 0.25f);
        }

        static void CreateGround(List<GameObject> models)
        {
            var bounds = models.Select(BoundsOf).Aggregate((a, b) => { a.Encapsulate(b); return a; });
            var ground = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            ground.name = "Ground";
            ground.transform.position = new Vector3(bounds.center.x, -0.05f, bounds.center.z);
            var size = Mathf.Max(bounds.size.x, bounds.size.z) + 4f;
            ground.transform.localScale = new Vector3(size, 0.05f, size);
            Object.DestroyImmediate(ground.GetComponent<Collider>());

            var material = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
            material.SetColor("_BaseColor", new Color(0.52f, 0.5f, 0.46f));
            material.SetFloat("_Smoothness", 0.1f);
            ground.GetComponent<Renderer>().sharedMaterial = material;
        }

        static Camera CreateCamera(List<GameObject> models)
        {
            var bounds = models.Select(BoundsOf).Aggregate((a, b) => { a.Encapsulate(b); return a; });
            var cameraObject = new GameObject("Main Camera") { tag = "MainCamera" };
            var camera = cameraObject.AddComponent<Camera>();
            camera.fieldOfView = 30f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.55f, 0.68f, 0.84f);

            // Far enough back to fit the whole line-up on a 16:9 screen, looking slightly down.
            var halfFov = camera.fieldOfView * 0.5f * Mathf.Deg2Rad;
            var fitHeight = bounds.size.y * 0.75f / Mathf.Tan(halfFov);
            var fitWidth = bounds.size.x * 0.55f / (Mathf.Tan(halfFov) * 16f / 9f);
            var distance = Mathf.Max(fitHeight, fitWidth) + bounds.extents.z + 1f;
            var target = new Vector3(bounds.center.x, bounds.size.y * 0.4f, bounds.center.z);
            cameraObject.transform.position = target + new Vector3(0f, distance * 0.35f, -distance);
            cameraObject.transform.LookAt(target);
            return camera;
        }
    }
}
