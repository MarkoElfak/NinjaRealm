using System.Collections.Generic;
using System.Linq;
using NinjaRealm.Combat;
using NinjaRealm.Core.Campaign;
using NinjaRealm.Core.Content;
using NinjaRealm.Core.Definitions;
using NinjaRealm.Core.Fights;
using NinjaRealm.Core.Progression;
using NinjaRealm.Core.Prototype;
using NUnit.Framework;
using UnityEngine;

namespace NinjaRealm.Tests
{
    /// <summary>The campaign on the prototype content: its shape, unlocking, stars, first clears, star chests and old saves.</summary>
    public class CampaignTests
    {
        InMemoryContentSink sink;
        ContentDatabase database;
        ProgressionDefinition progression;
        ProfileService service;

        [SetUp]
        public void CreateProfile()
        {
            sink = new InMemoryContentSink();
            PrototypeContent.Build(sink);
            database = sink.CreateDatabase();
            progression = ProfileService.FindProgression(database);
            service = ProfileService.Load(new MemorySaveStorage(), database, 42);
        }

        [TearDown]
        public void DestroyContent()
        {
            foreach (var definition in sink.Definitions) Object.DestroyImmediate(definition);
            Object.DestroyImmediate(database);
        }

        NinjaDefinition Ninja(string name) => database.Get<NinjaDefinition>("ninja." + name);

        StageDefinition Stage(string place) => database.Get<StageDefinition>("stage." + place);

        ChapterDefinition Chapter(string name) => database.Get<ChapterDefinition>("chapter." + name);

        int Gold => service.Balance(progression.Gold);

        int Karma => service.Balance(progression.Karma);

        int Steel => service.Balance(progression.Steel);

        // ---------------------------------------------------------------- shape

        [Test]
        public void Campaign_IsThreeChaptersOfEightStagesInOrder()
        {
            var campaign = service.Campaign;

            CollectionAssert.AreEqual(new[] { "chapter.bandit_hills", "chapter.ember_valley", "chapter.tide_coast" }, campaign.Chapters.Select(c => c.Id));
            Assert.AreEqual(24, campaign.Stages.Count);
            foreach (var chapter in campaign.Chapters)
            {
                Assert.AreEqual(8, chapter.Stages.Count, chapter.Id);
                Assert.AreEqual(24, chapter.MaxStars, chapter.Id);
                Assert.IsNotEmpty(chapter.Description, chapter.Id);
                CollectionAssert.AreEqual(new[] { 8, 16, 24 }, chapter.Chests.Select(chest => chest.stars), chapter.Id);
            }

            Assert.AreEqual("1-1", campaign.LabelOf(campaign.Stages[0]));
            Assert.AreEqual("2-4", campaign.LabelOf(Stage("2_4")));
            Assert.AreEqual("3-8", campaign.LabelOf(campaign.Stages[23]));
            Assert.AreEqual(Chapter("ember_valley"), campaign.ChapterOf(Stage("2_4")));
            Assert.IsNull(campaign.Previous(campaign.Stages[0]));
            Assert.AreEqual(Stage("2_1"), campaign.Next(Stage("1_8")));
            Assert.IsNull(campaign.Next(campaign.Stages[23]));
        }

        [Test]
        public void Stages_GetHarderAndPayMoreAsTheyGo()
        {
            var stages = service.Campaign.Stages;
            for (var i = 0; i < stages.Count; i++)
            {
                var stage = stages[i];
                var place = i % 8 + 1;
                Assert.IsNotNull(stage.Lineup, stage.Id);
                Assert.AreEqual(place == 8 ? StageKind.Boss : place == 4 ? StageKind.Elite : StageKind.Normal, stage.Kind, stage.Id);
                Assert.AreEqual(Mathf.Min(5, i / 5 + 1), stage.RewardTier, stage.Id);
                Assert.AreEqual(stage.Kind == StageKind.Boss ? 250 * (i / 8 + 1) : 0, stage.FirstClearKarma, stage.Id + ": only bosses pay Karma");
                Assert.AreEqual(stage.Kind == StageKind.Elite, stage.FirstClearSteel > 0, stage.Id);
                Assert.AreEqual(stage.Kind == StageKind.Boss, stage.FirstClearNinja != null, stage.Id);
                Assert.AreEqual(stage.Kind == StageKind.Boss, stage.Boss != null, stage.Id);
                if (i == 0) continue;
                Assert.That(stage.Level, Is.GreaterThanOrEqualTo(stages[i - 1].Level), stage.Id);
                Assert.That(stage.Lineup.Slots.Count, Is.GreaterThanOrEqualTo(stages[i - 1].Lineup.Slots.Count), stage.Id);
            }

            Assert.AreEqual(24, stages.Select(stage => stage.Lineup).Distinct().Count(), "every stage has a lineup of its own");
        }

