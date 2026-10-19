# Current work

Updated 2026-10-18. The user requires the entire Fallout 1 campaign in our 3D
runtime, faithful original-game art, and three complete ordinary-input
playthroughs without unresolved problems. Qualifying playthroughs: **0 of 3**.
Root works alone and owns code, builds, input and Git. FNV/FO3 work is preserved;
package g remains rejected for physical VR use.

## Current owner

Active branch: `codex/classic-acm-audio`, based on clean synchronized main
`c2e8b2e1` after checked PR #240 merged. No task PR was open at branch creation.
The shared dispatcher and lazy-local repair are merged with passing CI.

The active work adds in-memory C# ACM decoding for owned classic audio. Private
source inspection supplies the bitstream/codebook/synthesis contracts; a private
isolated x86 execution of the unchanged original decoder supplies a PCM oracle.
The opening terminal sound and initial complete music files match every native
sample, including final zero extension where an independent decoder differs.
Synthetic codebook, history, chunked-read, invalid-input and retained-fault checks
pass. All 2,529 owned FO1 resources decode with stable PCM across read sizes
and unchanged source bytes. Full native-oracle comparison, FO2 regression and
the runtime gate remain pending. No
audio playback or terminal interaction is claimed from this decoder work.

The merged repair admits source offset-minus-one local blocks without reading
neighboring MAP variables. The existing per-instance VM storage supplies zero
on first access and retains writes; allocated blocks still read their exact
source ranges. Invalid offsets, counts and truncated arrays remain rejected.
Synthetic checks cover access, writes, instance isolation, cold state and failed
execution rollback. The owned wooden door now initializes, examines and opens;
its declared three-local block remains untouched by those source events and
survives cold continuation. The selected two-map interaction audit passes,
including 18 timed doors. The FO1 all-map audit now reports seven missing-export
failures and no local-layout failure. FO2's sweep removes two identical local
failures and now reports 210 unbound initializers. Its original binary separately
corroborates lazy zero allocation. The full runtime gate and diff check pass.
The selected FO2 interaction regression also passes its bounded checks while
continuing to expose unsupported skill/stat queries in scripted use.

The shared dispatcher implements privately verified event contracts. FO1 runs
fresh startup with the triggering action; FO2 runs startup at action zero and
then explicitly calls the requested event. Names fold ASCII, the first source
match wins, and missing/index-zero handlers use source procedure index one.
FO1 examination resolves `desc_p_proc`; FO2 resolves `description_p_proc`.
Campaign initialization, door use/examination, nested use and timed callbacks
now use this shared dispatcher. Door initialization requests action one and
finishes its startup pass before any map-entry handler. FO1 event HHMM comes
from the authoritative timer tick rather than an absent or fixed hour.

Synthetic dispatch checks pass, including campaign-specific first load, source
fallback, name resolution, shared instruction budget and rollback. All three
owned FO1 starts and FO2 startup/cold regressions pass. Eighteen owned timed-door
fixtures pass. An owned MAP with synthetic scripts confirms that an early entry
handler sees an export declared by a later startup; its 19 scripted doors pass.
These are component fixtures, not ordinary campaign progression. A separate
ordinary-input session walked eleven more steps to the opening vault door,
received its source instruction to use the entry terminal, saved and restarted
at the same tile and facing with the equipped body/pistol visible. No successful
terminal interaction was established. Test processes were stopped and all
temporary captures deleted; continuous recording stayed off.

The all-map door audit admits 72 maps/392 doors and reports seven unbound cases,
all missing exported-variable owners. The source-local layout failure is repaired.
The two previously missing-hour cases now execute. The FO2 sweep reads
155 maps/1,171 doors, with two empty elevations and 210 unbound initializers.
Its selected door regression passes bounded clock/collision checks and still
exposes unsupported item-door use. Campaign/visual acceptance remains open.

## Verified state and remaining failures

FO1 Begin executes its actual opening module initializer and source-routed entry.
Source skills, trait/tag rules, Night Person clock inputs and instance-aware item
transfers supply the premade starting inventory. Fresh ordinary-input Max starts
at the source arrival with six stacks, including 72 loose rounds. F5 and a
separate-process continuation preserve inventory and equipped pistol without
duplicate grants. Older preview saves without initialization identity remain
unchanged; initialized saves reconstruct and verify their source baseline.

