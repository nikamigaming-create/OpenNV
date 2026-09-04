# Native owned-data promotion checkpoint — 2026-09-04

## Objective

Recover the uncommitted native owned-data work into a reviewable branch, restore
the repository gates, and state the next promotion boundary without overstating
runtime acceptance.

## Branch and provenance

- Branch: `codex/native-owned-promotion`
- Starting local commit: `240d72c` (`Keep FNV intro covered until camera ownership`)
- Retail inputs remain read-only. No retail asset, executable, save, or derived
  content cache is part of this checkpoint.
- `origin/main` contains the squashed shared-history commit
  `4e1058e` (`Advance shared Fallout retail runtime slices (#25)`). Its ancestry
  is reconciled after the bounded source commits while retaining this branch's
  tree.

## Recovered implementation

- Desktop registration and launch contracts for cacheless, sealed owned-data
  source stacks and local mod layering.
- In-memory plugin, BSA, DAT1, MAP, NIF, DDS, sound, cell, actor, and native
  installation transport used by the bounded native audit routes.
- Bounded Fallout 1 Vault 13 and Fallout 2 Arroyo owned-data presentations and
  their denominator/audit probes.
- The partial New Vegas native opening state machine through stage 200,
  including character choices, farewell loadout, and stack-scoped cold restore.
- The two oversized Fallout 3 partial classes were split mechanically so every
  source file remains below the 2,000-line architecture ceiling.
- New source literals were converted to named contracts; the source-constant
  debt baseline was not expanded.

## Verification

The canonical gate was run with the Godot 4.7.2 Mono console executable:

```powershell
.\scripts\Test-GodotRuntime.ps1 -Godot D:\code\gd\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe
```

Result:

- Python suite: 641 passed, 1 expected skip.
- Desktop launcher suite: 63 passed.
- Release and Debug C# builds: passed with zero warnings and zero errors.
- `dotnet format --verify-no-changes`: passed.
- Source-constant policy: passed with 1,245 current findings against the 1,279
  baseline (34 removed, no baseline increase).
- Contract probes: container inventory, actor animation, complexion, package
  selection, UI tile, package placement, owned auxiliary resource, and Fallout
  sound passed.
- Runtime gate report: `opennv-godot-runtime-gate/v1`, status `pass`, clean
  runtime `true`, OpenXR rig `true`; hardware and retail differential were not
  requested by this source-promotion gate.
- `git diff --check`: passed (line-ending conversion warnings only).

## Honest promotion boundary

This checkpoint promotes the source tree and its automated runtime contracts,
not the New Vegas opening to the current playable baseline. The native opening
still lacks accepted normal-input menu → New Game → stage 200 → exit → fresh
Continue evidence, and its stack-scoped save must be joined to the canonical
shared gameplay/save authority. Owned menu presentation, the full psychological
questionnaire, rendered character application, authored Doc package/dialogue
presentation, and final retail/Godot visual comparison remain explicit blockers.

The next corrective owner is the configured-input acceptance route and canonical
save join described in `docs/whole-game-delivery-plan.md`. No retail-parity claim
is made by this checkpoint.
