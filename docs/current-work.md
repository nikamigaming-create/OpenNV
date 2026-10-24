# Current work

Updated 2026-10-24. Active milestone: ordinary flat New Vegas through Doc,
Sunny's wells, gathering, campfire cooking, access interactions and save/resume.
Follow docs/implementation-plan.md. Recording is off. Owned data and immutable
input saves remain read-only. No campaign, XR or retail parity completion.

## Baseline and ownership

Accepted main is de7dec3e3ba6386b57a3cbdd05047a8f87bd6466 (PR303).
Its local/main/origin/live equality and reviewed merge tree were verified; all
five hosted checks passed before merge. The current branch is
codex/fnv-projectile-launch-state. One coordinator owns implementation, builds,
ordinary native input, Git and docs. Writer handoffs are frozen; denied optional
writer messaging is not retried. No duplicate worker, runtime or worktree.

Smoke25 PID27772 / runner70874 normally quit through observed requests8/9,
exit0, after the successful checkpoint below. No gameplay runtime is active.
The coherent batch passed its required gate at13:05:12.3415722 UTC: Gate0/Diff0,
2526 public files, no source/added-file/protected-save drift. It is frozen for
checked PR publication. The gate protects
all immutable slots, the canonical save, original protected checkpoint and public
source hashes. Held unrelated PR242/269 are untouched.

## Four-part delivery checklist

1. **Implemented and exercised; publication pending:** source NPC launch extent
   and Euler sampling replace the empirical median conversion on the NPC firing
   path. Authored minimum/legacy spread, actor penalty/perks and signed aim-chase
   remain distinct. Strict INI Float readers supply fAimChaseLookingMult:Combat
   and fAnimationMult:General; no GMST substitution or hardcoded setting default.
   Friendly-fire geometry uses the complete absolute launch extent before any
   shot draw or ammunition consumption. Native dart flights damaged1073e8 through
   death. Player aim/camera wobble and exact RNG/visual/retail parity remain open.
2. **Incomplete:** actor Eat inventory/consumption/effects and persistent procedure
   execution. The reached candidate is world food1210c9/base0ec01d. Food decoding
   exists, but actor effect ownership and the source animation/consume boundary
   are not established. The player-only ingestible service is not reused, food
   is not silently removed and the refusal remains visible. This does not hold
   independently delivered firing, recipe, quest or continuation improvements.
3. **Connected systems; ordinary completion still open:** PR303's exterior dialogue
   region admission preserves actual floor projection and movement, and keeps
   different interiors/worlds separate. Native Melissa admission remains a
   separate acceptance lane. The existing recipe menu uses actor-independent
   inventory crafting/rollback. Source Healing Powder consumes the actual Broc
   Flower and Xander Root; its required skill is-1, so no Survival threshold is
   invented. Physical campfire/recipe/quest interaction is not yet accepted.
4. **Durable checkpoint delivered; cold/publication next:** ordinary F5 committed
   slot596e7cdc15314761b8e6e0461483f787 at12:53:01.9824112 UTC, schema61,
   map0daeb8,100HP. Error/cleanupError were null, procedure/corpse blockers empty,
   publicationFailures0. Selected finite source voices completed through the
   existing preparation owner; no audio or failure counters were discarded.

## What the current batch owns

Reference snapshots retain actual launch process values. Only a genuinely new
native process can publish the qualified initial state at live Ready. Older
absent values remain unknown; living failed combat cannot silently cold-retry.
The four reached168c25-28 snapshots retain known fresh launch values. Actor168c27
also retains its actual Eat refusal, package/event prefix, pose, clock and RNG.
That is continuation support, not eating implementation.

The corpse1073e8 retains an actor/weapon-bound pre-launch refusal and actual
handling/random prefix while its missing launch value stays null. Actual death,
source hashes, body/equipment, inventory and all independent effect/audio guards
remain required. Arbitrary combat errors are not admitted. Cold embedded muzzle
binding uses the same declared WEAP NNAM node as live binding; no first-node
choice or fabricated emitter is used. Synthetic cold/source/foreign-fault/loot
contracts pass; separate-process native acceptance is next.

Combat end retires completed successful no-door pursuit waypoints, preserves
unresolved door/failure ownership, and returns to a typed stopped package owner
without replaying selection or turning that existing refusal into a combat
failure. Ambient speech checks both creature voices before constructing resident
identities. Package-animation audio now uses the reference event ledger with its
existing stream/timing; completed partial-audio diagnostics remain source-owned.
Unfinished effects, voices and other errors still refuse or await proven finite
completion. Corpse diagnostics identify the exact sound owner.

## Evidence and next action

Connected formatter/builds passed0warnings/errors. Existing weapon, package and
corpse probes passed, including source-bound refusal cold/loot preservation.
The native weapon audit passed source Euler/zero-draw-count/signed-decay/process
roundtrip and conservative friendly extent checks. These are bounded contracts,
not gameplay or bit-parity certificates. Smoke21-25 supplied the actual shared
firing/procedure/body/audio bug batch; only smoke25's successful slot is credited
for the new durable world state. All runs ended through ordinary Quit.

Publish this gated tree through a checked PR, then ordinary separate-process Continue of596e7cdc and resume travel toward
Broc15d981, source activation, Sunny/campfire/recipe and actual quest/save results.
Continue during hosted CI without a competing local build; merge only after all
five exact-head hosted checks succeed. If cold continuation exposes a regression,
repair that related group before merging.

## Preserved earned state

Immutable preparation eb57653da03748e4b4f325ceef5d3e14 remains unchanged:
SHA25601B00032AA507A1E961192E60DFDC5EAC0F52FC7E52D2D922C2672E4E6482D9A.
It has100HP, equipped/loaded shotgun, Xander Root1 and Gecko Meat1, no Broc Flower.
New596e preserves that inventory and ordinary route progress after the encounter.
Prior unsaved Broc pickups are not durable ingredient or cooking acceptance.

Accepted earlier checkpoints retain all seven tutorial gecko deaths/source events,
Sunny alive, the settler's actual death (no rescue claim), CompleteQuest's genuine
VCG02 completion and VCG03stage10, plus the earned Xander harvest. Campfire and
ordinary earned-XP allocation remain unfinished. Original protected slot4dc878ab
hash7B366B739C51102CCF62AD2C8CEE25D878EE8FAC7032A2C00B56AE36C9655F14 and all
immutable slots under smoke-01/save.json.slots-v1/ are preserved. No named-location
shortcut, state injection, fabricated survival/quest state or recording is used.
