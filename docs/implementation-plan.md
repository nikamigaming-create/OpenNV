# Playable OpenNV implementation plan

Updated 2026-11-03 under the user's direction. This is the sole active plan.
Work offline first: inspect both complete owned stacks, repair demonstrated
shared-runtime failures, and run synthetic plus owned-data checks. Do not launch
a campaign during this block. The first later integrated acceptance routes remain
New Vegas through Goodsprings and Fallout 3 through Vault 101 to populated Megaton.

## Offline proof and complete system checklist

An offline test proves its assertions for its declared inputs. A mathematical
invariant needs explicit assumptions and a complete state domain. Reading every
record, enumerating authored branches or passing a test suite cannot establish
all reachable gameplay, event interleavings, unbounded loops, native extensions,
audio, physical input or final pixels. Unknown scope remains unknown, never a
passing percentage. Do not remove an unsupported guard to make a gate green.

Use the existing DevelopmentLab `quest-graph` and `script-capability-map` commands
against each standalone installation. Keep original field accounting, inactive
branches, failed source bodies and compiled-only programs in their reports.
Use `corpus` for byte/layout failures when the selected repair requires it.
No new parallel engine, campaign simulator or generic proof framework is needed.

| System | Existing runtime owner and offline checks | Completion still requires |
| --- | --- | --- |
| Installation, DLC and overrides | RuntimeLiveContentSource, FalloutPluginStack; corpus and plugin probes | Complete winning plugin/resource graph and each supported selection |
| Scripts, events and branching | FalloutQuestScripts, FalloutReferenceScripts; quest-graph and reference-script probes | Compiled SCDA execution, all reached commands, state-dependent branches and event order |
| Quests, objectives and endings | FalloutQuestState, FalloutQuestStages; stage and campaign-goal contracts | Start/stop/fail/reward semantics, all branches, endings and continuation |
| Dialogue and speech | FalloutDialogueTopic, conditions and conversation owners; dialogue/result probes | Every eligible choice, result ordering, interruption, voice and timing |
| Character creation, levels and perks | Player actor values and advancement; SPECIAL/tag/trait/perk contracts | Independent FO3/FNV creation and all progression effects |
| Inventory, loot and equipment | FalloutPlayerInventory, reference inventories; variants/traversal/magazine contracts | Acquisition, removal, equip locks, scripts and cold state across all operations |
| Guns, melee and ammunition | Weapon handling, firing and projectiles; weapon contracts and owned assembly audits | Every weapon family, ammo type, reload, degradation, attachment and hit behavior |
| Combat, armor and effects | Actor damage and delivered-effect owners; hit/effect contracts | FO3/FNV rules, critical/sneak/resistance/DOT/limb/death and delayed/off-residency effects |
| VATS | FalloutVatsSession and action owners; AP/ammo/action contracts | Targeting, obstruction, cancellation, timing and cinematic presentation |
| Repair and crafting | Repair admission and inventory crafting; repair/recipe/UI contracts | All repair variants, recipes, quantity, skill/perk conditions and item conservation |
| Merchants and services | Barter/inventory and dialogue owners; transaction and cold-state checks | Prices, caps, loaded weapons, repair/healing services, restock and quest consequences |
| AI, creatures and companions | Actor packages, perception and combat; package/follow/sleep contracts | All procedures, schedules, companion commands, cross-cell continuity and furniture |
| World travel and physics | Reference placement, doors, streaming and navigation; graph/collision probes | Ordinary traversal, hazards, projectiles, doors, interiors/exteriors and resets |
| Housing and storage | Quest rewards, reference access, containers and sleep | Purchases/rewards, CELL ownership, upgrades, safe storage, respawn rules and beds; SetCellOwnership has no dispatcher |
| Locks, terminals and hacking | Access/lockpick/terminal owners; access/history/terminal contracts | Every eligibility branch, reward, failure and persistent access change |
| Factions, crime and reputation | Faction/reputation/karma/alarm owners; condition/mutation contracts | Hostility, theft, detection, disguise, services and quest outcomes |
| Survival, rest and travel time | FalloutFastTravel, player vitals/ingestibles and actor Sleep; travel/effect/sleep contracts | Player waiting/sleep, hardcore needs, radiation, addiction and time-dependent consequences |
| Maps, Pip-Boy, menus and controls | Shared UI/input owners; menu and input component checks | All controls, map views, messages, accessibility and ordinary player routes |
| Audio, radio and presentation | Native audio, animation, geometry, lighting and effects | Complete content, timing, mix, scene state and measured final output |
| Saves and continuation | FalloutNativeCampaignSave and atomic world restoration; save contracts | Every authoritative subsystem, pending events and fresh-process ordinary continuation |
| Minigames, DLC, TTW and plugins | Existing Caravan and shared content/runtime owners | Complete matches, campaign-specific behavior and every declared supported plugin combination |
| OpenXR and performance | Same authoritative gameplay plus XR adapters; software component checks | Physical headset input/hands/stereo, sustained frame targets and actual playability |

Rows are work domains, not a claim that their behavior is implemented. Each repair
must identify a concrete counterexample, its production owner, a regression that
fails without the repair, and owned-data evidence where available. Successful
component tests do not close a row. The complete game remains unaccepted until
its independent gameplay and matched-retail evidence lanes agree.

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

1. Use existing telemetry and complete offline source audits to find concrete
   blockers. Separate malformed/stale diagnostic source from missing executable
   behavior; never treat every audit row as a broken quest.
2. Trace the fault to source records and its shared owner: scripts/conditions,
   character creation, dialogue, actor procedures, navigation/collision, combat,
   inventory, crafting, portals/streaming or saving. Implement the general
   capability with focused synthetic and owned-data checks.
3. After the offline repair block, continue the actual quest through existing ordinary-input bots/player tools.
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
Reuse existing audits and probes. New harnesses, capture projects and unrelated
benchmark sweeps are not intermediate deliverables.

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
