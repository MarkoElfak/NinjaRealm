using System;
using System.Linq;
using NinjaRealm.Core.Campaign;
using NinjaRealm.Core.Content;
using NinjaRealm.Core.Definitions;
using NinjaRealm.Core.Progression;
using NinjaRealm.Core.Prototype;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace NinjaRealm.Tests
{
    /// <summary>Where Karma comes from: runes on the altar, a boss's first clear and Karma packs. Nothing else pays it.</summary>
    public class KarmaTests
    {
        static readonly DateTime Start = new DateTime(2026, 11, 20, 12, 0, 0, DateTimeKind.Utc);

        InMemoryContentSink sink;
        ContentDatabase database;
        ProgressionDefinition progression;
        MemorySaveStorage storage;
        ProfileService service;
        DateTime now;

        [SetUp]
        public void CreateProfile()
        {
            sink = new InMemoryContentSink();
            PrototypeContent.Build(sink);
            database = sink.CreateDatabase();
            progression = ProfileService.FindProgression(database);
            storage = new MemorySaveStorage();
            service = ProfileService.Load(storage, database, 42);
            now = Start;
            service.Clock = () => now;
        }

        [TearDown]
        public void DestroyContent()
        {
            foreach (var definition in sink.Definitions) Object.DestroyImmediate(definition);
            Object.DestroyImmediate(database);
        }

        RuneDefinition Rune(string name) => database.Get<RuneDefinition>("rune." + name);

        StageDefinition Stage(string place) => database.Get<StageDefinition>("stage." + place);

        int Karma => service.Balance(progression.Karma);

        /// <summary>Owns a rune at a rank without paying and sets it in a slot, lighting slots as needed.</summary>
        void Set(string name, int slot, int rank = 1)
        {
            service.Profile.runes.Add(new OwnedRune { id = "rune." + name, rank = rank });
            while (service.Profile.runeSlots.Count <= slot) service.Profile.runeSlots.Add(string.Empty);
            Assert.IsTrue(service.TrySetRune(slot, Rune(name), out var message), message);
        }

        // ---------------------------------------------------------------- rates

        [Test]
        public void RuneKarma_GrowsWithRarityAndRank()
        {
            Assert.AreEqual(2, service.RuneKarmaPerHour(Rune("ember_fang"), 1));
            Assert.AreEqual(6, service.RuneKarmaPerHour(Rune("ember_fang"), 5));
            Assert.AreEqual(4, service.RuneKarmaPerHour(Rune("hawk_eye"), 1));
            Assert.AreEqual(12, service.RuneKarmaPerHour(Rune("leech_fang"), 3));
            Assert.AreEqual(30, service.RuneKarmaPerHour(Rune("phoenix_ash"), 5));
        }

        [Test]
        public void EmptyAltar_MakesNoKarma()
        {
            service.StartIncome(now);
            now = Start.AddHours(10);
            Assert.AreEqual(0, service.AltarKarmaPerHour);
            Assert.AreEqual(0, service.PendingAltarKarma(now));
            Assert.IsFalse(service.TryCollectAltarKarma(now, out var message));
            StringAssert.Contains("Set a rune", message);
        }

        // ---------------------------------------------------------------- filling and collecting

        [Test]
        public void SetRune_MakesKarmaUntilTheAltarIsFull()
        {
            Set("ember_fang", 0);
            Assert.AreEqual(2, service.AltarKarmaPerHour);
            Assert.AreEqual(24, service.AltarKarmaCapacity, "12 hours of storage");

            Assert.AreEqual(2, service.PendingAltarKarma(Start.AddHours(1)));
            Assert.AreEqual(10, service.PendingAltarKarma(Start.AddHours(5)));
            Assert.AreEqual(TimeSpan.FromHours(7), service.AltarKarmaTimeToFull(Start.AddHours(5)));
            Assert.AreEqual(24, service.PendingAltarKarma(Start.AddHours(30)), "it stops at the capacity");
            Assert.AreEqual(TimeSpan.Zero, service.AltarKarmaTimeToFull(Start.AddHours(30)));
        }

        [Test]
        public void Collect_MovesKarmaToTheWalletAndKeepsThePartMade()
        {
            Set("ember_fang", 0);
            var karma = Karma;

            now = Start.AddMinutes(105);
            Assert.IsTrue(service.TryCollectAltarKarma(now, out var message), message);
            Assert.AreEqual(karma + 3, Karma, "an hour and three quarters at 2 an hour");
            Assert.AreEqual(0, service.PendingAltarKarma(now));

            // The 15 minutes past the third Karma count toward the next one.
            Assert.AreEqual(1, service.PendingAltarKarma(Start.AddHours(2)));
        }

        [Test]
        public void ChangingTheAltar_KeepsWhatTheOldRunesMade()
        {
            Set("oak_heart", 0);
            now = Start.AddHours(4);
            Set("hawk_eye", 1);
            Assert.AreEqual(6, service.AltarKarmaPerHour);
            Assert.AreEqual(8, service.PendingAltarKarma(now), "four hours at 2 an hour, not at the new 6");
            Assert.AreEqual(14, service.PendingAltarKarma(Start.AddHours(5)));

            now = Start.AddHours(5);
            Assert.IsTrue(service.ClearRuneSlot(0));
            Assert.IsTrue(service.ClearRuneSlot(1));
            Assert.AreEqual(14, service.PendingAltarKarma(Start.AddHours(20)), "taken out, the altar keeps what it made and stops");

            var loaded = ProfileService.Load(storage, database, 42);
            Assert.AreEqual(14, loaded.PendingAltarKarma(Start.AddHours(20)), "the banked Karma is saved");
        }

        [Test]
        public void RankUp_RaisesTheRateFromThenOn()
        {
            Set("ember_fang", 0);
            service.Profile.clanLevel = 3;
            service.Grant(new[] { new ResourceAmount(progression.Gold, 1000) });

            now = Start.AddHours(2);
            Assert.IsTrue(service.TryRankUpRune(Rune("ember_fang"), out var message), message);
            Assert.AreEqual(3, service.AltarKarmaPerHour);
            Assert.AreEqual(4 + 3, service.PendingAltarKarma(Start.AddHours(3)));
        }

        // ---------------------------------------------------------------- bosses and nothing else

        [Test]
        public void OnlyABossFirstClear_PaysKarma()
        {
            var karma = Karma;
            foreach (var stage in database.Get<ChapterDefinition>("chapter.bandit_hills").Stages)
            {
                var rewards = service.ApplyStageResult(stage, 3, 1);
                Assert.AreEqual(stage.Kind == StageKind.Boss ? 250 : 0, rewards.FirstClearKarma, stage.Id);
            }

            Assert.That(service.ClanLevel, Is.GreaterThan(1), "the chapter raised the clan level");
            Assert.AreEqual(karma + 250, Karma, "the boss's 250 and nothing from stages, clan levels or the chest");

            var boss = Stage("1_8");
            Assert.AreEqual(0, service.ApplyStageResult(boss, 3, 2).FirstClearKarma, "a boss pays Karma once");
            Assert.AreEqual(karma + 250, Karma);
        }

        [Test]
        public void StageThatIsNotABoss_MayNotPayKarma()
        {
            var lineup = Stage("1_1").Lineup;
            var stage = ScriptableObject.CreateInstance<StageDefinition>();
            try
            {
                stage.Configure(lineup, 1, StageKind.Elite, 1);
                stage.SetFirstClear(50, 0, null);
                Assert.IsTrue(stage.ValidateSelf().Any(issue => issue.Contains("Only bosses pay Karma")));

                stage.Configure(lineup, 1, StageKind.Boss, 1);
                stage.SetFirstClear(50, 0, null);
                Assert.IsFalse(stage.ValidateSelf().Any(issue => issue.Contains("Karma")));
            }
            finally
            {
                Object.DestroyImmediate(stage);
            }
        }

        // ---------------------------------------------------------------- Karma packs

        [Test]
        public void KarmaPack_PaysOncePerPurchase()
        {
            Assert.AreEqual(3, progression.KarmaPacks.Count);
            var karma = Karma;

            Assert.IsTrue(service.TryGrantKarmaPack("karma_small", "order-1", out var message), message);
            Assert.AreEqual(karma + 500, Karma);
            Assert.IsFalse(service.TryGrantKarmaPack("karma_small", "order-1", out _), "the same purchase pays once");
            Assert.IsTrue(service.TryGrantKarmaPack("karma_large", "order-2", out _));
            Assert.AreEqual(karma + 3500, Karma);

            Assert.IsFalse(service.TryGrantKarmaPack("karma_nope", "order-3", out _));
            Assert.IsFalse(service.TryGrantKarmaPack("karma_small", "", out _));
            Assert.AreEqual(karma + 3500, Karma);

            var loaded = ProfileService.Load(storage, database, 42);
            Assert.IsFalse(loaded.TryGrantKarmaPack("karma_small", "order-1", out _), "paid purchases are saved");
        }
    }
}
