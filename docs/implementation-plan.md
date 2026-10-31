# Playable OpenNV implementation plan

Updated 2026-10-31 under the user's direction. This is the sole active plan.
Root owns integration, builds, game input and Git. User-authorized workers
implement or review separate shared-system repairs in parallel; private drafts
keep the current build stable. Individual coding decisions and builds do not
wait for approval. Parallel work supports, and does not replace, retail
recording, Godot replay and ordinary gameplay verification.

Immediate priority is the user's live retail/Godot side-by-side gameplay request
at native 3840x2160 and 60 FPS. Do not expand test infrastructure in place of
playing and repairing the game. Required checks still precede publication.

## Objective and boundaries

Reproduce Fallout: New Vegas, Fallout 3, Tale of Two Wastelands and supported
plugins from the complete winning owned-data graph. Every object, transition,
event, voice, menu, draw and final pixel belongs to the parity denominator.
Deliver retail behavior and presentation through Goodsprings to the Strip,
then complete campaign/system coverage. Combat, all skills, quests, factions,
inventory, crafting, trade, Caravan and VATS are required.

Flat acceptance requires sustained native 3840x2160 at 60 FPS indoors and
outdoors. Physical OpenXR has a separate 144 Hz target, shared authoritative
gameplay/save state, correct hands/weapons/UI and user headset acceptance.
Desktop or simulator evidence cannot establish physical XR acceptance.

The project is greenfield: old implementations, saves and compatibility are
expendable. Replace duplicate or broken owners instead of preserving them for
their own sake. Current-build saving remains a game feature. Prefer useful
current checkpoints, but use ordinary New Game when missing historical state
cannot honestly advance; never backfill history or edit saves.

Owned retail inputs stay read-only/private. Implement first-party C# rules
from neutral evidence; do not import translated proprietary code or OpenMW.
Unknown behavior fails visibly. Never skip unsupported calls, return invented
defaults, hand-place actors, weaken collision or create named success paths.

## System-first dependency order

Identify complete systems, implement shared rules in coherent batches, execute
headless cases, integrate, then compare representative ordinary gameplay with
retail. Gameplay validates functioning systems; it is not the primary discovery
loop for one missing function at a time. Reuse runtime-capability-map.md and
local/system-map-active-20261027. Counts are declarations, not execution proof.
Do not require another giant audit before implementing known shared gaps.

1. **Data/readers:** winning plugin/resource precedence, source identities,
   layouts and dependencies across ESM/ESP/BSA/loose data; malformed inputs and
   actual source examples. In-process reuse must not become transformed assets
   as persistent launch inputs.
2. **Authoritative world/time/events/save:** references and actor lifecycle,
   enabled/deleted state, time, event order, RNG, transactions, streaming and
   current-build persistence; interruption/cancellation without replayed effects.
3. **Scripts/conditions/quests/dialogue:** shared function/context
   dispatch joining bytecode execution and world-state host. Route NPC,
   creature, dialogue and idle consumers through the same handlers; preserve
   caller/target/run-on namespaces, time, RNG, locals and event/result order.
   Implement missing function families through existing authoritative owners,
   prioritized by affected quests/packages/records. Execute actual source-used
   families headlessly. The retained inventory has 80,467 condition declarations,
   148 function IDs and 86 uses of 451; those counts do not establish support.
4. **Actor packages/animation/navigation:** shared procedure/request lifecycles,
   transitions, pose/controller ownership, locomotion, native collision, doors
   and cross-region continuation across actor families. Verify actual publication,
   physical arrival, interruption and lifecycle changes.
5. **Combat/AV/effects/inventory/crafting/trade/minigames:** equipment, weapons,
   attacks, projectiles, damage, death, VATS, inventory instances, ingestion,
   crafting, trade, factions and minigames; real input, effects and persistence
   with meaningful cross-system transaction/failure coverage.
6. **UI/audio/render/streaming:** source menus, dialogue/input lifetime, voices,
   spatial mix, materials, lighting, geometry, animation publication, LOD and
   streaming. Compare timing, sound and final pixels separately from semantics.
