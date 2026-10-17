# Implementation plan

## Objective and order

Repair the failed flat FNV playtest, then verify Godot/OpenXR for Quest 3 over Air Link, consolidate
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

Package f failed the user's October 17 flat test. First reproduce and repair
Continue/spatial saves, missing quest text, absent creatures, passive human and
creature combat, and shadow pop-in. Use the recording/log and copied save; require
actual terrain play after component checks. See current-work.md for verified
state. The previous headset-ready handoff is withdrawn.

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

Package f from the earlier runtime promotion failed the user's flat test despite
its narrower passing gates. Keep it as historical evidence. A replacement needs
the repair gates and actual play observations in current-work.md before another
headset handoff. Start subsequent work on a fresh feature branch from current
origin/main. Do not advertise unavailable FO3 VR routes or campaign completion.

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
