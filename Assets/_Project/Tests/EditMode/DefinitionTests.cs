using System.Collections.Generic;
using System.Linq;
using NinjaRealm.Core.Content;
using NinjaRealm.Core.Definitions;
using NinjaRealm.Core.Definitions.Effects;
using NUnit.Framework;
using UnityEngine;

namespace NinjaRealm.Tests
{
    public class DefinitionTests
    {
        readonly List<Object> created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (var obj in created)
                if (obj != null) Object.DestroyImmediate(obj);
            created.Clear();
        }

        T Make<T>(string id) where T : ContentDefinition
        {
            var definition = ScriptableObject.CreateInstance<T>();
            definition.name = id;
            definition.SetId(id);
            created.Add(definition);
            return definition;
        }

        StatDefinition Stat(string id, float defaultValue = 0f)
        {
            var stat = Make<StatDefinition>("stat." + id);
            stat.Configure(defaultValue, false, "");
            return stat;
        }

        TraitDefinition Trait(string id, TraitKind kind, params TraitTier[] tiers)
        {
            var trait = Make<TraitDefinition>("trait." + id);
            trait.Configure(kind, Color.white, tiers);
            return trait;
        }

        StatModifierEffect ArmorFor(EffectScope holders) =>
            new StatModifierEffect(Stat("armor"), ModifierOperation.Add, 20f, holders);

        SkillDefinition ValidSkill()
        {
            var skill = Make<SkillDefinition>("skill.test");
            skill.Configure("", 0.5f, new HealAction(new TargetSelector(TargetKind.Self), 10f));
            return skill;
        }

        static List<string> Issues(ContentDefinition definition) => definition.ValidateSelf().ToList();

        // ---- Traits ----

        [Test]
        public void Trait_GetActiveTier_PicksHighestReachedTier()
        {
            var two = new TraitTier(2, "", ArmorFor(EffectScope.TraitMembers));
            var three = new TraitTier(3, "", ArmorFor(EffectScope.TraitMembers));
            var trait = Trait("element.earth", TraitKind.Element, two, three);

            Assert.IsNull(trait.GetActiveTier(1));
            Assert.AreSame(two, trait.GetActiveTier(2));
            Assert.AreSame(three, trait.GetActiveTier(3));
            Assert.AreSame(three, trait.GetActiveTier(7));
        }

        [Test]
        public void Trait_ValidTiers_HaveNoIssues()
        {
            var trait = Trait("element.earth", TraitKind.Element,
                new TraitTier(2, "", ArmorFor(EffectScope.TraitMembers)),
                new TraitTier(3, "", ArmorFor(EffectScope.AllAllies)));

            CollectionAssert.IsEmpty(Issues(trait));
        }

        [Test]
        public void Trait_ReportsMissingTiers()
        {
            Assert.That(Issues(Trait("element.empty", TraitKind.Element)), Has.Some.Contains("no tiers"));
        }

        [Test]
        public void Trait_ReportsTiersOutOfOrderEmptyEffectsAndSelfHolders()
        {
            var trait = Trait("element.bad", TraitKind.Element,
                new TraitTier(3, "", ArmorFor(EffectScope.TraitMembers)),
                new TraitTier(2, "", ArmorFor(EffectScope.Self)),
                new TraitTier(4, "", (PassiveEffect)null));

            var issues = Issues(trait);
            Assert.That(issues, Has.Some.Contains("needs more ninjas than the tier before it"));
            Assert.That(issues, Has.Some.Contains("held by Self"));
            Assert.That(issues, Has.Some.Contains("is empty"));
        }

        // ---- Ninjas ----

        [Test]
        public void Ninja_ValidSetup_HasNoIssuesAndListsTraitsInOrder()
        {
            var element = Trait("element.fire", TraitKind.Element, new TraitTier(2, "", ArmorFor(EffectScope.TraitMembers)));
            var cls = Trait("class.vanguard", TraitKind.Class, new TraitTier(2, "", ArmorFor(EffectScope.TraitMembers)));
            var ninja = Make<NinjaDefinition>("ninja.kaito");
            ninja.Configure(Rarity.Common, element, cls, ValidSkill(), new[] { new StatValue(Stat("health"), 650f) });

            CollectionAssert.IsEmpty(Issues(ninja));
            CollectionAssert.AreEqual(new[] { element, cls }, ninja.Traits.ToArray());
        }

        [Test]
        public void Ninja_GetBaseStat_FallsBackToStatDefault()
        {
            var health = Stat("health", 500f);
            var armor = Stat("armor", 20f);
            var ninja = Make<NinjaDefinition>("ninja.kaito");
            ninja.Configure(Rarity.Common, null, null, null, new[] { new StatValue(health, 650f) });

            Assert.AreEqual(650f, ninja.GetBaseStat(health));
            Assert.AreEqual(20f, ninja.GetBaseStat(armor));
        }

