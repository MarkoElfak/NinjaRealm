using System;
using System.Collections.Generic;
using NinjaRealm.Combat;
using NinjaRealm.Core.Content;
using NinjaRealm.Core.Definitions;
using NinjaRealm.Core.Fights;
using UnityEngine;

namespace NinjaRealm.Presentation.Battle
{
    /// <summary>One ninja in a demo lineup, placed on its team's half of the board.</summary>
    [Serializable]
    public sealed class LineupEntry
    {
        public NinjaDefinition ninja;

        [Range(0, 1)] public int team;

        [Range(0, 6), Tooltip("0 is the left edge of the board, as the camera sees it.")]
        public int column = 3;

        [Range(0, 3), Tooltip("0 is the team's back row, 3 its front row.")]
        public int rowFromBack = 3;

        public List<WeaponDefinition> weapons = new List<WeaponDefinition>();
    }

    /// <summary>
    /// Sets up a fight from a lineup and plays it on the board. Prototype entry point for the
    /// battle scene until the lineup screen (step 5) and enemy lineups (step 6) exist.
    /// </summary>
    public sealed class BattleDemo : MonoBehaviour
    {
        [SerializeField, Tooltip("Optional. Falls back to GameBootstrap.Content.")]
        ContentDatabase content;

        [SerializeField] HexBoardView board;
        [SerializeField] BattlePlayback playback;

        [SerializeField, Tooltip("0 picks a new random seed for every fight. Any other value replays the same fight.")]
        int seed;

        [SerializeField, Tooltip("Leave empty to use the built-in demo lineup.")]
        List<LineupEntry> lineup = new List<LineupEntry>();

        public int CurrentSeed { get; private set; }

        public BattlePlayback Playback => playback;

        public ContentDatabase Content => content != null ? content : GameBootstrap.Content;

        void Start()
        {
            StartBattle(seed != 0 ? seed : NewSeed());
        }

        /// <summary>Plays the same fight again from the start.</summary>
        public void Replay() => StartBattle(CurrentSeed);

        /// <summary>Plays a new fight with the same lineup and a different seed.</summary>
        public void NextFight() => StartBattle(NewSeed());

        public void StartBattle(int fightSeed)
        {
            var database = Content;
            if (database == null)
            {
                Debug.LogError("[BattleDemo] No ContentDatabase. Assign one, or add a GameBootstrap to the scene.", this);
                return;
            }

            if (!database.IsInitialized) database.Initialize();

            CurrentSeed = fightSeed;
            var builder = CombatSetupBuilder.FromDatabase(database);
            var entries = lineup.Count > 0 ? lineup : DefaultLineup(database);
            foreach (var entry in entries)
            {
                if (entry == null || entry.ninja == null) continue;
                builder.Add(entry.team, entry.ninja, builder.TeamPosition(entry.team, entry.column, entry.rowFromBack),
                    entry.weapons.ToArray());
            }

            var setup = builder.Build();
            board.Build(setup.BoardColumns, setup.BoardRows);

            var simulation = new CombatSimulation(setup, (ulong)(uint)fightSeed);
            var views = new List<UnitView>();
            for (var i = 0; i < setup.Units.Count; i++)
            {
                var spec = setup.Units[i];
                var ninja = database.Get<NinjaDefinition>(spec.DefinitionId);
                var accent = ninja.Element != null ? ninja.Element.Color : Color.grey;
                views.Add(UnitView.Create(playback.transform, i, spec.Team, ninja.DisplayName, accent, board.ToWorld(spec.Position)));
            }

            playback.Begin(simulation, board, views, NameOf);
        }

        public string NameOf(string id)
        {
            var database = Content;
            return database != null && database.TryGet<ContentDefinition>(id, out var definition) ? definition.DisplayName : id;
        }

        static int NewSeed() => new System.Random().Next(1, int.MaxValue);

        /// <summary>Three Fire ninjas against a Water and Earth team, with a few weapons.</summary>
        static List<LineupEntry> DefaultLineup(ContentDatabase database)
        {
            var result = new List<LineupEntry>();

            void Add(int team, string ninja, int column, int row, params string[] weapons)
            {
                if (!database.TryGet<NinjaDefinition>("ninja." + ninja, out var definition)) return;
                var entry = new LineupEntry { ninja = definition, team = team, column = column, rowFromBack = row };
                foreach (var weapon in weapons)
                    if (database.TryGet<WeaponDefinition>("weapon." + weapon, out var weaponDefinition))
                        entry.weapons.Add(weaponDefinition);
                result.Add(entry);
            }

            Add(0, "jiro", 3, 3, "iron_plate");
            Add(0, "kaito", 2, 3, "shadow_katana");
            Add(0, "goro", 4, 3);
            Add(0, "mei", 4, 1, "dragon_fan");
            Add(0, "saya", 2, 0);

            Add(1, "takeshi", 3, 3);
            Add(1, "daichi", 2, 3);
            Add(1, "goro", 4, 2, "kusarigama");
            Add(1, "hana", 3, 0);
            Add(1, "nami", 1, 1, "wind_boots");
            return result;
        }
    }
}
