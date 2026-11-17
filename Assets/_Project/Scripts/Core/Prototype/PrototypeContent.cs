using System;
using System.Collections.Generic;
using NinjaRealm.Combat;
using NinjaRealm.Core.Content;
using NinjaRealm.Core.Definitions;
using NinjaRealm.Core.Definitions.Effects;
using NinjaRealm.Core.Fights;
using NinjaRealm.Core.Lineups;
using NinjaRealm.Core.Progression;
using UnityEngine;

namespace NinjaRealm.Core.Prototype
{
    /// <summary>
    /// Receives the prototype content. The editor seeder saves it as assets; tests and balance
    /// tools keep it in memory.
    /// </summary>
    public interface IContentSink
    {
        /// <summary>
        /// Returns the definition with this ID, creating it if needed. <paramref name="configure"/>
        /// sets its values; a sink may skip it for definitions that already exist.
        /// </summary>
        T Provide<T>(string folder, string id, string displayName, Action<T> configure) where T : ContentDefinition;
    }

    /// <summary>Creates definitions as plain in-memory objects.</summary>
    public sealed class InMemoryContentSink : IContentSink
    {
        readonly List<ContentDefinition> definitions = new List<ContentDefinition>();

        public IReadOnlyList<ContentDefinition> Definitions => definitions;

        public T Provide<T>(string folder, string id, string displayName, Action<T> configure) where T : ContentDefinition
        {
            var definition = ScriptableObject.CreateInstance<T>();
            definition.name = PrototypeContent.FileName(displayName);
            definition.SetId(id);
            definition.SetDisplayName(displayName);
            configure(definition);
            definitions.Add(definition);
            return definition;
        }

        /// <summary>A database of everything created, sorted by ID like the real one.</summary>
        public ContentDatabase CreateDatabase()
        {
            var sorted = new List<ContentDefinition>(definitions);
            sorted.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
            var database = ScriptableObject.CreateInstance<ContentDatabase>();
            database.SetDefinitions(sorted);
            database.Initialize();
            return database;
        }
    }

    /// <summary>
    /// The prototype's content: 17 stats, 3 elements, 4 classes, 9 skills, 9 ninjas,
    /// 6 components, 4 combined weapons, 5 enemy lineups, 3 resources and the progression
    /// settings. This is the source of truth for prototype numbers; the content assets are
    /// created from it.
    /// </summary>
    public sealed class PrototypeContent
    {
        readonly IContentSink sink;

        PrototypeContent(IContentSink sink)
        {
            this.sink = sink;
        }

        public static void Build(IContentSink sink) => new PrototypeContent(sink).Build();

        /// <summary>The asset file name for a display name: letters and digits only, so "Shogun's Guard" becomes ShogunsGuard.</summary>
        public static string FileName(string displayName)
        {
            var name = new System.Text.StringBuilder(displayName.Length);
            foreach (var c in displayName)
                if (char.IsLetterOrDigit(c)) name.Append(c);
            return name.ToString();
        }

        /// <summary>The prototype content as an in-memory database, for tests and balance runs.</summary>
        public static ContentDatabase CreateInMemoryDatabase()
        {
            var sink = new InMemoryContentSink();
            Build(sink);
            return sink.CreateDatabase();
        }

