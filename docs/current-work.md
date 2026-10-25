# Current work

## Active batch: FNV user-play regression fixes & campfire progression

Work resumed on owner instruction ("FNV 100% go"). Active branch is
`codex/fnv-campfire-playthrough-311` based on accepted `origin/main` (`b538369e`).
The full required runtime gate (`Test-GodotRuntime.ps1`) passes cleanly with 0 warnings,
0 errors, clean git diff, and verified Godot headless audits.

### Verified fixes

1. **Actor audio transfer rollback preservation**:
   - Separated asynchronous `PrepareActorAudioTransferAsync` from synchronous
     `RetireOverlappingActors(..., out previous)` so caller `catch` blocks
     preserve rollback receipts.
   - Updated `RuntimeCoordinator.NativeOwned.cs`, `RuntimeCoordinator.PlayerMoves.cs`,
     and `NativeActorPerformanceAudit.cs` to await preparation and pass `out` receipts.

2. **Ability condition 431 (`GetHealthPercentage`)**:
   - Condition 431 is evaluated by trait Hot Blooded (`OldWorldBlues.esm:01276a`)
     via `GetHealthPercentage < 0.5`.
   - Added `HealthPercentage` property to `FalloutPlayerSkills`, bound to
     `_vitals` in `RuntimeNativeOpeningStageDriver.cs`, and handled condition function 431.
   - Eliminates 2,643 condition refusals during Hot Blooded selection at stage 102.

3. **Compatibility GLES3 instance uniform buffer exhaustion (4096 limit)**:
   - Godot Compatibility (GLES3) renderer restricts instance shader variables to a
     single UBO clamped by OpenGL `GL_MAX_UNIFORM_BLOCK_SIZE` (4096 values / 16 indices
     = 256 instances maximum per scene).
   - Changed `source_exterior_environment` in `NativeNifMaterialEnvironment.cs` to default
     to `true`, so ordinary scene geometry automatically reads published cell lighting
     without allocating instance uniform buffer slots.
   - In `RuntimeNativeCellLighting.cs`, interior cell lighting is published to global shader
     uniforms on `_EnterTree()`, removing the per-mesh instance parameter binding that
     exhausted the 256-slot limit in Doc Mitchell's house.
   - In `NativeNifMaterialEnvironment.BindExterior`, instance parameters are only updated
     if previously set, preserving zero-allocation behavior for clean meshes.

4. **Audio voice lifecycle and concurrency**:
   - Hardened `FalloutSoundVoices` with concurrency locks and proper null checks during
     disposal to prevent race conditions during cell and actor transitions.

### Next steps

1. Commit and push `codex/fnv-campfire-playthrough-311`, create PR, and merge into `main`.
2. Fast-forward local `main` to `origin/main` and verify worktree cleanliness.
3. Launch the campaign bot (`RuntimeCoordinator.Bot.Campaign.cs`) to play through
   Fallout: New Vegas autonomously (Doc Mitchell -> Goodsprings -> tutorials -> wells -> campfire).
## Implemented capability

MGEF resistance and Painless declarations now reach shared ingestion validation.
Gecko Meat applies healing and resistance-adjusted radiation through the existing
player vitals and separate actor-value pools. Creature Medicine/Survival use their
source noncombat base values, independently of combat skill. Actor resistance
uses current Endurance, source settings and admitted ability/armor/perk modifiers.

Eating queries source procedure branches with actual ancestor, used-item and
subject predicates. A valid no-file result preserves the actual base pose; a
required missing file still refuses. Declared ANIO geometry binds to its source
KF with retained source hashes and source visibility controlling the final pose. Last-idle-history conditions remain with their real owner; the current meal is not substituted for that history.
No source actor, item, seat or animation is fabricated.

## Verification and actual outcome

Debug build: zero warnings/errors. Focused tests pass independent player/actor
radiation, creature scaling, transaction rollback, timers and no-file receipt
invariants. Selected owned audit passes actual cactus, Stimpak and Gecko Meat.

Ordinary smoke37 Continue opened Pip-Boy and consumed Gecko Meat through Use:
item removed, actionError null, healing/modifier timers active, radiation0->2.7.
Publication failures remained0. A normal combat death used ordinary Load recovery.
The subsequent actor-food approach spent time in native route planning; bounded
manual movement then contacted canyon rock107109. Two obstructed segments were
stopped. Only discovered Goodsprings travel was available in the map; no travel
was injected. Requests48/49 normally quit, runner exit0.

Native actor consumption and campfire completion remain UNVERIFIED. The next
playable outcome is a supported native actor meal and the remaining well /
ingredient/campfire journey, then Primm/ED-E. Do not repeat the blocked rock segment
or infer an engine collision defect from ordinary contact. Multiple carried food
kinds, conditional ownership/resistance, world stacks, lethal/dispel, radiation
illness and Hardcore needs remain unsupported. No campaign or retail-parity claim.

## Parked operation

Owner approved the specific Sandbox current-location anchor/save-format change,
but approval review rejected it and it did not execute. Do not retry, route around
it or ask for identical approval. This meal/effect batch does not reinterpret that
anchor. Classic follow-up remains preserved at59aecc9d with no classic work active.