        [Test]
        public void Ninja_ReportsSwappedTraitKindsMissingSkillAndDuplicateStats()
        {
            var element = Trait("element.fire", TraitKind.Element, new TraitTier(2, "", ArmorFor(EffectScope.TraitMembers)));
            var cls = Trait("class.vanguard", TraitKind.Class, new TraitTier(2, "", ArmorFor(EffectScope.TraitMembers)));
            var health = Stat("health");
            var ninja = Make<NinjaDefinition>("ninja.broken");
            ninja.Configure(Rarity.Common, cls, element, null, new[] { new StatValue(health, 1f), new StatValue(health, 2f) });

            var issues = Issues(ninja);
            Assert.That(issues, Has.Some.Contains("not an Element"));
            Assert.That(issues, Has.Some.Contains("not a Class"));
            Assert.That(issues, Has.Some.Contains("has no skill"));
            Assert.That(issues, Has.Some.Contains("more than once"));
        }

        // ---- Weapons ----

        WeaponDefinition Component(string id)
        {
            var weapon = Make<WeaponDefinition>("weapon." + id);
            weapon.Configure(WeaponTier.Component, "", null, null, ArmorFor(EffectScope.Self));
            return weapon;
        }

        [Test]
        public void Weapon_Combined_GetsBothComponentsEffectsPlusItsOwn()
        {
            var a = Component("kunai");
            var b = Component("iron_plate");
            var combined = Make<WeaponDefinition>("weapon.combo");
            combined.Configure(WeaponTier.Combined, "", a, b, ArmorFor(EffectScope.Self));

            CollectionAssert.IsEmpty(Issues(combined));
            Assert.AreEqual(3, combined.AllEffects().Count());
            Assert.AreEqual(1, a.AllEffects().Count());
        }

        [Test]
        public void Weapon_ReportsMissingComponentNestedCombinedAndNonSelfEffects()
        {
            var inner = Make<WeaponDefinition>("weapon.inner");
            inner.Configure(WeaponTier.Combined, "", Component("a"), Component("b"), ArmorFor(EffectScope.Self));
            var broken = Make<WeaponDefinition>("weapon.broken");
            broken.Configure(WeaponTier.Combined, "", inner, null, ArmorFor(EffectScope.AllAllies));

            var issues = Issues(broken);
            Assert.That(issues, Has.Some.Contains("missing one of its two components"));
            Assert.That(issues, Has.Some.Contains("not a Component"));
            Assert.That(issues, Has.Some.Contains("not held by Self"));
        }

        [Test]
        public void Weapon_ComponentWithComponents_IsReported()
        {
            var weapon = Make<WeaponDefinition>("weapon.odd");
            weapon.Configure(WeaponTier.Component, "", Component("a"), null, ArmorFor(EffectScope.Self));

            Assert.That(Issues(weapon), Has.Some.Contains("Only Combined weapons"));
        }

        // ---- Skills and effects ----

        [Test]
        public void Skill_ReportsNoActionsAndBadActions()
        {
            var empty = Make<SkillDefinition>("skill.empty");
            empty.Configure("", 0.5f);
            Assert.That(Issues(empty), Has.Some.Contains("no actions"));

            var bad = Make<SkillDefinition>("skill.bad");
            bad.Configure("", 0.5f,
                new DamageAction(new TargetSelector(TargetKind.CurrentTarget), DamageType.Physical, 0f, 0f, false),
                new ApplyStatusAction(new TargetSelector(TargetKind.CurrentTarget), StatusKind.Burn, 0f, 3f),
                null);

            var issues = Issues(bad);
            Assert.That(issues, Has.Some.Contains("deals no damage"));
            Assert.That(issues, Has.Some.Contains("Burn with a value of 0"));
            Assert.That(issues, Has.Some.Contains("is empty"));
        }

        [Test]
        public void StatusWithoutValue_IsFineForStunAndTaunt()
        {
            var stun = new ApplyStatusAction(new TargetSelector(TargetKind.CurrentTarget), StatusKind.Stun, 0f, 1.5f);
            CollectionAssert.IsEmpty(stun.Validate().ToList());
        }

        [Test]
        public void TriggeredEffect_ReportsZeroIntervalAndNoActions()
        {
            var effect = new TriggeredEffect(EffectTrigger.Periodic, EffectScope.Team) { intervalSeconds = 0f };
            var issues = effect.Validate().ToList();

            Assert.That(issues, Has.Some.Contains("interval is 0"));
            Assert.That(issues, Has.Some.Contains("no actions"));
        }

        [Test]
        public void StatModifier_ReportsMissingStatAndZeroValue()
        {
            var issues = new StatModifierEffect(null, ModifierOperation.Add, 0f).Validate().ToList();

            Assert.That(issues, Has.Some.Contains("no stat"));
            Assert.That(issues, Has.Some.Contains("value of 0"));
        }

        [Test]
        public void TargetSelector_ReportsBadRadius()
        {
            var issues = new TargetSelector(TargetKind.EnemiesInRadiusOfSelf, radius: 0).Validate().ToList();
            Assert.That(issues, Has.Some.Contains("radius below 1"));
        }

        [Test]
        public void Validation_RejectsWrongIdCategory()
        {
            var ninja = Make<NinjaDefinition>("weapon.kaito");
            var issues = ContentValidation.Validate(new ContentDefinition[] { ninja });

            Assert.That(issues.Select(issue => issue.Message), Has.Some.Contains("must start with 'ninja.'"));
        }
    }
}
