using System;
using System.Collections.Generic;
using System.Globalization;
using NinjaRealm.Combat;
using NinjaRealm.Core.Campaign;
using NinjaRealm.Core.Content;
using NinjaRealm.Core.Definitions;
using NinjaRealm.Core.Definitions.Effects;
using NinjaRealm.Core.Lineups;

namespace NinjaRealm.Core.Progression
{
    /// <summary>What a fight paid, for the result screen.</summary>
    public sealed class FightRewards
    {
        public bool Won;

        /// <summary>Stars this fight earned: 0 for a loss, up to <see cref="StageStars.Max"/>.</summary>
        public int Stars;

        /// <summary>The most stars ever won on the stage, counting this fight.</summary>
        public int BestStars;

        /// <summary>Stars this fight added to the stage's best.</summary>
        public int NewStars;

        public int Gold;
        public int Steel;
        public int ClanXp;

        /// <summary>The stage was cleared for the first time.</summary>
        public bool FirstWin;

        public int FirstWinGold;
        public int FirstClearKarma;
        public int FirstClearSteel;

        /// <summary>What the first clear's ninja did: joined, gained a shard or rose a star. Null when there was none.</summary>
        public string NinjaMessage;

        public WeaponDefinition Drop;
        public int ClanLevelsGained;
        public int Karma;
        public int TeamSizeBefore;
        public int TeamSizeAfter;
    }

    /// <summary>A rune set on the altar and the rank it fights at.</summary>
    public readonly struct SetRune
    {
        public readonly RuneDefinition Rune;
        public readonly int Rank;

        public SetRune(RuneDefinition rune, int rank)
        {
            Rune = rune;
            Rank = rank;
        }
    }

    /// <summary>
    /// The player's progress: owning, levelling and starring ninjas, the Dojo, the weapon
    /// inventory and forging, the Shrine's runes, fight rewards and clan levels. Every change checks its cost first,
    /// changes the <see cref="PlayerProfile"/>, then saves. Methods that can be refused return
    /// false with a message saying why; on success the message says what happened.
    /// </summary>
    public sealed class ProfileService
    {
        const string DateFormat = "yyyy-MM-dd";

        readonly ISaveStorage storage;

        public ProfileService(PlayerProfile profile, ContentDatabase content, ProgressionDefinition progression, ISaveStorage storage = null)
        {
            Profile = profile ?? throw new ArgumentNullException(nameof(profile));
            Content = content ?? throw new ArgumentNullException(nameof(content));
            Progression = progression ?? throw new ArgumentNullException(nameof(progression));
            this.storage = storage;
            Campaign = new CampaignMap(content);
            Clean();
        }

        public PlayerProfile Profile { get; }

        public ContentDatabase Content { get; }

        public ProgressionDefinition Progression { get; }

        /// <summary>The campaign's chapters and stages in play order.</summary>
        public CampaignMap Campaign { get; }

        /// <summary>Set when the save couldn't be read and a new one was started. The old file is kept beside it.</summary>
        public string LoadProblem { get; private set; }

        /// <summary>Raised after every change, once it is saved.</summary>
        public event Action Changed;

        /// <summary>The progression settings in a database: the first <see cref="ProgressionDefinition"/>, or null.</summary>
        public static ProgressionDefinition FindProgression(ContentDatabase content)
        {
            if (content == null) return null;
            foreach (var progression in content.GetAll<ProgressionDefinition>()) return progression;
            return null;
        }

        /// <summary>
        /// Loads the save from storage, or starts a new one when there is none. An unreadable save
        /// is set aside and a new one starts. Returns null when the content has no progression settings.
        /// </summary>
        public static ProfileService Load(ISaveStorage storage, ContentDatabase content, long newSeed)
        {
            var progression = FindProgression(content);
            if (progression == null || storage == null) return null;

            string problem = null;
            var fileVersion = SaveFile.CurrentVersion;
            var text = storage.Read();
            if (text == null || !SaveFile.TryRead(text, out var profile, out problem, out fileVersion))
            {
                if (text != null) storage.SetAside();
                profile = NewProfile(progression, newSeed);
            }

            // A save from an older version is rewritten in the new format; the old file stays beside it.
            var older = text != null && problem == null && fileVersion < SaveFile.CurrentVersion;
            if (older) storage.KeepCopy($"v{fileVersion}");

            var service = new ProfileService(profile, content, progression, storage) { LoadProblem = problem };
            if (text == null || problem != null || older) service.Save();
            return service;
        }

        /// <summary>A new save: the starting ninjas, weapons and resources.</summary>
        public static PlayerProfile NewProfile(ProgressionDefinition progression, long seed)
        {
            var profile = new PlayerProfile { version = SaveFile.CurrentVersion, randomSeed = seed };
            foreach (var amount in progression.StartingResources)
                if (amount?.resource != null) profile.wallet.Add(amount.resource, amount.amount);
            foreach (var ninja in progression.StartingNinjas)
                if (ninja != null && profile.ninjas.Find(n => n.id == ninja.Id) == null)
                    profile.ninjas.Add(new OwnedNinja { id = ninja.Id });
            foreach (var weapon in progression.StartingWeapons)
                if (weapon != null) AddWeapon(profile, weapon.Id, 1);
            return profile;
        }

        /// <summary>Writes the profile to storage and raises <see cref="Changed"/>.</summary>
        public void Save()
        {
            storage?.Write(SaveFile.ToJson(Profile));
            Changed?.Invoke();
        }

        // ---------------------------------------------------------------- clan

        /// <summary>The clan's name, or empty before the new game screen.</summary>
        public string ClanName => Profile.clanName ?? string.Empty;

        /// <summary>The clan's home village, or null before one is picked.</summary>
        public HomeVillage HomeVillage => Progression.Village(Profile.village);

        /// <summary>
        /// True until the clan has a name and, when the content has villages, a home village. A
        /// save from before the new game screen also needs one, and keeps all its progress.
        /// </summary>
        public bool NeedsFounding =>
            Core.Progression.ClanName.Problem(Profile.clanName) != null || (Progression.Villages.Count > 0 && HomeVillage == null);

        /// <summary>
        /// Names the clan and settles it in its home village. The next Dojo visit rolls fresh
        /// offers, so the village's ninjas show up from the first day.
        /// </summary>
        public bool TryFoundClan(string name, HomeVillage village, out string message)
        {
            message = Core.Progression.ClanName.Problem(name);
            if (message != null) return false;
            if (Progression.Villages.Count > 0 && (village == null || Progression.Village(village.id) != village))
            {
                message = "Pick a home village.";
                return false;
            }

            Profile.clanName = Core.Progression.ClanName.Clean(name);
            Profile.village = village?.id ?? string.Empty;
            Profile.dojoDate = string.Empty;
            message = village != null ? $"The {Profile.clanName} clan settles in {village.displayName}." : $"The {Profile.clanName} clan is founded.";
            Save();
            return true;
        }

        // ---------------------------------------------------------------- wallet

        public int Balance(ResourceDefinition resource) => Profile.wallet.Get(resource);

        /// <summary>Adds resources, for rewards and testing.</summary>
        public void Grant(IEnumerable<ResourceAmount> amounts)
        {
            foreach (var amount in amounts)
                if (amount?.resource != null && amount.amount > 0) Profile.wallet.Add(amount.resource, amount.amount);
            Save();
        }

        // ---------------------------------------------------------------- ninjas

