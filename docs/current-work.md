# Current work

Updated 2026-10-17. Root alone integrates the vanilla FNV local playtest for Quest 3
and Air Link; all subagents are stopped at the user's request. Priority remains
FNV, standalone FO3, mods, then TTW. No campaign or physical-headset completion
is claimed.

## Verified baseline

PR #207 restored the coherent runtime and consolidated the repository. Main was
synchronized; obsolete task branches and worktrees were removed after preserving
unique source. One checkout contains the current codex/fnv-playable-release work.

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

## Remaining work

Merge checked PR #208, verify clean local main equals origin/main, and pin one
asset-free local Quest 3 launcher with the accepted stage-25 checkpoint. Root
owns the handoff; no agents or background gameplay sessions remain active.

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
