# Fallout: New Vegas Goodsprings actor/package contract

Status: **owned-data audit only; compiler and runtime consumption are
unimplemented**. This document does not claim that these AI, quest, dialogue,
or enable-state beats run in OpenNV, and it makes no retail-parity claim.

## Source identity and precedence

- Read-only source: the user's legally owned Fallout: New Vegas installation.
- `FalloutNV.esm` SHA-256:
  `50991d36804b7d1e70df1afd7471b72f0e29d1b456ee2516a9717c002564e7c1`.
- Effective stack: the ten-plugin official order declared by
  `content/recipes/fnv-official-actor-parity-corpus-v1.json`.
- Stable FormKey winner resolution found no official-DLC override of any actor,
  base, package, quest, marker, or condition named below. Every audited winner
  is from `FalloutNV.esm`, so its runtime FormID remains `00xxxxxx` in that
  stack.

## Actor admission

| Actor | Placed reference -> base | Authored admission |
|---|---|---|
| Doc Mitchell | `00104c0f DocMitchellREF` -> `00104c0c DocMitchell` | enabled; record flags `00000400`; no `XESP` |
| Easy Pete | `00104c80 EasyPeteRef` -> `00104c7f GSEasyPete` | enabled; flags `00000400`; no `XESP` |
| Trudy | `00104c6d TrudyRef` -> `00104c6c GSTrudy` | **initially disabled**; flags `00000c00`; no `XESP` |
| Goodsprings settler | `00104f08 GoodspringsSettler04Ref` -> `00104f09 GSSettlerCM` | enabled; flags `00000400`; no `XESP` |
| Sunny Smiles | `00104e85 SunnyRef` -> `00104e84 GSSunnySmiles` | enabled; flags `00000400`; no `XESP` |
| Cheyenne | `0010588e CheyenneREF` -> `0010588d GSCheyenne` | enabled; flags `00000400`; no `XESP` |

Admission and package selection are separate. In particular, Trudy may not be
included merely because her base, appearance, or saloon placement was decoded.

## Condition variable identities

`GetQuestVariable` parameter 2 is the variable's compiled `SLSD` index in the
quest's attached `SCPT`; it is **not** the variable's ordinal `SCVR` position.
The first-slice indices are:

| Quest | `SCRI` script | Required `SLSD` indices |
|---|---|---|
| `0015ec5b VMS16b` | `0015ed60 VMS16bQuestScript` | `1=bPowderGangAttacking` |
| `00104eae VMS16` | `00105d4e VMS16QuestScript` | `3=bTrudyHelp`; `9=bGunFightStart` |
| `00104c66 VFreeformGoodsprings` | `00104c65 VFreeformGoodspringsScript` | `8=nWellsCleared`; `9=bOnGeckoQuest`; `24=TrudyToBar`; `48=bMetSunny`; `53=bEnableTrudyDone`; `55=bAllowEnableTrudy` |
| `0010a214 VCG02` | `0010a1f0 VCG02SCRIPT` | `4=bShootingTutorialActive` |

## Ordered package contract

Package order is the base record's authored `PKID` order; the first package
whose schedule and condition chain pass wins. `Any` below is the raw PSDT
`month=-1, day=-1, date=0, time=-1, duration=0`. The hexadecimal value after a
type is the raw PKDT general-flags field and must be preserved.

### Doc Mitchell

