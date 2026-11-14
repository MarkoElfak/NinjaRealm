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
                     and CombatSetupBuilder (content assets into fight setups)
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
Tests        ──> Core, Combat
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
* **Overtime**: after 30 seconds, all damage rises by 25% every 5 seconds. At 45 seconds the team with the larger share of its total health left wins; an exact tie is a draw.
* **Output**: `Events` lists everything that happened (moves, attacks, damage, casts, statuses, deaths, trait activations) for playback and the damage log. `Result` has the winner, duration and per-unit totals.

`NinjaRealm.Core.Fights.CombatSetupBuilder` builds a `FightSetup` from content: add each ninja with a team, a hex (`TeamPosition` flips team 1's half) and its weapons, then call `Build()`.

```csharp
var builder = CombatSetupBuilder.FromDatabase(GameBootstrap.Content);
builder.Add(0, kaito, builder.TeamPosition(0, 3, 3), shadowKatana);
builder.Add(1, hana, builder.TeamPosition(1, 3, 3));
var result = new CombatSimulation(builder.Build(), seed).Run();
```

Known limits for now: a timed buff to health does not change max health, and units do not path around blockers (they sidestep one hex at a time).

## Prototype content

**Ninja Realm > Content > Create Prototype Content** creates the combat prototype's assets under `Assets/_Project/Content/`: 17 stats, 3 elements, 4 classes, 9 skills, 9 ninjas, 6 components and 4 combined weapons. It skips assets that already exist, so it never overwrites edits. It then rebuilds the database.

The EditMode test `ProjectContentTests` validates every content asset in the project and checks that the database is up to date.

## Determinism

The combat simulation must give the same result for the same lineups and seed on every machine.

* Use `DeterministicRandom` for every random roll in combat, never `UnityEngine.Random` or `System.Random`.
* Express chances as integer basis points (2500 = 25%).
* Iterate lists in a defined order. Never let dictionary order decide the outcome of a fight.

## Git flow

* `main` gets merges from `develop` at milestones.
* `develop` is the working branch.
* Each step lives on its own branch from `develop` (`feature/...`, `chore/...`) and goes into `develop` through a reviewed pull request.
