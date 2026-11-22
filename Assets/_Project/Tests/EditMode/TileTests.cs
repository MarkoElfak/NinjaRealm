using System.Collections.Generic;
using System.Linq;
using NinjaRealm.Combat;
using NinjaRealm.Core.Campaign;
using NinjaRealm.Core.Content;
using NinjaRealm.Core.Definitions;
using NinjaRealm.Core.Fights;
using NinjaRealm.Core.Lineups;
using NinjaRealm.Core.Prototype;
using NUnit.Framework;
using UnityEngine;

namespace NinjaRealm.Tests
{
    /// <summary>Special board tiles: who they affect, how fights apply them, stage validation and the walker's placement.</summary>
    public class TileTests
    {
        InMemoryContentSink sink;
        ContentDatabase database;
        readonly List<Object> created = new List<Object>();

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

        [TearDown]
        public void DestroyCreated()
        {
            foreach (var target in created) Object.DestroyImmediate(target);
            created.Clear();
        }

        NinjaDefinition Ninja(string name) => database.Get<NinjaDefinition>("ninja." + name);

        TileDefinition Tile(string name) => database.Get<TileDefinition>("tile." + name);

        /// <summary>One ninja for each team, the player's at a place with optional tiles; returns the player's unit.</summary>
        CombatUnit Fight(NinjaDefinition ninja, int column, int rowFromBack, params BoardTile[] tiles)
        {
            var builder = CombatSetupBuilder.FromDatabase(database);
            builder.AddLineup(0, new[] { new LineupSlot(ninja, column, rowFromBack) });
            builder.AddLineup(1, new[] { new LineupSlot(Ninja("daichi"), 3, 3) });
            builder.AddTiles(tiles);
            return new CombatSimulation(builder.Build(), 1).Units[0];
        }

        static BoardTile Mine(TileDefinition tile, int column, int rowFromBack) => new BoardTile(tile, 0, column, rowFromBack);

        [Test]
        public void PrototypeTiles_AreFiveTypedAndFourTradeOffs()
        {
            var tiles = database.GetAll<TileDefinition>().ToList();
            Assert.AreEqual(9, tiles.Count);
            Assert.AreEqual(4, tiles.Count(tile => tile.IsTradeOff), "Trade-off tiles give everyone a bonus and a drawback.");
            foreach (var tile in tiles)
            {
                Assert.IsEmpty(tile.ValidateSelf().ToList(), tile.Id);
                Assert.IsTrue(tile.Parts.Any(part => !part.harmful), $"{tile.Id} has no bonus.");
                Assert.IsTrue(tile.Parts.Any(part => part.harmful), $"{tile.Id} has no drawback.");
                Assert.IsNotEmpty(tile.Description, tile.Id);
            }
        }

        [Test]
        public void Stages_BringTilesInFromTheThirdStage()
        {
            var campaign = new CampaignMap(database);
            var stages = campaign.Stages;
            Assert.IsEmpty(stages[0].Tiles);
            Assert.IsEmpty(stages[1].Tiles);
            for (var i = 2; i < stages.Count; i++)
            {
                Assert.IsNotEmpty(stages[i].Tiles, stages[i].Id);
                Assert.IsEmpty(BoardTile.Validate(stages[i].Tiles).ToList(), stages[i].Id);
            }
        }

        [Test]
        public void EnemyTiles_LieUnderAnEnemyTheyHelp()
        {
            foreach (var stage in database.GetAll<StageDefinition>())
                foreach (var entry in stage.Tiles.Where(entry => entry.team == 1))
                {
                    var slot = stage.Lineup.Slots.FirstOrDefault(s => s.column == entry.column && s.rowFromBack == entry.rowFromBack);
                    Assert.IsNotNull(slot, $"{stage.Id}: {entry.tile.Id} at {entry.column},{entry.rowFromBack} has nobody on it.");
                    Assert.GreaterOrEqual(entry.tile.Score(slot.ninja), 0, $"{stage.Id}: {entry.tile.Id} hurts {slot.ninja.Id}.");
                }
        }

        [Test]
        public void EmberBrazier_RaisesFireAttackDamage()
        {
            var plain = Fight(Ninja("kaito"), 2, 3);
            var onTile = Fight(Ninja("kaito"), 2, 3, Mine(Tile("ember_brazier"), 2, 3));

            Assert.AreEqual(Fixed.AddPercent(plain.GetStat(StatIds.AttackDamage), 20 * Fixed.One), onTile.GetStat(StatIds.AttackDamage));
        }

        [Test]
        public void EmberBrazier_CracksEarthResists()
        {
            var plain = Fight(Ninja("daichi"), 2, 3);
            var onTile = Fight(Ninja("daichi"), 2, 3, Mine(Tile("ember_brazier"), 2, 3));

            Assert.AreEqual(plain.GetStat(StatIds.Armor) - 20 * Fixed.One, onTile.GetStat(StatIds.Armor));
            Assert.AreEqual(plain.GetStat(StatIds.MagicResist) - 20 * Fixed.One, onTile.GetStat(StatIds.MagicResist));
            Assert.AreEqual(plain.GetStat(StatIds.AttackDamage), onTile.GetStat(StatIds.AttackDamage));
        }

