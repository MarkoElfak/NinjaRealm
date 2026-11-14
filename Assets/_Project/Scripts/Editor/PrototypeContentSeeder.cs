using System;
using NinjaRealm.Core.Content;
using NinjaRealm.Core.Prototype;
using UnityEditor;
using UnityEngine;

namespace NinjaRealm.Editor
{
    /// <summary>
    /// Saves the combat prototype's content (see <see cref="PrototypeContent"/>) as assets under
    /// Assets/_Project/Content, then rebuilds the content database.
    /// Batchmode: -executeMethod NinjaRealm.Editor.PrototypeContentSeeder.CreatePrototypeContent
    /// or NinjaRealm.Editor.PrototypeContentSeeder.ResetPrototypeContent
    /// </summary>
    public static class PrototypeContentSeeder
    {
        const string Root = "Assets/_Project/Content";

        /// <summary>Creates missing prototype assets. Existing assets are left as they are, so edits are kept.</summary>
        [MenuItem("Ninja Realm/Content/Create Prototype Content")]
        public static void CreatePrototypeContent() => Run(false);

        /// <summary>Creates missing assets and sets every existing prototype asset back to the values in code.</summary>
        [MenuItem("Ninja Realm/Content/Reset Prototype Content to Defaults")]
        public static void ResetPrototypeContent()
        {
            if (!Application.isBatchMode && !EditorUtility.DisplayDialog("Reset Prototype Content",
                    "Set every prototype content asset back to the values in PrototypeContent.cs? Edits made in the inspector are lost.",
                    "Reset", "Cancel"))
                return;

            Run(true);
        }

        static void Run(bool overwrite)
        {
            var sink = new AssetSink(overwrite);
            PrototypeContent.Build(sink);
            AssetDatabase.SaveAssets();
            Debug.Log($"[Content] Prototype content: created {sink.Created} new assets, reset {sink.Reset} existing assets.");
            ContentDatabaseMenu.Rebuild();
        }

        sealed class AssetSink : IContentSink
        {
            readonly bool overwrite;

            public AssetSink(bool overwrite)
            {
                this.overwrite = overwrite;
            }

            public int Created { get; private set; }

            public int Reset { get; private set; }

            public T Provide<T>(string folder, string id, string displayName, Action<T> configure) where T : ContentDefinition
            {
                var directory = $"{Root}/{folder}";
                var path = $"{directory}/{PrototypeContent.FileName(displayName)}.asset";

                var existing = AssetDatabase.LoadAssetAtPath<T>(path);
                if (existing != null)
                {
                    if (overwrite)
                    {
                        existing.SetId(id);
                        existing.SetDisplayName(displayName);
                        configure(existing);
                        EditorUtility.SetDirty(existing);
                        Reset++;
                    }

                    return existing;
                }

                ContentDatabaseMenu.EnsureFolder(directory);

                var asset = ScriptableObject.CreateInstance<T>();
                asset.SetId(id);
                asset.SetDisplayName(displayName);
                configure(asset);
                AssetDatabase.CreateAsset(asset, path);
                Created++;
                return asset;
            }
        }
    }
}