        void Build()
        {
            // Stats
            var health = Stat("health", "Health", 500f, false, "Damage a ninja can take before falling.");
            var attackDamage = Stat("attack_damage", "Attack Damage", 50f, false, "Damage dealt by each basic attack.");
            var attackSpeed = Stat("attack_speed", "Attack Speed", 0.7f, false, "Basic attacks per second.");
            var range = Stat("range", "Range", 1f, false, "Attack range in hexes. 1 is melee.");
            var armor = Stat("armor", "Armor", 20f, false, "Reduces physical damage taken: damage x 100 / (100 + armor).");
            var magicResist = Stat("magic_resist", "Magic Resist", 20f, false, "Reduces magic damage taken, like armor.");
            var abilityPower = Stat("ability_power", "Ability Power", 100f, false, "Scales skill damage. 100 is normal.");
            var maxMana = Stat("max_mana", "Max Mana", 60f, false, "The skill fires when mana reaches this.");
            var startMana = Stat("start_mana", "Starting Mana", 0f, false, "Mana at the start of a fight.");
            Stat("crit_chance", "Crit Chance", 25f, true, "Chance for a basic attack to crit.");
            var critDamage = Stat("crit_damage", "Crit Damage", 150f, true, "Damage of a crit, as a percentage of a normal hit.");
            var dodge = Stat("dodge", "Dodge", 0f, true, "Chance to avoid a basic attack.");
            Stat("move_speed", "Move Speed", 2f, false, "Hexes moved per second.");
            var omnivamp = Stat("omnivamp", "Omnivamp", 0f, true, "Heals for this percentage of all damage dealt.");
            var healingPower = Stat("healing_power", "Healing Power", 0f, true, "Bonus to heals and shields given.");
            var abilityDamage = Stat("ability_damage", "Ability Damage", 0f, true, "Bonus damage for skills.");
            var manaCostReduction = Stat("mana_cost_reduction", "Mana Cost Reduction", 0f, true, "Reduces max mana needed to cast.");

            // Elements
            var fire = Trait("fire", "Fire", TraitKind.Element, new Color(0.91f, 0.33f, 0.16f),
                new TraitTier(2, "Fire ninjas' attacks burn for 4% max health over 2s.",
                    OnHit(EffectScope.TraitMembers, Status(Target(TargetKind.CurrentTarget), StatusKind.Burn, 4f, 2f))),
                new TraitTier(3, "Burn for 8%, and burning enemies receive 40% less healing.",
                    OnHit(EffectScope.TraitMembers,
                        Status(Target(TargetKind.CurrentTarget), StatusKind.Burn, 8f, 2f),
                        Status(Target(TargetKind.CurrentTarget), StatusKind.HealingReduction, 40f, 2f))));

            var water = Trait("water", "Water", TraitKind.Element, new Color(0.18f, 0.55f, 0.92f),
                new TraitTier(2, "Every 4s, heal the most injured ally for 5% max health.",
                    Periodic(4f, EffectScope.Team, new HealAction(Target(TargetKind.LowestHealthAllies), 5f))),
                new TraitTier(3, "Heal for 8% instead.",
                    Periodic(4f, EffectScope.Team, new HealAction(Target(TargetKind.LowestHealthAllies), 8f))));

            var earth = Trait("earth", "Earth", TraitKind.Element, new Color(0.55f, 0.42f, 0.28f),
                new TraitTier(2, "Earth ninjas gain 20 armor and 20 magic resist.",
                    new StatModifierEffect(armor, ModifierOperation.Add, 20f, EffectScope.TraitMembers),
                    new StatModifierEffect(magicResist, ModifierOperation.Add, 20f, EffectScope.TraitMembers)),
                new TraitTier(3, "Earth ninjas gain 40 armor and 40 magic resist, and all other allies gain 10 of each.",
                    new StatModifierEffect(armor, ModifierOperation.Add, 40f, EffectScope.TraitMembers),
                    new StatModifierEffect(magicResist, ModifierOperation.Add, 40f, EffectScope.TraitMembers),
                    new StatModifierEffect(armor, ModifierOperation.Add, 10f, EffectScope.OtherAllies),
                    new StatModifierEffect(magicResist, ModifierOperation.Add, 10f, EffectScope.OtherAllies)));

            // Classes
            var vanguard = Trait("vanguard", "Vanguard", TraitKind.Class, new Color(0.62f, 0.66f, 0.72f),
                new TraitTier(2, "Vanguards start with a shield of 15% max health for 6s.",
                    AtStart(EffectScope.TraitMembers, new ShieldAction(Target(TargetKind.Self), 15f, 6f))),
                new TraitTier(3, "The shield is 30% instead.",
                    AtStart(EffectScope.TraitMembers, new ShieldAction(Target(TargetKind.Self), 30f, 6f))));

            var blademaster = Trait("blademaster", "Blademaster", TraitKind.Class, new Color(0.85f, 0.26f, 0.26f),
                new TraitTier(2, "Blademasters gain 12% attack damage.",
                    new StatModifierEffect(attackDamage, ModifierOperation.AddPercent, 12f, EffectScope.TraitMembers)),
                new TraitTier(3, "25% attack damage, and they heal for 10% of damage dealt.",
                    new StatModifierEffect(attackDamage, ModifierOperation.AddPercent, 25f, EffectScope.TraitMembers),
                    new StatModifierEffect(omnivamp, ModifierOperation.Add, 10f, EffectScope.TraitMembers)));

            var medic = Trait("medic", "Medic", TraitKind.Class, new Color(0.35f, 0.8f, 0.55f),
                new TraitTier(2, "Medics' heals and shields are 25% stronger.",
                    new StatModifierEffect(healingPower, ModifierOperation.Add, 25f, EffectScope.TraitMembers)),
                new TraitTier(3, "50% stronger instead.",
                    new StatModifierEffect(healingPower, ModifierOperation.Add, 50f, EffectScope.TraitMembers)));

            var mystic = Trait("mystic", "Mystic", TraitKind.Class, new Color(0.6f, 0.38f, 0.86f),
                new TraitTier(2, "Mystics gain 20 ability power.",
                    new StatModifierEffect(abilityPower, ModifierOperation.Add, 20f, EffectScope.TraitMembers)),
                new TraitTier(3, "40 ability power, and skills cost 10% less mana.",
                    new StatModifierEffect(abilityPower, ModifierOperation.Add, 40f, EffectScope.TraitMembers),
                    new StatModifierEffect(manaCostReduction, ModifierOperation.Add, 10f, EffectScope.TraitMembers)));

            // Skills
            var flameSlash = Skill("flame_slash", "Flame Slash", "Hits the target and enemies next to it for 180% attack damage.",
                new DamageAction(Target(TargetKind.CurrentTargetAndAdjacent), DamageType.Physical, 0f, 180f, false));
            var tideBlessing = Skill("tide_blessing", "Tide Blessing", "Heals the 2 most injured allies for 15% max health.",
                new HealAction(Target(TargetKind.LowestHealthAllies, count: 2), 15f));
            var stoneWall = Skill("stone_wall", "Stone Wall", "Taunts enemies within 2 hexes for 2s and takes 25% less damage for 4s.",
                Status(Target(TargetKind.EnemiesInRadiusOfSelf, radius: 2), StatusKind.Taunt, 0f, 2f),
                Status(Target(TargetKind.Self), StatusKind.DamageReduction, 25f, 4f));
            var quakeHammer = Skill("quake_hammer", "Quake Hammer", "Deals 200% attack damage and stuns the target for 1s.",
                new DamageAction(Target(TargetKind.CurrentTarget), DamageType.Physical, 0f, 200f, false),
                Status(Target(TargetKind.CurrentTarget), StatusKind.Stun, 0f, 1f));
            var fireLotus = Skill("fire_lotus", "Fire Lotus", "Deals 130 magic damage around the target and sets enemies burning.",
                new DamageAction(Target(TargetKind.EnemiesInRadiusOfTarget, radius: 1), DamageType.Magic, 130f, 0f, true),
                Status(Target(TargetKind.EnemiesInRadiusOfTarget, radius: 1), StatusKind.Burn, 4f, 3f));
            var riptideGuard = Skill("riptide_guard", "Riptide Guard", "Shields self for 25% max health and slows adjacent enemies.",
                new ShieldAction(Target(TargetKind.Self), 25f, 4f),
                Status(Target(TargetKind.AdjacentEnemies), StatusKind.Slow, 30f, 2f));
            var stoneskin = Skill("stoneskin", "Stoneskin", "Shields the most injured ally for 30% of their max health.",
                new ShieldAction(Target(TargetKind.LowestHealthAllies), 30f, 4f));
            var emberArmor = Skill("ember_armor", "Ember Armor", "Gains 30 armor and burns adjacent enemies for 4s.",
                new StatBuffAction(Target(TargetKind.Self), armor, ModifierOperation.Add, 30f, 4f),
                Status(Target(TargetKind.AdjacentEnemies), StatusKind.Burn, 4f, 4f));
            var whirlpool = Skill("whirlpool", "Whirlpool", "Deals 110 magic damage around the target and stuns them for 1.5s.",
                new DamageAction(Target(TargetKind.EnemiesInRadiusOfTarget, radius: 1), DamageType.Magic, 110f, 0f, true),
                Status(Target(TargetKind.EnemiesInRadiusOfTarget, radius: 1), StatusKind.Stun, 0f, 1.5f));

            // Ninjas: base stats come from the class, scaled by rarity.
            StatValue[] ClassStats(TraitDefinition cls, Rarity rarity)
            {
                var scale = rarity == Rarity.Common ? 1f : rarity == Rarity.Rare ? 1.1f : rarity == Rarity.Epic ? 1.2f : 1.3f;
                float hp, ad, aspd, rng, arm, mr, mana, start;
                if (cls == vanguard) { hp = 750; ad = 50; aspd = 0.6f; rng = 1; arm = 40; mr = 40; mana = 80; start = 30; }
                else if (cls == blademaster) { hp = 650; ad = 65; aspd = 0.75f; rng = 1; arm = 30; mr = 30; mana = 60; start = 0; }
                else if (cls == medic) { hp = 550; ad = 40; aspd = 0.65f; rng = 3; arm = 20; mr = 25; mana = 70; start = 20; }
                else { hp = 500; ad = 40; aspd = 0.65f; rng = 4; arm = 20; mr = 20; mana = 70; start = 10; }

                return new[]
                {
                    new StatValue(health, Mathf.Round(hp * scale)),
                    new StatValue(attackDamage, Mathf.Round(ad * scale)),
                    new StatValue(attackSpeed, aspd),
                    new StatValue(range, rng),
                    new StatValue(armor, arm),
                    new StatValue(magicResist, mr),
                    new StatValue(maxMana, mana),
                    new StatValue(startMana, start),
                };
            }

            NinjaDefinition Ninja(string id, string name, Rarity rarity, TraitDefinition element, TraitDefinition cls, SkillDefinition skill) =>
                sink.Provide<NinjaDefinition>("Ninjas", "ninja." + id, name,
                    n => n.Configure(rarity, element, cls, skill, ClassStats(cls, rarity)));

            var kaito = Ninja("kaito", "Kaito", Rarity.Common, fire, blademaster, flameSlash);
            var hana = Ninja("hana", "Hana", Rarity.Common, water, medic, tideBlessing);
            var daichi = Ninja("daichi", "Daichi", Rarity.Common, earth, vanguard, stoneWall);
            var goro = Ninja("goro", "Goro", Rarity.Rare, earth, blademaster, quakeHammer);
            var mei = Ninja("mei", "Mei", Rarity.Rare, fire, mystic, fireLotus);
            var takeshi = Ninja("takeshi", "Takeshi", Rarity.Rare, water, vanguard, riptideGuard);
            var saya = Ninja("saya", "Saya", Rarity.Rare, earth, medic, stoneskin);
            var jiro = Ninja("jiro", "Jiro", Rarity.Epic, fire, vanguard, emberArmor);
            var nami = Ninja("nami", "Nami", Rarity.Epic, water, mystic, whirlpool);

            // Weapon components
            var kunai = Component("kunai", "Kunai", "+10% attack damage.", Mod(attackDamage, ModifierOperation.AddPercent, 10f));
            var prayerBeads = Component("prayer_beads", "Prayer Beads", "+15 ability power.", Mod(abilityPower, ModifierOperation.Add, 15f));
            var ironPlate = Component("iron_plate", "Iron Plate", "+20 armor.", Mod(armor, ModifierOperation.Add, 20f));
            var swiftSandals = Component("swift_sandals", "Swift Sandals", "+10% attack speed.", Mod(attackSpeed, ModifierOperation.AddPercent, 10f));
            var chakraScroll = Component("chakra_scroll", "Chakra Scroll", "+15 starting mana.", Mod(startMana, ModifierOperation.Add, 15f));
            var jadeCharm = Component("jade_charm", "Jade Charm", "+10% max health.", Mod(health, ModifierOperation.AddPercent, 10f));

            // Combined weapons
            var shadowKatana = Combined("shadow_katana", "Shadow Katana", "Crits deal +30% damage.", kunai, kunai, Mod(critDamage, ModifierOperation.Add, 30f));
            var kusarigama = Combined("kusarigama", "Kusarigama", "Heals for 15% of all damage dealt.", kunai, prayerBeads, Mod(omnivamp, ModifierOperation.Add, 15f));
            var dragonFan = Combined("dragon_fan", "Dragon Fan", "Skills deal +20% damage.", prayerBeads, prayerBeads, Mod(abilityDamage, ModifierOperation.Add, 20f));
            var windBoots = Combined("wind_boots", "Wind Boots", "+10% dodge.", swiftSandals, swiftSandals, Mod(dodge, ModifierOperation.Add, 10f));

            // Enemy lineups, from easiest to hardest. Positions are seen from the enemy's side: row 3 is their front row.
            Lineup("bandit_patrol", "Bandit Patrol", 1, 1, "Three ninjas and no weapons. A warm-up.",
                Slot(daichi, 3, 3), Slot(kaito, 2, 3), Slot(hana, 3, 0));

            Lineup("ember_clan", "Ember Clan", 2, 2, "Three Fire ninjas set everything they hit burning.",
                Slot(jiro, 3, 3, ironPlate), Slot(kaito, 2, 3), Slot(goro, 4, 3), Slot(mei, 3, 1), Slot(daichi, 1, 3));

            Lineup("tide_shrine", "Tide Shrine", 3, 4, "Water healers that outlast you behind a shielded wall.",
                Slot(takeshi, 3, 3, jadeCharm), Slot(daichi, 2, 3, ironPlate), Slot(saya, 4, 1), Slot(hana, 2, 0, chakraScroll),
                Slot(nami, 4, 0, dragonFan, windBoots));

            Lineup("stone_fortress", "Stone Fortress", 4, 7, "Three Vanguards and three Earth ninjas. Hard to break, slow to kill you.",
                Slot(daichi, 3, 3, ironPlate, jadeCharm), Slot(jiro, 2, 3, ironPlate), Slot(takeshi, 4, 3, jadeCharm),
                Slot(goro, 3, 2, kusarigama), Slot(saya, 2, 1), Slot(mei, 4, 0, chakraScroll));

            Lineup("shogun_guard", "Shogun's Guard", 5, 10, "The Shogun's elite: every synergy they can field, and the best weapons.",
                Slot(jiro, 3, 3, ironPlate, jadeCharm), Slot(goro, 2, 3, shadowKatana, kusarigama), Slot(kaito, 4, 3, shadowKatana),
                Slot(takeshi, 1, 3, jadeCharm), Slot(nami, 3, 0, dragonFan, chakraScroll), Slot(mei, 5, 1, dragonFan, windBoots));

            // Resources
            var gold = Resource("gold", "Gold", new Color(1f, 0.82f, 0.35f), "Earned in every fight. Recruits ninjas.");
            var karma = Resource("karma", "Karma", new Color(0.7f, 0.55f, 1f), "Earned from clan levels. Levels up ninjas.");
            var steel = Resource("steel", "Steel", new Color(0.72f, 0.78f, 0.84f), "Earned from wins. Forges weapons.");

            // Progression: levels cost 20 Karma, growing 8% a level; each level adds 4% health and attack damage.
            var levelCosts = new List<int>();
            for (var level = 1; level < 60; level++)
                levelCosts.Add((int)Math.Round(20.0 * Math.Pow(1.08, level - 1), MidpointRounding.AwayFromZero));

            sink.Provide<ProgressionDefinition>("Economy", "progression.default", "Progression", p =>
            {
                p.gold = gold;
                p.karma = karma;
                p.steel = steel;
                p.startingResources = new List<ResourceAmount> { new ResourceAmount(gold, 500), new ResourceAmount(karma, 100), new ResourceAmount(steel, 0) };
                p.startingNinjas = new List<NinjaDefinition> { kaito, hana, daichi };
                p.startingWeapons = new List<WeaponDefinition> { kunai, ironPlate };
                p.rosterLimit = 20;
                p.levelCosts = levelCosts;
                p.statGrowthPerLevel = 400;
                p.levelsPerBelt = 5;
                p.beltNames = new List<string>
                {
                    "White", "Yellow", "Orange", "Green", "Blue", "Brown",
                    "Black, 1st dan", "Black, 2nd dan", "Black, 3rd dan", "Black, 4th dan", "Black, 5th dan", "Black, 6th dan",
                };
                p.stars = new List<ProgressionDefinition.StarRank>
                {
                    new ProgressionDefinition.StarRank { shards = 0, statBasisPoints = 10000 },
                    new ProgressionDefinition.StarRank { shards = 3, statBasisPoints = 16000 },
                    new ProgressionDefinition.StarRank { shards = 6, statBasisPoints = 25600 },
                };
                p.dojoOfferCount = 4;
                p.rarities = new List<ProgressionDefinition.RarityOffer>
                {
                    new ProgressionDefinition.RarityOffer { rarity = Rarity.Common, weight = 60, price = 200, directBuy = true },
                    new ProgressionDefinition.RarityOffer { rarity = Rarity.Rare, weight = 30, price = 500, directBuy = true },
                    new ProgressionDefinition.RarityOffer { rarity = Rarity.Epic, weight = 9, price = 1200, directBuy = false },
                    new ProgressionDefinition.RarityOffer { rarity = Rarity.Legendary, weight = 1, price = 3000, directBuy = false },
                };
                p.dojoRefreshCost = 50;
                p.directBuyPrice = 15000;
                p.forgeCost = 20;
                p.winGold = 60;
                p.winGoldPerDifficulty = 30;
                p.winSteelPerDifficulty = 5;
                p.winClanXp = 20;
                p.winClanXpPerDifficulty = 10;
                p.lossGold = 2500;
                p.lossClanXp = 5000;
                p.firstWinGoldPerDifficulty = 200;
                p.dropChance = 3000;
                p.dropChancePerDifficulty = 1000;
                p.clanXpPerLevel = 100;
                p.clanLevelKarma = 50;
                p.clanLevelKarmaPerLevel = 10;
                p.teamSizes = new List<ProgressionDefinition.TeamSizeStep>
                {
                    new ProgressionDefinition.TeamSizeStep { clanLevel = 1, teamSize = 3 },
                    new ProgressionDefinition.TeamSizeStep { clanLevel = 3, teamSize = 4 },
                    new ProgressionDefinition.TeamSizeStep { clanLevel = 6, teamSize = 5 },
                    new ProgressionDefinition.TeamSizeStep { clanLevel = 10, teamSize = 6 },
                };

                // Income: the Mint fills in 8 hours with about what four easy wins pay; the Steel Mine with enough for a forge and a half.
                p.incomes = new List<IncomeSource>
                {
                    new IncomeSource { id = IncomeIds.Mint, resource = gold, perHour = 40, perHourPerClanLevel = 5, storageHours = 8 },
                    new IncomeSource { id = IncomeIds.SteelMine, resource = steel, perHour = 4, perHourPerClanLevel = 1, storageHours = 8 },
                };
            });
        }


