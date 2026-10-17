# Playable VR recovery plan

## Objective and order

The October 17, 2026 user direction replaces earlier competing priorities:

1. Recover clean, vanilla Fallout: New Vegas in Godot/OpenXR.
2. Recover clean, vanilla Fallout 3 while retaining the FNV regression pass.
3. Add selected mods explicitly, with independent profiles and compatibility tests.
4. Add TTW after both base games and its dependencies work.

The first handoff is a local build for Quest 3 over Air Link. Public release and
new interaction mechanics follow the user's physical playtest. Restore the
previous FNV play experience; do not substitute a new demonstration room.

## What the source establishes

| Finding | Source | Consequence |
| --- | --- | --- |
| Campaign entry attaches and starts the Main caller. | RuntimeCoordinator.NativeOwned.cs; Campaigns/NewVegas/Opening/RuntimeNativeOpeningStageDriver.SourceMainScriptCaller.cs | The newer dispatcher is on the ordinary product path. |
| FNV MainHold, TimedContexts and clock consumers throw. | World/Cells/FalloutCampaignMainScriptConsumers.cs | A successful native-mod startup still cannot complete these FNV frame paths. |
| FNV held-interface and scene consumers and selected CELL-load operations remain unimplemented. | RuntimeCoordinator.MainPlayer.cs | Menu/frame/travel recovery has concrete missing owners. |
| Main failure is retained and disables gameplay producers, including XR input. | World/Cells/RuntimeNativeSourceMainScriptCaller.Async.cs; FalloutReferenceWorld.FunctionalFailure.cs; RuntimeNativePlayer.cs; RuntimeNativePlayer.OpenXR.cs | Existing hands, weapons, quests and dialogue can be present but unusable after an earlier shared failure. |
| Native extension discovery defaults to installed; an explicit none selection exists. | RuntimeCoordinator.Launcher.cs; Content/RuntimeLiveContentSource.NativePlugins.cs; scripts/Start-OpenNV.ps1 | A vanilla candidate must explicitly select its base records and disable native extensions. This does not fix the frame failures. |
| The historical package uses Godot 4.7.2/.NET 9; current source uses pinned Godot 4.8-dev7/.NET 10. | Historical build-toolchain.json; scripts/GodotToolchain.ps1; scripts/ManagedToolchain.ps1 | Keep toolchain differences visible when comparing builds; do not blame the renderer without a matched failure. |

**Leading hypothesis:** unfinished shared runtime integration was made mandatory
between the previously playable FNV path and its existing gameplay systems.
Optional mod startup adds independent failures. The source proves missing
consumers; a paired ordinary run must establish the actual first failure and
the smallest corrective change.

Historical commit 61ea88c7 and the October 9 FNV package at 03c3d23a are useful
comparison points. Their old reports are leads to replay, not current test passes.

## A. Consolidate the repository

Reuse the existing integration branch and PR while reconciling the current work.
Inventory every registered worktree's tracked edits, untracked source and unique
commits. Compare patch/content equivalence against current main; squash-merged
commits must not be reapplied merely because ancestry says they are unmerged.

Preserve unique source before cleanup. Integrate useful changes with their actual
dependencies; resolve duplicates and obsolete experiments explicitly. Never copy
retail inputs, saves, private reverse-engineering artifacts or generated caches
into Git. Keep one active candidate, not another collection of implementation forks.

Run the required runtime and owned-data gates, complete the checked PR, synchronize
main, and remove/archive superseded worktrees and task branches only after their
source is accounted for. Leave no outstanding task PR. The final primary checkout
must be clean main equal to origin/main. Source consolidation alone is not a
playability baseline.

## B. Restore vanilla FNV on the consolidated code

1. Use a separate base-game profile with explicit record/plugin selection and
   native extensions disabled. Preserve existing mod profiles and original saves.
2. Replay the historical packaged path with a compatible copied checkpoint, then
   exercise current vanilla New Game and Continue. Compare source stack, startup,
   first healthy frames, input, CELL changes and save behavior. A historical save
   format mismatch is a separate failure, not evidence that all gameplay broke.
