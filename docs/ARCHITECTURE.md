# Ninja Realm architecture

Ninja Realm is a 3D low poly auto battler built in Unity 6000.4 with URP. The full game design lives in the design doc. This file covers how the code is organized so the game stays easy to grow.

## Core rule

**Content is data, systems are code.** Every clan, ninja, trait, skill, weapon, recipe, resource, building and mode is a `ContentDefinition` ScriptableObject with a stable string ID such as `ninja.kaito`. Code looks content up by ID and never lists it by name. Adding content means adding assets, not editing code.

## Folder layout

```
Assets/
  _Project/
    Scripts/
      Core/          NinjaRealm.Core: content IDs, ContentDefinition, ContentDatabase, validation
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
Combat       ──> nothing (noEngineReferences: true)
```

Combat stays free of UnityEngine so fights can run headless, in tests, in balance scripts and on a server. Presentation turns combat results into visuals and never changes the outcome of a fight.

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

Authoring values are floats for easy editing. The combat simulation converts them to fixed-point integers when a fight starts, so results stay deterministic.

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