7. **Flat/XR/performance:** connected travel, interiors, interactions and combat;
   measured CPU/GPU/streaming/save owners; physical motion, input, hands,
   weapons, wrist UI and lifecycle against the shared gameplay state.

These are dependency batches in one product, not parallel rewrites. Keep working
paths integrated while replacing duplicate owners. FO3, TTW, DLC and plugins
remain in the objective; their acceptance follows the corresponding systems
rather than being inferred from FNV results.

## Current batch and delivery order

The user rejected the published tester build. Retail recording and Godot replay
are the primary execution loop. The current verified state and remaining capture
gaps are in current-work.md; old package-route checks do not establish playability.

1. Record substantial ordinary retail gameplay batches across source areas and
   applicable action/state permutations. Capture objects and arrangement, actor
   routines/support, scripts/quests/factions, combat and animation, voices/radio,
   Pip-Boy controls, doors/fast travel, weather/clouds/LOD and save/reload. Maintain
   the source-bound coverage queue and reacquire missing required evidence.
2. Establish comparable starting state before a matched replay. Preserve actual
   source/event/timing/UI/audio/pixel evidence separately. Unjoined diagnostics
   expose failures but cannot certify a matched run. Sampled actor fields and a
   latest-frame video are not lossless event/frame coverage.
3. Implement coherent shared owners using the dependency order above and measured
   failures. Current first repair: exterior residency from owned settings and one
   process-phase planning allowance across physics catch-up, followed by native
   query/request lifetime and actor process/support ownership. Preserve actual
   collision, gameplay clocks, source selection and fair retained work.
4. Run focused synthetic and owned-data checks, then the required full gate;
   produce an immutable build and replay/smoke it. Separate recording-free speed
   measurement from visual capture overhead. Target substantially better measured
   performance than retail on matched hardware/settings/workloads, alongside
   absolute flat/XR objectives. Do not infer performance from encoded video FPS.
5. Repair integrated failures, acquire missing evidence, then expand the next
   recording and implementation batch. A source cell count, isolated interaction
   or plausible frame never closes a scene, system or campaign.

Existing in-progress essential lifecycle work is preserved. SCDA execution,
general dynamic references, actor process tiers, radio generation/persistence,
source device animations and remaining script/function families are still real
system gaps. Do not bypass them with stub results or scenario-specific staging.

## Validation and publication

Focused synthetic/owned-data execution precedes an immutable export. Ordinary
representative gameplay then validates integration and retail behavior. Record
unsupported boundaries in the system map and repair them at the shared owner
in its batch. Do not substitute extra failure receipts or endless proof loops
for functioning behavior.

Use one active branch/build/export/profile. No concurrent build or export
overwrite during gameplay. Recording stays off except a specific visual
comparison; inspect/delete temporary frames including failure cleanup. Avoid
replaying opening/cooking/cold restore after every edit. Save meaningful healthy
progress; never save around a known unowned failure.

After normal close, before pushing runtime/claim changes, run the selected owned
audit and required gate:

~~~powershell
.\scripts\Test-GodotRuntime.ps1 -Godot 'D:\code\gd\Godot_v4.8-dev7_mono_win64\Godot_v4.8-dev7_mono_win64_console.exe'
git diff --check
~~~

The coordinator reviews the final coherent diff before publication. Use one
checked PR, attach it, merge after all hosted checks pass, verify local main
equals origin/main/no task PR remains, then start a fresh codex/ branch.
Preserve unrelated work. Report concrete behavior, code/check results or genuine
external blockers tersely. Counts and selected checks do not close campaign,
parity, headset or performance acceptance.

## Architecture research

The [nv-rs engine plan](https://github.com/slaterain/nv-rs/blob/main/docs/ENGINE_PORT_PLAN.md)
motivates whole-system work and replacing duplicate owners in place. The
[NVForever scripting design](https://github.com/LootJunkieAzza/NVForever/blob/main/docs/design/layer5.md)
separates bytecode interpretation, the world-state host and shared script/condition
function dispatch. Its [gap inventory](https://github.com/LootJunkieAzza/NVForever/blob/main/docs/outstanding.md)
prioritizes systems by affected quests. These are neutral architecture references,
not accepted behavior or code imports. OpenNV does not inherit skip/return-zero
policies for unknown semantics.
