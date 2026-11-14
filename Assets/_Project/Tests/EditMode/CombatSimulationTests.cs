using System.Collections.Generic;
using System.Linq;
using NinjaRealm.Combat;
using NUnit.Framework;

namespace NinjaRealm.Tests
{
    /// <summary>Pure C# tests of the fight simulation, with hand-built units (no content assets).</summary>
    public class CombatSimulationTests
    {
        const ulong Seed = 42;

        // Two adjacent hexes in the middle of the board, one per team.
        static readonly HexCoord Front0 = new HexCoord(3, 3);
        static readonly HexCoord Front1 = new HexCoord(3, 4);

        static CombatUnitSpec Unit(int team, HexCoord position, string id = null)
        {
            var spec = new CombatUnitSpec { DefinitionId = id ?? $"ninja.test{team}", Team = team, Position = position };
            spec.Stats[StatIds.Health] = 500 * Fixed.One;
            spec.Stats[StatIds.AttackDamage] = 50 * Fixed.One;
            spec.Stats[StatIds.AttackSpeed] = 1 * Fixed.One;
            spec.Stats[StatIds.Range] = 1 * Fixed.One;
            spec.Stats[StatIds.AbilityPower] = 100 * Fixed.One;
            spec.Stats[StatIds.CritDamage] = 150 * Fixed.One;
            spec.Stats[StatIds.MoveSpeed] = 2 * Fixed.One;
            return spec;
        }

        static FightSetup Setup(params CombatUnitSpec[] units)
        {
            var setup = new FightSetup();
            setup.Units.AddRange(units);
            return setup;
        }

        static List<CombatEvent> EventsOf(CombatSimulation simulation, CombatEventType type) =>
            simulation.Events.Where(e => e.Type == type).ToList();

        [Test]
        public void SameSetupAndSeed_GiveIdenticalFights()
        {
            CombatSimulation Make()
            {
                var a = Unit(0, new HexCoord(1, 0));
                a.Stats[StatIds.CritChance] = 25 * Fixed.One;
                var b = Unit(0, new HexCoord(5, 1));
                b.Stats[StatIds.Dodge] = 20 * Fixed.One;
                var c = Unit(1, new HexCoord(2, 7));
                c.Stats[StatIds.CritChance] = 50 * Fixed.One;
                var d = Unit(1, new HexCoord(4, 6));
                d.Stats[StatIds.Range] = 3 * Fixed.One;
                return new CombatSimulation(Setup(a, b, c, d), Seed);
            }

            var first = Make();
            var second = Make();
            first.Run();
            second.Run();

            CollectionAssert.AreEqual(first.Events.Select(e => e.ToString()).ToList(), second.Events.Select(e => e.ToString()).ToList());
            Assert.AreEqual(first.Result.WinningTeam, second.Result.WinningTeam);
            Assert.AreEqual(first.Result.DurationTicks, second.Result.DurationTicks);
        }

        [Test]
        public void StrongerUnit_WinsBeforeTimeout()
        {
            var strong = Unit(0, Front0);
            strong.Stats[StatIds.AttackDamage] = 120 * Fixed.One;
            var weak = Unit(1, Front1);

            var result = new CombatSimulation(Setup(strong, weak), Seed).Run();

            Assert.AreEqual(0, result.WinningTeam);
            Assert.IsFalse(result.DecidedByTimeout);
            Assert.IsTrue(result.Units[0].Survived);
            Assert.IsFalse(result.Units[1].Survived);
        }

        [Test]
        public void Units_WalkTowardEachOtherAndFight()
        {
            var simulation = new CombatSimulation(Setup(Unit(0, new HexCoord(3, 0)), Unit(1, new HexCoord(3, 7))), Seed);
            var result = simulation.Run();

            Assert.IsNotEmpty(EventsOf(simulation, CombatEventType.Move));
            Assert.IsNotEmpty(EventsOf(simulation, CombatEventType.Attack));
            Assert.AreNotEqual(-1, result.WinningTeam);
        }

        [Test]
        public void Armor_ReducesPhysicalDamage()
        {
            // 100 damage against 100 armor: 100 x 100 / (100 + 100) = 50.
            var attacker = Unit(0, Front0);
            attacker.Stats[StatIds.AttackDamage] = 100 * Fixed.One;
            var defender = Unit(1, Front1);
            defender.Stats[StatIds.Armor] = 100 * Fixed.One;

            var simulation = new CombatSimulation(Setup(attacker, defender), Seed);
            simulation.Step();

            var hit = EventsOf(simulation, CombatEventType.Damage).First(e => e.Source == 0);
            Assert.AreEqual(50 * Fixed.One, hit.Amount);
        }

