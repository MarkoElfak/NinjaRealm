using System;
using System.Collections.Generic;
using NinjaRealm.Combat;
using UnityEngine;

namespace NinjaRealm.Presentation.Battle
{
    /// <summary>A short-lived number or word that floats up from a unit, drawn by the HUD.</summary>
    public struct FloatingText
    {
        public Vector3 Position;
        public string Text;
        public Color Color;
        public float Age;
        public bool Large;
    }

    /// <summary>
    /// Plays a CombatSimulation back in real time: steps it at 20 ticks per second (times the
    /// speed), turns new combat events into animations, and keeps the bars in sync. The
    /// simulation decides everything; this only shows it.
    /// </summary>
    public sealed class BattlePlayback : MonoBehaviour
    {
        public const float FloatingTextSeconds = 1.1f;
        const float TickSeconds = 1f / Fixed.TicksPerSecond;

        [SerializeField, Range(0.25f, 8f)] float speed = 1f;

        readonly List<UnitView> views = new List<UnitView>();
        readonly List<FloatingText> floatingTexts = new List<FloatingText>();
        readonly SortedDictionary<int, long> burnDamage = new SortedDictionary<int, long>();

        HexBoardView board;
        Func<string, string> nameOf = id => id;
        int eventCursor;
        float accumulator;

        public CombatSimulation Simulation { get; private set; }

        public IReadOnlyList<UnitView> Views => views;

        public IReadOnlyList<FloatingText> FloatingTexts => floatingTexts;

        public float Speed
        {
            get => speed;
            set => speed = Mathf.Clamp(value, 0.25f, 8f);
        }

        public bool Paused { get; set; }

        /// <summary>Seconds of fight time shown so far.</summary>
        public float FightSeconds => Simulation != null ? Simulation.Tick * TickSeconds : 0f;

        public event Action<CombatResult> Finished;

        /// <summary>Starts showing a fight. The views must match the simulation's units by index.</summary>
        public void Begin(CombatSimulation simulation, HexBoardView boardView, IEnumerable<UnitView> unitViews, Func<string, string> displayName)
        {
            Clear();
            Simulation = simulation;
            board = boardView;
            nameOf = displayName ?? (id => id);
            views.AddRange(unitViews);
            ProcessNewEvents();
            SyncBars();
        }

        /// <summary>Removes the unit views and forgets the fight.</summary>
        public void Clear()
        {
            foreach (var view in views)
                if (view != null) PrototypeVisuals.DestroySafe(view.gameObject);
            views.Clear();
            floatingTexts.Clear();
            burnDamage.Clear();
            Simulation = null;
            eventCursor = 0;
            accumulator = 0f;
        }

        void Update()
        {
            Advance(Time.deltaTime);
        }

        /// <summary>Moves playback forward by real seconds. Also usable from editor tools outside play mode.</summary>
        public void Advance(float deltaTime)
        {
            if (Simulation == null) return;

            if (!Paused && !Simulation.IsFinished)
            {
                accumulator += deltaTime * speed;
                while (accumulator >= TickSeconds && !Simulation.IsFinished)
                {
                    accumulator -= TickSeconds;
                    Simulation.Step();
                    ProcessNewEvents();
                    if (Simulation.Tick % Fixed.TicksPerSecond == 0) ShowBurnDamage();
                }

                SyncBars();
                if (Simulation.IsFinished)
                {
                    accumulator = 0f;
                    Finished?.Invoke(Simulation.Result);
                }
            }

            var viewCamera = Camera.main;
            foreach (var view in views)
                view.Tick(deltaTime, viewCamera);

            for (var i = floatingTexts.Count - 1; i >= 0; i--)
            {
                var text = floatingTexts[i];
                text.Age += deltaTime;
                text.Position += Vector3.up * (deltaTime * 0.8f);
                if (text.Age >= FloatingTextSeconds) floatingTexts.RemoveAt(i);
                else floatingTexts[i] = text;
            }
        }

