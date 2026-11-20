# Ninja Realm architecture

Ninja Realm is a 3D low poly auto battler built in Unity 6000.4 with URP. The full game design lives in the design doc. This file covers how the code is organized so the game stays easy to grow.

## Core rule

**Content is data, systems are code.** Every clan, ninja, trait, skill, weapon, recipe, resource, building and mode is a `ContentDefinition` ScriptableObject with a stable string ID such as `ninja.kaito`. Code looks content up by ID and never lists it by name. Adding content means adding assets, not editing code.

## Folder layout

```
Assets/
  _Project/
    Scripts/
      Core/          NinjaRealm.Core: content IDs, ContentDefinition, ContentDatabase, validation,
                     lineups, CombatSetupBuilder (content assets into fight setups), the balance
                     runner, the campaign, progression and the save file, and the prototype content
      Combat/        NinjaRealm.Combat: deterministic fight simulation, pure C# with no UnityEngine
      Presentation/  NinjaRealm.Presentation: MonoBehaviours, scenes, UI, playback of combat events
      Editor/        NinjaRealm.Editor: menus, validation and import tools (editor only)
    Tests/
      EditMode/      NinjaRealm.Tests.EditMode: NUnit tests run by the Unity Test Runner
    Content/         ContentDatabase.asset and content assets, created as they are added
    UI/              UI Toolkit: Styles/ (USS) and Resources/UI/ (UXML layouts and the theme)
    Audio/           Resources/Music/ (the two music loops) and Resources/Voices/ (the voice lines)
  Scenes/            Unity scenes
  Settings/          URP pipeline, renderer and volume assets
```

## Assembly dependencies

```
Presentation ──> Core
Presentation ──> Combat
Editor       ──> Core, Combat, Presentation
Tests        ──> Core, Combat, Presentation
Core         ──> Combat
Combat       ──> nothing (noEngineReferences: true)
```

Combat stays free of UnityEngine so fights can run headless, in tests, in balance scripts and on a server. Presentation turns combat results into visuals and never changes the outcome of a fight.

Core references Combat, not the other way round: the shared effect enums (`EffectScope`, `TargetKind`, `StatusKind`...) live in Combat, and Core converts its assets into Combat's plain data.

## Content IDs

* Format: lowercase `category.name`, with optional extra segments and underscores: `ninja.kaito`, `weapon.shadow_katana`, `trait.element.fire`.
* A definition type can require a category by overriding `IdCategory`.
* Never change an ID after it has shipped. Saves, recipes and server data point to it.

## Content database

* `ContentDatabase` is one asset holding every definition, sorted by ID.
* Run **Ninja Realm > Content > Rebuild Database** after adding, removing or renaming content assets. The first run creates `Assets/_Project/Content/ContentDatabase.asset`.
* **Ninja Realm > Content > Validate** reports invalid, duplicate or miscategorized IDs, plus any checks a definition type adds in `ValidateSelf()`.
* At runtime, `GameBootstrap` in the first scene initializes the database and exposes it as `GameBootstrap.Content`.

## Definition types

All live in `NinjaRealm.Core.Definitions` and are created from **Assets > Create > Ninja Realm**.