        [Test]
        public void Crit_DealsCritDamage()
        {
            var attacker = Unit(0, Front0);
            attacker.Stats[StatIds.AttackDamage] = 100 * Fixed.One;
            attacker.Stats[StatIds.CritChance] = 100 * Fixed.One;
            var defender = Unit(1, Front1);

            var simulation = new CombatSimulation(Setup(attacker, defender), Seed);
            simulation.Step();

            var hit = EventsOf(simulation, CombatEventType.Damage).First(e => e.Source == 0);
            Assert.AreEqual(150 * Fixed.One, hit.Amount);
            Assert.AreEqual(1, hit.Detail);
        }

        [Test]
        public void FullDodge_AvoidsEveryAttack()
        {
            var attacker = Unit(0, Front0);
            var defender = Unit(1, Front1);
            defender.Stats[StatIds.Dodge] = 100 * Fixed.One;
            defender.Stats[StatIds.AttackDamage] = 0;

            var simulation = new CombatSimulation(Setup(attacker, defender), Seed);
            for (var i = 0; i < 100; i++) simulation.Step();

            Assert.IsNotEmpty(EventsOf(simulation, CombatEventType.Dodge));
            Assert.AreEqual(simulation.Units[1].MaxHealth, simulation.Units[1].Health);
        }

        [Test]
        public void Trait_ActivatesAtRequiredCount_CountingEachNinjaOnce()
        {
            var trait = new TraitSpec { Id = "trait.test" };
            var tier = new TraitTierSpec { RequiredCount = 2 };
            tier.Effects.Add(new StatModifierSpec
            {
                Holders = EffectScope.TraitMembers,
                StatId = StatIds.Armor,
                Operation = ModifierOperation.Add,
                Value = 40 * Fixed.One,
            });
            trait.Tiers.Add(tier);

            // Team 0: two different ninjas with the trait. Team 1: two copies of the same ninja.
            var a = Unit(0, new HexCoord(1, 0), "ninja.a");
            var b = Unit(0, new HexCoord(2, 0), "ninja.b");
            var c = Unit(1, new HexCoord(1, 7), "ninja.c");
            var d = Unit(1, new HexCoord(2, 7), "ninja.c");
            foreach (var unit in new[] { a, b, c, d }) unit.TraitIds.Add(trait.Id);

            var setup = Setup(a, b, c, d);
            setup.Traits.Add(trait);
            var simulation = new CombatSimulation(setup, Seed);

            Assert.AreEqual(40 * Fixed.One, simulation.Units[0].GetStat(StatIds.Armor));
            Assert.AreEqual(40 * Fixed.One, simulation.Units[1].GetStat(StatIds.Armor));
            Assert.AreEqual(0, simulation.Units[2].GetStat(StatIds.Armor));
            Assert.AreEqual(0, simulation.Units[3].GetStat(StatIds.Armor));

            var activations = EventsOf(simulation, CombatEventType.TraitActivated);
            Assert.AreEqual(1, activations.Count);
            Assert.AreEqual(0, activations[0].Amount);
            Assert.AreEqual("trait.test", activations[0].Id);
        }

        [Test]
        public void PercentHealthBonus_RaisesMaxHealth()
        {
            var unit = Unit(0, Front0);
            unit.Passives.Add(new StatModifierSpec
            {
                Holders = EffectScope.Self,
                StatId = StatIds.Health,
                Operation = ModifierOperation.AddPercent,
                Value = 10 * Fixed.One,
            });

            var simulation = new CombatSimulation(Setup(unit, Unit(1, Front1)), Seed);
            Assert.AreEqual(550 * Fixed.One, simulation.Units[0].MaxHealth);
            Assert.AreEqual(550 * Fixed.One, simulation.Units[0].Health);
        }

        [Test]
        public void Shield_AbsorbsDamageBeforeHealth()
        {
            var attacker = Unit(0, Front0);
            attacker.Stats[StatIds.AttackDamage] = 100 * Fixed.One;
            var defender = Unit(1, Front1);
            defender.Stats[StatIds.AttackDamage] = 0;
            var shield = new TriggeredSpec { Holders = EffectScope.Self, Trigger = EffectTrigger.FightStart };
            shield.Actions.Add(new ShieldSpec
            {
                Target = new TargetSpec(TargetKind.Self),
                PercentOfMaxHealth = 30 * Fixed.One,
                DurationTicks = 10 * Fixed.TicksPerSecond,
            });
            defender.Passives.Add(shield);

            var simulation = new CombatSimulation(Setup(attacker, defender), Seed);
            Assert.AreEqual(150 * Fixed.One, simulation.Units[1].ShieldTotal);

            simulation.Step();

            Assert.AreEqual(50 * Fixed.One, simulation.Units[1].ShieldTotal);
            Assert.AreEqual(500 * Fixed.One, simulation.Units[1].Health);
        }

