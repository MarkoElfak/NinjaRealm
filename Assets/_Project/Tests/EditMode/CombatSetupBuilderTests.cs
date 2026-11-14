using System.Linq;
using NinjaRealm.Combat;
using NinjaRealm.Core.Content;
using NinjaRealm.Core.Definitions;
using NinjaRealm.Core.Fights;
using NUnit.Framework;
using UnityEditor;

namespace NinjaRealm.Tests
{
    /// <summary>Builds fights from the real prototype content and runs them.</summary>
    public class CombatSetupBuilderTests
    {
        ContentDatabase database;

        [SetUp]
        public void LoadDatabase()
        {
            var guids = AssetDatabase.FindAssets("t:" + nameof(ContentDatabase));
            if (guids.Length == 0) Assert.Ignore("No ContentDatabase yet. Run Ninja Realm > Content > Create Prototype Content.");

            database = AssetDatabase.LoadAssetAtPath<ContentDatabase>(AssetDatabase.GUIDToAssetPath(guids[0]));
            database.Initialize();
            if (!database.Contains("ninja.kaito")) Assert.Ignore("Prototype content is missing.");
        }

        CombatSetupBuilder PrototypeFight()
        {
            var builder = CombatSetupBuilder.FromDatabase(database);
            NinjaDefinition Ninja(string name) => database.Get<NinjaDefinition>("ninja." + name);
            WeaponDefinition Weapon(string name) => database.Get<WeaponDefinition>("weapon." + name);

            // Team 0: three Fire ninjas. Team 1: a mix of Water and Earth.
            builder.Add(0, Ninja("jiro"), builder.TeamPosition(0, 3, 3), Weapon("iron_plate"));
            builder.Add(0, Ninja("kaito"), builder.TeamPosition(0, 2, 3), Weapon("shadow_katana"));
            builder.Add(0, Ninja("mei"), builder.TeamPosition(0, 4, 1), Weapon("dragon_fan"));
            builder.Add(1, Ninja("takeshi"), builder.TeamPosition(1, 3, 3));
            builder.Add(1, Ninja("goro"), builder.TeamPosition(1, 4, 3), Weapon("kusarigama"));
            builder.Add(1, Ninja("hana"), builder.TeamPosition(1, 3, 0));
            builder.Add(1, Ninja("nami"), builder.TeamPosition(1, 2, 1));
            return builder;
        }

        [Test]
        public void Build_ConvertsStatsToFixedPoint()
        {
            var setup = PrototypeFight().Build();
            var kaito = database.Get<NinjaDefinition>("ninja.kaito");
            var unit = setup.Units.Single(u => u.DefinitionId == "ninja.kaito");

            foreach (var stat in database.GetAll<StatDefinition>())
                Assert.AreEqual(Fixed.FromFloat(kaito.GetBaseStat(stat)), unit.Stats[stat.Id], stat.Id);

            Assert.AreEqual(kaito.Skill.Id, unit.Skill.Id);
            Assert.AreEqual(kaito.Skill.Actions.Count, unit.Skill.Actions.Count);
            CollectionAssert.AreEqual(kaito.Traits.Select(t => t.Id).ToList(), unit.TraitIds);
        }

        [Test]
        public void Build_GivesCombinedWeaponsTheirComponentEffects()
        {
            var setup = PrototypeFight().Build();
            var katana = database.Get<WeaponDefinition>("weapon.shadow_katana");
            var unit = setup.Units.Single(u => u.DefinitionId == "ninja.kaito");

            Assert.AreEqual(katana.AllEffects().Count(), unit.Passives.Count);
            Assert.AreEqual(3, unit.Passives.Count);
        }

        [Test]
        public void Build_IncludesEveryTraitOnTheBoardOnce()
        {
            var setup = PrototypeFight().Build();
            var expected = setup.Units.SelectMany(u => u.TraitIds).Distinct().OrderBy(id => id, System.StringComparer.Ordinal).ToList();
            CollectionAssert.AreEqual(expected, setup.Traits.Select(t => t.Id).ToList());
        }

        [Test]
        public void TeamPosition_FlipsTeamOne()
        {
            var builder = CombatSetupBuilder.FromDatabase(database);
            Assert.AreEqual(new HexCoord(2, 0), builder.TeamPosition(0, 2, 0));
            Assert.AreEqual(new HexCoord(2, 7), builder.TeamPosition(1, 2, 0));
            Assert.AreEqual(new HexCoord(2, 4), builder.TeamPosition(1, 2, 3));
        }

        [Test]
        public void PrototypeFight_EndsAndActivatesFire()
        {
            var simulation = new CombatSimulation(PrototypeFight().Build(), 7);
            var result = simulation.Run();

            Assert.IsTrue(simulation.IsFinished);
            Assert.LessOrEqual(result.DurationTicks, simulation.Setup.MaxTicks);
            Assert.IsTrue(simulation.Events.Any(e => e.Type == CombatEventType.TraitActivated && e.Id == "trait.element.fire" && e.Amount == 0));
            Assert.IsTrue(simulation.Events.Any(e => e.Type == CombatEventType.Cast), "someone should cast a skill");
        }

        [Test]
        public void PrototypeFight_IsDeterministic()
        {
            for (ulong seed = 1; seed <= 3; seed++)
            {
                var first = new CombatSimulation(PrototypeFight().Build(), seed);
                var second = new CombatSimulation(PrototypeFight().Build(), seed);
                first.Run();
                second.Run();

                CollectionAssert.AreEqual(first.Events.Select(e => e.ToString()).ToList(), second.Events.Select(e => e.ToString()).ToList(), $"seed {seed}");
            }
        }
    }
}
