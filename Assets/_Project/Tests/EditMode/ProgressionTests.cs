using System;
using System.Linq;
using NinjaRealm.Combat;
using NinjaRealm.Core.Campaign;
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

        /// <summary>A campaign stage by chapter and place, such as "1_1".</summary>
        StageDefinition Stage(string place) => database.Get<StageDefinition>("stage." + place);

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
            Assert.AreEqual(3, progression.TeamSize(1));
            Assert.AreEqual(4, progression.TeamSize(2));
            Assert.AreEqual(4, progression.TeamSize(4));
            Assert.AreEqual(5, progression.TeamSize(5));
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

        // ---------------------------------------------------------------- founding the clan

        HomeVillage Village(string id) => progression.Village(id);

        [Test]
        public void NewSave_NeedsANameAndAVillage()
        {
            Assert.IsTrue(service.NeedsFounding);
            Assert.AreEqual("", service.ClanName);
            Assert.IsNull(service.HomeVillage);
            Assert.AreEqual(3, progression.Villages.Count);
        }

        [Test]
        public void FoundClan_SavesTheNameAndVillage()
        {
            Assert.IsTrue(service.TryFoundClan("  Silent   Moon ", Village("mistwater"), out var message), message);
            StringAssert.Contains("Mistwater", message);
            Assert.IsFalse(service.NeedsFounding);

            var reloaded = ProfileService.Load(storage, database, 7);
            Assert.AreEqual("Silent Moon", reloaded.ClanName);
            Assert.AreEqual("mistwater", reloaded.HomeVillage.id);
            Assert.IsFalse(reloaded.NeedsFounding);
        }

        [Test]
        public void FoundClan_RefusesABadNameOrNoVillage()
        {
            Assert.IsFalse(service.TryFoundClan("Al", Village("emberfall"), out _), "too short");
            Assert.IsFalse(service.TryFoundClan("The Very Long Clan Name", Village("emberfall"), out _), "too long");
            Assert.IsFalse(service.TryFoundClan("Moon<3", Village("emberfall"), out _), "symbols");
            Assert.IsFalse(service.TryFoundClan("1234", Village("emberfall"), out _), "no letter");
            Assert.IsFalse(service.TryFoundClan("Silent Moon", null, out var message), "no village");
            StringAssert.Contains("village", message);
            Assert.IsTrue(service.NeedsFounding);
            Assert.AreEqual(1, storage.Writes, "only the new save itself was written");
        }

        [Test]
        public void ClanName_AllowsLettersDigitsSpacesHyphensAndApostrophes()
        {
            Assert.IsNull(ClanName.Problem("Kaze-no Ren's 2"));
            Assert.IsNull(ClanName.Problem("Hōshi"));
            Assert.AreEqual("Iron Lotus", ClanName.Clean("\tIron \n Lotus  "));
        }

        [Test]
        public void FoundClan_KeepsAnOlderSavesProgress()
        {
            service.Grant(new[] { new ResourceAmount(progression.Gold, 1000) });
            service.Profile.clanLevel = 4;
            Assert.IsTrue(service.TryFoundClan("Iron Lotus", Village("stonehollow"), out _));

            Assert.AreEqual(1500, Gold);
            Assert.AreEqual(4, service.ClanLevel);
            Assert.AreEqual(3, service.OwnedNinjas().Count);
        }

        [Test]
        public void Dojo_FavoursTheHomeVillagesElement()
        {
            var fire = Ninja("kaito").Element;
            var plain = ProfileService.Load(new MemorySaveStorage(), database, 42);
            var ember = ProfileService.Load(new MemorySaveStorage(), database, 42);
            Assert.IsTrue(ember.TryFoundClan("Ash Fang", Village("emberfall"), out _));

            int FireOffers(ProfileService roster)
            {
                var count = 0;
                for (var day = 0; day < 300; day++)
                {
                    roster.RefreshDojoIfNewDay(Today.AddDays(day));
                    count += roster.DojoOffers().Count(n => n != null && n.Element == fire);
                }

                return count;
            }

            var withoutVillage = FireOffers(plain);
            var withVillage = FireOffers(ember);
            Assert.Greater(withVillage, withoutVillage * 1.15f, $"fire offers: {withVillage} from Emberfall, {withoutVillage} with no village");
        }

        [Test]
        public void Dojo_WithNoVillageRollsAsBefore()
        {
            // The same seed gives the same offers as before villages existed.
            service.RefreshDojoIfNewDay(Today);
            var other = ProfileService.Load(new MemorySaveStorage(), database, 42);
            other.Profile.village = "nowhere";
            other.RefreshDojoIfNewDay(Today);
            CollectionAssert.AreEqual(service.Profile.dojoOffers, other.Profile.dojoOffers);
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
            var bandits = Stage("1_1");
            var rewards = service.ApplyStageResult(bandits, 3, 7);

            Assert.IsTrue(rewards.FirstWin);
            Assert.AreEqual(90, rewards.Gold);
            Assert.AreEqual(200, rewards.FirstWinGold);
            Assert.AreEqual(5, rewards.Steel);
            Assert.AreEqual(30, rewards.ClanXp);
            Assert.IsNotNull(rewards.Drop);
            Assert.AreEqual(WeaponTier.Component, rewards.Drop.Tier);
            Assert.AreEqual(500 + 290, Gold);
            Assert.AreEqual(5, Steel);
            Assert.AreEqual(100 + 20, Karma, "the first clear of a chapter 1 stage pays 20 Karma");
            Assert.IsTrue(service.HasCleared(bandits));

            var again = service.ApplyStageResult(bandits, 3, 7);
            Assert.IsFalse(again.FirstWin);
            Assert.AreEqual(0, again.FirstWinGold);
            Assert.AreEqual(0, again.FirstClearKarma);
        }

        [Test]
        public void Loss_PaysAQuarterOfTheGoldAndHalfTheXp()
        {
            var tierTwo = Stage("1_6");
            Assert.AreEqual(2, tierTwo.RewardTier);
            var rewards = service.ApplyStageResult(tierTwo, 0, 7);

            Assert.IsFalse(rewards.Won);
            Assert.AreEqual(30, rewards.Gold);
            Assert.AreEqual(0, rewards.Steel);
            Assert.AreEqual(20, rewards.ClanXp);
            Assert.IsNull(rewards.Drop);
            Assert.IsFalse(rewards.FirstWin);
        }

        [Test]
        public void Drops_AreTheSameForTheSameSeed()
        {
            var tide = Stage("3_2");
            service.Profile.stages.Add(new StageProgress { id = tide.Id, stars = 3 });
            var other = ProfileService.Load(new MemorySaveStorage(), database, 1);
            other.Profile.stages.Add(new StageProgress { id = tide.Id, stars = 3 });

            for (var seed = 1; seed < 20; seed++)
                Assert.AreEqual(service.ApplyStageResult(tide, 3, seed).Drop, other.ApplyStageResult(tide, 3, seed).Drop, $"seed {seed}");
        }

        [Test]
        public void ClanLevels_PayKarmaAndGrowTheTeam()
        {
            // Already cleared, so the first clear's own Karma stays out of the count.
            var shogun = Stage("3_8");
            service.Profile.stages.Add(new StageProgress { id = shogun.Id, stars = 3 });
            FightRewards rewards = null;
            var levels = 0;
            while (service.ClanLevel < 2)
            {
                rewards = service.ApplyStageResult(shogun, 3, levels);
                levels += rewards.ClanLevelsGained;
            }

            Assert.AreEqual(1, levels);
            Assert.AreEqual(100 + progression.ClanLevelKarma(2), Karma);
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
            service.ApplyStageResult(Stage("1_1"), 2, 3);
            var lineup = new PlayerLineup();
            lineup.Place(Ninja("hana"), 3, 0);
            lineup.Equip(lineup.Slots[0], Weapon("kunai"));
            service.SaveLineup(lineup);
            Assert.IsTrue(service.SelectStage(Stage("1_2")));

            var loaded = ProfileService.Load(new MemorySaveStorage(storage.Text), database, 999);

            Assert.IsNull(loaded.LoadProblem);
            Assert.AreEqual(2, loaded.RankOf(Ninja("kaito")).Level);
            Assert.AreEqual(Gold, loaded.Balance(progression.Gold));
            Assert.AreEqual(Karma, loaded.Balance(progression.Karma));
            Assert.AreEqual(Steel, loaded.Balance(progression.Steel));
            Assert.AreEqual(service.ClanXp, loaded.ClanXp);
            Assert.AreEqual(2, loaded.StarsOf(Stage("1_1")));
            CollectionAssert.AreEqual(service.Profile.dojoOffers, loaded.Profile.dojoOffers);
            Assert.AreEqual(Stage("1_2"), loaded.CurrentStage());
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
            service.ApplyStageResult(Stage("1_1"), 0, 1);
            Assert.AreEqual(writes + 3, storage.Writes);

            service.TryLevelUp(Ninja("nami"), out _);
            Assert.AreEqual(writes + 3, storage.Writes, "a refused change writes nothing");
        }

        // ---------------------------------------------------------------- income

        static readonly DateTime Noon = new DateTime(2026, 11, 15, 12, 0, 0, DateTimeKind.Utc);

        [Test]
        public void Income_StartsWithOneHourReady()
        {
            Assert.AreEqual(0, service.PendingIncome(IncomeIds.Mint, Noon), "nothing before the village starts it");

            Assert.IsTrue(service.StartIncome(Noon));

            Assert.AreEqual(40, service.PendingIncome(IncomeIds.Mint, Noon));
            Assert.AreEqual(4, service.PendingIncome(IncomeIds.SteelMine, Noon));
            Assert.IsFalse(service.StartIncome(Noon.AddHours(1)), "starting again changes nothing");
        }

        [Test]
        public void Income_FillsOverTimeUpToEightHours()
        {
            service.StartIncome(Noon);

            Assert.AreEqual(60, service.PendingIncome(IncomeIds.Mint, Noon.AddMinutes(30)));
            Assert.AreEqual(320, service.PendingIncome(IncomeIds.Mint, Noon.AddHours(7)));
            Assert.AreEqual(320, service.PendingIncome(IncomeIds.Mint, Noon.AddDays(3)), "full after eight hours");
            Assert.AreEqual(320, service.IncomeCapacity(IncomeIds.Mint));
            Assert.AreEqual(TimeSpan.FromHours(7), service.IncomeTimeToFull(IncomeIds.Mint, Noon));
            Assert.AreEqual(TimeSpan.Zero, service.IncomeTimeToFull(IncomeIds.Mint, Noon.AddDays(3)));
        }

        [Test]
        public void Income_CollectingPaysTheWalletAndStartsAgain()
        {
            service.StartIncome(Noon);
            var gold = Gold;
            var later = Noon.AddMinutes(30);

            Assert.IsTrue(service.TryCollectIncome(IncomeIds.Mint, later, out var message));

            StringAssert.Contains("60", message);
            Assert.AreEqual(gold + 60, Gold);
            Assert.AreEqual(0, service.PendingIncome(IncomeIds.Mint, later));
            Assert.IsFalse(service.TryCollectIncome(IncomeIds.Mint, later, out _), "nothing to collect twice");
            Assert.AreEqual(gold + 60, Gold);
            Assert.AreEqual(40, service.PendingIncome(IncomeIds.Mint, later.AddHours(1)));
        }

        [Test]
        public void Income_KeepsThePartThatHasNotMadeAWholeUnit()
        {
            service.StartIncome(Noon);

            // Steel comes every 15 minutes. Collecting after 70 minutes pays 4 and keeps 10 minutes.
            service.TryCollectIncome(IncomeIds.SteelMine, Noon.AddMinutes(10), out _);

            Assert.AreEqual(4, Steel);
            Assert.AreEqual(0, service.PendingIncome(IncomeIds.SteelMine, Noon.AddMinutes(14)));
            Assert.AreEqual(1, service.PendingIncome(IncomeIds.SteelMine, Noon.AddMinutes(15)));
        }

        [Test]
        public void Income_GrowsWithTheClanLevel()
        {
            service.StartIncome(Noon);
            service.Profile.clanLevel = 3;

            Assert.AreEqual(50, service.IncomePerHour(IncomeIds.Mint));
            Assert.AreEqual(6, service.IncomePerHour(IncomeIds.SteelMine));
            Assert.AreEqual(400, service.IncomeCapacity(IncomeIds.Mint));
        }

        [Test]
        public void Income_SurvivesAClockSetBackAndALoad()
        {
            service.StartIncome(Noon);
            var yesterday = Noon.AddDays(-1);

            Assert.AreEqual(0, service.PendingIncome(IncomeIds.Mint, yesterday), "a clock set back pays nothing");
            Assert.IsTrue(service.StartIncome(yesterday), "and the timer counts from the new time");
            Assert.AreEqual(40, service.PendingIncome(IncomeIds.Mint, yesterday.AddHours(1)));

            var loaded = ProfileService.Load(storage, database, 1);
            Assert.AreEqual(40, loaded.PendingIncome(IncomeIds.Mint, yesterday.AddHours(1)));
        }

        [Test]
        public void Income_OfAnUnknownSourceIsRefused()
        {
            Assert.IsFalse(service.TryCollectIncome("income.nowhere", Noon, out var message));
            Assert.IsNotEmpty(message);
            Assert.AreEqual(0, service.IncomePerHour("income.nowhere"));
        }
    }
}