        void SyncBars()
        {
            for (var i = 0; i < views.Count; i++)
            {
                var unit = Simulation.Units[i];
                if (views[i].IsDead) continue;

                var maxHealth = Mathf.Max(1f, unit.MaxHealth);
                var maxMana = unit.GetStat(StatIds.MaxMana);
                var mana = maxMana > 0 ? unit.Mana / (float)maxMana : 0f;
                views[i].SetBars(unit.Health / maxHealth, unit.ShieldTotal / maxHealth, mana);
            }
        }

        void ProcessNewEvents()
        {
            var events = Simulation.Events;
            for (; eventCursor < events.Count; eventCursor++)
                Show(events[eventCursor]);
        }

        void Show(CombatEvent e)
        {
            var source = e.Source >= 0 && e.Source < views.Count ? views[e.Source] : null;
            var target = e.Target >= 0 && e.Target < views.Count ? views[e.Target] : null;

            switch (e.Type)
            {
                case CombatEventType.Move when source != null:
                {
                    var hex = new HexCoord(e.Detail / 100, e.Detail % 100);
                    var moveSpeed = Mathf.Max(0.1f, Fixed.ToFloat(Simulation.Units[e.Source].GetStat(StatIds.MoveSpeed)));
                    source.MoveTo(board.ToWorld(hex), 1f / moveSpeed);
                    break;
                }

                case CombatEventType.Attack when source != null && target != null:
                    source.Attack(target.transform.position);
                    break;

                case CombatEventType.Dodge when target != null:
                    Float(target, "Dodge", new Color(0.8f, 0.8f, 0.8f));
                    break;

                case CombatEventType.Damage when target != null && e.Detail == 2:
                    // Burn ticks every tick; it is shown once a second as one number.
                    burnDamage.TryGetValue(e.Target, out var burned);
                    burnDamage[e.Target] = burned + e.Amount;
                    break;

                case CombatEventType.Damage when target != null:
                    target.Hit();
                    if (e.Source < 0 || (source != null && source.Team == target.Team))
                        Float(target, Amount(e.Amount), new Color(1f, 0.55f, 0.2f));
                    else if (e.Detail == 1)
                        Float(target, Amount(e.Amount) + "!", new Color(1f, 0.85f, 0.2f), true);
                    else
                        Float(target, Amount(e.Amount), Color.white);
                    break;

                case CombatEventType.Heal when target != null:
                    Float(target, "+" + Amount(e.Amount), new Color(0.4f, 1f, 0.45f));
                    break;

                case CombatEventType.Shield when target != null:
                    Float(target, "+" + Amount(e.Amount), new Color(0.75f, 0.9f, 1f));
                    break;

                case CombatEventType.CastStart when source != null:
                    source.Cast();
                    Float(source, nameOf(e.Id), new Color(0.8f, 0.6f, 1f), true);
                    break;

                case CombatEventType.StatusApplied when target != null:
                    var status = (StatusKind)e.Detail;
                    if (status == StatusKind.Stun || status == StatusKind.Taunt)
                        Float(target, status.ToString(), new Color(1f, 0.9f, 0.3f));
                    break;

                case CombatEventType.Death when target != null:
                    target.Die();
                    break;
            }
        }

        void ShowBurnDamage()
        {
            foreach (var entry in burnDamage)
                if (!views[entry.Key].IsDead && entry.Value >= Fixed.One)
                    Float(views[entry.Key], Amount(entry.Value), new Color(1f, 0.45f, 0.15f));
            burnDamage.Clear();
        }

        static string Amount(long fixedAmount) => Mathf.RoundToInt(Fixed.ToFloat(fixedAmount)).ToString();

        void Float(UnitView view, string text, Color color, bool large = false)
        {
            // Nudge texts sideways a little so several in one tick stay readable.
            var offset = new Vector3(((floatingTexts.Count * 37) % 5 - 2) * 0.08f, 0.15f, 0f);
            floatingTexts.Add(new FloatingText
            {
                Position = view.BarsPosition + offset,
                Text = text,
                Color = color,
                Large = large,
            });
        }
    }
}
