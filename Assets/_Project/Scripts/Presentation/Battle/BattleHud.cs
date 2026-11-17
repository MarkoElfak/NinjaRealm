using System.Collections.Generic;
using NinjaRealm.Combat;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;
using UnityEngine.UIElements;

namespace NinjaRealm.Presentation.Battle
{
    /// <summary>
    /// The fight HUD in UI Toolkit: who you fight and the time, pause and speed buttons, both
    /// teams' synergies, and the names and floating numbers over the board. Layout:
    /// UI/Resources/UI/BattleHud.uxml. Keys during a fight: Space pause, 1/2/3 speed, S skip
    /// to the result.
    /// </summary>
    public sealed class BattleHud : MonoBehaviour
    {
        static readonly float[] Speeds = { 1f, 2f, 4f };

        [SerializeField, FormerlySerializedAs("demo")] BattleController controller;

        UIDocument document;
        VisualElement fightUi;
        Label fightTitle;
        Button pause;
        readonly Button[] speedButtons = new Button[3];
        readonly VisualElement[] synergyLists = new VisualElement[2];
        WorldLabels names;
        WorldLabels numbers;
        CombatSimulation shownSynergies;
        string shownTitle;

        void Awake()
        {
            if (controller == null) controller = GetComponent<BattleController>();
        }

        void OnEnable()
        {
            if (document == null) document = GameUi.Create(transform, "BattleHud");
            if (document == null) return;

            var root = document.rootVisualElement;
            var labels = root.Q("boardLabels");
            names = new WorldLabels(labels, "board-name");
            numbers = new WorldLabels(labels, "floating");
            fightUi = root.Q("fightUi");
            fightTitle = root.Q<Label>("fightTitle");
            pause = root.Q<Button>("pause");
            pause.clicked += () => controller.Playback.Paused = !controller.Playback.Paused;
            for (var i = 0; i < Speeds.Length; i++)
            {
                var speed = Speeds[i];
                speedButtons[i] = root.Q<Button>("speed" + (int)speed);
                speedButtons[i].clicked += () => controller.Playback.Speed = speed;
            }

            root.Q<Button>("skip").clicked += () => controller.SkipFight();
            root.Q<Button>("giveUp").clicked += () => controller.EnterSetup();
            synergyLists[0] = root.Q("ourSynergies");
            synergyLists[1] = root.Q("theirSynergies");
            shownSynergies = null;
            shownTitle = null;
        }

        void Update()
        {
            if (controller == null) return;
            var keyboard = Keyboard.current;
            if (keyboard != null && controller.Phase == BattlePhase.Fight)
            {
                var playback = controller.Playback;
                if (keyboard.spaceKey.wasPressedThisFrame) playback.Paused = !playback.Paused;
                if (keyboard.digit1Key.wasPressedThisFrame) playback.Speed = 1f;
                if (keyboard.digit2Key.wasPressedThisFrame) playback.Speed = 2f;
                if (keyboard.digit3Key.wasPressedThisFrame) playback.Speed = 4f;
                if (keyboard.sKey.wasPressedThisFrame) controller.SkipFight();
            }
        }

        // The units move in Update, so the labels follow them here.
        void LateUpdate()
        {
            if (document == null || names == null) return;
            var playback = controller != null ? controller.Playback : null;
            var simulation = playback != null ? playback.Simulation : null;
            var showLabels = simulation != null && controller.Phase != BattlePhase.Setup;

            names.Begin();
            numbers.Begin();
            if (showLabels) PlaceLabels(playback, Camera.main);
            names.End();
            numbers.End();

            var fighting = showLabels && controller.Phase == BattlePhase.Fight;
            GameUi.Show(fightUi, fighting);
            if (!fighting) return;
            RefreshBar(playback, simulation);
            RefreshSynergies(simulation);
        }

        void RefreshBar(BattlePlayback playback, CombatSimulation simulation)
        {
            var overtime = simulation.Tick >= simulation.Setup.OvertimeStartTicks;
            var opponent = controller.Opponent != null ? controller.Opponent.DisplayName : "Enemy";
            var clan = controller.Profile != null && !string.IsNullOrEmpty(controller.Profile.ClanName) ? controller.Profile.ClanName : "You";
            var title = $"{clan} vs {opponent}    {playback.FightSeconds:0.0}s{(overtime ? "  <color=#FF7744>OVERTIME</color>" : "")}";
            if (title != shownTitle)
            {
                shownTitle = title;
                fightTitle.text = title;
            }

            pause.text = playback.Paused ? "Play" : "Pause";
            for (var i = 0; i < Speeds.Length; i++)
                speedButtons[i].EnableInClassList("speed--on", Mathf.Approximately(playback.Speed, Speeds[i]));
        }

        /// <summary>The synergies each team started the fight with. They only change with a new fight.</summary>
        void RefreshSynergies(CombatSimulation simulation)
        {
            if (simulation == shownSynergies) return;
            shownSynergies = simulation;
            var lines = new[] { new List<string>(), new List<string>() };
            foreach (var e in simulation.Events)
            {
                if (e.Tick > 0) break;
                if (e.Type == CombatEventType.TraitActivated && e.Amount >= 0 && e.Amount <= 1)
                    lines[e.Amount].Add($"<b>{controller.NameOf(e.Id)}</b>  {e.Detail}");
            }

            for (var team = 0; team < 2; team++)
            {
                synergyLists[team].Clear();
                if (lines[team].Count == 0) synergyLists[team].Add(GameUi.Label("None", "muted"));
                foreach (var line in lines[team]) synergyLists[team].Add(GameUi.Label(line, "side-line"));
            }
        }

        void PlaceLabels(BattlePlayback playback, Camera viewCamera)
        {
            foreach (var view in playback.Views)
                if (!view.IsDead) names.Place(view.BarsPosition, viewCamera, view.DisplayName);

            foreach (var text in playback.FloatingTexts)
            {
                var label = numbers.Place(text.Position, viewCamera, text.Text);
                if (label == null) continue;
                var alpha = 1f - Mathf.Clamp01((text.Age - BattlePlayback.FloatingTextSeconds * 0.6f) / (BattlePlayback.FloatingTextSeconds * 0.4f));
                label.style.color = new Color(text.Color.r, text.Color.g, text.Color.b, alpha);
                label.EnableInClassList("floating--large", text.Large);
            }
        }
    }
}