        /// <summary>Owned ninjas whose content exists, sorted by rarity then ID.</summary>
        public List<NinjaDefinition> OwnedNinjas()
        {
            var owned = new List<NinjaDefinition>();
            foreach (var entry in Profile.ninjas)
                if (Content.TryGet<NinjaDefinition>(entry.id, out var ninja)) owned.Add(ninja);
            owned.Sort((a, b) => a.Rarity != b.Rarity ? a.Rarity.CompareTo(b.Rarity) : string.CompareOrdinal(a.Id, b.Id));
            return owned;
        }

        public OwnedNinja Find(NinjaDefinition ninja)
        {
            if (ninja == null) return null;
            foreach (var entry in Profile.ninjas)
                if (entry.id == ninja.Id) return entry;
            return null;
        }

        public bool Owns(NinjaDefinition ninja) => Find(ninja) != null;

        /// <summary>A ninja's level and stars. Ninjas the player doesn't own are at the base rank.</summary>
        public NinjaRank RankOf(NinjaDefinition ninja) => Find(ninja)?.Rank ?? NinjaRank.Base;

        public bool RosterFull => Profile.ninjas.Count >= Progression.RosterLimit;

        /// <summary>Karma for this ninja's next level, or -1 when it isn't owned or is at the cap.</summary>
        public int LevelUpCost(NinjaDefinition ninja)
        {
            var owned = Find(ninja);
            return owned == null ? -1 : Progression.LevelCost(owned.level);
        }

        public bool TryLevelUp(NinjaDefinition ninja, out string message)
        {
            var owned = Find(ninja);
            if (owned == null)
            {
                message = $"You don't have {Name(ninja)}.";
                return false;
            }

            var cost = Progression.LevelCost(owned.level);
            if (cost < 0)
            {
                message = $"{ninja.DisplayName} is at the level cap ({Progression.MaxLevel}).";
                return false;
            }

            if (!Profile.wallet.TrySpend(Progression.Karma, cost))
            {
                message = $"Level {owned.level + 1} costs {cost} {Name(Progression.Karma)}. You have {Balance(Progression.Karma)}.";
                return false;
            }

            var belt = Progression.Belt(owned.level);
            owned.level++;
            var newBelt = Progression.Belt(owned.level);
            message = newBelt != belt
                ? $"{ninja.DisplayName} reached level {owned.level} and earned the {newBelt} belt."
                : $"{ninja.DisplayName} reached level {owned.level}.";
            Save();
            return true;
        }

        /// <summary>Why the player can't get this ninja right now, or null if they can.</summary>
        public string CannotGain(NinjaDefinition ninja)
        {
            if (ninja == null) return "There's no ninja there.";
            var owned = Find(ninja);
            if (owned == null)
                return RosterFull ? $"Your roster is full ({Progression.RosterLimit} ninjas)." : null;
            return Progression.ShardsForNextStar(owned.stars) < 0 ? $"{ninja.DisplayName} already has the most stars." : null;
        }

        /// <summary>Adds a ninja, or a shard of it when it's already owned. Shards raise its stars.</summary>
        string Gain(NinjaDefinition ninja)
        {
            var owned = Find(ninja);
            if (owned == null)
            {
                Profile.ninjas.Add(new OwnedNinja { id = ninja.Id });
                return $"{ninja.DisplayName} joined your clan.";
            }

            owned.shards++;
            var needed = Progression.ShardsForNextStar(owned.stars);
            if (needed > 0 && owned.shards >= needed)
            {
                owned.shards -= needed;
                owned.stars++;
                return $"{ninja.DisplayName} rose to {owned.stars} stars.";
            }

            return $"{ninja.DisplayName} gained a shard ({owned.shards} / {needed} to {owned.stars + 1} stars).";
        }

        // ---------------------------------------------------------------- dojo

        /// <summary>The Dojo's offers. Null entries were recruited already.</summary>
        public List<NinjaDefinition> DojoOffers()
        {
            var offers = new List<NinjaDefinition>();
            foreach (var id in Profile.dojoOffers)
                offers.Add(!string.IsNullOrEmpty(id) && Content.TryGet<NinjaDefinition>(id, out var ninja) ? ninja : null);
            return offers;
        }

        /// <summary>Rolls new Dojo offers for free when the day has changed since the last roll.</summary>
        public bool RefreshDojoIfNewDay(DateTime today)
        {
            var date = today.ToString(DateFormat, CultureInfo.InvariantCulture);
            if (Profile.dojoDate == date && Profile.dojoOffers.Count == Progression.DojoOfferCount) return false;
            RollDojo();
            Profile.dojoDate = date;
            Save();
            return true;
        }

        public bool TryRefreshDojo(out string message)
        {
            var cost = Progression.DojoRefreshCost;
            if (!Profile.wallet.TrySpend(Progression.Gold, cost))
            {
                message = $"A refresh costs {cost} {Name(Progression.Gold)}.";
                return false;
            }

            RollDojo();
            message = "The Dojo has new recruits.";
            Save();
            return true;
        }

        public bool TryRecruitOffer(int index, out string message)
        {
            var offers = DojoOffers();
            var ninja = index >= 0 && index < offers.Count ? offers[index] : null;
            if (ninja == null)
            {
                message = "That recruit has already joined you.";
                return false;
            }

            if (!TryPayForNinja(ninja, Progression.RecruitPrice(ninja.Rarity), out message)) return false;
            Profile.dojoOffers[index] = string.Empty;
            message = Gain(ninja);
            Save();
            return true;
        }

        /// <summary>Ninjas that can be bought directly, sorted by rarity then ID.</summary>
        public List<NinjaDefinition> DirectBuyNinjas()
        {
            var ninjas = new List<NinjaDefinition>();
            foreach (var ninja in Content.GetAll<NinjaDefinition>())
                if (Progression.DirectBuyPrice(ninja.Rarity) > 0) ninjas.Add(ninja);
            ninjas.Sort((a, b) => a.Rarity != b.Rarity ? a.Rarity.CompareTo(b.Rarity) : string.CompareOrdinal(a.Id, b.Id));
            return ninjas;
        }

        public bool TryBuyDirect(NinjaDefinition ninja, out string message)
        {
            var price = ninja != null ? Progression.DirectBuyPrice(ninja.Rarity) : -1;
            if (price < 0)
            {
                message = ninja != null ? $"{ninja.Rarity} ninjas can only be recruited in the Dojo." : "There's no ninja there.";
                return false;
            }

            if (!TryPayForNinja(ninja, price, out message)) return false;
            message = Gain(ninja);
            Save();
            return true;
        }

        bool TryPayForNinja(NinjaDefinition ninja, int price, out string message)
        {
            message = CannotGain(ninja);
            if (message != null) return false;
            if (price < 0)
            {
                message = $"{ninja.Rarity} ninjas have no price.";
                return false;
            }

            if (!Profile.wallet.TrySpend(Progression.Gold, price))
            {
                message = $"{ninja.DisplayName} costs {price} {Name(Progression.Gold)}. You have {Balance(Progression.Gold)}.";
                return false;
            }

            return true;
        }