| Type | ID prefix | What it holds |
| --- | --- | --- |
| `StatDefinition` | `stat.` | A combat stat and its default value |
| `TraitDefinition` | `trait.` | An element or class, with tiers (required count + effects) |
| `SkillDefinition` | `skill.` | A ninja's active skill: a list of actions |
| `NinjaDefinition` | `ninja.` | Rarity, element, class, skill and base stats |
| `WeaponDefinition` | `weapon.` | A component, or a combined weapon naming its two components |
| `LineupDefinition` | `lineup.` | An enemy lineup: description and slots (ninja, hex, weapons) |
| `StageDefinition` | `stage.` | One campaign fight: a lineup, the level it fights at, its kind (normal, elite, boss), its reward tier, an optional boss with stars, and what the first clear pays (in `NinjaRealm.Core.Campaign`) |
| `ChapterDefinition` | `chapter.` | A chapter of the campaign: its number, its stages in order and its star chests (in `NinjaRealm.Core.Campaign`) |
| `RuneDefinition` | `rune.` | A Shrine rune: rarity, family, the element a Seal covers, colour, and its ranks (passives plus Gold, clan XP and income bonuses per rank) |
| `ResourceDefinition` | `resource.` | A currency: Gold, Karma, Steel (in `NinjaRealm.Core.Progression`) |
| `ProgressionDefinition` | `progression.` | Every progression number: the new save, level costs, belts, stars, the Dojo, forging, fight rewards, clan levels, team sizes and the Shrine's runes (in `NinjaRealm.Core.Progression`) |

## Effects

Behaviour is built from small serialized effect blocks (`NinjaRealm.Core.Definitions.Effects`), stored with `[SerializeReference]`. The `[SubclassSelector]` attribute adds a type dropdown in the inspector.

* **Passive effects** (traits, weapons) have `holders` saying who carries them: `Self`, `TraitMembers`, `AllAllies`, `OtherAllies` or `Team`.
  * `StatModifierEffect`: a flat or percentage stat change.
  * `TriggeredEffect`: runs actions on fight start, on attack hit, on cast, every N seconds, or below a health threshold.
* **Action effects** (skills, triggers) have a `TargetSelector`.
  * `DamageAction`, `HealAction`, `ShieldAction`, `ApplyStatusAction` (stun, taunt, burn, slow, healing reduction, damage reduction) and `StatBuffAction`.

A new mechanic means one new effect class plus its handling in the combat simulation. After that, any ninja, trait or weapon can use it from the inspector.

Authoring values are floats for easy editing. Each effect's `ToSpec()` converts it to the combat simulation's fixed-point data when a fight is built, so results stay deterministic.

## Combat simulation

`NinjaRealm.Combat.CombatSimulation` runs one fight from a `FightSetup` and a seed.

* **Board**: hexes in odd-r offset coordinates (`HexCoord`), 7 columns by 8 rows. Team 0 starts on rows 0 to 3, team 1 on rows 4 to 7.
* **Time**: 20 ticks per second. `Step()` advances one tick, so presentation can play a fight back at any speed; `Run()` plays it to the end.
* **Numbers**: integers in thousandths (`Fixed`): 650 health is 650000, 25% is 25000. Durations are in ticks.
* **Each tick**: burns tick and expired statuses, shields and buffs are removed, overtime is checked, periodic triggers fire, then every living unit acts in index order.
* **A unit's turn**: stunned units do nothing. Otherwise it continues a cast, or picks a target (a taunter first, then its current target, then the nearest enemy), casts if its mana is full, attacks if the target is in range, or moves one hex closer.
* **Mana**: +10 per attack, and up to +15 when hit (scaled by damage taken as a share of max health).
* **Damage**: physical uses armor and magic uses magic resist (`damage x 100 / (100 + resist)`); true damage ignores both. Shields absorb damage before health. Omnivamp heals the attacker.
* **Fight start**: weapon passives apply, then each trait counts the distinct ninjas holding it per team and switches on its highest reached tier. Health and starting mana are set after all bonuses.
* **Overtime**: after 30 seconds, every 5 seconds all damage rises by 35% and all healing and shields fall by 25% (no healing at all after 20 seconds of overtime), so sustain teams can't stall. At 45 seconds the team with the larger share of its total health left wins; an exact tie is a draw.
* **Output**: `Events` lists everything that happened (moves, attacks, damage, casts, statuses, deaths, trait activations) for playback and the damage log. `Result` has the winner, duration and per-unit totals.

