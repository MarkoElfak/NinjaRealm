using System;
using System.Collections.Generic;

namespace NinjaRealm.Combat
{
    /// <summary>
    /// Runs one auto battle as a fixed-step simulation (20 ticks per second) using integer
    /// math and a seeded random generator, so the same setup and seed always give the same
    /// fight. Presentation can call <see cref="Step"/> once per tick to play it back, or
    /// <see cref="Run"/> to get the result instantly.
    /// </summary>
    public sealed class CombatSimulation
    {
        readonly FightSetup setup;
        readonly DeterministicRandom random;
        readonly List<CombatUnit> units = new List<CombatUnit>();
        readonly List<CombatEvent> events = new List<CombatEvent>();
        readonly List<TriggerInstance> triggers = new List<TriggerInstance>();

        int tick;
        int lastOvertimeStep = -1;
        CombatResult result;

        public CombatSimulation(FightSetup setup, ulong seed)
        {
            this.setup = setup ?? throw new ArgumentNullException(nameof(setup));
            random = new DeterministicRandom(seed);
            for (var i = 0; i < setup.Units.Count; i++)
                units.Add(new CombatUnit(i, setup.Units[i]));

            Initialize();
        }

        public IReadOnlyList<CombatUnit> Units => units;

        public IReadOnlyList<CombatEvent> Events => events;

        public FightSetup Setup => setup;

        public int Tick => tick;

        public bool IsFinished => result != null;

        /// <summary>The outcome, or null while the fight is still running.</summary>
        public CombatResult Result => result;

        /// <summary>Runs the fight to the end and returns the result.</summary>
        public CombatResult Run()
        {
            while (Step())
            {
            }

            return result;
        }

        /// <summary>Advances one tick. Returns false once the fight is over.</summary>
        public bool Step()
        {
            if (result != null) return false;

            tick++;
            UpdateStatuses();
            UpdateOvertime();
            RunPeriodicTriggers();

            foreach (var unit in units)
                if (unit.IsAlive) Act(unit);

            CheckEnd();
            return result == null;
        }

        // ---------------------------------------------------------------- setup

        void Initialize()
        {
            foreach (var unit in units)
            {
                var holders = new List<CombatUnit> { unit };
                foreach (var passive in setup.Units[unit.Index].Passives)
                    ApplyPassive(passive, holders, unit.Team, null);
            }

            ActivateTraits(0);
            ActivateTraits(1);

            foreach (var unit in units)
            {
                unit.MaxHealth = Math.Max(1, unit.GetStat(StatIds.Health));
                unit.Health = unit.MaxHealth;
                unit.Mana = Math.Max(0, unit.GetStat(StatIds.StartMana));
            }

            events.Add(new CombatEvent(0, CombatEventType.FightStart, -1, -1));

            foreach (var trigger in triggers.ToArray())
                if (trigger.Spec.Trigger == EffectTrigger.FightStart)
                    Fire(trigger, null);

            CheckEnd();
        }

        void ActivateTraits(int team)
        {
            foreach (var trait in setup.Traits)
            {
                var distinct = new HashSet<string>();
                foreach (var unit in units)
                    if (unit.Team == team && unit.HasTrait(trait.Id))
                        distinct.Add(unit.DefinitionId);

                var tier = trait.GetActiveTier(distinct.Count);
                if (tier == null) continue;

                events.Add(new CombatEvent(0, CombatEventType.TraitActivated, -1, -1, team, distinct.Count, trait.Id));

                foreach (var effect in tier.Effects)
                    ApplyPassive(effect, ResolveHolders(effect.Holders, team, trait.Id), team, trait.Id);
            }
        }

        List<CombatUnit> ResolveHolders(EffectScope scope, int team, string traitId)
        {
            var holders = new List<CombatUnit>();
            if (scope == EffectScope.Team) return holders;

            foreach (var unit in units)
            {
                if (unit.Team != team) continue;
                var member = unit.HasTrait(traitId);
                if (scope == EffectScope.AllAllies ||
                    (scope == EffectScope.OtherAllies && !member) ||
                    ((scope == EffectScope.TraitMembers || scope == EffectScope.Self) && member))
                    holders.Add(unit);
            }

            return holders;
        }