        [Test]
        public void Stun_StopsAttacksForItsDuration()
        {
            var stunner = Unit(0, Front0);
            stunner.Stats[StatIds.AttackDamage] = 1 * Fixed.One;
            var onHit = new TriggeredSpec { Holders = EffectScope.Self, Trigger = EffectTrigger.OnAttackHit, OncePerFight = true };
            onHit.Actions.Add(new StatusSpec
            {
                Target = new TargetSpec(TargetKind.CurrentTarget),
                Status = StatusKind.Stun,
                DurationTicks = 3 * Fixed.TicksPerSecond,
            });
            stunner.Passives.Add(onHit);

            // The target acts after the stunner, so it is stunned before its first attack.
            var target = Unit(1, Front1);
            var simulation = new CombatSimulation(Setup(stunner, target), Seed);
            for (var i = 0; i < 3 * Fixed.TicksPerSecond; i++) simulation.Step();

            Assert.IsEmpty(simulation.Events.Where(e => e.Type == CombatEventType.Attack && e.Source == 1));

            for (var i = 0; i < 5; i++) simulation.Step();
            Assert.IsNotEmpty(simulation.Events.Where(e => e.Type == CombatEventType.Attack && e.Source == 1));
        }

        [Test]
        public void FullMana_CastsSkill()
        {
            var caster = Unit(0, Front0);
            caster.Stats[StatIds.MaxMana] = 20 * Fixed.One;
            caster.Stats[StatIds.AttackDamage] = 10 * Fixed.One;
            caster.Skill = new SkillSpec { Id = "skill.test", CastTicks = 10 };
            caster.Skill.Actions.Add(new DamageSpec
            {
                Target = new TargetSpec(TargetKind.CurrentTarget),
                DamageType = DamageType.True,
                BaseDamage = 200 * Fixed.One,
            });
            var target = Unit(1, Front1);
            target.Stats[StatIds.AttackDamage] = 0;

            // Two attacks (10 mana each) fill the 20 mana by tick 21; the cast lands 10 ticks later.
            var simulation = new CombatSimulation(Setup(caster, target), Seed);
            for (var i = 0; i < 50; i++) simulation.Step();

            var cast = EventsOf(simulation, CombatEventType.Cast).Single();
            Assert.AreEqual("skill.test", cast.Id);
            Assert.IsTrue(simulation.Events.Any(e => e.Type == CombatEventType.Damage && e.Source == 0 && e.Amount == 200 * Fixed.One));
            Assert.AreEqual(1, EventsOf(simulation, CombatEventType.CastStart).Count);
        }

        [Test]
        public void Burn_DealsTrueDamageOverTime()
        {
            var burner = Unit(0, Front0);
            burner.Stats[StatIds.AttackDamage] = 0;
            var atStart = new TriggeredSpec { Holders = EffectScope.Self, Trigger = EffectTrigger.FightStart };
            atStart.Actions.Add(new StatusSpec
            {
                Target = new TargetSpec(TargetKind.RandomEnemies, 1),
                Status = StatusKind.Burn,
                Value = 20 * Fixed.One,
                DurationTicks = 2 * Fixed.TicksPerSecond,
            });
            burner.Passives.Add(atStart);
            var target = Unit(1, Front1);
            target.Stats[StatIds.AttackDamage] = 0;
            target.Stats[StatIds.Armor] = 1000 * Fixed.One;

            var simulation = new CombatSimulation(Setup(burner, target), Seed);
            for (var i = 0; i < 3 * Fixed.TicksPerSecond; i++) simulation.Step();

            // 20% of 500 over 2 seconds, ignoring armor, marked as damage over time.
            Assert.AreEqual(400 * Fixed.One, simulation.Units[1].Health);
            Assert.IsTrue(EventsOf(simulation, CombatEventType.Damage).All(e => e.Detail == 2));
        }

