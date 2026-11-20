using System;
using System.Collections.Generic;
using NinjaRealm.Combat;
using NinjaRealm.Core.Campaign;
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
    /// The prototype's content: 22 Shrine runes, 17 stats, 3 elements, 4 classes, 9 skills, 9 ninjas,
    /// 6 components, 4 combined weapons, 24 enemy lineups, a campaign of 3 chapters with 8
    /// stages each, 3 resources and the progression settings. This is the source of truth for
    /// prototype numbers; the content assets are created from it.
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
            var critChance = Stat("crit_chance", "Crit Chance", 25f, true, "Chance for a basic attack to crit.");
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

            var landslide = Skill("landslide", "Landslide", "Deals 100 magic damage to enemies within 2 hexes of the target and slows them by 40% for 3s.",
                new DamageAction(Target(TargetKind.EnemiesInRadiusOfTarget, radius: 2), DamageType.Magic, 100f, 0f, true),
                Status(Target(TargetKind.EnemiesInRadiusOfTarget, radius: 2), StatusKind.Slow, 40f, 3f));
            var phoenixRite = Skill("phoenix_rite", "Phoenix Rite", "Heals the 3 most injured allies for 18% max health and gives them 20% attack damage for 4s.",
                new HealAction(Target(TargetKind.LowestHealthAllies, count: 3), 18f),
                new StatBuffAction(Target(TargetKind.LowestHealthAllies, count: 3), attackDamage, ModifierOperation.AddPercent, 20f, 4f));
            var tidalCleave = Skill("tidal_cleave", "Tidal Cleave", "Hits the target and enemies next to it for 220% attack damage and slows them by 30% for 2s.",
                new DamageAction(Target(TargetKind.CurrentTargetAndAdjacent), DamageType.Physical, 0f, 220f, false),
                Status(Target(TargetKind.CurrentTargetAndAdjacent), StatusKind.Slow, 30f, 2f));

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
            var iwao = Ninja("iwao", "Iwao", Rarity.Epic, earth, mystic, landslide);
            var akane = Ninja("akane", "Akane", Rarity.Legendary, fire, medic, phoenixRite);
            var ryuu = Ninja("ryuu", "Ryuu", Rarity.Legendary, water, blademaster, tidalCleave);

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

            // Enemy lineups, in the order the campaign meets them. Positions are seen from the enemy's side: row 3 is their front row.
            // Chapter 1, Bandit Hills: three ninjas, then four, with at most one weapon component.
            var banditPatrol = Lineup("bandit_patrol", "Bandit Patrol", "Three ninjas and no weapons. A warm-up.",
                Slot(daichi, 3, 3), Slot(kaito, 2, 3), Slot(hana, 3, 0));
            var hillScouts = Lineup("hill_scouts", "Hill Scouts", "Two Blademasters hit hard and early. Their healer stands alone at the back.",
                Slot(kaito, 3, 3), Slot(goro, 4, 3), Slot(hana, 3, 0));
            var mountainLookout = Lineup("mountain_lookout", "Mountain Lookout", "Two shielded Vanguards hold the line for a Mystic behind them.",
                Slot(daichi, 3, 3), Slot(takeshi, 2, 3), Slot(mei, 3, 0));
            var banditAmbush = Lineup("bandit_ambush", "Bandit Ambush", "Goro leads with a Kunai, and Saya's shields keep him standing.",
                Slot(goro, 3, 3, kunai), Slot(kaito, 2, 3), Slot(saya, 3, 0));
            var smugglersCamp = Lineup("smugglers_camp", "Smugglers' Camp", "Four of them now: a wall, two blades and a healer.",
                Slot(daichi, 3, 3), Slot(kaito, 2, 3), Slot(goro, 4, 3), Slot(hana, 3, 0));
            var stolenShrine = Lineup("stolen_shrine", "Stolen Shrine", "Vanguards in front, a healer and a Mystic behind. Reach the back row.",
                Slot(takeshi, 3, 3, jadeCharm), Slot(daichi, 2, 3), Slot(hana, 2, 0), Slot(mei, 4, 0));
            var chiefsGuard = Lineup("chiefs_guard", "Chief's Guard", "Three Earth ninjas: armor and magic resist for the whole team.",
                Slot(daichi, 3, 3, ironPlate), Slot(goro, 2, 3), Slot(mei, 4, 1), Slot(saya, 3, 0));
            var banditChief = Lineup("bandit_chief", "Bandit Chief", "The chief is a two-star Kaito with a Shadow Katana. His crits end fights.",
                Slot(kaito, 3, 3, shadowKatana), Slot(daichi, 2, 3), Slot(goro, 4, 3), Slot(hana, 3, 0));

            // Chapter 2, Ember Valley: four ninjas, then five, with components and the odd combined weapon.
            var valleyGate = Lineup("valley_gate", "Valley Gate", "A plated wall, a blade and two casters at the back.",
                Slot(daichi, 3, 3, ironPlate), Slot(kaito, 2, 3), Slot(mei, 3, 0), Slot(hana, 4, 0));
            var ashRoad = Lineup("ash_road", "Ash Road", "Two armed Blademasters with a Mystic and a Medic behind them.",
                Slot(kaito, 3, 3, kunai), Slot(goro, 2, 3), Slot(mei, 2, 0, prayerBeads), Slot(saya, 4, 0));
            var burningFields = Lineup("burning_fields", "Burning Fields", "Fast attacks at the front, steady healing at the back.",
                Slot(takeshi, 3, 3, jadeCharm), Slot(kaito, 4, 3, swiftSandals), Slot(mei, 3, 1), Slot(hana, 3, 0, chakraScroll));
            var flameWardens = Lineup("flame_wardens", "Flame Wardens", "Three Fire ninjas: their burns cut your healing. Mei carries a Dragon Fan.",
                Slot(jiro, 3, 3, ironPlate), Slot(kaito, 2, 3, kunai), Slot(mei, 3, 0, dragonFan), Slot(saya, 4, 0));
            var lavaBridge = Lineup("lava_bridge", "Lava Bridge", "Five of them: two Vanguards, a blade, a healer and a Mystic.",
                Slot(daichi, 3, 3, ironPlate), Slot(takeshi, 2, 3), Slot(goro, 4, 3, kunai), Slot(hana, 2, 0), Slot(mei, 4, 0, prayerBeads));
            var emberClan = Lineup("ember_clan", "Ember Clan", "Three Fire ninjas set everything they hit burning.",
                Slot(jiro, 3, 3, ironPlate), Slot(kaito, 2, 3), Slot(goro, 4, 3), Slot(mei, 3, 1), Slot(daichi, 1, 3));
            var smokeTemple = Lineup("smoke_temple", "Smoke Temple", "Goro heals with every hit of his Kusarigama, behind two Vanguards.",
                Slot(takeshi, 3, 3, jadeCharm), Slot(jiro, 2, 3, ironPlate), Slot(goro, 4, 3, kusarigama), Slot(saya, 2, 0, chakraScroll),
                Slot(mei, 4, 0, prayerBeads));
            var emberLord = Lineup("ember_lord", "Ember Lord", "Jiro, lord of the Ember Clan, fights at two stars with his clan around him.",
                Slot(jiro, 3, 3, ironPlate, jadeCharm), Slot(kaito, 2, 3, kunai), Slot(goro, 4, 3, swiftSandals), Slot(mei, 3, 1, dragonFan),
                Slot(daichi, 1, 3, ironPlate));

            // Chapter 3, Tide Coast: five ninjas, then six, with combined weapons.
            var coastRoad = Lineup("coast_road", "Coast Road", "A full line with weapons on every ninja, and a Shadow Katana among them.",
                Slot(takeshi, 3, 3, jadeCharm), Slot(daichi, 2, 3, ironPlate), Slot(kaito, 4, 3, shadowKatana), Slot(hana, 2, 0, chakraScroll),
                Slot(mei, 4, 0, prayerBeads));
            var tideShrine = Lineup("tide_shrine", "Tide Shrine", "Water healers that outlast you behind a shielded wall.",
                Slot(takeshi, 3, 3, jadeCharm), Slot(daichi, 2, 3, ironPlate), Slot(saya, 4, 1), Slot(hana, 2, 0, chakraScroll),
                Slot(nami, 4, 0, dragonFan, windBoots));
            var pearlDivers = Lineup("pearl_divers", "Pearl Divers", "Three Water ninjas and two Medics. Burst them down or they heal it all back.",
                Slot(takeshi, 3, 3, jadeCharm, ironPlate), Slot(goro, 2, 3, kusarigama), Slot(nami, 3, 0, dragonFan), Slot(hana, 2, 0, chakraScroll),
                Slot(saya, 4, 0));
            var stormMonks = Lineup("storm_monks", "Storm Monks", "Two Mystics with Dragon Fans behind two Vanguards. Their skills come fast.",
                Slot(jiro, 3, 3, ironPlate, jadeCharm), Slot(takeshi, 2, 3, jadeCharm), Slot(nami, 3, 0, dragonFan, chakraScroll),
                Slot(mei, 4, 0, dragonFan), Slot(saya, 2, 0, prayerBeads));
            var stoneFortress = Lineup("stone_fortress", "Stone Fortress", "Three Vanguards and three Earth ninjas. Hard to break, slow to kill you.",
                Slot(daichi, 3, 3, ironPlate, jadeCharm), Slot(jiro, 2, 3, ironPlate), Slot(takeshi, 4, 3, jadeCharm),
                Slot(goro, 3, 2, kusarigama), Slot(saya, 2, 1), Slot(mei, 4, 0, chakraScroll));
            var harborBlockade = Lineup("harbor_blockade", "Harbor Blockade", "Six ninjas: four across the front, a healer and Nami behind.",
                Slot(daichi, 3, 3, ironPlate, jadeCharm), Slot(takeshi, 2, 3, jadeCharm), Slot(kaito, 4, 3, shadowKatana),
                Slot(goro, 1, 3, kusarigama), Slot(hana, 2, 0, chakraScroll), Slot(nami, 4, 0, dragonFan));
            var shogunsVanguard = Lineup("shoguns_vanguard", "Shogun's Vanguard", "Three Vanguards screen Goro and two casters. The last gate before the guard.",
                Slot(jiro, 3, 3, ironPlate, jadeCharm), Slot(daichi, 2, 3, ironPlate), Slot(takeshi, 4, 3, jadeCharm),
                Slot(goro, 3, 2, shadowKatana), Slot(saya, 2, 0, chakraScroll), Slot(mei, 4, 0, dragonFan, windBoots));
            var shogunGuard = Lineup("shogun_guard", "Shogun's Guard", "The Shogun's elite: every synergy they can field, the best weapons, and a two-star Goro.",
                Slot(jiro, 3, 3, ironPlate, jadeCharm), Slot(goro, 2, 3, shadowKatana, kusarigama), Slot(kaito, 4, 3, shadowKatana),
                Slot(takeshi, 1, 3, jadeCharm), Slot(nami, 3, 0, dragonFan, chakraScroll), Slot(mei, 5, 1, dragonFan, windBoots));

            // The campaign. Every fourth stage is an elite and every eighth a boss. What a fight
            // pays goes up a tier every five stages; the first clear pays 20 Karma per chapter number.
            var stageCount = 0;
            StageDefinition Stage(int chapter, int place, LineupDefinition lineup, int level,
                NinjaDefinition boss = null, int bossStars = 1, NinjaDefinition reward = null)
            {
                var kind = place == 8 ? StageKind.Boss : place == 4 ? StageKind.Elite : StageKind.Normal;
                var tier = Math.Min(5, stageCount / 5 + 1);
                stageCount++;
                return sink.Provide<StageDefinition>("Campaign/Stages", $"stage.{chapter}_{place}", $"{chapter}-{place} {lineup.DisplayName}", stage =>
                {
                    stage.Configure(lineup, level, kind, tier);
                    stage.SetBoss(boss, bossStars);
                    stage.SetFirstClear(20 * chapter, kind == StageKind.Elite ? 20 * chapter : 0, reward);
                });
            }

            // Star chests at 8, 16 and 24 stars: Gold and Steel, then Karma, then a combined weapon.
            StarChest[] Chests(int chapter, WeaponDefinition weapon) => new[]
            {
                new StarChest { stars = 8, gold = 300 * chapter, steel = 20 * chapter },
                new StarChest { stars = 16, karma = 100 * chapter },
                new StarChest { stars = 24, weapon = weapon },
            };

            void Chapter(int number, string id, string name, string description, WeaponDefinition lastChest, params StageDefinition[] stages) =>
                sink.Provide<ChapterDefinition>("Campaign/Chapters", "chapter." + id, name, c => c.Configure(number, description, stages, Chests(number, lastChest)));

            // The bosses fight with stars: Kaito, Jiro and Goro at two stars. Beating one wins a ninja.
            // The levels are tuned with the campaign walker; see docs/BALANCE.md.
            Chapter(1, "bandit_hills", "Bandit Hills", "Bandits hold the hill roads above the village. Clear them out and face their chief.", windBoots,
                Stage(1, 1, banditPatrol, 1), Stage(1, 2, hillScouts, 2), Stage(1, 3, mountainLookout, 2), Stage(1, 4, banditAmbush, 3),
                Stage(1, 5, smugglersCamp, 3), Stage(1, 6, stolenShrine, 3), Stage(1, 7, chiefsGuard, 4),
                Stage(1, 8, banditChief, 4, kaito, 2, mei));

            Chapter(2, "ember_valley", "Ember Valley", "The Ember Clan burns everything it touches. Their lord Jiro waits at the end of the valley.", kusarigama,
                Stage(2, 1, valleyGate, 7), Stage(2, 2, ashRoad, 8), Stage(2, 3, burningFields, 10), Stage(2, 4, flameWardens, 11),
                Stage(2, 5, lavaBridge, 12), Stage(2, 6, emberClan, 13), Stage(2, 7, smokeTemple, 14),
                Stage(2, 8, emberLord, 14, jiro, 2, jiro));

            Chapter(3, "tide_coast", "Tide Coast", "The Shogun's fleet has landed on the coast. Break through to his guard.", dragonFan,
                Stage(3, 1, coastRoad, 25), Stage(3, 2, tideShrine, 28), Stage(3, 3, pearlDivers, 31), Stage(3, 4, stormMonks, 33),
                Stage(3, 5, stoneFortress, 35), Stage(3, 6, harborBlockade, 37), Stage(3, 7, shogunsVanguard, 39),
                Stage(3, 8, shogunGuard, 40, goro, 2, nami));

            // Resources
            var gold = Resource("gold", "Gold", new Color(1f, 0.82f, 0.35f), "Earned in every fight. Recruits ninjas.");
            var karma = Resource("karma", "Karma", new Color(0.7f, 0.55f, 1f), "Earned from clan levels. Levels up ninjas.");
            var steel = Resource("steel", "Steel", new Color(0.72f, 0.78f, 0.84f), "Earned from wins. Forges weapons.");

            // Runes: carved stones the Shrine sells. Each covers every ninja in the lineup (a Seal only its element's).
            // Values run evenly from rank I to rank V; see RuneRanks.
            var blade = new Color(0.95f, 0.36f, 0.3f);
            var shell = new Color(0.56f, 0.74f, 0.92f);
            var spirit = new Color(0.74f, 0.52f, 1f);
            var fortune = new Color(1f, 0.82f, 0.35f);

            RuneOf("ember_fang", "Ember Fang", Rarity.Common, RuneFamily.Blade, null, blade, "A fang of red slate, warm to the touch.",
                StatRanks(attackDamage, ModifierOperation.AddPercent, 3f, 8f));
            RuneOf("hummingbird_sigil", "Hummingbird Sigil", Rarity.Common, RuneFamily.Blade, null, blade, "Hands move before the mind decides.",
                StatRanks(attackSpeed, ModifierOperation.AddPercent, 3f, 8f));
            RuneOf("oak_heart", "Oak Heart", Rarity.Common, RuneFamily.Shell, null, shell, "Old wood that bends and never breaks.",
                StatRanks(health, ModifierOperation.AddPercent, 3f, 8f));
            RuneOf("iron_bark", "Iron Bark", Rarity.Common, RuneFamily.Shell, null, shell, "Skin like the bark of a mountain pine.",
                StatRanks(armor, ModifierOperation.Add, 4f, 12f));
            RuneOf("still_pond", "Still Pond", Rarity.Common, RuneFamily.Shell, null, shell, "Spells sink into it like stones into calm water.",
                StatRanks(magicResist, ModifierOperation.Add, 4f, 12f));
            RuneOf("dawn_coal", "Dawn Coal", Rarity.Common, RuneFamily.Spirit, null, spirit, "A coal that glows before the fight begins.",
                StatRanks(startMana, ModifierOperation.Add, 4f, 12f));
            RuneOf("coin_lantern", "Coin Lantern", Rarity.Common, RuneFamily.Fortune, null, fortune, "Lights the way to a fuller purse.",
                EconomyRanks(400, 1200, (rank, value) => rank.fightGold = value));

            RuneOf("hawk_eye", "Hawk Eye", Rarity.Rare, RuneFamily.Blade, null, blade, "Sees the gap in every guard.",
                StatRanks(critChance, ModifierOperation.Add, 3f, 8f));
            RuneOf("split_moon", "Split Moon", Rarity.Rare, RuneFamily.Blade, null, blade, "A crescent cut clean in two.",
                StatRanks(critDamage, ModifierOperation.Add, 8f, 20f));
            RuneOf("paper_crane_step", "Paper Crane Step", Rarity.Rare, RuneFamily.Shell, null, shell, "Light enough to ride the wind of a blade.",
                StatRanks(dodge, ModifierOperation.Add, 2f, 6f));
            RuneOf("ink_storm", "Ink Storm", Rarity.Rare, RuneFamily.Spirit, null, spirit, "Every brush stroke lands like thunder.",
                StatRanks(abilityDamage, ModifierOperation.Add, 4f, 10f));
            RuneOf("lotus_breath", "Lotus Breath", Rarity.Rare, RuneFamily.Spirit, null, spirit, "Calm breath, stronger mending.",
                StatRanks(healingPower, ModifierOperation.Add, 5f, 12f));
            RuneOf("scholars_brush", "Scholar's Brush", Rarity.Rare, RuneFamily.Fortune, null, fortune, "Every fight is a lesson written down.",
                EconomyRanks(500, 1500, (rank, value) => rank.clanXp = value));
            RuneOf("kindling_seal", "Kindling Seal", Rarity.Rare, RuneFamily.Seal, fire, fire.Color, "Fans the fire in every Fire ninja.",
                StatRanks(attackDamage, ModifierOperation.AddPercent, 5f, 12f));
            RuneOf("tide_seal", "Tide Seal", Rarity.Rare, RuneFamily.Seal, water, water.Color, "The tide carries every Water ninja's healing further.",
                StatRanks(healingPower, ModifierOperation.Add, 8f, 20f));
            RuneOf("bedrock_seal", "Bedrock Seal", Rarity.Rare, RuneFamily.Seal, earth, earth.Color, "Earth ninjas stand on the roots of the mountain.",
                StatRanks(armor, ModifierOperation.Add, 8f, 20f));

            RuneOf("leech_fang", "Leech Fang", Rarity.Epic, RuneFamily.Spirit, null, spirit, "Every wound given closes one taken.",
                StatRanks(omnivamp, ModifierOperation.Add, 3f, 8f));
            RuneOf("twin_current", "Twin Current", Rarity.Epic, RuneFamily.Blade, null, blade, "Two streams, one faster blade.",
                StatRanks(attackSpeed, ModifierOperation.AddPercent, 5f, 10f, attackDamage, ModifierOperation.AddPercent, 3f, 6f));
            RuneOf("mountain_bell", "Mountain Bell", Rarity.Epic, RuneFamily.Fortune, null, fortune, "Its ring calls the village to work.",
                EconomyRanks(1000, 2500, (rank, value) => rank.income = value));

            RuneOf("phoenix_ash", "Phoenix Ash", Rarity.Legendary, RuneFamily.Spirit, null, new Color(1f, 0.55f, 0.2f),
                "Ash that remembers the flame. Once a fight, a falling ninja rises again.",
                Ranks(20f, 40f, value => new RuneRank(new TriggeredEffect(EffectTrigger.HealthBelowThreshold, EffectScope.Self,
                        new HealAction(Target(TargetKind.Self), value)) { healthThresholdPercent = 30f, oncePerFight = true })
                    { text = $"Once a fight, below 30% health: heal {value:0.##}% of max health" }));
            RuneOf("heart_of_the_realm", "Heart of the Realm", Rarity.Legendary, RuneFamily.Shell, null, new Color(1f, 0.93f, 0.7f),
                "The realm's own strength, shared by the clan.",
                StatRanks(health, ModifierOperation.AddPercent, 6f, 12f, attackDamage, ModifierOperation.AddPercent, 6f, 12f));
            RuneOf("thunder_script", "Thunder Script", Rarity.Legendary, RuneFamily.Blade, null, new Color(0.45f, 0.9f, 1f),
                "Words of lightning carved in black stone.",
                Ranks(40f, 100f, value => new RuneRank(new TriggeredEffect(EffectTrigger.OnAttackHit, EffectScope.Self,
                        new DamageAction(Target(TargetKind.CurrentTarget), DamageType.Magic, value, 0f, false)) { everyNth = 3 })
                    { text = $"Every 3rd attack: +{value:0.##} magic damage" }));

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
                    new ProgressionDefinition.TeamSizeStep { clanLevel = 2, teamSize = 4 },
                    new ProgressionDefinition.TeamSizeStep { clanLevel = 5, teamSize = 5 },
                    new ProgressionDefinition.TeamSizeStep { clanLevel = 9, teamSize = 6 },
                };

                // Income: the Mint fills in 8 hours with about what four easy wins pay; the Steel Mine with enough for a forge and a half.
                p.incomes = new List<IncomeSource>
                {
                    new IncomeSource { id = IncomeIds.Mint, resource = gold, perHour = 40, perHourPerClanLevel = 5, storageHours = 8 },
                    new IncomeSource { id = IncomeIds.SteelMine, resource = steel, perHour = 4, perHourPerClanLevel = 1, storageHours = 8 },
                };

                // Runes (the Shrine): six altar slots opened by clan level and lit with Karma, ranks II to V for Gold.
                p.runeSlots = new List<ProgressionDefinition.RuneSlotStep>
                {
                    new ProgressionDefinition.RuneSlotStep { clanLevel = 1, karma = 0 },
                    new ProgressionDefinition.RuneSlotStep { clanLevel = 2, karma = 40 },
                    new ProgressionDefinition.RuneSlotStep { clanLevel = 4, karma = 80 },
                    new ProgressionDefinition.RuneSlotStep { clanLevel = 7, karma = 150 },
                    new ProgressionDefinition.RuneSlotStep { clanLevel = 10, karma = 250 },
                    new ProgressionDefinition.RuneSlotStep { clanLevel = 14, karma = 400 },
                };
                p.runeRanks = new List<ProgressionDefinition.RuneRankStep>
                {
                    new ProgressionDefinition.RuneRankStep { costBasisPoints = 5000, clanLevel = 3 },
                    new ProgressionDefinition.RuneRankStep { costBasisPoints = 10000, clanLevel = 6 },
                    new ProgressionDefinition.RuneRankStep { costBasisPoints = 15000, clanLevel = 9 },
                    new ProgressionDefinition.RuneRankStep { costBasisPoints = 20000, clanLevel = 12 },
                };
                p.runeOffers = new List<ProgressionDefinition.RuneOffer>
                {
                    new ProgressionDefinition.RuneOffer { rarity = Rarity.Common, weight = 55, price = 300, alwaysForSale = true },
                    new ProgressionDefinition.RuneOffer { rarity = Rarity.Rare, weight = 32, price = 800 },
                    new ProgressionDefinition.RuneOffer { rarity = Rarity.Epic, weight = 11, price = 2000 },
                    new ProgressionDefinition.RuneOffer { rarity = Rarity.Legendary, weight = 2, price = 5000, karma = 200 },
                };
                p.shrineOfferCount = 4;
                p.shrineRefreshCost = 50;
                p.runeSellRefund = 5000;
                p.homeSealPrice = 7500;
                p.maxLegendaryRunes = 1;

                // Home villages: one per element. Its ninjas are twice as likely in the Dojo.
                p.villages = new List<HomeVillage>
                {
                    new HomeVillage
                    {
                        id = "emberfall", displayName = "Emberfall", element = fire, dojoAffinity = 20000,
                        description = "A forge town on a smouldering ridge. Its ninjas strike hard and leave their enemies burning.",
                    },
                    new HomeVillage
                    {
                        id = "mistwater", displayName = "Mistwater", element = water, dojoAffinity = 20000,
                        description = "A lake village wrapped in fog. Its ninjas heal one another and outlast their enemies.",
                    },
                    new HomeVillage
                    {
                        id = "stonehollow", displayName = "Stonehollow", element = earth, dojoAffinity = 20000,
                        description = "A fortress cut into the mountain. Its ninjas wear stone and hold the line.",
                    },
                };
            });
        }


        LineupDefinition Lineup(string id, string name, string description, params LineupSlot[] slots) =>
            sink.Provide<LineupDefinition>("Lineups", "lineup." + id, name, l => l.Configure(description, slots));

        RuneDefinition RuneOf(string id, string name, Rarity rarity, RuneFamily family, TraitDefinition element, Color color, string description,
            IEnumerable<RuneRank> ranks) =>
            sink.Provide<RuneDefinition>("Runes", "rune." + id, name, r => r.Configure(rarity, family, element, color, description, ranks));

        /// <summary>The value at each rank, evenly from rank I to rank V, to the nearest half.</summary>
        static float RankValue(float first, float last, int rank)
        {
            var value = first + (last - first) * rank / (RuneDefinition.MaxRanks - 1);
            return (float)(Math.Round(value * 2.0, MidpointRounding.AwayFromZero) / 2.0);
        }

        static List<RuneRank> Ranks(float first, float last, Func<float, RuneRank> rankOf)
        {
            var ranks = new List<RuneRank>();
            for (var i = 0; i < RuneDefinition.MaxRanks; i++) ranks.Add(rankOf(RankValue(first, last, i)));
            return ranks;
        }

        List<RuneRank> StatRanks(StatDefinition stat, ModifierOperation operation, float first, float last) =>
            Ranks(first, last, value => new RuneRank(Mod(stat, operation, value)));

        List<RuneRank> StatRanks(StatDefinition stat, ModifierOperation operation, float first, float last,
            StatDefinition stat2, ModifierOperation operation2, float first2, float last2)
        {
            var ranks = new List<RuneRank>();
            for (var i = 0; i < RuneDefinition.MaxRanks; i++)
                ranks.Add(new RuneRank(Mod(stat, operation, RankValue(first, last, i)), Mod(stat2, operation2, RankValue(first2, last2, i))));
            return ranks;
        }

        static List<RuneRank> EconomyRanks(int first, int last, Action<RuneRank, int> set)
        {
            var ranks = new List<RuneRank>();
            for (var i = 0; i < RuneDefinition.MaxRanks; i++)
            {
                var rank = new RuneRank();
                set(rank, (int)Math.Round(RankValue(first / 100f, last / 100f, i) * 100f));
                ranks.Add(rank);
            }

            return ranks;
        }

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
