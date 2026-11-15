using System;
using System.Collections.Generic;
using NinjaRealm.Combat;
using NinjaRealm.Core.Content;
using NinjaRealm.Core.Definitions;
using NinjaRealm.Core.Fights;
using NinjaRealm.Core.Lineups;
using NinjaRealm.Core.Progression;
using UnityEngine;

namespace NinjaRealm.Presentation.Battle
{
    public enum BattlePhase
    {
        /// <summary>The player arranges their lineup and picks an opponent.</summary>
        Setup,

        /// <summary>The fight plays.</summary>
        Fight,

        /// <summary>The fight is over and the result screen shows.</summary>
        Result,
    }

    /// <summary>
    /// Runs the prototype battle loop: lineup setup, the fight, and the result. The screens
    /// (LineupScreen, RosterScreen, BattleHud, ResultScreen) read and drive it. In play mode the
    /// lineup is built from the ninjas and weapons in the player's save (<see cref="GameProfile"/>),
    /// and every fight pays its rewards into it.
    /// </summary>
    public sealed class BattleController : MonoBehaviour
    {
        [SerializeField, Tooltip("Optional. Falls back to GameBootstrap.Content.")]
        ContentDatabase content;

        [SerializeField] HexBoardView board;
        [SerializeField] BattlePlayback playback;

        [SerializeField, Tooltip("0 picks a new random seed for every fight. Any other value makes every fight use it.")]
        int seed;

        readonly List<UnitView> previewViews = new List<UnitView>();
        readonly Dictionary<LineupSlot, UnitView> playerViews = new Dictionary<LineupSlot, UnitView>();
        readonly List<LineupDefinition> opponents = new List<LineupDefinition>();
        bool initialized;
        bool previewDirty;
        bool lineupDirty;
        bool payOnFinish;
        LineupDefinition lastOpponent;

        public BattlePhase Phase { get; private set; } = BattlePhase.Setup;

        public PlayerLineup Lineup { get; private set; }

        public LineupDefinition Opponent { get; private set; }

        public IReadOnlyList<LineupDefinition> Opponents => opponents;

        /// <summary>Both teams' ninjas shown on the board during setup.</summary>
        public IReadOnlyList<UnitView> PreviewViews => previewViews;

        public int CurrentSeed { get; private set; }

        public BattlePlayback Playback => playback;

        public HexBoardView Board => board;

        public ContentDatabase Content => content != null ? content : GameBootstrap.Content;

        /// <summary>The player's save. Null outside play mode or without progression content; then every ninja and weapon is available.</summary>
        public ProfileService Profile { get; private set; }

        /// <summary>What the last fight paid, or null when it paid nothing (a replay, or no save).</summary>
        public FightRewards LastRewards { get; private set; }

        /// <summary>The roster screen is open over the lineup screen.</summary>
        public bool RosterOpen { get; set; }

        /// <summary>Raised when the phase changes.</summary>
        public event Action<BattlePhase> PhaseChanged;

        void Awake()
        {
            // The screens are added here so the scene only needs this component and the HUD.
            if (GetComponent<LineupScreen>() == null) gameObject.AddComponent<LineupScreen>();
            if (GetComponent<ResultScreen>() == null) gameObject.AddComponent<ResultScreen>();
            if (GetComponent<RosterScreen>() == null) gameObject.AddComponent<RosterScreen>();
        }

        void Start()
        {
            if (!Initialize()) return;
            LoadSaved();
            EnterSetup();
        }

        void OnEnable()
        {
            if (playback != null) playback.Finished += OnFightFinished;
        }

        void OnDisable()
        {
            if (playback != null) playback.Finished -= OnFightFinished;
        }

        /// <summary>Loads the content and the opponent list. Safe to call more than once.</summary>
        public bool Initialize()
        {
            if (initialized) return true;

            var database = Content;
            if (database == null)
            {
                Debug.LogError("[Battle] No ContentDatabase. Assign one, or add a GameBootstrap to the scene.", this);
                enabled = false;
                return false;
            }

            if (!database.IsInitialized) database.Initialize();

            opponents.Clear();
            opponents.AddRange(database.GetAll<LineupDefinition>());
            opponents.Sort((a, b) => a.Difficulty != b.Difficulty ? a.Difficulty.CompareTo(b.Difficulty) : string.CompareOrdinal(a.Id, b.Id));
            Opponent = opponents.Count > 0 ? opponents[0] : null;

            if (Application.isPlaying) Profile = GameProfile.Load(database);

            Lineup = new PlayerLineup(Profile != null ? Profile.TeamSize : LineupRules.MaxUnits);
            Lineup.Changed += () =>
            {
                previewDirty = true;
                lineupDirty = true;
            };
            initialized = true;
            return true;
        }