        void ApplyPassive(PassiveSpec passive, List<CombatUnit> holders, int team, string traitId)
        {
            switch (passive)
            {
                case StatModifierSpec modifier:
                    foreach (var holder in holders)
                        holder.Modifiers.Add(new StatModifier
                        {
                            StatId = modifier.StatId,
                            Operation = modifier.Operation,
                            Value = modifier.Value,
                            EndTick = -1,
                        });
                    break;

                case TriggeredSpec triggered when triggered.Holders == EffectScope.Team:
                    triggers.Add(new TriggerInstance(triggered, null, team, traitId));
                    break;

                case TriggeredSpec triggered:
                    foreach (var holder in holders)
                        triggers.Add(new TriggerInstance(triggered, holder, team, traitId));
                    break;
            }
        }

        // ---------------------------------------------------------------- per tick

        void UpdateStatuses()
        {
            foreach (var unit in units)
            {
                if (!unit.IsAlive) continue;

                foreach (var status in unit.Statuses.ToArray())
                {
                    if (status.Kind != StatusKind.Burn || status.DamagePerTick <= 0) continue;
                    DealDamage(UnitOrNull(status.Source), unit, status.DamagePerTick, DamageType.True, false, true);
                    if (!unit.IsAlive) break;
                }

                if (!unit.IsAlive) continue;

                var now = tick;
                unit.Statuses.RemoveAll(status => status.EndTick <= now);
                unit.Shields.RemoveAll(shield => shield.EndTick <= now || shield.Amount <= 0);
                unit.Modifiers.RemoveAll(modifier => modifier.EndTick >= 0 && modifier.EndTick <= now);
            }
        }

        void UpdateOvertime()
        {
            if (tick < setup.OvertimeStartTicks) return;

            var step = (tick - setup.OvertimeStartTicks) / Math.Max(1, setup.OvertimeStepTicks);
            if (step == lastOvertimeStep) return;

            lastOvertimeStep = step;
            events.Add(new CombatEvent(tick, CombatEventType.Overtime, -1, -1, OvertimeBonus()));
        }

        /// <summary>Extra damage in milli-percent once overtime starts.</summary>
        long OvertimeBonus()
        {
            if (tick < setup.OvertimeStartTicks) return 0;
            var steps = 1 + (tick - setup.OvertimeStartTicks) / Math.Max(1, setup.OvertimeStepTicks);
            return steps * setup.OvertimeStepBonusPercent;
        }

        /// <summary>How much weaker healing and shields are, in milli-percent, once overtime starts.</summary>
        long OvertimeHealingCut()
        {
            if (tick < setup.OvertimeStartTicks) return 0;
            var steps = 1 + (tick - setup.OvertimeStartTicks) / Math.Max(1, setup.OvertimeStepTicks);
            return Math.Min(Fixed.Hundred, steps * setup.OvertimeStepHealingCutPercent);
        }

        void RunPeriodicTriggers()
        {
            foreach (var trigger in triggers)
            {
                if (trigger.Spec.Trigger != EffectTrigger.Periodic) continue;
                if (trigger.Holder != null && !trigger.Holder.IsAlive) continue;
                if (tick < trigger.NextTick) continue;

                trigger.NextTick += Math.Max(1, trigger.Spec.IntervalTicks);
                Fire(trigger, null);
            }
        }