        /// <summary>
        /// Picks the offers: a rarity by weight, then a ninja of that rarity. A ninja is offered at
        /// most once per roll, and rarities with no ninjas left are skipped.
        /// </summary>
        void RollDojo()
        {
            var byRarity = new Dictionary<Rarity, List<NinjaDefinition>>();
            foreach (var ninja in Content.GetAll<NinjaDefinition>())
            {
                if (!byRarity.TryGetValue(ninja.Rarity, out var list)) byRarity[ninja.Rarity] = list = new List<NinjaDefinition>();
                list.Add(ninja);
            }

            var random = NextRandom();
            Profile.dojoOffers.Clear();
            for (var i = 0; i < Progression.DojoOfferCount; i++)
            {
                var choices = new List<ProgressionDefinition.RarityOffer>();
                var totalWeight = 0;
                foreach (var offer in Progression.Rarities)
                {
                    if (offer == null || offer.weight <= 0 || !byRarity.TryGetValue(offer.rarity, out var left) || left.Count == 0) continue;
                    choices.Add(offer);
                    totalWeight += offer.weight;
                }

                if (totalWeight <= 0)
                {
                    Profile.dojoOffers.Add(string.Empty);
                    continue;
                }

                var roll = random.Range(0, totalWeight);
                var chosen = choices[choices.Count - 1];
                foreach (var choice in choices)
                {
                    if (roll < choice.weight)
                    {
                        chosen = choice;
                        break;
                    }

                    roll -= choice.weight;
                }

                var pool = byRarity[chosen.rarity];
                var pick = PickFromPool(pool, random);
                Profile.dojoOffers.Add(pool[pick].Id);
                pool.RemoveAt(pick);
            }
        }

        /// <summary>
        /// A ninja from one rarity's pool. Ninjas of the home village's element weigh its Dojo
        /// affinity against the others' 10000; without a home village every ninja is as likely.
        /// </summary>
        int PickFromPool(List<NinjaDefinition> pool, DeterministicRandom random)
        {
            var home = HomeVillage;
            var anyHome = false;
            if (home != null && home.dojoAffinity != ProgressionDefinition.BasisPoints)
                foreach (var ninja in pool)
                    anyHome |= home.IsHome(ninja);
            if (!anyHome) return random.Range(0, pool.Count);

            long total = 0;
            foreach (var ninja in pool) total += Weight(ninja);
            var roll = (long)random.Range(0, (int)Math.Min(int.MaxValue, total));
            for (var i = 0; i < pool.Count; i++)
            {
                var weight = Weight(pool[i]);
                if (roll < weight) return i;
                roll -= weight;
            }

            return pool.Count - 1;

            int Weight(NinjaDefinition ninja) => home.IsHome(ninja) ? Math.Max(1, home.dojoAffinity) : ProgressionDefinition.BasisPoints;
        }

        DeterministicRandom NextRandom()
        {
            unchecked
            {
                var seed = (ulong)Profile.randomSeed ^ ((ulong)(++Profile.randomRolls) * 0x9E3779B97F4A7C15UL);
                return new DeterministicRandom(seed);
            }
        }

        // ---------------------------------------------------------------- weapons

        public int WeaponCount(WeaponDefinition weapon) => weapon != null ? WeaponCount(Profile, weapon.Id) : 0;

        /// <summary>Owned weapons whose content exists, components first, then by ID.</summary>
        public List<WeaponDefinition> OwnedWeapons()
        {
            var owned = new List<WeaponDefinition>();
            foreach (var entry in Profile.weapons)
                if (entry.count > 0 && Content.TryGet<WeaponDefinition>(entry.id, out var weapon)) owned.Add(weapon);
            owned.Sort((a, b) => a.Tier != b.Tier ? a.Tier.CompareTo(b.Tier) : string.CompareOrdinal(a.Id, b.Id));
            return owned;
        }

        /// <summary>Every combined weapon, by ID, for the forge.</summary>
        public List<WeaponDefinition> Recipes()
        {
            var recipes = new List<WeaponDefinition>();
            foreach (var weapon in Content.GetAll<WeaponDefinition>())
                if (weapon.Tier == WeaponTier.Combined && weapon.ComponentA != null && weapon.ComponentB != null) recipes.Add(weapon);
            return recipes;
        }

        /// <summary>Why this weapon can't be forged right now, or null if it can.</summary>
        public string CannotForge(WeaponDefinition combined)
        {
            if (combined == null || combined.Tier != WeaponTier.Combined || combined.ComponentA == null || combined.ComponentB == null)
                return "Only combined weapons are forged.";

            var a = combined.ComponentA;
            var b = combined.ComponentB;
            var need = a == b ? 2 : 1;
            if (WeaponCount(a) < need || WeaponCount(b) < need)
                return a == b ? $"{combined.DisplayName} needs two {a.DisplayName}." : $"{combined.DisplayName} needs a {a.DisplayName} and a {b.DisplayName}.";

            if (!Profile.wallet.CanAfford(Progression.Steel, Progression.ForgeCost))
                return $"Forging costs {Progression.ForgeCost} {Name(Progression.Steel)}. You have {Balance(Progression.Steel)}.";
            return null;
        }

        /// <summary>Uses up both components and the Steel, and adds the combined weapon to the inventory.</summary>
        public bool TryForge(WeaponDefinition combined, out string message)
        {
            message = CannotForge(combined);
            if (message != null) return false;

            Profile.wallet.TrySpend(Progression.Steel, Progression.ForgeCost);
            AddWeapon(Profile, combined.ComponentA.Id, -1);
            AddWeapon(Profile, combined.ComponentB.Id, -1);
            AddWeapon(Profile, combined.Id, 1);
            message = $"You forged a {combined.DisplayName}.";
            Save();
            return true;
        }

        static int WeaponCount(PlayerProfile profile, string id)
        {
            foreach (var entry in profile.weapons)
                if (entry.id == id) return entry.count;
            return 0;
        }

        static void AddWeapon(PlayerProfile profile, string id, int amount)
        {
            foreach (var entry in profile.weapons)
            {
                if (entry.id != id) continue;
                entry.count = Math.Max(0, entry.count + amount);
                if (entry.count == 0) profile.weapons.Remove(entry);
                return;
            }

            if (amount > 0) profile.weapons.Add(new OwnedWeapon { id = id, count = amount });
        }

        // ---------------------------------------------------------------- runes (the Shrine)

        /// <summary>Every rune in the content, by rarity then ID.</summary>
        public List<RuneDefinition> AllRunes()
        {
            var runes = new List<RuneDefinition>(Content.GetAll<RuneDefinition>());
            runes.Sort((a, b) => a.Rarity != b.Rarity ? a.Rarity.CompareTo(b.Rarity) : string.CompareOrdinal(a.Id, b.Id));
            return runes;
        }

        public OwnedRune FindRune(RuneDefinition rune)
        {
            if (rune == null) return null;
            foreach (var entry in Profile.runes)
                if (entry.id == rune.Id) return entry;
            return null;
        }

        public bool OwnsRune(RuneDefinition rune) => FindRune(rune) != null;

        /// <summary>The rune's rank, or 0 when it isn't owned.</summary>
        public int RuneRankOf(RuneDefinition rune) => FindRune(rune)?.rank ?? 0;

        /// <summary>Owned runes whose content exists, by rarity then ID.</summary>
        public List<RuneDefinition> OwnedRunes()
        {
            var owned = new List<RuneDefinition>();
            foreach (var rune in AllRunes())
                if (OwnsRune(rune)) owned.Add(rune);
            return owned;
        }

        /// <summary>The highest rank a rune can reach: its own ranks, cut to the price list.</summary>
        public int RuneMaxRank(RuneDefinition rune) => rune == null ? 0 : Math.Min(rune.MaxRank, Progression.MaxRuneRank);