Equipping the pistol no longer retires the 3D player. The selected model's real
source targets distinguish absent alternative-model channels from declared
channels without runtime owners; the latter still fail closed. Fifteen owned
body/held-item component combinations bind and advance movement. A bounded
ordinary-input check walked ten source steps in different directions, toggled
equipment, saved and cold-continued with matching position, facing, steps and
inventory. This closes the missing-player defect, not complete animation or art.

Opening acceptance still fails. The cave reports 39 missing 3D models, the flare
has no 3D model, donor clothing/likeness is not source-faithful, and combat,
non-door actors, general interactions and campaign progression remain unbound.
No complete playthrough has been verified or counted as successful. The input
harness works for classic pointer input, but its FNV-oriented state summary does
not yet expose complete classic authoritative progression. Continuous recording
stayed off; inspected temporary screenshots were deleted and owned test processes
were stopped. The Windows Computer Use helper previously failed initialization;
the existing first-party harness remains usable for bounded input checks.

Export declarations retain owning modules. Assignment requires an existing name
and permits another module to write without taking ownership. FO1 and FO2
original wrappers corroborate that rule. The isolated FO1 initializer audit is
880 passing/66 unbound: 62 absent resources, one unsupported zero-body declaration
module and three modules requiring typed reference globals. This is not full
script execution. Nested use and scheduled callbacks share transactional state;
queue order, late ticks, replacement/removal, retained faults and cold continuation
have bounded proof. Timer, door and program persistence still do not establish
native map unloading or complete campaign saves.

Source-bound furniture fitting uses referenced mesh vertices. All 138 owned
production-scale fits pass the geometry check; silhouette, materials and final
pixels remain unaccepted. Blender 5.2.2 is installed and verified, but the
previously documented furniture authoring helper is absent. Recover reproducible
first-party authoring and complete asset coverage; existing GLBs and donor paths
do not establish a 1:1 original-game appearance. The older source-only Vault 13
world audit still assumes an obsolete scene owner and must not be used as a
passing current-world check.

## Next owners and acceptance

1. Complete the shared map/controller/object script owner. Private source traces
   establish startup, entry and update ordering; campaign-specific initial load;
   source-index event fallback; first-run flags; and separate variable lifetimes.
   Departure saves dispatch map exit, save map-local arrays and the first-run bit,
   then unload programs and exports. Ordinary saves set the bit without unloading.
   Current retained door programs do not implement that lifecycle. Implement
   fixed-parameter persistence, map-update events, general object ownership,
   unload/revisit and cold-load semantics with one authoritative global/export
   store. Preserve visible failures for unsupported reached effects.
2. Connect missing map export owners, source scenery interaction, floating
   messages and sound. Native shared-array local allocation and map unload/revisit
   remain unimplemented beyond the sparse local-value projection. Extend the
   same runtime to actor behavior, dialogue, quest state, inventory transactions,
   combat/AP/AI, world travel/encounters, companions, endings and persistent world
   changes. Do not replace script execution with named-location success paths.
3. Complete faithful 3D art across all maps, objects and actors, including source
   placement, dimensions, facing, materials and animation. Keep intentional
   original 2D UI/portrait art; donor identity is not visual acceptance.
4. Finish shared flat/first-person/OpenXR input and physical acceptance. Then run
   three complete ordinary-input campaigns, retaining each run's character,
   build/source identity, progression, defects, saves/cold continuations and
   ending evidence. An unresolved problem disqualifies a run; fixes require
   renewed validation. A successful route does not excuse missing assets,
   quests or enemies outside that route.

See [classic-fallout-plan.md](classic-fallout-plan.md),
[classic-world-realization.md](classic-world-realization.md),
[classic-inventory.md](classic-inventory.md), [classic-interactions.md](classic-interactions.md)
and [classic-script-events.md](classic-script-events.md). Preserved FNV failures
remain in [status.md](status.md), [implementation-plan.md](implementation-plan.md),
[runtime-performance.md](runtime-performance.md) and [vr-rig-diagnostics.md](vr-rig-diagnostics.md).
Owned files, saves and recordings remain private and unchanged. Recording stays
off except specific visual checks; remove temporary frames after inspection.
