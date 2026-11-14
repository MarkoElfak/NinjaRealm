using System.Linq;
using NinjaRealm.Core.Content;
using NinjaRealm.Core.Definitions;
using NinjaRealm.Core.Lineups;
using NinjaRealm.Core.Prototype;
using NUnit.Framework;
using UnityEngine;

namespace NinjaRealm.Tests
{
    /// <summary>Lineup rules, synergy counting and the player's lineup editing, with in-memory prototype content.</summary>
    public class LineupTests
    {
        InMemoryContentSink sink;
        ContentDatabase database;

        [OneTimeSetUp]
        public void CreateContent()
        {
            sink = new InMemoryContentSink();
            PrototypeContent.Build(sink);
            database = sink.CreateDatabase();
        }

        [OneTimeTearDown]
        public void DestroyContent()
        {
            foreach (var definition in sink.Definitions) Object.DestroyImmediate(definition);
            Object.DestroyImmediate(database);
        }

        NinjaDefinition Ninja(string name) => database.Get<NinjaDefinition>("ninja." + name);
        WeaponDefinition Weapon(string name) => database.Get<WeaponDefinition>("weapon." + name);

        // ---------------------------------------------------------------- rules

        [Test]
        public void IsOnBoard_CoversSevenColumnsAndFourRows()
        {
            Assert.IsTrue(LineupRules.IsOnBoard(0, 0));
            Assert.IsTrue(LineupRules.IsOnBoard(6, 3));
            Assert.IsFalse(LineupRules.IsOnBoard(7, 0));
            Assert.IsFalse(LineupRules.IsOnBoard(0, 4));
            Assert.IsFalse(LineupRules.IsOnBoard(-1, 2));
        }

        [Test]
        public void Validate_AcceptsAValidLineup()
        {
            var slots = new[] { new LineupSlot(Ninja("kaito"), 3, 3, Weapon("kunai")), new LineupSlot(Ninja("hana"), 3, 0) };
            Assert.IsEmpty(LineupRules.Validate(slots).ToList());
        }

        [Test]
        public void Validate_ReportsEachProblem()
        {
            Assert.IsNotEmpty(LineupRules.Validate(new LineupSlot[0]).ToList(), "empty lineup");

            var shared = new[] { new LineupSlot(Ninja("kaito"), 3, 3), new LineupSlot(Ninja("hana"), 3, 3) };
            StringAssert.Contains("shares its hex", LineupRules.Validate(shared).Single());

            var offBoard = new[] { new LineupSlot(Ninja("kaito"), 3, 5) };
            StringAssert.Contains("off the board", LineupRules.Validate(offBoard).Single());

            var kunai = Weapon("kunai");
            var tooManyWeapons = new[] { new LineupSlot(Ninja("kaito"), 3, 3, kunai, kunai, kunai, kunai) };
            StringAssert.Contains("4 weapons", LineupRules.Validate(tooManyWeapons).Single());

            var tooMany = Enumerable.Range(0, 7).Select(i => new LineupSlot(Ninja("kaito"), i, 0)).ToList();
            Assert.IsTrue(LineupRules.Validate(tooMany).Any(message => message.Contains("the most is 6")));
        }

        [Test]
        public void Synergies_CountEachNinjaOnceAndListActiveTraitsFirst()
        {
            var slots = new[]
            {
                new LineupSlot(Ninja("kaito"), 0, 3), // Fire, Blademaster
                new LineupSlot(Ninja("mei"), 1, 3), // Fire, Mystic
                new LineupSlot(Ninja("goro"), 2, 3), // Earth, Blademaster
                new LineupSlot(Ninja("hana"), 3, 3), // Water, Medic
            };

            var synergies = LineupSynergies.Count(slots);

            Assert.AreEqual(6, synergies.Count);
            var active = synergies.Where(s => s.ActiveTier != null).Select(s => s.Trait.Id).ToList();
            CollectionAssert.AreEqual(new[] { "trait.class.blademaster", "trait.element.fire" }, active);
            CollectionAssert.AreEqual(active, synergies.Take(2).Select(s => s.Trait.Id).ToList(), "active traits come first");
            Assert.AreEqual(3, synergies[0].NextTier.requiredCount);

            var fire = synergies.Single(s => s.Trait.Id == "trait.element.fire");
            Assert.AreEqual(2, fire.Count);
        }

        // ---------------------------------------------------------------- player lineup

