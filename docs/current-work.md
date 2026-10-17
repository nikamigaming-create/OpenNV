# Current work

Updated 2026-10-17. One integration owner is recovering a playable Godot/OpenXR
FNV candidate for Quest 3 over Air Link. Delivery order remains **vanilla FNV,
vanilla FO3, mods, then TTW**. Existing gameplay is a regression requirement.

## Current implementation

The October 9 gameplay implementation at 03c3d23a is restored as one coherent
runtime/native/tools/test boundary. Godot 4.8-dev7 and .NET 10 remain pinned.
Recent source work is retained in normal Git history at 17a56554. The incomplete
mandatory Main/Player integration is removed, not bypassed with success stubs.

Independent retained fixes cover signed font advances, compiler compatibility,
isolated launcher profiles, explicit official-content selection, OpenXR launch
selection, wrist pointer cancellation and input release, and player visibility.
Native extension modules are disabled. FO3/mod/TTW expansion is deferred.

## Executed checks

- Fresh pre-recovery New Game failed constructing the player in Doc Mitchell's
  house: source-Player-post-null-distinct-manager-word-constructor-and-writers-unowned.
- Historical executable loaded a copied save, moved and opened its actual Pip-Boy.
- Restored Debug and Release compile without warnings. The complete required
  runtime gate passed, including native Godot loading and navigation checks.
- Restored Debug simulator loaded Goodsprings through the actual game save menu.
  Both eyes show the world, hands, wrist device and equipped laser pistol.
  Controller locomotion moved the player. Ordinary Quit completed.
- A fresh exported Release loaded the same copied save and accepted controller
  movement. Automatic diagnostic frame readback inflated its frame time; with
  readback disabled, settled outdoor median draw interval was about 33 ms.
- Sampling identified repeated particle-collider matrix work and source-record
  decompression. Per-pass collider poses and bounded in-process record reuse are
  implemented; focused moving-collider, callback, cache and compression checks pass.
- The local simulator ignored action binding paths. Its general binding resolver
  and expiring controller leases are repaired in a separate local simulator build.
  Final controls/performance still need replay. Physical-headset acceptance is pending.
- Original user save hash remained unchanged. Tests use separate copied saves.

## Next work

Repeat the full gate for the performance changes. Export a fresh Release,
measure and repair actual performance/input failures, then exercise dialogue,
weapons, Pip-Boy pages, doors, loot and cold saves with ordinary inputs. Continue
the Goodsprings/Primm/ED-E regression route without forced quests or entities.

Complete checked PR #207, synchronize main, and retire accounted branches and
worktrees. Source reconciliation has preserved dirty source and identified
superseded experiments; cleanup is pending. Then use one fresh release branch
for the local handoff. Root owns builds, live input and Git integration; agents
review gameplay, launcher and source reconciliation. Frame recording is off.
