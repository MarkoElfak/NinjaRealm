using System;
using System.Collections.Generic;
using NinjaRealm.Core.Content;
using NinjaRealm.Core.Definitions;
using NinjaRealm.Core.Definitions.Effects;
using UnityEditor;
using UnityEngine;

namespace NinjaRealm.Editor
{
    /// <summary>
    /// Creates the combat prototype's content assets from the design doc: stats, 3 elements,
    /// 4 classes, 9 skills, 9 ninjas, 6 components and 4 combined weapons.
    /// It only creates assets that don't exist yet, so it never overwrites edits.
    /// Batchmode: -executeMethod NinjaRealm.Editor.PrototypeContentSeeder.CreatePrototypeContent
    /// </summary>
    public static class PrototypeContentSeeder
    {
        const string Root = "Assets/_Project/Content";

        static int createdCount;

        [MenuItem("Ninja Realm/Content/Create Prototype Content")]
        public static void CreatePrototypeContent()
        {
            createdCount = 0;
            Seed();
            AssetDatabase.SaveAssets();
            Debug.Log($"[Content] Prototype content: created {createdCount} new assets.");
            ContentDatabaseMenu.Rebuild();
        }

        static void Seed()
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
                new TraitTier(2, "Fire ninjas' attacks burn for 3% max health over 3s.",
                    OnHit(EffectScope.TraitMembers, Status(Target(TargetKind.CurrentTarget), StatusKind.Burn, 3f, 3f))),
                new TraitTier(3, "Burn for 6%, and burning enemies receive 30% less healing.",
                    OnHit(EffectScope.TraitMembers,
                        Status(Target(TargetKind.CurrentTarget), StatusKind.Burn, 6f, 3f),
                        Status(Target(TargetKind.CurrentTarget), StatusKind.HealingReduction, 30f, 3f))));

            var water = Trait("water", "Water", TraitKind.Element, new Color(0.18f, 0.55f, 0.92f),
                new TraitTier(2, "Every 4s, heal the most injured ally for 8% max health.",
                    Periodic(4f, EffectScope.Team, new HealAction(Target(TargetKind.LowestHealthAllies), 8f))),
                new TraitTier(3, "Heal for 14% instead.",
                    Periodic(4f, EffectScope.Team, new HealAction(Target(TargetKind.LowestHealthAllies), 14f))));

            var earth = Trait("earth", "Earth", TraitKind.Element, new Color(0.55f, 0.42f, 0.28f),
                new TraitTier(2, "Earth ninjas gain 20 armor.",
                    new StatModifierEffect(armor, ModifierOperation.Add, 20f, EffectScope.TraitMembers)),
                new TraitTier(3, "Earth ninjas gain 40 armor, and all other allies gain 10.",
                    new StatModifierEffect(armor, ModifierOperation.Add, 40f, EffectScope.TraitMembers),
                    new StatModifierEffect(armor, ModifierOperation.Add, 10f, EffectScope.OtherAllies)));

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
            var tideBlessing = Skill("tide_blessing", "Tide Blessing", "Heals the 2 most injured allies for 20% max health.",
                new HealAction(Target(TargetKind.LowestHealthAllies, count: 2), 20f));
            var stoneWall = Skill("stone_wall", "Stone Wall", "Taunts enemies within 2 hexes for 2s and takes 25% less damage for 4s.",
                Status(Target(TargetKind.EnemiesInRadiusOfSelf, radius: 2), StatusKind.Taunt, 0f, 2f),
                Status(Target(TargetKind.Self), StatusKind.DamageReduction, 25f, 4f));
            var quakeHammer = Skill("quake_hammer", "Quake Hammer", "Deals 220% attack damage and stuns the target for 1.5s.",
                new DamageAction(Target(TargetKind.CurrentTarget), DamageType.Physical, 0f, 220f, false),
                Status(Target(TargetKind.CurrentTarget), StatusKind.Stun, 0f, 1.5f));
            var fireLotus = Skill("fire_lotus", "Fire Lotus", "Deals 200 magic damage around the target and sets enemies burning.",
                new DamageAction(Target(TargetKind.EnemiesInRadiusOfTarget, radius: 1), DamageType.Magic, 200f, 0f, true),
                Status(Target(TargetKind.EnemiesInRadiusOfTarget, radius: 1), StatusKind.Burn, 4f, 3f));
            var riptideGuard = Skill("riptide_guard", "Riptide Guard", "Shields self for 30% max health and slows adjacent enemies.",
                new ShieldAction(Target(TargetKind.Self), 30f, 4f),
                Status(Target(TargetKind.AdjacentEnemies), StatusKind.Slow, 30f, 2f));
            var stoneskin = Skill("stoneskin", "Stoneskin", "Shields the most injured ally for 25% of their max health.",
                new ShieldAction(Target(TargetKind.LowestHealthAllies), 25f, 4f));
            var emberArmor = Skill("ember_armor", "Ember Armor", "Gains 30 armor and burns adjacent enemies for 4s.",
                new StatBuffAction(Target(TargetKind.Self), armor, ModifierOperation.Add, 30f, 4f),
                Status(Target(TargetKind.AdjacentEnemies), StatusKind.Burn, 4f, 4f));
            var whirlpool = Skill("whirlpool", "Whirlpool", "Deals 150 magic damage around the target and stuns them for 2s.",
                new DamageAction(Target(TargetKind.EnemiesInRadiusOfTarget, radius: 1), DamageType.Magic, 150f, 0f, true),
                Status(Target(TargetKind.EnemiesInRadiusOfTarget, radius: 1), StatusKind.Stun, 0f, 2f));

