using System;
using System.Collections.Generic;
using NinjaRealm.Combat;
using NinjaRealm.Core.Campaign;
using NinjaRealm.Core.Content;
using NinjaRealm.Core.Definitions;
using NinjaRealm.Core.Fights;
using NinjaRealm.Core.Lineups;
using NinjaRealm.Core.Progression;
using NinjaRealm.Presentation.Audio;
using UnityEngine;

namespace NinjaRealm.Presentation.Battle
{
    public enum BattlePhase
    {
        /// <summary>The player arranges their lineup against the stage's enemies.</summary>
        Setup,

        /// <summary>The fight plays.</summary>
        Fight,

        /// <summary>The fight is over and the result screen shows.</summary>
        Result,
    }

    /// <summary>
    /// Runs the prototype battle loop: lineup setup against a campaign stage, the fight, and the
    /// result. The screens (LineupScreen, RosterScreen, BattleHud, ResultScreen) read and drive
    /// it. In play mode the lineup is built from the ninjas and weapons in the player's save
    /// (<see cref="GameProfile"/>), the stage is the one picked there, and every fight pays its
    /// rewards and stars into it.
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
        bool initialized;
        bool previewDirty;
        bool lineupDirty;
        bool payOnFinish;
        LineupDefinition lastOpponent;
        StageDefinition lastStage;

        public BattlePhase Phase { get; private set; } = BattlePhase.Setup;

        public PlayerLineup Lineup { get; private set; }

        /// <summary>The campaign's chapters and stages in play order.</summary>
        public CampaignMap Campaign { get; private set; }

        /// <summary>The stage being set up for, or fought.</summary>
        public StageDefinition Stage { get; private set; }

        /// <summary>The enemy lineup on the board: the stage's, or the one an editor tool started a fight against.</summary>
        public LineupDefinition Opponent { get; private set; }

        /// <summary>Stars the last fight earned. 0 for a loss, a draw or a fight outside the campaign.</summary>
        public int LastStars { get; private set; }

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
            MusicPlayer.Play(MusicPlayer.Battle);
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

        /// <summary>Loads the content, the save and the stage to fight. Safe to call more than once.</summary>
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

            if (Application.isPlaying) Profile = GameProfile.Load(database);

            // Without a save every stage is open and the first one is picked.
            Campaign = Profile != null ? Profile.Campaign : new CampaignMap(database);
            Stage = Profile != null ? Profile.CurrentStage() : Campaign.Stages.Count > 0 ? Campaign.Stages[0] : null;
            Opponent = Stage != null ? Stage.Lineup : null;

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

            Opponent = Stage != null ? Stage.Lineup : null;
            board.Build(LineupRules.Columns, LineupRules.RowsPerTeam * 2);
            SetPhase(BattlePhase.Setup);
            RebuildPreview();
        }

        /// <summary>True when the stage can be fought: always without a save, otherwise once the save has unlocked it.</summary>
        public bool IsUnlocked(StageDefinition stage) =>
            stage != null && Campaign != null && Campaign.Contains(stage) && (Profile == null || Profile.IsUnlocked(stage));

        /// <summary>Picks the stage to set up for. Locked stages are refused.</summary>
        public bool SelectStage(StageDefinition stage)
        {
            if (Phase != BattlePhase.Setup || !IsUnlocked(stage)) return false;
            if (stage == Stage) return true;
            Stage = stage;
            Opponent = stage.Lineup;
            Profile?.SelectStage(stage);
            previewDirty = true;
            return true;
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

        public bool CanFight => Phase == BattlePhase.Setup && Lineup != null && Lineup.Slots.Count > 0 && Stage != null && Stage.Lineup != null;

        /// <summary>Starts a fight between the player's lineup and the picked stage.</summary>
        public void StartFight() => StartFight(seed != 0 ? seed : NewSeed());

        public void StartFight(int fightSeed)
        {
            if (!Initialize() || Lineup.Slots.Count == 0 || Stage == null || Stage.Lineup == null) return;
            Save();
            StartFight(Lineup.Slots, Stage, fightSeed, true);
        }

        /// <summary>
        /// Starts a fight between any player lineup and a stage. The player's ninjas fight at
        /// their level and stars in the save, the enemy's at the stage's level, its boss with the
        /// boss stars. When <paramref name="payRewards"/> is set, the result and its stars are
        /// paid into the save.
        /// </summary>
        public void StartFight(IReadOnlyList<LineupSlot> playerSlots, StageDefinition stage, int fightSeed, bool payRewards = false)
        {
            if (stage == null || stage.Lineup == null) return;
            StartFight(playerSlots, stage.Lineup, stage, stage.RankOf, fightSeed, payRewards);
        }

        /// <summary>Starts a fight against a lineup outside the campaign, at level 1. It pays nothing. Used by editor tools.</summary>
        public void StartFight(IReadOnlyList<LineupSlot> playerSlots, LineupDefinition opponent, int fightSeed) =>
            StartFight(playerSlots, opponent, null, _ => NinjaRank.Base, fightSeed, false);

        void StartFight(IReadOnlyList<LineupSlot> playerSlots, LineupDefinition opponent, StageDefinition stage,
            Func<NinjaDefinition, NinjaRank> enemyRank, int fightSeed, bool payRewards)
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
            if (opponent != null) builder.AddLineup(1, opponent.Slots, enemyRank);
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
            lastStage = stage;
            Opponent = opponent;
            payOnFinish = payRewards && Profile != null && stage != null;
            LastRewards = null;
            LastStars = 0;
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
        public void Replay() => StartLast(CurrentSeed, false);

        /// <summary>Plays the last matchup again with a new seed. It pays like any fight.</summary>
        public void FightAgain() => StartLast(seed != 0 ? seed : NewSeed(), true);

        void StartLast(int fightSeed, bool payRewards)
        {
            if (lastStage != null) StartFight(lastPlayerSlots, lastStage, fightSeed, payRewards);
            else if (lastOpponent != null) StartFight(lastPlayerSlots, lastOpponent, fightSeed);
        }

        /// <summary>The stage after the one just fought, when the result screen can offer it: it exists and is unlocked.</summary>
        public StageDefinition NextStage
        {
            get
            {
                var next = Campaign != null ? Campaign.Next(lastStage != null ? lastStage : Stage) : null;
                return IsUnlocked(next) ? next : null;
            }
        }

        /// <summary>Goes back to the lineup with the next stage picked.</summary>
        public void GoToNextStage()
        {
            var next = NextStage;
            EnterSetup();
            if (next != null) SelectStage(next);
        }

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
            LastStars = lastStage != null ? StageStars.For(result) : 0;
            if (payOnFinish && Profile != null)
                LastRewards = Profile.ApplyStageResult(lastStage, LastStars, CurrentSeed);
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
            }
            else
            {
                Lineup.SetSlots(StarterLineup(database.GetAll<NinjaDefinition>()));
            }

            lineupDirty = false;
        }

        void Save()
        {
            lineupDirty = false;
            if (Profile != null && Lineup != null) Profile.SaveLineup(Lineup);
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
