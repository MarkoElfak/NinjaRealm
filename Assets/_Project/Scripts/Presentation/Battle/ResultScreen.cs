using System.Collections.Generic;
using NinjaRealm.Combat;
using NinjaRealm.Core.Content;
using NinjaRealm.Core.Definitions;
using UnityEngine;
using UnityEngine.InputSystem;

namespace NinjaRealm.Presentation.Battle
{
    /// <summary>
    /// Prototype result screen: who won, how long it took, and a damage log with each ninja's
    /// damage dealt and taken, healing and shielding.
    /// </summary>
    [RequireComponent(typeof(BattleController))]
    public sealed class ResultScreen : MonoBehaviour
    {
        BattleController controller;
        readonly List<UnitSummary>[] teams = { new List<UnitSummary>(), new List<UnitSummary>() };

        void Awake()
        {
            controller = GetComponent<BattleController>();
        }

        void Update()
        {
            if (controller.Phase != BattlePhase.Result) return;

            var keyboard = Keyboard.current;
            if (keyboard == null) return;
            if (keyboard.escapeKey.wasPressedThisFrame) controller.EnterSetup();
            else if (keyboard.rKey.wasPressedThisFrame) controller.Replay();
            else if (keyboard.nKey.wasPressedThisFrame) controller.FightAgain();
        }

        void OnGUI()
        {
            if (controller == null || controller.Phase != BattlePhase.Result) return;
            var result = controller.Playback.Simulation?.Result;
            if (result == null) return;

            PrototypeGui.Begin();
            var width = Mathf.Min(760f, PrototypeGui.Width - 40f);
            var rowHeight = 30f;
            teams[0].Clear();
            teams[1].Clear();
            long mostDamage = 1;
            foreach (var unit in result.Units)
            {
                if (unit.Team == 0 || unit.Team == 1) teams[unit.Team].Add(unit);
                mostDamage = System.Math.Max(mostDamage, System.Math.Max(unit.DamageDealt, unit.HealingDone + unit.ShieldingDone));
            }

            foreach (var team in teams)
                team.Sort((a, b) => b.DamageDealt.CompareTo(a.DamageDealt));

            var rows = teams[0].Count + teams[1].Count;
            var height = 150f + rows * rowHeight + 2 * 34f;
            var panel = new Rect((PrototypeGui.Width - width) / 2f, Mathf.Max(20f, (PrototypeGui.Height - height) / 2f), width, height);
            PrototypeGui.Panel(panel);

            var won = result.WinningTeam == 0;
            var title = won ? "Victory" : result.WinningTeam == 1 ? "Defeat" : "Draw";
            var titleColor = won ? PrototypeGui.Gold : result.WinningTeam == 1 ? new Color(0.95f, 0.4f, 0.35f) : Color.white;
            GUI.Label(new Rect(panel.x, panel.y + 10f, panel.width, 48f), $"<color=#{PrototypeGui.Hex(titleColor)}>{title}</color>", PrototypeGui.Banner);

            var opponent = controller.Opponent != null ? controller.Opponent.DisplayName : "the enemy";
            var timing = result.DecidedByTimeout ? $"Time ran out after {result.DurationSeconds:0.0}s; decided by health left" : $"Fight lasted {result.DurationSeconds:0.0}s";
            GUI.Label(new Rect(panel.x, panel.y + 60f, panel.width, 22f), $"against {opponent}.  {timing}.  Seed {controller.CurrentSeed}", PrototypeGui.Centered);

            var y = panel.y + 92f;
            var x = panel.x + 16f;
            var inner = panel.width - 32f;
            DrawHeader(x, y, inner);
            y += 22f;

            for (var team = 0; team < 2; team++)
            {
                GUI.Label(new Rect(x, y, inner, 24f), team == 0 ? "<b>Your team</b>" : $"<b>{opponent}</b>", PrototypeGui.Label);
                y += 26f;
                foreach (var unit in teams[team])
                {
                    DrawRow(unit, x, y, inner, mostDamage);
                    y += rowHeight;
                }

                y += 8f;
            }

            var buttonY = panel.yMax - 44f;
            var buttonWidth = (inner - 20f) / 3f;
            if (GUI.Button(new Rect(x, buttonY, buttonWidth, 32f), "Back to lineup (Esc)")) controller.EnterSetup();
            if (GUI.Button(new Rect(x + buttonWidth + 10f, buttonY, buttonWidth, 32f), "Replay (R)")) controller.Replay();
            if (GUI.Button(new Rect(x + 2f * (buttonWidth + 10f), buttonY, buttonWidth, 32f), "Fight again, new seed (N)")) controller.FightAgain();
        }

        static void DrawHeader(float x, float y, float width)
        {
            GUI.Label(new Rect(x + 150f, y, 260f, 20f), "Damage dealt", PrototypeGui.Small);
            GUI.Label(new Rect(x + 420f, y, 100f, 20f), "Taken", PrototypeGui.Small);
            GUI.Label(new Rect(x + 510f, y, 120f, 20f), "Heal + shield", PrototypeGui.Small);
            GUI.Label(new Rect(x + width - 70f, y, 70f, 20f), "Status", PrototypeGui.Small);
        }

        void DrawRow(UnitSummary unit, float x, float y, float width, long mostDamage)
        {
            var database = controller.Content;
            var name = unit.DefinitionId;
            var color = Color.grey;
            if (database != null && database.TryGet<NinjaDefinition>(unit.DefinitionId, out var ninja))
            {
                name = ninja.DisplayName;
                if (ninja.Element != null) color = ninja.Element.Color;
            }

            PrototypeGui.Fill(new Rect(x, y + 4f, 4f, 18f), color);
            GUI.Label(new Rect(x + 10f, y + 2f, 136f, 22f), name, PrototypeGui.Label);

            var dealt = Fixed.ToFloat(unit.DamageDealt);
            PrototypeGui.Bar(new Rect(x + 150f, y + 7f, 200f, 12f), unit.DamageDealt / (float)mostDamage, new Color(1f, 0.55f, 0.25f));
            GUI.Label(new Rect(x + 356f, y + 2f, 60f, 22f), $"{dealt:0}", PrototypeGui.Small);
            GUI.Label(new Rect(x + 420f, y + 2f, 80f, 22f), $"{Fixed.ToFloat(unit.DamageTaken):0}", PrototypeGui.Small);

            var support = unit.HealingDone + unit.ShieldingDone;
            PrototypeGui.Bar(new Rect(x + 510f, y + 7f, 90f, 12f), support / (float)mostDamage, new Color(0.4f, 0.9f, 0.5f));
            GUI.Label(new Rect(x + 606f, y + 2f, 60f, 22f), $"{Fixed.ToFloat(support):0}", PrototypeGui.Small);

            var status = unit.Survived
                ? $"<color=#{PrototypeGui.Hex(new Color(0.5f, 0.95f, 0.55f))}>{100f * unit.Health / Mathf.Max(1, unit.MaxHealth):0}%</color>"
                : "<color=#999999>Fallen</color>";
            GUI.Label(new Rect(x + width - 70f, y + 2f, 70f, 22f), status, PrototypeGui.Small);
        }
    }
}
