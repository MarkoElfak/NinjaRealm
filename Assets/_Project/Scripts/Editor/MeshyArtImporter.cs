using System;
using System.IO;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace NinjaRealm.Editor
{
    /// <summary>
    /// Import settings for models saved from Meshy under <see cref="Root"/>. Meshy saves each
    /// model as Name.fbx next to Name_base_color.png. This gives the model's materials the URP Lit
    /// shader with that texture, so a downloaded model shows its colours without manual setup.
    /// Rigged models and the clips under <see cref="AnimationsRoot"/> import as Humanoid, so every
    /// ninja can share the same animations.
    /// </summary>
    public sealed class MeshyArtImporter : AssetPostprocessor
    {
        public const string Root = "Assets/_Project/Art/Meshy";
        public const string CharactersRoot = Root + "/Characters";
        public const string AnimationsRoot = Root + "/Animations";

        /// <summary>Clips that play on repeat. Every other clip plays once.</summary>
        public static readonly string[] LoopingClips = { "Idle", "Run" };

        const string BaseColorSuffix = "_base_color";

        // Bump when the settings below change, so Unity reimports the models.
        public override uint GetVersion() => 3;

        // Run after URP's own material description preprocessor, so these settings win.
        public override int GetPostprocessOrder() => 100;

        public static bool IsMeshyAsset(string path) => IsUnder(path, Root);

        static bool IsUnder(string path, string folder) =>
            path.Replace('\\', '/').StartsWith(folder + "/", StringComparison.Ordinal);

        /// <summary>True when an FBX file holds a skeleton. FBX writes the bone type name as plain text.</summary>
        public static bool HasSkeleton(string path)
        {
            if (!File.Exists(path)) return false;
            var bytes = File.ReadAllBytes(path);
            var marker = System.Text.Encoding.ASCII.GetBytes("LimbNode");
            for (var i = 0; i <= bytes.Length - marker.Length; i++)
            {
                var match = true;
                for (var j = 0; j < marker.Length && match; j++) match = bytes[i + j] == marker[j];
                if (match) return true;
            }

            return false;
        }

        /// <summary>The base colour texture Meshy saves next to a model.</summary>
        public static string BaseColorPath(string modelPath)
        {
            var directory = Path.GetDirectoryName(modelPath)?.Replace('\\', '/') ?? "";
            return $"{directory}/{Path.GetFileNameWithoutExtension(modelPath)}{BaseColorSuffix}.png";
        }

        void OnPreprocessModel()
        {
            if (!IsMeshyAsset(assetPath)) return;

            var importer = (ModelImporter)assetImporter;
            importer.importCameras = false;
            importer.importLights = false;
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
            importer.materialLocation = ModelImporterMaterialLocation.InPrefab;

            // Rigged characters and animation clips share one humanoid setup. Unrigged models
            // (buildings, props, characters before rigging) stay static.
            var animated = IsUnder(assetPath, AnimationsRoot) || (IsUnder(assetPath, CharactersRoot) && HasSkeleton(assetPath));
            if (animated)
            {
                importer.animationType = ModelImporterAnimationType.Human;
                importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                importer.importAnimation = IsUnder(assetPath, AnimationsRoot);

                // A clip file only lends its motion; the character models carry the look.
                if (IsUnder(assetPath, AnimationsRoot)) importer.materialImportMode = ModelImporterMaterialImportMode.None;
            }
            else
            {
                importer.animationType = ModelImporterAnimationType.None;
                importer.importAnimation = false;
            }

            // Import the texture first, and reimport the model when the texture changes.
            var texture = BaseColorPath(assetPath);
            if (!IsUnder(assetPath, AnimationsRoot) && File.Exists(texture)) context.DependsOnArtifact(texture);
        }

        // Each file under AnimationsRoot is one clip, named after the file, for example Run.fbx.
        void OnPreprocessAnimation()
        {
            if (!IsUnder(assetPath, AnimationsRoot)) return;

            var importer = (ModelImporter)assetImporter;
            var name = Path.GetFileNameWithoutExtension(assetPath);
            var loop = Array.IndexOf(LoopingClips, name) >= 0;
            var clips = importer.defaultClipAnimations;
            for (var i = 0; i < clips.Length; i++)
            {
                clips[i].name = clips.Length == 1 ? name : $"{name} {i + 1}";
                clips[i].loopTime = loop;
                clips[i].lockRootRotation = true;
                clips[i].lockRootHeightY = true;
                clips[i].lockRootPositionXZ = true;
                clips[i].keepOriginalOrientation = true;
                clips[i].keepOriginalPositionY = true;
                clips[i].keepOriginalPositionXZ = true;
            }

            importer.clipAnimations = clips;
        }

        void OnPreprocessTexture()
        {
            if (!IsMeshyAsset(assetPath) || !Path.GetFileNameWithoutExtension(assetPath).EndsWith(BaseColorSuffix, StringComparison.Ordinal))
                return;

            var importer = (TextureImporter)assetImporter;
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = true;
            importer.mipmapEnabled = true;
            importer.maxTextureSize = 2048;
        }

        // Model importers hand each material to this callback while it is built from the FBX.
        void OnPreprocessMaterialDescription(MaterialDescription description, Material material, AnimationClip[] clips)
        {
            if (!IsMeshyAsset(assetPath) || IsUnder(assetPath, AnimationsRoot)) return;

            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader != null) material.shader = shader;

            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(BaseColorPath(assetPath));
            if (texture != null)
            {
                material.SetTexture("_BaseMap", texture);
                material.mainTexture = texture;
            }
            else
            {
                Debug.LogWarning($"[Meshy] {assetPath} has no {Path.GetFileName(BaseColorPath(assetPath))} next to it, so it stays untextured.");
            }

            // Hand-painted look: lighting comes from the scene, so keep the surface matte.
            material.SetColor("_BaseColor", Color.white);
            material.SetFloat("_Metallic", 0f);
            material.SetFloat("_Smoothness", 0.15f);
        }
    }
}
