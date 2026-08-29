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
  controlled target and restoring saved state without replay.

The last implementation commit is `52f5b69`. Focused producer tests, Python
compilation, Debug/Release C# builds, and formatting verification passed before
the fresh owned-data build. No proprietary asset or generated cache is tracked.

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
- New Vegas: fresh compiler-family cache preparation and an ordinary completed
  stage-200 Continue/Pip-Boy `Tab`/`Escape` interaction passed. Current code
  repairs bounded replanning, actual blocking-door activation, and composite
  source articulation. A fresh cache for the articulation schema failed closed
  before admission, so current-source Doc-house→exterior→saloon and cold
  Continue acceptance are pending. Do not describe the route, actors, renderer,
  TTW, JAM, or OpenXR as complete or retail-parity.

## Exact terminal FNV boundary

One and only one fresh build was started at:

`D:\Builds\OpenNV-fnv-articulated-door-cache-20260829-r1`

It exited with code 2 after 245.365 seconds on:

`meshes\dungeons\nv_craftsmanhomesinterior\nvcraftsmanrmdooranimated.nif`

The compiler error was:

`Controller-bearing DOOR target has no joined authored collision`

The partial target contains 1,334 files / 218,620,063 bytes and no
`install-manifest.json`; it is not an admitted cache and must never be restored
or used as evidence. The owned Data tree was unchanged before/after: 321 files,
48 directories, 9,875,907,799 bytes, size/mtime digest
`25abe0156faaaad8f831b0bfc33745dc9a35cb0da65ac91e77eaab1c0323efbb`.
Run evidence is retained outside the repository at
`D:\Builds\OpenNV-fnv-articulated-door-cache-20260829-r1-evidence`.

The Goodsprings blocker `0010757e` itself has a deterministic exact articulation
contract, but the whole-cache build stopped on the different owned interior-door
pattern before emitting its route artifact. Read-only inspection showed that
target `OffDoorHotelSm` block 18 owns both visual block 23 and collision blocks
20/21/22. The shape is an eight-vertex, mass-zero
`bhkConvexVerticesShape`; there is no static sibling/root collision. Static
collision export currently admits only MOPP packed triangles, which is why the
strict articulation join saw no exported body. No retry and no Godot run
followed.

## Exact resume order

1. Read this file, `docs/architecture.md`, and
   `docs/evidence/fnv-owned-door-articulation-contract.md`; inspect
   `git status --short` and preserve the two unrelated Fallout 1 files.
2. Implement one narrow producer/runtime contract for the failing Craftsman
   door's mass-zero `bhkRigidBody` + `bhkConvexVerticesShape`: preserve its
   target-local ownership, filter/radius/points, emit deterministic convex
   collision under the same articulation wrapper, and keep unsupported convex
   variants fail-closed. Do not invent collision, reassign it to root, rotate the
   REFR, or weaken joins globally.
3. Run the focused articulation test and normal build/format checks once.
4. Build once into a new unique cache path; never overwrite or reuse the partial
   `r1` target. Require an admitted install manifest, family/output hash closure,
   and unchanged owned Data snapshot.
5. Only then run one ordinary completed-save FNV route pass followed by one cold
   Continue pass. If both pass, capture an honest Pip-Boy plus route video and
   state clearly that it is bounded development footage.
6. Resume FO2/FO3/FO1 work only after this FNV cache/route boundary is closed.

## Preserved local media

- Fallout 1: `C:\Users\nbrys\Downloads\Fallout1-3D-HUD-WEAPON-FIX-MOBILE.mp4`;
- Fallout 2: `C:\Users\nbrys\Downloads\Fallout2-OpenNV-Map3-to-Temple-Sneak-Peek-50f5c73.mp4`, SHA-256
  `2546b8795d344dee0532cf99f877ff13fadbf6b34328bec385f174f2c7297abd`;
- Fallout 3: `C:\Users\nbrys\Downloads\OpenNV-FO3-Vault101-Stage90-SneakPeek.mp4`, SHA-256
  `aaa4af724e7600f5de06be277ad1e9731b8d9450a928e30b5948f33af335eaee`;
- launcher package:
  `D:\code\OpenNV\desktop\release\OpenNevada-Launcher-0.1.0-win-x64.zip`, SHA-256
  `ef7029b36023c7dccd40f524c13f1b3a2d485c7110bb2e19780594dc41220e98`.

No media, retail asset, or generated cache belongs in Git.

## User-owned dirty files

Preserve these exactly; do not reset, restore, stage, or fold them into another
commit:

- `content/recipes/fo1-vault13-entrance-concept-v1.json`;
- `content/tests/test_fo1_concept_composition.py`.
