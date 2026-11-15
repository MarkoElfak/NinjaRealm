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
                     runner, progression and the save file, and the prototype content
      Combat/        NinjaRealm.Combat: deterministic fight simulation, pure C# with no UnityEngine
      Presentation/  NinjaRealm.Presentation: MonoBehaviours, scenes, UI, playback of combat events
      Editor/        NinjaRealm.Editor: menus, validation and import tools (editor only)
    Tests/
      EditMode/      NinjaRealm.Tests.EditMode: NUnit tests run by the Unity Test Runner
    Content/         ContentDatabase.asset and content assets, created as they are added
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
| `LineupDefinition` | `lineup.` | An enemy lineup: description, difficulty (1 to 5), the level its ninjas fight at, and slots (ninja, hex, weapons) |
| `ResourceDefinition` | `resource.` | A currency: Gold, Karma, Steel (in `NinjaRealm.Core.Progression`) |
| `ProgressionDefinition` | `progression.` | Every progression number: the new save, level costs, belts, stars, the Dojo, forging, fight rewards, clan levels and team sizes (in `NinjaRealm.Core.Progression`) |

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
* `LineupDefinition`: an enemy lineup as a content asset.

## Progression and the save file

`NinjaRealm.Core.Progression` holds the persistent roster.

* `PlayerProfile` is everything the save holds, by content ID: the `Wallet` (balances by resource ID), owned ninjas (level, stars, shards), weapon counts, clan level and XP, the lineup and opponent, beaten lineups, and the Dojo's offers, date and random seed.
* `SaveFile` writes it as JSON with a `version`, and runs older files through `Migrations` in order. To change the format, raise `CurrentVersion` and add one migration. A file from a newer version, or one that isn't JSON, is refused.
* `ISaveStorage` is where it lives: `FileSaveStorage` writes to a temporary file and then replaces the old one; `MemorySaveStorage` is for tests.
* `ProfileService` is the only thing that changes a profile: `TryLevelUp`, `TryRecruitOffer`, `TryRefreshDojo`, `RefreshDojoIfNewDay`, `TryBuyDirect`, `TryForge`, `ApplyFightResult` and `SaveLineup`. Each checks its cost, changes the profile and saves; a refusal changes nothing and says why. `FitLineup` keeps a lineup to what the player owns and the team size. On load it drops unknown IDs and duplicates and keeps numbers in range, so old or damaged saves still load; an unreadable file is set aside and a new save starts.
* Dojo offers use `DeterministicRandom` with the profile's seed and roll count, and fight drops use the fight's seed, so tests can check exact results.

In the game, `GameProfile` (Presentation) loads the save from `Application.persistentDataPath/profile.json` once and shares it. **Ninja Realm > Save** shows or deletes the file, and gives test resources in play mode.

## Prototype content

`NinjaRealm.Core.Prototype.PrototypeContent` is the source of truth for the prototype's numbers: 17 stats, 3 elements, 4 classes, 9 skills, 9 ninjas, 6 components, 4 combined weapons, 5 enemy lineups, 3 resources and the progression settings. It writes them into an `IContentSink`:

* **Ninja Realm > Content > Create Prototype Content** saves them as assets under `Assets/_Project/Content/`. It skips assets that already exist, so it never overwrites edits.
* **Ninja Realm > Content > Reset Prototype Content to Defaults** overwrites the assets with the values in code.
* `InMemoryContentSink` and `PrototypeContent.CreateInMemoryDatabase()` keep them in memory, for tests and balance runs.

Both menu items rebuild the database. To change a number, change it in `PrototypeContent` and run Reset. The EditMode test `ProjectContentTests` validates every content asset, checks that the database is up to date, and fails if an asset differs from `PrototypeContent`, naming the field.

## Balance runner

`NinjaRealm.Core.Fights.BalanceRunner` plays many fights headless. `Run(a, b)` plays a matchup with 20 seeds from both sides of the board, `RoundRobin(teams, title)` returns a Markdown win-rate table, and `NinjaReport()` lists damage, healing and survival per ninja. `PrototypeContent.BalanceArchetypes` gives five builds to compare. The latest results and tuning notes are in `docs/BALANCE.md`.

## Village scene

`Assets/Scenes/Village.unity` is the hub the game starts in. Create or recreate it with **Ninja Realm > Scenes > Create Village Scene**, which also puts it first in the build settings with the battle scene after it.

