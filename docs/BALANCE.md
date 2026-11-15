# Combat prototype balance

The first balance pass for the combat prototype (step 7). All numbers come from `BalanceRunner`, which plays each matchup with 20 seeds from both sides of the board (40 fights), so seed and side luck cancel out. The values live in `PrototypeContent`; see `docs/ARCHITECTURE.md` for how to change them.

## Goals

* Several builds can win: no build of five ninjas without weapons should win less than 40% or more than 60% overall.
* Fights last about 20 to 35 seconds and rarely reach the 45 second timeout.
* Enemy lineups get harder in difficulty order, and the hardest can still be beaten by a good, fully geared lineup.

## Builds

Five ninjas each, no weapons. Cells are the row team's win rate.

| | Fire burn | Water sustain | Earth bruisers | Blades and spells | Medics | Overall |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Fire burn | – | 48% | 50% | 80% | 53% | 58% |
| Water sustain | 53% | – | 70% | 45% | 15% | 46% |
| Earth bruisers | 50% | 30% | – | 80% | 50% | 53% |
| Blades and spells | 20% | 55% | 20% | – | 70% | 41% |
| Medics | 48% | 85% | 50% | 30% | – | 53% |

Average fight: 32.8s (matchup averages from 21.6s to 44.5s). Timeouts: 32 of 400 fights, almost all in Water sustain against Medics, where both sides heal. Every build has counters and good matchups, like rock, paper, scissors.

## Enemy lineups

> Since the persistent roster, Bandit Patrol has three ninjas (the new save's starting three, without weapons), and the enemy lineups fight at levels 1, 2, 4, 7 and 10. The tables below are from before that change. `ProgressionTests.StarterTeam_UsuallyBeatsTheFirstOpponent` checks that a new save's team beats Bandit Patrol at least 60% of the time.

The five enemy lineups played against each other:

| | Bandit Patrol | Ember Clan | Tide Shrine | Stone Fortress | Shogun's Guard | Overall |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Bandit Patrol | – | 0% | 0% | 0% | 0% | 0% |
| Ember Clan | 100% | – | 43% | 0% | 0% | 36% |
| Tide Shrine | 100% | 58% | – | 3% | 0% | 40% |
| Stone Fortress | 100% | 100% | 98% | – | 0% | 74% |
| Shogun's Guard | 100% | 100% | 100% | 100% | – | 100% |

Difficulty goes up in order (difficulty 1 to 5). Example player lineups against them:

| Player lineup | Bandit Patrol | Ember Clan | Tide Shrine | Stone Fortress | Shogun's Guard |
| --- | ---: | ---: | ---: | ---: | ---: |
| Five ninjas, no weapons (Fire burn) | 100% | 50% | 5% | 0% | 0% |
| Six ninjas, Fire, with weapons | 100% | 100% | 100% | 75% | 0% |
| Six ninjas, Water, with weapons | 100% | 100% | 100% | 100% | 50% |

So the first two lineups can be beaten with the starter roster, the middle ones need a full board and weapons, and Shogun's Guard needs a full, well-geared board with the right synergies.

## Ninjas

Across all build fights:

| Ninja | Fights | Damage per fight | Heal + shield per fight | Survived |
| --- | ---: | ---: | ---: | ---: |
| Daichi | 1120 | 289 | 100 | 13% |
| Goro | 960 | 967 | 38 | 33% |
| Hana | 640 | 548 | 577 | 27% |
| Jiro | 800 | 546 | 165 | 19% |
| Kaito | 960 | 974 | 0 | 20% |
| Mei | 1280 | 1441 | 1 | 51% |
| Nami | 640 | 1289 | 32 | 60% |
| Saya | 800 | 689 | 854 | 43% |
| Takeshi | 960 | 400 | 924 | 36% |

Mei and Nami are the main damage dealers, as backline mages should be, and survive most because they stand behind the front line. Daichi is a pure tank: low damage and low survival, since he takes the hits.

## Changes in this pass

Before the pass, Water sustain won 82% overall and beat Fire burn every time, while the Earth build won only 5%.

| What | Before | After | Why |
| --- | --- | --- | --- |
| Overtime damage per step | +25% | +35% | End stalled fights sooner. |
| Overtime healing and shields | No change | -25% per step | Sustain teams can't outheal overtime. |
| Water (2) heal every 4s | 8% | 5% | Water healed too much for free. |
| Water (3) heal every 4s | 14% | 8% | Same. |
| Tide Blessing heal | 20% | 15% | Same. |
| Riptide Guard shield | 30% | 25% | Takeshi was too hard to kill. |
| Fire (2) burn | 3% over 3s | 4% over 2s | Fire did too little before burns ran out. |
| Fire (3) burn and healing cut | 6% over 3s, 30% | 8% over 2s, 40% | Fire's answer to healers. |
| Earth (2) | 20 armor | 20 armor and 20 magic resist | Earth melted to mages. |
| Earth (3) | 40 armor, allies 10 | 40 of each, allies 10 of each | Same. |
| Stoneskin shield | 25% | 30% | Saya needed more impact. |
| Fire Lotus damage | 200 | 130 | Mei one-shot backlines. |
| Whirlpool damage and stun | 150, 2s | 110, 1.5s | Nami's stun decided too many fights. |
| Quake Hammer damage and stun | 220%, 1.5s | 200%, 1s | Same for Goro. |
| Mei and Nami max mana | 60 | 70 | Fewer casts per fight. |

## Income

The Mint and the Steel Mine pay for coming back, not for waiting. The numbers are in `PrototypeContent` and the Progression asset.

| Building | Per hour at clan level 1 | Per clan level | Stores | Full store at clan level 1 |
| --- | --- | --- | --- | --- |
| Mint | 40 Gold | +5 | 8 hours | 320 Gold |
| Steel Mine | 4 Steel | +1 | 8 hours | 32 Steel |

* A win against the first opponent pays 90 Gold and 5 Steel, so a full Mint is worth about three and a half easy wins and a full Steel Mine about six.
* A full Mint buys one Common recruit (200 Gold) with some left over. A full Steel Mine pays for one forge (20 Steel) and most of a second.
* Fights should stay the main source of Gold. If players stop fighting between collections, lower the Mint before raising fight pay.

## Next

* Water sustain against Medics still times out often. A third sustain counter (for example a class with anti-heal) would help once there are more ninjas.
* Re-run the report after adding ninjas, traits or weapons. The `BalanceRunner_PlaysBothSidesAndReports` test only checks that it runs; a menu item to print the full report in the editor would be a useful next step.