        [Test]
        public void Heal_TargetsLowestHealthAllyAndCapsAtMax()
        {
            var healer = Unit(0, new HexCoord(0, 0));
            healer.Stats[StatIds.AttackDamage] = 0;
            var periodic = new TriggeredSpec { Holders = EffectScope.Self, Trigger = EffectTrigger.Periodic, IntervalTicks = 1 };
            periodic.Actions.Add(new HealSpec { Target = new TargetSpec(TargetKind.LowestHealthAllies, 1), PercentOfMaxHealth = 10 * Fixed.One });
            healer.Passives.Add(periodic);
            var tank = Unit(0, Front0);
            tank.Stats[StatIds.AttackDamage] = 0;
            var enemy = Unit(1, Front1);
            enemy.Stats[StatIds.AttackDamage] = 60 * Fixed.One;

            var simulation = new CombatSimulation(Setup(healer, tank, enemy), Seed);
            for (var i = 0; i < 20; i++) simulation.Step();

            var heals = EventsOf(simulation, CombatEventType.Heal);
            Assert.IsNotEmpty(heals);
            Assert.IsTrue(heals.All(e => e.Target == 1), "only the damaged ally is healed");
            Assert.AreEqual(simulation.Units[1].MaxHealth, simulation.Units[1].Health);
        }

        [Test]
        public void Taunt_ForcesEnemiesToAttackTheTaunter()
        {
            var taunter = Unit(0, new HexCoord(2, 3));
            taunter.Stats[StatIds.AttackDamage] = 0;
            var atStart = new TriggeredSpec { Holders = EffectScope.Self, Trigger = EffectTrigger.FightStart };
            atStart.Actions.Add(new StatusSpec
            {
                Target = new TargetSpec(TargetKind.EnemiesInRadiusOfSelf, 1, 3),
                Status = StatusKind.Taunt,
                DurationTicks = 2 * Fixed.TicksPerSecond,
            });
            taunter.Passives.Add(atStart);
            var ally = Unit(0, Front0);
            ally.Stats[StatIds.AttackDamage] = 0;
            var enemy = Unit(1, Front1);

            var simulation = new CombatSimulation(Setup(taunter, ally, enemy), Seed);
            for (var i = 0; i < 30; i++) simulation.Step();

            var enemyAttacks = simulation.Events.Where(e => e.Type == CombatEventType.Attack && e.Source == 2).ToList();
            Assert.IsNotEmpty(enemyAttacks);
            Assert.IsTrue(enemyAttacks.All(e => e.Target == 0));
        }

        [Test]
        public void Timeout_WithEqualHealth_IsADraw()
        {
            var a = Unit(0, Front0);
            a.Stats[StatIds.AttackDamage] = 0;
            var b = Unit(1, Front1);
            b.Stats[StatIds.AttackDamage] = 0;

            var setup = Setup(a, b);
            var result = new CombatSimulation(setup, Seed).Run();

            Assert.AreEqual(-1, result.WinningTeam);
            Assert.IsTrue(result.DecidedByTimeout);
            Assert.AreEqual(setup.MaxTicks, result.DurationTicks);
        }

        [Test]
        public void Timeout_GoesToTeamWithMoreHealthLeft()
        {
            var a = Unit(0, Front0);
            a.Stats[StatIds.AttackDamage] = 1 * Fixed.One;
            a.Stats[StatIds.Health] = 100000 * Fixed.One;
            var b = Unit(1, Front1);
            b.Stats[StatIds.AttackDamage] = 0;
            b.Stats[StatIds.Health] = 100000 * Fixed.One;

            var result = new CombatSimulation(Setup(a, b), Seed).Run();

            Assert.IsTrue(result.DecidedByTimeout);
            Assert.AreEqual(0, result.WinningTeam);
        }

        [Test]
        public void Overtime_IncreasesDamage()
        {
            var a = Unit(0, Front0);
            a.Stats[StatIds.AttackDamage] = 1 * Fixed.One;
            a.Stats[StatIds.Health] = 100000 * Fixed.One;
            var b = Unit(1, Front1);
            b.Stats[StatIds.AttackDamage] = 0;
            b.Stats[StatIds.Health] = 100000 * Fixed.One;

            var simulation = new CombatSimulation(Setup(a, b), Seed);
            simulation.Run();

            var hits = simulation.Events.Where(e => e.Type == CombatEventType.Damage && e.Source == 0).ToList();
            Assert.AreEqual(1 * Fixed.One, hits.First().Amount);
            Assert.Greater(hits.Last().Amount, 1 * Fixed.One);
            Assert.IsNotEmpty(EventsOf(simulation, CombatEventType.Overtime));
        }

        [Test]
        public void EmptyTeam_EndsImmediately()
        {
            var simulation = new CombatSimulation(Setup(Unit(0, Front0)), Seed);
            Assert.IsTrue(simulation.IsFinished);
            Assert.AreEqual(0, simulation.Result.WinningTeam);
            Assert.AreEqual(0, simulation.Result.DurationTicks);
        }
    }
}