| Priority | Package and type | Schedule/location/target | Conditions |
|---:|---|---|---|
| 1 | `0015ed62 GoodspringsMilitiaTravelPackage`, Travel `04203202` | Any; near `0015ed61`, radius 256 | `VMS16b.bPowderGangAttacking == 1` |
| 2 | `0010b756 VCG01DocMitchellSandbox`, Sandbox `00201200` | Any; current location, radius 1024 | `GetQuestCompleted VCG01 == 1` |
| 3 | `00105bd2 VCG01DocMitchellFarewellDialogueStart`, Dialogue `00201000` | Any; near `00105bcf`; Player, distance 420 | `VCG01 stage >= 115` |
| 4 | `00105bd1 VCG01DocMitchellTravelToExit`, Travel `00201004` | Any; `00105bcf` | `VCG01 stage >= 110` |
| 5 | `001055bd VCG01DocMitchellTravelToExamSpot`, Travel `00201004` | Any; `001055b8` | `VCG01 stage >= 80` |
| 6 | `0010b046 VCG01DocMitchellTravelToSkullTestSpot`, Travel `00201004` | Any; `0010b044` | `VCG01 stage >= 76` |
| 7 | `00104c1e VCG01DocMitchellTravelToPlayerAtTester`, Travel `00201004` | Any; `00104c0e` | `VCG01 stage >= 55` |
| 8 | `00107238 VCG01DocMitchellBedsideStandingPackage`, Travel `00201006` | Any; `00107235` | `VCG01 stage >= 40` |
| 9 | `00104c1d VCG01DocMitchellFirstPosition`, Travel `00201004` | Any; `001059b0` | unconditional fallback |

### Easy Pete

| Priority | Package and type | Schedule/location | Conditions |
|---:|---|---|---|
| 1 | `0015f8dc EasyPeteHideInSaloonPackage`, Flee Not Combat `04000000` | Any; cell `00106185` | `VMS16.bGunFightStart == 1` |
| 2 | `0015ed62 GoodspringsMilitiaTravelPackage`, Travel `04203202` | Any; `0015ed61`, radius 256 | `VMS16b.bPowderGangAttacking == 1` |
| 3 | `0010655e EasyPeteChairPackage8x4`, Travel `00001000` | 08:00 for 4 hours; `0010634a` | none |
| 4 | `0016a9b0 EasyPeteEat12x2`, Eat `00000000` | 12:00 for 2 hours; `00107d4a`; default-food target | none |
| 5 | `0016a9af EasyPeteChairPackage14x4`, Travel `00000000` | 14:00 for 8 hours; `0010634a` | none |
| 6 | `00176acf EasyPeteSleepPackage`, Sleep `00000000` | Any fallback; cell `00105619` | none |

### Trudy

| Priority | Package and type | Schedule/location/target | Conditions |
|---:|---|---|---|
| 1 | `0015ed62 GoodspringsMilitiaTravelPackage`, Travel `04203202` | Any; `0015ed61`, radius 256 | `VMS16b.bPowderGangAttacking == 1` |
| 2 | `00116810 GSTrudyGunfightPackage`, Guard `1c803002` | Any; guard and target marker `00106971` | `VMS16.bTrudyHelp == 1 AND bGunFightStart == 1` |
| 3 | `00176acd GSTrudySleepPackage22x10`, Sleep `00000000` | 22:00 for 10 hours; cell `00105659` | `VFreeformGoodsprings.TrudyToBar == 1` |
| 4 | `00106641 GSTrudyAtBarPackage8x12`, Travel `00200001` | 08:00 for 12 hours; `0010663f` | `TrudyToBar == 1` |
| 5 | `00176acc GSTrudyEveningPackage20x2`, Sandbox `00000000` | 20:00 for 2 hours; cell `00105659` | `TrudyToBar == 1` |

### Goodsprings settler `00104f08`

| Priority | Package and type | Schedule/location | Conditions |
|---:|---|---|---|
| 1 | `00107480 GoodspringsFleePackage`, Flee Not Combat `0c203002` | Any | `(VMS16.bGunFightStart == 1 OR VMS16b.bPowderGangAttacking == 1) AND VMS16.bTrudyHelp == 0` |
| 2 | `00107869 GSSettlerAmbushPackage`, Ambush `10801200` | Any; editor location | `VMS16.bGunFightStart == 1` |
| 3 | `0010a1d9 GSSettler04SleepPackage22x8`, Sleep `00000000` | 22:00 for 8 hours; cell `001055e1` | none |
| 4 | `0010663c GoodspringsSettlerSaloonPackage`, Travel `00001000` | Any; `00106192` | `GetQuestCompleted VMS16 == 0 AND GetStageDone VMS16 70 == 0` |

### Sunny Smiles

