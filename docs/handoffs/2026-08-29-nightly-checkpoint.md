# OpenNV nightly checkpoint

Date: 2026-08-29
Worktree: `D:\code\OpenNV`
Branch: `main`

## Durable objective

Deliver an asset-free, data-driven OpenNV launcher and playable vertical slices
for legally owned Fallout 1, Fallout 2, Fallout 3, and Fallout: New Vegas across
their supported FPS, 3D-hex, and OpenXR modes. Preserve retail-faithful menus,
cinematics, HUD/Pip-Boy, character creation, gameplay state, quests, combat,
crafting, loot, saves, and honest TTW/JAM compatibility where implemented. Keep
the code and namespaces clean, use bounded evidence, and commit/push reviewable
slices without distributing proprietary assets.

The matching Codex goal remains active. This file is a stopping point, not a
completion or parity claim.

The source baseline at the final stopping pass is
`0ccb05dfca367533e6b3bcce252d57aed166ce3d`, already synchronized between
`main` and `origin/main` before this handoff-only update.

## Pushed implementation boundary

These 2026-08-29 slices are on `origin/main`:

- `531ccec` executes and cold-restores the owned Fallout 3 CG00 stage-100
  transition through `SetPCYoung 1`, leaving CG01 stage 0 unapplied;
- `14f2de2` advances FNV owned NAVM routes only across capsule-clear segments
  and allows at most three bounded replans;
- `8391dd5` activates and persists only the actual blocking in-range non-XTEL
  authored door encountered by the configured route;
- `d333f10` exports a canonical source-owned Open/Close articulation contract
  with exact moving visual/collision membership; and
- `52f5b69` validates and consumes that contract in Godot, moving only the
  controlled target and restoring saved state without replay;
- `f34bef6` preserves mass-zero target-local convex door collision and corrects
  articulated packed-collision target-local transforms;
- `982e068` accepts current CELL scene schema v14;
- `208ca8d` validates one-piece generated-collision fallback doors without a
  FormID exception;
- `80f2a02` waits for source door terminals, uses exact sweep remainder
  semantics, and keeps door/non-door recovery inside the bounded replan budget;
  and
- `45ff582` treats intermediate NAVM shared edges as tolerance regions while
  keeping the final three waypoints strict; and
- `82a2054` requires vertical convergence at intermediate edges, gives authored
  Havok triangle soup two-sided body collision, keeps activation rays front-
  face-only, and reaches the source-backed saloon porch/door route; and
- `6e73bd3` samples portal frames at synchronous closed articulation terminals,
  rejects facing fallback after non-door hits, requires a unique empty-ray
  portal candidate, and gates acceptance on the exact selected source door.
- `62a4dfa` makes the current CELL own the active WorldEnvironment/sky, restores
  XCLL background/fog on interior transitions, and renders the source-backed
  default WTHR atmosphere and clouds for the configured Goodsprings clear-day
  slice. Exterior surface/directional lighting remains provisional.
- `75a78ff` narrows that report boundary explicitly and makes the route
  validator require the exact WTHR selection, update order, sky source hashes,
  and bound cloud-texture count.

The last implementation commit is `75a78ff`. The current Debug and Release C#
builds, formatting gate, native first-run, native cold Continue, and both
manifest-backed report validators pass.
No proprietary asset or generated cache is tracked.

## Honest game status

- Fallout 1: the bounded V13ENT 3D/HUD/weapon slice and retained mobile clip are
  unchanged. Two pre-existing user-owned concept files remain dirty and must be
  preserved exactly.
- Fallout 2: the bounded character-to-Arroyo/ARTEMPLE Hex route remains pushed.
  Its clip is development footage; fixed-Y FRM billboards and black map-edge
  diamonds remain visible. FPS/OpenXR and campaign systems are absent.
- Fallout 3: ordinary CG00 flow reaches and cold-restores stage 100. Seven of
  eight stage-100 commands are applied; CG01 stage 0 is the explicit boundary.
  This is not a freely playable Vault 101 route.