        /// <summary>True when the rune is the Seal of the clan's home village element, which is cheaper.</summary>
        public bool IsHomeSeal(RuneDefinition rune) =>
            rune != null && rune.Element != null && HomeVillage != null && HomeVillage.element == rune.Element;

        /// <summary>Gold to buy a rune at rank I, or -1 when its rarity has no price.</summary>
        public int RunePrice(RuneDefinition rune)
        {
            var offer = rune != null ? Progression.RuneOfferFor(rune.Rarity) : null;
            return offer == null ? -1 : Discounted(rune, offer.price);
        }

        /// <summary>Karma to buy a rune, on top of its Gold.</summary>
        public int RuneKarmaPrice(RuneDefinition rune) => rune != null ? Progression.RuneOfferFor(rune.Rarity)?.karma ?? 0 : 0;

        /// <summary>Gold to raise an owned rune to its next rank, or -1 when it isn't owned or is at its top rank.</summary>
        public int RuneRankCost(RuneDefinition rune)
        {
            var owned = FindRune(rune);
            var offer = rune != null ? Progression.RuneOfferFor(rune.Rarity) : null;
            if (owned == null || offer == null || owned.rank >= RuneMaxRank(rune)) return -1;
            var share = Progression.RuneRankCostBasisPoints(owned.rank + 1);
            return share < 0 ? -1 : Discounted(rune, (int)((long)offer.price * share / ProgressionDefinition.BasisPoints));
        }

        int Discounted(RuneDefinition rune, int gold) =>
            IsHomeSeal(rune) ? (int)((long)gold * Progression.HomeSealPrice / ProgressionDefinition.BasisPoints) : gold;

        /// <summary>Why the player can't buy this rune right now, or null if they can.</summary>
        public string CannotBuyRune(RuneDefinition rune)
        {
            if (rune == null) return "There's no rune there.";
            if (OwnsRune(rune)) return $"You already have {rune.DisplayName}. Rank it up instead.";
            var price = RunePrice(rune);
            if (price < 0) return $"{rune.Rarity} runes aren't for sale.";
            if (!Profile.wallet.CanAfford(Progression.Gold, price))
                return $"{rune.DisplayName} costs {price} {Name(Progression.Gold)}. You have {Balance(Progression.Gold)}.";
            var karma = RuneKarmaPrice(rune);
            if (!Profile.wallet.CanAfford(Progression.Karma, karma))
                return $"{rune.DisplayName} also costs {karma} {Name(Progression.Karma)}. You have {Balance(Progression.Karma)}.";
            return null;
        }

        /// <summary>True when a rune can be bought straight from the Shrine's list, not only from the daily offer.</summary>
        public bool AlwaysForSale(RuneDefinition rune) => rune != null && (Progression.RuneOfferFor(rune.Rarity)?.alwaysForSale ?? false);

        /// <summary>The runes always for sale, by rarity then ID.</summary>
        public List<RuneDefinition> RunesForSale()
        {
            var runes = new List<RuneDefinition>();
            foreach (var rune in AllRunes())
                if (AlwaysForSale(rune)) runes.Add(rune);
            return runes;
        }

        /// <summary>Buys a rune at rank I. It must be always for sale or in today's offer.</summary>
        public bool TryBuyRune(RuneDefinition rune, out string message)
        {
            var offered = rune != null && Profile.shrineOffers.Contains(rune.Id);
            if (rune != null && !offered && !AlwaysForSale(rune))
            {
                message = $"{rune.DisplayName} isn't in the Shrine today.";
                return false;
            }

            message = CannotBuyRune(rune);
            if (message != null) return false;

            var price = RunePrice(rune);
            Profile.wallet.TrySpend(Progression.Gold, price);
            Profile.wallet.TrySpend(Progression.Karma, RuneKarmaPrice(rune));
            Profile.runes.Add(new OwnedRune { id = rune.Id, rank = 1, goldSpent = price });
            for (var i = 0; i < Profile.shrineOffers.Count; i++)
                if (Profile.shrineOffers[i] == rune.Id) Profile.shrineOffers[i] = string.Empty;

            var slot = Profile.runeSlots.IndexOf(string.Empty);
            if (slot >= 0 && CannotSetRune(slot, rune) == null)
            {
                Profile.runeSlots[slot] = rune.Id;
                message = $"You bought {rune.DisplayName} and set it on the altar.";
            }
            else
            {
                message = $"You bought {rune.DisplayName}.";
            }

            Save();
            return true;
        }

        /// <summary>Why this rune can't rank up right now, or null if it can.</summary>
        public string CannotRankUpRune(RuneDefinition rune)
        {
            var owned = FindRune(rune);
            if (owned == null) return rune != null ? $"You don't have {rune.DisplayName}." : "There's no rune there.";
            if (owned.rank >= RuneMaxRank(rune)) return $"{rune.DisplayName} is at its top rank.";
            var needed = Progression.RuneRankClanLevel(owned.rank + 1);
            if (ClanLevel < needed) return $"Rank {RomanRank(owned.rank + 1)} needs clan level {needed}.";
            var cost = RuneRankCost(rune);
            if (!Profile.wallet.CanAfford(Progression.Gold, cost))
                return $"Rank {RomanRank(owned.rank + 1)} costs {cost} {Name(Progression.Gold)}. You have {Balance(Progression.Gold)}.";
            return null;
        }

        public bool TryRankUpRune(RuneDefinition rune, out string message)
        {
            message = CannotRankUpRune(rune);
            if (message != null) return false;

            var owned = FindRune(rune);
            var cost = RuneRankCost(rune);
            Profile.wallet.TrySpend(Progression.Gold, cost);
            owned.rank++;
            owned.goldSpent += cost;
            message = $"{rune.DisplayName} rose to rank {RomanRank(owned.rank)}.";
            Save();
            return true;
        }

        /// <summary>Gold selling this rune returns.</summary>
        public int RuneSellValue(RuneDefinition rune)
        {
            var owned = FindRune(rune);
            return owned == null ? 0 : (int)((long)owned.goldSpent * Progression.RuneSellRefund / ProgressionDefinition.BasisPoints);
        }

        /// <summary>Gives a rune back to the Shrine for part of the Gold spent on it. It leaves the altar.</summary>
        public bool TrySellRune(RuneDefinition rune, out string message)
        {
            var owned = FindRune(rune);
            if (owned == null)
            {
                message = rune != null ? $"You don't have {rune.DisplayName}." : "There's no rune there.";
                return false;
            }

            var refund = RuneSellValue(rune);
            Profile.runes.Remove(owned);
            for (var i = 0; i < Profile.runeSlots.Count; i++)
                if (Profile.runeSlots[i] == rune.Id) Profile.runeSlots[i] = string.Empty;
            Profile.wallet.Add(Progression.Gold, refund);
            message = $"You gave {rune.DisplayName} back to the Shrine for {refund} {Name(Progression.Gold)}.";
            Save();
            return true;
        }

        // The altar's slots.

        /// <summary>Slots lit so far. Runes can only be set in lit slots.</summary>
        public int RuneSlotsLit => Profile.runeSlots.Count;

        /// <summary>Slots the clan level has opened, lit or not.</summary>
        public int RuneSlotsOpen => Math.Min(Progression.RuneSlots.Count, Progression.RuneSlotsOpen(ClanLevel));

        /// <summary>Every slot the altar has.</summary>
        public int RuneSlotCount => Progression.RuneSlots.Count;