        void Act(CombatUnit unit)
        {
            if (unit.HasStatus(StatusKind.Stun)) return;

            if (unit.AttackCooldown > 0) unit.AttackCooldown--;
            if (unit.MoveCooldown > 0) unit.MoveCooldown--;

            if (unit.IsCasting)
            {
                unit.CastTicksLeft--;
                if (unit.CastTicksLeft <= 0) FinishCast(unit);
                return;
            }

            var target = AcquireTarget(unit);
            if (target == null) return;

            if (unit.Skill != null && unit.Skill.Actions.Count > 0)
            {
                var maxMana = EffectiveMaxMana(unit);
                if (maxMana > 0 && unit.Mana >= maxMana)
                {
                    StartCast(unit);
                    return;
                }
            }

            var range = Math.Max(1, (int)(unit.GetStat(StatIds.Range) / Fixed.One));
            if (HexCoord.Distance(unit.Position, target.Position) <= range)
            {
                if (unit.AttackCooldown > 0) return;
                Attack(unit, target);
                unit.AttackCooldown = AttackInterval(unit);
            }
            else
            {
                Move(unit, target);
            }
        }

        CombatUnit AcquireTarget(CombatUnit unit)
        {
            foreach (var status in unit.Statuses)
            {
                if (status.Kind != StatusKind.Taunt) continue;
                var taunter = UnitOrNull(status.Source);
                if (taunter != null && taunter.IsAlive && taunter.Team != unit.Team)
                {
                    unit.TargetIndex = taunter.Index;
                    return taunter;
                }
            }

            if (unit.TargetIndex >= 0 && units[unit.TargetIndex].IsAlive)
                return units[unit.TargetIndex];

            CombatUnit best = null;
            var bestDistance = int.MaxValue;
            foreach (var other in units)
            {
                if (!other.IsAlive || other.Team == unit.Team) continue;
                var distance = HexCoord.Distance(unit.Position, other.Position);
                if (distance < bestDistance)
                {
                    best = other;
                    bestDistance = distance;
                }
            }

            unit.TargetIndex = best?.Index ?? -1;
            return best;
        }

        void Move(CombatUnit unit, CombatUnit target)
        {
            if (unit.MoveCooldown > 0) return;

            var current = HexCoord.Distance(unit.Position, target.Position);
            var bestHex = unit.Position;
            var bestDistance = current;
            var sidestep = unit.Position;

            for (var direction = 0; direction < 6; direction++)
            {
                var next = unit.Position.Neighbor(direction);
                if (!IsOnBoard(next) || IsOccupied(next)) continue;

                var distance = HexCoord.Distance(next, target.Position);
                if (distance < bestDistance)
                {
                    bestHex = next;
                    bestDistance = distance;
                }
                else if (distance == current && sidestep == unit.Position && next != unit.PreviousPosition)
                {
                    sidestep = next;
                }
            }

            if (bestHex == unit.Position) bestHex = sidestep;
            if (bestHex == unit.Position) return;

            unit.PreviousPosition = unit.Position;
            unit.Position = bestHex;
            unit.MoveCooldown = MoveInterval(unit);
            events.Add(new CombatEvent(tick, CombatEventType.Move, unit.Index, -1, 0, bestHex.Column * 100 + bestHex.Row));
        }

        void Attack(CombatUnit unit, CombatUnit target)
        {
            events.Add(new CombatEvent(tick, CombatEventType.Attack, unit.Index, target.Index));

            if (random.RollBasisPoints(Fixed.ToBasisPoints(target.GetStat(StatIds.Dodge))))
            {
                events.Add(new CombatEvent(tick, CombatEventType.Dodge, unit.Index, target.Index));
                GainMana(unit, setup.ManaPerAttack);
                return;
            }

            var damage = unit.GetStat(StatIds.AttackDamage);
            var crit = random.RollBasisPoints(Fixed.ToBasisPoints(unit.GetStat(StatIds.CritChance)));
            if (crit) damage = Fixed.Percent(damage, unit.GetStat(StatIds.CritDamage));

            DealDamage(unit, target, damage, DamageType.Physical, crit);
            GainMana(unit, setup.ManaPerAttack);

            foreach (var trigger in triggers.ToArray())
                if (trigger.Holder == unit && trigger.Spec.Trigger == EffectTrigger.OnAttackHit)
                    Fire(trigger, target);
        }