        void Update()
        {
            if (lineupDirty) Save();
            if (Phase != BattlePhase.Setup) return;

            if (previewDirty) RebuildPreview();

            var viewCamera = Camera.main;
            foreach (var view in previewViews)
                if (view != null) view.Tick(Time.deltaTime, viewCamera);
        }

        // ---------------------------------------------------------------- setup

        public void EnterSetup()
        {
            payOnFinish = false;
            playback.Clear();
            if (Profile != null)
            {
                Profile.RefreshDojoIfNewDay(DateTime.Now);
                FitLineupToProfile();
            }

            board.Build(LineupRules.Columns, LineupRules.RowsPerTeam * 2);
            SetPhase(BattlePhase.Setup);
            RebuildPreview();
        }

        public void SelectOpponent(LineupDefinition opponent)
        {
            if (opponent == null || opponent == Opponent) return;
            Opponent = opponent;
            previewDirty = true;
            lineupDirty = true;
        }

        /// <summary>Keeps the lineup to the ninjas and weapons the player owns and the team size. Call after the roster changes.</summary>
        public void FitLineupToProfile()
        {
            if (Profile == null || Lineup == null) return;
            Profile.FitLineup(Lineup);
            previewDirty = true;
        }

        /// <summary>A ninja's level and stars in the player's save, or the base rank without one.</summary>
        public NinjaRank RankOf(NinjaDefinition ninja) => Profile != null ? Profile.RankOf(ninja) : NinjaRank.Base;

        /// <summary>The player's ninja view for a slot, while in setup.</summary>
        public UnitView ViewFor(LineupSlot slot) => slot != null && playerViews.TryGetValue(slot, out var view) ? view : null;

        /// <summary>World position of a hex on the player's half.</summary>
        public Vector3 PlayerHexToWorld(int column, int rowFromBack) => board.ToWorld(new HexCoord(column, rowFromBack));

        /// <summary>Converts a board hex to a place on the player's half. False for the enemy half.</summary>
        public bool IsPlayerHex(HexCoord hex, out int column, out int rowFromBack)
        {
            column = hex.Column;
            rowFromBack = hex.Row;
            return LineupRules.IsOnBoard(column, rowFromBack);
        }

        public void RebuildPreview()
        {
            previewDirty = false;
            foreach (var view in previewViews)
                if (view != null) PrototypeVisuals.DestroySafe(view.gameObject);
            previewViews.Clear();
            playerViews.Clear();
            if (Phase != BattlePhase.Setup || Lineup == null) return;

            var builder = new CombatSetupBuilder(Array.Empty<StatDefinition>());
            var index = 0;
            foreach (var slot in Lineup.Slots)
            {
                var view = CreateView(index++, 0, slot.ninja, builder.TeamPosition(0, slot.column, slot.rowFromBack));
                view.SetBars(1f, 0f, 0f);
                playerViews[slot] = view;
                previewViews.Add(view);
            }

            if (Opponent != null)
                foreach (var slot in Opponent.Slots)
                {
                    if (slot?.ninja == null) continue;
                    var view = CreateView(index++, 1, slot.ninja, builder.TeamPosition(1, slot.column, slot.rowFromBack));
                    view.SetBars(1f, 0f, 0f);
                    previewViews.Add(view);
                }
        }

        UnitView CreateView(int index, int team, NinjaDefinition ninja, HexCoord hex)
        {
            var accent = ninja.Element != null ? ninja.Element.Color : Color.grey;
            return UnitView.Create(playback.transform, index, team, ninja.DisplayName, accent, board.ToWorld(hex), ninja.ModelPrefab);
        }

        // ---------------------------------------------------------------- fight

        public bool CanFight => Phase == BattlePhase.Setup && Lineup != null && Lineup.Slots.Count > 0 && Opponent != null;

        /// <summary>Starts a fight between the player's lineup and the chosen opponent.</summary>
        public void StartFight() => StartFight(seed != 0 ? seed : NewSeed());

        public void StartFight(int fightSeed)
        {
            if (!Initialize() || Lineup.Slots.Count == 0 || Opponent == null) return;
            Save();
            StartFight(Lineup.Slots, Opponent, fightSeed, true);
        }

