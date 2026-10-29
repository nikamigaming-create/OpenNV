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
3. **Scripts/conditions/quests/dialogue — next batch:** shared function/context
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

## Current coherent batch

Checked baseline PR #331 is 1f1a44e0, with required local/owned and all five hosted
checks passed. Local main equals origin/main. Branch
codex/idle-history-bot-control adds distinct source-qualified last-request
history for function 451, shared procedure request admission and bot planning /
moving-target segment control. Focused synthetic and owned-condition checks
pass; the full required gate passes and reviewed publication remains pending.

Immutable export MVID 5b54e57b-d2f3-40b2-a0db-3481ee1aca8c Continued the useful
north-shoulder save without replaying cooking. Canonical SHA256 remains
02ABCFE0D0179E3C8B51E0F867363D13D81CB7B364C882EA0CCF8038907E277F.
VCG03 objectives 10/20/30 are complete and 40 remains open. Restoration does not
invent missing request history: the next selection correctly refused that absent
field. Normal close preserved the checkpoint; no game is running. Final queue
corrections pass the required gate; publication remains pending. Complete this batch, then
the shared script/condition batch; do not start another NPC-specific repair chain.

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
