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
  keeping the final three waypoints strict.

The last implementation commit is `45ff582`. Focused producer tests, Python
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
- New Vegas: one fresh full four-family cache is admitted. Ordinary stage-200
  Continue reaches the physical Pip-Boy setup, Doc-house portal, and
  source-animated Goodsprings gate. It does not yet prove the Prospector Saloon
  stair: loose X/Z tolerance accepted waypoint 57 without completing the
  0.257-metre rise. The later run fails closed at waypoint 62/64 from that
  invalid under-height state, 4.889 metres from the portal. It emitted no route
  report, so cold Continue and video were not run. Do not describe the route,
  actors, renderer, TTW, JAM, or OpenXR as complete or retail-parity.

## Exact terminal FNV boundary

The admitted cache is:

`D:\Builds\OpenNV-fnv-articulated-convex-cache-20260829-r1`

It completed once in 689.831 seconds with 6,104 files / 1,019,974,001 bytes and
four closed compiler-family identities. The owned Data tree stayed unchanged at
321 files, 48 directories, 9,875,907,799 bytes, with size/mtime digest
`b2e21cd1d34d9e9a5b62dc68790fb8e390bdaaf0a442d764260469cb270c3bfc`.

The latest route proof root is:

`D:\Builds\OpenNV-fnv-articulated-convex-route-acceptance-20260829-r10`

It passed the Doc portal and exact one-second `0010757e` gate articulation. At
the saloon porch, waypoint 57 was accepted by loose horizontal tolerance before
the player completed the source/NAVM-backed 0.257-metre rise. By exterior
waypoint 62/64, the capsule center was 0.430 metres below the target and its
bottom overlapped the upper landing by 0.333 metres. The correctly placed packed
collision belongs to REFR `001055e0`, base `0010243e` `NVProspectorSaloon`, asset
`351f418a5cca45d12b71`; the door, XTEL pair, NAVM tail, and collision placement
all audited correctly. The remaining waypoint distance was 1.304 metres and the
portal was 4.889 metres away. No report was emitted. A candidate generic fix—
require the existing 0.18-metre vertical convergence alongside loose
intermediate X/Z tolerance, with the fresh blocked sweep normal available only
for recovery—built cleanly but was reverted because two native-window proofs
were throttled before acceptance began. Their incomplete `r11` and `r12` roots
were recycled. Do not run cold Continue or make a route video until a fresh
first-run proof and validator pass.

## Exact resume order

1. Read this file, `docs/architecture.md`, and both FNV evidence contracts;
   inspect `git status --short` and preserve the two unrelated Fallout 1 files.
2. Apply the audited generic reach rule against the admitted cache: loose
   intermediate X/Z tolerance must also require vertical convergence within the
   existing 0.18-metre limit. A fresh sweep normal may guide recovery only; it
   must never waive the height gate. Do not rebuild the cache.
3. Fix only that evidence-backed navigation/step mismatch. Do not disable
   the saloon collision, enlarge final tolerance, teleport the player, skip a
   waypoint, or special-case a FormID.
4. Run Debug/Release, format/diff checks, then one new unique first-run proof.
   Require `OPENNV_FLAT_ROUTE_TRAVEL_PASS phase=first-run`, no errors, and the
   manifest-backed `flat-route-travel` validator.
5. Only after first-run passes, run one cold process against the same save and
   require the `flat-route-reload` validator. Then capture an honest bounded
   Pip-Boy/route video.
6. Resume FO2/FO3/FO1 work after recording this FNV boundary or closing it.

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