        void StartCast(CombatUnit unit)
        {
            unit.Mana = 0;
            unit.CastTicksLeft = Math.Max(0, unit.Skill.CastTicks);
            events.Add(new CombatEvent(tick, CombatEventType.CastStart, unit.Index, unit.TargetIndex, 0, 0, unit.Skill.Id));
            if (unit.CastTicksLeft == 0) FinishCast(unit);
        }

        void FinishCast(CombatUnit unit)
        {
            unit.CastTicksLeft = -1;
            var target = AcquireTarget(unit);
            events.Add(new CombatEvent(tick, CombatEventType.Cast, unit.Index, target?.Index ?? -1, 0, 0, unit.Skill.Id));

            foreach (var action in unit.Skill.Actions)
                Execute(action, unit, target, true);

            foreach (var trigger in triggers.ToArray())
                if (trigger.Holder == unit && trigger.Spec.Trigger == EffectTrigger.OnAbilityCast)
                    Fire(trigger, target);
        }

        // ---------------------------------------------------------------- triggers and actions

        void Fire(TriggerInstance trigger, CombatUnit contextTarget)
        {
            if (trigger.Spec.OncePerFight && trigger.Fired) return;

            trigger.Count++;
            if (trigger.Count % Math.Max(1, trigger.Spec.EveryNth) != 0) return;

            var caster = trigger.Holder ?? TeamSource(trigger.Team, trigger.TraitId);
            if (caster == null || !caster.IsAlive) return;

            trigger.Fired = true;
            foreach (var action in trigger.Spec.Actions)
                Execute(action, caster, contextTarget, false);
        }

        CombatUnit TeamSource(int team, string traitId)
        {
            CombatUnit fallback = null;
            foreach (var unit in units)
            {
                if (unit.Team != team || !unit.IsAlive) continue;
                if (traitId != null && unit.HasTrait(traitId)) return unit;
                if (fallback == null) fallback = unit;
            }

            return fallback;
        }

        void Execute(ActionSpec action, CombatUnit caster, CombatUnit contextTarget, bool isSkill)
        {
            var targets = ResolveTargets(action.Target, caster, contextTarget);

            switch (action)
            {
                case DamageSpec damage:
                {
                    var amount = damage.BaseDamage + Fixed.Percent(caster.GetStat(StatIds.AttackDamage), damage.AttackDamagePercent);
                    if (damage.ScalesWithAbilityPower) amount = Fixed.Percent(amount, caster.GetStat(StatIds.AbilityPower));
                    if (isSkill) amount = Fixed.AddPercent(amount, caster.GetStat(StatIds.AbilityDamage));
                    foreach (var target in targets) DealDamage(caster, target, amount, damage.DamageType, false);
                    break;
                }

                case HealSpec heal:
                    foreach (var target in targets) Heal(caster, target, Fixed.Percent(target.MaxHealth, heal.PercentOfMaxHealth), true);
                    break;

                case ShieldSpec shield:
                    foreach (var target in targets) AddShield(caster, target, Fixed.Percent(target.MaxHealth, shield.PercentOfMaxHealth), shield.DurationTicks);
                    break;

                case StatusSpec status:
                    foreach (var target in targets) ApplyStatus(caster, target, status);
                    break;

                case StatBuffSpec buff:
                    foreach (var target in targets)
                        target.Modifiers.Add(new StatModifier
                        {
                            StatId = buff.StatId,
                            Operation = buff.Operation,
                            Value = buff.Value,
                            EndTick = tick + Math.Max(1, buff.DurationTicks),
                        });
                    break;
            }
        }