- New Vegas: one fresh full four-family cache is admitted. Ordinary stage-200
  Continue reaches the physical Pip-Boy setup, Doc-house portal, and
  source-animated Goodsprings gate. r25 climbs the source-backed Prospector
  Saloon porch, crosses the second XTEL pair, saves in the saloon, and cold-
  restores with zero replayed transitions. The current active-CELL
  WorldEnvironment/sky owner also removes the Doc-house brown-fog leak and renders the owned
  `NVWastelandClear` day atmosphere/cloud pair in Goodsprings. Both reports
  validate. Exterior surface/directional lighting remains provisional and
  dynamic time/weather remains absent. Do not
  describe the actors, renderer, campaign, TTW, JAM, or OpenXR as complete or
  retail-parity.

## Exact terminal FNV boundary

The admitted cache is:

`D:\Builds\OpenNV-fnv-articulated-convex-cache-20260829-r1`

It completed once in 689.831 seconds with 6,104 files / 1,019,974,001 bytes and
four closed compiler-family identities. The owned Data tree stayed unchanged at
321 files, 48 directories, 9,875,907,799 bytes, with size/mtime digest
`b2e21cd1d34d9e9a5b62dc68790fb8e390bdaaf0a442d764260469cb270c3bfc`.

The accepted route proof root is:

`D:\Builds\OpenNV-fnv-articulated-convex-route-acceptance-20260829-r25`

It passed the Doc portal, exact one-second `0010757e` gate articulation, source-
backed porch rise, saloon portal, save, and cold Continue. Authored packed Havok
triangle soup remains source-wound and is two-sided only at the body-collision
boundary; interaction rays remain front-face-only. The first-run and cold
reports and resulting save have SHA-256 values
`8701da0500a9b7ca5620c81b1c53236d1898deeb2bf72e93021286be571908e9`,
`fca248ad7f36e2caa172940559b6f7136fab5412aedced90963d35e4eb1eb3bc`, and
`c8f6765e215f6221814cd602ee944d8411285b5d331879b246d1e41d1f22298f`.
Both manifest-backed validators pass. r20 is superseded: it allowed a saloon-
shell hit to fall through to facing activation and did not bind the selected
door identity. r25 instead resolves exact source door `0010636f` after sampling
the articulated portal at its synchronous closed terminal.

The accepted current-code environment-report proof is:

`D:\Builds\OpenNV-fnv-route-environment-acceptance-20260829-r4`

It reuses the same admitted cache without rebuilding. The first/cold report and
resulting save SHA-256 values are
`4f94eae1182ef32d1e643dc351e627bb9ed288c351b14824e88b448358c2d449`,
`7353410112de5c518fe7a52c64010f0cd0b5320b291725a51da909c7fa119f2f`,
and `40c047aac33c4a293798389ae2ec764de1d7ee480171c4b6c9885cab46d0cd81`.
The landscape/mobile visual copies remain under the r3 sky-corrected capture;
their hashes are `84462af39409b1b716dc775e314f61dc7470d39409e43ad7a5db41c885bb2012`
and `4d55a15b77b886cc23e7c3ffdd7f188c4cfb8ab1e01e6b2ab9adbc7615ae13b4`.
The sky selection is the declared bounded clear-day adapter. Its owned WTHR
values do not yet replace the provisional compiled exterior surface/directional
lighting, and it is not a dynamic weather, image-space, or retail-parity claim.

## Exact resume order

1. Read this file, `docs/architecture.md`, and both FNV evidence contracts;
   inspect `git status --short` and preserve the two unrelated Fallout 1 files.
2. Reuse the admitted cache and r25 evidence; do not rebuild the cache unless a
   compiler/source identity actually changes.
3. The current landscape/mobile FNV route copies are ready to review. Do not
   use the visually bad r2 capture; r3 is the current sky-corrected source.