| Priority | Package and type | Schedule/location/target | Conditions |
|---:|---|---|---|
| 1 | `00105cc7 SunnyMeetPlayerDialoguePackage`, Dialogue `00001000` | Any; editor location; Player distance 256 | `VFreeformGoodsprings.bMetSunny == 0` |
| 2 | `0010a21a VCG02SunnySmilesDialogueStart`, Dialogue `00001000` | Any; `0010a201`; Player distance 1024 | VCG02 objective 70 not displayed; objective 20 not displayed; objective 10 completed |
| 3 | `0010a21c SunnySmilesStayAtCurrentLocation`, Travel `00801000` | Any; current location | objective 70 not displayed; quest incomplete; `(stage == 45 OR 40 OR 20)` |
| 4 | `0010a219 VCG02SunnySmilesDialogueSneakEnd`, Dialogue `08001000` | Any; `0010a1ff`; Player distance 420 | objective 70 not displayed; objective 40 incomplete; stage 50 |
| 5 | `0010a217 VCG02SunnySneakCloserToWell`, Travel `0c021204` | Any; `0010a1ff` | objective 70 not displayed; stage 35 |
| 6 | `0010a218 VCG02SunnySmilesDialogueSneakStart`, Dialogue `08001000` | Any; `0010a200`; Player distance 420 | objective 70 not displayed; stage 30 |
| 7 | `0010a216 VCG02SunnyTravelOutside`, Travel `00201004` | Any; `0010a201` | objective 70 not displayed; stage 10 |
| 8 | `0010a21b VCG02SunnyTravelToWell1`, Travel `0c803004` | Any; `0010a200` | objective 70 not displayed; stage 25 |
| 9 | `0010a215 VCG02SunnyTravelToWell2`, Travel `00803004` | Any; `0010a1fc` | objective 70 not displayed; objective 60 incomplete; `nWellsCleared == 0`; `bOnGeckoQuest == 1` |
| 10 | `0010a21d VCG02SunnyTravelToWell3`, Travel `00803004` | Any; `0010a1fb` | objective 70 not displayed; objective 60 incomplete; `nWellsCleared >= 1`; `bOnGeckoQuest == 1` |
| 11 | `0015d9f5 VCG03SunnyTravelToCampfire`, Travel `00001004` | Any; `0015d9f1` | VCG02 objective 70 not displayed; VCG03 objective 10 displayed; objective 40 not displayed |
| 12 | `0015ed62 GoodspringsMilitiaTravelPackage`, Travel `04203202` | Any; `0015ed61`, radius 256 | `VMS16b.bPowderGangAttacking == 1` |
| 13 | `00176ad3 GSSunnySleepPackage0x8`, Sleep `00000000` | 00:00 for 8 hours; cell `00105659` | none |
| 14 | `000200d5 DefaultStayAtEditorLocation`, Travel `00000000` | Any; editor location | unconditional fallback |

### Cheyenne

| Priority | Package and type | Schedule/location/target | Conditions |
|---:|---|---|---|
| 1 | `0017267e CheyenneShootingTutorialIdle`, Travel `00002004` | Any; `0017267d` | `VCG02.bShootingTutorialActive == 1` |
| 2 | `00152afa CheyenneAccompany`, Accompany `04501000` | Any; Sunny placed ref `00104e85`, distance 128 | `GetDeadCount` on Sunny base `00104e84 == 0` |
| 3 | `0010696d GSCheyenneSandboxPackage`, Sandbox `00000000` | Any; editor location, radius 512 | unconditional fallback |

## Sunny and Cheyenne linkage

The relationship is package/script data, not `XESP`:

- Cheyenne's `00152afa` package targets Sunny's placed reference `00104e85`
  and tests death count against Sunny's base `00104e84`.
- VCG02 stage 10 sets `bShootingTutorialActive=1` and calls
  `CheyenneRef.evp`; stage 25 clears it and evaluates Cheyenne and Sunny.
- `0016ad04 SunnySmilesTriggerSCRIPT`, attached to
  `0016ad05 VCG02CheyenneBarkTrigger` and placed as `0016ad06`, handles the
  first player entry. While its local one-shot is clear and `bMetSunny==0`, it
  calls `CheyenneRef.PlayIdle LooseDogGrowl` and plays
  `NPCDogGrowlEntry`.

