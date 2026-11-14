using System.Collections.Generic;
using NinjaRealm.Combat;
using UnityEngine;
using UnityEngine.InputSystem;

namespace NinjaRealm.Presentation.Battle
{
    /// <summary>
    /// Prototype battle HUD drawn with IMGUI: fight timer, speed controls, active synergies,
    /// names, floating numbers and the result. The real UI comes later with the art pass.
    /// Keys: Space pause, 1/2/3 speed, R replay, N new fight.
    /// </summary>
    public sealed class BattleHud : MonoBehaviour
    {
        [SerializeField] BattleDemo demo;

        GUIStyle label;
        GUIStyle centered;
        GUIStyle floating;
        GUIStyle floatingLarge;
        GUIStyle nameLabel;
        GUIStyle banner;
        readonly List<string>[] synergies = { new List<string>(), new List<string>() };

        void Update()
        {
            var keyboard = Keyboard.current;
            var playback = demo != null ? demo.Playback : null;
            if (keyboard == null || playback == null) return;

            if (keyboard.spaceKey.wasPressedThisFrame) playback.Paused = !playback.Paused;
            if (keyboard.digit1Key.wasPressedThisFrame) playback.Speed = 1f;
            if (keyboard.digit2Key.wasPressedThisFrame) playback.Speed = 2f;
            if (keyboard.digit3Key.wasPressedThisFrame) playback.Speed = 4f;
            if (keyboard.rKey.wasPressedThisFrame) demo.Replay();
            if (keyboard.nKey.wasPressedThisFrame) demo.NextFight();
        }

        void OnGUI()
        {
            var playback = demo != null ? demo.Playback : null;
            var simulation = playback != null ? playback.Simulation : null;
            if (simulation == null) return;

            EnsureStyles();
            var scale = Screen.height / 900f;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            var width = Screen.width / scale;
            var height = Screen.height / scale;
            var viewCamera = Camera.main;

            DrawWorldLabels(playback, viewCamera, scale);
            DrawTopBar(playback, simulation, width);
            DrawSynergies(simulation, width);

            if (simulation.IsFinished) DrawResult(simulation.Result, width, height);
        }

        void DrawTopBar(BattlePlayback playback, CombatSimulation simulation, float width)
        {
            var overtime = simulation.Tick >= simulation.Setup.OvertimeStartTicks;
            var time = $"{playback.FightSeconds:0.0}s{(overtime ? "  OVERTIME" : "")}";
            GUI.Box(new Rect(width / 2f - 260f, 8f, 520f, 64f), GUIContent.none);
            GUI.Label(new Rect(width / 2f - 250f, 12f, 500f, 26f), $"{time}    Seed {demo.CurrentSeed}", centered);

            var x = width / 2f - 250f;
            var y = 40f;
            if (GUI.Button(new Rect(x, y, 80f, 26f), playback.Paused ? "Play" : "Pause")) playback.Paused = !playback.Paused;
            x += 86f;
            foreach (var speed in new[] { 1f, 2f, 4f })
            {
                var text = Mathf.Approximately(playback.Speed, speed) ? $"[{speed}x]" : $"{speed}x";
                if (GUI.Button(new Rect(x, y, 56f, 26f), text)) playback.Speed = speed;
                x += 62f;
            }

            if (GUI.Button(new Rect(x, y, 100f, 26f), "Replay (R)")) demo.Replay();
            x += 106f;
            if (GUI.Button(new Rect(x, y, 122f, 26f), "New fight (N)")) demo.NextFight();
        }

        void DrawSynergies(CombatSimulation simulation, float width)
        {
            synergies[0].Clear();
            synergies[1].Clear();
            foreach (var e in simulation.Events)
            {
                if (e.Tick > 0) break;
                if (e.Type == CombatEventType.TraitActivated && e.Amount >= 0 && e.Amount <= 1)
                    synergies[e.Amount].Add($"{demo.NameOf(e.Id)}  {e.Detail}");
            }

            DrawList(new Rect(10f, 90f, 200f, 0f), "Your synergies", synergies[0]);
            DrawList(new Rect(width - 210f, 90f, 200f, 0f), "Enemy synergies", synergies[1]);
        }

        void DrawList(Rect area, string title, List<string> lines)
        {
            area.height = 34f + 22f * Mathf.Max(1, lines.Count);
            GUI.Box(area, GUIContent.none);
            GUI.Label(new Rect(area.x + 10f, area.y + 6f, area.width - 20f, 22f), title, label);
            if (lines.Count == 0)
                GUI.Label(new Rect(area.x + 10f, area.y + 30f, area.width - 20f, 22f), "None", label);
            for (var i = 0; i < lines.Count; i++)
                GUI.Label(new Rect(area.x + 10f, area.y + 30f + 22f * i, area.width - 20f, 22f), lines[i], label);
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

        void DrawResult(CombatResult result, float width, float height)
        {
            var text = result.WinningTeam == 0 ? "Victory" : result.WinningTeam == 1 ? "Defeat" : "Draw";
            var detail = result.DecidedByTimeout ? "Decided on time" : $"{result.DurationSeconds:0.0}s";
            var area = new Rect(width / 2f - 180f, height * 0.3f, 360f, 110f);
            GUI.Box(area, GUIContent.none);
            GUI.Label(new Rect(area.x, area.y + 12f, area.width, 50f), text, banner);
            GUI.Label(new Rect(area.x, area.y + 66f, area.width, 26f), $"{detail}.  R to replay, N for a new fight.", centered);
        }

        void EnsureStyles()
        {
            if (label != null) return;

            label = new GUIStyle(GUI.skin.label) { fontSize = 15 };
            label.normal.textColor = Color.white;
            centered = new GUIStyle(label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, fontSize = 17 };
            nameLabel = new GUIStyle(label) { alignment = TextAnchor.MiddleCenter, fontSize = 12, fontStyle = FontStyle.Bold };
            floating = new GUIStyle(label) { alignment = TextAnchor.MiddleCenter, fontSize = 17, fontStyle = FontStyle.Bold };
            floatingLarge = new GUIStyle(floating) { fontSize = 22 };
            banner = new GUIStyle(centered) { fontSize = 40 };
        }
    }
}
