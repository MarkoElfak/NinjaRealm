using System;
using System.Linq;
using NinjaRealm.Core.Content;
using UnityEditor;
using UnityEngine;

namespace NinjaRealm.Editor
{
    /// <summary>Menu commands for keeping the content database in sync with the content assets.</summary>
    public static class ContentDatabaseMenu
    {
        const string DefaultDatabasePath = "Assets/_Project/Content/ContentDatabase.asset";

        [MenuItem("Ninja Realm/Content/Rebuild Database")]
        public static void Rebuild()
        {
            var database = FindOrCreateDatabase();
            var definitions = AssetDatabase.FindAssets("t:" + nameof(ContentDefinition))
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<ContentDefinition>)
                .Where(definition => definition != null)
                .OrderBy(definition => definition.Id ?? string.Empty, StringComparer.Ordinal)
                .ToList();

            database.SetDefinitions(definitions);
            EditorUtility.SetDirty(database);
            AssetDatabase.SaveAssets();

            Debug.Log($"[Content] Database rebuilt with {definitions.Count} definitions.", database);
            LogIssues(database);
        }

        [MenuItem("Ninja Realm/Content/Validate")]
        public static void Validate()
        {
            var database = FindDatabase();
            if (database == null)
            {
                Debug.LogWarning("[Content] No ContentDatabase found. Run Ninja Realm > Content > Rebuild Database.");
                return;
            }

            LogIssues(database);
        }

        static void LogIssues(ContentDatabase database)
        {
            var issues = ContentValidation.Validate(database.Definitions);
            foreach (var issue in issues)
                Debug.LogError(issue.ToString(), issue.Definition);

            if (issues.Count == 0)
                Debug.Log($"[Content] All {database.Definitions.Count} definitions are valid.", database);
        }

        static ContentDatabase FindDatabase()
        {
            var guids = AssetDatabase.FindAssets("t:" + nameof(ContentDatabase));
            if (guids.Length > 1)
                Debug.LogWarning($"[Content] Found {guids.Length} ContentDatabase assets. The game should have exactly one.");

            return guids.Length == 0
                ? null
                : AssetDatabase.LoadAssetAtPath<ContentDatabase>(AssetDatabase.GUIDToAssetPath(guids[0]));
        }

        static ContentDatabase FindOrCreateDatabase()
        {
            var database = FindDatabase();
            if (database != null) return database;

            EnsureFolder("Assets/_Project/Content");
            database = ScriptableObject.CreateInstance<ContentDatabase>();
            AssetDatabase.CreateAsset(database, DefaultDatabasePath);
            Debug.Log($"[Content] Created {DefaultDatabasePath}.", database);
            return database;
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;

            var slash = path.LastIndexOf('/');
            var parent = path.Substring(0, slash);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, path.Substring(slash + 1));
        }
    }
}