        [Test]
        public void Boss_FightsWithItsStars()
        {
            var chief = Stage("1_8");

            Assert.AreEqual(new NinjaRank(chief.Level, 2), chief.RankOf(Ninja("kaito")));
            Assert.AreEqual(new NinjaRank(chief.Level, 1), chief.RankOf(Ninja("daichi")));
            Assert.AreEqual(Ninja("mei"), chief.FirstClearNinja);
            Assert.AreEqual(new NinjaRank(1, 1), Stage("1_1").RankOf(Ninja("kaito")));
        }

        [Test]
        public void EveryStage_FightsToTheEndTheSameWayEveryTime()
        {
            var player = PrototypeContent.BalanceArchetypes(database)[0].Slots;
            foreach (var stage in service.Campaign.Stages)
            {
                CombatResult Fight()
                {
                    var builder = CombatSetupBuilder.FromDatabase(database);
                    builder.AddLineup(0, player);
                    builder.AddLineup(1, stage.Lineup.Slots, stage.RankOf);
                    return new CombatSimulation(builder.Build(), 77).Run();
                }

                var first = Fight();
                var second = Fight();
                Assert.AreEqual(first.WinningTeam, second.WinningTeam, stage.Id);
                Assert.AreEqual(first.DurationTicks, second.DurationTicks, stage.Id);
                Assert.That(StageStars.For(first), Is.InRange(0, StageStars.Max), stage.Id);
            }
        }

        // ---------------------------------------------------------------- walker

        [Test]
        public void Walker_PlaysTheSameWayForTheSameSeed()
        {
            var walker = new CampaignWalker(database) { MaxFights = 40, Seed = 3 };

            var first = walker.Run();
            var second = walker.Run();

            Assert.That(first.Fights, Is.InRange(1, 40));
            Assert.IsTrue(first.Stages[0].Cleared, "the starting team gets past the first stage within 40 fights");
            Assert.AreEqual(first.Fights, first.Stages.Sum(stage => stage.Tries + stage.FarmFights));
            Assert.AreEqual(first.Fights, second.Fights);
            CollectionAssert.AreEqual(first.Stages.Select(stage => stage.Tries), second.Stages.Select(stage => stage.Tries));
            CollectionAssert.AreEqual(first.Stages.Select(stage => stage.Stars), second.Stages.Select(stage => stage.Stars));

            var report = walker.Report(first);
            StringAssert.Contains("| 1-1 | Bandit Patrol |", report);
            StringAssert.Contains("Chapter 1, Bandit Hills", report);
        }

        // ---------------------------------------------------------------- unlocking

        [Test]
        public void Stages_UnlockOneAfterAnother()
        {
            Assert.IsTrue(service.IsUnlocked(Stage("1_1")));
            Assert.IsFalse(service.IsUnlocked(Stage("1_2")));
            Assert.IsFalse(service.IsUnlocked(Chapter("ember_valley")));
            Assert.AreEqual(Stage("1_1"), service.CurrentStage());

            service.ApplyStageResult(Stage("1_1"), 0, 1);
            Assert.IsFalse(service.IsUnlocked(Stage("1_2")), "a loss unlocks nothing");

            service.ApplyStageResult(Stage("1_1"), 1, 1);
            Assert.IsTrue(service.IsUnlocked(Stage("1_2")));
            Assert.IsFalse(service.IsUnlocked(Stage("1_3")));
            Assert.AreEqual(Stage("1_2"), service.CurrentStage());

            foreach (var stage in Chapter("bandit_hills").Stages) service.ApplyStageResult(stage, 1, 1);
            Assert.IsTrue(service.IsUnlocked(Chapter("ember_valley")));
            Assert.AreEqual(Stage("2_1"), service.CurrentStage());
        }

