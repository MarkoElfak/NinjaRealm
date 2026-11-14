using System.Linq;
using NinjaRealm.Combat;
using NinjaRealm.Core.Content;
using NinjaRealm.Core.Definitions;
using NinjaRealm.Core.Fights;
using NinjaRealm.Core.Lineups;
using NinjaRealm.Core.Prototype;
using NUnit.Framework;
using UnityEngine;

namespace NinjaRealm.Tests
{
    /// <summary>The prototype content built in memory: it is valid, its enemy lineups fight, and the balance tools run.</summary>
    public class PrototypeContentTests
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

        [Test]
        public void Content_IsValid()
        {
            var issues = ContentValidation.Validate(sink.Definitions).Select(issue => issue.Message).ToList();
            Assert.IsEmpty(issues, string.Join("\n", issues));
        }

        [Test]
        public void FileNames_AreLettersAndDigitsOnly()
        {
            Assert.AreEqual("ShogunsGuard", PrototypeContent.FileName("Shogun's Guard"));
            Assert.AreEqual("FireLotus", PrototypeContent.FileName("Fire Lotus"));
        }

        [Test]
        public void EnemyLineups_HaveOneOfEachDifficulty()
        {
            var lineups = database.GetAll<LineupDefinition>().ToList();

            Assert.AreEqual(5, lineups.Count);
            CollectionAssert.AreEquivalent(new[] { 1, 2, 3, 4, 5 }, lineups.Select(l => l.Difficulty));
            foreach (var lineup in lineups)
            {
                Assert.IsNotEmpty(lineup.Description, lineup.Id);
                Assert.IsEmpty(LineupRules.Validate(lineup.Slots).ToList(), lineup.Id);
            }
        }

        [Test]
        public void EnemyLineups_FightToTheEndTheSameWayEveryTime()
        {
            var player = PrototypeContent.BalanceArchetypes(database)[0].Slots;
            foreach (var lineup in database.GetAll<LineupDefinition>())
            {
                CombatResult Fight()
                {
                    var builder = CombatSetupBuilder.FromDatabase(database);
                    builder.AddLineup(0, player);
                    builder.AddLineup(1, lineup.Slots);
                    return new CombatSimulation(builder.Build(), 123).Run();
                }

                var first = Fight();
                var second = Fight();
                Assert.AreEqual(player.Count + lineup.Slots.Count, first.Units.Count, lineup.Id);
                Assert.AreEqual(first.WinningTeam, second.WinningTeam, lineup.Id);
                Assert.AreEqual(first.DurationTicks, second.DurationTicks, lineup.Id);
            }
        }

        [Test]
        public void AddLineup_PlacesTeamOneOnTheMirroredSide()
        {
            var slot = new LineupSlot(database.Get<NinjaDefinition>("ninja.kaito"), 2, 3);
            var builder = CombatSetupBuilder.FromDatabase(database);
            builder.AddLineup(0, new[] { slot });
            builder.AddLineup(1, new[] { slot });

            var units = builder.Build().Units;

            Assert.AreEqual(builder.TeamPosition(0, 2, 3), units[0].Position);
            Assert.AreEqual(builder.TeamPosition(1, 2, 3), units[1].Position);
            Assert.AreEqual(3, units[0].Position.Row);
            Assert.AreEqual(4, units[1].Position.Row);
        }

        [Test]
        public void BalanceRunner_PlaysBothSidesAndReports()
        {
            var teams = PrototypeContent.BalanceArchetypes(database);
            var runner = new BalanceRunner(database) { SeedsPerSide = 2 };

            var result = runner.Run(teams[0], teams[1]);
            Assert.AreEqual(4, result.Fights);
            Assert.AreEqual(result.Fights, result.WinsA + result.WinsB + result.Draws);
            Assert.That(result.WinRateA, Is.InRange(0.0, 1.0));

            var report = runner.RoundRobin(teams.Take(3).ToList(), "Smoke test");
            StringAssert.Contains("Smoke test", report);
            foreach (var team in teams.Take(3)) StringAssert.Contains(team.Name, report);
            StringAssert.Contains("Kaito", runner.NinjaReport());
        }
    }
}
