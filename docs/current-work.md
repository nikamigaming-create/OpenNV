# Current work

## User direction — 2026-10-17

Recover the previously playable Godot/OpenXR game for a local Quest 3 / Air Link
test. Delivery order is **vanilla New Vegas, vanilla Fallout 3, mods, then TTW**.
VR is part of each game's acceptance from the start.

Consolidate outstanding source onto checked main, establish a working baseline,
then begin one fresh release branch. The next FNV acceptance route includes
Goodsprings, travel to Primm, ED-E repair/recruitment, companion combat and loot,
dialogue, inventory/Pip-Boy, and persistent continuation. Crafting remains an
explicit test target. Existing playthroughs are regression requirements.

## Source findings

Ordinary startup attaches and starts the mandatory source Main caller in
RuntimeCoordinator.NativeOwned.cs. Its New Vegas consumers still throw for
MainHold, TimedContexts and renderer/registered-clock callbacks in
FalloutCampaignMainScriptConsumers.cs. RuntimeCoordinator.MainPlayer.cs also
contains unimplemented FNV interface/scene and CELL-loading consumers.

A Main failure is retained in the shared world. RuntimeNativePlayer.cs and
RuntimeNativePlayer.OpenXR.cs then stop gameplay movement/actions. Existing
VR presentation and gameplay implementations remain in the source. Disabling
native mods alone cannot repair these required frame consumers.

This is a code-proven incomplete production path and the leading regression
hypothesis. The first failure in a fresh vanilla run and the exact breaking
revision have not yet been reproduced in this recovery session.

## Repository inventory at planning time

- main and origin/main: 147a3f9d.
- Current branch: codex/fo3-model-load, committed HEAD 17eddb43.
- Existing PR: #207; current checkout has 169 changed/untracked paths.
- 72 local branches; 24 registered worktrees; 18 worktrees have changes.
- An October 9 exported FNV package is present locally with source identity
  03c3d23a. Historical revisions are comparison inputs, not today's acceptance.

## Next execution

Follow [implementation-plan.md](implementation-plan.md). Preserve source changes
and saves, reconcile branch contents, reproduce vanilla FNV against the historical
reference, repair the first demonstrated integration regression, and verify the
ordinary route in the simulator. Keep one integration owner and candidate.

No new runtime, simulator, save or headset pass is claimed by this planning
reset. Frame recording is off. Prior narrative progress reports are not gates.