        List<CombatUnit> ResolveTargets(TargetSpec spec, CombatUnit caster, CombatUnit contextTarget)
        {
            var targets = new List<CombatUnit>();
            var hasContext = contextTarget != null && contextTarget.IsAlive;

            switch (spec.Kind)
            {
                case TargetKind.Self:
                    targets.Add(caster);
                    break;

                case TargetKind.CurrentTarget:
                    if (hasContext) targets.Add(contextTarget);
                    break;

                case TargetKind.CurrentTargetAndAdjacent:
                    if (!hasContext) break;
                    targets.Add(contextTarget);
                    foreach (var unit in units)
                        if (unit != contextTarget && IsEnemy(caster, unit) && HexCoord.Distance(unit.Position, contextTarget.Position) <= 1)
                            targets.Add(unit);
                    break;

                case TargetKind.AdjacentEnemies:
                    foreach (var unit in units)
                        if (IsEnemy(caster, unit) && HexCoord.Distance(unit.Position, caster.Position) <= 1)
                            targets.Add(unit);
                    break;

                case TargetKind.EnemiesInRadiusOfTarget:
                    if (!hasContext) break;
                    foreach (var unit in units)
                        if (IsEnemy(caster, unit) && HexCoord.Distance(unit.Position, contextTarget.Position) <= spec.Radius)
                            targets.Add(unit);
                    break;

                case TargetKind.EnemiesInRadiusOfSelf:
                    foreach (var unit in units)
                        if (IsEnemy(caster, unit) && HexCoord.Distance(unit.Position, caster.Position) <= spec.Radius)
                            targets.Add(unit);
                    break;

                case TargetKind.LowestHealthAllies:
                    foreach (var unit in units)
                        if (unit.IsAlive && unit.Team == caster.Team) targets.Add(unit);
                    targets.Sort(CompareHealthFraction);
                    if (targets.Count > spec.Count) targets.RemoveRange(spec.Count, targets.Count - spec.Count);
                    break;

                case TargetKind.AllAllies:
                    foreach (var unit in units)
                        if (unit.IsAlive && unit.Team == caster.Team) targets.Add(unit);
                    break;

                case TargetKind.RandomEnemies:
                    foreach (var unit in units)
                        if (IsEnemy(caster, unit)) targets.Add(unit);
                    for (var i = 0; i < targets.Count - 1; i++)
                    {
                        var j = random.Range(i, targets.Count);
                        var swap = targets[i];
                        targets[i] = targets[j];
                        targets[j] = swap;
                    }

                    if (targets.Count > spec.Count) targets.RemoveRange(spec.Count, targets.Count - spec.Count);
                    break;
            }

            return targets;
        }

        static int CompareHealthFraction(CombatUnit a, CombatUnit b)
        {
            var left = a.Health * b.MaxHealth;
            var right = b.Health * a.MaxHealth;
            if (left != right) return left < right ? -1 : 1;
            return a.Index.CompareTo(b.Index);
        }

        // ---------------------------------------------------------------- damage, healing, statuses

        void DealDamage(CombatUnit source, CombatUnit target, long raw, DamageType type, bool crit, bool overTime = false)
        {
            if (!target.IsAlive || raw <= 0) return;

            var damage = raw;
            if (type != DamageType.True)
            {
                var resist = type == DamageType.Physical ? target.GetStat(StatIds.Armor) : target.GetStat(StatIds.MagicResist);
                resist = Math.Max(resist, -Fixed.Hundred / 2);
                damage = raw * Fixed.Hundred / (Fixed.Hundred + resist);
            }

            damage = Fixed.AddPercent(damage, OvertimeBonus());
            var reduction = Math.Min(target.StatusValue(StatusKind.DamageReduction), Fixed.Hundred);
            damage = Fixed.Percent(damage, Fixed.Hundred - reduction);
            if (damage <= 0) return;

            var total = damage;
            foreach (var shield in target.Shields)
            {
                var absorbed = Math.Min(shield.Amount, damage);
                shield.Amount -= absorbed;
                damage -= absorbed;
                if (damage == 0) break;
            }

            target.Shields.RemoveAll(shield => shield.Amount <= 0);
            target.Health -= Math.Min(damage, target.Health);

            if (source != null) source.DamageDealt += total;
            target.DamageTaken += total;
            events.Add(new CombatEvent(tick, CombatEventType.Damage, source?.Index ?? -1, target.Index, total, crit ? 1 : overTime ? 2 : 0));

            if (target.IsAlive)
                GainMana(target, Math.Min(setup.MaxManaFromHit, total * Fixed.Hundred / target.MaxHealth));

            if (source != null && source.IsAlive)
            {
                var omnivamp = source.GetStat(StatIds.Omnivamp);
                if (omnivamp > 0) Heal(source, source, Fixed.Percent(total, omnivamp), false);
            }

            if (!target.IsAlive) Kill(target, source);
            else CheckHealthThreshold(target);
        }

