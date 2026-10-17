# Implementation plan

## Objective and order

Restore a practical FNV Godot/OpenXR playtest for Quest 3 over Air Link, consolidate
it onto clean main, then prepare one local release candidate. Continue with clean
FO3, followed by mods, then TTW. Use source and reproduced behavior to decide what
survives; there is no requirement to preserve an unfinished architecture.

## Diagnosis and chosen repair

A fresh pre-recovery vanilla launch failed during player construction in Doc
Mitchell's house. The mandatory Main/Player integration also rejected ordinary
menus and introduced tracked-pose refusals. The October 9 implementation still
loaded the user's copied save and supported movement and the Pip-Boy.

Restore that coherent gameplay/native/tool/test boundary with the current Godot
and .NET toolchain, retaining only independent fixes. Preserve recent work in Git
history. Do not mix incomplete Main partials into the restored owners or replace
missing behavior with successful constants.

This restoration is applied. Exported Release simulator play has exercised
Goodsprings, tutorial dialogue and bottle hits, the well fight and corpse loot,
wrist-ray fast travel, and ordinary save/cold continuation. These were bounded
runs, not a completed campaign. Final creature portal navigation, post-travel
population/persistence, performance and the ED-E route remain open.

## Active loop

1. Complete compiler/format/contract checks and the owned-content audit.
2. Export a fresh Release from a committed source tree. Use isolated vanilla
   profiles and copied saves, with native extensions off.
3. Measure settled CPU/GPU frame times with no recording and no active bot.
   Separate diagnostic overhead from gameplay. Repair demonstrated hotspots at
   their owner without dropping state transitions or substituting fake success.
4. Exercise actual menus, locomotion/head motion, snap turn, weapon equip/fire/
   reload, Pip-Boy pages and pointer focus, dialogue, doors, loot and cold saves.
5. Replay New Game through Doc's exit and Goodsprings. Continue to Primm and
   repair/recruit ED-E through real requirements, with combat, loot and an
   available crafting transaction. Historical checkpoints accelerate isolation;
   they do not replace fresh progression.
   Nipton, Novac, the Strip and other towns remain on the same unrestricted
   runtime loading path. Check missing actor construction at its shared owner.
   Exercise fast travel through both flat and VR interfaces, including elapsed
   time, followers, destination state and persistence.
6. Fix the next actual blocker and repeat the affected play action. Keep one
   candidate and one integration owner. At the user's request root works alone
   and owns builds, live input and Git; do not restart other agents.

Inspect both final eyes for VR acceptance. Check held-trigger/stick release on UI
close, tracking changes, stable hands and weapon grips, readable wrist pixels and
controller menus. Simulator success precedes the user's physical-headset test.
Do not infer all weapons or complete campaigns from one working encounter.

## Consolidation and local handoff

Preserve unique committed/uncommitted source before removing obsolete checkouts.
Review content equivalence rather than merging every old branch. Keep one verified
local source bundle and the accounted dirty-source snapshot; remove redundant
branches/worktrees only after their contents are safe. Do not accumulate captures,
build caches or duplicate packages.

PR #208 completed the runtime promotion with all five CI checks passing and local
main equal to origin/main. A clean asset-free package and dedicated Quest 3/Air
Link launcher now use an isolated accepted save. Physical testing is next; see
current-work.md for the exact candidate and remaining gameplay issues. Start any
subsequent implementation on one fresh feature branch from current origin/main.
Do not advertise unavailable FO3 VR routes or campaign completion.

## Required checks before pushing

Run the selected owned-data audit, then:

~~~powershell
.\scripts\Test-GodotRuntime.ps1 -Godot 'D:\code\gd\Godot_v4.8-dev7_mono_win64\Godot_v4.8-dev7_mono_win64_console.exe'
git diff --check
~~~

Keep checks focused on the actual changed owners. Record factual results in
current-work.md; do not recreate a large proof framework or stale task history.
Matched retail evidence is required only for an actual parity claim. No such
claim is needed to report a bounded playable local candidate.