        /// <summary>The next slot to light, or null when every slot is lit.</summary>
        public ProgressionDefinition.RuneSlotStep NextRuneSlot =>
            RuneSlotsLit < Progression.RuneSlots.Count ? Progression.RuneSlots[RuneSlotsLit] : null;

        /// <summary>Why the next slot can't be lit right now, or null if it can.</summary>
        public string CannotLightRuneSlot()
        {
            var next = NextRuneSlot;
            if (next == null) return "Every altar slot is lit.";
            if (ClanLevel < next.clanLevel) return $"The next slot opens at clan level {next.clanLevel}.";
            if (!Profile.wallet.CanAfford(Progression.Karma, next.karma))
                return $"Lighting the slot costs {next.karma} {Name(Progression.Karma)}. You have {Balance(Progression.Karma)}.";
            return null;
        }

        public bool TryLightRuneSlot(out string message)
        {
            message = CannotLightRuneSlot();
            if (message != null) return false;

            Profile.wallet.TrySpend(Progression.Karma, NextRuneSlot.karma);
            Profile.runeSlots.Add(string.Empty);
            message = $"Altar slot {RuneSlotsLit} is lit.";
            Save();
            return true;
        }

        /// <summary>The rune in a lit slot, or null.</summary>
        public RuneDefinition RuneInSlot(int slot) =>
            slot >= 0 && slot < Profile.runeSlots.Count && Content.TryGet<RuneDefinition>(Profile.runeSlots[slot], out var rune) ? rune : null;

        /// <summary>
        /// Why this rune can't go in this slot, or null if it can. A rune already in another slot
        /// moves. Two set runes covering the same ninjas may not raise the same stat, and only so
        /// many Legendaries fit.
        /// </summary>
        public string CannotSetRune(int slot, RuneDefinition rune)
        {
            if (slot < 0 || slot >= RuneSlotsLit) return "That altar slot isn't lit yet.";
            if (rune == null) return "There's no rune there.";
            if (!OwnsRune(rune)) return $"You don't have {rune.DisplayName}.";

            var stats = rune.RaisedStats();
            var legendaries = rune.Rarity == Rarity.Legendary ? 1 : 0;
            for (var i = 0; i < Profile.runeSlots.Count; i++)
            {
                if (i == slot) continue;
                var other = RuneInSlot(i);
                if (other == null || other == rune) continue;
                if (other.Rarity == Rarity.Legendary) legendaries++;

                // A Seal only covers its element's ninjas, so it may raise the same stat as a rune for everyone.
                if (other.Element != rune.Element) continue;
                foreach (var stat in other.RaisedStats())
                    if (stats.Contains(stat))
                        return $"{other.DisplayName} already raises that stat. Two runes can't raise the same one.";
            }

            if (rune.Rarity == Rarity.Legendary && legendaries > Progression.MaxLegendaryRunes)
                return Progression.MaxLegendaryRunes == 1 ? "Only one Legendary rune fits on the altar." : $"Only {Progression.MaxLegendaryRunes} Legendary runes fit on the altar.";
            return null;
        }

        /// <summary>Sets a rune in a lit slot, replacing what was there. A rune already set elsewhere moves.</summary>
        public bool TrySetRune(int slot, RuneDefinition rune, out string message)
        {
            message = CannotSetRune(slot, rune);
            if (message != null) return false;

            for (var i = 0; i < Profile.runeSlots.Count; i++)
                if (Profile.runeSlots[i] == rune.Id) Profile.runeSlots[i] = string.Empty;
            Profile.runeSlots[slot] = rune.Id;
            message = $"{rune.DisplayName} is set on the altar.";
            Save();
            return true;
        }

        /// <summary>Takes the rune out of a slot. It stays yours.</summary>
        public bool ClearRuneSlot(int slot)
        {
            if (slot < 0 || slot >= Profile.runeSlots.Count || string.IsNullOrEmpty(Profile.runeSlots[slot])) return false;
            Profile.runeSlots[slot] = string.Empty;
            Save();
            return true;
        }

        /// <summary>The slot a rune is set in, or -1.</summary>
        public int SlotOfRune(RuneDefinition rune) => rune != null ? Profile.runeSlots.IndexOf(rune.Id) : -1;

        /// <summary>The runes set on the altar with their ranks, in slot order.</summary>
        public List<SetRune> SetRunes()
        {
            var set = new List<SetRune>();
            for (var i = 0; i < Profile.runeSlots.Count; i++)
            {
                var rune = RuneInSlot(i);
                var owned = FindRune(rune);
                if (rune != null && owned != null) set.Add(new SetRune(rune, Math.Min(owned.rank, rune.MaxRank)));
            }

            return set;
        }

        /// <summary>
        /// The passives the set runes give one of the player's ninjas, in slot order. Pass it to
        /// <see cref="Fights.CombatSetupBuilder.AddLineup"/> for the player's team.
        /// </summary>
        public List<PassiveEffect> RunePassives(NinjaDefinition ninja)
        {
            var effects = new List<PassiveEffect>();
            foreach (var set in SetRunes())
            {
                if (!set.Rune.Covers(ninja)) continue;
                foreach (var effect in set.Rune.Rank(set.Rank).effects)
                    if (effect != null) effects.Add(effect);
            }

            return effects;
        }

        /// <summary>Extra Gold from fights the set runes give, in basis points.</summary>
        public int RuneFightGoldBonus => RuneBonus(rank => rank.fightGold);

        /// <summary>Extra clan XP from fights the set runes give, in basis points.</summary>
        public int RuneClanXpBonus => RuneBonus(rank => rank.clanXp);

        /// <summary>Extra Mint and Steel Mine output the set runes give, in basis points.</summary>
        public int RuneIncomeBonus => RuneBonus(rank => rank.income);

        int RuneBonus(Func<RuneRank, int> of)
        {
            var total = 0;
            foreach (var set in SetRunes()) total += Math.Max(0, of(set.Rune.Rank(set.Rank)));
            return total;
        }

        static int WithBonus(int amount, int bonusBasisPoints) =>
            bonusBasisPoints <= 0 ? amount : (int)((long)amount * (ProgressionDefinition.BasisPoints + bonusBasisPoints) / ProgressionDefinition.BasisPoints);

        // The daily offer.

        /// <summary>The Shrine's offers. Null entries were bought already.</summary>
        public List<RuneDefinition> ShrineOffers()
        {
            var offers = new List<RuneDefinition>();
            foreach (var id in Profile.shrineOffers)
                offers.Add(!string.IsNullOrEmpty(id) && Content.TryGet<RuneDefinition>(id, out var rune) ? rune : null);
            return offers;
        }

        /// <summary>Rolls new Shrine offers for free when the day has changed since the last roll.</summary>
        public bool RefreshShrineIfNewDay(DateTime today)
        {
            var date = today.ToString(DateFormat, CultureInfo.InvariantCulture);
            if (Profile.shrineDate == date && Profile.shrineOffers.Count == Progression.ShrineOfferCount) return false;
            RollShrine();
            Profile.shrineDate = date;
            Save();
            return true;
        }

        public bool TryRefreshShrine(out string message)
        {
            var cost = Progression.ShrineRefreshCost;
            if (!Profile.wallet.TrySpend(Progression.Gold, cost))
            {
                message = $"New runes cost {cost} {Name(Progression.Gold)}.";
                return false;
            }

            RollShrine();
            message = "The Shrine has new runes.";
            Save();
            return true;
        }