        void Kill(CombatUnit target, CombatUnit source)
        {
            target.Health = 0;
            target.Statuses.Clear();
            target.Shields.Clear();
            target.CastTicksLeft = -1;
            events.Add(new CombatEvent(tick, CombatEventType.Death, source?.Index ?? -1, target.Index));
        }

        void CheckHealthThreshold(CombatUnit unit)
        {
            foreach (var trigger in triggers.ToArray())
            {
                if (trigger.Holder != unit || trigger.Spec.Trigger != EffectTrigger.HealthBelowThreshold || trigger.Fired) continue;
                if (unit.Health * Fixed.Hundred / unit.MaxHealth < trigger.Spec.HealthThresholdPercent)
                {
                    trigger.Fired = true;
                    foreach (var action in trigger.Spec.Actions)
                        Execute(action, unit, UnitOrNull(unit.TargetIndex), false);
                }
            }
        }

        void Heal(CombatUnit source, CombatUnit target, long amount, bool applyHealingPower)
        {
            if (!target.IsAlive || amount <= 0) return;

            if (applyHealingPower) amount = Fixed.AddPercent(amount, source.GetStat(StatIds.HealingPower));
            var reduction = Math.Min(target.StatusValue(StatusKind.HealingReduction), Fixed.Hundred);
            amount = Fixed.Percent(amount, Fixed.Hundred - reduction);
            amount = Fixed.Percent(amount, Fixed.Hundred - OvertimeHealingCut());
            amount = Math.Min(amount, target.MaxHealth - target.Health);
            if (amount <= 0) return;

            target.Health += amount;
            source.HealingDone += amount;
            events.Add(new CombatEvent(tick, CombatEventType.Heal, source.Index, target.Index, amount));
        }

        void AddShield(CombatUnit source, CombatUnit target, long amount, int durationTicks)
        {
            if (!target.IsAlive || amount <= 0) return;

            amount = Fixed.AddPercent(amount, source.GetStat(StatIds.HealingPower));
            amount = Fixed.Percent(amount, Fixed.Hundred - OvertimeHealingCut());
            if (amount <= 0) return;

            target.Shields.Add(new ActiveShield { Amount = amount, EndTick = tick + Math.Max(1, durationTicks) });
            source.ShieldingDone += amount;
            events.Add(new CombatEvent(tick, CombatEventType.Shield, source.Index, target.Index, amount));
        }

        void ApplyStatus(CombatUnit source, CombatUnit target, StatusSpec spec)
        {
            if (!target.IsAlive) return;

            var duration = Math.Max(1, spec.DurationTicks);
            target.Statuses.RemoveAll(existing => existing.Kind == spec.Status);

            var status = new ActiveStatus
            {
                Kind = spec.Status,
                Value = spec.Value,
                EndTick = tick + duration,
                Source = source.Index,
            };

            if (spec.Status == StatusKind.Burn)
                status.DamagePerTick = Math.Max(1, Fixed.Percent(target.MaxHealth, spec.Value) / duration);

            if (spec.Status == StatusKind.Taunt)
                target.TargetIndex = source.Index;

            target.Statuses.Add(status);
            events.Add(new CombatEvent(tick, CombatEventType.StatusApplied, source.Index, target.Index, spec.Value, (int)spec.Status));
        }