`NinjaRealm.Core.Fights.CombatSetupBuilder` builds a `FightSetup` from content: add each ninja with a team, a hex (`TeamPosition` flips team 1's half) and its weapons, then call `Build()`.

```csharp
var builder = CombatSetupBuilder.FromDatabase(GameBootstrap.Content);
builder.Add(0, kaito, builder.TeamPosition(0, 3, 3), shadowKatana);
builder.Add(1, hana, builder.TeamPosition(1, 3, 3));
var result = new CombatSimulation(builder.Build(), seed).Run();
```

`AddLineup(team, slots, rankOf)` adds a whole lineup at once, using `TeamPosition` for each slot. A ninja's `NinjaRank` (level and stars) multiplies its base health and attack damage by `ProgressionDefinition.StatMultiplier`, in whole basis points; `FromDatabase` picks up the progression asset, and without one every ninja fights at its base stats.

Known limits for now: a timed buff to health does not change max health, and units do not path around blockers (they sidestep one hex at a time).

## Lineups

`NinjaRealm.Core.Lineups` holds what both sides of a fight bring to the board.

* `LineupSlot`: a ninja, a hex on its team's half (column 0 to 6, `rowFromBack` 0 to 3, where 3 is the front row) and up to three weapons.
* `LineupRules`: the limits (7 columns, 4 rows, 6 ninjas, 3 weapons each) and `Validate`, which lists every problem with a lineup.
* `LineupSynergies.Count`: counts each trait once per different ninja, the same way the simulation does.
* `PlayerLineup`: the lineup the player edits. `Place`, `Move` (swaps when the hex is taken), `Remove`, `Equip` and `Unequip` all keep the rules, and `Changed` fires after each edit. `ToJson` and `LoadJson` save it by content ID; unknown IDs are skipped so old saves keep loading.
* `LineupDefinition`: an enemy lineup as a content asset. It says who stands where and with which weapons; the stage that uses it says at what level.

## Campaign

`NinjaRealm.Core.Campaign` is the single-player path: chapters of stages, fought in order.

* `StageDefinition` is one fight. `RankOf(ninja)` gives the level and stars an enemy fights at: the stage's level, and the boss stars for the stage's boss. The lineup holds no level, so one lineup can come back in a later stage.
* `ChapterDefinition` lists its stages in order and its `StarChest`s (stars needed, and Gold, Steel or a weapon inside).
* `CampaignMap` puts the chapters in order of their number and gives every stage its place: `Stages`, `Previous`, `Next`, `ChapterOf` and `LabelOf` ("2-4").
* `StageStars.For` turns a finished fight into stars: 1 for a win, 2 with at least half the team standing, 3 with nobody fallen. A loss or a draw is 0.
* The save keeps the best stars per stage, the opened chests and the stage picked for the next fight. `ProfileService` has the rules: `IsUnlocked` (the first stage, a stage cleared before, or the one after a cleared stage), `CurrentStage`, `SelectStage`, `ApplyStageResult` and `TryOpenChest`.
* `ApplyStageResult(stage, stars, seed)` pays every fight by the stage's reward tier (Gold, Steel, clan XP, a chance of a component). The first clear adds the first-win Gold, a certain component, and the stage's own Karma (bosses only; validation flags any other stage that pays it), Steel and ninja. Clan levels pay no Karma. A ninja the player already owns arrives as a shard.
* `CampaignWalker` plays the whole campaign headless with a simple player (strongest ninjas, star chests opened, altar slots lit and filled with Common runes, the altar's Karma collected each day, Karma into the lowest level, Dojo offers that help, weapons handed out, farming the stage before after two losses) and reports the fights each stage and chapter took. **Ninja Realm > Balance > Walk the Campaign** writes the report to `Logs/campaign_walk.md`.

## Runes (the Shrine)

The Shrine sells runes: carved stones set into the altar's slots, each giving the player's ninjas a passive bonus in every fight. Enemies never get runes.

* `RuneDefinition` (`rune.` IDs) lists its ranks, rank I first. Each `RuneRank` holds passive effects (the same `StatModifierEffect` and `TriggeredEffect` blocks weapons use, with holders `Self`) and optional economy bonuses in basis points: `fightGold`, `clanXp` and `income`. `text` overrides the generated description for effects that can't describe themselves (triggers). A Seal names an element and only covers that element's ninjas.
* The numbers live on `ProgressionDefinition` under **Runes**: the altar's slots (`runeSlots`: the clan level that opens each and the Karma to light it), the ranks (`runeRanks`: the share of the price each costs and the clan level it needs), prices and offer weights per rarity (`runeOffers`; Common is always for sale, Legendary also costs Karma), the daily offer size and refresh cost, the sell refund, the home village Seal discount and the Legendary limit.
* **Karma.** Runes are the main source of Karma: every set rune makes `RuneOffer.karmaPerHour` (by rarity) times its rank's `RuneRankStep.karmaBasisPoints`, and the altar holds `runeKarmaStorageHours` of it. `ProfileService` has `AltarKarmaPerHour`, `PendingAltarKarma(now)`, `AltarKarmaCapacity`, `AltarKarmaTimeToFull(now)` and `TryCollectAltarKarma(now)`. A change to the set runes (set, take out, rank up, sell, a buy that sets) first banks what was made at the old rate into `runeKarma`, reading the time from `ProfileService.Clock`; tests and the walker set their own clock. The other Karma sources are a boss's first clear and `TryGrantKarmaPack(packId, transactionId)`, the hook for in-app purchases: `karmaPacks` on `ProgressionDefinition`, paid once per transaction ID (kept in the save's `purchases`).
* The save (version 4) keeps `runes` (ID, rank, Gold spent), `runeSlots` (one entry per lit slot, empty for no rune), the Shrine's `shrineOffers` and `shrineDate`, the altar's banked `runeKarma` and `runeKarmaSince`, and the paid-out `purchases`. Free slots are lit on load, so older saves start with one.
* `ProfileService` has the rules: `TryBuyRune`, `TryRankUpRune`, `TrySellRune`, `TryLightRuneSlot`, `TrySetRune`, `ClearRuneSlot`, `RefreshShrineIfNewDay` and `TryRefreshShrine`. Two set runes covering the same ninjas may not raise the same stat, and only `maxLegendaryRunes` Legendaries fit.
* Fights: `ProfileService.RunePassives(ninja)` goes to `CombatSetupBuilder.AddLineup(team, slots, rankOf, bonusesOf)` for the player's team, and the builder adds them after the weapons. Fortune runes raise the normal fight Gold and clan XP in `ApplyStageResult` (not first win Gold) and the per-hour rate of income sources.
* Art: icons are `Art/Runes/Resources/RuneIcons/<name>.png`, where the name is the ID without `rune.`; `RuneArt.Icon` loads them, and a rune's own `icon` wins when linked. `Tools/RuneIcons/make_rune_icons.py` draws them (a faceted stone in the rarity colour with a glowing glyph); a new rune needs a glyph there and a run of the script. In the village, `ShrineAltar` stands a stone per lit slot in front of the Shrine, glowing in the set rune's colour, and the rune rail down the left side of the village screen (`VillageScreen`, `runeRail` in VillageHud.uxml) lists every slot's rune, rank and bonus, with the altar's Karma rate and a Collect button at its foot; clicking anywhere else on it opens the Shrine on its Runes tab. The Shrine's name plate shows the Karma ready, like the Mint's Gold.

To add a rune: add it in `PrototypeContent` (or as an asset from **Create > Ninja Realm > Rune**), give it a glyph in the icon script, run the script, then **Reset Prototype Content to Defaults** and **Rebuild Database**.

## Progression and the save file

`NinjaRealm.Core.Progression` holds the persistent roster.

* `PlayerProfile` is everything the save holds, by content ID: the clan's name and home village, the `Wallet` (balances by resource ID), owned ninjas (level, stars, shards), weapon counts, clan level and XP, the lineup, the campaign (best stars per stage, opened chests, the stage picked), income timers, and the Dojo's offers, date and random seed.
* `SaveFile` writes it as JSON with a `version`, and runs older files through `Migrations` in order. To change the format, raise `CurrentVersion` and add one migration. A file from a newer version, or one that isn't JSON, is refused. When an older file is loaded, a copy of it is kept beside the save (`profile.v1.json`) before it is rewritten. Version 2 added the campaign: the lineups a version 1 save had beaten become cleared stages, which `ProfileService` does on load because it needs the content.
* `ISaveStorage` is where it lives: `FileSaveStorage` writes to a temporary file and then replaces the old one; `MemorySaveStorage` is for tests.
* `ProfileService` is the only thing that changes a profile: `TryLevelUp`, the rune methods, `TryRecruitOffer`, `TryRefreshDojo`, `RefreshDojoIfNewDay`, `TryBuyDirect`, `TryForge`, `ApplyStageResult`, `SelectStage`, `TryOpenChest`, `TryCollectIncome` and `SaveLineup`. Each checks its cost, changes the profile and saves; a refusal changes nothing and says why. `FitLineup` keeps a lineup to what the player owns and the team size. On load it drops unknown IDs and duplicates and keeps numbers in range, so old or damaged saves still load; an unreadable file is set aside and a new save starts.
* Dojo offers use `DeterministicRandom` with the profile's seed and roll count, and fight drops use the fight's seed, so tests can check exact results.
* **Home villages** (`HomeVillage`, listed on the progression settings) are the villages a new clan can pick: Emberfall (Fire), Mistwater (Water) and Stonehollow (Earth). `NeedsFounding` is true until the clan has a valid name (`ClanName`: 3 to 16 letters, digits, spaces, hyphens or apostrophes) and a home village; `TryFoundClan` sets both and leaves everything else as it was, so a save from before the new game screen keeps its progress. In the Dojo, a ninja of the home village's element weighs the village's `dojoAffinity` (20000, twice as likely) against 10000 for the others of its rarity. With no home village the roll is the same as before villages existed.

In the game, `GameProfile` (Presentation) loads the save from `Application.persistentDataPath/profile.json` once and shares it. **Ninja Realm > Save** shows or deletes the file, and gives test resources in play mode.

## Prototype content

`NinjaRealm.Core.Prototype.PrototypeContent` is the source of truth for the prototype's numbers: 17 stats, 3 elements, 4 classes, 12 skills, 12 ninjas, 6 components, 4 combined weapons, 24 enemy lineups, the campaign's 3 chapters of 8 stages, 3 resources and the progression settings. It writes them into an `IContentSink`:

* **Ninja Realm > Content > Create Prototype Content** saves them as assets under `Assets/_Project/Content/`. It skips assets that already exist, so it never overwrites edits.
* **Ninja Realm > Content > Reset Prototype Content to Defaults** overwrites the assets with the values in code.
* `InMemoryContentSink` and `PrototypeContent.CreateInMemoryDatabase()` keep them in memory, for tests and balance runs.

Both menu items rebuild the database. To change a number, change it in `PrototypeContent` and run Reset. The EditMode test `ProjectContentTests` validates every content asset, checks that the database is up to date, and fails if an asset differs from `PrototypeContent`, naming the field.

## Balance runner

`NinjaRealm.Core.Fights.BalanceRunner` plays many fights headless. `Run(a, b)` plays a matchup with 20 seeds from both sides of the board, `RoundRobin(teams, title)` returns a Markdown win-rate table, and `NinjaReport()` lists damage, healing and survival per ninja. `PrototypeContent.BalanceArchetypes` gives five builds to compare. The latest results and tuning notes are in `docs/BALANCE.md`.

## Village scene

`Assets/Scenes/Village.unity` is the hub the game starts in. Create or recreate it with **Ninja Realm > Scenes > Create Village Scene**, which also puts it first in the build settings with the battle scene after it.

* `VillageController` loads the save (`GameProfile`), moves the camera (drag to pan, scroll to zoom) and opens what is clicked: a building's panel, or the details of a ninja on the plaza. Esc closes it.
* `VillageBuilding` marks a clickable building and says what it is (`VillageBuildingKind`): the **Command Hall** shows the campaign map and goes to the battle scene, the **Dojo** recruits and shows every ninja, the **Shrine** levels up ninjas, the **Blacksmith** forges, and the **Mint** and **Steel Mine** make Gold and Steel over time (see Income below). A building without a Meshy model gets a placeholder house built in code in play mode, and a box collider round whatever it shows.
* `VillageNinjas` stands the player's ninjas on the plaza in rows: every ninja they own, so every ninja they can put in a fight (every ninja in the content when there is no save). Each is a `UnitView` without its ring and bars, 2.4 times its board size, facing the camera, with a capsule collider so it can be clicked. A new recruit joins them when the Dojo closes.
* `NewGameScreen` covers the village while the save `NeedsFounding`: a name field with a random name button, a card per home village (its element, what it does in the Dojo, its element's first synergy and its ninjas), and **Found the clan**. Afterwards the top bar reads "Silent Moon of Mistwater", and the fight HUD and result log use the clan's name. **Ninja Realm > Village > Open New Game Screen** shows it again in play mode, which renames the clan or moves it; **Found a Random Clan** and **Forget the Clan's Name and Village** found or clear one without typing.
* `VillageScreen` is the village's UI (see UI below): the clan, its village and the wallet along the top, a name over each building and plaza ninja, and the open panel. The Command Hall is `CommandHallView`; the Dojo, Shrine and Blacksmith show `RosterViews`, the same views as the battle scene's roster screen (Tab).
* **Ninja Realm > Village** (editor menu, while the Village scene plays) opens each panel (Command Hall, Dojo, Shrine, Blacksmith, Mint, Steel Mine), a chapter of the campaign map or a plaza ninja, and goes to the battle scene, so a panel can be checked without clicking through the scene.
* The Command Hall's panel is the campaign map: a tab per chapter with its stars, a card per stage (level, best stars, locked or not), the chapter's three star chests with an Open button, and the picked stage's enemy, pay and first clear rewards with the **To battle** button.
* The Dojo has two tabs: **Recruits**, and **All ninjas**, a hall with a card for every ninja in the content (owned or not), the picked ninja's model, its stats and skill, and how to get it.
* Clicking a ninja on the plaza opens its details with its model and a level up button (`RosterViews.Details`).
* The banners in the village take the home village's colour: `VillageController.ApplyHomeVillage` gives every object named Banner... the texture `Art/Village/Resources/VillageBanners/Banner_<village id>` (red for Emberfall, blue for Mistwater, ochre for Stonehollow), and the top bar's stripe is the village's element colour.
* `NinjaPreview` is what puts a model in a panel: a small stage 600 m from the village with its own camera, filmed into a render texture that the panel shows as its background image. The ninja turns slowly, and dragging across the picture turns it by hand.

### Income

The Mint and the Steel Mine fill up while the player is away and are emptied with a Collect button.

* `ProgressionDefinition.incomes` lists the sources (`IncomeSource`): an ID from `IncomeIds`, the resource, the amount per hour, how much that grows per clan level, and how many hours it stores. A new income building is one more entry plus a `VillageBuildingKind` that returns its ID from `VillageBuilding.IncomeIdOf`.
* The save keeps one `IncomeTimer` per source: the time it has been filling from, in seconds since 1970 (UTC). Nothing ticks in the background; `ProfileService.PendingIncome` works the amount out from the clock.
* `ProfileService.StartIncome` starts new sources with one hour already worked, and counts from now if the clock was set back. `TryCollectIncome` pays the wallet and keeps the seconds that have not made a whole unit yet.
* Every income method takes the time as a parameter, so tests pass their own clock. The village passes `DateTime.UtcNow`.
* `GameScenes` names the scenes and loads them. The battle scene's lineup screen and result screen have a **Back to village** button; the save stays loaded between scenes.
* The layout (buildings, houses, trees, lanterns) is in `VillageSceneBuilder.Layout`. Models come from `Art/Meshy/Buildings` and `Art/Meshy/Props` (see `docs/ART_STYLE.md`), placed with `MeshyPlacement` like the battle arena. Run the menu item again after adding a model.

From the command line, `-executeMethod NinjaRealm.Editor.VillageSceneBuilder.CaptureVillage -captureOut <png>` saves a screenshot of the village.

## Battle scene

`Assets/Scenes/Battle.unity` is the combat prototype, reached from the village's Command Hall. Create or recreate it with **Ninja Realm > Scenes > Create Battle Scene**, then press Play (in the village, or in this scene directly). It loops through three phases:

1. **Setup** (`LineupScreen`): see the picked stage (its enemy, level, best stars and what a win and the first clear pay) and step to the stage before or after it, drag ninjas you own from the bench onto your half of the board, drag them to move or swap, drag weapons you own from the armory onto a ninja, and see both teams' synergies. The lineup holds as many ninjas as your clan level allows. Delete removes the selected ninja. Enter or **Fight!** starts the fight. The lineup and the picked stage are kept in the save.
2. **Roster** (`RosterScreen`, Tab from setup): level up ninjas with Karma, recruit from the Dojo's four daily offers or buy Common and Rare ninjas directly with Gold, and forge combined weapons from two components and Steel.
3. **Fight** (`BattleHud`): the timer, overtime, speed (1, 2, 3), pause (Space), skip (S) and give up, with synergies, names and floating numbers.
4. **Result** (`ResultScreen`): victory, defeat or draw, the stars won, the duration and seed, what the fight paid (Gold, Steel, clan XP, the first clear's rewards, a weapon drop, clan levels), and a damage log per team (damage dealt and taken, healing and shields). Esc goes back to the lineup, R replays the same fight to watch it (a replay pays nothing), N fights again with a new seed, and after a win Enter goes on to the next stage.

**Ninja Realm > Battle** (editor menu, while the battle scene plays) fights the picked stage, skips to the result, goes to the next stage, back to the lineup, opens the roster or goes back to the village.

* `BattleController` owns the phases. It builds each fight from the two lineups with `CombatSetupBuilder.AddLineup`, the player's ninjas at their rank in the save and the enemy's at the stage's rank (`StageDefinition.RankOf`), and pays the result and its stars into the save. Without a save every stage is open. Outside play mode, or without a progression asset, there is no save and every ninja and weapon is available at the base rank. A seed of 0 picks a new seed for each fight; any other seed replays the same fight.
* `BattlePlayback` steps the simulation at 20 ticks per second times the speed, and turns new events into animations. It never changes the fight.
* `HexBoardView` is built in code (`PrototypeVisuals`): low poly hex tiles with a stone texture, grass ground, and the arena prefab (fences, braziers, banners and the village behind the board) placed around it. The art is linked by **Ninja Realm > Art > Set Up Battle Arena**; without it the board falls back to plain colours. `HexBoardView.TryGetHex` turns a screen point into a hex for drag and drop.
* `UnitView` shows a ninja's `ModelPrefab` (see `docs/ART_STYLE.md`), scaled to 1.2 m, with a team ring and health, shield and mana bars. Combat events become animator triggers (`NinjaAnimation`: `Attack`, `Cast`, `Hit`, `Die`, and the `Moving` bool). A ninja without a model gets a placeholder chibi body built in code, in dark cloth with the element colour.
* The screens are UI Toolkit (see UI below). `LineupScreen` reads the mouse from the Input System and asks the panel what is under it (`GameUi.Under`): a bench card carries its ninja or weapon in `userData`, and anything else that is UI keeps the click off the board.
* `HexLayout` maps board hexes to world positions: pointy-top hexes, 0.75 m radius, board centred on the origin, team 0 nearest the camera.

From the command line, `-executeMethod NinjaRealm.Editor.BattleSceneBuilder.CaptureBattle -captureOut <png> -captureSeed 7 -captureSeconds 6 -captureOpponent lineup.ember_clan` plays a fight against that lineup at level 1 without entering play mode and saves a screenshot.

## UI

The game's screens are UI Toolkit. Layouts are UXML in `UI/Resources/UI`, loaded by name, so a scene needs no UI setup; styles are USS in `UI/Styles`.

* `GameUi` makes the one `PanelSettings` every screen shares (scaled to a 1600 by 900 reference, matching height) with the theme `UI/Resources/UI/NinjaTheme.tss`, which imports Unity's default runtime theme and `Theme.uss`. `GameUi.Create(host, layout, sortingOrder)` puts a layout on a new `UIDocument` under the host.
* `Theme.uss` holds the colour tokens (`--ink`, `--gold`, `--red` and the rest), the restyled labels, buttons and scroll bars, and shared classes: `.panel`, `.overlay`, `.card`, `.title`, `.note`, `.small`, `.pips`, `.bar`, `.hidden`.
* Each screen builds its changing parts in C# with `GameUi.Label`, `GameUi.Element` and `GameUi.Button`, and styles them with classes. Colours that come from content (an element, a resource, a trait) are set inline. Text uses rich text tags for bold and colour.
* `RosterViews` builds the Shrine's ninja list, the Dojo's recruits, the Blacksmith's armory and one ninja's details for any screen. After a button changes the save it reports the result and asks its screen to rebuild.
* `WorldLabels` keeps labels over points of the 3D world (names over ninjas and buildings, damage numbers), reusing them from frame to frame.
* `UiMessage` shows the result of the last action under a panel for a few seconds.
* Village layouts: `VillageHud`, `BuildingPanel` (the frame every building panel and the battle roster use) and `CommandHall`. Battle layouts: `Lineup`, `BattleHud` and `Result`.

## Audio

* `MusicPlayer.Play(track)` fades to a looping track and keeps playing across scene loads: `Village` from `VillageController`, `Battle` from `BattleController`. The tracks are `Audio/Resources/Music/Village.ogg` (a calm koto and shakuhachi loop in the Yo scale) and `Battle.ogg` (taiko, shamisen and flute in the In scale). `MusicPlayer.Volume` is kept in PlayerPrefs.
* Voice lines: `Audio/Resources/Voices/VoiceLines.txt` holds what each ninja says, one block per ninja (`[kaito]`, then optional `voice =`, `rate =` and `pitch =` lines, then one line per thing it says). `VoiceBook` reads it and `NinjaVoices` plays a random line, never the same one twice in a row. A ninja speaks when it is clicked on the plaza, when its card or model is clicked in the Dojo's hall, and the line shows along the bottom of the screen.
* To change the lines, edit the text file and run **Ninja Realm > Audio > Record the Voice Lines** (Unity on a Mac). It records each changed line with the macOS `say` voice into `Voices/<ninja>/<ninja>_NN.wav`, remembers in the clip's import data what it was made from, and deletes clips of removed lines. A line without a recording still shows as text.

## Determinism

The combat simulation must give the same result for the same lineups and seed on every machine.

* Use `DeterministicRandom` for every random roll in combat, never `UnityEngine.Random` or `System.Random`.
* Express chances as integer basis points (2500 = 25%).
* Iterate lists in a defined order. Never let dictionary order decide the outcome of a fight.

## Git flow

* `main` gets merges from `develop` at milestones.
* `develop` is the working branch.
* Each step lives on its own branch from `develop` (`feature/...`, `chore/...`) and goes into `develop` through a reviewed pull request.