            // Ninjas: base stats come from the class, scaled by rarity.
            StatValue[] ClassStats(TraitDefinition cls, Rarity rarity)
            {
                var scale = rarity == Rarity.Common ? 1f : rarity == Rarity.Rare ? 1.1f : rarity == Rarity.Epic ? 1.2f : 1.3f;
                float hp, ad, aspd, rng, arm, mr, mana, start;
                if (cls == vanguard) { hp = 750; ad = 50; aspd = 0.6f; rng = 1; arm = 40; mr = 40; mana = 80; start = 30; }
                else if (cls == blademaster) { hp = 650; ad = 65; aspd = 0.75f; rng = 1; arm = 30; mr = 30; mana = 60; start = 0; }
                else if (cls == medic) { hp = 550; ad = 40; aspd = 0.65f; rng = 3; arm = 20; mr = 25; mana = 70; start = 20; }
                else { hp = 500; ad = 40; aspd = 0.65f; rng = 4; arm = 20; mr = 20; mana = 60; start = 10; }

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

            void Ninja(string id, string name, Rarity rarity, TraitDefinition element, TraitDefinition cls, SkillDefinition skill) =>
                CreateOrLoad<NinjaDefinition>("Ninjas", "ninja." + id, name,
                    n => n.Configure(rarity, element, cls, skill, ClassStats(cls, rarity)));

            Ninja("kaito", "Kaito", Rarity.Common, fire, blademaster, flameSlash);
            Ninja("hana", "Hana", Rarity.Common, water, medic, tideBlessing);
            Ninja("daichi", "Daichi", Rarity.Common, earth, vanguard, stoneWall);
            Ninja("goro", "Goro", Rarity.Rare, earth, blademaster, quakeHammer);
            Ninja("mei", "Mei", Rarity.Rare, fire, mystic, fireLotus);
            Ninja("takeshi", "Takeshi", Rarity.Rare, water, vanguard, riptideGuard);
            Ninja("saya", "Saya", Rarity.Rare, earth, medic, stoneskin);
            Ninja("jiro", "Jiro", Rarity.Epic, fire, vanguard, emberArmor);
            Ninja("nami", "Nami", Rarity.Epic, water, mystic, whirlpool);

            // Weapon components
            var kunai = Component("kunai", "Kunai", "+10% attack damage.", Mod(attackDamage, ModifierOperation.AddPercent, 10f));
            var prayerBeads = Component("prayer_beads", "Prayer Beads", "+15 ability power.", Mod(abilityPower, ModifierOperation.Add, 15f));
            Component("iron_plate", "Iron Plate", "+20 armor.", Mod(armor, ModifierOperation.Add, 20f));
            var swiftSandals = Component("swift_sandals", "Swift Sandals", "+10% attack speed.", Mod(attackSpeed, ModifierOperation.AddPercent, 10f));
            Component("chakra_scroll", "Chakra Scroll", "+15 starting mana.", Mod(startMana, ModifierOperation.Add, 15f));
            Component("jade_charm", "Jade Charm", "+10% max health.", Mod(health, ModifierOperation.AddPercent, 10f));

            // Combined weapons
            Combined("shadow_katana", "Shadow Katana", "Crits deal +30% damage.", kunai, kunai, Mod(critDamage, ModifierOperation.Add, 30f));
            Combined("kusarigama", "Kusarigama", "Heals for 15% of all damage dealt.", kunai, prayerBeads, Mod(omnivamp, ModifierOperation.Add, 15f));
            Combined("dragon_fan", "Dragon Fan", "Skills deal +20% damage.", prayerBeads, prayerBeads, Mod(abilityDamage, ModifierOperation.Add, 20f));
            Combined("wind_boots", "Wind Boots", "+10% dodge.", swiftSandals, swiftSandals, Mod(dodge, ModifierOperation.Add, 10f));

        }