        [Test]
        public void Tile_DoesNothingToANinjaItDoesNotName()
        {
            var plain = Fight(Ninja("hana"), 2, 3);
            var onTile = Fight(Ninja("hana"), 2, 3, Mine(Tile("ember_brazier"), 2, 3));

            Assert.AreEqual(plain.GetStat(StatIds.AttackDamage), onTile.GetStat(StatIds.AttackDamage));
            Assert.AreEqual(plain.GetStat(StatIds.Armor), onTile.GetStat(StatIds.Armor));
            Assert.IsNull(Tile("ember_brazier").DescribeFor(Ninja("hana")));
        }

        [Test]
        public void Tile_DoesNothingToANinjaNextToIt()
        {
            var plain = Fight(Ninja("kaito"), 2, 3);
            var nextTo = Fight(Ninja("kaito"), 2, 3, Mine(Tile("ember_brazier"), 3, 3));

            Assert.AreEqual(plain.GetStat(StatIds.AttackDamage), nextTo.GetStat(StatIds.AttackDamage));
        }

        [Test]
        public void TradeOffTile_GivesTheSameNinjaABonusAndADrawback()
        {
            var plain = Fight(Ninja("hana"), 3, 2);
            var onTile = Fight(Ninja("hana"), 3, 2, Mine(Tile("blood_altar"), 3, 2));

            Assert.Greater(onTile.GetStat(StatIds.AttackDamage), plain.GetStat(StatIds.AttackDamage));
            Assert.Less(onTile.MaxHealth, plain.MaxHealth);
            Assert.AreEqual(onTile.MaxHealth, onTile.Health, "Fights start at full health after the drawback.");
            Assert.AreEqual(0, Tile("blood_altar").Score(Ninja("hana")));
        }

        [Test]
        public void Barricade_ShieldsAVanguardAtTheStart()
        {
            var plain = Fight(Ninja("daichi"), 3, 3);
            var onTile = Fight(Ninja("daichi"), 3, 3, Mine(Tile("barricade"), 3, 3));

            Assert.AreEqual(0, plain.ShieldTotal);
            Assert.AreEqual(onTile.MaxHealth / 5, onTile.ShieldTotal);
        }

        [Test]
        public void HighGround_GivesCastersRange()
        {
            var plain = Fight(Ninja("mei"), 3, 0);
            var onTile = Fight(Ninja("mei"), 3, 0, Mine(Tile("high_ground"), 3, 0));

            Assert.AreEqual(plain.GetStat(StatIds.Range) + Fixed.One, onTile.GetStat(StatIds.Range));
        }

        [Test]
        public void EnemyTile_HelpsTheEnemyOnTheMirroredHex()
        {
            var builder = CombatSetupBuilder.FromDatabase(database);
            builder.AddLineup(0, new[] { new LineupSlot(Ninja("kaito"), 3, 3) });
            builder.AddLineup(1, new[] { new LineupSlot(Ninja("kaito"), 3, 3) });
            builder.AddTiles(new[] { new BoardTile(Tile("ember_brazier"), 1, 3, 3) });
            var units = new CombatSimulation(builder.Build(), 1).Units;

            Assert.AreEqual(Fixed.AddPercent(units[0].GetStat(StatIds.AttackDamage), 20 * Fixed.One), units[1].GetStat(StatIds.AttackDamage));
        }

        [Test]
        public void Validate_ReportsTilesInTheWrongPlace()
        {
            var tiles = new List<BoardTile>
            {
                Mine(Tile("high_ground"), 3, 3), // the front row
                Mine(Tile("barricade"), 2, 0), // the back row
                Mine(Tile("ember_brazier"), 4, 1),
                Mine(Tile("blood_altar"), 4, 1), // same hex
                Mine(Tile("blood_altar"), 7, 1), // off the board
                new BoardTile(null, 0, 1, 1),
            };

            Assert.AreEqual(5, BoardTile.Validate(tiles).Count());
        }

        [Test]
        public void Walker_PutsTheRightNinjaOnAHelpfulTileAndStepsOffAHarmfulOne()
        {
            var stage = ScriptableObject.CreateInstance<StageDefinition>();
            created.Add(stage);
            stage.SetTiles(new[] { Mine(Tile("ember_brazier"), 2, 3), Mine(Tile("stone_ridge"), 4, 3) });

            var kaito = new LineupSlot(Ninja("kaito"), 3, 3);
            var daichi = new LineupSlot(Ninja("daichi"), 2, 3);
            var hana = new LineupSlot(Ninja("hana"), 4, 3);
            var slots = new List<LineupSlot> { daichi, kaito, hana };

            CampaignWalker.UseTiles(slots, stage);

            Assert.AreEqual((2, 3), (kaito.column, kaito.rowFromBack), "Kaito (Fire) takes the brazier.");
            Assert.AreEqual((4, 3), (daichi.column, daichi.rowFromBack), "Daichi (Earth) takes the ridge.");
            Assert.IsNull(BoardTile.At(stage.Tiles, 0, hana.column, hana.rowFromBack), "Hana (Water) leaves the ridge.");
            Assert.AreEqual(3, slots.Select(slot => (slot.column, slot.rowFromBack)).Distinct().Count());
            Assert.IsEmpty(LineupRules.Validate(slots).ToList());
        }
    }
}