These links require no actor-specific runtime branch if target, death-count,
quest-variable, trigger-event, idle, sound, and evaluate-package operations are
generic.

## Trudy enable route

Normal live-Sunny path:

1. INFO `0015f8b6` belongs to `0015f8b5 VCG02LastObjective` and quest
   `0010a214 VCG02`; its speaker/base is Sunny `00104e84`, with
   `GetIsID Sunny == 1`.
2. Its result unlocks `00109040 GSGasStationDoorRef`; enables Trudy
   `00104c6d`, Joe Cobb `00104c68`, and trigger `0010521e`; sets
   `VFreeformGoodsprings.bEnableTrudyDone` (`SLSD 53`) to one; and evaluates
   Sunny.
3. Joe/Trudy dialogue results later set `TrudyToBar` (`SLSD 24`) to one,
   admitting Trudy's ordinary daily package schedule. INFO `00104c5b` also
   closes the argument and evaluates Joe Cobb.

Dead-Sunny fallback:

- Sunny base script `0010d9f4 GSSunnySmilesScript` sets
  `bAllowEnableTrudy` (`SLSD 55`) on Sunny's death.
- Active trigger script `0016ad04` performs the same unlock/enable transition
  when Sunny is dead, allow is one, and done is zero.
- `00174774 GSSaloonExitTriggerScript`, attached to ACTI `00172fc3` and placed
  as `00172fc4`-`00172fc6`, also unlocks/enables after allow becomes one.
- `00171b96 GSSaloonTriggerScript` contains a similar branch but has no `SCRI`
  owner in the effective owned master; it is not an admitted runtime route.

## Smallest generic compiler/runtime contract

1. Merge records by stable FormKey and preserve winner plugin, source hash, raw
   FormID, runtime FormID, and record provenance.
2. Compile `ACHR/ACRE -> NAME -> NPC_/CREA -> ordered PKID -> PACK`, authored
   initial-disabled state, and `XESP` where present.
3. Resolve `QUST.SCRI -> SCPT.SLSD/SCVR`; emit typed quest-variable identities
   instead of treating CTDA parameter 2 as an array index.
4. Emit and evaluate PSDT time windows, including midnight wrap, CTDA comparison
   operators and AND/OR chains, and the first-slice functions `GetStage`,
   `GetStageDone`, `GetQuestVariable`, `GetDeadCount`,
   `GetObjectiveCompleted`, `GetObjectiveDisplayed`, `GetQuestCompleted`, and
   dialogue `GetIsID`.
5. Select the first passing package in authored `PKID` order. Reevaluate on
   time, quest, objective, variable, death, enable-state, and explicit `EVP`
   changes.
6. Execute Travel, Dialogue, Sandbox, Eat, Sleep, Flee Not Combat, Ambush,
   Guard, and Accompany with authored PKDT flags, locations, radii, and targets.
7. Admit bounded `GameMode`, `OnDeath`, `OnTriggerEnter`, and INFO-result
   operations needed here: quest-variable/stage/objective mutation, `Enable`,
   `Unlock`, `EVP`, `PlayIdle`, and `PlaySound`. Save their authoritative state.

Actor FormIDs, package IDs, stages, objectives, variables, markers, and dialogue
results remain data. They must not become Doc-, Sunny-, Cheyenne-, Trudy-, Pete-,
or settler-specific C# branches.

## Explicit unimplemented boundary

The current `guideActorAi` recipe admits only package types Travel, Sandbox, and
Dialogue and condition functions `GetStage`, `GetQuestVariable`, and
`GetQuestCompleted`. Its compiler does not emit PSDT schedules. OpenNV has not
yet proven generic consumption of the additional package types, CTDA functions,
AND/OR grouping, quest-script events, INFO results, trigger activation, `EVP`,
navigation/path completion, scheduled furniture use, or actor-state save/reload
defined here.

Therefore this evidence establishes the owned source contract only. It does not
establish that Doc's sequence, Sunny's tutorial, Cheyenne's accompaniment,
Trudy's enable transition, or any other audited beat is currently playable.