        void GainMana(CombatUnit unit, long amount)
        {
            if (unit.IsCasting || amount <= 0) return;

            var max = EffectiveMaxMana(unit);
            unit.Mana = max > 0 ? Math.Min(unit.Mana + amount, max) : unit.Mana + amount;
        }

        // ---------------------------------------------------------------- end of fight

        void CheckEnd()
        {
            if (result != null) return;

            long health0 = 0, max0 = 0, health1 = 0, max1 = 0;
            bool alive0 = false, alive1 = false;
            foreach (var unit in units)
            {
                if (unit.Team == 0)
                {
                    health0 += unit.Health;
                    max0 += unit.MaxHealth;
                    alive0 |= unit.IsAlive;
                }
                else
                {
                    health1 += unit.Health;
                    max1 += unit.MaxHealth;
                    alive1 |= unit.IsAlive;
                }
            }

            if (!alive0 || !alive1)
            {
                Finish(alive0 ? 0 : alive1 ? 1 : -1, false);
                return;
            }

            if (tick < setup.MaxTicks) return;

            // Timeout: the team with the larger share of its total health left wins.
            var left = health0 * max1;
            var right = health1 * max0;
            Finish(left > right ? 0 : right > left ? 1 : -1, true);
        }

        void Finish(int winningTeam, bool timeout)
        {
            result = new CombatResult
            {
                WinningTeam = winningTeam,
                DurationTicks = tick,
                DecidedByTimeout = timeout,
            };

            foreach (var unit in units)
                result.Units.Add(new UnitSummary(unit));

            events.Add(new CombatEvent(tick, CombatEventType.FightEnd, -1, -1, winningTeam));
        }

        // ---------------------------------------------------------------- helpers

        long EffectiveMaxMana(CombatUnit unit)
        {
            var reduction = Math.Max(0, Math.Min(unit.GetStat(StatIds.ManaCostReduction), Fixed.Hundred));
            return Fixed.Percent(unit.GetStat(StatIds.MaxMana), Fixed.Hundred - reduction);
        }

        int AttackInterval(CombatUnit unit)
        {
            var speed = unit.GetStat(StatIds.AttackSpeed);
            var slow = Math.Min(unit.StatusValue(StatusKind.Slow), Fixed.Hundred);
            speed = Math.Max(100, Fixed.Percent(speed, Fixed.Hundred - slow));
            return (int)Math.Max(1, Fixed.TicksPerSecond * Fixed.One / speed);
        }

        int MoveInterval(CombatUnit unit)
        {
            var speed = Math.Max(100, unit.GetStat(StatIds.MoveSpeed));
            return (int)Math.Max(1, Fixed.TicksPerSecond * Fixed.One / speed);
        }

        bool IsOnBoard(HexCoord hex) =>
            hex.Column >= 0 && hex.Column < setup.BoardColumns && hex.Row >= 0 && hex.Row < setup.BoardRows;

        bool IsOccupied(HexCoord hex)
        {
            foreach (var unit in units)
                if (unit.IsAlive && unit.Position == hex) return true;
            return false;
        }

        static bool IsEnemy(CombatUnit caster, CombatUnit other) => other.IsAlive && other.Team != caster.Team;

        CombatUnit UnitOrNull(int index) => index >= 0 && index < units.Count ? units[index] : null;

        sealed class TriggerInstance
        {
            public readonly TriggeredSpec Spec;
            public readonly CombatUnit Holder;
            public readonly int Team;
            public readonly string TraitId;
            public int Count;
            public bool Fired;
            public int NextTick;

            public TriggerInstance(TriggeredSpec spec, CombatUnit holder, int team, string traitId)
            {
                Spec = spec;
                Holder = holder;
                Team = team;
                TraitId = traitId;
                NextTick = Math.Max(1, spec.IntervalTicks);
            }
        }
    }
}
