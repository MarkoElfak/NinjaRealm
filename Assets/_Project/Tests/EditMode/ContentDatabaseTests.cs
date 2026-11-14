using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NinjaRealm.Core.Content;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace NinjaRealm.Tests
{
    public class ContentDatabaseTests
    {
        readonly List<Object> created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (var obj in created)
                if (obj != null) Object.DestroyImmediate(obj);
            created.Clear();
        }

        T Make<T>(string id, string assetName = null) where T : ContentDefinition
        {
            var definition = ScriptableObject.CreateInstance<T>();
            definition.name = assetName ?? id;
            definition.SetId(id);
            created.Add(definition);
            return definition;
        }

        ContentDatabase MakeDatabase(params ContentDefinition[] definitions)
        {
            var database = ScriptableObject.CreateInstance<ContentDatabase>();
            database.SetDefinitions(definitions);
            created.Add(database);
            return database;
        }

        [Test]
        public void Get_ReturnsDefinitionById()
        {
            var kaito = Make<TestNinjaDefinition>("ninja.kaito");
            var database = MakeDatabase(kaito, Make<TestContentDefinition>("weapon.kunai"));

            Assert.AreSame(kaito, database.Get<TestNinjaDefinition>("ninja.kaito"));
            Assert.AreEqual(2, database.Count);
        }

        [Test]
        public void TryGet_FailsForWrongTypeOrMissingId()
        {
            var database = MakeDatabase(Make<TestNinjaDefinition>("ninja.kaito"));

            Assert.IsFalse(database.TryGet<TestContentDefinition>("ninja.kaito", out _));
            Assert.IsFalse(database.TryGet<TestNinjaDefinition>("ninja.nobody", out _));
            Assert.IsFalse(database.TryGet<TestNinjaDefinition>(null, out _));
        }

        [Test]
        public void Get_ThrowsForMissingId()
        {
            var database = MakeDatabase();

            Assert.Throws<KeyNotFoundException>(() => database.Get<TestNinjaDefinition>("ninja.nobody"));
        }

        [Test]
        public void GetAll_ReturnsOnlyThatTypeInDatabaseOrder()
        {
            var hana = Make<TestNinjaDefinition>("ninja.hana");
            var kaito = Make<TestNinjaDefinition>("ninja.kaito");
            var database = MakeDatabase(hana, Make<TestContentDefinition>("weapon.kunai"), kaito);

            CollectionAssert.AreEqual(new[] { hana, kaito }, database.GetAll<TestNinjaDefinition>().ToArray());
        }

        [Test]
        public void Initialize_SkipsInvalidIdsAndEmptySlots()
        {
            var database = MakeDatabase(Make<TestNinjaDefinition>("Bad Id"), null, Make<TestNinjaDefinition>("ninja.kaito"));

            Assert.AreEqual(1, database.Count);
            Assert.IsTrue(database.Contains("ninja.kaito"));
        }

        [Test]
        public void Initialize_KeepsFirstOfDuplicateIdsAndLogsError()
        {
            var first = Make<TestNinjaDefinition>("ninja.kaito", "KaitoA");
            var second = Make<TestNinjaDefinition>("ninja.kaito", "KaitoB");
            var database = MakeDatabase(first, second);

            LogAssert.Expect(LogType.Error, new Regex("Duplicate ID 'ninja.kaito'"));
            database.Initialize();

            Assert.AreSame(first, database.Get<TestNinjaDefinition>("ninja.kaito"));
        }

        [Test]
        public void Validate_ReportsNothingForCleanContent()
        {
            var issues = ContentValidation.Validate(new ContentDefinition[]
            {
                Make<TestNinjaDefinition>("ninja.kaito"),
                Make<TestContentDefinition>("weapon.kunai"),
            });

            Assert.IsEmpty(issues);
        }

        [Test]
        public void Validate_ReportsInvalidDuplicateWrongCategoryAndEmptySlot()
        {
            var issues = ContentValidation.Validate(new ContentDefinition[]
            {
                Make<TestNinjaDefinition>("Bad Id"),
                Make<TestNinjaDefinition>("ninja.kaito", "KaitoA"),
                Make<TestNinjaDefinition>("ninja.kaito", "KaitoB"),
                Make<TestNinjaDefinition>("weapon.kunai"),
                null,
            });

            var messages = issues.Select(issue => issue.Message).ToList();
            Assert.AreEqual(4, messages.Count, string.Join("\n", messages));
            Assert.That(messages, Has.Some.Contains("invalid ID 'Bad Id'"));
            Assert.That(messages, Has.Some.Contains("share the ID 'ninja.kaito'"));
            Assert.That(messages, Has.Some.Contains("must start with 'ninja.'"));
            Assert.That(messages, Has.Some.Contains("empty slot"));
        }
    }
}