        LineupDefinition Lineup(string id, string name, int difficulty, int level, string description, params LineupSlot[] slots) =>
            sink.Provide<LineupDefinition>("Lineups", "lineup." + id, name, l => l.Configure(description, difficulty, level, slots));

        ResourceDefinition Resource(string id, string name, Color color, string description) =>
            sink.Provide<ResourceDefinition>("Economy", "resource." + id, name, r => r.Configure(description, color));

        static LineupSlot Slot(NinjaDefinition ninja, int column, int rowFromBack, params WeaponDefinition[] weapons) =>
            new LineupSlot(ninja, column, rowFromBack, weapons);

        StatDefinition Stat(string id, string name, float defaultValue, bool isPercentage, string description) =>
            sink.Provide<StatDefinition>("Stats", "stat." + id, name, s => s.Configure(defaultValue, isPercentage, description));

        TraitDefinition Trait(string id, string name, TraitKind kind, Color color, params TraitTier[] tiers) =>
            sink.Provide<TraitDefinition>(kind == TraitKind.Element ? "Traits/Elements" : "Traits/Classes",
                $"trait.{(kind == TraitKind.Element ? "element" : "class")}.{id}", name, t => t.Configure(kind, color, tiers));

        SkillDefinition Skill(string id, string name, string description, params ActionEffect[] actions) =>
            sink.Provide<SkillDefinition>("Skills", "skill." + id, name, s => s.Configure(description, 0.5f, actions));