        /// <summary>
        /// Starts a fight between any player lineup and an enemy lineup. The player's ninjas fight
        /// at their level and stars in the save, the enemy's at the lineup's level. When
        /// <paramref name="payRewards"/> is set, the result is paid into the save. Used by the
        /// screens and by editor tools.
        /// </summary>
        public void StartFight(IReadOnlyList<LineupSlot> playerSlots, LineupDefinition opponent, int fightSeed, bool payRewards = false)
        {
            if (!Initialize()) return;

            foreach (var view in previewViews)
                if (view != null) PrototypeVisuals.DestroySafe(view.gameObject);
            previewViews.Clear();
            playerViews.Clear();

            var database = Content;
            CurrentSeed = fightSeed;
            var builder = CombatSetupBuilder.FromDatabase(database);
            builder.AddLineup(0, playerSlots, RankOf);
            var enemyRank = new NinjaRank(opponent != null ? opponent.Level : 1, 1);
            if (opponent != null) builder.AddLineup(1, opponent.Slots, _ => enemyRank);
            var setup = builder.Build();
            board.Build(setup.BoardColumns, setup.BoardRows);

            var simulation = new CombatSimulation(setup, (ulong)(uint)fightSeed);
            var views = new List<UnitView>();
            for (var i = 0; i < setup.Units.Count; i++)
            {
                var spec = setup.Units[i];
                views.Add(CreateView(i, spec.Team, database.Get<NinjaDefinition>(spec.DefinitionId), spec.Position));
            }

            lastPlayerSlots = CopySlots(playerSlots);
            lastOpponent = opponent;
            payOnFinish = payRewards && Profile != null;
            LastRewards = null;
            SetPhase(BattlePhase.Fight);
            playback.Paused = false;
            playback.Begin(simulation, board, views, NameOf);
        }

        List<LineupSlot> lastPlayerSlots = new List<LineupSlot>();

        static List<LineupSlot> CopySlots(IReadOnlyList<LineupSlot> slots)
        {
            var copy = new List<LineupSlot>(slots.Count);
            foreach (var slot in slots)
                if (slot != null) copy.Add(slot.Clone());
            return copy;
        }

        /// <summary>Plays the last fight again with the same seed, to watch it. A replay pays nothing.</summary>
        public void Replay() => StartFight(lastPlayerSlots, lastOpponent, CurrentSeed);

        /// <summary>Plays the last matchup again with a new seed. It pays like any fight.</summary>
        public void FightAgain() => StartFight(lastPlayerSlots, lastOpponent, seed != 0 ? seed : NewSeed(), true);

        /// <summary>True when the village scene can be loaded, so the screens show a way back.</summary>
        public bool CanLeaveToVillage => Application.isPlaying && GameScenes.CanLoad(GameScenes.Village);

        /// <summary>Saves the lineup and goes back to the village.</summary>
        public void LeaveToVillage()
        {
            Save();
            GameScenes.Load(GameScenes.Village);
        }

        /// <summary>Jumps straight to the end of the fight.</summary>
        public void SkipFight()
        {
            if (Phase == BattlePhase.Fight) playback.SkipToEnd();
        }

        void OnFightFinished(CombatResult result)
        {
            if (Phase != BattlePhase.Fight) return;
            if (payOnFinish && Profile != null)
                LastRewards = Profile.ApplyFightResult(lastOpponent, result.WinningTeam == 0, CurrentSeed);
            payOnFinish = false;
            SetPhase(BattlePhase.Result);
        }

        void SetPhase(BattlePhase phase)
        {
            Phase = phase;
            board.Highlight(null, false);
            PhaseChanged?.Invoke(phase);
        }

        // ---------------------------------------------------------------- names and saving

        public string NameOf(string id)
        {
            var database = Content;
            return database != null && database.TryGet<ContentDefinition>(id, out var definition) ? definition.DisplayName : id;
        }

        void LoadSaved()
        {
            var database = Content;
            if (Profile != null)
            {
                Lineup.LoadSaved(Profile.Profile.lineup, database);
                if (Lineup.Slots.Count == 0) Lineup.SetSlots(StarterLineup(Profile.OwnedNinjas()));
                SelectSavedOpponent(Profile.Profile.opponent);
            }
            else
            {
                Lineup.SetSlots(StarterLineup(database.GetAll<NinjaDefinition>()));
            }

            lineupDirty = false;
        }

        void SelectSavedOpponent(string opponentId)
        {
            foreach (var opponent in opponents)
                if (opponent.Id == opponentId) Opponent = opponent;
        }

        void Save()
        {
            lineupDirty = false;
            if (Profile != null && Lineup != null) Profile.SaveLineup(Lineup, Opponent);
        }

        /// <summary>The first ninjas available, along the front and back rows, so the first press of Fight shows something.</summary>
        static IEnumerable<LineupSlot> StarterLineup(IEnumerable<NinjaDefinition> ninjas)
        {
            var places = new[] { (3, 3), (2, 3), (3, 0), (4, 1), (4, 3), (2, 0) };
            var index = 0;
            foreach (var ninja in ninjas)
            {
                if (index >= places.Length) yield break;
                var (column, row) = places[index++];
                yield return new LineupSlot(ninja, column, row);
            }
        }

        static int NewSeed() => new System.Random().Next(1, int.MaxValue);
    }
}
