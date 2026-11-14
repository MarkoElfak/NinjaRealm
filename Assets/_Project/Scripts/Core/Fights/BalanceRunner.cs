using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using NinjaRealm.Combat;
using NinjaRealm.Core.Content;
using NinjaRealm.Core.Lineups;

namespace NinjaRealm.Core.Fights
{
    /// <summary>A named lineup for balance runs.</summary>
    public sealed class BalanceTeam
    {
        public readonly string Name;
        public readonly IReadOnlyList<LineupSlot> Slots;

        public BalanceTeam(string name, IReadOnlyList<LineupSlot> slots)
        {
            Name = name;
            Slots = slots;
        }
    }

    /// <summary>How two lineups did against each other over many seeds, playing both sides.</summary>
    public sealed class MatchupResult
    {
        public string TeamA;
        public string TeamB;
        public int Fights;
        public int WinsA;
        public int WinsB;
        public int Draws;
        public int Timeouts;
        public long TotalTicks;

        public double WinRateA => Fights == 0 ? 0 : (WinsA + Draws * 0.5) / Fights;

        public double AverageSeconds => Fights == 0 ? 0 : TotalTicks / (double)Fights / Fixed.TicksPerSecond;
    }

    /// <summary>
    /// Runs many simulated fights to check balance: win rates, fight lengths and who deals the
    /// damage. Each matchup is played from both sides of the board with the same seeds, so
    /// side and seed luck cancel out.
    /// </summary>
    public sealed class BalanceRunner
    {
        readonly ContentDatabase database;
        readonly Dictionary<string, (long damage, long healing, int fights, int survived)> ninjaTotals =
            new Dictionary<string, (long, long, int, int)>(StringComparer.Ordinal);

        public BalanceRunner(ContentDatabase database)
        {
            this.database = database ?? throw new ArgumentNullException(nameof(database));
        }

        public int SeedsPerSide { get; set; } = 20;

        public MatchupResult Run(BalanceTeam a, BalanceTeam b)
        {
            var result = new MatchupResult { TeamA = a.Name, TeamB = b.Name };
            for (var seed = 1; seed <= SeedsPerSide; seed++)
            {
                for (var side = 0; side < 2; side++)
                {
                    var builder = CombatSetupBuilder.FromDatabase(database);
                    builder.AddLineup(side, a.Slots);
                    builder.AddLineup(1 - side, b.Slots);
                    var simulation = new CombatSimulation(builder.Build(), (ulong)(seed * 7919));
                    var fight = simulation.Run();

                    result.Fights++;
                    result.TotalTicks += fight.DurationTicks;
                    if (fight.DecidedByTimeout) result.Timeouts++;
                    if (fight.WinningTeam < 0) result.Draws++;
                    else if (fight.WinningTeam == side) result.WinsA++;
                    else result.WinsB++;

                    foreach (var unit in fight.Units)
                    {
                        ninjaTotals.TryGetValue(unit.DefinitionId, out var totals);
                        ninjaTotals[unit.DefinitionId] = (totals.damage + unit.DamageDealt,
                            totals.healing + unit.HealingDone + unit.ShieldingDone, totals.fights + 1,
                            totals.survived + (unit.Survived ? 1 : 0));
                    }
                }
            }

            return result;
        }

        /// <summary>Plays every team against every other team and returns a Markdown report.</summary>
        public string RoundRobin(IReadOnlyList<BalanceTeam> teams, string title)
        {
            var results = new MatchupResult[teams.Count, teams.Count];
            var overall = new double[teams.Count];
            var all = new List<MatchupResult>();
            for (var i = 0; i < teams.Count; i++)
            for (var j = i + 1; j < teams.Count; j++)
            {
                var result = Run(teams[i], teams[j]);
                results[i, j] = result;
                all.Add(result);
                overall[i] += result.WinRateA;
                overall[j] += 1 - result.WinRateA;
            }

            var text = new StringBuilder();
            text.AppendLine($"## {title}");
            text.AppendLine();
            text.AppendLine($"Each pair fights {SeedsPerSide * 2} times ({SeedsPerSide} seeds, from both sides). Cells are the row team's win rate.");
            text.AppendLine();

            text.Append("| | ");
            foreach (var team in teams) text.Append(team.Name).Append(" | ");
            text.AppendLine("Overall |");
            text.Append("| --- | ");
            foreach (var _ in teams) text.Append("---: | ");
            text.AppendLine("---: |");

            for (var i = 0; i < teams.Count; i++)
            {
                text.Append("| ").Append(teams[i].Name).Append(" | ");
                for (var j = 0; j < teams.Count; j++)
                {
                    if (i == j) text.Append("– | ");
                    else if (i < j) text.Append(Percent(results[i, j].WinRateA)).Append(" | ");
                    else text.Append(Percent(1 - results[j, i].WinRateA)).Append(" | ");
                }

                text.Append(Percent(teams.Count > 1 ? overall[i] / (teams.Count - 1) : 0)).AppendLine(" |");
            }

            long ticks = 0;
            int fights = 0, timeouts = 0;
            var shortest = double.MaxValue;
            var longest = 0d;
            foreach (var result in all)
            {
                ticks += result.TotalTicks;
                fights += result.Fights;
                timeouts += result.Timeouts;
                shortest = Math.Min(shortest, result.AverageSeconds);
                longest = Math.Max(longest, result.AverageSeconds);
            }

            text.AppendLine();
            if (fights > 0)
                text.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "Average fight: {0:0.0}s (matchup averages from {1:0.0}s to {2:0.0}s). Timeouts: {3} of {4} fights.",
                    ticks / (double)fights / Fixed.TicksPerSecond, shortest, longest, timeouts, fights));
            text.AppendLine();
            return text.ToString();
        }

        /// <summary>Average damage dealt and healing plus shielding per fight for each ninja, over every fight run so far.</summary>
        public string NinjaReport()
        {
            var ids = new List<string>(ninjaTotals.Keys);
            ids.Sort(StringComparer.Ordinal);

            var text = new StringBuilder();
            text.AppendLine("## Ninjas");
            text.AppendLine();
            text.AppendLine("| Ninja | Fights | Damage per fight | Heal + shield per fight | Survived |");
            text.AppendLine("| --- | ---: | ---: | ---: | ---: |");
            foreach (var id in ids)
            {
                var totals = ninjaTotals[id];
                var name = database.TryGet<ContentDefinition>(id, out var definition) ? definition.DisplayName : id;
                text.AppendLine(string.Format(CultureInfo.InvariantCulture, "| {0} | {1} | {2:0} | {3:0} | {4} |",
                    name, totals.fights, Fixed.ToFloat(totals.damage) / totals.fights,
                    Fixed.ToFloat(totals.healing) / totals.fights, Percent(totals.survived / (double)totals.fights)));
            }

            text.AppendLine();
            return text.ToString();
        }

        static string Percent(double value) => (value * 100).ToString("0", CultureInfo.InvariantCulture) + "%";
    }
}
