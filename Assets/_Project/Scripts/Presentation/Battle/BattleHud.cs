using System.Collections.Generic;
using NinjaRealm.Combat;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;

namespace NinjaRealm.Presentation.Battle
{
    /// <summary>
    /// Prototype fight HUD drawn with IMGUI: timer, speed controls, active synergies, names
    /// and floating numbers. The real UI comes later with the art pass.
    /// Keys during a fight: Space pause, 1/2/3 speed, S skip to the result.
    /// </summary>
    public sealed class BattleHud : MonoBehaviour
    {
        [SerializeField, FormerlySerializedAs("demo")] BattleController controller;

        GUIStyle floating;
        GUIStyle floatingLarge;
        GUIStyle nameLabel;
        readonly List<string>[] synergies = { new List<string>(), new List<string>() };

        void Awake()
        {
            if (controller == null) controller = GetComponent<BattleController>();
        }

        void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null || controller == null || controller.Phase != BattlePhase.Fight) return;

            var playback = controller.Playback;
            if (keyboard.spaceKey.wasPressedThisFrame) playback.Paused = !playback.Paused;
            if (keyboard.digit1Key.wasPressedThisFrame) playback.Speed = 1f;
            if (keyboard.digit2Key.wasPressedThisFrame) playback.Speed = 2f;
            if (keyboard.digit3Key.wasPressedThisFrame) playback.Speed = 4f;
            if (keyboard.sKey.wasPressedThisFrame) controller.SkipFight();
        }

        void OnGUI()
        {
            if (controller == null || controller.Phase == BattlePhase.Setup) return;
            var playback = controller.Playback;
            var simulation = playback != null ? playback.Simulation : null;
            if (simulation == null) return;

            PrototypeGui.Begin();
            EnsureStyles();
            DrawWorldLabels(playback, Camera.main, PrototypeGui.Scale);

            if (controller.Phase != BattlePhase.Fight) return;
            DrawTopBar(playback, simulation, PrototypeGui.Width);
            DrawSynergies(simulation, PrototypeGui.Width);
        }

        void DrawTopBar(BattlePlayback playback, CombatSimulation simulation, float width)
        {
            var overtime = simulation.Tick >= simulation.Setup.OvertimeStartTicks;
            var opponent = controller.Opponent != null ? controller.Opponent.DisplayName : "Enemy";
            var time = $"{playback.FightSeconds:0.0}s{(overtime ? "  <color=#FF7744>OVERTIME</color>" : "")}";
            var bar = new Rect(width / 2f - 270f, 8f, 540f, 70f);
            PrototypeGui.Panel(bar);
            GUI.Label(new Rect(bar.x, bar.y + 4f, bar.width, 26f), $"You vs {opponent}    {time}", PrototypeGui.Centered);

            var x = bar.x + 12f;
            var y = bar.y + 36f;
            if (GUI.Button(new Rect(x, y, 80f, 26f), playback.Paused ? "Play" : "Pause")) playback.Paused = !playback.Paused;
            x += 86f;
            foreach (var speed in new[] { 1f, 2f, 4f })
            {
                var text = Mathf.Approximately(playback.Speed, speed) ? $"[{speed}x]" : $"{speed}x";
                if (GUI.Button(new Rect(x, y, 56f, 26f), text)) playback.Speed = speed;
                x += 62f;
            }

            if (GUI.Button(new Rect(x, y, 120f, 26f), "Skip (S)")) controller.SkipFight();
            x += 126f;
            if (GUI.Button(new Rect(x, y, bar.xMax - x - 12f, 26f), "Give up")) controller.EnterSetup();
        }

        void DrawSynergies(CombatSimulation simulation, float width)
        {
            synergies[0].Clear();
            synergies[1].Clear();
            foreach (var e in simulation.Events)
            {
                if (e.Tick > 0) break;
                if (e.Type == CombatEventType.TraitActivated && e.Amount >= 0 && e.Amount <= 1)
                    synergies[e.Amount].Add($"{controller.NameOf(e.Id)}  {e.Detail}");
            }

            DrawList(new Rect(10f, 90f, 200f, 0f), "Your synergies", synergies[0]);
            DrawList(new Rect(width - 210f, 90f, 200f, 0f), "Enemy synergies", synergies[1]);
        }

        static void DrawList(Rect area, string title, List<string> lines)
        {
            area.height = 34f + 22f * Mathf.Max(1, lines.Count);
            PrototypeGui.Panel(area);
            GUI.Label(new Rect(area.x + 10f, area.y + 6f, area.width - 20f, 22f), $"<b>{title}</b>", PrototypeGui.Label);
            if (lines.Count == 0)
                GUI.Label(new Rect(area.x + 10f, area.y + 30f, area.width - 20f, 22f), "None", PrototypeGui.Label);
            for (var i = 0; i < lines.Count; i++)
                GUI.Label(new Rect(area.x + 10f, area.y + 30f + 22f * i, area.width - 20f, 22f), lines[i], PrototypeGui.Label);
        }

        void DrawWorldLabels(BattlePlayback playback, Camera viewCamera, float scale)
        {
            if (viewCamera == null) return;

            foreach (var view in playback.Views)
            {
                if (view.IsDead) continue;
                if (!ToGui(viewCamera, view.BarsPosition, scale, out var point)) continue;
                GUI.Label(new Rect(point.x - 60f, point.y - 30f, 120f, 18f), view.DisplayName, nameLabel);
            }

            foreach (var text in playback.FloatingTexts)
            {
                if (!ToGui(viewCamera, text.Position, scale, out var point)) continue;
                var style = text.Large ? floatingLarge : floating;
                var alpha = 1f - Mathf.Clamp01((text.Age - BattlePlayback.FloatingTextSeconds * 0.6f) / (BattlePlayback.FloatingTextSeconds * 0.4f));
                var rect = new Rect(point.x - 80f, point.y - 30f, 160f, 30f);

                GUI.color = new Color(0f, 0f, 0f, alpha * 0.8f);
                GUI.Label(new Rect(rect.x + 1.5f, rect.y + 1.5f, rect.width, rect.height), text.Text, style);
                GUI.color = new Color(text.Color.r, text.Color.g, text.Color.b, alpha);
                GUI.Label(rect, text.Text, style);
            }

            GUI.color = Color.white;
        }

        static bool ToGui(Camera viewCamera, Vector3 world, float scale, out Vector2 point)
        {
            var screen = viewCamera.WorldToScreenPoint(world);
            point = new Vector2(screen.x / scale, (Screen.height - screen.y) / scale);
            return screen.z > 0f;
        }

        void EnsureStyles()
        {
            if (floating != null) return;

            nameLabel = new GUIStyle(PrototypeGui.Label) { alignment = TextAnchor.MiddleCenter, fontSize = 12, fontStyle = FontStyle.Bold };
            floating = new GUIStyle(PrototypeGui.Label) { alignment = TextAnchor.MiddleCenter, fontSize = 17, fontStyle = FontStyle.Bold, richText = false };
            floatingLarge = new GUIStyle(floating) { fontSize = 22 };
        }
    }
}
