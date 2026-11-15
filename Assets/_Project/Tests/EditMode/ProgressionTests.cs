using System;
using System.Linq;
using NinjaRealm.Combat;
using NinjaRealm.Core.Content;
using NinjaRealm.Core.Definitions;
using NinjaRealm.Core.Fights;
using NinjaRealm.Core.Lineups;
using NinjaRealm.Core.Progression;
using NinjaRealm.Core.Prototype;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace NinjaRealm.Tests
{
    /// <summary>The persistent roster on the prototype content: costs, the Dojo, forging, fight rewards and the save file.</summary>
    public class ProgressionTests
    {
        static readonly DateTime Today = new DateTime(2026, 11, 15);

        InMemoryContentSink sink;
        ContentDatabase database;
        ProgressionDefinition progression;
        MemorySaveStorage storage;
        ProfileService service;

        [SetUp]
        public void CreateProfile()
        {
            sink = new InMemoryContentSink();
            PrototypeContent.Build(sink);
            database = sink.CreateDatabase();
            progression = ProfileService.FindProgression(database);
            storage = new MemorySaveStorage();
            service = ProfileService.Load(storage, database, 42);
        }

        [TearDown]
        public void DestroyContent()
        {
            foreach (var definition in sink.Definitions) Object.DestroyImmediate(definition);
            Object.DestroyImmediate(database);
        }

        NinjaDefinition Ninja(string name) => database.Get<NinjaDefinition>("ninja." + name);

        WeaponDefinition Weapon(string name) => database.Get<WeaponDefinition>("weapon." + name);

        LineupDefinition Opponent(string name) => database.Get<LineupDefinition>("lineup." + name);

        int Gold => service.Balance(progression.Gold);

        int Karma => service.Balance(progression.Karma);

        int Steel => service.Balance(progression.Steel);

        // ---------------------------------------------------------------- new save

        [Test]
        public void NewSave_StartsWithThreeNinjasTwoWeaponsAndResources()
        {
            CollectionAssert.AreEquivalent(new[] { "ninja.daichi", "ninja.hana", "ninja.kaito" }, service.OwnedNinjas().Select(n => n.Id));
            Assert.AreEqual(1, service.WeaponCount(Weapon("kunai")));
            Assert.AreEqual(1, service.WeaponCount(Weapon("iron_plate")));
            Assert.AreEqual(500, Gold);
            Assert.AreEqual(100, Karma);
            Assert.AreEqual(0, Steel);
            Assert.AreEqual(1, service.ClanLevel);
            Assert.AreEqual(3, service.TeamSize);
            Assert.AreEqual(1, storage.Writes, "a new save is written straight away");
        }

        [Test]
        public void Progression_HasSixtyLevelsTwelveBeltsAndThreeStars()
        {
            Assert.AreEqual(60, progression.MaxLevel);
            Assert.AreEqual(20, progression.LevelCost(1));
            Assert.AreEqual(22, progression.LevelCost(2));
            Assert.AreEqual(-1, progression.LevelCost(60));
            Assert.AreEqual("White", progression.Belt(1));
            Assert.AreEqual("Yellow", progression.Belt(6));
            Assert.AreEqual("Black, 6th dan", progression.Belt(60));
            Assert.AreEqual(3, progression.MaxStars);
            Assert.AreEqual(10000, progression.StatMultiplier(1, 1));
            Assert.AreEqual(13600, progression.StatMultiplier(10, 1));
            Assert.AreEqual(16000, progression.StatMultiplier(1, 2));
            Assert.AreEqual(21760, progression.StatMultiplier(10, 2));
            Assert.AreEqual(3, progression.TeamSize(2));
            Assert.AreEqual(4, progression.TeamSize(3));
            Assert.AreEqual(6, progression.TeamSize(20));
        }

        // ---------------------------------------------------------------- levels and stars

        [Test]
        public void LevelUp_SpendsKarmaUntilItRunsOut()
        {
            var kaito = Ninja("kaito");

            Assert.IsTrue(service.TryLevelUp(kaito, out _));
            Assert.AreEqual(2, service.RankOf(kaito).Level);
            Assert.AreEqual(80, Karma);

            Assert.IsTrue(service.TryLevelUp(kaito, out _));
            Assert.IsTrue(service.TryLevelUp(kaito, out _));
            Assert.IsTrue(service.TryLevelUp(kaito, out _));
            Assert.AreEqual(5, service.RankOf(kaito).Level);
            Assert.AreEqual(100 - 20 - 22 - 23 - 25, Karma);

            Assert.IsFalse(service.TryLevelUp(kaito, out var message));
            StringAssert.Contains("Karma", message);
            Assert.AreEqual(5, service.RankOf(kaito).Level);
        }

        [Test]
        public void LevelUp_StopsAtTheCapAndNeedsTheNinja()
        {
            service.Find(Ninja("kaito")).level = 60;
            service.Grant(new[] { new ResourceAmount(progression.Karma, 100000) });

            Assert.IsFalse(service.TryLevelUp(Ninja("kaito"), out _));
            Assert.IsFalse(service.TryLevelUp(Ninja("nami"), out _), "not owned");
        }

        [Test]
        public void Duplicates_BecomeShardsAndRaiseStars()
        {
            service.Grant(new[] { new ResourceAmount(progression.Gold, 100000) });
            var kaito = Ninja("kaito");

            for (var i = 0; i < 3; i++) Assert.IsTrue(service.TryBuyDirect(kaito, out _));
            Assert.AreEqual(2, service.RankOf(kaito).Stars);
            Assert.AreEqual(0, service.Find(kaito).shards);

            for (var i = 0; i < 6; i++) Assert.IsTrue(service.TryBuyDirect(kaito, out _));
            Assert.AreEqual(3, service.RankOf(kaito).Stars);

            Assert.IsFalse(service.TryBuyDirect(kaito, out var message), "no stars left to earn");
            StringAssert.Contains("most stars", message);
        }

        // ---------------------------------------------------------------- dojo

        [Test]
        public void Dojo_RollsFourOffersOncePerDay()
        {
            Assert.IsTrue(service.RefreshDojoIfNewDay(Today));
            var offers = service.DojoOffers();
            Assert.AreEqual(4, offers.Count);
            Assert.That(offers, Has.All.Not.Null);
            Assert.That(offers, Is.Unique, "a ninja is offered once per roll");

            Assert.IsFalse(service.RefreshDojoIfNewDay(Today), "same day");
            CollectionAssert.AreEqual(offers, service.DojoOffers());

            Assert.IsTrue(service.RefreshDojoIfNewDay(Today.AddDays(1)), "a new day");
            Assert.AreEqual(500, Gold, "the daily roll is free");
        }

        [Test]
        public void Dojo_IsTheSameForTheSameSeed()
        {
            var other = ProfileService.Load(new MemorySaveStorage(), database, 42);
            service.RefreshDojoIfNewDay(Today);
            other.RefreshDojoIfNewDay(Today);

            CollectionAssert.AreEqual(service.Profile.dojoOffers, other.Profile.dojoOffers);
        }

        [Test]
        public void Dojo_RecruitSpendsGoldAndEmptiesTheOffer()
        {
            service.Grant(new[] { new ResourceAmount(progression.Gold, 100000) });
            service.RefreshDojoIfNewDay(Today);
            var ninja = service.DojoOffers()[0];
            var gold = Gold;

            Assert.IsTrue(service.TryRecruitOffer(0, out _));
            Assert.AreEqual(gold - progression.RecruitPrice(ninja.Rarity), Gold);
            Assert.IsTrue(service.Owns(ninja));
            Assert.IsNull(service.DojoOffers()[0]);
            Assert.IsFalse(service.TryRecruitOffer(0, out _), "already recruited");
        }

        [Test]
        public void Dojo_RefreshCostsGold()
        {
            service.RefreshDojoIfNewDay(Today);

            Assert.IsTrue(service.TryRefreshDojo(out _));
            Assert.AreEqual(450, Gold);

            service.Profile.wallet.Add(progression.Gold, -Gold);
            Assert.IsFalse(service.TryRefreshDojo(out _));
        }

        [Test]
        public void DirectBuy_IsOnlyForCommonAndRareAtAHigherPrice()
        {
            Assert.That(service.DirectBuyNinjas().Select(n => n.Rarity), Has.All.LessThanOrEqualTo(Rarity.Rare));
            Assert.AreEqual(750, progression.DirectBuyPrice(Rarity.Rare));

            service.Grant(new[] { new ResourceAmount(progression.Gold, 100000) });
            Assert.IsFalse(service.TryBuyDirect(Ninja("nami"), out _), "Epic");
            Assert.IsTrue(service.TryBuyDirect(Ninja("mei"), out _));
            Assert.IsTrue(service.Owns(Ninja("mei")));
        }

        [Test]
        public void Recruiting_StopsWhenTheRosterIsFull()
        {
            // The prototype has fewer ninjas than the limit, so fill the roster with stand-ins.
            for (var i = 0; service.Profile.ninjas.Count < progression.RosterLimit; i++)
                service.Profile.ninjas.Add(new OwnedNinja { id = "ninja.stand_in_" + i });
            service.Grant(new[] { new ResourceAmount(progression.Gold, 100000) });

            Assert.IsTrue(service.RosterFull);
            Assert.IsFalse(service.TryBuyDirect(Ninja("mei"), out var message), "a new ninja doesn't fit");
            StringAssert.Contains("roster is full", message);
            Assert.IsTrue(service.TryBuyDirect(Ninja("kaito"), out _), "a shard of an owned ninja still fits");
        }

        // ---------------------------------------------------------------- weapons

        [Test]
        public void Forge_UsesBothComponentsAndSteel()
        {
            var katana = Weapon("shadow_katana");
            Assert.IsNotNull(service.CannotForge(katana), "only one Kunai");

            service.Profile.weapons.Add(new OwnedWeapon { id = "weapon.kunai", count = 1 });
            service = new ProfileService(service.Profile, database, progression, storage);
            Assert.IsNotNull(service.CannotForge(katana), "no Steel");

            service.Grant(new[] { new ResourceAmount(progression.Steel, 25) });
            Assert.IsTrue(service.TryForge(katana, out _));
            Assert.AreEqual(0, service.WeaponCount(Weapon("kunai")));
            Assert.AreEqual(1, service.WeaponCount(katana));
            Assert.AreEqual(5, Steel);
            Assert.IsFalse(service.TryForge(Weapon("kunai"), out _), "components aren't forged");
        }

        // ---------------------------------------------------------------- fights

        [Test]
        public void FirstWin_PaysABonusAndAWeapon()
        {
            var bandits = Opponent("bandit_patrol");
            var rewards = service.ApplyFightResult(bandits, true, 7);

            Assert.IsTrue(rewards.FirstWin);
            Assert.AreEqual(90, rewards.Gold);
            Assert.AreEqual(200, rewards.FirstWinGold);
            Assert.AreEqual(5, rewards.Steel);
            Assert.AreEqual(30, rewards.ClanXp);
            Assert.IsNotNull(rewards.Drop);
            Assert.AreEqual(WeaponTier.Component, rewards.Drop.Tier);
            Assert.AreEqual(500 + 290, Gold);
            Assert.AreEqual(5, Steel);
            Assert.IsTrue(service.HasBeaten(bandits));

            var again = service.ApplyFightResult(bandits, true, 7);
            Assert.IsFalse(again.FirstWin);
            Assert.AreEqual(0, again.FirstWinGold);
        }

        [Test]
        public void Loss_PaysAQuarterOfTheGoldAndHalfTheXp()
        {
            var rewards = service.ApplyFightResult(Opponent("ember_clan"), false, 7);

            Assert.AreEqual(30, rewards.Gold);
            Assert.AreEqual(0, rewards.Steel);
            Assert.AreEqual(20, rewards.ClanXp);
            Assert.IsNull(rewards.Drop);
            Assert.IsFalse(rewards.FirstWin);
        }

        [Test]
        public void Drops_AreTheSameForTheSameSeed()
        {
            var tide = Opponent("tide_shrine");
            service.Profile.beatenLineups.Add(tide.Id);
            var other = ProfileService.Load(new MemorySaveStorage(), database, 1);
            other.Profile.beatenLineups.Add(tide.Id);

            for (var seed = 1; seed < 20; seed++)
                Assert.AreEqual(service.ApplyFightResult(tide, true, seed).Drop, other.ApplyFightResult(tide, true, seed).Drop, $"seed {seed}");
        }

        [Test]
        public void ClanLevels_PayKarmaAndGrowTheTeam()
        {
            var shogun = Opponent("shogun_guard");
            FightRewards rewards = null;
            var levels = 0;
            while (service.ClanLevel < 3)
            {
                rewards = service.ApplyFightResult(shogun, true, levels);
                levels += rewards.ClanLevelsGained;
            }

            Assert.AreEqual(2, levels);
            Assert.AreEqual(100 + progression.ClanLevelKarma(2) + progression.ClanLevelKarma(3), Karma);
            Assert.AreEqual(4, service.TeamSize);
            Assert.AreEqual(4, rewards.TeamSizeAfter);
        }

        // ---------------------------------------------------------------- lineup

        [Test]
        public void FitLineup_KeepsOnlyOwnedNinjasAndWeaponsWithinTheTeamSize()
        {
            var lineup = new PlayerLineup();
            lineup.SetSlots(new[]
            {
                new LineupSlot(Ninja("kaito"), 2, 3, Weapon("kunai"), Weapon("kunai")),
                new LineupSlot(Ninja("nami"), 3, 0),
                new LineupSlot(Ninja("daichi"), 3, 3, Weapon("iron_plate"), Weapon("jade_charm")),
                new LineupSlot(Ninja("hana"), 4, 0),
                new LineupSlot(Ninja("kaito"), 5, 0),
            });

            Assert.IsTrue(service.FitLineup(lineup));

            Assert.AreEqual(3, lineup.MaxUnits);
            CollectionAssert.AreEqual(new[] { "ninja.kaito", "ninja.daichi", "ninja.hana" }, lineup.Slots.Select(s => s.ninja.Id));
            Assert.AreEqual(1, lineup.EquippedCount(Weapon("kunai")));
            Assert.AreEqual(1, lineup.EquippedCount(Weapon("iron_plate")));
            Assert.AreEqual(0, lineup.EquippedCount(Weapon("jade_charm")));
            Assert.IsFalse(service.FitLineup(lineup), "already fits");
        }

        [Test]
        public void Ranks_ScaleHealthAndAttackDamageInFights()
        {
            var kaito = Ninja("kaito");
            var builder = CombatSetupBuilder.FromDatabase(database);
            builder.Add(0, kaito, builder.TeamPosition(0, 3, 3));
            builder.Add(1, kaito, builder.TeamPosition(1, 3, 3), new NinjaRank(10, 2));
            var units = builder.Build().Units;

            Assert.AreEqual(units[0].Stats[StatIds.Health] * 21760 / 10000, units[1].Stats[StatIds.Health]);
            Assert.AreEqual(units[0].Stats[StatIds.AttackDamage] * 21760 / 10000, units[1].Stats[StatIds.AttackDamage]);
            Assert.AreEqual(units[0].Stats[StatIds.Armor], units[1].Stats[StatIds.Armor], "other stats stay");
        }

        /// <summary>A new save's three ninjas and two weapons should beat the warm-up lineup most of the time.</summary>
        [Test]
        public void StarterTeam_UsuallyBeatsTheFirstOpponent()
        {
            var starter = new BalanceTeam("Starter", new[]
            {
                new LineupSlot(Ninja("daichi"), 3, 3, Weapon("iron_plate")),
                new LineupSlot(Ninja("kaito"), 2, 3, Weapon("kunai")),
                new LineupSlot(Ninja("hana"), 3, 0),
            });
            var bandits = new BalanceTeam("Bandit Patrol", Opponent("bandit_patrol").Slots.ToList());

            var result = new BalanceRunner(database).Run(starter, bandits);

            Assert.That(result.WinRateA, Is.GreaterThanOrEqualTo(0.6), $"the starter team won {result.WinRateA:P0}");
        }

        // ---------------------------------------------------------------- save file

        [Test]
        public void SaveFile_KeepsEverythingAcrossALoad()
        {
            service.TryLevelUp(Ninja("kaito"), out _);
            service.RefreshDojoIfNewDay(Today);
            service.ApplyFightResult(Opponent("bandit_patrol"), true, 3);
            var lineup = new PlayerLineup();
            lineup.Place(Ninja("hana"), 3, 0);
            lineup.Equip(lineup.Slots[0], Weapon("kunai"));
            service.SaveLineup(lineup, Opponent("ember_clan"));

            var loaded = ProfileService.Load(new MemorySaveStorage(storage.Text), database, 999);

            Assert.IsNull(loaded.LoadProblem);
            Assert.AreEqual(2, loaded.RankOf(Ninja("kaito")).Level);
            Assert.AreEqual(Gold, loaded.Balance(progression.Gold));
            Assert.AreEqual(Karma, loaded.Balance(progression.Karma));
            Assert.AreEqual(Steel, loaded.Balance(progression.Steel));
            Assert.AreEqual(service.ClanXp, loaded.ClanXp);
            Assert.IsTrue(loaded.HasBeaten(Opponent("bandit_patrol")));
            CollectionAssert.AreEqual(service.Profile.dojoOffers, loaded.Profile.dojoOffers);
            Assert.AreEqual("lineup.ember_clan", loaded.Profile.opponent);
            Assert.AreEqual(42, loaded.Profile.randomSeed, "the seed comes from the file, not the new one");

            var reloaded = new PlayerLineup();
            reloaded.LoadSaved(loaded.Profile.lineup, database);
            Assert.AreEqual("ninja.hana", reloaded.Slots.Single().ninja.Id);
            Assert.AreEqual("weapon.kunai", reloaded.Slots.Single().weapons.Single().Id);
        }

        [Test]
        public void SaveFile_SkipsUnknownIdsAndKeepsNumbersInRange()
        {
            var json = SaveFile.ToJson(service.Profile)
                .Replace("\"ninja.hana\"", "\"ninja.retired\"")
                .Replace("\"weapon.kunai\"", "\"weapon.retired\"");
            var profile = JsonUtility.FromJson<PlayerProfile>(json);
            profile.ninjas[0].level = 500;
            profile.ninjas[0].stars = -2;
            profile.ninjas.Add(new OwnedNinja { id = profile.ninjas[0].id });

            var loaded = new ProfileService(profile, database, progression);

            CollectionAssert.AreEquivalent(new[] { "ninja.daichi", "ninja.kaito" }, loaded.OwnedNinjas().Select(n => n.Id));
            Assert.AreEqual(0, loaded.WeaponCount(Weapon("kunai")));
            Assert.AreEqual(1, loaded.WeaponCount(Weapon("iron_plate")));
            var first = loaded.Profile.ninjas[0];
            Assert.AreEqual(60, first.level);
            Assert.AreEqual(1, first.stars);
        }

        [Test]
        public void SaveFile_MigratesFilesWithoutAVersion()
        {
            Assert.IsTrue(SaveFile.TryRead("{\"clanLevel\":4,\"ninjas\":[{\"id\":\"ninja.kaito\",\"level\":3,\"stars\":1}]}", out var profile, out _));
            Assert.AreEqual(SaveFile.CurrentVersion, profile.version);
            Assert.AreEqual(4, profile.clanLevel);
            Assert.AreEqual(3, profile.ninjas.Single().level);
        }

        [Test]
        public void SaveFile_RefusesNewerAndBrokenFiles()
        {
            Assert.IsFalse(SaveFile.TryRead("{\"version\":999}", out _, out var newer));
            StringAssert.Contains("newer", newer);
            Assert.IsFalse(SaveFile.TryRead("not json", out _, out _));
            Assert.IsFalse(SaveFile.TryRead("", out _, out _));
        }

        [Test]
        public void UnreadableSave_IsSetAsideAndANewOneStarts()
        {
            var broken = new MemorySaveStorage("{ this is not a save");
            var loaded = ProfileService.Load(broken, database, 5);

            Assert.IsNotNull(loaded.LoadProblem);
            Assert.AreEqual("{ this is not a save", broken.SetAsideText);
            Assert.AreEqual(3, loaded.OwnedNinjas().Count);
            Assert.IsTrue(SaveFile.TryRead(broken.Text, out _, out _), "the new save was written");
        }

        [Test]
        public void EveryChange_IsSaved()
        {
            var writes = storage.Writes;
            service.TryLevelUp(Ninja("kaito"), out _);
            service.RefreshDojoIfNewDay(Today);
            service.ApplyFightResult(Opponent("bandit_patrol"), false, 1);
            Assert.AreEqual(writes + 3, storage.Writes);

            service.TryLevelUp(Ninja("nami"), out _);
            Assert.AreEqual(writes + 3, storage.Writes, "a refused change writes nothing");
        }
    }
}