* `VillageController` loads the save (`GameProfile`), moves the camera (drag to pan, scroll to zoom) and opens a building when it is clicked. Esc closes it.
* `VillageBuilding` marks a clickable building and says what it is (`VillageBuildingKind`): the **Command Hall** goes to the battle scene, the **Dojo** recruits, the **Shrine** levels up ninjas and the **Blacksmith** forges. The **Mint** and **Steel Mine** stand in the village but are not open yet. A building without a Meshy model gets a placeholder house built in code in play mode, and a box collider round whatever it shows.
* `VillageNinjas` stands the player's ninjas on the plaza in rows: every ninja they own, so every ninja they can put in a fight (every ninja in the content when there is no save). Each is a `UnitView` without its ring and bars, 1.5 times its board size, facing the camera. A new recruit joins them when the Dojo closes.
* `VillageScreen` draws the wallet, a name over each building, and the open building's panel. The Dojo, Shrine and Blacksmith panels are `RosterPanels`, the same panels as the battle scene's roster screen (Tab).
* `GameScenes` names the scenes and loads them. The battle scene's lineup screen and result screen have a **Back to village** button; the save stays loaded between scenes.
* The layout (buildings, houses, trees, lanterns) is in `VillageSceneBuilder.Layout`. Models come from `Art/Meshy/Buildings` and `Art/Meshy/Props` (see `docs/ART_STYLE.md`), placed with `MeshyPlacement` like the battle arena. Run the menu item again after adding a model.

From the command line, `-executeMethod NinjaRealm.Editor.VillageSceneBuilder.CaptureVillage -captureOut <png>` saves a screenshot of the village.

## Battle scene

`Assets/Scenes/Battle.unity` is the combat prototype, reached from the village's Command Hall. Create or recreate it with **Ninja Realm > Scenes > Create Battle Scene**, then press Play (in the village, or in this scene directly). It loops through three phases:

1. **Setup** (`LineupScreen`): pick an opponent (sorted by difficulty, with its level and what a win pays), drag ninjas you own from the bench onto your half of the board, drag them to move or swap, drag weapons you own from the armory onto a ninja, and see both teams' synergies. The lineup holds as many ninjas as your clan level allows. Delete removes the selected ninja. Enter or **Fight!** starts the fight. The lineup and opponent are kept in the save.
2. **Roster** (`RosterScreen`, Tab from setup): level up ninjas with Karma, recruit from the Dojo's four daily offers or buy Common and Rare ninjas directly with Gold, and forge combined weapons from two components and Steel.
3. **Fight** (`BattleHud`): the timer, overtime, speed (1, 2, 3), pause (Space), skip (S) and give up, with synergies, names and floating numbers.
4. **Result** (`ResultScreen`): victory, defeat or draw, the duration and seed, what the fight paid (Gold, Steel, clan XP, a first-win bonus, a weapon drop, clan levels), and a damage log per team (damage dealt and taken, healing and shields). Esc goes back to the lineup, R replays the same fight to watch it (a replay pays nothing), N fights again with a new seed.

* `BattleController` owns the phases. It builds each fight from the two lineups with `CombatSetupBuilder.AddLineup`, the player's ninjas at their rank in the save and the enemy's at the lineup's level, and pays the result into the save. Outside play mode, or without a progression asset, there is no save and every ninja and weapon is available at the base rank. A seed of 0 picks a new seed for each fight; any other seed replays the same fight.
* `BattlePlayback` steps the simulation at 20 ticks per second times the speed, and turns new events into animations. It never changes the fight.
* `HexBoardView` is built in code (`PrototypeVisuals`): low poly hex tiles with a stone texture, grass ground, and the arena prefab (fences, braziers, banners and the village behind the board) placed around it. The art is linked by **Ninja Realm > Art > Set Up Battle Arena**; without it the board falls back to plain colours. `HexBoardView.TryGetHex` turns a screen point into a hex for drag and drop.
* `UnitView` shows a ninja's `ModelPrefab` (see `docs/ART_STYLE.md`), scaled to 1.2 m, with a team ring and health, shield and mana bars. Combat events become animator triggers (`NinjaAnimation`: `Attack`, `Cast`, `Hit`, `Die`, and the `Moving` bool). A ninja without a model gets a placeholder chibi body built in code, in dark cloth with the element colour.
* The screens are temporary IMGUI, drawn with `PrototypeGui` and scaled to a 900 pixel tall reference screen. They will be replaced by real UI.
* `HexLayout` maps board hexes to world positions: pointy-top hexes, 0.75 m radius, board centred on the origin, team 0 nearest the camera.

From the command line, `-executeMethod NinjaRealm.Editor.BattleSceneBuilder.CaptureBattle -captureOut <png> -captureSeed 7 -captureSeconds 6 -captureOpponent lineup.ember_clan` plays a fight without entering play mode and saves a screenshot.

## Determinism

The combat simulation must give the same result for the same lineups and seed on every machine.

* Use `DeterministicRandom` for every random roll in combat, never `UnityEngine.Random` or `System.Random`.
* Express chances as integer basis points (2500 = 25%).
* Iterate lists in a defined order. Never let dictionary order decide the outcome of a fight.

## Git flow

* `main` gets merges from `develop` at milestones.
* `develop` is the working branch.
* Each step lives on its own branch from `develop` (`feature/...`, `chore/...`) and goes into `develop` through a reviewed pull request.
