using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using NinjaRealm.Core.Content;
using NinjaRealm.Core.Prototype;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

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

        /// <summary>
        /// The content assets are created from PrototypeContent, which is the source of truth. If
        /// this fails, either run Ninja Realm > Content > Reset Prototype Content to Defaults, or
        /// copy the change you made in the Inspector into PrototypeContent.
        /// </summary>
        [Test]
        public void ContentAssetsMatchPrototypeContent()
        {
            var databaseGuids = AssetDatabase.FindAssets("t:" + nameof(ContentDatabase));
            if (databaseGuids.Length == 0) Assert.Ignore("No ContentDatabase yet.");
            var database = AssetDatabase.LoadAssetAtPath<ContentDatabase>(AssetDatabase.GUIDToAssetPath(databaseGuids[0]));
            database.Initialize();

            var sink = new InMemoryContentSink();
            PrototypeContent.Build(sink);
            try
            {
                var problems = new List<string>();
                foreach (var expected in sink.Definitions)
                {
                    if (!database.TryGet<ContentDefinition>(expected.Id, out var actual))
                    {
                        problems.Add($"{expected.Id} is missing from the project.");
                        continue;
                    }

                    var expectedValues = Describe(expected);
                    var actualValues = Describe(actual);
                    var difference = expectedValues.Except(actualValues).Concat(actualValues.Except(expectedValues)).FirstOrDefault();
                    if (difference != null)
                        problems.Add($"{expected.Id} differs at {difference.Split('=')[0]}: code has {Find(expectedValues, difference)}, asset has {Find(actualValues, difference)}.");
                }

                Assert.IsEmpty(problems, string.Join("\n", problems));
            }
            finally
            {
                foreach (var definition in sink.Definitions) Object.DestroyImmediate(definition);
            }
        }

        static string Find(List<string> values, string line)
        {
            var path = line.Split('=')[0] + "=";
            return values.FirstOrDefault(value => value.StartsWith(path, System.StringComparison.Ordinal)) ?? "nothing";
        }

        /// <summary>Every serialized value of an object as path=value lines. References to content are written as IDs.</summary>
        static List<string> Describe(Object target)
        {
            var lines = new List<string>();
            using (var serialized = new SerializedObject(target))
            {
                var property = serialized.GetIterator();
                var enterChildren = true;
                while (property.Next(enterChildren))
                {
                    enterChildren = property.propertyType != SerializedPropertyType.String;
                    if (property.propertyPath == "m_Script" || property.propertyPath == "m_Name" ||
                        property.propertyPath == "m_EditorClassIdentifier") continue;

                    var value = Value(property);
                    if (value != null) lines.Add(property.propertyPath + "=" + value);
                }
            }

            return lines;
        }

        static string Value(SerializedProperty property)
        {
            switch (property.propertyType)
            {
                case SerializedPropertyType.Integer:
                case SerializedPropertyType.LayerMask:
                case SerializedPropertyType.Character:
                    return property.longValue.ToString(CultureInfo.InvariantCulture);
                case SerializedPropertyType.ArraySize:
                    return property.intValue.ToString(CultureInfo.InvariantCulture);
                case SerializedPropertyType.Boolean:
                    return property.boolValue ? "true" : "false";
                case SerializedPropertyType.Float:
                    return property.floatValue.ToString("R", CultureInfo.InvariantCulture);
                case SerializedPropertyType.String:
                    return property.stringValue;
                case SerializedPropertyType.Enum:
                    return property.intValue.ToString(CultureInfo.InvariantCulture);
                case SerializedPropertyType.Color:
                    return property.colorValue.ToString("F4");
                case SerializedPropertyType.ObjectReference:
                    var reference = property.objectReferenceValue;
                    return reference == null ? "null" : reference is ContentDefinition content ? content.Id : reference.name;
                case SerializedPropertyType.ManagedReference:
                    return property.managedReferenceFullTypename;
                default:
                    return null;
            }
        }
    }
}