        WeaponDefinition Component(string id, string name, string description, params PassiveEffect[] effects) =>
            sink.Provide<WeaponDefinition>("Weapons/Components", "weapon." + id, name,
                w => w.Configure(WeaponTier.Component, description, null, null, effects));

        WeaponDefinition Combined(string id, string name, string description, WeaponDefinition a, WeaponDefinition b, params PassiveEffect[] effects) =>
            sink.Provide<WeaponDefinition>("Weapons/Combined", "weapon." + id, name,
                w => w.Configure(WeaponTier.Combined, description, a, b, effects));

        StatModifierEffect Mod(StatDefinition stat, ModifierOperation operation, float value) =>
            new StatModifierEffect(stat, operation, value);

        TargetSelector Target(TargetKind kind, int count = 1, int radius = 1) => new TargetSelector(kind, count, radius);

        ApplyStatusAction Status(TargetSelector target, StatusKind status, float value, float duration) =>
            new ApplyStatusAction(target, status, value, duration);

        TriggeredEffect OnHit(EffectScope holders, params ActionEffect[] actions) =>
            new TriggeredEffect(EffectTrigger.OnAttackHit, holders, actions);

        TriggeredEffect AtStart(EffectScope holders, params ActionEffect[] actions) =>
            new TriggeredEffect(EffectTrigger.FightStart, holders, actions) { oncePerFight = true };

