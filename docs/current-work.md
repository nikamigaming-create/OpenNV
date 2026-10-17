# Current work

Updated 2026-10-17. The vanilla FNV local playtest is ready for the user's Quest 3
and Air Link test. All subagents and gameplay processes are stopped. Priority
remains FNV, standalone FO3, mods, then TTW. No campaign or physical-headset
completion is claimed.

## Verified baseline

PR #207 restored the coherent runtime and consolidated the repository. PR #208
merged the playtest changes after all five CI checks passed. Its main commit is
e601a04a7c1dd7cdcedf1d0f58754926f526baf3. Local main matched origin/main with one
checkout and no remaining feature branch. Unique earlier source remains preserved.

Fresh creation, Doc's exit, saloon doors, Sunny's tutorial, actual rifle hits on
all three bottles and the next dialogue work in flat/simulator OpenXR. Earlier
Release play killed both first-well geckos, advanced to stage 50, looted corpses,
and used wrist-ray Pip-Boy fast travel. The newest accepted manual checkpoint is
stage 25, after a saloon round trip. Original owned files and user saves remain
untouched. No physical Quest 3, ED-E, campaign, FO3 VR, mod or TTW acceptance yet.

## Integrated changes and checks

Runtime now owns source music and transient explosions, objective setters,
movement values and fast travel policy, current-location/sneak Travel, directed
NPC/creature portal continuations, IDLM travel destinations and Accompany.
Cell replacement retires old active/warm/staged actor bindings before replacement
and restores retained actors on rollback. Prepared door models retain their real
animation clocks even when prefetch is cancelled. Unfinished NPC route searches
persist as pending queries with their actual pose and source identity; cold
restoration replans without inventing arrival or replaying package events.

Weapon observation shares the current held-weapon owner. Bot follow no longer
starves moving-target routes, and final reference approaches retain the original
arrival region. Source indexing/reuse, duplicate LOD texture reads, particle draw
modifier selection and native child-array enumeration were tightened without
excluding source references or particles.

The complete Test-GodotRuntime gate and git diff --check pass for this candidate.
Owned Travel/Accompany/movement checks and native actor overlap/rollback, pending
route cold state, door cancellation/cold clocks, projected arrival and particle
controller/output checks pass. The final export saved inside the saloon, loaded
the stage-25 exterior checkpoint in a fresh process and saved again at stage 30
with no opening-driver or Sunny AI error. Both test processes quit normally.

## Local handoff and remaining work

The pinned asset-free package is OpenNV-fnv-playtest-20261017-f-windows-x64,
built from clean commit 0c8b65900bb0fc8d19919fab22a204a78efc8525 (the exact tree
merged by #208). All 207 file hashes and exported launcher identity passed.
The ignored local release directory contains OpenNV Quest 3.cmd for Air Link,
OpenNV Recovery Test.cmd for flat play, and headset-handoff.json with exact paths
and hashes. Both launchers use one isolated stage-25 profile; user saves are
untouched. Quest Link runtime/profile validation passed without launching a headset
session. The user's physical test is next; root owns any subsequent repair alone.

Automatic approval review blocked deleting the superseded package and earlier
simulator/screenshot diagnostics. They remain inactive; do not bypass that rejection.

Cheyenne still cannot reach the saloon portal: its native capsule is blocked at
the required source-corridor endpoint before entering the final arrival region.
Moving the player away did not resolve this. Source Travel/Accompany selection
is admitted, but actual creature door traversal is not accepted. Fix the shared
collision/navigation owner without bypassing walls, required route prefixes or
door activation. Final-export fast-travel population and saving after travel
also remain unverified; the native actor-lifetime checks alone do not close them.

Loading is not instant. One prior cold exterior Continue measured 31.27s: restore
1.12s, world 8.70s, characters/controls 9.24s, distant world 12.20s. Warm exterior
reload measured 6.61s and saloon entry 3.25s. New performance changes need matched
measurement. Unsupported source behavior, missing SpeedTree decoding and other
reported divergences remain visible. ED-E/campaign and crafting need continued
playthrough; unloaded background-cell simulation is not complete.

Frame recording is off. Only root operates the game, builds and Git.
