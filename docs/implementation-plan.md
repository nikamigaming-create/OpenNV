# Playable OpenNV implementation plan

Updated 2026-10-29 under the user's direction. This is the sole active plan.
One GPT-6.1 Sol High executor owns implementation, builds, game input and Git.
The coordinator prioritizes and reviews the final diff before publication;
individual coding decisions and builds do not wait for approval. No parallel
investigation or implementation agents are active.

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
3. **Scripts/conditions/quests/dialogue — active batch:** shared function/context
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

Checked baseline PR #333 is c6ac9376. The script-state-owners candidate implements
shared mutable FormLists and behavioral actor-value pools, used command/query
families, live consumers and current-save persistence. Named source definitions
are immutable; world state and native/Godot adapters remain separate. Focused
production-handler contracts and the selected owned GRA-prefix/player-state audit
pass. The full publication gate and diff check pass. Actual fresh-game export b2a220ad reached VCG01 stage80 through Doc speech,
Reflectron, manual traversal and Vigor acceptance, then saved successfully.
Retain the requested screenshots and reuse the fresh checkpoint for next launch.

The user now requests an outside-tester release. After checked publication:

1. Build/pin the existing experimental Windows playtest pipeline. Inspect actual
   PCK entries as well as loose/package archive contents. Ship no owned/derived
   retail assets, saves or private diagnostics. Verify notices and hashes.
2. Verify ordinary packaged launch, real scripting/quest interaction, combat and
   useful save/Continue. Reuse fresh stage80 progress; do not replay character
   creation after minor cleanup. Resolve blockers on this playable release path
   in coherent shared-system batches and label known limitations accurately.
   Retain the user-requested continuous proof video from this exact package:
   identity/launcher, Continue, finish Doc, Goodsprings/Sunny, shooting, combat,
   loot/crafting and save/quit/Continue. Use real elapsed time and game audio if
   available, exclude unrelated apps, and keep the original take. Encoded frame
   rate is not measured game FPS. A blocked route stays explicitly incomplete.
3. Publish an asset-free experimental release once package/gameplay and required
   checks pass. Complete parity, sustained native 4K60 and physical XR144 remain
   product objectives, not prerequisites or claims for this limited playtest.
4. Complete shared actor-state semantics required on that path: mutable actor-base
   flags plus essential damage, knockdown, recovery, queries and persistence.
   SetEssential targets a base identity; per-reference extension behavior is
   distinct. Registration alone cannot replace the current essential-hit refusal.
5. Advance compiled SCDA program decoding/execution through the existing shared
   host. Extent accounting and SCTX parsing are not a VM. Do not turn an unclosed
   NVSE expression into another isolated parser-patch chain. Implement remaining
   used function/context families by dependency breadth, without default returns.

General dynamic references also require authoritative allocation, stable identity,
lifetime and persistence. PluginStack currently resolves source forms, PlayerRef
is intrinsic and PlaceAtMe owns EXPL only. A VM must share the eventual world
owner rather than manufacture plugin records or privately stash dynamic members.
Do not add another streaming owner: exterior worker preparation and reads exist.
Measured CPU creature/package/capsule work remains a performance target; unrelated
GPU techniques are not evidence of a fix.

The complete system-first dependency order above remains active. Shared AV IDs
and aliases stay centralized; recognizing an ID never invents a supported pool.
Existing selected systems do not imply full campaign, FO3/TTW/plugin or XR support.

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