        TriggeredEffect Periodic(float interval, EffectScope holders, params ActionEffect[] actions) =>
            new TriggeredEffect(EffectTrigger.Periodic, holders, actions) { intervalSeconds = interval };


        /// <summary>
        /// Lineups of equal size and no weapons, one per main build, for checking that several
        /// builds can win. Used by the balance report, not shown in the game.
        /// </summary>
        public static List<BalanceTeam> BalanceArchetypes(ContentDatabase database)
        {
            NinjaDefinition N(string name) => database.Get<NinjaDefinition>("ninja." + name);
            BalanceTeam Team(string name, params LineupSlot[] slots) => new BalanceTeam(name, slots);

            return new List<BalanceTeam>
            {
                Team("Fire burn", Slot(N("jiro"), 3, 3), Slot(N("daichi"), 2, 3), Slot(N("kaito"), 4, 3), Slot(N("goro"), 3, 2), Slot(N("mei"), 3, 0)),
                Team("Water sustain", Slot(N("takeshi"), 3, 3), Slot(N("daichi"), 2, 3), Slot(N("saya"), 4, 1), Slot(N("hana"), 2, 0), Slot(N("nami"), 4, 0)),
                Team("Earth bruisers", Slot(N("daichi"), 3, 3), Slot(N("goro"), 2, 3), Slot(N("kaito"), 4, 3), Slot(N("saya"), 2, 0), Slot(N("mei"), 4, 0)),
                Team("Blades and spells", Slot(N("takeshi"), 3, 3), Slot(N("kaito"), 2, 3), Slot(N("goro"), 4, 3), Slot(N("mei"), 2, 0), Slot(N("nami"), 4, 0)),
                Team("Medics", Slot(N("takeshi"), 3, 3), Slot(N("jiro"), 2, 3), Slot(N("hana"), 2, 0), Slot(N("saya"), 4, 0), Slot(N("mei"), 3, 0)),
            };
        }
    }
}
