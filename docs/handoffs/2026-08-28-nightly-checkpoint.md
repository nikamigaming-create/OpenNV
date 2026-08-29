# OpenNV nightly checkpoint

Date: 2026-08-28
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

The matching Codex goal is active. This checkpoint is a stopping point, not a
completion claim.

## Pushed stopping point

The following bounded slices are on `origin/main`:

- `097d8cd` derives and persists owned New Vegas opening vitals;
- `57ba827` suspends distant prepared New Vegas CELL spaces;
- `7c332bf` advances the ordinary Fallout 3 birth flow through stage 90;
- `0317ff6` traverses the owned Fallout 2 Arroyo exit grid into ARTEMPLE;
- `50f5c73` removes the non-source opaque Fallout 2 Temple wall proxy;
- `a97cd29` refreshes the current multi-game route truth;
- `2386b58` splits New Vegas cache compiler provenance into static, CELL,
  opening, and actor families;
- `a99acbc` compiles the exact owned Fallout 3 stage-90 timer and stage-100
  result through the unapplied CG01 stage-0 boundary.

The cache-family slice passed 11 focused tests, Python compilation, Debug and
Release builds, formatting verification, and a synthetic opening-only stale
cache proof. An opening change now invalidates opening and dependent actors
without invalidating unchanged static or CELL output. Restore remains read-only
and legacy caches still fail closed.

## Honest game status

- Fallout 1: retain the existing good 3D/HUD/weapon clip. The two dirty Fallout
  1 concept files listed below predate this checkpoint and must not be discarded.
- Fallout 2: ordinary grounded movement reaches exact exit 1738 from Map 3 and
  arrives at ARTEMPLE Map 126 tile 16486. The clip shows the owned directional
  FRMs and all 45 owned Temple wall FRMs. Fixed-Y billboards and exposed black
  map-edge diamonds remain visible, so this is development footage, not parity.
- Fallout 3: ordinary menu/character/birth-room flow reaches and cold-restores
  stage 90 with the owned fade and sound. Stage 100 is not implemented at
  runtime.
- New Vegas: vitals, populated campaign Pip-Boy, reciprocal Doc house/exterior/
  saloon routing, and the current/direct-neighbor active set are implemented.
  Normal current-source capture is blocked because existing caches use the old
  monolithic identity. One explicit fresh family-cache migration is required.
- TTW, JAM, OpenXR, full retail presentation, package AI, dialogue/LIP, and
  campaign-wide parity must not be described as complete.

## Next bounded Fallout 3 slice

The Fallout 3 stage-100 compiler work is coherent but integration-incomplete and
is committed in `a99acbc`. It changes exactly:

- `content/recipes/fo3-goty-opening-profile-v1.json`;
- `content/tools/prepare_fo3_profile.py`;
- `content/tests/test_fo3_profile_transition.py`.

It resolves the exact CG00 `runTimer == 1`, `timer > 0`,
`timer -= GetSecondsPassed`, stage-90-to-100 gate; binds stage 100's eight ordered
commands; applies the contract only through `SetPCYoung 1`; and records CG01
stage 0 as an explicit unapplied boundary. The focused synthetic file passes
7/7. It still lacks `Fo3Stage100Transition.cs`, `Fo3OpeningFlow.cs` timer/final
state/Dad-disable wiring, save/cold restore, an owned-profile compile, C# builds,
and formatting verification. Do not advance beyond CG01 stage 0 in this slice.

Two unrelated Fallout 1 files are also dirty and user-owned:

- `content/recipes/fo1-vault13-entrance-concept-v1.json`;
- `content/tests/test_fo1_concept_composition.py`.

Preserve them exactly. Do not reset, restore, stage, or fold them into another
commit.

## Private media retained outside the repository

- Fallout 1: `C:\Users\nbrys\Downloads\Fallout1-3D-HUD-WEAPON-FIX-MOBILE.mp4`;
- Fallout 2: `C:\Users\nbrys\Downloads\Fallout2-OpenNV-Map3-to-Temple-Sneak-Peek-50f5c73.mp4`, SHA-256
  `2546b8795d344dee0532cf99f877ff13fadbf6b34328bec385f174f2c7297abd`;
- Fallout 3: `C:\Users\nbrys\Downloads\OpenNV-FO3-Vault101-Stage90-SneakPeek.mp4`, SHA-256
  `aaa4af724e7600f5de06be277ad1e9731b8d9450a928e30b5948f33af335eaee`.

The Fallout 2 raw capture and manifest remain under
`C:\Users\nbrys\AppData\Local\OpenNV\proofs\fallout2\exit-sneak-peek-20260828-r1`.
No media or retail assets are committed.

## Exact resume order

1. Read this file and `docs/architecture.md`, then inspect `git status --short`.
   Preserve the two dirty Fallout 1 files.
2. Finish only the Fallout 3 stage-100 runtime/persistence slice described above.
   Compile the owned profile, run its focused tests, Debug/Release builds, and
   formatting verification, then commit and push it before advancing to CG01.
3. Perform one explicit New Vegas family-cache migration:

   ```powershell
   python content/tools/prepare_legal_assets.py --data-root "D:\SteamLibrary\steamapps\common\Fallout New Vegas\Data" --cache-root "D:\Builds\OpenNV-fnv-family-cache-20260829-r1" --cell-recipe "goodsprings-doc-mitchell-house-v1" --preferences-ini "C:\Users\nbrys\OneDrive\Documents\My Games\FalloutNV\FalloutPrefs.ini"
   ```

4. Use that accepted cache through the normal launcher path and record one honest
   New Vegas acceptance showing menu, intro skip, Doc house, HUD, populated
   Pip-Boy open/close, reciprocal exterior/saloon travel, and cold Continue.
5. Only after visual review, update the short Fallout 1/2/3 reel with New Vegas.

No Godot, Python compiler, FFmpeg, or capture process was intentionally left
running at this checkpoint.