        [Test]
        public void SelectStage_KeepsThePickAndRefusesLockedStages()
        {
            service.ApplyStageResult(Stage("1_1"), 3, 1);
            service.ApplyStageResult(Stage("1_2"), 3, 1);

            Assert.IsTrue(service.SelectStage(Stage("1_1")), "a cleared stage can be fought again");
            Assert.AreEqual(Stage("1_1"), service.CurrentStage());
            Assert.IsFalse(service.SelectStage(Stage("1_5")));
            Assert.IsFalse(service.SelectStage(null));
            Assert.AreEqual(Stage("1_1"), service.CurrentStage());
        }

        [Test]
        public void FinishedCampaign_StaysOnTheLastStage()
        {
            foreach (var stage in service.Campaign.Stages) service.ApplyStageResult(stage, 3, 1);

            Assert.AreEqual(Stage("3_8"), service.NextUnclearedStage());
            Assert.AreEqual(72, service.Campaign.Chapters.Sum(chapter => service.ChapterStars(chapter)));
        }

        // ---------------------------------------------------------------- stars

        [Test]
        public void StageStars_CountTheTeamLeftStanding()
        {
            Assert.AreEqual(0, StageStars.For(false, 4, 4));
            Assert.AreEqual(3, StageStars.For(true, 4, 4));
            Assert.AreEqual(2, StageStars.For(true, 4, 2));
            Assert.AreEqual(1, StageStars.For(true, 4, 1));
            Assert.AreEqual(2, StageStars.For(true, 3, 2));
            Assert.AreEqual(1, StageStars.For(true, 3, 1));
            Assert.AreEqual(2, StageStars.For(true, 5, 3));
            Assert.AreEqual(3, StageStars.For(true, 1, 1));
        }

        [Test]
        public void Stars_KeepTheBestResult()
        {
            var stage = Stage("1_1");

            var first = service.ApplyStageResult(stage, 1, 1);
            Assert.AreEqual(1, first.NewStars);
            Assert.AreEqual(1, first.BestStars);

            var better = service.ApplyStageResult(stage, 3, 2);
            Assert.IsFalse(better.FirstWin);
            Assert.AreEqual(2, better.NewStars);
            Assert.AreEqual(3, service.StarsOf(stage));

            var worse = service.ApplyStageResult(stage, 2, 3);
            Assert.AreEqual(0, worse.NewStars);
            Assert.AreEqual(3, worse.BestStars);
            Assert.AreEqual(3, service.StarsOf(stage));
            Assert.AreEqual(3, service.ChapterStars(Chapter("bandit_hills")));

            var lost = service.ApplyStageResult(stage, 0, 4);
            Assert.AreEqual(3, lost.BestStars, "a loss keeps the stars");
        }

        // ---------------------------------------------------------------- first clears

        [Test]
        public void EliteFirstClear_AddsSteelOnce()
        {
            var elite = Stage("1_4");
            var steelBefore = Steel;

            var first = service.ApplyStageResult(elite, 2, 5);
            Assert.AreEqual(20, first.FirstClearSteel);
            Assert.AreEqual(0, first.FirstClearKarma, "an elite pays no Karma");
            Assert.AreEqual(steelBefore + first.Steel + 20, Steel);

            var again = service.ApplyStageResult(elite, 2, 5);
            Assert.AreEqual(0, again.FirstClearSteel);
            Assert.AreEqual(steelBefore + first.Steel + 20 + again.Steel, Steel);
        }