3. Trace the first actual fault through the mandatory dispatcher to the existing
   gameplay owner. Correct the integration or narrowly revert the offending
   integration and reconnect verified behavior. Do not replace missing behavior
   with successful constants, discard faults or force quest outcomes.
4. Make movement, scripts, interactions and saves consume consistent C# state.
   Reuse existing game systems; avoid a second gameplay path or a new engine rewrite.
5. Repeat ordinary input after each repair. Confirm visible results and retained
   state rather than counting callbacks or component tests.

## C. Required FNV regression route

Run from the real game menu through character creation and Doc Mitchell's exit,
Goodsprings dialogue/tutorial and connected interiors/exteriors, then travel to
Primm and repair/recruit ED-E through ordinary conditions and dialogue.

Exercise enemy attacks, player damage/healing, companion combat, death and loot,
inventory/equipment, trade, and an available crafting transaction. Keep crafting
failures explicit. Save, Quit normally, and Continue in three fresh processes,
including a CELL change and a post-combat state. Copies of historical checkpoints
speed regression isolation but do not replace the fresh-game route.

## D. Simulator and local headset candidate

Use the existing OpenXR simulator/input tooling and the same exported executable
and gameplay owners intended for the headset. Inspect both final eyes in motion.

| Area | Blocking checks |
| --- | --- |
| View and movement | Stable head tracking, scale, height, locomotion, collision, snap-turn pivot, stairs/doors, dialogue camera behavior. |
| Body and hands | One visible hand pair, attached forearms/body, correct grips, no pose lag or duplicate hands through movement and turns. |
| Pip-Boy and menus | Readable wrist screen; reliable focus/pointer; Stats/Items/Data navigation; equip/use; quests/maps/radio; open/close and recovery from every blocking screen. |
| Weapons | Source-bound equip, aim, fire, reload, ammo and damage; both-hand attachment; animation/audio/effects. Inventory every vanilla WEAP and report coverage by weapon and behavior family. One working gun is not all guns. |
| Interaction | Dialogue choices and control release, loot/container, trade, crafting, quest updates and companion behavior. |
| Persistence | Ordinary Save/Quit/Continue, same state in flat and XR, cold reload after travel and combat. |
| Performance | Measure CPU/GPU frame-time distributions, stalls, allocations, load time and repeated-travel memory. Tune against the actual Quest/Air Link refresh configuration. |

Use short targeted recordings only for visual checks and remove temporary frames
in finally/cleanup paths. Keep selected useful evidence and the requested output.
Simulator evidence does not certify the physical Air Link tracking/latency path.

Only after the runnable main baseline passes, start one fresh release branch.
Build the asset-free local package from its exact clean commit, verify the exported
launcher and gameplay, and provide one clear Quest 3/Air Link launch entry, controls
and precise remaining limitations. Merge completed release work and clean up again.

## Next two-hour recovery block

Treat these as work timeboxes, not invented completion guarantees:

- First 30 minutes: preserve/reconcile source inventory and reproduce the first
  vanilla failure against the historical reference.
- Next 60 minutes: repair the demonstrated integration regression and replay the
  affected gameplay, CELL and save paths.
- Remaining 30 minutes: run the available simulator regression and required checks,
  then report the actual playable build or the exact remaining blocker.

The objective is a usable FNV recovery candidate as soon as the gates pass.
Do not spend this block implementing FO3, native mods or TTW to make FNV start.
If a repair is larger than the block, keep the same candidate and report the
reproduced blocker immediately rather than expanding scope.

## Promotion gates

Before pushing runtime or claim changes, run the selected owned-data audit and:

~~~powershell
.\scripts\Test-GodotRuntime.ps1 -Godot 'D:\code\gd\Godot_v4.8-dev7_mono_win64\Godot_v4.8-dev7_mono_win64_console.exe'
git diff --check
~~~

Mark the dated baseline only after ordinary gameplay and complete saves pass.
Keep build/component, simulator, physical-headset and matched-retail evidence
separate. Later phases must rerun the accepted FNV route before promotion.

## Documentation policy

The source and fresh executed evidence determine status. current-work.md holds
only the current result and next action. status.md holds a short evidence matrix.
architecture.md describes actual ownership and known gaps. Older technical notes
may help locate a test or contract; they cannot override this delivery order or
turn an old report into a current pass.
