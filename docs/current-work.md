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
- The local simulator's action binding resolver and expiring controller leases
  are repaired. Fresh Release input exercised wrist opening, ray selection of
  ITEMS, input release, reload, firing, snap turns and walking through the actual
  Prospector Saloon door. Initial outdoor median is about 14 ms; settled measurement
  and physical-headset acceptance remain pending. The full runtime gate passed.
- Actual Sunny activation exposed an unbound GetDead condition. It now uses the
  shared actor death owner with source scopes and reference adjustment. Synthetic,
  vanilla-owned and cold-state checks pass. Actual Sunny conversation now opens
  and completes without that error.
- Door autosave ran before destination actor initialization completed. Both
  interaction and door autosaves now use the existing readiness queue, retaining
  capture guards and rechecking player health before writing. The saloon autosave
  was written and loaded in a fresh process with its cell and player state intact.
- Rendered FO3 New Game reaches its birth quest, but an anonymous sound emitter
  broke telemetry publication. Readiness now reports that per-voice error without
  throwing away controls; persistence still refuses an unowned continuation.
  A fresh rendered replay reaches character selection/stage22 and quits normally.
- Fresh FNV character creation passed name, appearance, SPECIAL, couch dialogue,
  tags and traits with ordinary input. A new manual save was written inside Doc's
  house. The original copied save retains an earlier Doc-exit script failure:
  the two-argument ForceWeather command stopped before resetting VCG02.
  Its optional override flag is now implemented and source-audited. Old faults
  are retained; fresh exit replay is next.
- Tutorial weapon queries now share live equipment/drawn state and the documented
  script animation-category mapping. Owned bottle-script hits advance the objective;
  actual tutorial shooting remains pending. Controller-ray 9mm equip, reload and
  firing worked in the exported simulator, alongside the earlier laser-pistol check.
- Original user save hash remained unchanged. Tests use separate copied saves.

## Next work

Export the weather/weapon-query fixes and continue the fresh character through
Doc's exit and Sunny's tutorial. Repeat the full gate; the prior run exposed an
uninitialized vitals owner in the save-deferral fixture, now corrected with a real
owner and defeated-player coverage. Measure settled performance, then exercise
loot and cold saves with ordinary inputs. Continue
the Goodsprings/Primm/ED-E regression route without forced quests or entities.

Complete checked PR #207, synchronize main, and retire accounted branches and
worktrees. Source reconciliation has preserved dirty source and identified
superseded experiments; cleanup is pending. Then use one fresh release branch
for the local handoff. Root owns builds, live input and Git integration; agents
review gameplay, launcher and source reconciliation. Frame recording is off.