        [Test]
        public void BossFirstClear_BringsItsNinja()
        {
            Assert.IsFalse(service.Owns(Ninja("mei")));

            var first = service.ApplyStageResult(Stage("1_8"), 1, 5);
            Assert.IsTrue(service.Owns(Ninja("mei")));
            StringAssert.Contains("Mei", first.NinjaMessage);

            var again = service.ApplyStageResult(Stage("1_8"), 3, 5);
            Assert.IsNull(again.NinjaMessage);
            Assert.AreEqual(0, service.Find(Ninja("mei")).shards);
        }

        [Test]
        public void BossFirstClear_GivesAShardOfANinjaAlreadyOwned()
        {
            // Kaito is in the starting roster; a stage that pays Kaito gives a shard instead.
            var stage = ScriptableObject.CreateInstance<StageDefinition>();
            try
            {
                stage.SetId("stage.test_kaito");
                stage.Configure(Stage("1_1").Lineup, 1, StageKind.Boss, 1);
                stage.SetFirstClear(0, 0, Ninja("kaito"));

                var rewards = service.ApplyStageResult(stage, 3, 5);

                Assert.AreEqual(1, service.Find(Ninja("kaito")).shards);
                StringAssert.Contains("shard", rewards.NinjaMessage);
                Assert.IsFalse(service.HasCleared(Stage("1_1")), "only the fought stage is cleared");
            }
            finally
            {
                Object.DestroyImmediate(stage);
            }
        }

        // ---------------------------------------------------------------- star chests

        [Test]
        public void Chests_OpenOnceWhenTheChapterHasTheStars()
        {
            var chapter = Chapter("bandit_hills");
            var chest = chapter.Chests[0];
            Assert.IsFalse(service.TryOpenChest(chapter, chest, out var early));
            StringAssert.Contains("8 stars", early);

            service.ApplyStageResult(Stage("1_1"), 3, 1);
            service.ApplyStageResult(Stage("1_2"), 3, 1);
            service.ApplyStageResult(Stage("1_3"), 2, 1);
            Assert.AreEqual(8, service.ChapterStars(chapter));
            var gold = Gold;
            var steel = Steel;

            Assert.IsTrue(service.TryOpenChest(chapter, chest, out var opened));
            StringAssert.Contains("300", opened);
            Assert.AreEqual(gold + 300, Gold);
            Assert.AreEqual(steel + 20, Steel);
            Assert.IsTrue(service.ChestOpened(chapter, chest));

            Assert.IsFalse(service.TryOpenChest(chapter, chest, out _));
            Assert.AreEqual(gold + 300, Gold);
            Assert.IsNotNull(service.CannotOpenChest(chapter, chapter.Chests[1]));
            Assert.IsNotNull(service.CannotOpenChest(Chapter("ember_valley"), chest), "a chest only opens in its own chapter");
        }

        [Test]
        public void LastChest_HoldsACombinedWeapon()
        {
            var chapter = Chapter("bandit_hills");
            foreach (var stage in chapter.Stages) service.ApplyStageResult(stage, 3, 1);
            var karma = Karma;
            var gold = Gold;
            var boots = database.Get<WeaponDefinition>("weapon.wind_boots");

            Assert.IsTrue(service.TryOpenChest(chapter, chapter.Chests[1], out _));
            Assert.AreEqual(gold + 500, Gold);
            Assert.AreEqual(karma, Karma, "star chests hold no Karma");
            Assert.IsTrue(service.TryOpenChest(chapter, chapter.Chests[2], out var message));
            Assert.AreEqual(1, service.WeaponCount(boots));
            StringAssert.Contains("Wind Boots", message);
        }

        // ---------------------------------------------------------------- saves

        [Test]
        public void CampaignProgress_SurvivesALoad()
        {
            var storage = new MemorySaveStorage();
            var saved = ProfileService.Load(storage, database, 1);
            saved.ApplyStageResult(Stage("1_1"), 3, 1);
            saved.ApplyStageResult(Stage("1_2"), 2, 1);
            saved.ApplyStageResult(Stage("1_3"), 3, 1);
            var chapter = Chapter("bandit_hills");
            Assert.IsTrue(saved.TryOpenChest(chapter, chapter.Chests[0], out _));

            var loaded = ProfileService.Load(new MemorySaveStorage(storage.Text), database, 2);

            Assert.IsNull(loaded.LoadProblem);
            Assert.AreEqual(8, loaded.ChapterStars(chapter));
            Assert.AreEqual(2, loaded.StarsOf(Stage("1_2")));
            Assert.IsTrue(loaded.ChestOpened(chapter, chapter.Chests[0]));
            Assert.AreEqual(Stage("1_4"), loaded.CurrentStage());
        }

