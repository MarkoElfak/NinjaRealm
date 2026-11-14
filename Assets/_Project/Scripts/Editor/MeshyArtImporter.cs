using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace NinjaRealm.Editor
{
    /// <summary>
    /// Import settings for models saved from Meshy under <see cref="Root"/>. Meshy saves each
    /// model as Name.fbx next to Name_base_color.png. This gives the model's materials the URP Lit
    /// shader with that texture, so a downloaded model shows its colours without manual setup.
    /// </summary>
    public sealed class MeshyArtImporter : AssetPostprocessor
    {
        public const string Root = "Assets/_Project/Art/Meshy";

        const string BaseColorSuffix = "_base_color";

        // Bump when the settings below change, so Unity reimports the models.
        public override uint GetVersion() => 1;

        public static bool IsMeshyAsset(string path) =>
            path.Replace('\\', '/').StartsWith(Root + "/", StringComparison.Ordinal);

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

            // Import the texture first, and reimport the model when the texture changes.
            var texture = BaseColorPath(assetPath);
            if (File.Exists(texture)) context.DependsOnArtifact(texture);
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

        void OnPostprocessMaterial(Material material)
        {
            if (!IsMeshyAsset(assetPath)) return;

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
