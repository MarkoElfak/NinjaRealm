using System;
using System.Collections.Generic;
using NinjaRealm.Combat;
using NinjaRealm.Core.Campaign;
using NinjaRealm.Core.Definitions;
using NinjaRealm.Core.Progression;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace NinjaRealm.Presentation.Battle
{
    /// <summary>
    /// The result screen in UI Toolkit: who won and with how many stars, how long it took, what
    /// the fight paid into the save, and a damage log with each ninja's damage dealt and taken,
    /// healing and shielding. Layout: UI/Resources/UI/Result.uxml.
    /// </summary>
    [RequireComponent(typeof(BattleController))]
    public sealed class ResultScreen : MonoBehaviour
    {
        BattleController controller;
        UIDocument document;
        VisualElement overlay;
        VisualElement panel;
        Label banner;
        Label summary;
        VisualElement stars;
        VisualElement rewards;
        VisualElement log;
        VisualElement buttons;
        CombatResult shown;
        FightRewards shownRewards;

        void Awake()
        {
            controller = GetComponent<BattleController>();
        }

        void OnEnable()
        {
            if (document == null) document = GameUi.Create(transform, "Result", 20f);
            if (document == null) return;

            var root = document.rootVisualElement;
            overlay = root.Q("overlay");
            panel = root.Q("result");
            banner = root.Q<Label>("banner");
            summary = root.Q<Label>("summary");
            stars = root.Q("stars");
            rewards = root.Q("rewards");
            log = root.Q("log");
            buttons = root.Q("buttons");
            shown = null;
            GameUi.Show(overlay, false);
        }

        void Update()
        {
            if (overlay == null) return;
            var result = controller.Phase == BattlePhase.Result ? controller.Playback.Simulation?.Result : null;
            GameUi.Show(overlay, result != null);
            if (result == null)
            {
                shown = null;
                return;
            }

            // A replay or a new fight gives a new result; rewards arrive with the result.
            if (result != shown || controller.LastRewards != shownRewards) Build(result);

            var keyboard = Keyboard.current;
            if (keyboard == null) return;
            if (keyboard.escapeKey.wasPressedThisFrame) controller.EnterSetup();
            else if (keyboard.rKey.wasPressedThisFrame) controller.Replay();
            else if (keyboard.nKey.wasPressedThisFrame) controller.FightAgain();
            else if (keyboard.enterKey.wasPressedThisFrame && Won && controller.NextStage != null) controller.GoToNextStage();
        }

        bool Won => controller.Playback.Simulation?.Result?.WinningTeam == 0;

        void Build(CombatResult result)
        {
            shown = result;
            shownRewards = controller.LastRewards;

            var won = result.WinningTeam == 0;
            var lost = result.WinningTeam == 1;
            banner.text = won ? "Victory" : lost ? "Defeat" : "Draw";
            banner.style.color = won ? PrototypeGui.Gold : lost ? new Color(0.95f, 0.4f, 0.35f) : Color.white;
            panel.EnableInClassList("result--victory", won);
            panel.EnableInClassList("result--defeat", !won);

            var opponent = controller.Opponent != null ? controller.Opponent.DisplayName : "the enemy";
            var stage = controller.Stage != null && controller.Campaign != null && controller.Opponent == controller.Stage.Lineup
                ? $"Stage {controller.Campaign.LabelOf(controller.Stage)}, "
                : "";
            var timing = result.DecidedByTimeout ? $"Time ran out after {result.DurationSeconds:0.0}s; decided by health left" : $"Fight lasted {result.DurationSeconds:0.0}s";
            summary.text = $"{stage}against {opponent}.  {timing}.  Seed {controller.CurrentSeed}";

            stars.Clear();
            GameUi.Show(stars, controller.LastStars > 0);
            if (controller.LastStars > 0)
            {
                for (var i = 0; i < StageStars.Max; i++)
                {
                    var star = GameUi.Label("★", "big-star");
                    star.EnableInClassList("big-star--on", i < controller.LastStars);
                    stars.Add(star);
                }

                stars.Add(GameUi.Label("   " + StarsText(controller.LastStars)));
            }

            rewards.Clear();
            var lines = RewardLines();
            GameUi.Show(rewards, lines != null);
            if (lines != null)
                foreach (var line in lines)
                    rewards.Add(GameUi.Label(line, "wrap"));

            BuildLog(result, opponent);
            BuildButtons(won);
        }

        static string StarsText(int count) => count >= StageStars.Max ? "Nobody fell." : count == 2 ? "At least half your team is standing." : "A win, but most of your team fell.";

        /// <summary>What the fight paid, one line each. Null without a save.</summary>
        List<string> RewardLines()
        {
            var profile = controller.Profile;
            if (profile == null) return null;

            var fightRewards = controller.LastRewards;
            if (fightRewards == null) return new List<string> { "<color=#9ca3b0>Replays don't pay rewards.</color>" };

            var gold = PrototypeGui.Hex(profile.Progression.Gold != null ? profile.Progression.Gold.Color : PrototypeGui.Gold);
            var lines = new List<string>
            {
                $"<b>Rewards</b>   <color=#{gold}>+{fightRewards.Gold} Gold</color>" +
                (fightRewards.Steel > 0 ? $"   +{fightRewards.Steel} Steel" : "") +
                $"   +{fightRewards.ClanXp} clan XP   <color=#9ca3b0>(clan {profile.ClanLevel}: {profile.ClanXp} / {profile.ClanXpToNext} XP)</color>",
            };
            if (fightRewards.FirstWin)
            {
                var first = $"<color=#{gold}>First clear: +{fightRewards.FirstWinGold} Gold</color>";
                if (fightRewards.FirstClearKarma > 0) first += $"   +{fightRewards.FirstClearKarma} Karma";
                if (fightRewards.FirstClearSteel > 0) first += $"   +{fightRewards.FirstClearSteel} Steel";
                lines.Add(first);
            }
            else if (fightRewards.NewStars > 0)
            {
                lines.Add($"<color=#{gold}>New best: {fightRewards.BestStars} of {StageStars.Max} stars on this stage.</color>");
            }

            if (fightRewards.NinjaMessage != null) lines.Add($"<color=#{gold}>{fightRewards.NinjaMessage}</color>");
            if (fightRewards.Drop != null) lines.Add($"Found a weapon: <b>{fightRewards.Drop.DisplayName}</b>");
            if (fightRewards.ClanLevelsGained > 0)
            {
                var line = $"<color=#{gold}>Clan level {profile.ClanLevel}!</color>  +{fightRewards.Karma} Karma";
                if (fightRewards.TeamSizeAfter > fightRewards.TeamSizeBefore) line += $". Your team can now hold {fightRewards.TeamSizeAfter} ninjas.";
                lines.Add(line);
            }

            return lines;
        }

        // ---------------------------------------------------------------- damage log

        void BuildLog(CombatResult result, string opponent)
        {
            log.Clear();
            var teams = new[] { new List<UnitSummary>(), new List<UnitSummary>() };
            long most = 1;
            foreach (var unit in result.Units)
            {
                if (unit.Team == 0 || unit.Team == 1) teams[unit.Team].Add(unit);
                most = Math.Max(most, Math.Max(unit.DamageDealt, unit.HealingDone + unit.ShieldingDone));
            }

            var header = GameUi.Element("log-row", "log-row--header");
            header.Add(GameUi.Element("log__name"));
            header.Add(Cell("Damage dealt", "log__dealt"));
            header.Add(GameUi.Label("Taken", "log__taken"));
            header.Add(Cell("Heal + shield", "log__support"));
            header.Add(GameUi.Label("Status", "log__status"));
            log.Add(header);

            for (var team = 0; team < 2; team++)
            {
                teams[team].Sort((a, b) => b.DamageDealt.CompareTo(a.DamageDealt));
                log.Add(GameUi.Label(team == 0 ? "Your team" : opponent, "team-title"));
                foreach (var unit in teams[team]) log.Add(Row(unit, most));
            }
        }

        static VisualElement Cell(string text, string className)
        {
            var cell = GameUi.Element(className);
            cell.Add(GameUi.Label(text));
            return cell;
        }

        VisualElement Row(UnitSummary unit, long most)
        {
            var database = controller.Content;
            var name = unit.DefinitionId;
            var color = Color.grey;
            if (database != null && database.TryGet<NinjaDefinition>(unit.DefinitionId, out var ninja))
            {
                name = ninja.DisplayName;
                if (ninja.Element != null) color = ninja.Element.Color;
            }

            var row = GameUi.Element("log-row");
            var nameCell = GameUi.Element("log__name");
            var swatch = GameUi.Element("log__swatch");
            swatch.style.backgroundColor = color;
            nameCell.Add(swatch);
            nameCell.Add(GameUi.Label(name));
            row.Add(nameCell);

            row.Add(BarCell("log__dealt", unit.DamageDealt / (float)most, Fixed.ToFloat(unit.DamageDealt)));
            row.Add(GameUi.Label($"{Fixed.ToFloat(unit.DamageTaken):0}", "log__taken"));
            var support = unit.HealingDone + unit.ShieldingDone;
            row.Add(BarCell("log__support", support / (float)most, Fixed.ToFloat(support)));

            var status = unit.Survived
                ? $"<color=#80f28c>{100f * unit.Health / Mathf.Max(1, unit.MaxHealth):0}%</color>"
                : "<color=#999999>Fallen</color>";
            row.Add(GameUi.Label(status, "log__status"));
            return row;
        }

        static VisualElement BarCell(string className, float fraction, float value)
        {
            var cell = GameUi.Element(className);
            var bar = GameUi.Element("log__bar");
            var fill = GameUi.Element("bar__fill");
            fill.style.width = Length.Percent(Mathf.Clamp01(fraction) * 100f);
            bar.Add(fill);
            cell.Add(bar);
            cell.Add(GameUi.Label($"{value:0}", "log__number"));
            return cell;
        }

        // ---------------------------------------------------------------- buttons

        /// <summary>The next stage shows after a win that has one, the village when it can be loaded.</summary>
        void BuildButtons(bool won)
        {
            buttons.Clear();
            buttons.Add(GameUi.Button("Lineup (Esc)", controller.EnterSetup));
            buttons.Add(GameUi.Button("Replay (R)", controller.Replay));
            buttons.Add(GameUi.Button("Fight again (N)", controller.FightAgain));
            if (won && controller.NextStage != null) buttons.Add(GameUi.Button("Next stage (Enter)", controller.GoToNextStage, "button--primary"));
            if (controller.CanLeaveToVillage) buttons.Add(GameUi.Button("Back to village", controller.LeaveToVillage));
            var last = buttons.childCount - 1;
            if (last >= 0) buttons[last].AddToClassList("button-row__last");
        }
    }
}
