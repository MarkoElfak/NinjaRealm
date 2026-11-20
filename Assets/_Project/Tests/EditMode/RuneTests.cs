using System;
using System.Linq;
using NinjaRealm.Combat;
using NinjaRealm.Core.Content;
using NinjaRealm.Core.Definitions;
using NinjaRealm.Core.Definitions.Effects;
using NinjaRealm.Core.Fights;
using NinjaRealm.Core.Lineups;
using NinjaRealm.Core.Progression;
using NinjaRealm.Core.Prototype;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace NinjaRealm.Tests
{
    /// <summary>The Shrine's runes on the prototype content: buying, ranks, the altar's slots and rules, fights, income and the save.</summary>
    public class RuneTests
    {
        static readonly DateTime Today = new DateTime(2026, 11, 20);

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

        RuneDefinition Rune(string name) => database.Get<RuneDefinition>("rune." + name);

        NinjaDefinition Ninja(string name) => database.Get<NinjaDefinition>("ninja." + name);

        int Gold => service.Balance(progression.Gold);

        int Karma => service.Balance(progression.Karma);

        void GiveGold(int amount) => service.Grant(new[] { new ResourceAmount(progression.Gold, amount) });

        void GiveKarma(int amount) => service.Grant(new[] { new ResourceAmount(progression.Karma, amount) });

        /// <summary>Puts a rune in today's offer, so a rarity that isn't always for sale can be bought.</summary>
        void Offer(params string[] names)
        {
            service.Profile.shrineOffers = names.Select(name => "rune." + name).ToList();
        }

        /// <summary>Owns a rune at a rank without paying, for rule tests.</summary>
        void Own(string name, int rank = 1)
        {
            service.Profile.runes.Add(new OwnedRune { id = "rune." + name, rank = rank });
        }

        // ---------------------------------------------------------------- content

        [Test]
        public void Prototype_HasTwentyTwoRunesWithFiveRanks()
        {
            var runes = database.GetAll<RuneDefinition>().ToList();

            Assert.AreEqual(22, runes.Count);
            foreach (var rune in runes)
            {
                Assert.AreEqual(5, rune.MaxRank, rune.Id);
                Assert.IsNotEmpty(rune.Describe(1), rune.Id);
                Assert.IsEmpty(rune.ValidateSelf().ToList(), rune.Id);
            }

            Assert.AreEqual(3, runes.Count(r => r.Rarity == Rarity.Legendary));
            Assert.AreEqual(3, runes.Count(r => r.Family == RuneFamily.Seal));
        }

        [Test]
        public void RankValues_RunEvenlyFromRankOneToFive()
        {
            var ember = Rune("ember_fang");

            Assert.AreEqual("+3% Attack Damage", ember.Describe(1));
            Assert.AreEqual("+8% Attack Damage", ember.Describe(5));
            Assert.AreEqual("Fire ninjas: +5% Attack Damage", Rune("kindling_seal").Describe(1));
            Assert.AreEqual("+4% Gold from fights", Rune("coin_lantern").Describe(1));
            Assert.AreEqual(1200, Rune("coin_lantern").Rank(5).fightGold);
        }

        [Test]
        public void IconName_IsTheIdWithoutItsCategory()
        {
            Assert.AreEqual("ember_fang", Rune("ember_fang").IconName);
        }

        // ---------------------------------------------------------------- altar slots

        [Test]
        public void NewSave_HasOneLitSlotOfSixAndNoRunes()
        {
            Assert.AreEqual(6, service.RuneSlotCount);
            Assert.AreEqual(1, service.RuneSlotsLit);
            Assert.IsEmpty(service.OwnedRunes());
            Assert.IsEmpty(service.SetRunes());
        }

        [Test]
        public void LightingASlot_NeedsItsClanLevelAndKarma()
        {
            GiveKarma(1000);
            Assert.IsFalse(service.TryLightRuneSlot(out var early));
            StringAssert.Contains("clan level 2", early);

            service.Profile.clanLevel = 2;
            var karma = Karma;
            Assert.IsTrue(service.TryLightRuneSlot(out _));
            Assert.AreEqual(2, service.RuneSlotsLit);
            Assert.AreEqual(karma - 40, Karma);

            Assert.IsFalse(service.TryLightRuneSlot(out var next), "slot 3 opens at clan level 4");
            StringAssert.Contains("clan level 4", next);
        }

        // ---------------------------------------------------------------- buying, ranks, selling

        [Test]
        public void BuyingACommonRune_TakesGoldAndSetsItInTheEmptySlot()
        {
            var gold = Gold;
            Assert.IsTrue(service.TryBuyRune(Rune("ember_fang"), out var message), message);

            Assert.AreEqual(gold - 300, Gold);
            Assert.AreEqual(1, service.RuneRankOf(Rune("ember_fang")));
            Assert.AreEqual(Rune("ember_fang"), service.RuneInSlot(0));
            Assert.IsFalse(service.TryBuyRune(Rune("ember_fang"), out _), "a rune is owned once");
        }

        [Test]
        public void RareRunes_OnlySellFromTodaysOffer()
        {
            GiveGold(5000);
            Assert.IsFalse(service.TryBuyRune(Rune("hawk_eye"), out var message));
            StringAssert.Contains("isn't in the Shrine today", message);

            Offer("hawk_eye");
            Assert.IsTrue(service.TryBuyRune(Rune("hawk_eye"), out _));
            Assert.AreEqual(string.Empty, service.Profile.shrineOffers[0], "the bought offer is taken");
        }

        [Test]
        public void LegendaryRunes_AlsoCostKarma()
        {
            GiveGold(10000);
            GiveKarma(500);
            Offer("phoenix_ash");
            var karma = Karma;

            Assert.IsTrue(service.TryBuyRune(Rune("phoenix_ash"), out _));
            Assert.AreEqual(karma - 200, Karma);
        }

        [Test]
        public void RankingUp_CostsAShareOfThePriceAndNeedsClanLevel()
        {
            GiveGold(10000);
            var ember = Rune("ember_fang");
            service.TryBuyRune(ember, out _);

            Assert.AreEqual(150, service.RuneRankCost(ember), "rank II is half the price");
            Assert.IsFalse(service.TryRankUpRune(ember, out var early));
            StringAssert.Contains("clan level 3", early);

            service.Profile.clanLevel = 12;
            var gold = Gold;
            for (var rank = 2; rank <= 5; rank++) Assert.IsTrue(service.TryRankUpRune(ember, out _), $"rank {rank}");

            Assert.AreEqual(5, service.RuneRankOf(ember));
            Assert.AreEqual(gold - (150 + 300 + 450 + 600), Gold, "rank V costs six times the price in all");
            Assert.AreEqual(-1, service.RuneRankCost(ember));
            Assert.IsFalse(service.TryRankUpRune(ember, out _));
        }

        [Test]
        public void Selling_ReturnsHalfTheGoldAndEmptiesTheSlot()
        {
            GiveGold(10000);
            service.Profile.clanLevel = 3;
            var oak = Rune("oak_heart");
            service.TryBuyRune(oak, out _);
            service.TryRankUpRune(oak, out _);
            var gold = Gold;

            Assert.IsTrue(service.TrySellRune(oak, out _));
            Assert.AreEqual(gold + 225, Gold, "half of 300 + 150");
            Assert.IsFalse(service.OwnsRune(oak));
            Assert.IsNull(service.RuneInSlot(0));
        }

        [Test]
        public void HomeSeal_CostsAQuarterLess()
        {
            var emberfall = progression.Village("emberfall");
            Assert.IsTrue(service.TryFoundClan("Test Clan", emberfall, out _));

            Assert.AreEqual(600, service.RunePrice(Rune("kindling_seal")));
            Assert.AreEqual(800, service.RunePrice(Rune("tide_seal")));
        }

        // ---------------------------------------------------------------- the altar's rules

        [Test]
        public void TwoRunesRaisingTheSameStat_CantBothBeSet()
        {
            service.Profile.clanLevel = 4;
            service.Profile.runeSlots.Add(string.Empty);
            Own("ember_fang");
            Own("twin_current");
            Own("kindling_seal");

            Assert.IsTrue(service.TrySetRune(0, Rune("ember_fang"), out _));
            Assert.IsFalse(service.TrySetRune(1, Rune("twin_current"), out var message), "both raise attack damage");
            StringAssert.Contains("Ember Fang", message);
            Assert.IsTrue(service.TrySetRune(1, Rune("kindling_seal"), out _), "a Seal only covers Fire ninjas");
        }

        [Test]
        public void OnlyOneLegendary_FitsOnTheAltar()
        {
            service.Profile.runeSlots.Add(string.Empty);
            Own("phoenix_ash");
            Own("thunder_script");

            Assert.IsTrue(service.TrySetRune(0, Rune("phoenix_ash"), out _));
            Assert.IsFalse(service.TrySetRune(1, Rune("thunder_script"), out _));
            Assert.IsTrue(service.TrySetRune(0, Rune("thunder_script"), out _), "it can replace the other one");
        }

        [Test]
        public void SettingARuneThatIsAlreadySet_MovesIt()
        {
            service.Profile.runeSlots.Add(string.Empty);
            Own("iron_bark");

            service.TrySetRune(0, Rune("iron_bark"), out _);
            Assert.IsTrue(service.TrySetRune(1, Rune("iron_bark"), out _));
            Assert.IsNull(service.RuneInSlot(0));
            Assert.AreEqual(Rune("iron_bark"), service.RuneInSlot(1));
        }

        // ---------------------------------------------------------------- fights

        [Test]
        public void Seals_OnlyGiveTheirElementsNinjas()
        {
            Own("kindling_seal", 2);
            service.TrySetRune(0, Rune("kindling_seal"), out _);

            Assert.AreEqual(1, service.RunePassives(Ninja("kaito")).Count, "Kaito is Fire");
            Assert.IsEmpty(service.RunePassives(Ninja("hana")), "Hana is Water");
        }

        [Test]
        public void SetRunes_RaiseTheStatsOfThePlayersNinjasInAFight()
        {
            Own("ember_fang", 5);
            service.TrySetRune(0, Rune("ember_fang"), out _);
            var kaito = Ninja("kaito");

            var builder = CombatSetupBuilder.FromDatabase(database);
            builder.AddLineup(0, new[] { new LineupSlot(kaito, 3, 3) }, service.RankOf, service.RunePassives);
            builder.AddLineup(1, new[] { new LineupSlot(kaito, 3, 3) });
            var setup = builder.Build();

            var modifier = setup.Units[0].Passives.OfType<StatModifierSpec>().Single();
            Assert.AreEqual(StatIds.AttackDamage, modifier.StatId);
            Assert.AreEqual(ModifierOperation.AddPercent, modifier.Operation);
            Assert.AreEqual(Fixed.FromFloat(8f), modifier.Value);
            Assert.IsEmpty(setup.Units[1].Passives, "the enemy gets no runes");
        }

        [Test]
        public void FightsWithRunes_StayDeterministic()
        {
            Own("phoenix_ash", 3);
            Own("hawk_eye", 3);
            service.Profile.runeSlots.Add(string.Empty);
            service.TrySetRune(0, Rune("phoenix_ash"), out _);
            service.TrySetRune(1, Rune("hawk_eye"), out _);

            CombatResult Fight()
            {
                var builder = CombatSetupBuilder.FromDatabase(database);
                builder.AddLineup(0, new[] { new LineupSlot(Ninja("kaito"), 3, 3), new LineupSlot(Ninja("hana"), 3, 0) }, service.RankOf, service.RunePassives);
                builder.AddLineup(1, database.Get<LineupDefinition>("lineup.bandit_patrol").Slots);
                return new CombatSimulation(builder.Build(), 7).Run();
            }

            var first = Fight();
            var second = Fight();
            Assert.AreEqual(first.WinningTeam, second.WinningTeam);
            Assert.AreEqual(first.DurationTicks, second.DurationTicks);
        }

        // ---------------------------------------------------------------- economy

        [Test]
        public void CoinLantern_RaisesFightGold()
        {
            var stage = database.Get<NinjaRealm.Core.Campaign.StageDefinition>("stage.1_1");
            var plain = service.ApplyStageResult(stage, 0, 1).Gold;

            Own("coin_lantern", 5);
            service.TrySetRune(0, Rune("coin_lantern"), out _);
            var lantern = service.ApplyStageResult(stage, 0, 1).Gold;

            Assert.AreEqual(plain * 112 / 100, lantern);
        }

        [Test]
        public void MountainBell_RaisesIncomePerHour()
        {
            var plain = service.IncomePerHour(IncomeIds.Mint);

            Own("mountain_bell", 1);
            service.TrySetRune(0, Rune("mountain_bell"), out _);

            Assert.AreEqual(plain * 110 / 100, service.IncomePerHour(IncomeIds.Mint));
        }

        // ---------------------------------------------------------------- the daily offer

        [Test]
        public void ShrineOffers_AreFourRunesNotOwnedAndRepeatForTheSameSeed()
        {
            Own("ember_fang");
            service.RefreshShrineIfNewDay(Today);
            var offers = service.ShrineOffers();

            Assert.AreEqual(4, offers.Count);
            Assert.AreEqual(4, offers.Distinct().Count());
            CollectionAssert.DoesNotContain(offers, Rune("ember_fang"));
            Assert.IsFalse(service.RefreshShrineIfNewDay(Today), "once a day");

            var again = ProfileService.Load(new MemorySaveStorage(), database, 42);
            again.Profile.runes.Add(new OwnedRune { id = "rune.ember_fang" });
            again.RefreshShrineIfNewDay(Today);
            CollectionAssert.AreEqual(offers, again.ShrineOffers());
        }

        // ---------------------------------------------------------------- the save

        [Test]
        public void Save_KeepsRunesAndTheAltar()
        {
            GiveGold(1000);
            service.TryBuyRune(Rune("still_pond"), out _);

            var loaded = ProfileService.Load(new MemorySaveStorage(storage.Text), database, 1);

            Assert.AreEqual(1, loaded.RuneRankOf(Rune("still_pond")));
            Assert.AreEqual(Rune("still_pond"), loaded.RuneInSlot(0));
        }

        [Test]
        public void VersionTwoSave_LoadsWithTheFreeSlotLit()
        {
            const string json = "{\"version\":2,\"clanLevel\":5,\"ninjas\":[{\"id\":\"ninja.kaito\",\"level\":2,\"stars\":1}]}";
            var old = new MemorySaveStorage(json);
            var loaded = ProfileService.Load(old, database, 1);

            Assert.IsNull(loaded.LoadProblem);
            Assert.AreEqual(1, loaded.RuneSlotsLit);
            Assert.AreEqual("v2", old.CopyLabel);
            Assert.AreEqual(SaveFile.CurrentVersion, loaded.Profile.version);
        }

        [Test]
        public void Save_DropsUnknownRunesAndBrokenAltars()
        {
            var profile = service.Profile;
            profile.runes.Add(new OwnedRune { id = "rune.retired", rank = 2 });
            profile.runes.Add(new OwnedRune { id = "rune.ember_fang", rank = 99 });
            profile.runes.Add(new OwnedRune { id = "rune.twin_current", rank = 1 });
            profile.runeSlots = new[] { "rune.ember_fang", "rune.twin_current", "rune.not_owned", "", "", "", "", "" }.ToList();

            var loaded = new ProfileService(SaveFileCopy(profile), database, progression);

            Assert.AreEqual(2, loaded.OwnedRunes().Count);
            Assert.AreEqual(5, loaded.RuneRankOf(Rune("ember_fang")));
            Assert.AreEqual(6, loaded.RuneSlotsLit, "cut to the altar's six slots");
            Assert.AreEqual(Rune("ember_fang"), loaded.RuneInSlot(0));
            Assert.IsNull(loaded.RuneInSlot(1), "it raises the same stat as Ember Fang");
            Assert.IsNull(loaded.RuneInSlot(2));
        }

        static PlayerProfile SaveFileCopy(PlayerProfile profile)
        {
            Assert.IsTrue(SaveFile.TryRead(SaveFile.ToJson(profile), out var copy, out _));
            return copy;
        }
    }
}
