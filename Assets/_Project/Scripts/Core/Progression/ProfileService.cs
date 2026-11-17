using System;
using System.Collections.Generic;
using System.Globalization;
using NinjaRealm.Combat;
using NinjaRealm.Core.Content;
using NinjaRealm.Core.Definitions;
using NinjaRealm.Core.Lineups;

namespace NinjaRealm.Core.Progression
{
    /// <summary>What a fight paid, for the result screen.</summary>
    public sealed class FightRewards
    {
        public bool Won;
        public int Gold;
        public int Steel;
        public int ClanXp;
        public bool FirstWin;
        public int FirstWinGold;
        public WeaponDefinition Drop;
        public int ClanLevelsGained;
        public int Karma;
        public int TeamSizeBefore;
        public int TeamSizeAfter;
    }

    /// <summary>
    /// The player's progress: owning, levelling and starring ninjas, the Dojo, the weapon
    /// inventory and forging, fight rewards and clan levels. Every change checks its cost first,
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
            Clean();
        }

        public PlayerProfile Profile { get; }

        public ContentDatabase Content { get; }

        public ProgressionDefinition Progression { get; }

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
            var text = storage.Read();
            if (text == null || !SaveFile.TryRead(text, out var profile, out problem))
            {
                if (text != null) storage.SetAside();
                profile = NewProfile(progression, newSeed);
            }

            var service = new ProfileService(profile, content, progression, storage) { LoadProblem = problem };
            if (text == null || problem != null) service.Save();
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
                var pick = random.Range(0, pool.Count);
                Profile.dojoOffers.Add(pool[pick].Id);
                pool.RemoveAt(pick);
            }
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

        // ---------------------------------------------------------------- clan

        public int ClanLevel => Profile.clanLevel;

        public int ClanXp => Profile.clanXp;

        public int ClanXpToNext => Progression.ClanXpToNext(Profile.clanLevel);

        public int TeamSize => Math.Min(LineupRules.MaxUnits, Progression.TeamSize(Profile.clanLevel));

        // ---------------------------------------------------------------- fights

        public bool HasBeaten(LineupDefinition opponent) => opponent != null && Profile.beatenLineups.Contains(opponent.Id);

        /// <summary>
        /// Pays for a finished fight: Gold, Steel and clan XP by the opponent's difficulty, a
        /// first-win bonus, a chance of a weapon component on a win, and Karma for each clan
        /// level reached. The seed makes the drop the same every time for the same fight.
        /// </summary>
        public FightRewards ApplyFightResult(LineupDefinition opponent, bool won, int seed)
        {
            var difficulty = opponent != null ? opponent.Difficulty : 1;
            var rewards = new FightRewards { Won = won, TeamSizeBefore = TeamSize };
            Progression.FightPay(difficulty, won, out rewards.Gold, out rewards.Steel, out rewards.ClanXp);

            var random = new DeterministicRandom(unchecked((ulong)(uint)seed * 0x2545F4914F6CDD1DUL + 0x5EED));
            if (won)
            {
                rewards.FirstWin = opponent != null && !HasBeaten(opponent);
                if (rewards.FirstWin)
                {
                    rewards.FirstWinGold = Progression.FirstWinGold(difficulty);
                    Profile.beatenLineups.Add(opponent.Id);
                }

                if (rewards.FirstWin || random.RollBasisPoints(Progression.DropChance(difficulty)))
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

            Profile.wallet.Add(Progression.Gold, rewards.Gold + rewards.FirstWinGold);
            Profile.wallet.Add(Progression.Steel, rewards.Steel);

            Profile.clanXp += rewards.ClanXp;
            while (Profile.clanXp >= Progression.ClanXpToNext(Profile.clanLevel))
            {
                Profile.clanXp -= Progression.ClanXpToNext(Profile.clanLevel);
                Profile.clanLevel++;
                rewards.ClanLevelsGained++;
                rewards.Karma += Progression.ClanLevelKarma(Profile.clanLevel);
            }

            Profile.wallet.Add(Progression.Karma, rewards.Karma);
            rewards.TeamSizeAfter = TeamSize;
            Save();
            return rewards;
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
            return income.Made(UnixSeconds(now) - timer.collectedAt, ClanLevel);
        }

        /// <summary>What an income source makes per hour at the player's clan level.</summary>
        public int IncomePerHour(string id) => Progression.Income(id)?.PerHour(ClanLevel) ?? 0;

        /// <summary>The most an income source holds at the player's clan level.</summary>
        public int IncomeCapacity(string id) => Progression.Income(id)?.Capacity(ClanLevel) ?? 0;

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
            var amount = income.Made(worked, ClanLevel);
            if (amount <= 0)
            {
                message = $"No {Name(income.resource)} to collect yet.";
                return false;
            }

            // Below a full store, keep the seconds that haven't made a whole unit yet.
            var perHour = income.PerHour(ClanLevel);
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

        /// <summary>Stores the player's lineup and chosen opponent in the save.</summary>
        public void SaveLineup(PlayerLineup lineup, LineupDefinition opponent)
        {
            if (lineup != null) Profile.lineup = lineup.ToSaved();
            if (opponent != null) Profile.opponent = opponent.Id;
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
            profile.opponent ??= string.Empty;
            profile.beatenLineups ??= new List<string>();
            profile.beatenLineups.RemoveAll(id => !Content.TryGet<LineupDefinition>(id, out _));
            profile.dojoOffers ??= new List<string>();
            for (var i = 0; i < profile.dojoOffers.Count; i++)
                if (!Content.TryGet<NinjaDefinition>(profile.dojoOffers[i], out _)) profile.dojoOffers[i] = string.Empty;
            profile.dojoDate ??= string.Empty;

            profile.incomes ??= new List<IncomeTimer>();
            var seenIncomes = new HashSet<string>();
            profile.incomes.RemoveAll(timer => timer == null || Progression.Income(timer.id) == null || !seenIncomes.Add(timer.id));
        }

        static string Name(ContentDefinition definition) => definition != null ? definition.DisplayName : "?";
    }
}