        // ---- Builders ----

        static StatDefinition Stat(string id, string name, float defaultValue, bool isPercentage, string description) =>
            CreateOrLoad<StatDefinition>("Stats", "stat." + id, name, s => s.Configure(defaultValue, isPercentage, description));

        static TraitDefinition Trait(string id, string name, TraitKind kind, Color color, params TraitTier[] tiers) =>
            CreateOrLoad<TraitDefinition>(kind == TraitKind.Element ? "Traits/Elements" : "Traits/Classes",
                $"trait.{(kind == TraitKind.Element ? "element" : "class")}.{id}", name, t => t.Configure(kind, color, tiers));

        static SkillDefinition Skill(string id, string name, string description, params ActionEffect[] actions) =>
            CreateOrLoad<SkillDefinition>("Skills", "skill." + id, name, s => s.Configure(description, 0.5f, actions));

        static WeaponDefinition Component(string id, string name, string description, params PassiveEffect[] effects) =>
            CreateOrLoad<WeaponDefinition>("Weapons/Components", "weapon." + id, name,
                w => w.Configure(WeaponTier.Component, description, null, null, effects));

        static WeaponDefinition Combined(string id, string name, string description, WeaponDefinition a, WeaponDefinition b, params PassiveEffect[] effects) =>
            CreateOrLoad<WeaponDefinition>("Weapons/Combined", "weapon." + id, name,
                w => w.Configure(WeaponTier.Combined, description, a, b, effects));

        static StatModifierEffect Mod(StatDefinition stat, ModifierOperation operation, float value) =>
            new StatModifierEffect(stat, operation, value);

        static TargetSelector Target(TargetKind kind, int count = 1, int radius = 1) => new TargetSelector(kind, count, radius);

        static ApplyStatusAction Status(TargetSelector target, StatusKind status, float value, float duration) =>
            new ApplyStatusAction(target, status, value, duration);

        static TriggeredEffect OnHit(EffectScope holders, params ActionEffect[] actions) =>
            new TriggeredEffect(EffectTrigger.OnAttackHit, holders, actions);

        static TriggeredEffect AtStart(EffectScope holders, params ActionEffect[] actions) =>
            new TriggeredEffect(EffectTrigger.FightStart, holders, actions) { oncePerFight = true };

        static TriggeredEffect Periodic(float interval, EffectScope holders, params ActionEffect[] actions) =>
            new TriggeredEffect(EffectTrigger.Periodic, holders, actions) { intervalSeconds = interval };

        static T CreateOrLoad<T>(string folder, string id, string displayName, Action<T> configure) where T : ContentDefinition
        {
            var directory = $"{Root}/{folder}";
            var path = $"{directory}/{displayName.Replace(" ", string.Empty)}.asset";

            var existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing != null) return existing;

            ContentDatabaseMenu.EnsureFolder(directory);

            var asset = ScriptableObject.CreateInstance<T>();
            asset.SetId(id);
            asset.SetDisplayName(displayName);
            configure(asset);
            AssetDatabase.CreateAsset(asset, path);
            createdCount++;
            return asset;
        }
    }
}