        [Test]
        public void Place_FailsWhenTheRulesSayNo()
        {
            var lineup = new PlayerLineup(maxUnits: 2);

            Assert.IsNotNull(lineup.Place(Ninja("kaito"), 3, 3));
            Assert.IsNull(lineup.Place(Ninja("kaito"), 2, 3), "the same ninja twice");
            Assert.IsNull(lineup.Place(Ninja("hana"), 3, 3), "a taken hex");
            Assert.IsNull(lineup.Place(Ninja("hana"), 9, 3), "off the board");
            Assert.IsNotNull(lineup.Place(Ninja("hana"), 3, 0));
            Assert.IsTrue(lineup.IsFull);
            Assert.IsNull(lineup.Place(Ninja("mei"), 4, 0), "a full lineup");
            Assert.AreEqual(2, lineup.Slots.Count);
        }

        [Test]
        public void Move_ToATakenHexSwapsTheTwoNinjas()
        {
            var lineup = new PlayerLineup();
            var kaito = lineup.Place(Ninja("kaito"), 3, 3);
            var hana = lineup.Place(Ninja("hana"), 3, 0);

            Assert.IsTrue(lineup.Move(kaito, 3, 0));

            Assert.AreEqual((3, 0), (kaito.column, kaito.rowFromBack));
            Assert.AreEqual((3, 3), (hana.column, hana.rowFromBack));
            Assert.IsFalse(lineup.Move(kaito, 3, 4), "off the board");
        }

        [Test]
        public void Equip_StopsAtThreeWeaponsAndTracksWhatIsEquipped()
        {
            var lineup = new PlayerLineup();
            var kaito = lineup.Place(Ninja("kaito"), 3, 3);
            var katana = Weapon("shadow_katana");

            Assert.IsFalse(lineup.IsEquipped(katana));
            Assert.IsTrue(lineup.Equip(kaito, katana));
            Assert.IsTrue(lineup.Equip(kaito, Weapon("kunai")));
            Assert.IsTrue(lineup.Equip(kaito, Weapon("iron_plate")));
            Assert.IsFalse(lineup.Equip(kaito, Weapon("jade_charm")));
            Assert.IsTrue(lineup.IsEquipped(katana));

            Assert.IsTrue(lineup.Unequip(kaito, 0));
            Assert.IsFalse(lineup.IsEquipped(katana));
            Assert.AreEqual(2, kaito.weapons.Count);
        }

        [Test]
        public void Changes_RaiseTheChangedEvent()
        {
            var lineup = new PlayerLineup();
            var changes = 0;
            lineup.Changed += () => changes++;

            var kaito = lineup.Place(Ninja("kaito"), 3, 3);
            lineup.Equip(kaito, Weapon("kunai"));
            lineup.Move(kaito, 2, 3);
            lineup.Remove(kaito);
            lineup.Place(Ninja("kaito"), 0, 0); // a ninja can come back after being removed

            Assert.AreEqual(5, changes);
        }

        [Test]
        public void Json_RoundTripsThroughContentIds()
        {
            var lineup = new PlayerLineup();
            var kaito = lineup.Place(Ninja("kaito"), 3, 3);
            lineup.Equip(kaito, Weapon("shadow_katana"));
            lineup.Equip(kaito, Weapon("kunai"));
            lineup.Place(Ninja("nami"), 5, 0);

            var loaded = new PlayerLineup();
            Assert.IsTrue(loaded.LoadJson(lineup.ToJson(), database));

            Assert.AreEqual(2, loaded.Slots.Count);
            var loadedKaito = loaded.At(3, 3);
            Assert.AreSame(Ninja("kaito"), loadedKaito.ninja);
            CollectionAssert.AreEqual(new[] { Weapon("shadow_katana"), Weapon("kunai") }, loadedKaito.weapons);
            Assert.AreSame(Ninja("nami"), loaded.At(5, 0).ninja);
        }

        [Test]
        public void LoadJson_SkipsUnknownIdsAndRejectsGarbage()
        {
            const string json = "{\"slots\":[{\"ninja\":\"ninja.removed\",\"column\":1,\"row\":1,\"weapons\":[]}," +
                                "{\"ninja\":\"ninja.hana\",\"column\":2,\"row\":0,\"weapons\":[\"weapon.removed\",\"weapon.kunai\"]}]}";
            var lineup = new PlayerLineup();

            Assert.IsTrue(lineup.LoadJson(json, database));
            Assert.AreEqual(1, lineup.Slots.Count);
            CollectionAssert.AreEqual(new[] { Weapon("kunai") }, lineup.Slots[0].weapons);

            Assert.IsFalse(new PlayerLineup().LoadJson("", database));
            Assert.IsFalse(new PlayerLineup().LoadJson("not json", database));
        }
    }
}
