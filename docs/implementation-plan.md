# Playable OpenNV implementation plan

Updated 2026-11-03 under the user's direction. This is the sole active plan.
Deliver New Vegas through Goodsprings and Fallout 3 through Vault 101 to populated
Megaton before expanding weapon coverage, other regions, benchmarks or tools.

## Finish criteria

**New Vegas:** normal New Game; Doc Mitchell's character setup and release;
Sunny's shooting and gecko tutorials; the campfire crafting tutorial; resolution
of the first Goodsprings conflict through an offered quest branch; ordinary
travel out of town. Dialogue, objectives, acquisition, equipment, combat,
crafting and rewards must come from the winning records and runtime owners.
Optional choices remain choices. Skipping a tutorial does not meet this target.

**Fallout 3:** normal New Game; birth/character creation; Baby Steps; Growing Up
Fast; Future Imperfect; Escape; ordinary travel through Vault 101's real exit,
across the wasteland and through Megaton's entrance. Residents enabled for that
quest state must be present as functioning source actors, with appearance,
movement, dialogue, services and interactions. Check the arrival conversation
and early main-quest continuation. A teleport, scene load or actor count does
not establish arrival or a functioning population.

For each route, save ordinary progress, close the process, load and continue.
Quest stages/objectives, choices, inventory, equipment, actors and position must
persist through the existing save owner. Each campaign needs its own execution;
FNV or TTW success does not establish standalone FO3 support.

Flat and OpenXR share gameplay and saves. Input, hands and UI must permit the
same interactions; physical-headset acceptance remains separately required.
Native 4K60 flat and physical 144 Hz XR remain product targets. Repair performance
when it blocks these routes, without replacing progression with benchmarks.

## Execution order

1. Use existing evidence and the current build to find each route's first real
   blocker. Begin with ordinary New Game when old saves lack required history.
2. Trace the fault to source records and its shared owner: scripts/conditions,
   character creation, dialogue, actor procedures, navigation/collision, combat,
   inventory, crafting, portals/streaming or saving. Implement the general
   capability with focused synthetic and owned-data checks.
3. Continue the actual quest through existing ordinary-input bots/player tools.
   Add bot policies only to operate real controls. Use source objectives and
   offered menu choices; never force quest success or mutate gameplay state.
4. Keep healthy checkpoints, verify cold-load continuation and advance. Check
   both routes when a shared repair affects them. Do not replay the whole opening
   after every edit or save around a known unowned failure.
5. Complete each route and check destination residents and interactions. Use
   compact outcomes from existing tooling. Matched retail evidence is additionally
   required before any parity claim.

Use bounded sessions and close them before coding/building. Recording stays off
except a necessary specific visual check; delete temporary frames in cleanup.
New harnesses, capture projects, giant inventories and unrelated system sweeps
are not intermediate deliverables.

## Product boundaries

OpenNV reads the complete winning owned ESM/ESP and loose/BSA graph. C# owns
formats, world state, gameplay and saves; Godot owns presentation, input and
OpenXR. Keep one product path with in-process reuse. Transformed retail assets
are never persistent launch inputs.

Owned files stay read-only/private. Do not publish retail assets, saves, captures
or derivatives, import OpenMW, or paste proprietary decompiler output. Unknown
layouts and unsupported behavior fail visibly. Never hand-place residents, make
proxy actors, weaken collision, skip script calls, return invented defaults or
add location-specific success paths.

FNV, FO3, TTW, DLC and supported plugins remain the broader objective. Complete
campaigns, all systems and exact presentation remain open beyond this milestone.
Counts and plausible frames do not establish campaign or parity acceptance.

## One branch and publication

Root owns integration, builds, game input and Git. Parallel workers use separate
file ownership on the same branch; no independent implementation branches or
concurrent build/export overwrite. Inspect unique commits and preserve work
before removing leftovers.

Use one codex/ branch from current origin/main, one integrated build and separate
genuine saves for the two campaigns. Before pushing runtime or claim changes,
run the selected owned-data check and required gate:

~~~powershell
.\scripts\Test-GodotRuntime.ps1 -Godot 'D:\code\gd\Godot_v4.8-dev7_mono_win64\Godot_v4.8-dev7_mono_win64_console.exe'
git diff --check
~~~

Review the integrated diff, publish one checked PR, merge when all checks pass,
verify local main equals origin/main, and remove the merged task branch. Remove
already merged leftovers without discarding unique work. Keep current-work.md
concise: verified state, actual route blocker and next owner.
