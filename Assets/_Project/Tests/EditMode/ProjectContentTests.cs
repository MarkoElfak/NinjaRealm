using System.Linq;
using NinjaRealm.Core.Content;
using NUnit.Framework;
using UnityEditor;

namespace NinjaRealm.Tests
{
    /// <summary>Checks the real content assets in the project, so broken content fails the test run.</summary>
    public class ProjectContentTests
    {
        [Test]
        public void ProjectHasExactlyOneContentDatabase()
        {
            var guids = AssetDatabase.FindAssets("t:" + nameof(ContentDatabase));
            if (guids.Length == 0) Assert.Ignore("No ContentDatabase yet. Run Ninja Realm > Content > Rebuild Database.");

            Assert.AreEqual(1, guids.Length, "The project should have exactly one ContentDatabase asset.");
        }

        [Test]
        public void AllProjectContentIsValid()
        {
            var guids = AssetDatabase.FindAssets("t:" + nameof(ContentDefinition));
            var definitions = guids
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<ContentDefinition>)
                .Where(definition => definition != null)
                .ToList();

            var issues = ContentValidation.Validate(definitions).Select(issue => issue.Message).ToList();
            Assert.IsEmpty(issues, string.Join("\n", issues));
        }

        [Test]
        public void DatabaseListsEveryContentAsset()
        {
            var databaseGuids = AssetDatabase.FindAssets("t:" + nameof(ContentDatabase));
            if (databaseGuids.Length == 0) Assert.Ignore("No ContentDatabase yet.");

            var database = AssetDatabase.LoadAssetAtPath<ContentDatabase>(AssetDatabase.GUIDToAssetPath(databaseGuids[0]));
            var assetCount = AssetDatabase.FindAssets("t:" + nameof(ContentDefinition)).Length;

            Assert.AreEqual(assetCount, database.Definitions.Count(definition => definition != null),
                "The database is out of date. Run Ninja Realm > Content > Rebuild Database.");
        }
    }
}