        /// <summary>Picks the offers: a rarity by weight, then a rune of that rarity the player doesn't own. Each rune at most once a roll.</summary>
        void RollShrine()
        {
            var byRarity = new Dictionary<Rarity, List<RuneDefinition>>();
            foreach (var rune in AllRunes())
            {
                if (OwnsRune(rune) || RunePrice(rune) < 0) continue;
                if (!byRarity.TryGetValue(rune.Rarity, out var list)) byRarity[rune.Rarity] = list = new List<RuneDefinition>();
                list.Add(rune);
            }

            var random = NextRandom();
            Profile.shrineOffers.Clear();
            for (var i = 0; i < Progression.ShrineOfferCount; i++)
            {
                var choices = new List<ProgressionDefinition.RuneOffer>();
                var totalWeight = 0;
                foreach (var offer in Progression.RuneOffers)
                {
                    if (offer == null || offer.weight <= 0 || !byRarity.TryGetValue(offer.rarity, out var left) || left.Count == 0) continue;
                    choices.Add(offer);
                    totalWeight += offer.weight;
                }

                if (totalWeight <= 0)
                {
                    Profile.shrineOffers.Add(string.Empty);
                    continue;
                }

                var roll = random.Range(0, totalWeight);
                var chosen = choices[choices.Count - 1];
                foreach (var choice in choices)
                {
                    if (roll < choice.weight)
                    {
                        chosen = choice;
                        break;
                    }

                    roll -= choice.weight;
                }

                var pool = byRarity[chosen.rarity];
                var pick = random.Range(0, pool.Count);
                Profile.shrineOffers.Add(pool[pick].Id);
                pool.RemoveAt(pick);
            }
        }