4. Resume bounded FO2/FO3/FO1 work. FNV dynamic time/weather, reverse traversal,
   grid streaming, integrated OpenXR, actors, and retail visual parity remain
   separate gates.

## Audited first moves for the next session

These are read-only audit conclusions. No implementation, build, runtime, or
cache work was started after the stopping pass.

### Fallout 3

The exact unapplied eighth CG00 stage-100 command is `SetStage CG01 0` for
quest `00014e83`. Its owned stage-0 result contains four commands in order:

1. `CG01DadREF.moveto CG01DadStartMarker`;
2. `setstage CG01 5`;
3. `player.setscale .4`; and
4. `player.moveto CG01PlayerStartMarker`.

Do not flip the current boundary to applied. First extend the FO3 profile
producer to emit a versioned, typed CG01 stage-0 result with exact reference,
marker, source-transform, and hash identities, including the nested CG01 stage-5
result closure. Add the focused producer regression before adding runtime/save
application or the separately source-bound CG01 Dad actor.

### Fallout 2

The highest-value next bounded owner is the source-scripted ARTEMPLE actor at
Map 126 object serial 379, script `ACKlint.int`. Existing source evidence binds
its exact tile/rotation, PID/FID, `nmwarrga.frm`, script identity, and child
`spear.frm` inventory object. First compile its owned INT, scripts-list, MSG,
event, condition, and effect dependencies into a fail-closed implementation-
neutral contract. Do not infer an NPC name, dialogue, spear transfer, or generic
Fallout 2 VM behavior until the owned sources prove each claim.

### Fallout: New Vegas

The next renderer owner remains `CellEnvironmentSet.BuildExterior`, using the
already selected `RetailExteriorEnvironment.ResolvedEnvironment`; do not add a
second WTHR selector. Before replacing provisional exterior surface and
directional lighting, capture one bounded retail observation in Goodsprings
CELL `000daebb` with effective WTHR `000ffc88` at GameHour 8 after the weather
transition completes. Capture the active IMAD slots and an identity-basis road
light vector. That evidence should resolve both the exact sun direction and
directional energy. Reuse the admitted cache; this observation does not require
a rebuild.

## Preserved local media

- Fallout 1: `C:\Users\nbrys\Downloads\Fallout1-3D-HUD-WEAPON-FIX-MOBILE.mp4`;
- Fallout 2: `C:\Users\nbrys\Downloads\Fallout2-OpenNV-Map3-to-Temple-Sneak-Peek-50f5c73.mp4`, SHA-256
  `2546b8795d344dee0532cf99f877ff13fadbf6b34328bec385f174f2c7297abd`;
- Fallout 3: `C:\Users\nbrys\Downloads\OpenNV-FO3-Vault101-Stage90-SneakPeek.mp4`, SHA-256
  `aaa4af724e7600f5de06be277ad1e9731b8d9450a928e30b5948f33af335eaee`;
- launcher package:
  `D:\code\OpenNV\desktop\release\OpenNevada-Launcher-0.1.0-win-x64.zip`, SHA-256
  `ef7029b36023c7dccd40f524c13f1b3a2d485c7110bb2e19780594dc41220e98`.
- New Vegas current route:
  `D:\Builds\OpenNV-fnv-route-sneak-peek-20260829-r3-sky\OpenNV-FNV-Doc-Goodsprings-Saloon-sneak-peek.mp4`;
- New Vegas current route mobile:
  `D:\Builds\OpenNV-fnv-route-sneak-peek-20260829-r3-sky\OpenNV-FNV-Doc-Goodsprings-Saloon-sneak-peek-mobile.mp4`.

No media, retail asset, or generated cache belongs in Git.

## User-owned dirty files

Preserve these exactly; do not reset, restore, stage, or fold them into another
commit:

- `content/recipes/fo1-vault13-entrance-concept-v1.json`;
- `content/tests/test_fo1_concept_composition.py`.