        [Test]
        public void OldSave_TurnsBeatenLineupsIntoClearedStages()
        {
            const string json = "{\"version\":1,\"clanLevel\":2,\"ninjas\":[{\"id\":\"ninja.kaito\",\"level\":2,\"stars\":1}]," +
                                "\"opponent\":\"lineup.ember_clan\",\"beatenLineups\":[\"lineup.bandit_patrol\",\"lineup.ember_clan\",\"lineup.gone\"]}";

            Assert.IsTrue(SaveFile.TryRead(json, out var profile, out var problem), problem);
            var loaded = new ProfileService(profile, database, progression);

            Assert.AreEqual(SaveFile.CurrentVersion, profile.version);
            Assert.AreEqual(1, loaded.StarsOf(Stage("1_1")));
            Assert.AreEqual(1, loaded.StarsOf(Stage("2_6")), "Ember Clan is stage 2-6");
            Assert.AreEqual(2, profile.stages.Count);
            Assert.IsEmpty(profile.beatenLineups);
            Assert.IsTrue(loaded.IsUnlocked(Stage("1_2")));
            Assert.IsTrue(loaded.IsUnlocked(Stage("2_6")), "a stage beaten before the campaign can be fought again");
            Assert.IsFalse(loaded.IsUnlocked(Stage("2_1")));
            Assert.AreEqual(Stage("1_2"), loaded.CurrentStage());
        }

        [Test]
        public void OldSave_IsCopiedBeforeItIsRewritten()
        {
            const string json = "{\"version\":1,\"clanLevel\":3,\"beatenLineups\":[\"lineup.bandit_patrol\"]}";
            var storage = new MemorySaveStorage(json);

            var loaded = ProfileService.Load(storage, database, 7);

            Assert.IsNull(loaded.LoadProblem);
            Assert.AreEqual("v1", storage.CopyLabel);
            Assert.AreEqual(json, storage.CopyText);
            Assert.AreEqual(1, storage.Writes, "the save is rewritten in the new format");
            Assert.IsTrue(SaveFile.TryRead(storage.Text, out var rewritten, out _, out var version));
            Assert.AreEqual(SaveFile.CurrentVersion, version);
            Assert.AreEqual("stage.1_1", rewritten.stages.Single().id);

            var again = new MemorySaveStorage(storage.Text);
            ProfileService.Load(again, database, 7);
            Assert.IsNull(again.CopyLabel, "a save in the current format is left alone");
            Assert.AreEqual(0, again.Writes);
        }

        [Test]
        public void DamagedSave_DropsUnknownStagesAndChests()
        {
            var profile = ProfileService.NewProfile(progression, 1);
            profile.stage = "stage.gone";
            profile.stages = new List<StageProgress>
            {
                new StageProgress { id = "stage.1_1", stars = 9 },
                new StageProgress { id = "stage.1_1", stars = 1 },
                new StageProgress { id = "stage.gone", stars = 3 },
                new StageProgress { id = "stage.1_2", stars = 0 },
                null,
            };
            profile.chests = new List<OpenedChest>
            {
                new OpenedChest { chapter = "chapter.bandit_hills", stars = 8 },
                new OpenedChest { chapter = "chapter.bandit_hills", stars = 8 },
                new OpenedChest { chapter = "chapter.bandit_hills", stars = 5 },
                new OpenedChest { chapter = "chapter.gone", stars = 8 },
            };

            var loaded = new ProfileService(profile, database, progression);

            Assert.AreEqual(1, profile.stages.Count);
            Assert.AreEqual(3, loaded.StarsOf(Stage("1_1")));
            Assert.AreEqual(1, profile.chests.Count);
            Assert.AreEqual(Stage("1_2"), loaded.CurrentStage());
        }
    }
}
