# Fallout 2 bounded first-slice branch ledger

Schema: `opennv-fo2-first-slice-branch-ledger/v1`

Evidence date: 2026-08-30

Status: **two bounded, cold-restorable branches over one selected identity; no
dead-guardian shortcut and no full-campaign or retail-parity claim**

This is the canonical asset-free status ledger for the current Fallout 2 Hex
slice. It records hashes, record identities, state transitions, and explicit
boundaries only. It contains no Fallout data, decoded frames, commercial asset
paths, executables, or disposable cache contents.

## Shared prefix

Both retained branches use the same registered source profile
`59875f90116bfb4f8dc4eee5de3aa6d105e7ba08739f03f64cf73e279444bd79`
and the same modified owned Chitsa basis:

| Contract | Current value |
| --- | --- |
| Selection mode / identity | `custom-created-from-owned-rules` / `custom` / source-basis Chitsa / Female |
| Owned GCD SHA-256 | `e5fe3b89d7c62edf629e249e4ffda2f7486701a73398e77b0ae9c0fc2a4bb010` |
| Appearance recipe SHA-256 | `df10b7379f511a5d7b571edcb5f9a8013757e7d1069914d3ada03b04e3d90bf0` |
| Opening handoff | terminal source frame `1145`; Skip applies the terminal state, presents black, then releases controls |
| Map 3 exit | serial `1738`, tile `31307`, source-path SHA-256 `9895a6b2cffcfe36bfad927bb28377ea69edc732afc7a7a66fbb357c97d57413` |
| ARTEMPLE arrival | Map `126`, tile `16486`, elevation `0`, rotation `0` |
| ARTEMPLE MAP SHA-256 | `11fc44f3fad558f3cb9e12bf41018a023ef7de8def365d516a44336157fec050` |
| Save schema | `opennv-fo2-character-arroyo-save/v12` |

The owned classic GCD/FRM state remains identity and gameplay authority. The
verified owned FNV full-body donor is presentation-only, must match the selected
sex/body/outfit/socket contract, and has no procedural, silhouette, standee, or
FRM-player fallback.

## Branch P — peaceful trial to live ARVILLAG

Retained checkpoint: r26.

| Gate | Proven state |
| --- | --- |
| Trial contract SHA-256 | `933e78daa33186d7077ccbe2f77d5b5e4d3f98811089597f980453c7110f47a6` |
| Cameron outcome | exact tagged-Speech sequence complete; global `10 = 2`; Cameron released; trial door opened/unlocked |
| Klint outcome | alive; `ACKlint` map-enter moves the exact gate to tile `19698` |
| Village transition | exit serial `476`, ARTEMPLE tile `22115` to ARVILLAG tile `11683` |
| ARVILLAG MAP / walk mask | `0edcdff2afb6fac7e8203ce9eae8ba4663d37f3be112d3ef4713af3093d8d52a` / `17747a8f61dd5c315bfacde676155ac02689d8619becdca39d66439544aa2b62` |
| First live action | configured Godot input moves exact source hex `11683 -> 11482` |
| Saved stage | `arvillag-first-legal-action`, map `4`, tile `11482`, controls enabled |
| Save SHA-256 | `aff8f1f5a9e3c06cae8df6c79bdee68851f5a2aa3a1ced406a57a02012addeea` |
| Write ledger SHA-256 | `63c069fa62e9da7c493d1f132ad0c28bc8e8d542cd377e462aa265fbb9878db7` |
| Full route report SHA-256 | `75d3436c8371f2ce965e3c50c56e63de841e140bdd9ce60b8bdbfa9618c56d90` |
| Cold-restore report SHA-256 | `29cf59f1b060f6686065ef71a8dd30e72630edd8fb1a0032a059c7ab6f16fcf8` |

The arrival actor retains the selected GCD identity and exact admitted donor
outfit. This branch does not defeat or loot Klint and therefore reaches
ARVILLAG unarmed.

## Branch C — Temple combat, loot, equipment, and save

Retained checkpoint: r29.

| Gate | Proven state |
| --- | --- |
| Encounter identity | critter serial `379`, PID `01000003`, SID `04000001`, tile `21101` |
| Source AP route | 29 adjacent source-walkable steps; movement cost `1`; 3 AP-restoring end turns |
| Bounded combat | deterministic player-only melee; 38 successful attempts with 13 AP-restoring end turns; final player AP `4` |
| Defeat / loot | target HP `0`; exact nested Spear serial `378`, PID `00000007`, quantity `1` looted |
| Inventory | owned `INVBOX` source SHA-256 `ae347b83f24d00fbf5806f80a9084855d6ae275f31388cfabee90b700903a657`; selected character remains Chitsa |
| Equipment | Spear equipped; exact owned female `GA` composite is active |
| Saved boundary | ARTEMPLE Map `126`, tile `20900`; target hidden after loot; combat inactive |
| Save SHA-256 | `005cadb487c80b85134bdabb95f13c191322b518bea945821e8996c448fac303` |
| Integrated write ledger SHA-256 | `19933f3955f0f71be4720712f8364f18a2fb1a5e7c178eebe09fb7d6f59b914c` |
| Integrated cold-restore SHA-256 | `f171e66ca347a5b3002a9aff5bb35b08563c7c55f129bafc0e868fdba70c8610` |

Cold restore retains the same modified Chitsa GCD identity, target defeat,
Spear-looted/equipped state, `GA` presentation, inventory selection, player AP,
and save hash.

## Deliberate non-join

These are two alternatives after the shared picker/opening/Map-3 prefix, not a
single save that claims mutually exclusive outcomes. Branch P owns the peaceful
Cameron state and an alive Klint before ARVILLAG. Branch C owns Klint's bounded
combat state and stops in ARTEMPLE after loot/equip/save. Its trial-progress
record remains at the initial Cameron stage and is not promoted into the
peaceful route.

OpenNV therefore does not:

- infer the peaceful trial globals after killing the guardian;
- reuse the r26 moved-gate state in the combat branch;
- invent a dead-guardian direct exit to ARVILLAG;
- claim target AI/turns, general INT execution, retail combat formulas,
  campaign-wide persistence, complete Fallout 2, FPS/OpenXR, or visual parity.

The next campaign-continuity change must come from a separate owned-script and
state contract. It may not be inferred from either retained proof.