        /// <summary>A rank as a Roman numeral, I to X.</summary>
        public static string RomanRank(int rank)
        {
            string[] numerals = { "I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX", "X" };
            return rank >= 1 && rank <= numerals.Length ? numerals[rank - 1] : rank.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>Drops unknown or duplicate runes, keeps ranks in range, keeps the altar to its slots and its rules, and lights the free slots.</summary>
        void CleanRunes()
        {
            var profile = Profile;
            profile.runes ??= new List<OwnedRune>();
            var seen = new HashSet<string>();
            profile.runes.RemoveAll(r => r == null || !Content.TryGet<RuneDefinition>(r.id, out _) || !seen.Add(r.id));
            foreach (var owned in profile.runes)
            {
                var rune = Content.Get<RuneDefinition>(owned.id);
                owned.rank = Math.Max(1, Math.Min(Math.Max(1, RuneMaxRank(rune)), owned.rank));
                owned.goldSpent = Math.Max(0, owned.goldSpent);
            }

            profile.runeSlots ??= new List<string>();
            if (profile.runeSlots.Count > Progression.RuneSlots.Count)
                profile.runeSlots.RemoveRange(Progression.RuneSlots.Count, profile.runeSlots.Count - Progression.RuneSlots.Count);

            // Light every open slot that costs nothing, so a new or older save starts with one.
            while (profile.runeSlots.Count < Progression.RuneSlots.Count)
            {
                var next = Progression.RuneSlots[profile.runeSlots.Count];
                if (next == null || next.karma > 0 || profile.clanLevel < next.clanLevel) break;
                profile.runeSlots.Add(string.Empty);
            }

            // Keep only runes that are owned, set once, and allowed beside the ones before them.
            var placed = new List<string>(profile.runeSlots);
            for (var i = 0; i < profile.runeSlots.Count; i++) profile.runeSlots[i] = string.Empty;
            for (var i = 0; i < placed.Count; i++)
            {
                if (string.IsNullOrEmpty(placed[i]) || !Content.TryGet<RuneDefinition>(placed[i], out var rune)) continue;
                if (profile.runeSlots.Contains(rune.Id) || CannotSetRune(i, rune) != null) continue;
                profile.runeSlots[i] = rune.Id;
            }

            profile.shrineOffers ??= new List<string>();
            for (var i = 0; i < profile.shrineOffers.Count; i++)
                if (!Content.TryGet<RuneDefinition>(profile.shrineOffers[i], out _)) profile.shrineOffers[i] = string.Empty;
            profile.shrineDate ??= string.Empty;
        }

        // ---------------------------------------------------------------- clan

        public int ClanLevel => Profile.clanLevel;

        public int ClanXp => Profile.clanXp;

        public int ClanXpToNext => Progression.ClanXpToNext(Profile.clanLevel);

        public int TeamSize => Math.Min(LineupRules.MaxUnits, Progression.TeamSize(Profile.clanLevel));

        // ---------------------------------------------------------------- campaign

        /// <summary>The most stars won on a stage. 0 until it is cleared.</summary>
        public int StarsOf(StageDefinition stage)
        {
            if (stage == null) return 0;
            foreach (var progress in Profile.stages)
                if (progress.id == stage.Id) return progress.stars;
            return 0;
        }

        public bool HasCleared(StageDefinition stage) => StarsOf(stage) > 0;

        /// <summary>A stage can be fought when it is the first, was cleared before, or the stage before it was cleared.</summary>
        public bool IsUnlocked(StageDefinition stage)
        {
            var index = Campaign.IndexOf(stage);
            if (index < 0) return false;
            return index == 0 || HasCleared(stage) || HasCleared(Campaign.Stages[index - 1]);
        }

        /// <summary>A chapter opens with its first stage.</summary>
        public bool IsUnlocked(ChapterDefinition chapter) =>
            chapter != null && chapter.Stages.Count > 0 && IsUnlocked(chapter.Stages[0]);

        /// <summary>Stars won in a chapter, out of <see cref="ChapterDefinition.MaxStars"/>.</summary>
        public int ChapterStars(ChapterDefinition chapter)
        {
            var stars = 0;
            if (chapter != null)
                foreach (var stage in chapter.Stages) stars += StarsOf(stage);
            return stars;
        }

        /// <summary>The first stage not cleared yet, or the last stage when the campaign is finished. Null without a campaign.</summary>
        public StageDefinition NextUnclearedStage()
        {
            var stages = Campaign.Stages;
            foreach (var stage in stages)
                if (!HasCleared(stage)) return stage;
            return stages.Count > 0 ? stages[stages.Count - 1] : null;
        }

        /// <summary>The stage picked for the next fight: the saved pick while it can be fought, otherwise <see cref="NextUnclearedStage"/>.</summary>
        public StageDefinition CurrentStage() =>
            Content.TryGet<StageDefinition>(Profile.stage, out var picked) && IsUnlocked(picked) ? picked : NextUnclearedStage();

        /// <summary>Picks the stage for the next fight. False when it is still locked.</summary>
        public bool SelectStage(StageDefinition stage)
        {
            if (!IsUnlocked(stage)) return false;
            if (Profile.stage == stage.Id) return true;
            Profile.stage = stage.Id;
            Save();
            return true;
        }

        /// <summary>
        /// Pays for a finished fight on a stage. Every fight pays Gold, Steel and clan XP by the
        /// stage's reward tier, a win has a chance of a weapon component, and each clan level
        /// reached pays Karma. The first clear adds bonus Gold, a certain component and the
        /// stage's own Karma, Steel and ninja. <paramref name="stars"/> is 0 for a loss; the best
        /// stars are kept. The seed makes the drop the same every time for the same fight.
        /// </summary>
        public FightRewards ApplyStageResult(StageDefinition stage, int stars, int seed)
        {
            stars = Math.Max(0, Math.Min(StageStars.Max, stars));
            var won = stars > 0;
            var tier = stage != null ? stage.RewardTier : 1;
            var rewards = new FightRewards { Won = won, Stars = stars, TeamSizeBefore = TeamSize };
            Progression.FightPay(tier, won, out rewards.Gold, out rewards.Steel, out rewards.ClanXp);
            rewards.Gold = WithBonus(rewards.Gold, RuneFightGoldBonus);
            rewards.ClanXp = WithBonus(rewards.ClanXp, RuneClanXpBonus);

            var random = new DeterministicRandom(unchecked((ulong)(uint)seed * 0x2545F4914F6CDD1DUL + 0x5EED));
            if (won)
            {
                var before = StarsOf(stage);
                rewards.FirstWin = stage != null && before == 0;
                if (stage != null && stars > before)
                {
                    SetStars(stage, stars);
                    rewards.NewStars = stars - before;
                }

                if (rewards.FirstWin)
                {
                    rewards.FirstWinGold = Progression.FirstWinGold(tier);
                    rewards.FirstClearKarma = stage.FirstClearKarma;
                    rewards.FirstClearSteel = stage.FirstClearSteel;
                    if (stage.FirstClearNinja != null && CannotGain(stage.FirstClearNinja) == null)
                        rewards.NinjaMessage = Gain(stage.FirstClearNinja);
                }

                if (rewards.FirstWin || random.RollBasisPoints(Progression.DropChance(tier)))
                {
                    var components = new List<WeaponDefinition>();
                    foreach (var weapon in Content.GetAll<WeaponDefinition>())
                        if (weapon.Tier == WeaponTier.Component) components.Add(weapon);
                    if (components.Count > 0)
                    {
                        rewards.Drop = components[random.Range(0, components.Count)];
                        AddWeapon(Profile, rewards.Drop.Id, 1);
                    }
                }
            }

            rewards.BestStars = StarsOf(stage);
            Profile.wallet.Add(Progression.Gold, rewards.Gold + rewards.FirstWinGold);
            Profile.wallet.Add(Progression.Steel, rewards.Steel + rewards.FirstClearSteel);

            Profile.clanXp += rewards.ClanXp;
            while (Profile.clanXp >= Progression.ClanXpToNext(Profile.clanLevel))
            {
                Profile.clanXp -= Progression.ClanXpToNext(Profile.clanLevel);
                Profile.clanLevel++;
                rewards.ClanLevelsGained++;
                rewards.Karma += Progression.ClanLevelKarma(Profile.clanLevel);
            }

            Profile.wallet.Add(Progression.Karma, rewards.Karma + rewards.FirstClearKarma);
            rewards.TeamSizeAfter = TeamSize;
            Save();
            return rewards;
        }

        void SetStars(StageDefinition stage, int stars)
        {
            foreach (var progress in Profile.stages)
            {
                if (progress.id != stage.Id) continue;
                progress.stars = stars;
                return;
            }

            Profile.stages.Add(new StageProgress { id = stage.Id, stars = stars });
        }

        public bool ChestOpened(ChapterDefinition chapter, StarChest chest)
        {
            if (chapter == null || chest == null) return false;
            foreach (var opened in Profile.chests)
                if (opened.chapter == chapter.Id && opened.stars == chest.stars) return true;
            return false;
        }

        /// <summary>Why a star chest can't be opened right now, or null if it can.</summary>
        public string CannotOpenChest(ChapterDefinition chapter, StarChest chest)
        {
            if (chapter == null || chest == null || !Contains(chapter.Chests, chest)) return "There's no chest there.";
            if (ChestOpened(chapter, chest)) return "You already opened this chest.";
            var stars = ChapterStars(chapter);
            return stars < chest.stars ? $"Needs {chest.stars} stars in {chapter.DisplayName}. You have {stars}." : null;
        }

        /// <summary>Pays out a star chest, once.</summary>
        public bool TryOpenChest(ChapterDefinition chapter, StarChest chest, out string message)
        {
            message = CannotOpenChest(chapter, chest);
            if (message != null) return false;

            Profile.chests.Add(new OpenedChest { chapter = chapter.Id, stars = chest.stars });
            Profile.wallet.Add(Progression.Gold, chest.gold);
            Profile.wallet.Add(Progression.Steel, chest.steel);
            Profile.wallet.Add(Progression.Karma, chest.karma);
            if (chest.weapon != null) AddWeapon(Profile, chest.weapon.Id, 1);
            message = $"The chest held {ChestContents(chest)}.";
            Save();
            return true;
        }

        /// <summary>What a star chest holds, such as "300 Gold, 30 Steel and a Shadow Katana".</summary>
        public string ChestContents(StarChest chest)
        {
            var parts = new List<string>();
            if (chest == null) return string.Empty;
            if (chest.gold > 0) parts.Add($"{chest.gold} {Name(Progression.Gold)}");
            if (chest.steel > 0) parts.Add($"{chest.steel} {Name(Progression.Steel)}");
            if (chest.karma > 0) parts.Add($"{chest.karma} {Name(Progression.Karma)}");
            if (chest.weapon != null) parts.Add(chest.weapon.DisplayName);
            if (parts.Count == 0) return "nothing";
            if (parts.Count == 1) return parts[0];
            return string.Join(", ", parts.GetRange(0, parts.Count - 1)) + " and " + parts[parts.Count - 1];
        }

        static bool Contains(IReadOnlyList<StarChest> chests, StarChest chest)
        {
            foreach (var candidate in chests)
                if (candidate == chest) return true;
            return false;
        }

        // ---------------------------------------------------------------- income

        static long UnixSeconds(DateTime time)
        {
            var utc = time.Kind == DateTimeKind.Local ? time.ToUniversalTime() : DateTime.SpecifyKind(time, DateTimeKind.Utc);
            return new DateTimeOffset(utc).ToUnixTimeSeconds();
        }

        IncomeTimer Timer(string id) => Profile.incomes.Find(timer => timer.id == id);

        /// <summary>
        /// Starts every income source that isn't running yet, with one hour already worked so
        /// there is something to collect on the first visit. Call when the village opens.
        /// Returns true if anything changed.
        /// </summary>
        public bool StartIncome(DateTime now)
        {
            var seconds = UnixSeconds(now);
            var changed = false;
            foreach (var income in Progression.Incomes)
            {
                if (income == null || string.IsNullOrEmpty(income.id)) continue;
                var timer = Timer(income.id);
                if (timer == null)
                {
                    Profile.incomes.Add(new IncomeTimer { id = income.id, collectedAt = seconds - IncomeSource.SecondsPerHour });
                    changed = true;
                }
                else if (timer.collectedAt > seconds)
                {
                    // The clock went back. Count from now, so the source can't stall.
                    timer.collectedAt = seconds;
                    changed = true;
                }
            }

            if (changed) Save();
            return changed;
        }

        /// <summary>How much an income source (see <see cref="IncomeIds"/>) has ready to collect.</summary>
        public int PendingIncome(string id, DateTime now)
        {
            var income = Progression.Income(id);
            var timer = Timer(id);
            if (income == null || timer == null) return 0;
            return income.Made(UnixSeconds(now) - timer.collectedAt, ClanLevel, RuneIncomeBonus);
        }

        /// <summary>What an income source makes per hour at the player's clan level.</summary>
        public int IncomePerHour(string id) => Progression.Income(id)?.PerHour(ClanLevel, RuneIncomeBonus) ?? 0;

        /// <summary>The most an income source holds at the player's clan level.</summary>
        public int IncomeCapacity(string id) => Progression.Income(id)?.Capacity(ClanLevel, RuneIncomeBonus) ?? 0;

        /// <summary>How long until an income source is full. Zero when it is full or doesn't exist.</summary>
        public TimeSpan IncomeTimeToFull(string id, DateTime now)
        {
            var income = Progression.Income(id);
            var timer = Timer(id);
            if (income == null || timer == null) return TimeSpan.Zero;
            var worked = Math.Max(0L, UnixSeconds(now) - timer.collectedAt);
            return TimeSpan.FromSeconds(Math.Max(0L, income.StorageSeconds - worked));
        }

        /// <summary>Moves what an income source has made into the wallet.</summary>
        public bool TryCollectIncome(string id, DateTime now, out string message)
        {
            var income = Progression.Income(id);
            if (income == null)
            {
                message = "Nothing is made here.";
                return false;
            }

            if (Timer(id) == null) StartIncome(now);
            var timer = Timer(id);
            var seconds = UnixSeconds(now);
            var worked = Math.Max(0L, seconds - timer.collectedAt);
            var amount = income.Made(worked, ClanLevel, RuneIncomeBonus);
            if (amount <= 0)
            {
                message = $"No {Name(income.resource)} to collect yet.";
                return false;
            }

            // Below a full store, keep the seconds that haven't made a whole unit yet.
            var perHour = income.PerHour(ClanLevel, RuneIncomeBonus);
            var leftover = 0L;
            if (worked < income.StorageSeconds && perHour > 0)
                leftover = Math.Max(0L, worked - ((long)amount * IncomeSource.SecondsPerHour + perHour - 1) / perHour);
            timer.collectedAt = seconds - leftover;

            Profile.wallet.Add(income.resource, amount);
            message = $"Collected {amount} {Name(income.resource)}.";
            Save();
            return true;
        }

        // ---------------------------------------------------------------- lineup

        /// <summary>Stores the player's lineup in the save.</summary>
        public void SaveLineup(PlayerLineup lineup)
        {
            if (lineup != null) Profile.lineup = lineup.ToSaved();
            Save();
        }

        /// <summary>
        /// Makes a lineup fit what the player owns: ninjas they don't have are removed, weapons
        /// beyond the number owned are taken off, and the lineup is cut to the team size.
        /// Returns true if anything changed.
        /// </summary>
        public bool FitLineup(PlayerLineup lineup)
        {
            if (lineup == null) return false;
            lineup.MaxUnits = TeamSize;

            var kept = new List<LineupSlot>();
            var used = new Dictionary<WeaponDefinition, int>();
            var changed = false;
            foreach (var slot in lineup.Slots)
            {
                if (!Owns(slot.ninja) || kept.Count >= TeamSize)
                {
                    changed = true;
                    continue;
                }

                var copy = new LineupSlot(slot.ninja, slot.column, slot.rowFromBack);
                foreach (var weapon in slot.weapons)
                {
                    used.TryGetValue(weapon, out var count);
                    if (count >= WeaponCount(weapon))
                    {
                        changed = true;
                        continue;
                    }

                    used[weapon] = count + 1;
                    copy.weapons.Add(weapon);
                }

                kept.Add(copy);
            }

            if (changed) lineup.SetSlots(kept);
            return changed;
        }

        // ---------------------------------------------------------------- cleaning

        /// <summary>Drops unknown IDs and duplicates and keeps every number in range, so a damaged or old save still loads.</summary>
        void Clean()
        {
            var profile = Profile;
            profile.wallet ??= new Wallet();
            profile.wallet.balances ??= new List<Wallet.Balance>();
            profile.wallet.balances.RemoveAll(b => b == null || !Content.TryGet<ResourceDefinition>(b.id, out _));
            foreach (var balance in profile.wallet.balances) balance.amount = Math.Max(0, balance.amount);

            profile.ninjas ??= new List<OwnedNinja>();
            var seenNinjas = new HashSet<string>();
            profile.ninjas.RemoveAll(n => n == null || !Content.TryGet<NinjaDefinition>(n.id, out _) || !seenNinjas.Add(n.id));
            foreach (var ninja in profile.ninjas)
            {
                ninja.level = Math.Max(1, Math.Min(Progression.MaxLevel, ninja.level));
                ninja.stars = Math.Max(1, Math.Min(Progression.MaxStars, ninja.stars));
                ninja.shards = Math.Max(0, ninja.shards);
            }

            profile.weapons ??= new List<OwnedWeapon>();
            var merged = new List<OwnedWeapon>();
            foreach (var weapon in profile.weapons)
            {
                if (weapon == null || weapon.count <= 0 || !Content.TryGet<WeaponDefinition>(weapon.id, out _)) continue;
                var existing = merged.Find(w => w.id == weapon.id);
                if (existing != null) existing.count += weapon.count;
                else merged.Add(new OwnedWeapon { id = weapon.id, count = weapon.count });
            }

            profile.weapons = merged;
            profile.clanLevel = Math.Max(1, profile.clanLevel);
            profile.clanXp = Math.Max(0, profile.clanXp);
            profile.lineup ??= new List<SavedLineupSlot>();

            profile.stage ??= string.Empty;
            profile.stages ??= new List<StageProgress>();
            var seenStages = new HashSet<string>();
            profile.stages.RemoveAll(progress => progress == null || progress.stars <= 0 ||
                                                 !Content.TryGet<StageDefinition>(progress.id, out _) || !seenStages.Add(progress.id));
            foreach (var progress in profile.stages) progress.stars = Math.Min(StageStars.Max, progress.stars);

            // A save from before the campaign: each beaten lineup clears the first stage that uses it, with one star.
            profile.beatenLineups ??= new List<string>();
            foreach (var lineupId in profile.beatenLineups)
                foreach (var stage in Campaign.Stages)
                {
                    if (stage.Lineup == null || stage.Lineup.Id != lineupId) continue;
                    if (seenStages.Add(stage.Id)) profile.stages.Add(new StageProgress { id = stage.Id, stars = 1 });
                    break;
                }

            profile.beatenLineups.Clear();

            profile.chests ??= new List<OpenedChest>();
            var seenChests = new HashSet<string>();
            profile.chests.RemoveAll(opened => opened == null || !Content.TryGet<ChapterDefinition>(opened.chapter, out var chapter) ||
                                               !HasChest(chapter, opened.stars) || !seenChests.Add($"{opened.chapter}#{opened.stars}"));

            profile.dojoOffers ??= new List<string>();
            for (var i = 0; i < profile.dojoOffers.Count; i++)
                if (!Content.TryGet<NinjaDefinition>(profile.dojoOffers[i], out _)) profile.dojoOffers[i] = string.Empty;
            profile.dojoDate ??= string.Empty;

            profile.incomes ??= new List<IncomeTimer>();
            var seenIncomes = new HashSet<string>();
            profile.incomes.RemoveAll(timer => timer == null || Progression.Income(timer.id) == null || !seenIncomes.Add(timer.id));

            CleanRunes();
        }

        static bool HasChest(ChapterDefinition chapter, int stars)
        {
            foreach (var chest in chapter.Chests)
                if (chest != null && chest.stars == stars) return true;
            return false;
        }

        static string Name(ContentDefinition definition) => definition != null ? definition.DisplayName : "?";
    }
}
